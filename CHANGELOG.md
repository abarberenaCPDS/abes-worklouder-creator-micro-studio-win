# Changelog

The Windows port follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html),
and this file follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

The version lives in `Directory.Build.props` and is surfaced by the app and the
console harness, so there is nothing else to keep in step.

## [Unreleased]

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

### Fixed

- A HID write deadlock: the blocking read loop and the writes shared one
  synchronous handle, so `WriteFile` could block forever. The transport now uses
  a dedicated write handle.
- The emulator's `fs.read` returned `data` as a nested object where the firmware
  returns a JSON *string*, so every emulated read failed to parse.
- The keymap check demanded all four joystick cardinals, which firmware 0.6.3's
  two-sector stick can never satisfy.
