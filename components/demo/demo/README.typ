= The refinement loop --- Scale in practice
This directory is the smallest complete circuit of Scale\'s promise: a
specification that grows when it is falsified.

```
  demo/player.scale                 (the single editable source of truth)
        │
        │  compile
        ▼
  demo/generated/PlayerSpecificationPristine.fs   (the property that is falsified)
  demo/generated/PlayerSpecificationRefined.fs          (the property that is confirmed)
  demo/generated/PlayerSpecificationPristine.smt2 (the SMT obligation)
  demo/generated/PlayerSpecificationRefined.smt2        (the SMT obligation)
        │
        │  run
        ▼
  Hedgehog finds a counterexample and shrinks it to the smallest one
        │
        │  refine
        ▼
  the refinement service proposes single-field guards, and Z3 proves
  `pre ∧ guard ∧ ¬post` unsatisfiable for the weakest one that closes the
  fault; the refinement is appended to the specification
        │
        │  recompile
        ▼
  the refined F# property holds under Hedgehog, and Z3 proves the refined
  obligation; that fault class is closed
```

Both `.fs` artifacts are compiled into the project, and the loop runs the
#strong[compiled] properties --- the emitter\'s output, not a mirror of it.

== Running it
```
dotnet run
```

Watch for the line `=== Scale -> Hedgehog -> Z3 (the refinement loop) ===`.

You will see something like this:

```
mode: exploratory
specification: Every player that is alive can jump.
emitted: demo/generated/PlayerSpecificationPristine.fs
         demo/generated/PlayerSpecificationRefined.fs
         demo/generated/PlayerSpecificationPristine.smt2
         demo/generated/PlayerSpecificationRefined.smt2
Hedgehog: falsified after 2 tests and 0 shrinks
          smallest counterexample: true, 0.0
          recheck seed: 1_14081941735641328067_10605437989747123525_
refiner: the weakest guard that closes the class is: health > 1
coverage: 9 of the specification's 21 inputs remain in scope
provenance: Z3 4.16.0.0
            witness model: alive=true, health=1
Hedgehog: the refined property holds; that bug class is closed.
Z3: initial obligation: Falsified (alive=true, health=1)
Z3: refined obligation: Proved
```

== What actually happened
The specification said #emph[every player that is alive can jump];. The
implementation in `demo-domain.fs` silently also requires the health to be
above one. Hedgehog generated inputs, found the discrepancy, and shrank it to
the smallest witness: a player who is alive with health zero.

The refinement service then read that witness and proposed single-field
guards. Since W14, closure is no longer an exhaustive scan: the service asks Z3
whether `pre ∧ guard ∧ ¬post` is unsatisfiable, and accepts the guard only when
that obligation is `Proved` and the guarded precondition is satisfiable. It
chose `health > 1`: that guard keeps nine of the specification\'s twenty-one
inputs in scope, and it is the weakest proved guard. Anything weaker (such as
`health > 0`) leaves the fault open, and anything vacuous (such as
`alive is false`, which keeps no inputs in scope) is rejected.

The chosen guard was appended to `demo/player.scale` with its provenance, the
artifacts were re-emitted, and a fresh Hedgehog run of the #strong[refined compiled
property] confirmed the fault class is closed. Z3 proves the refined SMT
obligation as well.

== Two readings, your choice
A refinement can be read two ways, and this demonstration does not choose for
you:

- #strong[A discovered requirement.] The specification was incomplete; the
  implementation knew a rule that the specification did not state. Accept the
  refinement into the specification.
- #strong[An implementation fault.] The specification is right and the implementation
  is wrong: an alive player whose health is low #emph[should] be able to jump. Fix
  the implementation instead.

Scale keeps the specification file editable for exactly this reason. The file
is the source of truth, not an oracle.

== Honest limits
Recorded here so the next reader is not misled (see decision records D7, D10,
and D11):

- #strong[The runtime check is the compiler\'s output --- not a mirror (D9/W12).] The
  demonstration runs the #emph[compiled] property modules
  (`demo/generated/PlayerSpecificationPristine.fs`, `demo/generated/PlayerSpecificationRefined.fs`),
  which the emitter produced from the document and which are compiled into the
  project. A thin harness supplies the generators and calls the compiled `: bool`
  predicate; re-emission is pinned byte-identical to the checked-in artifacts,
  so a run leaves the repository clean. (This was D7\'s first limit; D9/W12
  closed it.)
- #strong[The SMT encoding is only as faithful as the theory (D10).] `Health` is a
  `double` in F\# but integer-valued by construction, so the obligation uses
  `Int`. Widening the domain means widening the theory and naming the
  NaN/infinity caveat. Differential tests keep the encoding honest against the
  compiled predicate on the demo universe and every returned model.
- #strong[Refinement candidates are single-field guards over an enumerable universe.]
  The universe is now used to #emph[propose] thresholds and to rank coverage; it is
  no longer the soundness boundary. A larger domain needs a richer candidate
  generator, not a sampling-based closure check (D10/W14).
- #strong[The refinement is single-field, and positive in shape.] The parser
  recognizes varied sentences by vocabulary category and marker order rather
  than by hardcoded words (decision record D8), and the emitter negates the
  postcondition for an Invariant. This demonstration, however, uses a positive
  Capability sentence, and its refinement service is written for that shape;
  extending the search to invariants and transitions, and distinguishing
  quantifier polarity (\"no\" versus \"every\"), is future work.
- #strong[The specification file is reset before each run];, so the showcase is
  repeatable. In real use the service would only ever append. Fixed-spec mode
  (`Repair.Mode.FixedSpecification`, D11) already makes that append unreachable by rule;
  the demo deliberately stays exploratory.

== The files
- `player.scale` --- the specification: one sentence, plus machine-appended
  refinements, each with the counterexample that revealed it.
- `generated/PlayerSpecificationPristine.fs` --- the emitted property for the pristine
  document (the one that is falsified).
- `generated/PlayerSpecificationRefined.fs` --- the emitted property for the refined document
  (the one whose confirmation closes the fault class).
- `generated/PlayerSpecificationPristine.smt2` --- the pristine SMT obligation.
- `generated/PlayerSpecificationRefined.smt2` --- the refined SMT obligation, proved by Z3.
- `../scale-file.fs` --- reads and writes the specification document.
- `../emitter.fs` --- the one renderer of the F\# and SMT artifacts.
- `../demo-domain.fs` --- the record and implementation under test.
- `../refiner.fs` --- the refinement service.
- `../demo.fs` --- the pipeline that connects them.
