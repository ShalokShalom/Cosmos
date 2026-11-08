module ActorHost

open System
open System.IO
open System.Net.Sockets
open Types
open Wire

/// A host-side failure. The wire and the actor keep their own named reasons;
/// this union only adds the host edge.
type HostError =
    | WireFailure of WireError
    | ActorFailure of EvaluationError
    | ActorDiagnostic of Diagnostic
    | IoFailure of string
    | Unreachable of path: string
    | MessageFailure of string

let describeHostError (error: HostError) : string =
    match error with
    | WireFailure wire -> Wire.describeError wire
    | ActorFailure actor -> "actor: " + Runtime.describeError actor
    | ActorDiagnostic diagnostic -> "actor: " + Runtime.describeError diagnostic.Error
    | IoFailure reason -> "io: " + reason
    | Unreachable path -> "unreachable: " + path
    | MessageFailure reason -> "message: " + reason

/// One running instance with one actor mailbox and a capability table. The host
/// is the effect edge and owns the mutable current state; the evaluator remains
/// pure.
type Host =
    {
        mutable State: EvaluationState
        Mailbox: EntryIdentity
        mutable Capabilities: Map<uint64, EntryIdentity>
        mutable NextCapability: uint64
    }

let private deleteSocket (path: string) : unit =
    try
        File.Delete path
    with _ ->
        ()

/// Creates a host around the root actor. Capability 0 names the root.
let create () : Result<Host, HostError> =
    let (vocabularyRoot, store) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    let store = Store.setVocabularyTable vocabularyRoot store
    let state = Evaluator.initial store vocabularyRoot
    let root = Scheduler.getCurrentActor state.SchedulerState

    match root.Mailbox with
    | Some mailbox ->
        Ok
            {
                State = state
                Mailbox = mailbox
                Capabilities = Map.ofList [ 0UL, state.SchedulerState.SelfId ]
                NextCapability = 1UL
            }
    | None -> Error(MessageFailure "the root actor has no mailbox")

let private addCapability (host: Host) (actorId: EntryIdentity) : uint64 =
    let capability = host.NextCapability
    host.Capabilities <- host.Capabilities.Add(capability, actorId)
    host.NextCapability <- capability + 1UL
    capability

let private exitReasonName (code: int) : string =
    match code with
    | 0 -> "normal"
    | 1 -> "error"
    | 2 -> "timeout"
    | 3 -> "shutdown"
    | 4 -> "killed"
    | _ -> "unknown"

/// Reads the first Down message from the root mailbox, if the worker exited.
let private downReason (host: Host) : int option =
    let _, queued = ChannelStore.readChannelState host.Mailbox host.State.Store

    queued
    |> List.tryPick (fun identity ->
        match Store.lookupTensor identity host.State.Store with
        | Some tensor when Tensor.size tensor >= 3 && Tensor.item tensor 0 = DownTag ->
            Some(int (Tensor.item tensor 2))
        | _ -> None)

/// Delivers a numeric message to the actor named by its capability, runs the
/// actor, and returns the result and any exit reason.
let receiveOn (host: Host) (capability: uint64) (value: double) : Result<double * int option, HostError> =
    match host.Capabilities.TryFind capability with
    | None -> Error(MessageFailure $"unknown capability {capability}")
    | Some actorId ->
        match host.State.SchedulerState.Actors.TryFind actorId with
        | None -> Error(MessageFailure $"actor for capability {capability} is gone")
        | Some actor when actor.Status = Terminated ->
            Error(MessageFailure $"capability {capability} is retired")
        | Some actor ->
            match actor.Mailbox with
            | None -> Error(MessageFailure $"actor for capability {capability} has no mailbox")
            | Some mailbox ->
                let (messageId, store) = Store.addTensor (Tensor.scalar value) host.State.Store

                match Scheduler.deliverMessage mailbox messageId store host.State.SchedulerState with
                | Error error -> Error(ActorFailure error)
                | Ok(store, scheduler) ->
                    match Scheduler.getActorState actorId scheduler with
                    | None -> Error(MessageFailure $"actor for capability {capability} has no state")
                    | Some actorState ->
                        let runState =
                            {
                                actorState with
                                    Store = store
                                    SchedulerState = { scheduler with SelfId = actorId }
                            }

                        match Evaluator.run runState with
                        | Error diagnostic -> Error(ActorDiagnostic diagnostic)
                        | Ok final ->
                            let result =
                                match Stack.popResolved final.Store final.Stack with
                                | Some(tensor, _) ->
                                    match ScalarRead.readScalar tensor with
                                    | Ok number -> Ok number
                                    | Error reason -> Error(MessageFailure reason)
                                | None -> Error(MessageFailure "the actor left no result")

                            let rootId = host.State.SchedulerState.SelfId

                            host.State <-
                                {
                                    host.State with
                                        Store = final.Store
                                        SchedulerState = { final.SchedulerState with SelfId = rootId }
                                }

                            result |> Result.map (fun number -> number, downReason host)

