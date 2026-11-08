# Install files and capability-literal access

> **The surface should be capability-literal, not effect-abstract.**

This is the design slogan for `.install` files and the resource/effect surface.

## Meaning

- The computer appears as named, concrete capabilities: `console`, `net`,
  `files`, `host`.
- Those names are installation-scoped opaque tokens, not ambient globals.
- A function receives the token it uses; the caller passes it explicitly.
- An `.install` file is the only composition root that acquires host resources.
- Effect labels are derived requirements, not authority. A label says what a
  function needs; the installation supplies the concrete token.
- Release is declared and ordered; a token cannot be forged or dereferenced.

## Boundary

Literal names at the surface, opaque tokens at runtime, explicit passing at
every boundary, and acquisition only in `.install`.

This is a design proposal. The parser, token value kind, ownership checker, and
install runtime are not implemented yet. The prose surface is underdeveloped and
is not ready for review; the install/token/plan layers come first.
