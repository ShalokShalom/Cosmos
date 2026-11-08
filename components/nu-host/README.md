# Scale Nu host

The bridge between the Nu engine and the Scale kernel. The engine's source
skeleton is the external node `components/nu-engine`; prebuilt dependencies and
binary assets are fetched separately. This component is the adapter that will
load the kernel, wire the callbacks, and swap Prelude images in the game loop.

`Scale.NuHost.fsproj`; the current module is a skeleton. The next integration
step is a Nu project reference and a minimal game loop that loads an image and
swaps it under NativeAOT.
