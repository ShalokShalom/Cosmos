# scale

The standalone toolchain project. It publishes `bases/scale-cli` as a
NativeAOT product binary for the release RIDs; the binary runs the showcase and
the product modes. The test suite is the separate JIT-only
`projects/scale-tests` artifact (D176).

The source base is `bases/scale-cli/scale.fsproj`; this directory is the
Polylith project composition and publish target, not a second implementation.
`just publish`, `just aot-test`, and `just release` publish it.
