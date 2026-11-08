= Incus development environment

#strong[Status: implemented management recipes.] Scale uses the host Arch system
for the fast inner loop and one Incus Arch container for clean builds, package
work, and isolation from the host toolchain. smolvm remains the disposable
microVM boundary for untrusted work; it is not the daily default.

== Why Incus

- One Arch userland, not a second host OS.
- `pacman` and `paru` are the package managers; Scale does not implement one.
- The project is a bind-mounted workspace, so edits stay on the host.
- Containers share the host kernel; use an Incus VM or smolvm when a separate
  kernel boundary is required.
- The Incus CLI is the API. The recipes do not wrap a private daemon.

== Commands

```sh
just incus-check                 # client/server and config check
just incus-status                # instance state and devices
just incus-create                # create, start, mount, wait
just incus-bootstrap             # Arch packages, dev user, paru
just incus-verify                # check dotnet/just/paru/fd/rg and workspace
just incus-shell                 # shell as dev in /workspace
just incus-run "just docs-check" # non-interactive command in /workspace
just incus-snapshot before-upgrade
just incus-stop
just incus-delete confirm=yes    # destructive; explicit confirmation required
```

`just incus-create` and `just incus-bootstrap` are idempotent. The workspace
mount is added only when absent. The bootstrap copies the host
`/etc/pacman.d/mirrorlist` and retries package installation to tolerate a slow
mirror.

== Image and packages

The default image is `images:archlinux/current`. The bootstrap installs:

`base-devel`, `git`, `sudo`, `just`, `fd`, `ripgrep`, `bat`, `eza`, `jq`,
`fzf`, `sd`, `dotnet-sdk`, `aspnet-runtime`, `fish`, and `paru`.

`aspnet-runtime` is required by the SageFs daemon, which hosts a web dashboard;
without it `sagefs` starts and dies on a missing `Microsoft.AspNetCore.App`
framework.

`paru` is built from the `paru-bin` AUR package by a non-root `dev` user. Review
AUR recipes before trusting them; run untrusted builds in smolvm.

== Shell

The `dev` account logs in with `fish`, and `just incus-shell` opens that login
shell in `/workspace`. The bootstrap writes one `~/.config/fish/config.fish`
that adds `~/.dotnet/tools` and `~/.local/bin` to the path and sets
`DOTNET_CLI_TELEMETRY_OPTOUT` and `DOTNET_NOLOGO`, and installs a `just` fish
completion. Nothing else is generated, so there is no second set of shell
configuration to keep in step with the host.

`just incus-run` deliberately stays on `bash -lc`, because a recipe argument is
usually a bash command and a shell change would silently alter its quoting.
`just incus-verify` runs the account's login shell, so a broken fish setup fails
the verification instead of passing on bash alone.

== SageFs in the container

The bootstrap installs the pinned `SageFs 0.6.923` global tool under
`~/.dotnet/tools`, which the fish path setup already reaches. That makes
`just incus-run "just sage-ensure"` a self-contained loop inside the container.
The host daemon stays the primary loop; a container daemon is a second, isolated
one, and the two do not share a session.

== Isolation

- Incus container: trusted development, system package work, clean builds.
- Incus VM: whole-kernel boundary when needed.
- smolvm: disposable untrusted/AUR/release work and portable `.smolmachine`
  snapshots.
- The host remains the trusted control plane. Never run an unsigned smolvm
  release for untrusted workloads.

== Environment

```sh
SCALE_INCUS_INSTANCE=scale-dev
SCALE_INCUS_IMAGE=images:archlinux/current
SCALE_INCUS_CPUS=4
SCALE_INCUS_MEMORY=8GiB
SCALE_INCUS_DISK=40GiB
SCALE_INCUS_USER=dev
```

The recipes use the host `$PWD` as the workspace source. Keep the checkout on
the host; do not copy it into the container.

== Caveats

- Arch is rolling: pin a package snapshot or archive date for reproducibility.
- `paru` and AUR build scripts execute code; use smolvm for untrusted packages.
- File-watch propagation through the mount depends on Incus and the host
  filesystem; the host remains the primary SageFs loop.
- Incus containers share the host kernel and are not a hostile-tenant boundary.
