# Abe's WorkLouder Creator Micro Studio for Windows

![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![UI](https://img.shields.io/badge/UI-.NET%20MAUI-512BD4)
![Version](https://img.shields.io/badge/version-0.4.1-blue)
![SemVer](https://img.shields.io/badge/SemVer-2.0.0-3E9392)
![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-FE5196)

A Windows 11 tray app that lights each running [Herdr](https://herdr.dev) agent
on its own key of a Work Louder
**[Creator Micro 2](https://worklouder.cc/creator-micro-2)**, and jumps to that
agent when you press the key.

It drives the pad the same way the macOS app does — Herdr's API on one side, a
raw-HID JSON-RPC channel on the other — with no Node and no daemon.

> **Status:** the branded app, configurable actions, sidebar pages and persistent
> Studio lighting build. F5 visual verification and live-device/action checks
> remain; [PLAN.md](PLAN.md) tracks progress.

The executable, app window, splash screen and tray icon use the Abe's WorkLouder
Creator Micro Studio identity. Regenerate the Windows `.ico` from the checked-in
artwork with `./scripts/Generate-StudioIcon.ps1` from `windows/`.

## Requirements

- Windows 11 (build `10.0.19041` or newer)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Creator Micro 2 and a running Herdr server for anything beyond the emulator —
  on Windows that usually means [Herdr inside WSL](#connecting-to-herdr)
- Visual Studio 2026 to launch the MAUI app — **F5** is the supported path

## Build, test, run

```powershell
cd windows
dotnet build AbesWorkLouderCreatorMicroStudio.Windows.sln
dotnet test tests\CreatorMicroStudio.Core.Tests\CreatorMicroStudio.Core.Tests.csproj
dotnet run --project src\CreatorMicroStudio.Console   # hardware smoke test
```

The WSL integration test talks to a live Herdr server when `HERDR_SOCKET_PATH`
selects a WSL socket; for a no-live-service test run, exclude it explicitly:

```powershell
dotnet test tests\CreatorMicroStudio.Core.Tests\CreatorMicroStudio.Core.Tests.csproj --filter "FullyQualifiedName!~HerdrWslTests"
```

`CreatorMicroStudio.Core` targets `net10.0`, so `dotnet test` also runs on macOS.
`CreatorMicroStudio.App` is a MAUI app and only builds on Windows; it is the
solution's startup project.

## No pad to hand

```powershell
$env:WL_EMULATE = "1"
dotnet run --project src\CreatorMicroStudio.Console
```

The old `MICROMANAGER_EMULATOR` and `MICROMANAGER_WSL_DISTRO` names were retired in
0.5.0 and are no longer read.

The emulator stands in for the firmware and reproduces its awkward habits on
purpose, so a bug it shows you is one you would have met on hardware — see the
[top-level README](../README.md#no-pad-to-hand).

## Connecting to Herdr

Herdr serves its API on a named pipe on Windows, but a Herdr server running
**inside WSL** listens on a Linux socket that no Windows process can open. The
app bridges that by running `nc -U` in the distro and speaking the protocol over
`wsl.exe`'s stdio — no relay process, no listening port, nothing left running on
the Linux side.

The shape of `HERDR_SOCKET_PATH` picks the transport: an absolute Unix path means
WSL, anything else means a Windows pipe.

```powershell
$env:HERDR_SOCKET_PATH = '/home/you/.config/herdr/herdr.sock'
$env:WL_WSL_DISTRO = 'AlmaLinux-9'   # only if not your default distro
```

That needs `nc` with `-U` in the distro (`nmap-ncat`) and an absolute path — `~`
is not expanded, because no shell is involved. The named-pipe case for Herdr
running natively on Windows, and the failure modes of both, are in
**[docs/using-the-pad.md](docs/using-the-pad.md#reaching-herdr)**.

## Using the pad

Key-by-key layout, the colour table, how the pad gets bound to the app, the
config file and troubleshooting are in
**[docs/using-the-pad.md](docs/using-the-pad.md)** — the Windows walk-through of
driving the real hardware.

## Studio pages and controls

The Studio window now has a left navigation rail for **Studio**, **Controls**,
**Lighting**, **Herdr**, and **Inspector**. Controls assigns host-side actions to
the physical keys, dial and joystick; it never writes those choices to the
pad's flash keymap. The map follows the hardware's reversed top row and marks
joystick sectors or dial push unavailable when the active profile does not
report them.

The action set includes Herdr focus/tab/workspace/pane operations, key injection,
prompts, Herdr CLI commands, model/effort controls, keyboard shortcuts, scroll,
media, typing, open targets and PowerShell commands. Windows Voice Typing uses
`Win+H`; it is not Superwhisper. See
**[Using the pad](docs/using-the-pad.md)** for the action catalog and limitations.

**Lighting** stores the Studio profile in `config.json` and reapplies it after
restart/reconnect. Preview edits save after a short idle delay, with Save now
and Revert available. **Inspector** manual lighting is temporary; leaving that
page returns control to Studio's saved preferences.

## Configuration

Actions, prompt text, model lists and lighting preferences are stored in
`%USERPROFILE%\.config\creator-micro-studio\config.json` (or
`$XDG_CONFIG_HOME\creator-micro-studio\config.json` when that is set). Existing
`micromanager/config.json` settings are read until the Studio file is created;
the first edit migrates them without deleting the old file. Everything works
without a config file. Example:

```json
{
  "actions": {
    "6": "focus-agent",
    "7": "previous-tab",
    "9": { "type": "submit-prompt", "text": "Run the test suite" },
    "dial-right": "effort-up",
    "joy-up": "model-previous"
  },
  "lighting": {
    "brightness": 1.0,
    "speed": 0.5,
    "status": {
      "blocked": { "c": "#FF2D2D", "e": "breath" },
      "working": { "c": "#FFA000", "e": "solid" },
      "done": { "c": "#00B0FF", "e": "solid" },
      "idle": { "c": "#00C853", "e": "solid" }
    },
    "underglow": { "mode": "status", "c": "#00C853", "e": "rainbow" },
    "backlight": { "mode": "off", "c": "#FFA000", "e": "solid" },
    "keys": { "8": { "c": "#7C4DFF", "e": "solid", "b": 0.5 } }
  }
}
```

Unknown JSON fields are preserved on save. Actions and lighting are host-side;
they do not rewrite the pad keymap. See the
[Windows usage guide](docs/using-the-pad.md) for action parameters and input aliases.

## Versioning

The Windows app follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). The
version has one home, [`Directory.Build.props`](Directory.Build.props); the SDK
derives the assembly version from it, and the app and console harness display it.
[CHANGELOG.md](CHANGELOG.md) records what each version contains. A one-off build
can override it without editing anything:

```powershell
dotnet build AbesWorkLouderCreatorMicroStudio.Windows.sln -p:Version=0.4.2-rc.1
```

## Layout

| | |
|---|---|
| `src/CreatorMicroStudio.Core` | protocol, Herdr client (named pipe, Unix socket, WSL bridge), keymap, status mapping, pad emulator |
| `src/CreatorMicroStudio.Transport.Winrt` | raw-HID transport (vendor usage page `0xFF00`) |
| `src/CreatorMicroStudio.App` | the MAUI tray app |
| `src/CreatorMicroStudio.Console` | hardware smoke test |
| `src/CreatorMicroStudio.HidSpike` | the Phase 0 transport probe |
| `tests/CreatorMicroStudio.Core.Tests` | the ported WLKit test suite |
| `AbesWorkLouderCreatorMicroStudio.Windows.sln` | branded Windows solution |
| `PLAN.md` | the port's plan and current state |
| `docs/using-the-pad.md` | using the hardware: key map, colours, config, troubleshooting |

The macOS side of the repo — `Sources/WLKit` in particular — is the reference
the port is written against; [`docs/hacking.md`](../docs/hacking.md) documents
the device protocol itself.