/// Delivers a numeric message to the root actor, runs receive/add, and returns
/// the result. This is the S1 path.
let receiveNumber (host: Host) (value: double) : Result<double, HostError> =
    let (messageId, store) = Store.addTensor (Tensor.scalar value) host.State.Store

    match Scheduler.deliverMessage host.Mailbox messageId store host.State.SchedulerState with
    | Error error -> Error(ActorFailure error)
    | Ok(store, scheduler) ->
        let state =
            {
                host.State with
                    Store = store
                    SchedulerState = scheduler
                    Program = EvaluatorProgram.ofTokensOrFail [ "receive-message"; "1"; "add" ]
                    Stack = []
                    Output = []
            }

        match Evaluator.run state with
        | Error diagnostic -> Error(ActorDiagnostic diagnostic)
        | Ok final ->
            host.State <- final

            match Stack.popResolved final.Store final.Stack with
            | Some(tensor, _) ->
                match ScalarRead.readScalar tensor with
                | Ok result -> Ok result
                | Error reason -> Error(MessageFailure reason)
            | None -> Error(MessageFailure "the actor left no result")

/// Creates a worker actor, monitors it from the root, and returns its
/// capability and monitor reference.
let private spawnWorker (host: Host) : Result<uint64 * int, HostError> =
    let quotationTerms =
        match EvaluatorProgram.ofText "receive-message 2 multiply" with
        | Ok terms -> terms
        | Error error -> failwith (EvaluatorProgram.describeError error)

    let (quotationId, store) =
        match EvaluatorCode.encode quotationTerms host.State.Store with
        | Ok value -> value
        | Error error -> failwith (EvaluatorCode.describeError error)

    let state = { host.State with Store = store }

    match Scheduler.spawnActor quotationId state with
    | Error error -> Error(ActorFailure error)
    | Ok(handle, spawned) ->
        match Scheduler.resolveActorHandle handle spawned.SchedulerState with
        | None -> Error(MessageFailure "the worker handle did not resolve")
        | Some workerId ->
            let rootId = spawned.SchedulerState.SelfId
            let reference, scheduler = Scheduler.monitorActor rootId workerId spawned.SchedulerState
            host.State <- { spawned with SchedulerState = scheduler }
            Ok(addCapability host workerId, reference)

/// Binds a Unix socket, accepts one connection, receives one numeric envelope,
/// runs the root actor, and writes the numeric reply. This is the S1 listener.
let serveOnce (path: string) (onReady: unit -> unit) : Result<double, HostError> =
    let mutable bound = false
    deleteSocket path

    try
        try
            match create () with
            | Error error -> Error error
            | Ok host ->
                use listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
                listener.Bind(UnixDomainSocketEndPoint path)
                listener.Listen 1
                bound <- true
                onReady ()

                use connection = listener.Accept()
                use stream = new NetworkStream(connection, true)

                match Wire.read stream with
                | Error error -> Error(WireFailure error)
                | Ok envelope when envelope.Operation = Send ->
                    match envelope.Body with
                    | Number value ->
                        match receiveNumber host value with
                        | Error error -> Error error
                        | Ok reply ->
                            let response =
                                {
                                    MessageId = envelope.MessageId + 1UL
                                    Target = envelope.Sender
                                    Sender = envelope.Target
                                    Reply = 0UL
                                    Operation = Reply
                                    Body = Number reply
                                }

                            match Wire.write stream response with
                            | Error error -> Error(WireFailure error)
                            | Ok() -> Ok reply
                    | Text _ -> Error(MessageFailure "the S1 actor accepts numeric messages only")
                | Ok _ -> Error(MessageFailure "the S1 listener expects a send")
        with ex ->
            Error(IoFailure ex.Message)
    finally
        if bound then
            deleteSocket path

