= Quest: KaOS Noctalia kiosk

#strong[Status: research and contract only; nothing installed or implemented.]
The target is one bootable KaOS machine whose entire interface is Noctalia, whose
single visible application is a maximized Konsole running TuiOS, and whose
behaviour is driven from the Scale Action layer. This page fixes the control
contract and the boot shape before any configuration is written.

== Problem

Four requests meet in one image:

+ the desktop is 100% KaOS with Noctalia as the only UI;
+ exactly one application is visible: a maximized Konsole;
+ a development environment exists at boot, then TuiOS takes over;
+ Scale Action drives the machine, with tapes as the recording medium.

Every one of them constrains a different layer (#text[compositor], #text[shell],
#text[terminal], #text[window manager], #text[automation]). Wiring them in the
wrong order produces a desktop that is 90% Plasma, or a Konsole that cannot show
TuiOS graphics, or an automation layer that types into its own terminal.

== Evidence

#table(
  columns: (auto, auto, 1fr),
  align: (left, left, left),
  table.header([Piece], [Verified fact], [Source]),
  [Noctalia],
  [Native Wayland shell, no Qt or GTK, OpenGL ES, built on layer-shell; TOML configuration with hot reload and IPC for runtime control; MIT.],
  [github.com/noctalia-dev/noctalia, `README.md`],
  [Noctalia compositors],
  [Integrations listed: Niri, Hyprland, Sway, Scroll, Mango, Labwc, dwl, and other layer-shell compositors. KWin is not listed.],
  [same `README.md`, "Wayland Compositor Support"],
  [TuiOS],
  [Go terminal window manager and multiplexer, MIT; daemon keeps sessions alive; unix socket; JSON verb protocol; tape DSL; TOML configuration; prefix `Ctrl+B`.],
  [github.com/Gaurav-Gosain/tuios, `README.md`],
  [TuiOS control],
  [Line-delimited JSON over the daemon's unix socket, one request line and one response line, classified from the first byte; `hello` reports a protocol range; every result carries a `type`; errors carry `code`, `message` and a `hint`.],
  [tuios `docs/protocol.md`],
  [TuiOS tapes],
  [`Action`, `Press`, `Run`, `WaitFor`, `Expect`, `Source`; `tuios tape play/validate/exec`; `exec` waits and exits non-zero with file, line and column.],
  [tuios `docs/TAPE_SCRIPTING.md`],
  [TuiOS terminal needs],
  [True color required; kitty graphics and sixel recommended, not required.],
  [tuios `README.md`, "Requirements"],
  [VHS],
  [Separate recorder, MIT; needs `ttyd` and `ffmpeg`; one tape renders to gif/mp4/webm/PNG frames/ascii; `vhs manual` is the command reference.],
  [github.com/charmbracelet/vhs, `README.md`],
  [VHS drives its own terminal],
  [A tape is played into a virtual terminal VHS spawns and captures; it is not a control channel to a running desktop.],
  [same `README.md`, "Tutorial" and "Hide"],
  [KaOS],
  [Arch-based distribution; `pacman` plus AUR through a helper. It ships KDE
    Plasma today and abandons it next month, so Plasma is not a stable target.],
  [kaosx.us; the Plasma retirement is the user's statement, not yet verified in
    a KaOS release note],
)

Two facts decide the whole design:

- Noctalia cannot be the UI of a Plasma/KWin session; it needs a compositor that
  speaks layer-shell.
- VHS cannot control this machine. It records. TuiOS has its own tape DSL and a
  separate JSON control protocol, and those are the automation surfaces.

== Options

=== A. Dedicated Niri session

A Wayland session entry starts Niri, Niri starts Noctalia, Noctalia's seed
starts one maximized Konsole, and Konsole runs TuiOS.

- Rich: the smallest layer-shell compositor on the support list, scrollable
  columns, one window by construction, no Plasma process at all.
- Compact: one session `.desktop`, one compositor config, Noctalia seeds the
  launch.
- Cost: KaOS's KDE identity is unused at boot. TuiOS sits inside a real
  compositor, so it is not the top of the stack.
- Risk: nested-or-not is decided by the compositor; Niri's own nesting support
  is #text[unverified].

=== B. Nested compositor inside one Konsole

KaOS autologins to a Plasma session with no panel, autostarts one Konsole
fullscreen, and Konsole runs a nested compositor (Hyprland) whose sole window is
Noctalia's surface, with TuiOS in a terminal view.

- Satisfies both literal requirements at once: Plasma is still the session, one
  Konsole is the only application, and the UI drawn is Noctalia.
- Two window managers stack (KWin for Konsole, the nested compositor for
  Noctalia), which is twice the invariants and twice the failure modes.
- Nested mode for the chosen compositor is #text[unverified]; graphics
  passthrough through Konsole for Noctalia's OpenGL ES surfaces is
  #text[unverified].
- Rejected unless the user asks for "Konsole literally holds Noctalia".

=== C. Plasma-only kiosk

KWin autostart with panels removed, one fullscreen Konsole, TuiOS inside, no
Noctalia.

- Cheapest and most stable, and it is the only option with zero unverified
  compositor claims.
- Fails the 100% Noctalia requirement outright. It is the fallback, not the
  plan.

=== D. Bare terminal, no compositor

A DRM/KMS console login, `getty` on tty1, `agetty -a` autologin, Konsole in
framebuffer or `kmscon`, TuiOS on top.

- Fewest moving parts, and TuiOS genuinely is the interface.
- No Wayland surface exists, so Noctalia cannot run. Fails the requirement.

Decision: #text[A] is confirmed. The user ruled out a Plasma session, and KaOS
abandons Plasma next month, so #text[B] and #text[C] are withdrawn. #text[D]
stays the degradation when no compositor starts. The kiosk is paused while the
language work continues.

== Control contract

The control layer is one small surface, named for what it does, holding the
refusal vocabulary TuiOS already publishes instead of inventing a second one.

```text
TuiOSControl
  ensure_daemon   : unit -> Result<Daemon, Refusal>
  list_sessions   : unit -> Result<Session list, Refusal>
  new_window      : Session -> Name -> Command -> Result<Window, Refusal>
  run             : Window -> Command -> Result<unit, Refusal>
  send_keys       : Window -> Key list -> Result<unit, Refusal>
  capture_pane    : Window -> Source -> Result<string, Refusal>
  tape_exec       : Path -> Result<TapeReport, Refusal>

Refusal =
  ToolMissing name
  DaemonUnavailable reason
  ProtocolMismatch served accepted
  UnknownVerb verb
  SessionNotFound session available
  WindowNotFound window available
  Forbidden capability config_table
  TapeFailed file line column
  OutputTooLarge bound
```

Rules:

- Every refusal maps to a TuiOS `code` and, where present, its `hint` fields
  (`param`, `accepted`, `available`, `command`, `did_you_mean`). A refusal the
  protocol does not name is `DaemonUnavailable`, never a silent success.
- `ProtocolMismatch` is decided by `hello` alone; a daemon that predates the
  protocol closes the connection with no line, which is a mismatch, not a
  transport fault.
- `Forbidden` keeps TuiOS's `missing` and `table` hint so the Scale refusal can
  name the config table that grants the capability.
- `OutputTooLarge` bounds every capture; `capture-pane` returns untrusted text
  from another machine, so the fence and control-character rules of the protocol
  are applied before a Value is built.

=== Two transports, one order

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Transport], [Shape], [When]),
  [CLI], [`tuios <verb> --json` through the existing shell host words], [First slice. Bounded output, no framing code, version mismatch already reported by the CLI.],
  [Socket], [line JSON on the daemon socket, `hello` first], [When events matter: `subscribe`, `wait-for`, `agent-state`. The CLI cannot stream.],
)

