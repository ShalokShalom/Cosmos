namespace Types

open Types

[<AutoOpen>]
module Runtime =
        /// The durable epoch. Actor incarnations use 1, 2, and so on.
        type EpochId = int

        let durableEpoch: EpochId = 0

        /// One half-open allocation interval [Start, End) in the shared frontier.
        type AllocationRange =
            {
                Start: int
                End: int
            }

        /// Per-incarnation epoch bookkeeping. Execution state; never serialized.
        type EpochState =
            {
                Current: EpochId
                NextEpoch: EpochId
                /// The intervals each epoch allocated, newest first.
                Ranges: Map<EpochId, AllocationRange list>
                /// Entries reclassified as durable. A promoted entry is never reclaimed
                /// when its epoch closes.
                Promoted: Set<EntryIdentity>
                /// The frontier already attributed to the current epoch.
                LastFrontier: int
                /// Incarnations that have ended and whose entries may be reclaimed.
                Closed: Set<EpochId>
            }

        let emptyEpochState: EpochState =
            {
                Current = durableEpoch
                NextEpoch = 1
                Ranges = Map.empty
                Promoted = Set.empty
                LastFrontier = 0
                Closed = Set.empty
            }
        /// Channel metaobject tag (D20, W6): a store-resident communication endpoint.
        [<Literal>]
        let ChannelTag = -7.0

        /// Channel state metaobject tag (D27): the open flag and queued messages.
        [<Literal>]
        let ChannelStateTag = -9.0

        /// ChannelEvent-stream step (W8, D29): one executed token.
        [<Literal>]
        let HistoryStepTag = -10.0
        /// A method's role in a standard method combination (D48).
        type MethodQualifier =
            | Primary
            | Before
            | After
            | Around

        /// A method projection. The store entry is the authority.
        type Method =
            {
                Address: EntryIdentity
                Qualifier: MethodQualifier
                Arity: int
                Body: EntryIdentity
                Specializers: EntryIdentity list
            }

        /// The role of a method invocation in the combination machine (D48).
        type MethodCallKind =
            | AroundCall
            | BeforeCall
            | PrimaryCall
            | AfterCall

        /// One method invocation. `CallerProgram` and `CallerStack` are the saved
        /// continuation; `ReturnToCaller` says whether the result resumes that
        /// continuation or the surrounding core. Execution state, never serialized.
        type MethodCallFrame =
            {
                Method: Method
                Kind: MethodCallKind
                CallerProgram: SymbolicAbstraction.Term list
                CallerStack: EntryIdentity list
                Arguments: EntryIdentity list
                NextMethods: Method list
                ReturnToCaller: bool
                /// The core groups an around method runs when it exhausts its arounds.
                Core: CombinationFrame option
            }

        /// The core of a standard combination: all before methods, the primary chain,
        /// and all after methods. Execution state, never serialized.
        and CombinationFrame =
            {
                CallerProgram: SymbolicAbstraction.Term list
                CallerStack: EntryIdentity list
                Arguments: EntryIdentity list
                Befores: Method list
                Primaries: Method list
                Afters: Method list
                PrimaryStack: EntryIdentity list option
            }

        /// A frame in the method-combination machine (D48).
        type MethodFrame =
            | MethodCall of MethodCallFrame
            | CombinationCall of CombinationFrame

        /// The phase of a dynamic handler (D50).
        type HandlerPhase =
            | BodyPhase
            | HandlerPhase

        /// A dynamic handler frame. The body and handler are image quotations; the
        /// caller continuation is execution state, never serialized.
        type HandlerFrame =
            {
                Handler: EntryIdentity
                CallerProgram: SymbolicAbstraction.Term list
                CallerStack: EntryIdentity list
                Phase: HandlerPhase
            }

        /// The family of a name that was not found.
        type UnknownKind =
            | Binding
            | Model
            | Class
            | ValueSet
            | Slot
            | Generic
            | Word
            | Metaclass

        /// What an error points at: the word that raised it, or an address in the image.
        type ErrorSubject =
            | ErrorWord of string
            | ErrorAddress of int

        /// What a type mismatch expected to find.
        type ExpectedValue =
            | ExpectedQuotation
            | ExpectedText
            | ExpectedSet
            | ExpectedStep
            | ExpectedRuntimeGuard
            | ExpectedConstructor
            | ExpectedCell
            | ExpectedConditionValue
            | ExpectedRestart
            | ExpectedMethod
            | ExpectedInstance
            | ExpectedClass
            | ExpectedEvent
            | ExpectedEventList
            | ExpectedActorHandle
            | ExpectedMonitor

        /// How an arity check failed.
        type ArityFault =
            | InvalidTerm of word: string * arity: RealNumber
            | InvalidGeneric of word: string * arity: RealNumber
            | GenericMismatch of name: string * expected: int * actual: int
            | MethodMismatch of name: string * expected: int * actual: int

        /// How a channel operation failed.
        type ChannelFault =
            | ChannelNotFound
            | ChannelClosed
            | SendFailed of reason: string
            | ReceiveFailed of reason: string

        /// How a sort (value-set) operation failed.
        type SortFault =
            | Mismatch of first: string * second: string
            | NotClosed of set: string
            | Undecided of reason: string
            | SlotMismatch of slot: string

        /// How a supervision limit failed.
        type LimitFault =
            | NegativeSupervisionLimit of word: string * value: int
            | SupervisionExceeded of strategy: string * restarts: int

        /// How an enumeration-valued word argument failed.
        type EnumFault =
            | InvalidRestartType of kind: string
            | InvalidWordCategory of category: string
            | InvalidWordActive of value: string
            | InvalidConditionKind of kind: string

        /// How an actor operation failed.
        type ActorFault =
            | CannotKillSelf of actor: EntryIdentity
            | NoMailbox of actor: int
            | UnknownRegisteredName of name: string
            | NameInUse of name: string

        /// How pattern matching failed.
        type PatternFault =
            | NoClauseMatched of subject: string
            | MalformedPattern of reason: string
            | MissingPin of name: string
            | UnboundRepeated of name: string
            | PatternUndecided of reason: string
            | GuardFailed of name: string * reason: string

        /// Every way evaluation can fail.
        type EvaluationError =
            | StackUnderflow of word: string * needed: int * available: int
            | EmptyValue of word: string
            | ShapeMismatch of word: string * left: int[] * right: int[]
            | UnknownIndex of index: int
            | Unresolved of kind: UnknownKind * name: string
            | InactiveWord of form: string
            | InvalidBookmark
            | UnclosedQuotation
            | DivisionByZero
            | ReductionExhausted
            | UnclosedSet
            | UnexpectedSetClose
            | SetLiteralSchedulerWord of word: string
            | InvalidHistoryStep of index: int * count: int
            | NotOwned of address: int * actor: int
            | OwnedByAnother of address: int * owner: int
            | ValueConsumed of address: int
            | DispatchUndecided of reason: string
            | UnificationUndecided of reason: string
            | RuntimeGuardUndecided of reason: string
            | NoApplicableMethod of name: string * arguments: string list
            | AmbiguousDispatch of name: string * candidates: string list
            | EmptyEventChoice of word: string
            | EventGuardFailed of word: string
            | EventGuardBudget of word: string
            | UnmatchedReturn
            | MissingSlot of slot: string
            | UnboundSlot of slot: string
            | ClassCycle of name: string
            | InconsistentPrecedence of name: string
            | NoNextMethod
            | InvalidMethodQualifier of qualifier: string
            | NoSupervisionPolicy of word: string
            | InvalidSystemMessage of reason: string
            | ArityError of fault: ArityFault
            | TypeMismatch of subject: ErrorSubject * expected: ExpectedValue
            | ChannelError of channelId: EntryIdentity * fault: ChannelFault
            | SortError of fault: SortFault
            | LimitError of fault: LimitFault
            | EnumError of fault: EnumFault
            | ActorError of fault: ActorFault
            | PatternError of fault: PatternFault
            | CodeError of reason: string
            | HostFailure of reason: string
            | DynamicCall of token: string

        /// A stable, reflection-free rendering of an evaluation error. The AOT build
        /// cannot use `%A` on a union, so callers that report an error use this.
        let describeError (error: EvaluationError) : string =
            let number (value: RealNumber) =
                value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

            let shapeText (shape: int[]) =
                "[" + String.concat ", " (shape |> Array.map string) + "]"

            match error with
            | StackUnderflow(word, needed, available) -> $"{word}: stack underflow (needed {needed}, available {available})"
            | EmptyValue word -> $"{word}: empty value"
            | ArityError(ArityFault.InvalidTerm(word, arity)) -> $"{word}: invalid term arity {number arity}"
            | ShapeMismatch(word, left, right) -> $"{word}: shape mismatch {shapeText left} vs {shapeText right}"
            | UnknownIndex index -> $"unknown index {index}"
            | Unresolved(UnknownKind.Word, token) -> $"unknown word {token}"
            | InactiveWord form -> $"{form}: inactive word"
            | Unresolved(UnknownKind.Binding, name) -> $"unknown binding {name}"
            | Unresolved(UnknownKind.Model, name) -> $"unknown model {name}"
            | TypeMismatch(ErrorWord word, ExpectedQuotation) -> $"{word}: not a quotation"
            | TypeMismatch(ErrorWord word, ExpectedText) -> $"{word}: not text"
            | UnificationUndecided reason -> $"unification is undecided: {reason}"
            | InvalidBookmark -> "invalid bookmark"
            | UnclosedQuotation -> "unclosed quotation"
            | CodeError reason -> "code: " + reason
            | HostFailure reason -> "host: " + reason
            | DynamicCall token ->
                "a lowered word call cannot resolve a runtime lexical binding: " + token
            | DivisionByZero -> "division by zero"
            | ReductionExhausted -> "reduction budget exhausted"
            | UnclosedSet -> "unclosed set"
            | UnexpectedSetClose -> "unexpected set close"
            | SetLiteralSchedulerWord word ->
                $"set literal: the word '{word}' changes or blocks the actor; a set literal may not contain it"
            | InvalidHistoryStep(index, count) ->
                $"invalid history step {number (double index)} (the history holds {count} steps)"
            | TypeMismatch(ErrorAddress address, ExpectedStep) -> $"not an event-stream step: {number (double address)}"
            | NotOwned(address, actor) ->
                $"value {number (double address)} is not owned by actor {number (double actor)}; call own first"
            | OwnedByAnother(address, owner) -> $"value {number (double address)} is owned by actor {number (double owner)}"
            | ValueConsumed address -> $"value {number (double address)} has already been consumed"
            | SortError(SortFault.Undecided reason) -> $"sort membership is undecided: {reason}"
            | DispatchUndecided reason -> $"dispatch is undecided: {reason}"
            | TypeMismatch(ErrorAddress address, ExpectedRuntimeGuard) -> $"not a guard definition: {number (double address)}"
            | TypeMismatch(ErrorAddress address, ExpectedConstructor) -> $"not a sort constructor: {number (double address)}"
            | RuntimeGuardUndecided reason -> $"guard verdict is undecided: {reason}"
            | TypeMismatch(ErrorWord word, ExpectedSet) -> $"{word}: not a set"
            | SortError(SortFault.Mismatch(first, second)) -> $"sort mismatch: {first} vs {second}"
            | SortError(SortFault.NotClosed set) -> $"{set}: sort not closed"
            | ArityError(ArityFault.InvalidGeneric(word, arity)) -> $"{word}: invalid generic arity {number arity}"
            | Unresolved(UnknownKind.Generic, name) -> $"unknown generic {name}"
            | ArityError(ArityFault.GenericMismatch(name, expected, actual)) ->
                $"{name}: expected {expected} generic arguments, got {actual}"
            | ArityError(ArityFault.MethodMismatch(name, expected, actual)) ->
                $"{name}: expected method arity {expected}, got {actual}"
            | NoApplicableMethod(name, arguments) ->
                let joined = String.concat "; " arguments
                $"{name}: no applicable method for [{joined}]"
            | AmbiguousDispatch(name, candidates) ->
                let joined = String.concat "; " candidates
                $"{name}: ambiguous dispatch between [{joined}]"
            | ActorError(ActorFault.CannotKillSelf actor) -> $"cannot kill self: actor {EntryIdentity.value actor}"
            | ChannelError(channelId, ChannelFault.ChannelNotFound) -> $"channel not found: {EntryIdentity.value channelId}"
            | ChannelError(channelId, ChannelFault.ChannelClosed) -> $"channel closed: {EntryIdentity.value channelId}"
            | ChannelError(channelId, ChannelFault.SendFailed reason) ->
                $"channel {EntryIdentity.value channelId}: send failed: {reason}"
            | ChannelError(channelId, ChannelFault.ReceiveFailed reason) ->
                $"channel {EntryIdentity.value channelId}: receive failed: {reason}"
            | TypeMismatch(ErrorWord word, ExpectedEvent) -> $"{word}: not an event"
            | TypeMismatch(ErrorWord word, ExpectedEventList) -> $"{word}: not an event list"
            | EmptyEventChoice word -> $"{word}: empty event choice"
            | EventGuardFailed word -> $"{word}: guard did not produce a value"
            | EventGuardBudget word -> $"{word}: guard exceeded its reduction budget"
            | EnumError(EnumFault.InvalidWordCategory category) -> $"invalid word category: {category}"
            | EnumError(EnumFault.InvalidWordActive value) -> $"invalid word active value: {value}"
            | UnmatchedReturn -> "return without a call"
            | Unresolved(UnknownKind.Class, name) -> $"unknown class {name}"
            | Unresolved(UnknownKind.ValueSet, name) -> $"unknown value set {name}"
            | Unresolved(UnknownKind.Slot, name) -> $"unknown slot {name}"
            | MissingSlot slot -> $"missing slot {slot}"
            | UnboundSlot slot -> $"unbound slot {slot}"
            | TypeMismatch(ErrorAddress address, ExpectedInstance) -> $"not an instance: {number (double address)}"
            | TypeMismatch(ErrorAddress address, ExpectedClass) -> $"not a class: {number (double address)}"
            | SortError(SortFault.SlotMismatch slot) -> $"slot type mismatch: {slot}"
            | ClassCycle name -> $"class cycle at {name}"
            | InconsistentPrecedence name -> $"inconsistent class precedence at {name}"
            | NoNextMethod -> "no next method"
            | InvalidMethodQualifier qualifier -> $"invalid method qualifier: {qualifier}"
            | TypeMismatch(ErrorAddress address, ExpectedMethod) -> $"not a method: {number (double address)}"
            | Unresolved(UnknownKind.Metaclass, name) -> $"unknown metaclass {name}"
            | TypeMismatch(ErrorAddress address, ExpectedCell) -> $"not a cell: {number (double address)}"
            | EnumError(EnumFault.InvalidConditionKind kind) -> $"invalid condition kind: {kind}"
            | TypeMismatch(ErrorAddress address, ExpectedConditionValue) -> $"not a condition: {number (double address)}"
            | TypeMismatch(ErrorAddress address, ExpectedRestart) -> $"not a restart: {number (double address)}"
            | NoSupervisionPolicy word ->
                $"{word}: no supervision policy; configure one with one-for-one, one-for-all, or rest-for-one"
            | EnumError(EnumFault.InvalidRestartType kind) -> $"invalid restart type: {kind}"
            | LimitError(LimitFault.NegativeSupervisionLimit(word, value)) ->
                $"{word}: supervision limit must be non-negative, got {value}"
            | LimitError(LimitFault.SupervisionExceeded(strategy, restarts)) ->
                $"supervision limit exceeded for {strategy} after {restarts} restart(s)"
            | TypeMismatch(ErrorAddress address, ExpectedActorHandle) -> $"not an actor handle: {number (double address)}"
            | ActorError(ActorFault.NoMailbox actor) -> $"actor {number (double actor)} has no mailbox"
            | InvalidSystemMessage reason -> $"invalid system message: {reason}"
            | TypeMismatch(ErrorAddress reference, ExpectedMonitor) -> $"not a monitor reference: {reference}"
            | ActorError(ActorFault.UnknownRegisteredName name) -> $"no actor is registered as {name}"
            | ActorError(ActorFault.NameInUse name) ->
                $"the name {name} is already registered"
            | PatternError(PatternFault.NoClauseMatched subject) ->
                $"no pattern clause matched {subject}"
            | PatternError(PatternFault.MalformedPattern reason) ->
                $"malformed pattern: {reason}"
            | PatternError(PatternFault.MissingPin name) ->
                $"pattern pin {name} is not bound"
            | PatternError(PatternFault.UnboundRepeated name) ->
                $"pattern variable {name} is repeated before it is bound"
            | PatternError(PatternFault.PatternUndecided reason) ->
                $"pattern matching is undecided: {reason}"
            | PatternError(PatternFault.GuardFailed(name, reason)) ->
                $"pattern guard {name} failed: {reason}"

        /// A failure located at a point in the source, ready for the transpiler to report.
        type Diagnostic =
            {
                Location: int
                Baseline: int
                Position: int
                Error: EvaluationError
            }

        /// Why an actor stopped running (D63).
        type ExitReason =
            | NormalExit
            | ErrorExit of error: EvaluationError
            | TimeoutExit
            | ShutdownExit
            | KilledExit

        /// When a supervised child is restarted (D63). `Permanent` restarts on every
        /// exit, `Transient` on an abnormal one, and `Temporary` never restarts.
        type RestartType =
            | Permanent
            | Transient
            | Temporary

        /// How a supervisor restarts its children after a failure (D63).
        type SupervisionStrategy =
            | OneForOne
            | OneForAll
            | RestForOne

        /// A supervisor's restart budget and each child's token deadline (D63).
        /// `WithinSteps = 0` means no time window; `Deadline = 0` means no deadline.
        type SupervisionLimits =
            {
                MaxRestarts: int
                WithinSteps: int
                Deadline: int
            }

        /// A supervisor's strategy and limits (D63). Execution state, never serialized.
        type SupervisionPolicy =
            {
                Strategy: SupervisionStrategy
                Limits: SupervisionLimits
            }

        /// What a supervised child runs and how it restarts (D63).
        type ActorSpec =
            {
                Quotation: EntryIdentity
                RestartType: RestartType
            }

        /// The stable word for a supervision strategy, used in diagnostics.
        let supervisionStrategyName (strategy: SupervisionStrategy) : string =
            match strategy with
            | OneForOne -> "one-for-one"
            | OneForAll -> "one-for-all"
            | RestForOne -> "rest-for-one"

        /// The stable word for an exit reason, used in diagnostics.
        let exitReasonName (reason: ExitReason) : string =
            match reason with
            | NormalExit -> "normal"
            | ErrorExit _ -> "error"
            | TimeoutExit -> "timeout"
            | ShutdownExit -> "shutdown"
            | KilledExit -> "killed"

        /// The state of an actor in the scheduler.
        type ActorStatus =
            | Running
            | Ready
            | Halted of error: EvaluationError option
            | Terminated

        /// A channel is an store-resident communication endpoint.
        /// Channels are created as store entries and identified by their EntryIdentity.
        type Channel =
            {
                /// The unique channel identifier (address in the store)
                Identity: EntryIdentity
                /// Actor IDs waiting to receive from this channel. This is control
                /// state, like the ready queue: it says *who* is waiting, not what the
                /// channel holds. The channel's open flag and its queued messages are
                /// store-resident (D27), so they survive persistence.
                ReceiveWaiters: EntryIdentity list
            }

        /// Per-run linear ownership (W9, D30). Ownership is execution state (D1):
        /// a run owns references to store entries, and the store itself is untouched.
        /// `Owners` maps a value address to the actor that owns it; `Consumed` marks
        /// the addresses a run has consumed, so a second consume (or a borrow) is a
        /// named refusal. The state is never serialized.
        type OwnershipState =
            {
                Owners: Map<EntryIdentity, EntryIdentity>
                Consumed: Set<EntryIdentity>
                /// The actor incarnation's allocation epoch (D83). Execution state:
                /// persistence never carries it.
                Epoch: EpochState
            }

        /// A run starts owning nothing and has not opened an incarnation epoch yet.
        let emptyOwnership: OwnershipState =
            {
                Owners = Map.empty
                Consumed = Set.empty
                Epoch = emptyEpochState
            }

        /// The effect port for host words. The base attaches an adapter; the
        /// Prelude offers an unhandled token to it and keeps no OS authority.
        type HostWordAdapter =
            abstract Capability: string
            abstract TryExecute: token: string * state: EvaluationState -> Result<EvaluationState, EvaluationError> option

        and EvaluationState =
            {
                Store: Store
                Stack: EntryIdentity list
                VocabularyTable: EntryIdentity
                UserWordTable: EntryIdentity option
                BindingTable: EntryIdentity option
                SetTable: EntryIdentity option
                DeclarationTable: EntryIdentity option
                GenericFunctionTable: EntryIdentity option
                WordTable: EntryIdentity option
                /// The event-stream's history root: a vector of step addresses (W8).
                /// The steps are store data; this is the execution-state pointer,
                /// threaded like the other roots.
                HistoryTable: EntryIdentity option
                /// Cell-system class root: name -> class definition (D44).
                /// Execution state, re-threaded by a persistence reader like the other roots.
                ClassTable: EntryIdentity option
                /// Cell-system instance-state root: instance -> newest state (D44).
                /// Execution state, re-threaded by a persistence reader like the other roots.
                InstanceStateTable: EntryIdentity option
                /// The current lexical frame (D45). Execution state, never serialized;
                /// a closure's environment is image data, the current pointer is control state.
                CurrentFrame: EntryIdentity option
                /// Saved caller frames for closure calls (D45). Execution state, never
                /// serialized; the internal return token restores the previous frame.
                CallFrames: EntryIdentity option list
                /// The method-combination machine (D48). Execution state, never
                /// serialized; the chain and its continuations are control state.
                MethodFrames: MethodFrame list
                /// Dynamic condition handlers (D50). Execution state, never serialized.
                Handlers: HandlerFrame list
                /// The run's linear-ownership state (W9). Execution state, not data:
                /// persistence never carries it.
                Ownership: OwnershipState
                SchedulerState: SchedulerState
                Program: SymbolicAbstraction.Term list
                /// Whether the event stream records a step for each token. Off
                /// by default; `history-on` opts in. Execution state, never
                /// serialized.
                RecordHistory: bool
                Reductions: int
                /// Executed tokens in this actor's current incarnation (D63). A fairness
                /// yield does not reset it, and a blocked actor does not advance it.
                Consumed: int
                /// This incarnation's token deadline (D63). `0` means no deadline.
                Deadline: int
                Location: int
                Baseline: int
                Position: int
                Output: string list
                /// A failure delivered by the scheduler when the actor resumes, such as
                /// `ChannelClosed` after a blocked receive. `run` reports it as a
                /// terminal diagnostic before executing another word. Delivery clears
                /// it; manual resume preserves it so the same failure is reported again.
                PendingError: EvaluationError option
                /// The effect ports for host words (D183, D184). Execution
                /// state, never serialized; an empty list refuses every host
                /// word.
                HostWords: HostWordAdapter list
                /// The capabilities granted to host-word adapters in this run.
                /// Execution state, never serialized; empty is default-deny.
                Capabilities: Set<string>
                /// The last external command's exit status (D183). Execution state,
                /// never serialized; the script runner uses it as the exit code.
                LastCommandStatus: int option
                /// The directory a relative book path resolves against (D184).
                /// Execution state, never serialized.
                BookBaseDirectory: string option
            }

        and Actor =
            {
                Identity: EntryIdentity
                State: EvaluationState
                Status: ActorStatus
                /// A suspended actor keeps its status and state but does not run user
                /// code. System messages still reach the runtime.
                Suspended: bool
                Parent: EntryIdentity option
                Children: EntryIdentity list
                /// Set only for a `supervise`d child; `spawn` leaves it `None` (D63).
                Specification: ActorSpec option
                /// Why this actor stopped, once it has stopped (D63).
                Exit: ExitReason option
                /// The last snapshotted token count of this incarnation (D63).
                Consumed: int
                /// The private capability a program holds for this actor (D64).
                Handle: EntryIdentity option
                /// The actor's private mailbox channel (D65). The mailbox id never
                /// reaches a program; `send-to`/`receive-message` resolve it from the
                /// actor, not from a stack operand.
                Mailbox: EntryIdentity option
                /// Actors linked to this one (D65). Links are per incarnation.
                Links: EntryIdentity list
                /// An exit signal delivered by a link, consumed before the next token.
                PendingExit: ExitReason option
                /// When true, a link's abnormal exit becomes a mailbox message instead
                /// of a kill (D66).
                TrappingExits: bool
                /// The supervision policy this actor applies to its supervised children.
                Policy: SupervisionPolicy option
                /// Restarts admitted in the current window, and when the window began.
                Restarts: int
                RestartWindowStart: int
            }

        /// A message crossing an instance boundary. The message stays in the
        /// store; the host reads it and writes the transport envelope.
        and TransportRequest =
            {
                Instance: int
                Capability: int
                Message: EntryIdentity
                Sender: EntryIdentity
            }

        and SchedulerState =
            {
                /// The actor ID of the currently running actor (self-reference).
                SelfId: EntryIdentity
                /// All actors in this process's supervision tree, keyed by EntryIdentity.
                Actors: Map<EntryIdentity, Actor>
                /// Ready queue: actor IDs of actors ready to run (excluding current).
                ReadyQueue: EntryIdentity list
                /// Next actor ID to assign.
                NextActorId: int
                /// Channel registry: maps channel ID to channel state (host-side cache)
                Channels: Map<EntryIdentity, Channel>
                /// Channel root: points to store-resident vector of channel IDs
                ChannelsRoot: EntryIdentity option
                /// Sync waiters: actor ID -> event ID. A blocked sync is woken when a
                /// channel changes, then retries the event. This is control state, not
                /// cell data, so it stays host-side like the ready queue.
                EventWaiters: Map<EntryIdentity, EntryIdentity>
                /// Private actor handles: a handle entry a program received from
                /// `spawn`/`supervise` maps to the actor it names (D64). Execution
                /// state, never serialized. A handle is a capability, not the actor's
                /// address; an entry the scheduler did not register is refused.
                ActorHandles: Map<EntryIdentity, EntryIdentity>
                /// Remote capability entries issued by the host. A tensor with the
                /// remote tag is not enough; the host must register the entry, so a
                /// program cannot forge a remote capability.
                RemoteHandles: Set<EntryIdentity>
                /// Monitors by reference number (D67). A monitor fires once, on any
                /// exit of its target, and is then removed.
                Monitors: Map<int, Monitor>
                NextMonitorId: int
                /// Named registry (D67). A name maps to an actor identity; it is
                /// execution state, never serialized, and is removed on termination.
                Registry: Map<string, EntryIdentity>
                /// Selective-receive waiters by mailbox (D68). A send wakes these
                /// actors without delivering the message, so they rescan the queue.
                SelectiveWaiters: Map<EntryIdentity, EntryIdentity list>
                /// Messages `send-to` addressed to a remote handle. The language
                /// records the request; the host owns the transport and clears it.
                PendingTransport: TransportRequest list
            }

        /// A one-way observation of an actor's exit (D67).
        and Monitor =
            {
                Watcher: EntryIdentity
                Target: EntryIdentity
            }

        /// Creates an initial scheduler state for a new process.
        /// The actor's state will be initialized by the caller (Evaluator.initial).
        let initialSchedulerState (selfId: EntryIdentity) : SchedulerState =
            {
                SelfId = selfId
                Actors = Map.empty
                ReadyQueue = []
                NextActorId = 1
                Channels = Map.empty
                ChannelsRoot = None
                EventWaiters = Map.empty
                ActorHandles = Map.empty
                RemoteHandles = Set.empty
                Monitors = Map.empty
                NextMonitorId = 1
                Registry = Map.empty
                SelectiveWaiters = Map.empty
                PendingTransport = []
            }

        /// Initializes the scheduler state with the initial actor's state.
        let initializeSchedulerState (selfId: EntryIdentity) (initialState: EvaluationState) : SchedulerState =
            let empty =
                {
                    SelfId = selfId
                    Actors = Map.empty
                    ReadyQueue = []
                    NextActorId = 1
                    Channels = Map.empty
                    ChannelsRoot = None
                    EventWaiters = Map.empty
                    ActorHandles = Map.empty
                    RemoteHandles = Set.empty
                    Monitors = Map.empty
                    NextMonitorId = 1
                    Registry = Map.empty
                    SelectiveWaiters = Map.empty
                    PendingTransport = []
                }

            let rootState =
                { initialState with
                    SchedulerState = empty
                }

            let root =
                {
                    Identity = selfId
                    State = rootState
                    Status = Running
                    Suspended = false
                    Parent = None
                    Children = []
                    Specification = None
                    Exit = None
                    Consumed = 0
                    Handle = None
                    Mailbox = None
                    Links = []
                    PendingExit = None
                    TrappingExits = false
                    Policy = None
                    Restarts = 0
                    RestartWindowStart = 0
                }

            let sched =
                { empty with
                    Actors = Map.empty.Add(selfId, root)
                }

            // The stored snapshot gets a scheduler that at least contains the root, so
            // inspection (`getActorState`) never returns a state whose scheduler has
            // no actors. A fully self-referential immutable value is impossible; live
            // states are made canonical by `Evaluator.initial`.
            let snapshot =
                { rootState with
                    SchedulerState = sched
                }

            { sched with
                Actors = sched.Actors.Add(selfId, { root with State = snapshot })
            }

        /// The closed enumeration of execution roots. A root is a pointer into
        /// an append-only association vector; the current and saved frames are
        /// control state and are not roots here.
        type RootKind =
            | VocabularyRoot
            | UserWordsRoot
            | BindingsRoot
            | SetsRoot
            | DeclarationsRoot
            | GenericFunctionsRoot
            | WordsRoot
            | HistoryRoot
            | ClassesRoot
            | InstanceStatesRoot
            | ChannelsRoot

        /// Every root, in stable order. The coverage test compares this list
        /// with the union cases, so a new case cannot be added silently.
        let rootKinds: RootKind list =
            [
                VocabularyRoot
                UserWordsRoot
                BindingsRoot
                SetsRoot
                DeclarationsRoot
                GenericFunctionsRoot
                WordsRoot
                HistoryRoot
                ClassesRoot
                InstanceStatesRoot
                ChannelsRoot
            ]

        let getRoot (kind: RootKind) (state: EvaluationState) : EntryIdentity option =
            match kind with
            | VocabularyRoot -> Some state.VocabularyTable
            | UserWordsRoot -> state.UserWordTable
            | BindingsRoot -> state.BindingTable
            | SetsRoot -> state.SetTable
            | DeclarationsRoot -> state.DeclarationTable
            | GenericFunctionsRoot -> state.GenericFunctionTable
            | WordsRoot -> state.WordTable
            | HistoryRoot -> state.HistoryTable
            | ClassesRoot -> state.ClassTable
            | InstanceStatesRoot -> state.InstanceStateTable
            | ChannelsRoot -> state.SchedulerState.ChannelsRoot

        /// Reattaches one root. The vocabulary root is mandatory, so trying to
        /// remove it is a named refusal rather than a silent no-op.
        let setRoot
            (kind: RootKind)
            (root: EntryIdentity option)
            (state: EvaluationState)
            : Result<EvaluationState, string> =
            match kind with
            | VocabularyRoot ->
                match root with
                | Some identity -> Ok { state with VocabularyTable = identity }
                | None -> Error "the vocabulary root cannot be absent"
            | UserWordsRoot -> Ok { state with UserWordTable = root }
            | BindingsRoot -> Ok { state with BindingTable = root }
            | SetsRoot -> Ok { state with SetTable = root }
            | DeclarationsRoot -> Ok { state with DeclarationTable = root }
            | GenericFunctionsRoot -> Ok { state with GenericFunctionTable = root }
            | WordsRoot -> Ok { state with WordTable = root }
            | HistoryRoot -> Ok { state with HistoryTable = root }
            | ClassesRoot -> Ok { state with ClassTable = root }
            | InstanceStatesRoot -> Ok { state with InstanceStateTable = root }
            | ChannelsRoot ->
                Ok
                    { state with
                        SchedulerState = { state.SchedulerState with ChannelsRoot = root }
                    }

// ============================================================
// Potential extensions (not commitments)
// - Question profile AST: facts, rules, goals, and question plans for database questions.
// - Generated image/package schemas: one source for both portable codecs.
// - Canonical semantic-term bridge: one expression-to-term-to-lowered path.
// - Named-field constructors: record-style construction and patterns for the Prelude port.
// ============================================================
