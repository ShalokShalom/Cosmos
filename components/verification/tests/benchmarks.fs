module Benchmarks

// Stage 0 of the performance plan. This is a JIT harness owned by the test
// deployable; the product base refuses --benchmark. It pins a few representative
// workloads and records wall time and allocations. The committed snapshot is
// docs/performance-baseline.typ.

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open Types
open LoweredInstructions

type private Measurement =
    {
        Name: string
        Iterations: int
        Milliseconds: float
        AllocatedBytes: int64
    }

let private measure (name: string) (iterations: int) (action: unit -> unit) : Measurement =
    // Warm the code paths before the measured run. The action builds its own
    // state, so repeating it does not carry the measured run's mutations.
    action ()
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()

    let before = GC.GetAllocatedBytesForCurrentThread()
    let stopwatch = Stopwatch.StartNew()
    action ()
    stopwatch.Stop()
    let allocated = GC.GetAllocatedBytesForCurrentThread() - before

    {
        Name = name
        Iterations = iterations
        Milliseconds = stopwatch.Elapsed.TotalMilliseconds
        AllocatedBytes = allocated
    }

let private demoState () : EvaluationState =
    let (vocabularyRoot, store0) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    let store = Store.setVocabularyTable vocabularyRoot store0
    Evaluator.initial store vocabularyRoot

let private kernelArithmetic (iterations: int) : Measurement =
    let instructions =
        let mutable reversed = []

        for _ in 1 .. iterations do
            reversed <- Drop :: Add :: LiteralNumber 2.0 :: LiteralNumber 1.0 :: reversed

        List.rev reversed

    let state = KernelState.create () Store.empty [] []
    let executeWord _ _ = Error "unexpected word in the kernel arithmetic benchmark"

    measure "kernel arithmetic" iterations (fun () ->
        match LoweredAbstraction.run executeWord instructions state with
        | Ok _ -> ()
        | Error error -> failwith (string error))

let private interpreterArithmetic (iterations: int) : Measurement =
    let state = demoState ()
    let program = EvaluatorProgram.ofTokensOrFail [ "1"; "2"; "add"; "drop" ]

    measure "interpreter arithmetic" iterations (fun () ->
        for _ in 1 .. iterations do
            match Evaluator.run { state with Program = program } with
            | Ok _ -> ()
            | Error diagnostic -> failwith (describeError diagnostic.Error))

let private proseBookCast (iterations: int) : Measurement =
    let path =
        Path.Combine(
            Path.GetTempPath(),
            "scale-benchmark-" + Guid.NewGuid().ToString("N") + ".book"
        )

    File.WriteAllText(path, "logic double x = multiply(x 2).\nlogic quad x = double(x).")

    let adapters: HostWordAdapter list = [ BookHost.wordAdapter ]
    let baseDirectory = Some(Path.GetDirectoryName path)
    let text = "cast(\"" + path + "\" \"quad\" 21)."

    let measurement =
        measure "prose book cast" iterations (fun () ->
            match ProseShell.runTextWith adapters baseDirectory text with
            | ProseShell.ProseCompleted _ -> ()
            | ProseShell.ProseRefused(reason, _) -> failwith reason)

    File.Delete path
    measurement

let private textIntern (iterations: int) : Measurement =
    let text = String.replicate 128 "abcdefgh"

    measure "text intern/read" iterations (fun () ->
        let mutable store = Store.empty

        for _ in 1 .. iterations do
            let (identity, next) = Store.addText text store
            store <- next

            match Store.readTextAt identity store with
            | Some _ -> ()
            | None -> failwith "text was not interned")

let run () : unit =
    printfn "=== Benchmark baseline (JIT) ==="
    printfn "Machine: %d logical processor(s), %s" Environment.ProcessorCount (RuntimeInformation.OSDescription)
    printfn "Runtime: %s" (RuntimeInformation.FrameworkDescription)
    printfn ""
    printfn "%-24s %10s %14s %16s" "workload" "iterations" "wall ms" "allocated bytes"

    for measurement in
        [
            kernelArithmetic 50_000
            interpreterArithmetic 5_000
            proseBookCast 200
            textIntern 50_000
        ] do
        printfn
            "%-24s %10d %14.3f %16d"
            measurement.Name
            measurement.Iterations
            measurement.Milliseconds
            measurement.AllocatedBytes
