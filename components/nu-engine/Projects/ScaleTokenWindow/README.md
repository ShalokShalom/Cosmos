# Scale token window

A small Nu ImSim window for entering a Codeberg token. The token field is
password-masked and the token is written to the desktop keyring through
`secret-tool`; it is not written to Git config or the repository.

Run it from the full checkout:

```sh
dotnet run --project components/nu-engine/Projects/ScaleTokenWindow
```

Then store the token:

```sh
dotnet fsi tools/codeberg-token.fsx push
```

The window requires Nu's SDL3/Vulkan runtime and a graphical session.
