module Evaluator

// ============================================================
// The evaluator: the word dispatcher and the main loop.
//
// The word handlers live in vertical slices, each owning its words and the
// helpers only it uses:
//   EvaluatorStack        swap, over, rotate, duplicate, drop
//   EvaluatorControl      quotation, if, apply, define
//   EvaluatorBookmark     bookmark, jump, copyBookmark, showBookmark
//   EvaluatorEnvironment  bind, lookup, unify, unify-verdict, resolve, term, typed, lambda-term, reduce
//   EvaluatorValueSets         [ ] literals, seal, unname, and the structural set words
//   EvaluatorSetAlgebra        model, admit, and, or, nor, is
//   EvaluatorDispatch     generic, method, and type-first method selection
// EvaluatorCommon holds the plumbing more than one slice needs. This module
// holds only the dispatcher, the main loop, and the two entry points.
//
// Railway: every word handler returns Result<EvaluationState, EvaluationError>.
// The main loop (`run`) attaches source position and returns a Diagnostic, and
// is the single decision point for what a failure means.
// ============================================================

open Types
open EvaluatorCommon
open EvaluatorScheduler
open EvaluatorChannel
open EvaluatorChannelEvent
open EvaluatorWord
open EvaluatorCondition

let locate (state: EvaluationState) (error: EvaluationError) : Diagnostic =
    {
        Location = state.Location
        Baseline = state.Baseline
        Position = state.Position
        Error = error
    }

// ============================================================
// Word dispatcher
// ============================================================

/// A bare token that names a lexical binding in the current frame chain. The
/// binding stores the interned name, so the returned store must replace the
/// caller's (D122).
let lexicalBinding (token: string) (state: EvaluationState) : (EntryIdentity * Store) option =
    match state.CurrentFrame with
    | None -> None
    | Some _ when isReservedToken token -> None
    | Some _ ->
        let (nameId, store) = Store.addText token state.Store
        EvaluatorCellSystem.lookupInFrame nameId state.CurrentFrame store
        |> Option.map (fun valueId -> (valueId, store))

