# scale-cli

The language product base: the CLI and the showcase. It owns the reference
Prelude sources and the demo; the tests live in the separate `projects/scale-tests`
deployable (D176).

The base is an Exe today; `projects/scale` is the AOT publish target. The AOT
artifact is the product and has no test modes. `--tests`, `--all-tests`, and
`--granular-tests` are refused by name; run `projects/scale-tests` for the
suite.
