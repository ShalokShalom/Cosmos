# smolvm review and image-format decision

Status: review complete; conditional adoption.

## Reviewed

- Repository: `smol-machines/smolvm`
- Commit: `b1c2340fb0a2b67b3d330716edc67f432d9fb67f`
- Version: `1.34.0`
- License: Apache-2.0
- Scope: pack format, OCI layer extraction, security model, CI, tests, and
  image/artifact boundaries.

## Evidence

- `cargo test -p smolvm-pack -p smolvm-oci-layer --lib`:
  **197 passed, 0 failed, 2 ignored**.
- CI covers macOS arm64, Linux x86_64/aarch64, Linux musl, Windows cross,
  formatting, Clippy with `-D warnings`, `cargo audit`, secrets guards, libkrun
  provenance, glibc floor, and static guest init.
- The OCI layer extractor has a jailed join that rejects lexical traversal and
  planted symlink escapes, with tests.
- The pack format has bounds checks, SHA-256 identity, CRC32 footer, no-clobber
  publication, sparse-file handling, and extraction tests.
- The security model is explicit: guest root is untrusted; host user, host OS,
  hypervisor, libkrun, and smolvm are in the trusted computing base.

## Findings

Positive:

- Real VM boundary with a separate guest kernel.
- OCI-native images; no Docker daemon.
- Embeddable library and CLI.
- Strong CI and test depth for the pack/OCI slice.
- Security-conscious extraction and release checks.
- `.smolmachine` is a real portable stateful artifact with a versioned format.

Caveats:

- Release archives are checksum-verified but not signed and have no provenance
  attestations; the installer permits installation when the checksum file is
  unavailable.
- The host user, host OS, hypervisor, and libkrun are in the TCB. This is not a
  hostile-local-user or multi-tenant control plane.
- `.smolmachine` is **not** an OCI image format. It is a self-contained stateful
  VM/checkpoint pack layered over OCI image layers.
- The format version history includes a 3→1 reset with compatibility reads; this
  is documented, but it is a governance detail to pin.
- Full VM integration and CRI conformance are out of the local unit-test run;
  they require KVM/libkrun and real hardware.

## Decision

Adopt smolvm conditionally:

1. **OCI is the canonical image format.** Keep standard OCI images as the
   interoperable unit.
2. **`.smolmachine` is the portable world/checkpoint artifact**, not the
   canonical image format. It may be pushed to an OCI registry as a custom
   media type, but it does not replace OCI layers.
3. **smolvm is the microVM runtime** for untrusted or whole-OS-isolated worlds.
4. **Incus is the higher-level orchestrator** for trusted system containers,
   projects, profiles, storage, and networking. Incus containers are not the
   whole-OS boundary; use Incus VMs or smolvm where that boundary is required.
5. Pin the smolvm version and artifact digest. Do not run unsigned release
   archives for untrusted workloads. Add signature/provenance before making it
   a default installer path.

## Consequences

- The world artifact remains portable across hosts.
- OCI interoperability is preserved.
- Incus can own trusted container orchestration without pretending to be the
  microVM boundary.
- Two runtime paths must be tested: Incus containers/VMs and smolvm microVMs.
- The host control plane remains the trusted component.
