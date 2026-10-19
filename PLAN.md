# Windows 11 port plan — Abe's WorkLouder Creator Micro Studio for Windows (MAUI / .NET 10)

Status: **Phase 2 in progress** — the MAUI app builds and launches via F5 (VS 2026, unpackaged), drives the real Creator Micro 2 over raw HID, binds its agent keymap, lives in the tray, and acts on key presses (agent key → `agent.focus` + raise the terminal). The Herdr connection is wired up too, including a server running inside WSL, and has been confirmed end-to-end on the real pad.

## Studio controls and persistent profile work (0.4.0)

This additive feature track brings the Xcode Studio's configurable controls and
sidebar organization to Windows, while retaining Windows-specific device and
system behavior. “Profile” means one saved Studio configuration (`config.json`),
not multiple named profiles or the pad's firmware profile. Do not commit the
supplied `abes-worklouder-creator-micro-studio/` reference directory.

The Windows product identity is **Abe's WorkLouder Creator Micro Studio for
Windows** and the intended repository slug is
`abes-worklouder-creator-micro-studio-win`. Assemblies and C# namespaces use the
`AbesWorkLouderCreatorMicroStudio.Windows.*` prefix. Windows-owned app, tray and
splash assets do not depend on the untracked Xcode reference folder. The GitHub
remote rename is an external action and is not performed by this worktree.

- [x] Phase 0 — compare the reference docs/code with the Windows port; confirm
  one persistent config, a safe legacy-config migration, and record dial-push/
  joystick hardware limitations.
- [x] Branding implementation — renamed solution, project folders, assemblies
  and C# namespaces; updated product metadata, app/window/tray labels, docs and
  the app/splash/tray icons. A local PowerShell script regenerates the ICO from
  the Windows-owned art; existing `MICROMANAGER_*` environment aliases remain.
- [ ] Branding validation — launch through Visual Studio F5 and confirm the
  executable icon, splash, tray icon and window title on Windows.
- [x] Phase 1 foundation — add the typed host-side action catalog, canonical
  physical input descriptors, atomic config read/modify/write, and core tests.
- [x] Phase 1 integration — action overrides are dispatched by `BridgeService`;
  legacy config forms/aliases remain readable, the active config lives under
  `creator-micro-studio`, and old `micromanager` settings migrate on first write.
- [x] Phase 2 implementation — centralized dispatch now covers Herdr navigation,
  prompts, tune/model, system actions, launch actions and argument-array Herdr
  CLI commands. Core parser/config tests pass; live platform behavior still
  needs F5/emulator validation.
- [ ] Add fake-based dispatcher/system-input tests and verify the action set
  from the Controls page with an emulator-generated input.
- [x] Phase 3 implementation — Studio sidebar destinations and physical-map
  Controls UI support action editing, bulk apply, reset, test, and profile
  capability indicators. F5 visual validation remains.
- [x] Phase 4 implementation — Studio lighting settings persist and repaint;
  the Inspector/manual-lighting page is temporary and hands the pad back when
  leaving. F5/hardware validation remains.
- [x] Phase 5 implementation — Herdr status/mapping page and usage/config docs
  are in place. Remaining: F5 visual pass, real pad/Herdr action verification,
  and final acceptance checklist. Current SemVer is 0.4.0; the GitHub remote
  rename remains external to this worktree.

### Verification so far

- `dotnet build AbesWorkLouderCreatorMicroStudio.Windows.sln` succeeds (existing
  nullable/obsolete warnings remain).
- `dotnet test tests\CreatorMicroStudio.Core.Tests\CreatorMicroStudio.Core.Tests.csproj --filter "FullyQualifiedName!~HerdrWslTests"` passes: 158 tests.
- Setting `$env:MICROMANAGER_EMULATOR = "1"` and running
  `dotnet run --no-build --project src\CreatorMicroStudio.Console` passes the
  emulator HID/keymap smoke test.
- `HerdrWslTests` is live when the environment selects a WSL socket; the current
  configured server returned an empty agent list. Re-run against a real active
  Herdr session before closing Phase 5.
- The WSL Herdr CLI path-resolution test passes with `WL_HERDR_PATH=~/.local/bin/herdr`;
  `herdr tab rename --help` confirms the CLI syntax is `tab rename <TAB_ID> <LABEL>...`.
