module NuHost

/// The Nu engine is the external node `components/nu-engine`. This module is
/// the host seam: the engine will load the kernel through the adapter, own the
/// callback table, and swap Prelude images in the game loop. It is a skeleton
/// until the Nu project reference and the AOT game smoke test land.
let engineNode = "components/nu-engine"

let describe () =
    "Nu host skeleton: the engine is vendored in components/nu-engine; the image/ABI bridge is the next integration."

// ============================================================
// Potential extensions (not commitments)
// - Real engine integration: reference Nu and own the kernel callback table.
// - NativeAOT game smoke: publish and run one game loop from the host.
// - Input, render, and asset words: expose host services as capabilities.
// - Remote node connection: drive a Nu process over the actor transport.
// - Editor integration: inspect images, units, and hot-swap state live.
// ============================================================
