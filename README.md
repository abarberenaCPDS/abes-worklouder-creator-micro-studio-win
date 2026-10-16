# Micro Manager for Windows

[![CI](https://github.com/schacon/micro-manager/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/schacon/micro-manager/actions/workflows/ci.yml)
[![Release](https://github.com/schacon/micro-manager/actions/workflows/release.yml/badge.svg)](https://github.com/schacon/micro-manager/actions/workflows/release.yml)
![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![UI](https://img.shields.io/badge/UI-.NET%20MAUI-512BD4)
![Version](https://img.shields.io/badge/version-0.2.0-blue)
![SemVer](https://img.shields.io/badge/SemVer-2.0.0-3E9392)
![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-FE5196)

The Windows port of [Micro Manager](../README.md): a tray app that lights each
running [Herdr](https://herdr.dev) agent on its own key of a Work Louder
**[Creator Micro 2](https://worklouder.cc/creator-micro-2)**, and jumps to that
agent when you press the key.

It drives the pad the same way the macOS app does — Herdr's API on one side, a
raw-HID JSON-RPC channel on the other — with no Node and no daemon.

> **Status: Phase 2 in progress.** The app builds, launches, lives in the tray
> and drives the real pad over raw HID. [PLAN.md](PLAN.md) has the checklist and
> what is still open.

## Requirements

- Windows 11 (build `10.0.19041` or newer)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Creator Micro 2 and a running Herdr server for anything beyond the emulator —
  on Windows that usually means [Herdr inside WSL](#connecting-to-herdr)
- Visual Studio 2026 to launch the MAUI app — **F5** is the supported path

## Build, test, run

```powershell
cd windows
dotnet build MicroManager.sln
dotnet test tests\MicroManager.Core.Tests\MicroManager.Core.Tests.csproj
dotnet run --project src\MicroManager.Console   # hardware smoke test
```

`MicroManager.Core` targets `net10.0`, so `dotnet test` also runs on macOS.
`MicroManager.App` is a MAUI app and only builds on Windows; it is the
solution's startup project.

## No pad to hand

```powershell
$env:MICROMANAGER_EMULATOR = "1"
dotnet run --project src\MicroManager.Console
```

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
$env:MICROMANAGER_WSL_DISTRO = 'AlmaLinux-9'   # only if not your default distro
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

## Configuration

Macro keys, the dial's effort ladder and the joystick's model lists come from
`%USERPROFILE%\.config\micromanager\config.json` (or
`$XDG_CONFIG_HOME\micromanager\config.json` when that is set) — the same path and
shape the macOS app uses. Everything works without the file. See the
[top-level README](../README.md#configuration).

## Versioning

The port follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). The
version has one home, [`Directory.Build.props`](Directory.Build.props); the SDK
derives the assembly version from it, and the app and console harness display it.
[CHANGELOG.md](CHANGELOG.md) records what each version contains. A one-off build
can override it without editing anything:

```powershell
dotnet build MicroManager.sln -p:Version=0.2.0-rc.1
```

## Layout

| | |
|---|---|
| `src/MicroManager.Core` | protocol, Herdr client (named pipe, Unix socket, WSL bridge), keymap, status mapping, pad emulator |
| `src/MicroManager.Transport.Winrt` | raw-HID transport (vendor usage page `0xFF00`) |
| `src/MicroManager.App` | the MAUI tray app |
| `src/MicroManager.Console` | hardware smoke test |
| `src/MicroManager.HidSpike` | the Phase 0 transport probe |
| `tests/MicroManager.Core.Tests` | the ported WLKit test suite |
| `PLAN.md` | the port's plan and current state |
| `docs/using-the-pad.md` | using the hardware: key map, colours, config, troubleshooting |

The macOS side of the repo — `Sources/WLKit` in particular — is the reference
the port is written against; [`docs/hacking.md`](../docs/hacking.md) documents
the device protocol itself.