- Launching the MAUI window and visually checking the packaged taskbar/tray icon
  still requires **F5 in Visual Studio 2026**; a shell launch is not a valid UI
  check for this app.

Progress details: `C:\Users\Abe.Barberenagil\.opencode\plan\worklouder-studio-windows.md`.

## Progress checklist

- [x] Phase 0 — HID spike against real Creator Micro 2 (VID 0x303A / PID 0x8298, fw 0.6.3)
- [x] Phase 1 — `CreatorMicroStudio.Core` ported, 68/68 tests pass
- [x] Phase 2 (shell) — MAUI app builds + launches via F5
- [x] Phase 2 (device) — `WinrtTransport` wired into `BridgeService`; verified against real hardware (`device.status`, `sys.version`, `v.oai.thstatus` all answered). Also fixed a HID **write deadlock**: the blocking read loop and the writes shared one synchronous handle, so `WriteFile` could block forever — the transport now writes on a dedicated handle.
- [x] Phase 2 (keymap) — `KeymapManager.ApplyAsync` now completes against the real pad (reports "already applied" on the pad, and applies cleanly on the emulator). Two bugs fixed: the emulator returned `fs.read`'s `data` as a nested object where the firmware — and the Swift emulator — return a JSON **string**, so every emulated read failed to parse; and the joystick check demanded all four cardinal sectors, but firmware 0.6.3's radial stick exposes only two (0.25/0.75), so the check could never pass. The check now verifies the cardinals the device actually exposes (an exposed cardinal with a wrong code still fails).
- [x] Phase 2 (tray) — real tray icon via `H.NotifyIcon.Maui`: generated icon, live tooltip, context menu (Show/Hide Window, Toggle Bridge, Exit), left-click toggles the window. (Window *close* → tray is not wired yet.)
- [x] Tooling — SemVer from one source (`Directory.Build.props`, surfaced by the app and the console harness), `CHANGELOG.md`, a `README.md` with badges, and a repo-root `AGENTS.md`
- [x] Phase 2 (keys) — key presses are dispatched off the `v.oai.hid` notification stream: an agent key calls `agent.focus` and raises the terminal window, the tab key cycles tabs, the macro keys type the configured text into the focused pane unsubmitted, and the switch clears the lights while leaving the keymap bound. Painting is skipped unless a fingerprint of the whole picture changes.
- [x] Phase 2 (tune) — the dial **scrolls the focused pane** by default: five lines a detent, moving the pane's own viewport over `pane.scroll` wherever Herdr holds the output (anything drawing to the normal screen), and doing nothing where it does not (a full-screen TUI's alternate-screen rows never reach the host's scrollback, and Herdr can send neither a wheel event nor a page key — `pageup` is rejected as `unsupported key`; `/tui default` is the way out). Turned right it moves down towards the newest output, turned left up. `"dial": "effort"` puts it back on the Claude effort ladder (`/effort`) or the Codex chords, and the dial's **press is left alone** as macOS leaves it — the encoder's third slot stays the play/pause media key. The joystick cycles the Claude model list (`/model`) or drives the Codex picker. Agents are named by their Herdr pane title rather than the working directory, so several agents sharing one checkout are distinguishable — the macOS app stops at the directory, so this is a deliberate divergence; the tray window lists that name over the checkout and its rows jump to the agent.
- [x] Phase 2 (lights) — a **lights console** drives the pad by hand: the macOS **Inspector**'s colour half (pick keys, colour, effect, brightness, speed; apply to the selection, the keys zone or the underglow; all lights off). Applying takes the lights off the bridge with a "take the lights off the bridge" switch, because the 2.5 s poll would repaint over them before they were seen; the bridge keeps tracking Herdr and the keys keep working while it is on. The payload building is `LightingEditor` in Core (11 tests); the panel is MAUI and lives by F5, like the rest of the window.
- [x] Phase 2 (emulator) — the pad emulator now pushes `v.oai.hid` notifications as input reports in the abbreviated `m`/`p` envelope, so the key path is reachable without hardware. Concurrent RPC sends are serialised at the transport, so a key press handled on the read thread cannot interleave its frames with the poll loop's.
- [x] Phase 2 (Herdr transport) — a Herdr server **inside WSL** is now reachable: an absolute `HERDR_SOCKET_PATH` runs `nc -U` in the distro over `wsl.exe`'s stdio, with `MICROMANAGER_WSL_DISTRO` selecting the distro, so no relay process and no port. Exercised end-to-end from the real pad (F5 against a running Herdr 0.9.5 session in WSL: agents listed, key presses acted on); `HerdrWslTests` covers the transport choice, the live call, and both failure modes. Three latent bugs fell out of it: the platform line ending (`\r\n`) went out instead of the protocol's newline, disposing a connection could throw over the real error (the reader closed the handle, then the writer's flush failed on it), and a transport that complained in non-JSON surfaced as a deserialisation error rather than its own words. The **named-pipe** branch is still unexercised against a real Windows Herdr and the pipe name it creates is unknown, so the app's default Unix-shaped path will not resolve to one.
- [x] Phase 2 (reconnect) — the pad going away is noticed and reopened. A failed read or write against a removed device is taken as a disconnect (it used to be retried forever with the handle still open, so `IsConnected` reported a pad that had gone), and a supervisor re-enumerates and reopens with a 1–10 s backoff. Unplugging and a Bluetooth pad dropping off look the same from here, so one path covers both, and launching without a pad keeps looking — plugging one in just starts working. The emulator is deliberately not a reconnect fallback.
- [ ] Phase 2 (login item) — start with Windows via `HKCU\...\Run`
- [ ] Upstream issue — propose the `windows/` subdirectory before Phase 2 grows
- [ ] Phase 3 (parity) — stack float window, land panel, emulator window, wide-key default. The lights console covers the macOS Inspector's colour and effect controls only; its traffic log, raw JSON-RPC console, key walk and "resend whole pad" are still unported.
- [ ] Phase 3 (packaging/CI) — unpackaged zip v1, `windows-latest` CI job publishing `AbesWorkLouderCreatorMicroStudio-win-x64.zip`
- [x] Phase 4 (Bluetooth) — no work needed: it already works. `WinrtTransport` matches on vendor + usage pair over the *HID class*, never on the bus, so it finds the vendor collection over USB and Bluetooth alike; verified on the real pad with the cable out (`Connected via win32-hid`, `battery: 99`, `is_charging: false`). What was missing was not Bluetooth support but **recovery**, and that has since landed: a drop is noticed and the pad reopened. See [Phase 4](#phase-4--bluetooth).

Goal: an MVP tray app for Windows 11 that does what the macOS menu-bar app does — light pad keys per Herdr agent state and jump to agents on key press — then reach full parity. MVP first, full parity after. Code lands in `windows/` as a separate solution alongside the Swift package.

## Why this is feasible

- The protocol logic (JSON-RPC framing over 64-byte HID reports, keymap binding, status colours, tab cycle, land plan, pad emulator) is platform-independent Swift → C# port.
- Herdr runs natively on Windows (GA) and exposes the same newline-delimited JSON API over a **named pipe** instead of a Unix socket ([herdr.dev/docs/socket-api](https://herdr.dev/docs/socket-api)). `NamedPipeClientStream` covers it. A Herdr server inside **WSL** is the case that actually comes up on a dev box, and it serves a *Linux* socket — reached through `nc -U` over `wsl.exe` (see the checklist).
- Windows has no Input Monitoring permission gate for HID vendor collections — the app's biggest macOS papercut (signing/ad-hoc hash churn) disappears.

## What ports cleanly (put in `src/CreatorMicroStudio.Core`, target net10.0)

| Source | Destination | Notes |
|---|---|---|
| `Sources/WLKit/OAIProtocol.swift` | Core | pure framing/JSON; C# port is line-for-line logic |
| `Sources/WLKit/WLDevice.swift` | Core (framing) + Windows transport impl | separate transport interface; pure codec (chunking, brace reassembly, pending RPC ids <1000) goes in Core |
| `Sources/WLKit/HerdrClient.swift` | Core | `UnixDomainSocketEndPoint` (macOS/Linux) / `NamedPipeClientStream` (Windows), one-request-per-connection |
| `Sources/WLKit/StatusMapper.swift` | Core | pure logic |
| `Sources/WLKit/KeyBindings.swift` | Core | Studio config: `XDG_CONFIG_HOME` else `%USERPROFILE%\.config\creator-micro-studio\config.json`; reads the old `micromanager` path as a migration fallback |
| `Sources/WLKit/KeymapManager.swift` | Core | reads/writes keymap over RPC; no OS keymap |
| `Sources/WLKit/PadEmulator.swift` | Core | in-process, zero OS dependency — port first, develop without hardware |
| `Sources/WLKit/AnsiHTML.swift` | Core | pure |
| `Sources/WLKit/GitButler.swift` | Core | shell out to `but`; Windows path search (`LOCALAPPDATA`, scoop, cargo) |
| `Sources/WLKit/HerdrTabCycle*` land logic | Core | pure |

Tests to port (they are the spec): `PadEmulatorTests`, `StatusMapperTests`, `KeyBindingsTests`, `KeymapManagerTests`, `HerdrTabCycleTests`, `GitButlerLandPlanTests`, `AnsiHTMLTests`. Live tests (`LiveHerdrTests`, `LiveDeviceTests`, `LiveGitButlerTests`) skip without hardware/socket — same behavior on Windows. Reuse `Tests/WLKitTests/Fixtures/stock-keymap.json` directly via relative path.

## What must be rebuilt (`src/CreatorMicroStudio.App`, MAUI net10.0-windows)

- Tray icon: MAUI has no tray API → `H.NotifyIcon.Maui` NuGet package; pad state (dimmed/off, coloured dot, missing/badge)
- Agent keys: full colour semantics incl. breathing blocked state; key press → `agent.focus` → bring terminal forward
- Dial (reasoning effort) + joystick (model switch) against Claude/Codex panes
- On/off switch: stop driving, leave pad bound (macOS behavior)
- Macro keys from `config.json`
- Login item: registry `HKCU\...\Run` (unpackaged v1); StartupTask if MSIX later

Windows-specific gotchas:
- **Focus stealing**: `agent.focus` selects the pane but does not bring the terminal forward, so the port raises the window itself (`SetForegroundWindow`, attached to the foreground thread's input queue — a background process cannot just take focus). The terminal is found by executable name; override with `WL_TERMINAL_PROCESS`.
- **Herdr in WSL**: a Linux socket inside a WSL2 VM cannot be opened from Windows at all, and `HERDR_SOCKET_PATH` only overrides the path string, not the transport. The port therefore reads the *shape* of that path: absolute Unix path → run `nc -U` in the distro over `wsl.exe`'s stdio; anything else → a named pipe. Costs one process spawn per request, which is fine because Herdr serves one request per connection and closes it. Needs `nc -U` in the distro (`nmap-ncat`).
- **Joystick travel**: firmware 0.6.3 exposes only two stick sectors (bound to AG15/AG17, i.e. north/south), so the Codex picker can be opened and moved but its east/west confirm and cancel are unreachable on this firmware. Claude's model list only needs the vertical deflections, so it is unaffected.
- **Wide key**: Superwhisper doesn't exist on Windows; right-Win tap opens Start menu. Default to Win+H (voice typing) or make it configurable.
- **Stack window**: needs `WS_EX_NOACTIVATE` Win32 interop so it never steals focus from the terminal.
- **GitButler `but`**: confirm it ships on Windows; degrade stack/land keys gracefully if not.

## Proposed repo layout

```
windows/
  AbesWorkLouderCreatorMicroStudio.Windows.sln
  src/CreatorMicroStudio.Core/            net10.0 — protocol, Herdr client, emulator, mappers
  src/CreatorMicroStudio.App/             net10.0-windows MAUI — tray, panels, emulator window
  src/CreatorMicroStudio.Transport.Winrt/ net10.0-windows — WinRT HID transport (vendor usage page 0xFF00)
  src/CreatorMicroStudio.Console/         hardware smoke test
  src/CreatorMicroStudio.HidSpike/        transport probe
  tests/CreatorMicroStudio.Core.Tests/    xUnit — ported WLKitTests
  build.ps1 / publish.ps1           optional zip packaging scripts
```

Core stays `net10.0` so `dotnet test` runs on your Mac. Only the App project needs Windows to build. The transport interface lives in Core; the WinRT implementation references `Windows.Devices.HumanInterfaceDevice`.

## Phase 0 — Spike on Windows PC (half day, kill unknowns)

1. **WinRT HID**: enumerate `VID_303A`, find usage-page `0xFF00` / usage `0x01` collection, send one lighting payload (report ID `0x06`, 64-byte reports), receive one input report. Fallback: `HidSharp` / raw hid.dll.
2. **Herdr named pipe**: connect via `HERDR_SOCKET_PATH` (or default under `%APPDATA%\herdr`), send `agent.list\n`, parse agents. Confirm one-request-per-connection semantics match macOS.

Exit criteria: Core compiles on Mac (`dotnet build`), spike works on Windows.

## Phase 1 — Core port (mostly on your Mac)

Port in dependency order, test-first against the existing suites:

1. PadEmulator + framing codec round-trip tests
2. StatusMapper, KeyBindings, KeymapManager (reuse `Fixtures/stock-keymap.json`)
3. HerdrClient with dual transports + tab-cycle + land-plan logic
4. GitButler runner with Windows path search

Exit criteria: `dotnet test` green on macOS; emulator drives a fake end-to-end loop.

## Phase 2 — MVP app shell (Windows PC)

MAUI tray app per the "What must be rebuilt" list. Run against real Herdr + real pad (USB passthrough in VM if needed, but emulator covers development).

## Phase 3 — Parity

Stack float window, land panel, emulator window, Inspector (deferrable), wide-key default, packaging (unpackaged zip v1; MSIX + cert later), `windows-latest` CI job publishing `AbesWorkLouderCreatorMicroStudio-win-x64.zip` as an extra artifact. Do **not** touch the existing macOS release workflow.

## Phase 4 — Bluetooth

**Verified on the real pad — there is nothing to implement.** The port already
drives the pad over Bluetooth, with no Bluetooth-specific code anywhere, and that
is what the transport was built to do rather than luck.

`WinrtTransport.FindDevicePath` does *not* enumerate USB. It walks the **HID
class** with SetupAPI and accepts an interface only when `HidD_GetAttributes`
reports vendor `0x303A` **and** `HidP_GetCaps` reports usage page `0xFF00` /
usage `1`. It never checks the product id and never looks at the bus, so it finds
the vendor collection over USB and over HID-over-GATT alike.

Confirmed by the console harness with the cable out, on battery:

```
Connected via win32-hid
device.status: {"version":"0.6.3","profile_index":1,"layer_index":1,"battery":99,"is_charging":false}
```

`battery: 99` / `is_charging: false` is the corroboration — the link cannot be
USB. macOS arrives at the same place by matching on vendor alone, for the same
reason: over Bluetooth the pad is a *single* HID device whose primary usage is
keyboard, with the vendor collection alongside it in its usage pairs
(`Sources/WLKit/WLDevice.swift:143-161`, `docs/hacking.md:78-85`).

So there is no Bluetooth feature to build. What was left was not support but
**recovery**, and both halves of it have since landed:

- **Reconnection.** `WinrtTransport` used to retry a failed read forever with the
  handle still open, so `IsConnected` reported a pad that had gone and the only
  symptom was a poll error every 2.5 s. It now takes a failed read or write
  against a removed device as the pad leaving, and `BridgeService`'s supervisor
  re-enumerates and reopens it — which is what a sleeping pad needs. Launching
  with no pad keeps looking too, so plugging one in starts working.
- **The fallback that hid itself.** The emulator fallback announced itself with an
  error that `OnDeviceConnected` overwrote one line later, so the window read
  `Connected` with no pad attached at all — which is exactly why this question
  needed a console harness to settle. The status line now names the emulator
  (`BridgeService.UsingEmulator`).

Neither is a Bluetooth change; both were latent on USB as well.

## PR strategy

Open an issue on the owner's repo proposing the `windows/` subdirectory before Phase 2 grows — it's a structural decision for a Swift-native repo and de-risks effort going to waste.

## First commands on the Windows PC

```powershell
git clone <repo>
cd <repo>/windows
dotnet build src/CreatorMicroStudio.Core
dotnet test tests/CreatorMicroStudio.Core.Tests
```

Branch: `windows-port`, sync between Mac and Windows via git.
