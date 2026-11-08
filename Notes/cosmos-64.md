# Cosmos 64

Status: idea note, non-normative.

**Cosmos 64** is a Creative open source multimedia operating system for 64-bit
devices. The guiding principle is **abstraction all the way down**: IRs are the
basic principle of computing, and software is built by composing layers that each
expose a smaller, more meaningful contract.

The Scale stack is one possible semantic spine for Cosmos 64:

```text
64-bit hardware
  -> kernel and C ABI
  -> image and portable IRs
  -> logic definitions
  -> prose execution
  -> applications, media, and tools
```

The important point is not that every layer needs a different IR. It is that
every layer needs an explicit contract:

- the kernel owns execution and the ABI;
- the image owns units, dependencies, and hot-swap identity;
- the semantic term IR owns expressions, patterns, packages, and code values;
- `logic` owns reusable definitions;
- prose owns execution-only composition;
- applications own domain policy.

This is why the IR case study matters. A small number of well-chosen IRs with
explicit lowerings is stronger than a large number of peer representations. The
target is one semantic term family plus one lowered execution form, with
portable documents generated as projections.

## Constraints

- Do not collapse the kernel/Prelude boundary just to reduce the IR count.
- Do not make every layer share source code; share contracts, schemas, and
  conformance vectors where the boundary is real.
- Keep NativeAOT and the C ABI viable.
- Keep the multimedia layer outside the language core; it consumes the host and
  image contracts.
- Treat "abstraction all the way down" as a design direction, not a claim that
  every layer has the same semantics.

## Relationship to Scale

Scale supplies the language and semantic runtime; Cosmos 64 supplies the device,
multimedia, and application environment. The shared contract is the image,
portable documents, host-call boundary, and capability model.
