# AGENTS.md

Abe's WorkLouder Creator Micro Studio for Windows — a .NET 10 / MAUI app that
lights each running [Herdr](https://herdr.dev) agent on its own key of a Work
Louder Creator Micro 2 (vendor `0x303A`), and jumps to that agent when the key
is pressed. This repository contains only the Windows app.

- `src/CreatorMicroStudio.Core` — shared logic: pad protocol, keymap, lighting
  palette, Herdr client and host-side actions. Targets `net10.0` on purpose.
- `src/CreatorMicroStudio.App` — the MAUI app; only builds on Windows.
- `src/CreatorMicroStudio.Transport.Winrt` — the WinRT HID transport.
- `src/CreatorMicroStudio.Console` — the hardware smoke-test harness.
- `src/CreatorMicroStudio.HidSpike` — the raw transport probe.
- `tests/CreatorMicroStudio.Core.Tests` — the test suite (the spec).

Assemblies and namespaces use the `AbesWorkLouderCreatorMicroStudio.Windows.*`
prefix.

## Commands

From the repository root:

```powershell
dotnet build AbesWorkLouderCreatorMicroStudio.Windows.sln
dotnet test tests\CreatorMicroStudio.Core.Tests\CreatorMicroStudio.Core.Tests.csproj
dotnet run --project src\CreatorMicroStudio.Console    # hardware smoke test
```

- Single test: `dotnet test <csproj> --filter FullyQualifiedName~KeymapManagerTests`.
- `CreatorMicroStudio.Core` targets `net10.0`, so the test suite also runs on
  macOS and Linux. `CreatorMicroStudio.App` is MAUI and only builds on Windows —
  launch it with **F5** in Visual Studio 2026, not by starting the `.exe` (a shell
  launch leaves the window hidden, which is an artifact of that context, not a
  bug).
- `HerdrWslTests` calls a live Herdr server when `HERDR_SOCKET_PATH` selects a WSL
  socket; for local runs without that service, use
  `--filter "FullyQualifiedName!~HerdrWslTests"`.
- No hardware: set `WL_EMULATE=1` to run against the in-process pad emulator.

## Device protocol traps

Full write-up in `docs/hacking.md`; these are the ones that fail *silently*:

- **Notifications use `{m, p}`; responses use `{method, params, id}`.** Matching
  only on `method` drops every key press without an error.
- The firmware answers `{"ok":1}` to almost any payload, malformed included.
  The LEDs are the only ground truth.
- Per-key colour requires the key to be bound to `KV_OAI_AG*` **on the active
  layer**; `v.oai.thstatus` still returns `{"ok":1}` for a key it cannot light.
- The keymap file is **double-encoded**: `fs.read` → `{"data": "<json string>"}`.
  `activeProfileId` selects the profile; the active layer is `layers[0]`.
- Call ids must be under 1000.
- Key index is row-major over a `[2, 4, 4, 3]` matrix, and **row 0 is reversed**
  (index 0 is the top-*right* key). Reading order lives in `Pad` in
  `src/CreatorMicroStudio.Core/Pad/Oai.cs`.
- **Writes must not use the HID read handle.** `WinrtTransport` keeps a dedicated
  write handle; sharing one synchronous handle deadlocks `WriteFile` behind the
  blocking read. A message can also span several 64-byte reports, so two callers
  must never interleave theirs — `RpcDevice` serialises the send half of a call
  (never the wait for the reply) for that reason.
- The emulator reproduces the firmware's awkward behaviours deliberately. Do not
  "fix" those; they are what makes it useful. It does push `v.oai.hid`
  notifications as input reports, so the key path is reachable with no hardware.

## Divergences worth knowing

- The Studio config path is `XDG_CONFIG_HOME` else
  `%USERPROFILE%\.config\creator-micro-studio\config.json` (not `%APPDATA%`);
  old `micromanager/config.json` files are read as a fallback and migrate on
  first write without deleting the source.
- Firmware 0.6.3 exposes only two joystick sectors (0.25/0.75), so
  `KeymapManager` only requires the cardinals the device actually reports.
- Agents are named by the Herdr pane title (`terminal_title_stripped`), not by
  the working directory the way the original macOS app's `shortName` did — agents
  sharing one checkout are otherwise indistinguishable. The directory is still
  available as `HerdrAgent.DirectoryName`.
- The lighting **palette** is compiled in: `BridgeConfig` holds the state colours,
  effects, brightness, speed and worst-state priority, and nothing loads them from
  a file. The only hand-driven colour paths are the lights console
  (`LightingEditor` in Core plus `LightingViewModel` in the app).
- The pad works over **USB and Bluetooth**, with no Bluetooth-specific code:
  `WinrtTransport` matches on vendor + usage pair over the *HID class*, so it
  never looks at the bus (verified on the real pad on battery —
  `Connected via win32-hid`) — over Bluetooth the pad is one HID device whose
  primary usage is keyboard (`docs/hacking.md` has the trap).
- A **removed device fails reads and writes** rather than blocking, which is the
  only signal the port gets that the pad has gone; `WinrtTransport` treats it as
  a disconnect and `BridgeService`'s supervisor reopens the pad. A transport that
  ignores that looks identical to a working one: the handle stays non-zero, so
  `IsConnected` lies. Both unplugging and a Bluetooth drop look the same.
- The dial **scrolls** the focused pane by default rather than stepping reasoning
  effort, because scrolling is not agent-specific; the old behaviour is one config
  line away (`"dial": "effort"`). It moves the pane's own viewport over Herdr's
  `pane.scroll` — exact lines, needing nothing of the application — which means it
  only reaches a pane drawing to the **normal screen**, whose rows are in Herdr's
  scrollback. An alternate-screen TUI — every full-screen agent by default — is
  out of reach: those rows never enter the host's scrollback, Herdr can synthesise
  no mouse wheel, and its key vocabulary has no page key (`pageup` and every
  spelling are `unsupported key`). Claude Code's `/tui default` and Codex's
  `[tui] alternate_screen = "never"` are what put an agent's output back in reach;
  the port says so on the status line rather than sending a guess. Turned right it
  moves the viewport down towards the newest output, turned left up. The dial's
  press is left alone, as the original macOS app leaves it: the encoder's third
  slot stays the play/pause media key a stock pad ships there.

## Conventions

- **Conventional Commits.** Commits are single-line and scoped:
  `type(windows): subject`. Types in use: `feat`, `fix`, `test`, `docs`, `chore`,
  `ci`.
- The app is versioned with SemVer from a single source,
  `Directory.Build.props`; record changes in `CHANGELOG.md`.
- Studio environment variables use the `WL_` prefix (`WL_EMULATE`,
  `WL_WSL_DISTRO`, `WL_HERDR_PATH`, `WL_TERMINAL_PROCESS`); `StudioEnvironment` in
  Core is the one place they are named. They used to be `MICROMANAGER_*` and were
  renamed in 0.5.0 — do not reintroduce the old names. Herdr's own
  `HERDR_SOCKET_PATH` and `XDG_CONFIG_HOME` keep their upstream names.
- Never commit the untracked `abes-worklouder-creator-micro-studio/` reference
  directory, and do not change the Git remote without explicit instruction.
- `PLAN.md` is the checklist and state of play — keep it current when you land or
  change scope.
- Keep `tests/CreatorMicroStudio.Core.Tests` green; those tests are the spec.
