# Windows 11 port plan — Micro Manager (MAUI / .NET 10)

Status: **Phase 2 in progress** — the MAUI app builds and launches via F5 (VS 2026, unpackaged), drives the real Creator Micro 2 over raw HID, and lives in the tray.

## Progress checklist

- [x] Phase 0 — HID spike against real Creator Micro 2 (VID 0x303A / PID 0x8298, fw 0.6.3)
- [x] Phase 1 — `MicroManager.Core` ported, 65/65 tests pass
- [x] Phase 2 (shell) — MAUI app builds + launches via F5
- [x] Phase 2 (device) — `WinrtTransport` wired into `BridgeService`; verified against real hardware (`device.status`, `sys.version`, `v.oai.thstatus` all answered). Also fixed a HID **write deadlock**: the blocking read loop and the writes shared one synchronous handle, so `WriteFile` could block forever — the transport now writes on a dedicated handle.
- [ ] Phase 2 (keymap) — `KeymapManager.ApplyAsync` rejected by the firmware ("keymap not accepted"); agent-key binding not yet working
- [x] Phase 2 (tray) — real tray icon via `H.NotifyIcon.Maui`: generated icon, live tooltip, context menu (Show/Hide Window, Toggle Bridge, Exit), left-click toggles the window. (Window *close* → tray is not wired yet.)
- [ ] Phase 2 (features) — agent-key colours, dial/joystick, on/off switch, macro keys, login item
- [ ] Upstream issue — propose the `windows/` subdirectory before Phase 2 grows
- [ ] Phase 3 (parity) — stack float window, land panel, emulator window, wide-key default
- [ ] Phase 3 (packaging/CI) — unpackaged zip v1, `windows-latest` CI job publishing `MicroManager-win-x64.zip`

Goal: an MVP tray app for Windows 11 that does what the macOS menu-bar app does — light pad keys per Herdr agent state and jump to agents on key press — then reach full parity. MVP first, full parity after. Code lands in `windows/` as a separate solution alongside the Swift package.

## Why this is feasible

- The protocol logic (JSON-RPC framing over 64-byte HID reports, keymap binding, status colours, tab cycle, land plan, pad emulator) is platform-independent Swift → C# port.
- Herdr runs natively on Windows (GA) and exposes the same newline-delimited JSON API over a **named pipe** instead of a Unix socket ([herdr.dev/docs/socket-api](https://herdr.dev/docs/socket-api)). `NamedPipeClientStream` covers it.
- Windows has no Input Monitoring permission gate for HID vendor collections — the app's biggest macOS papercut (signing/ad-hoc hash churn) disappears.

## What ports cleanly (put in `src/MicroManager.Core`, target net10.0)

| Source | Destination | Notes |
|---|---|---|
| `Sources/WLKit/OAIProtocol.swift` | Core | pure framing/JSON; C# port is line-for-line logic |
| `Sources/WLKit/WLDevice.swift` | Core (framing) + Windows transport impl | separate transport interface; pure codec (chunking, brace reassembly, pending RPC ids <1000) goes in Core |
| `Sources/WLKit/HerdrClient.swift` | Core | `UnixDomainSocketEndPoint` (macOS/Linux) / `NamedPipeClientStream` (Windows), one-request-per-connection |
| `Sources/WLKit/StatusMapper.swift` | Core | pure logic |
| `Sources/WLKit/KeyBindings.swift` | Core | config path → `%APPDATA%\MicroManager\config.json` |
| `Sources/WLKit/KeymapManager.swift` | Core | reads/writes keymap over RPC; no OS keymap |
| `Sources/WLKit/PadEmulator.swift` | Core | in-process, zero OS dependency — port first, develop without hardware |
| `Sources/WLKit/AnsiHTML.swift` | Core | pure |
| `Sources/WLKit/GitButler.swift` | Core | shell out to `but`; Windows path search (`LOCALAPPDATA`, scoop, cargo) |
| `Sources/WLKit/HerdrTabCycle*` land logic | Core | pure |

Tests to port (they are the spec): `PadEmulatorTests`, `StatusMapperTests`, `KeyBindingsTests`, `KeymapManagerTests`, `HerdrTabCycleTests`, `GitButlerLandPlanTests`, `AnsiHTMLTests`. Live tests (`LiveHerdrTests`, `LiveDeviceTests`, `LiveGitButlerTests`) skip without hardware/socket — same behavior on Windows. Reuse `Tests/WLKitTests/Fixtures/stock-keymap.json` directly via relative path.

## What must be rebuilt (`src/MicroManager.App`, MAUI net10.0-windows)

- Tray icon: MAUI has no tray API → `H.NotifyIcon.Maui` NuGet package; pad state (dimmed/off, coloured dot, missing/badge)
- Agent keys: full colour semantics incl. breathing blocked state; key press → `agent.focus` → bring terminal forward
- Dial (reasoning effort) + joystick (model switch) against Claude/Codex panes
- On/off switch: stop driving, leave pad bound (macOS behavior)
- Macro keys from `config.json`
- Login item: registry `HKCU\...\Run` (unpackaged v1); StartupTask if MSIX later

Windows-specific gotchas:
- **Focus stealing**: Herdr Windows release notes show it can jump to a pane and bring the terminal window forward — verify `agent.focus` on Windows before writing `SetForegroundWindow` interop.
- **Wide key**: Superwhisper doesn't exist on Windows; right-Win tap opens Start menu. Default to Win+H (voice typing) or make it configurable.
- **Stack window**: needs `WS_EX_NOACTIVATE` Win32 interop so it never steals focus from the terminal.
- **GitButler `but`**: confirm it ships on Windows; degrade stack/land keys gracefully if not.

## Proposed repo layout

```
windows/
  MicroManager.sln
  src/MicroManager.Core/            net10.0 — protocol, Herdr client, emulator, mappers
  src/MicroManager.App/             net10.0-windows MAUI — tray, panels, emulator window
  src/MicroManager.Transport.Winrt/ net10.0-windows — WinRT HID transport (vendor usage page 0xFF00)
  tests/MicroManager.Core.Tests/    xUnit — ported WLKitTests
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

Stack float window, land panel, emulator window, Inspector (deferrable), wide-key default, packaging (unpackaged zip v1; MSIX + cert later), `windows-latest` CI job publishing `MicroManager-win-x64.zip` as an extra artifact. Do **not** touch the existing macOS release workflow.

## PR strategy

Open an issue on the owner's repo proposing the `windows/` subdirectory before Phase 2 grows — it's a structural decision for a Swift-native repo and de-risks effort going to waste.

## First commands on the Windows PC

```powershell
git clone <repo>
cd <repo>/windows
dotnet build src/MicroManager.Core
dotnet test tests/MicroManager.Core.Tests
```

Branch: `windows-port`, sync between Mac and Windows via git.
