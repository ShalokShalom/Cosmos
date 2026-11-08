set shell := ["bash", "-uc"]

# Keep the toolchain directories on PATH for every recipe.
export PATH := "/usr/share/dotnet:" + env_var("HOME") + "/.dotnet/tools:" + env_var("PATH")
export DOTNET_NOLOGO := "1"
export DOTNET_CLI_TELEMETRY_OPTOUT := "1"

# The runtime identifiers the release recipes accept. A RID reaches the
# filesystem through the output path, so the list is closed and every recipe
# that writes or deletes an output directory checks it first: an unvalidated
# identifier could otherwise name a directory outside the repository.
allowed_rids := "linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64"

incus_instance := env_var_or_default("SCALE_INCUS_INSTANCE", "scale-dev")
incus_image := env_var_or_default("SCALE_INCUS_IMAGE", "images:archlinux/current")
incus_cpus := env_var_or_default("SCALE_INCUS_CPUS", "4")
incus_memory := env_var_or_default("SCALE_INCUS_MEMORY", "8GiB")
incus_disk := env_var_or_default("SCALE_INCUS_DISK", "40GiB")
incus_user := env_var_or_default("SCALE_INCUS_USER", "dev")
incus_workspace := "/workspace"

# Checks that a runtime identifier is on the allowlist and is a plain name.
check_rid rid:
    #!/usr/bin/env bash
    set -euo pipefail
    case " {{allowed_rids}} " in
        *" {{rid}} "*) ;;
        *)
            echo "Unsupported RID: '{{rid}}'. Allowed: {{allowed_rids}}" >&2
            exit 1
            ;;
    esac
    case "{{rid}}" in
        */*|*..*)
            echo "Refusing a RID that is not a plain name: '{{rid}}'" >&2
            exit 1
            ;;
    esac

# List recipes.
default:
    @just --list

# Check the toolchain, the project, and the SageFs daemon.
doctor:
    @echo "dotnet     $(dotnet --version)"
    @echo "fantomas   $(dotnet fantomas --version | tail -1)"
    @echo "sagefs     $(sagefs --version 2>/dev/null | tail -1 || echo 'not installed')"
    @echo "vestige    $(vestige-mcp --version 2>/dev/null | tail -1 || echo 'not installed')"
    @echo "just       $(just --version)"
    @echo "opencode   $(opencode --version 2>/dev/null || echo 'not installed')"
    @echo "serena     $(serena --version 2>/dev/null | head -1 || echo 'not installed')"
    @echo "fsautocomplete $(fsautocomplete --version 2>/dev/null | head -1 || echo 'not installed')"
    @echo "lsp        run 'just lsp-check'"
    @echo "project    $(ls bases/scale-cli/*.fsproj projects/scale-tests/*.fsproj)"
    @command -v sagefs >/dev/null && sagefs check || echo "sagefs is not on PATH"

# Check the Crush preflight hook that gates the agent loop: it parses, it
# refuses the forbidden and the generic commands, and it allows ordinary ones.
# A hook that fails to parse exits 2, which Crush reads as a deny for every
# tool, so the parse check comes first and is fatal on its own.
hooks-check:
    #!/usr/bin/env bash
    set -uo pipefail

    hook="${XDG_CONFIG_HOME:-$HOME/.config}/crush/hooks/scale-preflight.sh"

    if [[ ! -f $hook ]]; then
        echo "No preflight hook at $hook" >&2
        exit 1
    fi

    if ! bash -n "$hook"; then
        echo "The preflight hook does not parse. Every tool call will be denied." >&2
        exit 1
    fi

    echo "parse    ok    $hook"

    failures=0

    probe() {
        local wanted="$1"
        local command_text="$2"
        local status

        CRUSH_TOOL_NAME=bash CRUSH_TOOL_INPUT_COMMAND="$command_text" "$hook" >/dev/null 2>&1
        status=$?

        if [[ $status == "$wanted" ]]; then
            printf 'ok       exit %s    %s\n' "$wanted" "$command_text"
        else
            printf 'FAIL     exit %s    %s (expected %s)\n' "$status" "$command_text" "$wanted"
            failures=$((failures + 1))
        fi
    }

    probe 2 'grep -rn RecordStep components'
    probe 2 'cat docs/now.typ'
    probe 2 'cd /tmp && sed -n 1,5p x'
    probe 2 'dotnet build'
    probe 2 'just check'
    probe 2 'sed -i s/a/b/ x'
    probe 0 'git status --short'
    probe 0 'just docs-check'
    probe 0 'echo ok'

    if [[ $failures != 0 ]]; then
        echo "$failures probe(s) disagreed with the policy" >&2
        exit 1
    fi

# Check the Incus client, server, and trusted connection.
incus-check:
    #!/usr/bin/env bash
    set -euo pipefail
    command -v incus >/dev/null || { echo "incus is not installed" >&2; exit 1; }
    incus info >/dev/null
    incus version

# Show the development instance, if it exists.
incus-status:
    #!/usr/bin/env bash
    set -euo pipefail
    if incus info "{{incus_instance}}" >/dev/null 2>&1; then
        incus list "{{incus_instance}}"
        incus config device show "{{incus_instance}}"
    else
        echo "{{incus_instance}} is not created"
    fi

# Create and start the Arch development container, mount the checkout, and wait
# until exec is available. Idempotent.
incus-create: incus-check
    #!/usr/bin/env bash
    set -euo pipefail
    name="{{incus_instance}}"
    image="{{incus_image}}"
    workspace="{{incus_workspace}}"
    source="$PWD"

    if incus info "$name" >/dev/null 2>&1; then
        echo "reusing $name"
    else
        incus launch "$image" "$name" \
            -c limits.cpu="{{incus_cpus}}" \
            -c limits.memory="{{incus_memory}}" \
            -d root,size="{{incus_disk}}"
    fi

    incus start "$name" >/dev/null 2>&1 || true

    ready=0
    for _ in $(seq 1 90); do
        if incus exec "$name" -- true >/dev/null 2>&1; then
            ready=1
            break
        fi
        sleep 1
    done

    if [[ "$ready" -ne 1 ]]; then
        echo "container did not become ready" >&2
        exit 1
    fi

    if ! incus config device show "$name" | grep -q '^workspace:'; then
        incus config device add "$name" workspace disk \
            source="$source" path="$workspace" shift=true
    fi

    incus config device show "$name"

# Install Arch packages, create the dev user, and install paru. Idempotent.
incus-bootstrap: incus-create
    #!/usr/bin/env bash
    set -euo pipefail
    name="{{incus_instance}}"
    user="{{incus_user}}"

    if [[ -f /etc/pacman.d/mirrorlist ]]; then
        incus file push /etc/pacman.d/mirrorlist "$name/etc/pacman.d/mirrorlist"
    fi

    incus exec "$name" -- bash -lc \
        'for attempt in 1 2 3; do pacman -Syu --noconfirm --needed base-devel git sudo just fd ripgrep bat eza jq fzf sd dotnet-sdk aspnet-runtime fish && exit 0; sleep 5; done; exit 1'

    incus exec "$name" -- bash -lc \
        "id -u $user >/dev/null 2>&1 || useradd -m -G wheel -s /usr/bin/fish $user"

    incus exec "$name" -- bash -lc "usermod -s /usr/bin/fish $user"

    incus exec "$name" -- su - "$user" -c \
        "mkdir -p ~/.config/fish && printf '%s\n' 'fish_add_path -g ~/.dotnet/tools' 'fish_add_path -g ~/.local/bin' 'set -gx DOTNET_CLI_TELEMETRY_OPTOUT 1' 'set -gx DOTNET_NOLOGO 1' > ~/.config/fish/config.fish"

    incus exec "$name" -- bash -lc \
        "printf '%s\n' '$user ALL=(ALL) NOPASSWD: ALL' > /etc/sudoers.d/90-scale-dev && chmod 440 /etc/sudoers.d/90-scale-dev"

    incus exec "$name" -- su - "$user" -c \
        'command -v paru >/dev/null 2>&1 || { rm -rf /tmp/paru-bin && git clone --depth 1 https://aur.archlinux.org/paru-bin.git /tmp/paru-bin && cd /tmp/paru-bin && makepkg -si --noconfirm; }'

    incus exec "$name" -- su - "$user" -c 'fish -lc "test -f ~/.config/fish/config.fish && echo incus-fish-config-ok"'

    incus exec "$name" -- su - "$user" -c \
        'mkdir -p ~/.config/fish/completions && just --completions fish > ~/.config/fish/completions/just.fish'

    incus exec "$name" -- su - "$user" -c \
        'command -v sagefs >/dev/null 2>&1 || dotnet tool install --global SageFs --version 0.6.923'

# Open an interactive shell as the dev user in the mounted checkout.
# The shell is the account's login shell, which the bootstrap sets to fish.
incus-shell: incus-create
    #!/usr/bin/env bash
    set -euo pipefail
    name="{{incus_instance}}"
    user="{{incus_user}}"
    uid=$(incus exec "$name" -- id -u "$user")
    login_shell=$(incus exec "$name" -- getent passwd "$user" | cut -d: -f7)
    incus exec -t "$name" --user "$uid" --cwd "{{incus_workspace}}" \
        --env HOME="/home/$user" -- "$login_shell" -l

# Run one command as the dev user in the mounted checkout.
incus-run *command: incus-create
    #!/usr/bin/env bash
    set -euo pipefail
    uid=$(incus exec "{{incus_instance}}" -- id -u "{{incus_user}}")
    incus exec "{{incus_instance}}" --user "$uid" --cwd "{{incus_workspace}}" \
        --env HOME="/home/{{incus_user}}" -- bash -lc "{{command}}"

# Verify the booted container from outside, without opening a shell.
# It runs the account's login shell, so the fish path setup is proved too.
incus-verify: incus-create
    #!/usr/bin/env bash
    set -euo pipefail
    name="{{incus_instance}}"
    user="{{incus_user}}"
    workspace="{{incus_workspace}}"
    uid=$(incus exec "$name" -- id -u "$user")
    login_shell=$(incus exec "$name" -- getent passwd "$user" | cut -d: -f7)

    test "$(basename "$login_shell")" = fish

    incus exec "$name" --user "$uid" --cwd "$workspace" --env HOME="/home/$user" -- "$login_shell" -lc \
        "dotnet --version; and just --version; and paru --version; and fd --version | head -1; and rg --version | head -1; and fish --version; and sagefs --version; and test -f ~/.config/fish/completions/just.fish; and test -f workspace.edn; and echo incus-verify-ok"

# Create a named snapshot of the development container.
incus-snapshot name="snapshot": incus-create
    #!/usr/bin/env bash
    set -euo pipefail
    case "{{name}}" in
        *[!A-Za-z0-9._-]*|"")
            echo "invalid snapshot name: {{name}}" >&2
            exit 1
            ;;
    esac
    incus snapshot create "{{incus_instance}}" "{{name}}"

# Stop the development container if it is running.
incus-stop:
    #!/usr/bin/env bash
    set -euo pipefail
    incus stop "{{incus_instance}}" >/dev/null 2>&1 || true

# Delete the development container. Requires confirm=yes.
incus-delete confirm="no":
    #!/usr/bin/env bash
    set -euo pipefail
    if [[ "{{confirm}}" != "yes" ]]; then
        echo "refusing to delete {{incus_instance}}; pass confirm=yes" >&2
        exit 1
    fi
    incus delete --force "{{incus_instance}}"

# Create and bootstrap the Incus development container.
incus-up: incus-bootstrap

# Format with Fantomas (the formatting authority).
fmt:
    dotnet fantomas .

# Verify formatting without writing.
fmt-check:
    dotnet fantomas check .

# Compile the product. Warnings are failures for the handoff gate.
build:
    dotnet build bases/scale-cli/scale.fsproj

# Compile the JIT-only test deployable.
build-tests:
    dotnet build projects/scale-tests/scale-tests.fsproj

# Publish a NativeAOT binary for one RID (default: linux-x64).
publish rid="linux-x64": (check_rid rid)
    dotnet publish bases/scale-cli/scale.fsproj -c Release -r {{rid}} -p:EnableWriter=false -o artifacts/native/{{rid}}

# Publish the kernel as a NativeAOT shared library and run the C conformance
# harness against it on Linux. A kernel change requires this check; the product
# AOT publish remains a release step.
kernel-aot rid="linux-x64": (check_rid rid)
    #!/usr/bin/env bash
    set -euo pipefail
    out="artifacts/native/kernel/{{rid}}"
    rm -rf "$out"
    dotnet publish components/kernel/Scale.Kernel.fsproj -c Release -r {{rid}} -p:EnableWriter=false -o "$out"
    case "{{rid}}" in
        linux-*)
            if [[ -f "$out/libScale.Kernel.so" ]]; then
                lib="$out/libScale.Kernel.so"
            else
                lib="$out/Scale.Kernel.so"
            fi
            ;;
        osx-*)
            if [[ -f "$out/libScale.Kernel.dylib" ]]; then
                lib="$out/libScale.Kernel.dylib"
            else
                lib="$out/Scale.Kernel.dylib"
            fi
            ;;
        win-*) lib="$out/Scale.Kernel.dll" ;;
        *) echo "Unsupported RID: {{rid}}" >&2; exit 1 ;;
    esac
    if [[ ! -f "$lib" ]]; then
        echo "kernel-aot: expected $lib" >&2
        exit 1
    fi
    echo "kernel-aot: $lib"
    if [[ "{{rid}}" == linux-* ]]; then
        just conformance "$lib"
    else
        echo "kernel-aot: published; the C conformance harness runs on Linux"
    fi

# The AOT artifact is the product binary; the suite is JIT-only. This is the
# release smoke, kept under the historical recipe name.
aot-test rid="linux-x64": (aot-smoke rid)

# Run the published product binary from an isolated directory, so a missing
# publish payload (demo fixtures, witnesses) fails. The test suite is not in
# this artifact and is not run here.
aot-smoke rid="linux-x64": (publish rid)
    #!/usr/bin/env bash
    set -euo pipefail
    tmpdir="$(mktemp -d)"
    trap 'rm -rf "$tmpdir"' EXIT
    cp -r artifacts/native/{{rid}}/. "$tmpdir"
    cp components/verification/tests/fixtures/math.book "$tmpdir/math.book"
    cd "$tmpdir"
    output="$(./scale)"
    printf '%s\n' "$output"
    grep -Fq '=== Building Image ===' <<<"$output"
    grep -Fq '=== Persistence ===' <<<"$output"

    # The product must also carry the language entry points into the isolated
    # AOT payload, not just the demo showcase.
    printf '%s\n' '1 1 add.' > prose-smoke.prose
    printf '%s\n' 'logic main = 42.' > book-smoke.book

    prose="$(./scale --prose prose-smoke.prose)"
    grep -Fq '2' <<<"$prose"

    book="$(./scale --book book-smoke.book)"
    grep -Fq 'Kind:  spell' <<<"$book"
    grep -Fq '42' <<<"$book"

    cast="$(./scale --cast math.book double 21)"
    grep -Fq '42' <<<"$cast"

# The release command: format, full suite, NativeAOT kernel and product smoke,
# then refresh test counts and generated docs, verify them, and archive.
# Counts are updated only here, never during normal work. No git or network
# side effects.
release rid="":
    #!/usr/bin/env bash
    set -euo pipefail
    rid="{{rid}}"
    if [[ -z "$rid" ]]; then
        case "$(uname -s)-$(uname -m)" in
            Linux-x86_64) rid=linux-x64 ;;
            Linux-aarch64) rid=linux-arm64 ;;
            Darwin-arm64) rid=osx-arm64 ;;
            Darwin-x86_64) rid=osx-x64 ;;
            *) echo "Unsupported host: $(uname -s) $(uname -m); pass a RID explicitly." >&2; exit 1 ;;
        esac
    fi
    just check_rid "$rid"
    just fmt
    just poly-check
    just test-granular
    just kernel-aot "$rid"
    rm -rf "artifacts/native/$rid"
    just aot-test "$rid"
    rm -f "artifacts/native/$rid/libz3.dll"
    export SCALE_RELEASE=1
    just release-test-count
    just test-all
    just line-counts
    just docs-check
    just fmt-check
    just release-archive "$rid"

# Archive the already-published product payload. `aot-test` owns the publish and
# smoke; this recipe only packages that verified artifact.
release-archive rid:
    #!/usr/bin/env bash
    set -euo pipefail
    output_dir="artifacts/native/{{rid}}"
    if [[ ! -d "$output_dir" ]]; then
        echo "release-archive: missing $output_dir; run 'just aot-test {{rid}}' first" >&2
        exit 1
    fi
    archive="artifacts/scale-{{rid}}.tar.gz"
    tar -czf "$archive" -C "$output_dir" .
    echo "release artifact: $archive"

# Compile the C adapter and the conformance harness (alpha; no native kernel).
conformance-check:
    cc -std=c11 -Wall -Wextra -Werror -fsyntax-only -I components/kernel -I components/adapter-c components/adapter-c/scale_adapter.c
    cc -std=c11 -Wall -Wextra -Werror -fsyntax-only -I components/kernel -I components/adapter-c components/adapter-c/conformance/conformance.c
    cc -std=c11 -Wall -Wextra -Werror -fsyntax-only -I components/kernel components/adapter-c/conformance/fake-kernel.c
    cc -std=c11 -Wall -Wextra -Werror -fsyntax-only -I components/kernel -I components/adapter-c components/adapter-c/conformance/version-probe.c
    cc -std=c11 -Wall -Wextra -Werror -I components/kernel -I components/adapter-c components/adapter-c/scale_adapter.c components/adapter-c/conformance/conformance.c -o /tmp/scale-conformance -ldl

# Prove the adapter refuses a kernel with a different ABI version (alpha).
conformance-version-check: conformance-check
    #!/usr/bin/env bash
    set -euo pipefail
    cc -std=c11 -Wall -Wextra -Werror -shared -fPIC -I components/kernel components/adapter-c/conformance/fake-kernel.c -o /tmp/libscale-fake-kernel.so
    cc -std=c11 -Wall -Wextra -Werror -I components/kernel -I components/adapter-c components/adapter-c/conformance/version-probe.c components/adapter-c/scale_adapter.c -o /tmp/scale-version-probe -ldl
    /tmp/scale-version-probe /tmp/libscale-fake-kernel.so

# Run the C conformance harness against a native kernel (release only).
conformance lib="artifacts/native/kernel/linux-x64/Scale.Kernel.so":
    #!/usr/bin/env bash
    set -euo pipefail
    cc -std=c11 -Wall -Wextra -Werror -I components/kernel -I components/adapter-c components/adapter-c/scale_adapter.c components/adapter-c/conformance/conformance.c -o /tmp/scale-conformance -ldl
    /tmp/scale-conformance "{{lib}}"

# Run the full, unfiltered suite. `dotnet test` does not discover this Exe suite.
test:
    dotnet run --project projects/scale-tests/scale-tests.fsproj -- --tests

# Run every kind of test once: examples, properties, snapshots, integrations.
test-all:
    dotnet run --project projects/scale-tests/scale-tests.fsproj -- --all-tests

# Run the granular suite and the snapshots (JIT only; see D23).
test-granular:
    dotnet run --project projects/scale-tests/scale-tests.fsproj -- --granular-tests

# Internal release step: run the example suite, count the granular cases, then
# update every current test-count reference. Only `just release` calls this.
release-test-count:
    dotnet fsi tools/update-test-counts.fsx --write

# Run both suites and verify the documented counts without writing.
test-count-check:
    dotnet fsi tools/update-test-counts.fsx --check

# Regenerate docs/generated/line-counts.typ from scc. Run at a documentation checkpoint
# or when files are added or removed, not after every edit.
line-counts:
    dotnet fsi tools/update-line-counts.fsx --write

# Verify the documented line counts without writing.
line-counts-check:
    dotnet fsi tools/update-line-counts.fsx --check

# Read the F# recursion classification from one source file with FCS. This is
# the build-time adapter; it emits the read-off JSON consumed by
# components/core/fsharp-abstraction.fs.
fsharp-classify file:
    dotnet fsi tools/fsharp-classifier.fsx {{file}}

# Compile every root Typst document under docs/ to docs/pdf/. Run after an
# explicit presentation request; the PDFs are checked in and must never be deleted.
docs-pdf:
    #!/usr/bin/env bash
    set -euo pipefail
    shopt -s nullglob globstar

    if ! command -v typst >/dev/null; then
        echo "typst is not on PATH." >&2
        exit 1
    fi

    files=(docs/**/*.typ)

    if [[ "${#files[@]}" -eq 0 ]]; then
        echo "no Typst documents under docs/" >&2
        exit 1
    fi

    mkdir -p docs/pdf

    for file in "${files[@]}"; do
        relative="${file#docs/}"
        name="${relative%.typ}"
        mkdir -p "docs/pdf/$(dirname "$name")"
        typst compile "$file" "docs/pdf/${name}.pdf"
        echo "wrote docs/pdf/${name}.pdf"
    done

