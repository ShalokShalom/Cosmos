// Repository-wide identifier renames.
//
// Purpose: keep the naming campaign in one place, so a later rename is a mapping
// entry instead of a hunt through the tree. Run with --check to verify that the
// old names are gone, or with --write to apply the current mapping.
//
// Naming philosophy:
//   * A name says what the thing is or does, not how it is implemented.
//   * Prefer one clear word over a metaphor: Store, not Image or Heap; ValueSet,
//     not Sort; ValueTag, not ClassTag; ChannelEvents, not CML.
//   * Avoid ordinary words that already mean something else in another language
//     or product: sort, class, heap, model.
//   * Keep names short enough to say aloud, but spell abbreviations out unless
//     the abbreviation is an established technology name (SMT, HTTP, JSON, SDK,
//     MCP, LSP).
//   * Prefer words a speech-to-text engine already knows: ordinary English
//     words, no invented names or rare jargon. Expand an acronym on first use.
//   * User-facing vocabulary is the authority; code names follow it.
//
// Earlier campaigns already applied (kept here as a record):
//   Image/Heap -> Store; Sort -> ValueSet; FieldSort -> FieldType;
//   Model -> SolverAssignment; ClassTag/class-of/TypeVerdict ->
//   ValueTag/value-of/ValueVerdict; Event -> ChannelEvent; Guard ->
//   RefinementGuard/RuntimeGuard; Step -> HistoryStep; Vivid -> SetAlgebra;
//   Lace -> ScalarRead; Bag -> VocabularyList; Term -> UnificationTerm;
//   Closure -> ProvenClosure; Probe -> QuotationRun; model/admit ->
//   set/add-member; withNack -> with-fallback; Reifier -> Refiner;
//   snoc -> appendElement; BAG -> Vocabulary. Lia is now the mascot, so it is
//   deliberately not renamed; Scale is the product name.
//
// Usage:
//   dotnet fsi tools/rename-names.fsx --write
//   dotnet fsi tools/rename-names.fsx --check

open System
open System.IO
open System.Text.RegularExpressions

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let mode =
    match fsi.CommandLineArgs |> Array.skip 1 with
    | [| "--write" |] -> "write"
    | [| "--check" |] -> "check"
    | _ -> failwith "usage: dotnet fsi tools/rename-names.fsx --write|--check"

let files =
    Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
    |> Array.filter (fun path ->
        not (
            path.Contains "/.git/"
            || path.Contains "/obj/"
            || path.Contains "/bin/"
            || path.Contains "/artifacts/"
            || path.Contains "/.SageFs/"
            || path.EndsWith "tools/rename-names.fsx"
        ))
    |> Array.filter (fun path ->
        let extension = Path.GetExtension path

        List.contains extension [ ".fs"; ".fsx"; ".fsproj"; ".typ"; ".md"; ".scale"; ".smt2"; ".txt" ]
        || Path.GetFileName path = "justfile")

