# scale-tests

The JIT-only test deployable. It references `components/verification/tests` and exposes the
suite modes: `--tests`, `--all-tests`, `--granular-tests`, and
`--granular-count`.

The product deployable is `bases/scale-cli/scale.fsproj`; it does not reference
the development library. The test runner is not NativeAOT-compatible and is not
published as a native artifact. `just test`, `just test-all`, and
`just test-granular` target this project.