# Archive and remove the `.bak.<timestamp>` files that the Typst MCP writes
# before every rewrite. Run at the commit boundary, after `just docs-pdf` and
# before the commit; the archive keeps the evidence out of the repository.
docs-clean:
    #!/usr/bin/env bash
    set -euo pipefail
    shopt -s nullglob globstar
    archive="/tmp/scale-doc-backups-$(date +%Y%m%d-%H%M%S).tar.gz"
    files=(docs/**/*.bak.* README.typ.bak.*)
    if [[ "${#files[@]}" -eq 0 ]]; then
        echo "no documentation backups"
        exit 0
    fi
    tar -czf "$archive" "${files[@]}"
    rm -f "${files[@]}"
    echo "archived ${#files[@]} backup(s) to $archive"

# Validate the Polylith workspace: manifest, dependency rules, cycles, interfaces.
poly-check:
    dotnet fsi tools/polylith-check.fsx
    dotnet fsi tools/component-extensions-check.fsx

# Check that every component records its potential extensions at the bottom of
# its implementation file.
extensions-check:
    dotnet fsi tools/component-extensions-check.fsx

# Print the module dependency cuts for the app base.
module-deps:
    dotnet fsi tools/module-dependencies.fsx

# Regenerate the machine-readable specification projection.
spec-model:
    dotnet fsi tools/spec-model.fsx --write