// Order matters: the longest and most specific names first.
let mapping =
    [
        // `counts` is the test-count recipe; line counts are separate.
        @"\bline-test-count-check\b", "line-counts-check"
        @"\bjust counts-check\b", "just test-count-check"
        @"\bjust counts\b", "just test-count"
        @"^counts-check:$", "test-count-check:"
        @"^counts:$", "test-count:"

        // The object system is the cell system. Proper names (CLOS), JSON
        // objects, F#/.NET objects, and mathematical objects keep their names.
        @"\bCommon Lisp Object System clone\b", "CLOS clone"
        @"\bCommon Lisp Object System\b", "CLOS"
        @"\bEvaluatorObjectSystem\b", "EvaluatorCellSystem"
        @"\bObjectSystem\b", "CellSystem"
        @"\bobjectSystem\b", "cellSystem"
        @"\bobject-system\b", "cell-system"
        @"\bObject-system\b", "Cell-system"
        @"\bObject System\b", "Cell System"
        @"\bobject system\b", "cell system"
        @"\bObject system\b", "Cell system"
        @"\bobject-semantics\b", "cell-semantics"
        @"\bobject semantics\b", "cell semantics"
        @"\bobject-behavior\b", "cell-behavior"
        @"\bobject behavior\b", "cell behavior"
        @"\bobject-class\b", "cell-class"
        @"\bObject-class\b", "Cell-class"
        @"\bobjectClassWord\b", "cellClassWord"
        @"\bobjectClass\b", "cellClass"
        @"\bobjectInitialState\b", "cellInitialState"
        @"\bNotAnObject\b", "NotACell"
        @"\bnot an object\b", "not a cell"
        @"\bobject model\b", "cell model"
        @"\bobject query\b", "cell query"
        @"\bobject layer\b", "cell layer"
        @"\bobject primitives\b", "cell primitives"
        @"\bobject identity\b", "cell identity"
        @"\bobject value domain\b", "cell value domain"
        @"\bobject values\b", "cell values"
        @"\bobject value\b", "cell value"
        @"\bobject data\b", "cell data"
        @"\bobject entry\b", "cell entry"
        @"\bobject graph\b", "cell graph"
        @"\bobject class\b", "cell class"
        @"\bobject classes\b", "cell classes"
        @"\bobject instance\b", "cell instance"
        @"\bobject instances\b", "cell instances"
        @"\bobject declaration\b", "cell declaration"
        @"\bobject definitions\b", "cell definitions"
        @"\bobject references\b", "cell references"
        @"\bobject reference\b", "cell reference"
        @"\bobject root\b", "cell root"
        @"\bobject roots\b", "cell roots"
        @"\bobject state\b", "cell state"
        @"\bobject widens\b", "cell widens"
        @"\bobject yields\b", "cell yields"
        @"\bobject grows\b", "cell grows"
        @"\bobject lifespan\b", "cell lifespan"
        @"\blong-lived object\b", "long-lived cell"
        @"\bobjects are communicating cells\b", "cells communicate"
        @"\bimage-resident code objects\b", "image-resident code cells"
        @"\bfirst-class code object\b", "first-class code cell"
        @"\bcode objects\b", "code cells"
        @"\bcode object\b", "code cell"
        @"\bclasses are objects\b", "classes are cells"
        @"\bhost object graph\b", "host cell graph"
        @"\bhost objects\b", "host cells"
        @"\btheir objects live\b", "their cells live"
        @"\bdoes not use objects\b", "does not use cells"
        @"\bUntagged universal object\b", "Untagged universal cell"
        @"\bprotocol object\b", "protocol cell"
        @"\bnominal object\b", "nominal cell"
        @"\bObject / Host\b", "Cell / Host"
        @"\bobject([A-Z]\w*)", "cell$1"
        @"\ban cell-system\b", "a cell-system"
        @"\btypical class-based cell system\b", "typical class-based system"
        @"\bnot host cells\b", "not host objects"
        @"\bonly the encoding \(D44\) and closures \(D45\) have landed, while slots, instances, precedence, method combination, and metaclasses are deferred\.",
        "the encoding (D44), closures (D45), classes/slots/instances (D46), precedence/dispatch (D47), method combination (D48), and metaclasses (D49) have landed; conditions and restarts (D50) are deferred."
        @"\bClosures landed as D45; method combination remains\.",
        "Closures landed as D45; method combination landed as D48."
        @"\bD43--D47\b", "D43--D49"
        @"\bD44--D47\b", "D44--D49"
        @"\bD43--D49\b", "D43--D50"
        @"\bD44--D49\b", "D44--D50"
        @"\bA--F done; G deferred\b", "A--G done"
        @"\bA--F landed, G deferred\b", "A--G landed"
        @"\bThe prepared layer above the metaobject substrate\b", "The layer above the metaobject substrate"
        @"\bScale's prepared cell system\b", "Scale's cell system"
        @"\bThe remaining behavior is deferred;\b", "All stages are landed;"
        @"\bprepared, encoding/closures/classes/dispatch landed\b", "A--G landed"
        @"\bplanned deterministic precedence\b", "C3 deterministic precedence"
        @"\bplan multiple inheritance\b", "support multiple inheritance"
        @";\s*behavior deferred\.", "; landed."
        @"\bconditions and restarts \(D50\) remain deferred\b", "conditions and restarts (D50) landed"
        @"\bConditions and restarts \(D50\) remain deferred\b", "Conditions and restarts (D50) landed"
        @"\bThe next useful slice is the condition layer\.", "The full cell system is landed."
        @"\bconditions/restarts D50 deferred\b", "conditions/restarts D50 landed"
        @"\bconditions and restarts are deferred\b", "conditions and restarts landed"
        @"\bcombination and metaclasses are deferred\b", "all stages landed"
        @"\bthe rest is deferred\b", "all stages landed"
        @"\bthe remaining behavior is deferred\b", "conditions and restarts (D50) remain deferred"
        @"\bits remaining behavior is deferred\b", "conditions and restarts (D50) remain deferred"
        @"\bremaining behavior is deferred\b", "conditions and restarts (D50) remain deferred"
        @"\bMethod combination and metaclass behavior remain deferred\.",
        "Method combination landed as D48 and metaclasses as D49; conditions and restarts (D50) remain deferred."
        @"\bcombination and metaclasses are deferred\b", "conditions and restarts are deferred"
        @"\bthe rest is deferred\b", "conditions and restarts are deferred"
        @"\bupdated for D47\b", "updated for D49"
        @"\bD1--D47\b", "D1--D49"
        @"\[Partial \(D44 encoding, D45 closures, D46 classes/slots/instances, D47 precedence/dispatch; behavior deferred\)\]",
        "[Partial (D44 encoding, D45 closures, D46 classes/slots/instances, D47 precedence/dispatch, D48 method combination, D49 metaclasses; conditions/restarts D50 deferred)]"
        @"\[D16, D17, D27, D31, D32, D43, D44, D45, D46, D47\]",
        "[D16, D17, D27, D31, D32, D43, D44, D45, D46, D47, D48, D49]"
        @"\bthe same cell yields more\b", "the same value yields more"
        @"\bthe cell widens\b", "the value widens"
        @"\bcell widens past both\b", "value widens past both"
        @"\[Object\]", "[Cell]"

        // The concurrency pattern is ours: first-class synchronous channel events.
        @"\bCML first-class events\b", "ChannelEvents"
        @"\bCML events\b", "ChannelEvents"
        @"\bCML\b", "ChannelEvents"

        // Identity is a full word; Id is not a separate concept. Bare `id` is
        // left alone: it is the F# identity function, not our identifier.
        @"\bEntryId\b", "EntryIdentity"
        @"\bId\b", "Identity"
        @"\bids\b", "identities"

        // The product is Scale; the old Lia signature was a duplicate name.
        @"Generated by Scale \(Scale\)", "Generated by Scale"
        @"Generated by Scale \(Lia\)", "Generated by Scale"
        @"\bLia\.Generated", "Scale.Generated"

        // A negative acknowledgement is a fallback event; spell the concept out.
        @"\bwithNackWord\b", "withFallbackWord"
        @"\bwithNackEventId\b", "withFallbackEventId"
        @"\bwithNackEvent\b", "withFallbackEvent"
        @"\bwithNackState\b", "withFallbackState"
        @"\bwithNackWins\b", "withFallbackWins"
        @"\bwithNack\b", "with-fallback"
        @"Nack([A-Z]\w*)", "Fallback$1"
        @"nack([A-Z]\w*)", "fallback$1"
        @"\bNack\b", "Fallback"
        @"\bnack\b", "fallback"

        // Reification is rare jargon; the module refines a specification.
        @"\bReifier\b", "Refiner"
        @"\breifier\b", "refiner"
        @"\bReification\b", "Refinement"
        @"\breification\b", "refinement"
        @"\bReify\b", "Refine"
        @"\breify\b", "refine"
        @"\breifies\b", "refines"
        @"\breified\b", "refined"

        // Snoc is cons reversed; say what the operation does.
        @"\bsnoc\b", "appendElement"

        // The stale metaphor BAG and the old F# label.
        @"\bBAG\b", "Vocabulary"
        @"F# name", "Implementation name"

        // FixedSpec is an abbreviation; spell it out in code.
        @"\bFixedSpecRun\b", "FixedSpecificationRun"
        @"\breportFixedSpec\b", "reportFixedSpecification"
        @"\bFixedSpec\b", "FixedSpecification"
        @"\bcurrentSpecHash\b", "currentSpecificationHash"
    ]

let mutable changedFiles = 0
let mutable replacementCount = 0

for file in files do
    let original = File.ReadAllText file
    let mutable historical = false

    let text =
        original.Split('\n')
        |> Array.map (fun line ->
            if line.Contains "rename-check: historical-start" then
                historical <- true
                line
            elif line.Contains "rename-check: historical-end" then
                historical <- false
                line
            elif historical then
                line
            else
                let mutable current = line

                for (pattern, replacement) in mapping do
                    let regex = Regex(pattern)
                    let count = regex.Matches(current).Count

                    if count > 0 then
                        replacementCount <- replacementCount + count
                        current <- regex.Replace(current, replacement)

                current)
        |> String.concat "\n"

    if text <> original then
        changedFiles <- changedFiles + 1

        if mode = "write" then
            File.WriteAllText(file, text)

        printfn "%s %s" (if mode = "write" then "updated" else "stale  ") (Path.GetRelativePath(root, file))

printfn "%s: %d replacement(s) across %d file(s)" mode replacementCount changedFiles

if mode = "check" && changedFiles > 0 then
    exit 1
