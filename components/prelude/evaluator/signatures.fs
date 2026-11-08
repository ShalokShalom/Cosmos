module PreludeSignatures

open TypeRetrieval

let numberType: Type = Primitive "number"
let booleanType: Type = Primitive "boolean"
let unitType: Type = Primitive "unit"
let textType: Type = Primitive "text"
let actorType: Type = Primitive "actor"
let channelType: Type = Primitive "channel"
let conditionType: Type = Primitive "condition"
let restartType: Type = Primitive "restart"
let quotationType: Type = Primitive "quotation"
let codeType: Type = Primitive "code"
let setType: Type = Primitive "set"
let bookmarkType: Type = Primitive "bookmark"

let private signature
    (parameters: Type list)
    (result: Type)
    (effects: EffectSet)
    : WordSignature =
    {
        Quantified = []
        QuantifiedEffects = []
        QuantifiedRows = []
        Parameters = parameters
        Result = result
        Effects = effects
    }

let private effect label = effectSet label
let private actorEffect = effect "actor"
let private channelEffect = effect "channel"
let private conditionEffect = effect "condition"
let private consoleEffect = effect "console"
let private setEffect = effect "set"
let private stateEffect = effect "state"

let private binaryNumberSignature: WordSignature =
    { Quantified = []
      QuantifiedEffects = []
      QuantifiedRows = []
      Parameters = [ numberType; numberType ]
      Result = numberType
      Effects = emptyEffects }

let private binaryComparisonSignature: WordSignature =
    { Quantified = []
      QuantifiedEffects = []
      QuantifiedRows = []
      Parameters = [ numberType; numberType ]
      Result = booleanType
      Effects = emptyEffects }

/// How the P4 expression elaborator treats one built-in word. An `Expression`
/// entry carries the word's callable signature; `NotInferred` says the word is
/// executed by the dispatcher but is not an expression form (a stack word, a
/// control word, an actor word, and so on).
type WordClassification =
    | Expression of WordSignature list
    | Stack of StackSignature
    | NotInferred of reason: string

and StackSignature =
    {
        Quantified: TypeVariable list
        Pops: Type list
        Pushes: Type list
        Effects: EffectSet
    }

let private notInferred =
    NotInferred "not an expression form; the dispatcher executes it directly"

let private stack
    (quantified: TypeVariable list)
    (pops: Type list)
    (pushes: Type list)
    (effects: EffectSet)
    : WordClassification =
    Stack
        {
            Quantified = quantified
            Pops = pops
            Pushes = pushes
            Effects = effects
        }