# Cross-check the facts the compiler and tests cannot see: ABI and schema
# versions, documented suite counts, decision ids, the docs index, and compile
# items.
docs-check:
    dotnet fsi tools/spec-model.fsx --check
    dotnet fsi tools/consistency-check.fsx
    dotnet fsi tools/architecture-check.fsx --check
    dotnet fsi tools/decision-index.fsx --check
    dotnet fsi tools/type-system-check.fsx

# Compare project sources with the last Debug build. A stale build means the REPL
# and the tests are not running the files on disk.
freshness-check:
    dotnet fsi tools/freshness-check.fsx

# The handoff gate: MCP gate, architecture, documentation, and freshness checks,
# formatting, build, and every kind of test. The gate runs first, so a configured
# server that cannot answer (Vestige included) fails before the expensive work.
# Line counts are a documentation metric and are intentionally not part of this
# gate.
# Expect 1741 checks (1683 examples, 58 properties), 13 snapshots, 14 integrations, and 36 granular cases (13 snapshots, 14 integrations, 9 writer checks).
check: mcp-check poly-check docs-check fmt-check build build-tests freshness-check kernel-aot
    dotnet run --no-build --project projects/scale-tests/scale-tests.fsproj -- --all-tests
    dotnet run --no-build --project projects/scale-tests/scale-tests.fsproj -- --granular-tests