let rec executeWord (token: string) (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match EvaluatorCallable.resolveControl token state with
    | Error error -> Error error
    | Ok(Some callable) ->
        EvaluatorCallable.invoke executeWord executeBuiltinWord token callable state
    | Ok None ->
        EvaluatorCallable.invoke executeWord executeBuiltinWord token (EvaluatorCallable.BuiltinCallable token) state

and executeBuiltinWord (token: string) (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match token with
    | "{" -> EvaluatorControl.quotationWord state
    | "[" -> EvaluatorValueSets.literalWord executeWord state
    | "]" -> Error UnexpectedSetClose
    | _ ->
        match System.Double.TryParse token with
        | true, number ->
            let (identity, newStore) = Store.addTensor (Tensor.scalar number) state.Store

            Ok
                { state with
                    Store = newStore
                    Stack = Stack.push identity state.Stack
                }
        | false, _ ->
            match token with
            | token when (lexicalBinding token state).IsSome ->
                let (valueId, store) = (lexicalBinding token state).Value
                Ok { state with Store = store; Stack = Stack.push valueId state.Stack }
            | "add" -> binaryOperation "add" (fun a b -> Ok(a + b)) state
            | "subtract" -> binaryOperation "subtract" (fun a b -> Ok(a - b)) state
            | "multiply" -> binaryOperation "multiply" (fun a b -> Ok(a * b)) state
            | "divide" ->
                binaryOperation "divide" (fun a b -> if b = 0.0 then Error DivisionByZero else Ok(a / b)) state
            | "lessThan" -> binaryOperation "lessThan" (fun a b -> Ok(if a < b then 1.0 else 0.0)) state
            | "greaterThan" -> binaryOperation "greaterThan" (fun a b -> Ok(if a > b then 1.0 else 0.0)) state
            | "equals" -> binaryOperation "equals" (fun a b -> Ok(if a = b then 1.0 else 0.0)) state
            | "duplicate" -> EvaluatorStack.duplicateWord state
            | "swap" -> EvaluatorStack.swapWord state
            | "drop" -> EvaluatorStack.dropWord state
            | "over" -> EvaluatorStack.overWord state
            | "rotate" -> EvaluatorStack.rotateWord state
            | "control-quotation" -> EvaluatorControl.quotationWord state
            | "control-if" -> EvaluatorControl.executeIf state
            | "control-apply" -> EvaluatorControl.executeApply state
            | "control-define" -> EvaluatorControl.executeDefine state
            | "control-logic" -> EvaluatorControl.executeLogic state
            | "control-cycle" -> EvaluatorControl.executeCycle state
            | "control" -> EvaluatorControl.controlWord state
            | "if" -> EvaluatorControl.executeIf state
            | "apply" -> EvaluatorControl.executeApply state
            | "define" -> EvaluatorControl.executeDefine state
            | "logic" -> EvaluatorControl.executeLogic state
            | "cycle" -> EvaluatorControl.executeCycle state
            | "match" -> EvaluatorPattern.matchWord executeWord state
            | "let" -> EvaluatorClosure.letWord state
            | "lambda" -> EvaluatorClosure.lambdaWord state
            | token when token = EvaluatorClosure.returnToken -> EvaluatorClosure.returnWord state
            | token when token = EvaluatorDispatch.methodReturnToken -> EvaluatorDispatch.methodReturnWord state
            | token when token = EvaluatorCondition.handlerReturnToken -> EvaluatorCondition.handlerReturnWord state
            | "class" -> EvaluatorClassSystem.classWord state
            | "slot" -> EvaluatorClassSystem.slotWord state
            | "create-instance" -> EvaluatorClassSystem.createInstanceWord state
            | "slot-value" -> EvaluatorClassSystem.slotValueWord state
            | "set-slot-value" -> EvaluatorClassSystem.setSlotValueWord executeWord state
            | "with-fields" -> EvaluatorWithFields.withFieldsWord executeWord state
            | "class-precedence" -> EvaluatorClassSystem.classPrecedenceWord state
            | "subclass-of" -> EvaluatorClassSystem.subclassOfWord state
            | "instance-of" -> EvaluatorClassSystem.instanceOfWord state
            | "metaclass" -> EvaluatorClassSystem.metaclassWord state
            | "use-metaclass" -> EvaluatorClassSystem.useMetaclassWord state
            | "cell-class" -> EvaluatorClassSystem.cellClassWord state
            | "print" ->
                match popResolved "print" state with
                | Error error -> Error error
                | Ok(tensor, remainingStack) ->
                    match scalarValue "print" tensor with
                    | Error error -> Error error
                    | Ok value ->
                        let line = value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

                        Ok
                            { state with
                                Stack = remainingStack
                                Output = state.Output @ [ line ]
                            }
            | "bookmark" -> EvaluatorBookmark.createBookmark state
            | "jump" -> EvaluatorBookmark.executeJump state
            | "copyBookmark" -> EvaluatorBookmark.copyBookmark state
            | "showBookmark" -> EvaluatorBookmark.showBookmark state
            | "history" -> EvaluatorHistory.historyWord state
            | "history-on" -> Ok { state with RecordHistory = true }
            | "history-off" -> Ok { state with RecordHistory = false }
            | "rollback" -> EvaluatorHistory.rollbackWord state
            | "own" -> EvaluatorOwnership.ownWord state
            | "borrow" -> EvaluatorOwnership.borrowWord state
            | "consume" -> EvaluatorOwnership.consumeWord state
            | "release" -> EvaluatorOwnership.releaseWord state
            | "reclaimable" -> EvaluatorOwnership.reclaimableWord state
            | "compact" -> EvaluatorOwnership.compactWord state
            | "linearity" -> EvaluatorOwnership.linearityWord state
            | "bind" -> EvaluatorEnvironment.bindWord state
            | "lookup" -> EvaluatorEnvironment.lookupWord state
            | "unify" -> EvaluatorEnvironment.unifyWord state
            | "unify-verdict" -> EvaluatorEnvironment.unifyVerdictWord state
            | "resolve" -> EvaluatorEnvironment.resolveWord state
            | "term" -> EvaluatorEnvironment.termWord state
            | "typed" -> EvaluatorEnvironment.typedWord state
            | "lambda-term" -> EvaluatorEnvironment.lambdaTermWord state
            | "reduce" -> EvaluatorEnvironment.reduceWord state
            | "generic" -> EvaluatorDispatch.genericWord state
            | "method" -> EvaluatorDispatch.methodWord state
            | "before-method" -> EvaluatorDispatch.beforeMethodWord state
            | "after-method" -> EvaluatorDispatch.afterMethodWord state
            | "around-method" -> EvaluatorDispatch.aroundMethodWord state
            | "call-next-method" -> EvaluatorDispatch.callNextMethodWord state
            | "next-method-p" -> EvaluatorDispatch.nextMethodPWord state
            | "condition" -> EvaluatorCondition.conditionWord state
            | "restart" -> EvaluatorCondition.restartWord state
            | "with-handler" -> EvaluatorCondition.withHandlerWord state
            | "signal" -> EvaluatorCondition.signalWord state
            | "invoke-restart" -> EvaluatorCondition.invokeRestartWord state
            | "value-of" -> EvaluatorDispatch.valueOfWord executeWord state
            | "word" -> EvaluatorWord.wordWord state
            | "set" -> EvaluatorSetAlgebra.setWord state
            | "add-member" -> EvaluatorSetAlgebra.addMemberWord state
            | "and" -> EvaluatorSetAlgebra.andWord state
            | "or" -> EvaluatorSetAlgebra.orWord state
            | "nor" -> EvaluatorSetAlgebra.norWord state
            | "is" -> EvaluatorSetAlgebra.isWord executeWord state
            | "spawn" -> spawnWord state
            | "yield" -> yieldWord state
            | "halt" -> haltWord state
            | "actors" -> actorsWord state
            | "resume" -> resumeWord state
            | "kill" -> killWord state
            // D65 actor model
            | "self" -> selfWord state
            | "send-to" -> sendToWord state
            | "receive-message" -> receiveMessageWord state
            | "receive-matching" -> receiveMatchingWord executeWord state
            | "link" -> linkWord state
            | "unlink" -> unlinkWord state
            | "trap-exit" -> trapExitWord state
            | "exit-signal?" -> exitSignalWord state
            | "exit-reason" -> exitReasonWord state
            // D67 monitors and the named registry
            | "monitor" -> monitorWord state
            | "demonitor" -> demonitorWord state
            | "down?" -> downWord state
            | "down-ref" -> downRefWord state
            | "down-reason" -> downReasonWord state
            | "register" -> registerWord state
            | "unregister" -> unregisterWord state
            | "whereis" -> whereisWord state
            // D63 supervision and deadlines
            | "one-for-one" -> oneForOneWord state
            | "one-for-all" -> oneForAllWord state
            | "rest-for-one" -> restForOneWord state
            | "supervise" -> superviseWord state
            // W6 Channels
            | "channel" -> channelWord state
            | "send" -> sendWord state
            | "receive" -> receiveWord state
            | "close" -> closeWord state
            // W7 Events
            | "send-event" -> sendEventWord state
            | "receive-event" -> receiveEventWord state
            | "choose" -> chooseWord state
            | "wrap" -> wrapWord state
            | "guard" -> guardWord state
            | "runtime-guard-define" -> EvaluatorRuntimeGuard.defineWord state
            | "runtime-guard-holds" -> EvaluatorRuntimeGuard.holdsWord executeWord state
            | "with-fallback" -> withFallbackWord state
            | "sync" -> syncWord executeWord state
            | "seal" -> EvaluatorValueSets.sealWord state
            | "predicate-seal" -> EvaluatorValueSets.predicateSealWord state
            | "constructor" -> EvaluatorValueSets.constructorWord state
            | "constructor-seal" -> EvaluatorValueSets.constructorSealWord state
            | "unname" -> EvaluatorValueSets.unnameWord state
            | "filter" -> EvaluatorValueSets.filterWord executeWord state
            | "set-subset" -> EvaluatorValueSets.subsetWord executeWord state
            | "set-union" -> EvaluatorValueSets.unionWord state
            | "set-intersect" -> EvaluatorValueSets.intersectWord state
            | "set-except" -> EvaluatorValueSets.exceptWord state
            | "set-member" -> EvaluatorValueSets.memberWord executeWord state
            | "same-shape" -> EvaluatorValueSets.sameShapeWord state
            | "same-name" -> EvaluatorValueSets.sameNameWord state
            | "sort-of" -> EvaluatorValueSets.sortOfWord state
            | "merge" -> EvaluatorValueSets.mergeWord state
            | "set-complement" -> EvaluatorValueSets.complementWord state
            | token when token.StartsWith(":") && token.Length > 1 ->
                let (identity, newStore) = Store.addText (token.Substring(1)) state.Store

                Ok
                    { state with
                        Store = newStore
                        Stack = Stack.push identity state.Stack
                    }
            | token when token.StartsWith("?") ->
                let (identity, newStore) = Store.addText token state.Store

                Ok
                    { state with
                        Store = newStore
                        Stack = Stack.push identity state.Stack
                    }
            | token when isTextLiteral token ->
                let (identity, newStore) = Store.addText (literalBody token) state.Store

                Ok
                    { state with
                        Store = newStore
                        Stack = Stack.push identity state.Stack
                    }
            | _ ->
                match EvaluatorCallable.resolveName token state with
                | Error error -> Error error
                | Ok None -> Error(Unresolved(UnknownKind.Word, token))
                | Ok(Some(callable, store)) ->
                    EvaluatorCallable.invoke
                        executeWord
                        executeBuiltinWord
                        token
                        callable
                        { state with Store = store }

/// The lowered path's strict call: a `CallWord` must resolve a word, not a
/// runtime lexical binding. The interpreter keeps the dynamic `let`/`define`
/// behavior; the compiled path refuses an ambiguous dynamic call.
let executeWordStrict
    (token: string)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match EvaluatorCallable.resolveControl token state with
    | Error error -> Error error
    | Ok(Some callable) ->
        EvaluatorCallable.invoke executeWord executeBuiltinWord token callable state
    | Ok None ->
        match lexicalBinding token state with
        | Some _ -> Error(DynamicCall token)
        | None ->
            EvaluatorCallable.invoke executeWord executeBuiltinWord token (EvaluatorCallable.BuiltinCallable token) state

// ============================================================
// Main loop: the single failure decision point.
// ============================================================

let rec run (state: EvaluationState) : Result<EvaluationState, Diagnostic> =
    match state.PendingError with
    | Some error -> Error(locate state error)
    | None -> runProgram state

and runProgram (state: EvaluationState) : Result<EvaluationState, Diagnostic> =
    // A link's exit signal is consumed before the actor's next token, so a
    // blocked actor cannot ignore it (D65). A state whose `SelfId` has no
    // actor record (a test's foreign-owner state) has no signal to consume.
    match state.SchedulerState.Actors.TryFind state.SchedulerState.SelfId with
    | Some current ->
        match current.PendingExit with
        | Some reason -> run (Scheduler.exitCurrent reason state)
        | None -> runProgramBody state
    | None -> runProgramBody state

and runProgramBody (state: EvaluationState) : Result<EvaluationState, Diagnostic> =
    match state.Program with
    | [] ->
        // The current actor finished. Its exit may restart it (D63); otherwise a
        // ready actor gets the CPU, and the root ending with nothing ready ends.
        let current = Scheduler.getCurrentActor state.SchedulerState

        match current.Status with
        | Terminated -> Ok state
        | _ when current.Parent.IsNone && state.SchedulerState.ReadyQueue = [] -> Ok state
        | _ ->
            let next = Scheduler.exitCurrent NormalExit state

            if next.Program = [] && next.SchedulerState.ReadyQueue = [] then
                match next.PendingError with
                | Some error -> Error(locate next error)
                | None -> Ok next
            else
                run next
    | term :: remaining ->
        if state.Deadline > 0 && state.Consumed >= state.Deadline then
            // D63: the actor's token budget for this incarnation is spent. The
            // supervisor decides what the `TimeoutExit` becomes.
            run (Scheduler.timeoutCurrent state)
        elif state.Reductions <= 0 then
            // W5: the budget is a fairness bound, not a failure. Yield to the
            // next ready actor (or continue this one with a fresh budget).
            run (Scheduler.yieldControl state)
        else
            // W8: when recording is on, every executed token appends an
            // event-stream step before it runs, so the state before the token
            // can be restored by rollback. Recording is opt-in (`history-on`).
            let recorded =
                if state.RecordHistory then
                    let (_, next) = EvaluatorHistory.record term (term :: remaining) state
                    next
                else
                    state

            let preState =
                { recorded with
                    Program = remaining
                    Reductions = recorded.Reductions - 1
                    Consumed = recorded.Consumed + 1
                    Location = recorded.Location + 1
                    Position = recorded.Position + 1
                }
            // Explicit match (not Result.bind) so `run postState` stays a tail call.
            let executed =
                match term with
                | SymbolicAbstraction.Quote terms ->
                    match EvaluatorCode.encode terms preState.Store with
                    | Ok(codeId, store) ->
                        Ok
                            { preState with
                                Store = store
                                Stack = Stack.push codeId preState.Stack
                            }
                    | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | _ -> EvaluatorProgram.executeTerm executeWord term preState

            match executed with
            | Error error ->
                match EvaluatorCondition.handleError (locate preState error) preState with
                | Some handled -> run handled
                | None ->
                    if Scheduler.isSupervised preState.SchedulerState then
                        // A supervised child's failure is an exit its supervisor
                        // consumes; an unsupervised actor aborts the run (D63).
                        run (Scheduler.exitCurrent (ErrorExit error) preState)
                    else
                        Error(locate preState error)
            | Ok postState -> run postState

// ============================================================
// Public API
// ============================================================

exception EvaluationException of Diagnostic

/// Convenience for call sites that assert success (tests, demos).
/// Real callers should handle Result, since the whole point is that failures
/// are values, not exceptions.
let runOrFail (state: EvaluationState) : EvaluationState =
    match run state with
    | Ok finalState -> finalState
    | Error diagnostic -> raise (EvaluationException diagnostic)

let initial (store: Store) (vocabularyRoot: EntryIdentity) : EvaluationState =
    let selfId = EntryIdentity.ofInt 0
    // The six set operations and the empty `set` declaration are image entries;
    // their roots are execution state and are seeded with every fresh state.
    let (setSeededStore, genericRoot, declarationRoot) =
        EvaluatorDispatch.seedSetProtocolMethods store

    let (seededStore, wordRoot) = EvaluatorControl.seedControlForms setSeededStore

    // Create initial state with dummy scheduler to break circular dependency
    let dummySched = Types.Runtime.initialSchedulerState selfId

    // The root actor gets the same private resources as any child: a capability
    // handle and a mailbox channel (D64, D65).
    let (rootHandle, storeWithHandle) =
        Store.addTensor (Tensor.vector [ ActorHandleTag ]) seededStore

    let (rootMailbox, storeWithResources, mailboxSched) =
        Scheduler.createMailbox storeWithHandle dummySched

    let (_, rootEpochState) = Epoch.beginEpoch storeWithResources.NextIndex Epoch.empty

    let initialState =
        {
            Store = storeWithResources
            Stack = Stack.empty
            VocabularyTable = vocabularyRoot
            UserWordTable = None
            BindingTable = None
            SetTable = None
            DeclarationTable = declarationRoot
            GenericFunctionTable = genericRoot
            WordTable = wordRoot
            HistoryTable = None
            ClassTable = None
            InstanceStateTable = None
            CurrentFrame = None
            CallFrames = []
            MethodFrames = []
            Handlers = []
            Ownership = { emptyOwnership with Epoch = rootEpochState }
            SchedulerState = dummySched
            Program = []
            RecordHistory = false
            Reductions = DefaultReductions
            Consumed = 0
            Deadline = 0
            Location = 0
            Baseline = 1
            Position = 1
            Output = []
            PendingError = None
            HostWords = []
            Capabilities = Set.empty
            LastCommandStatus = None
            BookBaseDirectory = None
        }
    // The root scheduler is the one canonical construction of the
    // actor/scheduler self-reference (D179 stage 1).
    let properSched =
        Scheduler.initializeRoot selfId initialState rootHandle rootMailbox mailboxSched

    { initialState with
        SchedulerState = properSched
    }

// ============================================================
// Potential extensions (not commitments)
// - Automatic currying and partial values: higher-order Prelude APIs.
// - Local let rec and mutual recursion: direct ports of F# local functions.
// - Conditions and restart handlers: recovery without host exceptions.
// - Mutable locals and loops: porting Prelude caches and counters.
// - Collection libraries: List, Array, Map, Set, Option, and Result words.
// ============================================================