/// The single word index for P4 coverage. Every explicit word case in
/// `Evaluator.executeBuiltinWord` appears here exactly once, and
/// `tools/consistency-check.fsx` enforces that equality. Adding a built-in
/// word without classifying it fails the gate; `primitiveTable` is derived from
/// this list rather than maintained beside it.
let classification: (string * WordClassification) list =
    [
        ("[", notInferred)
        ("]", notInferred)
        ("actors", notInferred)
        ("add", Expression [ binaryNumberSignature ])
        ("add-member", notInferred)
        ("after-method", notInferred)
        ("and", notInferred)
        ("apply", notInferred)
        ("around-method", notInferred)
        ("before-method", notInferred)
        ("bind", notInferred)
        ("bookmark", Expression [ signature [] bookmarkType stateEffect ])
        ("borrow", notInferred)
        ("call-next-method", notInferred)
        ("cell-class", notInferred)
        ("channel", Expression [ signature [] channelType channelEffect ])
        ("choose", Expression [ signature [ channelType ] textType channelEffect ])
        ("class", notInferred)
        ("class-precedence", notInferred)
        ("close", Expression [ signature [ channelType ] unitType channelEffect ])
        ("compact", notInferred)
        ("condition", Expression [ signature [ textType; textType; textType ] conditionType conditionEffect ])
        ("constructor", notInferred)
        ("constructor-seal", notInferred)
        ("consume", notInferred)
        ("control", notInferred)
        ("control-apply", notInferred)
        ("control-cycle", notInferred)
        ("control-define", notInferred)
        ("control-if", notInferred)
        ("control-logic", notInferred)
        ("control-quotation", notInferred)
        ("copyBookmark", notInferred)
        ("create-instance", notInferred)
        ("cycle", notInferred)
        ("define", notInferred)
        ("demonitor", Expression [ signature [ textType ] unitType actorEffect ])
        ("divide", Expression [ binaryNumberSignature ])
        ("down-reason", Expression [ signature [] textType actorEffect ])
        ("down-ref", Expression [ signature [] textType actorEffect ])
        ("down?", Expression [ signature [] booleanType actorEffect ])
        ("drop", stack [ 0 ] [ Variable 0 ] [] emptyEffects)
        ("duplicate", stack [ 0 ] [ Variable 0 ] [ Variable 0; Variable 0 ] emptyEffects)
        ("equals", Expression [ binaryComparisonSignature ])
        ("exit-reason", Expression [ signature [] textType actorEffect ])
        ("exit-signal?", Expression [ signature [] booleanType actorEffect ])
        ("filter", notInferred)
        ("generic", notInferred)
        ("greaterThan", Expression [ binaryComparisonSignature ])
        ("guard", notInferred)
        ("halt", notInferred)
        ("history", notInferred)
        ("history-off", notInferred)
        ("history-on", notInferred)
        ("if", notInferred)
        ("instance-of", notInferred)
        ("invoke-restart", Expression [ signature [ restartType ] unitType conditionEffect ])
        ("is", notInferred)
        ("jump", notInferred)
        ("kill", notInferred)
        ("lambda", notInferred)
        ("lambda-term", notInferred)
        ("lessThan", Expression [ binaryComparisonSignature ])
        ("let", notInferred)
        ("linearity", notInferred)
        ("link", Expression [ signature [ actorType ] unitType actorEffect ])
        ("logic", notInferred)
        ("lookup", notInferred)
        ("match", notInferred)
        ("merge", notInferred)
        ("metaclass", notInferred)
        ("method", notInferred)
        ("monitor", Expression [ signature [ actorType ] textType actorEffect ])
        ("multiply", Expression [ binaryNumberSignature ])
        ("next-method-p", notInferred)
        ("nor", notInferred)
        ("one-for-all", notInferred)
        ("one-for-one", notInferred)
        ("or", notInferred)
        ("over", stack [ 0; 1 ] [ Variable 0; Variable 1 ] [ Variable 1; Variable 0; Variable 1 ] emptyEffects)
        ("own", notInferred)
        ("predicate-seal", notInferred)
        ("print", Expression [ signature [ numberType ] unitType consoleEffect ])
        ("receive", Expression [ signature [ channelType ] textType channelEffect ])
        ("receive-event", Expression [ signature [ channelType ] textType channelEffect ])
        ("receive-matching", notInferred)
        ("receive-message", notInferred)
        ("reclaimable", notInferred)
        ("reduce", notInferred)
        ("register", Expression [ signature [ textType; actorType ] unitType actorEffect ])
        ("release", notInferred)
        ("resolve", notInferred)
        ("rest-for-one", notInferred)
        ("restart", Expression [
            {
                Quantified = [ 0 ]
                QuantifiedEffects = [ LabelSets.SetVariable 0 ]
                QuantifiedRows = []
                Parameters = [ textType; Code(Variable 0, LabelSets.Open(Set.empty, LabelSets.SetVariable 0)) ]
                Result = restartType
                Effects = conditionEffect
            }
        ])
        ("resume", notInferred)
        ("rollback", notInferred)
        ("rotate", stack [ 0; 1; 2 ] [ Variable 0; Variable 1; Variable 2 ] [ Variable 1; Variable 2; Variable 0 ] emptyEffects)
        ("runtime-guard-define", notInferred)
        ("runtime-guard-holds", notInferred)
        ("same-name", notInferred)
        ("same-shape", notInferred)
        ("seal", notInferred)
        ("self", Expression [ signature [] actorType actorEffect ])
        ("send", Expression [ signature [ channelType; textType ] unitType channelEffect ])
        ("send-event", Expression [ signature [ channelType; textType ] unitType channelEffect ])
        ("send-to", Expression [ signature [ actorType; textType ] unitType actorEffect ])
        ("set", notInferred)
        ("set-complement", notInferred)
        ("set-except", notInferred)
        ("set-intersect", notInferred)
        ("set-member", notInferred)
        ("set-slot-value", notInferred)
        ("set-subset", notInferred)
        ("set-union", notInferred)
        ("showBookmark", notInferred)
        ("signal", Expression [ signature [ conditionType ] unitType conditionEffect ])
        ("slot", notInferred)
        ("slot-value", notInferred)
        ("sort-of", notInferred)
        ("spawn", Expression [
            {
                Quantified = [ 0 ]
                QuantifiedEffects = [ LabelSets.SetVariable 0 ]
                QuantifiedRows = []
                Parameters = [ Code(Variable 0, LabelSets.Open(Set.empty, LabelSets.SetVariable 0)) ]
                Result = actorType
                Effects = actorEffect
            }
        ])
        ("subclass-of", notInferred)
        ("subtract", Expression [ binaryNumberSignature ])
        ("supervise", Expression [
            {
                Quantified = [ 0 ]
                QuantifiedEffects = [ LabelSets.SetVariable 0 ]
                QuantifiedRows = []
                Parameters =
                    [
                        Code(Variable 0, LabelSets.Open(Set.empty, LabelSets.SetVariable 0))
                        Code(Variable 0, LabelSets.Open(Set.empty, LabelSets.SetVariable 0))
                    ]
                Result = actorType
                Effects = actorEffect
            }
        ])
        ("swap", stack [ 0; 1 ] [ Variable 0; Variable 1 ] [ Variable 0; Variable 1 ] emptyEffects)
        ("sync", notInferred)
        ("term", notInferred)
        ("trap-exit", Expression [ signature [ booleanType ] unitType actorEffect ])
        ("typed", notInferred)
        ("unify", notInferred)
        ("unify-verdict", notInferred)
        ("unlink", Expression [ signature [ actorType ] unitType actorEffect ])
        ("unname", notInferred)
        ("unregister", Expression [ signature [ textType ] unitType actorEffect ])
        ("use-metaclass", notInferred)
        ("value-of", notInferred)
        ("whereis", Expression [ signature [ textType ] actorType actorEffect ])
        ("with-fallback", notInferred)
        ("with-fields", notInferred)
        ("with-handler", notInferred)
        ("word", notInferred)
        ("wrap", Expression [
            {
                Quantified = [ 0 ]
                QuantifiedEffects = [ LabelSets.SetVariable 0 ]
                QuantifiedRows = []
                Parameters = [ textType; Code(Variable 0, LabelSets.Open(Set.empty, LabelSets.SetVariable 0)) ]
                Result = Code(Variable 0, LabelSets.Open(Set.empty, LabelSets.SetVariable 0))
                Effects = channelEffect
            }
        ])
        ("yield", notInferred)
        ("{", notInferred)
    ]

/// The callable subset consumed by the expression elaborator. It is derived
/// from the classification list so the two cannot disagree.
let primitiveTable: SignatureTable =
    classification
    |> List.choose (fun (name, classification) ->
        match classification with
        | Expression signatures -> Some(name, signatures)
        | Stack _
        | NotInferred _ -> None)
    |> Map.ofList