# Run the tests and then the product showcase.
run:
    dotnet run --project projects/scale-tests/scale-tests.fsproj -- --all-tests
    dotnet run --project bases/scale-cli/scale.fsproj

# Reuse this checkout's SageFs session, or create one when none is active.
# Never stops or restarts a shared daemon.
sage-ensure project="projects/scale-tests/scale-tests.fsproj":
    bash tools/sage-ensure.sh {{project}}

# Read-only look at the shared SageFs daemon. Do not restart it.
repl-status:
    sagefs status

# Classify the current working tree before deciding whether to start a rebuild.
rebuild-plan:
    dotnet fsi tools/rebuild-plan.fsx

# Build-performance measurement only. This is the one alpha exception to the
# no-MSBuild rule; it writes binlogs under /tmp and does not run tests.
measure-build:
    dotnet fsi tools/build-time.fsx

# Verify the F# language server really loads the Scale projects, and that Serena
# is healthy. Read-only; no restart.
lsp-check:
    #!/usr/bin/env bash
    set -euo pipefail

    if ! command -v serena >/dev/null; then
        echo "Serena not found on PATH." >&2
        exit 1
    fi

    echo "serena $(serena --version 2>&1 | head -1)"

    # Health is not project load: prove a real Scale file resolves symbols. This
    # speaks LSP itself, with the client options this repository needs, so it
    # cannot be satisfied by a healthy server with an empty project set.
    dotnet fsi tools/lsp-symbol-check.fsx

    status=0
    output="$(timeout 180 serena project health-check "{{justfile_directory()}}" 2>&1)" || status=$?

    if [[ "$status" -ne 0 ]]; then
        printf '%s\n' "$output" | tail -40 >&2
        echo "Serena F# LSP health check failed." >&2
        echo "Report this, then run 'just lsp-restart' and retry." >&2
        exit 1
    fi

    printf '%s\n' "$output" | tail -5

    # Serena is a second client and it is health-checked above. Its symbol path
    # is advisory: SolidLSP's F# server passes no initialization options, so it
    # cannot exclude the vendored Nu workspace and cannot load these projects.
    # The assertion above is the gate; this reports the known divergence.
    root="{{justfile_directory()}}"
    index_status=0
    index_output="$(timeout 180 serena project index-file components/prelude/evaluator/evaluator.fs "$root" 2>&1)" || index_status=$?
    if [[ "$index_status" -ne 0 ]]; then
        echo "warning: Serena's own F# server cannot load this workspace (SolidLSP passes no" >&2
        echo "  initialization options, so the peek picks components/nu-engine/Nu.slnx)." >&2
        echo "  Symbol work goes through the configured agent client; see Notes/lsp-workspace-plan.md." >&2
    else
        printf '%s\n' "$index_output" | tail -1
        echo "Serena F# LSP: OK (project loaded)"
    fi
    echo "F# LSP: OK (workspace loaded, symbols resolve)"