The CLI route reuses the host words from D183 (`run`, `capture`) and the
`ToolCatalog` rules from `quest-shell-tools.typ`: a missing tool is a named
refusal, arguments are built, never interpolated, and output is decoded or
refused, never trusted. The socket route is the extension, not a rewrite.

== Tapes and recording

Three different things are called a tape. They stay separate.

#table(
  columns: (auto, auto, 1fr),
  align: (left, left, left),
  table.header([Artifact], [Owner], [Purpose]),
  [TuiOS tape], [TuiOS], [Drives a live session: `Action`, `Press`, `Run`, `WaitFor`, `Expect`. The only tape that changes the machine.],
  [VHS tape], [VHS], [Renders a gif or ascii golden file from a virtual terminal. Never touches the running kiosk.],
  [Scale Action script], [Scale], [Composes the two: calls `tape_exec` for behaviour, calls VHS only to record the result for documentation.],
)

The first failing command stops a TuiOS tape, and `tape exec` exits non-zero
with file, line and column, which becomes `TapeFailed`. A VHS tape is a build
artifact of the documentation, not a control path.

== Boot sequence

+ Firmware, loader, kernel, `systemd`.
+ `getty` autologin on the graphical VT, or SDDM autologin if a display manager
  is kept. The exact unit and file are KaOS-specific and belong to the
  implementation slice.
