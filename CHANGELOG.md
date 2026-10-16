# Changelog

The Windows port follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html),
and this file follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

The version lives in `Directory.Build.props` and is surfaced by the app and the
console harness, so there is nothing else to keep in step.

## [Unreleased]

### Added

- A **lights console** — the Windows counterpart of the macOS **Inspector**'s
  lighting half, and the only place in either implementation where a colour is
  chosen by hand (both menu-bar apps paint from a compiled-in palette). Pick
  keys, set colour, effect, brightness and speed, and apply to the selection, to
  the whole keys zone, or to the underglow, with "all lights off" to end.
- A **take the lights off the bridge** switch. The bridge repaints every 2.5 s,
  so a colour applied underneath it would be gone before it was seen; while the
  switch is on, the bridge keeps tracking Herdr and the keys keep working, but
  it stops painting. (On macOS the Inspector is a separate process and simply
  competes with the bridge, which is why its colours do not last.)

### Changed

- Agents are named by their Herdr **pane title** (`terminal_title_stripped`)
  rather than only by the working directory, so several agents working in one
  checkout are no longer all called `arcOS.Web`. A Herdr too old to report a
  title still falls back to the directory name. The macOS app uses the directory
  name alone, so this is a deliberate divergence.
- The window's agent list now shows that name with the checkout beneath it, and
  its rows jump to the agent when clicked. It had been showing the agent *kind*,
  so a row per Claude agent read `claude` over and over.

## [0.1.0] - 2026-10-15

### Added

- WinRT raw-HID transport for the Creator Micro 2, talking the vendor JSON-RPC
  channel (usage page `0xFF00`) directly.
- Keymap binding over `fs.read`/`fs.write`, so the pad's keys can light
  individually.
- MAUI tray app: generated tray icon, live tooltip, context menu (show/hide,
  toggle bridge, exit) and left-click to toggle the window.
- Console hardware harness (`MicroManager.Console`) that probes the pad and
  applies the keymap.
- In-process pad emulator, reachable with `MICROMANAGER_EMULATOR=1`.
- Semantic versioning for the port, from a single `Directory.Build.props`.
- Key presses, dispatched off the `v.oai.hid` stream: an agent key focuses that
  agent and raises the terminal window, the tab key cycles tabs, and the macro
  keys type their configured text into the focused pane unsubmitted.
- The dial and joystick: effort and model for the focused Claude or Codex pane.
- The pad's lighting mirrored in the window as a row of key swatches, with the
  last action on a status line — the pad itself has no screen.
- Reaching a Herdr server that runs **inside WSL**: an absolute
  `HERDR_SOCKET_PATH` runs `nc -U` in the distro over `wsl.exe`'s stdio, so a
  Linux socket becomes reachable with no relay process and no listening port.
  `MICROMANAGER_WSL_DISTRO` selects the distro when it is not the default.

### Fixed

- Every request to a Herdr in WSL flashed a console window: the app is a GUI
  process with no console of its own, so `wsl.exe` was allocated one, and on the
  key path that is a flash per key press (plus one every poll). The child is now
  created with no window.
- A transport that answered with something that was not JSON — `wsl.exe`
  complaining, say — surfaced as a JSON deserialisation error rather than as its
  own words.
- Disposing a connection could throw while unwinding: the reader closed the
  socket, then the writer's parting flush failed on the closed handle, replacing
  the real error with an `ObjectDisposedException`.
- Requests went out with the platform's line ending (`\r\n` on Windows) instead
  of the protocol's newline.
- `UnixHerdrConnection` never created its writer, so every write on macOS and
  Linux threw `"not connected"`.
- The emulator raised its own notification event but never pushed the input
  report a real pad sends, so key handling could only be tried against hardware.
- A HID write deadlock: the blocking read loop and the writes shared one
  synchronous handle, so `WriteFile` could block forever. The transport now uses
  a dedicated write handle.
- A message longer than one 64-byte report went out as several, so a key press
  handled on the read thread could interleave its frames with the poll loop's
  and leave two unparseable messages on the wire. Sends are now serialised.
- The app project did not allow unsafe blocks, which the CsWinRT AOT source
  generator emits into, so any change to the app's sources failed to compile.
- The emulator's `fs.read` returned `data` as a nested object where the firmware
  returns a JSON *string*, so every emulated read failed to parse.
- The keymap check demanded all four joystick cardinals, which firmware 0.6.3's
  two-sector stick can never satisfy.
- The window bound each agent's status colour to a property that did not exist,
  so the list was always grey.