# Restart this user's F# language-server processes, then check. Never touches SageFs.
lsp-restart:
    #!/usr/bin/env bash
    set -euo pipefail
    pkill -u "$(id -u)" -f '[f]sautocomplete' 2>/dev/null || true
    sleep 1
    just lsp-check

# Fast MCP gate: config, server commands, daemon; `restart` restarts SageFs first.
mcp-check action="up":
    #!/usr/bin/env bash
    set -euo pipefail

    config="${HOME}/.config/opencode/opencode.json"

    if ! jq -e '.mcp | type == "object" and length > 0 and all(.[]; .enabled == true)' "$config" >/dev/null; then
        echo "One or more MCP servers are disabled or missing in $config." >&2
        exit 1
    fi

    if ! jq -e '.mcp.serena.command | index("start-mcp-server")' "$config" >/dev/null; then
        echo "The serena MCP command must be: /usr/bin/serena start-mcp-server ..." >&2
        exit 1
    fi

    # A configured server whose executable is missing cannot be started later,
    # so resolve every command now: this is cheap and catches the common break.
    while IFS= read -r command; do
        if ! command -v "$command" >/dev/null; then
            echo "MCP server command not found on PATH: $command" >&2
            exit 1
        fi
    done < <(jq -r '.mcp[].command[0] // empty' "$config" | sort -u)

    if jq -e '.mcp.vestige' "$config" >/dev/null; then
        if ! vestige-mcp --version >/dev/null 2>&1; then
            echo "Vestige is configured but vestige-mcp --version failed." >&2
            exit 1
        fi

        # Version is not liveness. Start a session and read the store health
        # through the real MCP handshake. An empty store is healthy; no answer
        # is not.
        if ! vestige_output="$(
            printf '%s\n' \
                '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"scale-mcp-check","version":"1"}}}' \
                '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
                '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"memory_status","arguments":{"view":"health"}}}' \
                '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"session_start","arguments":{"context":{"repoPath":"{{justfile_directory()}}","codebase":"Scale"},"include_intentions":true,"include_status":true,"filters":{"token_budget":2000}}}}' \
                | timeout 20 vestige-mcp 2>/dev/null
        )"; then
            vestige_output=""
        fi

        vestige_memories="$(
            printf '%s\n' "$vestige_output" \
                | jq -R 'fromjson? | select(.id == 2) | .result.structuredContent.totalMemories' \
                | tail -1
        )"

        if ! [[ "$vestige_memories" =~ ^[0-9]+$ ]]; then
            echo "Vestige is configured but did not answer memory_status; check its data directory and server." >&2
            exit 1
        fi

        if ! printf '%s\n' "$vestige_output" | jq -eR 'fromjson? | select(.id == 3) | .result' >/dev/null; then
            echo "Vestige is configured but did not start a session; check its data directory and server." >&2
            exit 1
        fi

        echo "Vestige session started ($vestige_memories memories)."
    fi

    if [[ "{{action}}" == "restart" ]]; then
        timeout 15 sagefs stop >/dev/null 2>&1 || true
    fi

    if ! timeout 8 sagefs status >/dev/null 2>&1; then
        echo "Starting the SageFs daemon..."
        setsid sagefs --ttl 2h </dev/null >/dev/null 2>&1 &

        for _ in $(seq 1 20); do
            if timeout 4 sagefs status >/dev/null 2>&1; then
                break
            fi

            sleep 1
        done
    fi

    if ! timeout 8 sagefs status >/dev/null 2>&1; then
        echo "The SageFs daemon is not answering. Run 'just mcp-check restart'." >&2
        exit 1
    fi

    count="$(jq '.mcp | length' "$config")"
    echo "MCP gate: $count server(s) configured and present; the SageFs daemon is healthy."
    echo "Run 'just mcp-verify' for the full handshake."