+ The session entry starts the compositor; the compositor starts Noctalia.
+ Noctalia seeds one maximized Konsole with no tab bar.
+ Konsole runs TuiOS, which attaches to its daemon and restores the saved session.
+ The development environment is a TuiOS window group from the saved layout, not
  a second application.
+ Scale Action connects through `TuiOSControl` and drives from there.

Each step must be observable from outside: `tuios ls --json`, `capture-pane`, a
compositor log, and a systemd unit state. A step that cannot be observed does not
count as done.

== Refusals and limits

- No automatic compositor install. Package installation is a named, explicit
  step, as in `quest-shell-tools.typ`.
- No second window manager if option A is chosen: one compositor, one shell, one
  terminal, one window manager inside it.
- No key injection into TuiOS's own terminal through VHS; keystrokes go through
  TuiOS's verbs so its modal state is respected.
- No reading or writing the daemon socket as an unauthenticated root shortcut;
  the socket is mode 0700 and stays with the running user.
- No secret in a tape, a shell argument, or a session layout.

== Critical review

Compromises that need explicit user confirmation:

+ #text[Noctalia cannot run on KWin. Resolved.] No Plasma session exists in the
  target: KaOS abandons Plasma next month and the user confirmed option A. The
  nested compositor of option B is withdrawn with it, so no two window managers
  stack.
+ #text[VHS is not the control layer.] If the user meant VHS as the thing that
  operates the kiosk, that cannot be built; VHS only records. TuiOS tapes and
  the JSON protocol are the operating surfaces.
+ #text[One maximized Konsole and a compositor are in tension.] A fullscreen
  Konsole is already a window in a compositor; option A adds a second layer of
  window management inside it. That is acceptable only because TuiOS is the
  window manager the user asked for.
+ #text[Konsole graphic capability is unverified.] TuiOS asks for true color and
  recommends kitty graphics or sixel. Konsole's support for either is
  #text[unverified]; if it supports neither, TuiOS loses images and keeps text,
  which is a degradation the user must accept.
+ #text[Development environment at boot versus TuiOS.] Both want the screen. The
  only compact answer is that the development environment is TuiOS windows, so
  "then TuiOS" becomes "TuiOS, whose layout already holds the development
  windows". The user's phrasing suggests a sequence; the design should confirm
  it is one screen, not two phases.
+ #text[TuiOS is a young, fast-moving project.] Versions and verbs move; a kiosk
  pins one version and treats an upgrade as a work item with a tape that proves
  the surface still works.

Facts still unverified and therefore blocking implementation:

+ Niri nested mode, and Hyprland nested mode inside Konsole.
+ Konsole sixel/kitty graphics support.
+ KaOS package availability for the chosen compositor and for Noctalia.
+ The exact KaOS autologin unit, the display-manager choice, and whether a
  custom `wayland-sessions` entry is honoured.

== Extension inventory

+ A TuiOS event subscription that turns `agent-state` and `attention` into Scale
  values, so Action can wait on a real fact instead of polling `capture-pane`.
+ A layout template per work item: opening a Quest brings its windows.
+ A recording hook that renders the same TuiOS tape through VHS for the
  documentation, so behaviour and evidence stay one artifact.
+ Program Status Protocol reports from the development environment, so a build
  or a test run shows on TuiOS's rail like an agent does.
+ A conformance tape replayed after every TuiOS upgrade, with the expected
  captures as golden files.
+ Hosted panes on a second machine, so the kiosk screen can show work that runs
  elsewhere without a second terminal.