/// Binds a Unix socket, creates a worker, accepts a monitor request, then a
/// numeric send, runs the worker, replies, and sends the worker's exit when it
/// terminates. This is the S2 listener.
let serveSupervised (path: string) (onReady: unit -> unit) : Result<double * string * int, HostError> =
    let mutable bound = false
    deleteSocket path

    try
        try
            match create () with
            | Error error -> Error error
            | Ok host ->
                match spawnWorker host with
                | Error error -> Error error
                | Ok(capability, monitorReference) ->
                    use listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
                    listener.Bind(UnixDomainSocketEndPoint path)
                    listener.Listen 1
                    bound <- true
                    onReady ()

                    use connection = listener.Accept()
                    use stream = new NetworkStream(connection, true)
                    stream.ReadTimeout <- 5000

                    match Wire.read stream with
                    | Error error -> Error(WireFailure error)
                    | Ok monitor when monitor.Operation = Monitor && monitor.Target = capability ->
                        let acknowledgement =
                            {
                                MessageId = monitor.MessageId + 1UL
                                Target = monitor.Sender
                                Sender = monitor.Target
                                Reply = 0UL
                                Operation = MonitorAck
                                Body = Number(double monitorReference)
                            }

                        match Wire.write stream acknowledgement with
                        | Error error -> Error(WireFailure error)
                        | Ok() ->
                            match Wire.read stream with
                            | Error error -> Error(WireFailure error)
                            | Ok send when send.Operation = Send && send.Target = capability ->
                                match send.Body with
                                | Number value ->
                                    match receiveOn host capability value with
                                    | Error error -> Error error
                                    | Ok(result, down) ->
                                        let reply =
                                            {
                                                MessageId = send.MessageId + 1UL
                                                Target = send.Sender
                                                Sender = send.Target
                                                Reply = 0UL
                                                Operation = Reply
                                                Body = Number result
                                            }

                                        match Wire.write stream reply with
                                        | Error error -> Error(WireFailure error)
                                        | Ok() ->
                                            match down with
                                            | Some code ->
                                                let reason = exitReasonName code

                                                let downMessage =
                                                    {
                                                        MessageId = send.MessageId + 2UL
                                                        Target = send.Sender
                                                        Sender = send.Target
                                                        Reply = 0UL
                                                        Operation = Down
                                                        Body = Text("down " + reason)
                                                    }

                                                match Wire.write stream downMessage with
                                                | Error error -> Error(WireFailure error)
                                                | Ok() -> Ok(result, "down " + reason, monitorReference)
                                            | None -> Ok(result, "alive", monitorReference)
                                | Text _ -> Error(MessageFailure "the supervised worker accepts numbers only")
                            | Ok _ -> Error(MessageFailure "expected a send after the monitor")
                    | Ok _ -> Error(MessageFailure "expected a monitor request")
        with ex ->
            Error(IoFailure ex.Message)
    finally
        if bound then
            deleteSocket path

/// Connects to an S1 listener, sends one numeric message, and reads the reply.
let connect (path: string) (value: double) : Result<double, HostError> =
    try
        use socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        socket.Connect(UnixDomainSocketEndPoint path)
        use stream = new NetworkStream(socket, true)

        let request =
            {
                MessageId = 1UL
                Target = 0UL
                Sender = 0UL
                Reply = 0UL
                Operation = Send
                Body = Number value
            }

        match Wire.write stream request with
        | Error error -> Error(WireFailure error)
        | Ok() ->
            match Wire.read stream with
            | Error error -> Error(WireFailure error)
            | Ok response ->
                match response.Operation, response.Body with
                | Reply, Number reply -> Ok reply
                | _ -> Error(MessageFailure "the S1 listener did not reply")
    with
    | :? SocketException -> Error(Unreachable path)
    | ex -> Error(IoFailure ex.Message)

/// Connects to an S2 listener, monitors a capability, sends one number, reads
/// the reply, and reads the worker's exit.
let connectSupervisedWith
    (path: string)
    (capability: uint64)
    (value: double)
    : Result<double * string * int, HostError> =
    try
        use socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        socket.Connect(UnixDomainSocketEndPoint path)
        use stream = new NetworkStream(socket, true)
        stream.ReadTimeout <- 5000

        let monitor =
            {
                MessageId = 1UL
                Target = capability
                Sender = 0UL
                Reply = 0UL
                Operation = Monitor
                Body = Number 0.0
            }

        match Wire.write stream monitor with
        | Error error -> Error(WireFailure error)
        | Ok() ->
            match Wire.read stream with
            | Error error -> Error(WireFailure error)
            | Ok acknowledgement ->
                match acknowledgement.Operation, acknowledgement.Body with
                | MonitorAck, Number reference ->
                    let send =
                        {
                            MessageId = 2UL
                            Target = capability
                            Sender = 0UL
                            Reply = 0UL
                            Operation = Send
                            Body = Number value
                        }

                    match Wire.write stream send with
                    | Error error -> Error(WireFailure error)
                    | Ok() ->
                        match Wire.read stream with
                        | Error error -> Error(WireFailure error)
                        | Ok reply ->
                            match reply.Operation, reply.Body with
                            | Reply, Number result ->
                                match Wire.read stream with
                                | Error error -> Error(WireFailure error)
                                | Ok down ->
                                    match down.Operation, down.Body with
                                    | Down, Text reason -> Ok(result, reason, int reference)
                                    | _ -> Error(MessageFailure "the worker did not report an exit")
                            | _ -> Error(MessageFailure "the supervised worker did not reply")
                | _ -> Error(MessageFailure "the listener did not acknowledge the monitor")
    with
    | :? SocketException -> Error(Unreachable path)
    | ex -> Error(IoFailure ex.Message)

/// The compatibility form: capability 1 is the worker created by the S2
/// listener.
let connectSupervised (path: string) (value: double) : Result<double * string * int, HostError> =
    connectSupervisedWith path 1UL value

// ============================================================
// Potential extensions (not commitments)
// - Full remote send and selective receive: distributed actor workloads.
// - Resource leases with expiry and revocation: long-lived host resources.
// - Protocol negotiation: mixed-version hosts and staged upgrades.
// - Streaming transport: messages larger than one bounded frame.
// ============================================================