# Full MCP handshake, cached against the config and binaries; `force` bypasses.
mcp-verify force="":
    #!/usr/bin/env bash
    set -euo pipefail

    config="${HOME}/.config/opencode/opencode.json"
    stamp="${XDG_CACHE_HOME:-$HOME/.cache}/scale/mcp-verify.stamp"
    mkdir -p "$(dirname "$stamp")"

    commands="$(jq -r '.mcp[].command[0] // empty' "$config" | sort -u)"

    while IFS= read -r command; do
        if ! command -v "$command" >/dev/null; then
            echo "MCP server command not found on PATH: $command" >&2
            exit 1
        fi
    done <<< "$commands"

    fingerprint="$(
        {
            sha256sum "$config"
            while IFS= read -r command; do
                stat -Lc '%n %Y' "$(command -v "$command")"
            done <<< "$commands"
        } | sha256sum | cut -d' ' -f1
    )"

    if [[ "{{force}}" != "force" && -f "$stamp" && "$(cat "$stamp")" == "$fingerprint" ]]; then
        echo "MCP handshake cached: the config and server binaries are unchanged."
        exit 0
    fi

    output="$(timeout 60 opencode mcp list 2>&1 || true)"
    printf '%s\n' "$output"

    if [[ "$output" == *"failed"* || "$output" == *"✗"* ]]; then
        echo "MCP servers are not all connected. Fix the named server, then rerun." >&2
        rm -f "$stamp"
        exit 1
    fi

    printf '%s\n' "$fingerprint" > "$stamp"
    echo "MCP handshake verified; cached until the config or a server binary changes."

# Restart the SageFs daemon, then run the fast MCP check.
mcp-restart:
    @just mcp-check restart
