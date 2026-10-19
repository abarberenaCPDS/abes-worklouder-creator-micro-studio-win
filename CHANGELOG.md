# Changelog

The Windows Studio follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html),
and this file follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

The version lives in `Directory.Build.props` and is surfaced by the app and the
console harness, so there is nothing else to keep in step.

## [Unreleased]

## [0.4.0] - 2026-10-19

### Added

- Configurable host-side actions for pad keys, dial and joystick, with a physical
  Controls map, grouped input editor, bulk apply/reset, test, and profile-aware
  availability notes. Actions are host-side and do not rewrite the device keymap.
- Herdr actions for tabs/workspaces, pane focus/zoom, prompts, key injection,
  effort/model controls and Herdr CLI commands. CLI placeholders and ask prompts
  resolve at press time, and arguments are launched directly without a shell.
- Windows system actions for shortcuts, scrolling, media keys, typing, Voice
  Typing (`Win+H`), open targets and configured PowerShell commands. Hardware/
  OS limitations report clearly (for example dial push may not emit AG19).
- A Studio sidebar with Studio, Controls, Lighting, Herdr and Inspector pages.
- Persistent Studio lighting preferences with preview, debounce-save, Save now,
  Revert and Restore defaults. Inspector/manual paints remain temporary; leaving
  Inspector hands the pad back to Studio's saved look.
- A Windows app/executable, splash and tray icon based on the Creator Micro 2
  artwork. Regenerate the multi-size `.ico` with `scripts/Generate-StudioIcon.ps1`.

### Changed

- Default prompts no longer mention GitButler. The former stack/land keys now
  default to Focus Agent and can be assigned any supported action in Controls.
- Rebranded as **Abe's WorkLouder Creator Micro Studio for Windows**. Renamed
  the solution, project folders, assemblies and C# namespaces to the
  `AbesWorkLouderCreatorMicroStudio.Windows.*` identity. Existing
  `MICROMANAGER_*` environment variables remain compatibility aliases.
- Studio settings now live in `creator-micro-studio/config.json`; the previous
  `micromanager/config.json` path is read as a fallback and migrates on first
  write without deleting the original.

### Fixed

- Herdr CLI actions from a WSL-backed Studio now resolve `WL_HERDR_PATH` (including
  `~`) inside the selected distro and invoke the executable by absolute path.
  Previously `env herdr` relied on WSL's noninteractive `PATH`, which omitted
  `~/.local/bin` and produced “env herdr: no such file or directory.”

## [0.3.0] - 2026-10-16

### Added

- **Reconnection.** The port now notices the pad leaving and opens it again.
  `WinrtTransport` previously did not notice at all: its read loop retried a
  failed read forever while the handle stayed non-zero, so `IsConnected` reported
  a pad that was gone and the only symptom was a poll error every 2.5 s. A failed
  read or write against a removed device is now taken as the pad leaving, and a
  supervisor re-enumerates and reopens it (1 s, doubling to 10 s) — which is what
  a Bluetooth pad going to sleep needs. Launching with no pad also keeps looking,
  so plugging one in now starts working without a restart. The emulator is
  deliberately **not** a reconnect fallback: a phantom pad on the lights is worse
  than a window that says the pad is gone.
- **Scrolling the pane, not the application.** The dial scrolls through Herdr's
  `pane.scroll`, so a detent moves the viewport by an exact number of lines
  (`"scroll": { "lines": 5 }`) and asks nothing of whatever is running in the
  pane — five lines a detent, a nudge past the three a wheel notch covers.
  Turned right the viewport moves **down** towards the newest output and turned
  left up through the older output, the way rolling a wheel does. It follows that
  it only reaches panes drawing to the **normal screen**, whose rows are in
  Herdr's scrollback. An alternate-screen TUI leaves nothing to move, and nothing
  can be sent to it either: Herdr has no mouse-wheel event anywhere in its
  protocol and
  no page key — `pageup` and every spelling of it come back `unsupported key`.
  Such a pane gets a status line saying so. The ways out are `/tui default` in
  Claude Code, `[tui] alternate_screen = "never"` in Codex, or naming the
  application's own line-scroll keys in `"up"` / `"down"`.

### Changed

- The dial now **scrolls the focused pane** instead of stepping reasoning effort —
  five lines per detent rather than one, and no longer dependent on the agent
  understanding arrow keys. Scrolling is not agent-specific, so the dial now does
  something in any agent's pane rather than only in Claude and Codex ones, and long
  output is what a dial is good at. `"dial": "effort"` in the config file puts the
  old behaviour back.
- The dial's press slot is **given back**. An earlier build of this port bound it
  to `AG19` to use the click itself; the app no longer listens for that code, so a
  pad still carrying it gets the stock play/pause media key restored on the next
  connect and the slot is left alone from then on.

### Fixed

- The dial no longer sends `pageup` / `pagedown` where a pane keeps no
  scrollback. Herdr rejects both as `unsupported key`, so every detent in a
  full-screen pane failed and the dial looked dead. Nothing it accepts scrolls an
  application-owned viewport: the protocol carries no mouse-wheel event and no
  page key. Such panes now say so on the status line, and [the dial
  documentation](docs/using-the-pad.md#the-dial) names the way out.
- The window's status line now names the **pad emulator** when no pad could be
  opened, instead of reading "Connected" either way. The fallback warning was
  raised before the connected event that overwrote it, which left the window
  unable to answer "is this the real pad?" — the question that made Bluetooth
  support hard to confirm.

## [0.2.0] - 2026-10-16

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
- Console hardware harness (`CreatorMicroStudio.Console`) that probes the pad and
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
