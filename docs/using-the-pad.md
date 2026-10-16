# Using Micro Manager with the pad

The practical guide: what each key does, where the colours come from, how the pad
gets bound to the app, and what you can change. For the protocol underneath —
raw-HID JSON-RPC, the double-encoded keymap file, the vendor lighting API — see
[`docs/hacking.md`](../../docs/hacking.md). For what is built and what is not,
see [`../PLAN.md`](../PLAN.md).

## Before you start

- A **Creator Micro 2** (vendor `0x303A`). Firmware `0.6.3` is what this port was
  written and tested against.
- A running **Herdr** server with at least one agent pane. The pad shows
  `agent.list` and nothing else: with Herdr unreachable the app still drives the
  pad, but every key goes dark and the status line reports the error. On Windows
  that usually means Herdr inside WSL — see [Reaching Herdr](#reaching-herdr).
- **Quit Work Louder's `Input` app and the Codex desktop app** while you use
  this. All three drive the same lighting and will overwrite each other. Windows
  needs no permission grant (unlike macOS Input Monitoring), but the port does
  **not** yet detect a second client, so there is no warning if one is running.
- .NET 10 SDK to build and run. Launch the app from **F5 in Visual Studio** or by
  running the built `MicroManager.App.exe`.

The app has no taskbar entry: it lives in the tray. Left-click the tray icon to
toggle the window, right-click for the menu.

### The window

| | |
|---|---|
| **Status** | connected / disconnected, or the last error |
| **Device** | `connected` / `disconnected` — whether the raw-HID pad is open |
| **Bridge** | `enabled` or `disabled` — the same switch as the tray menu |
| **Pad keys** | 13 swatches mirroring the pad, each showing that key's live colour |
| last action | what the pad was last used for — the pad has no screen of its own |
| agent list | one row per Herdr agent, in Herdr's own order, each in its key's colour |

### The tray menu

`v0.1.0` (the running version), **Show / Hide Window**, **Toggle Bridge**,
**Exit**. Left-clicking the icon toggles the window.

## The pad, key by key

The firmware numbers its keys row-major over a `[2, 4, 4, 3]` matrix, but **the
first row is wired right to left** — key `0` is the top-*right* key. Reading
order therefore starts `1, 0`, and the six agent keys are the two in the top row
plus the four in the second. Everything below is addressed by these numbers, and
so is every lighting payload the app sends.

```
          +---------+   +---------+
 row 1    |  key 1  |   |  key 0  |          agent 0        agent 1
          +---------+   +---------+
 row 2    |  key 2  |   |  key 3  |  |  key 4  |  |  key 5  |
          +---------+   +---------+  +---------+  +---------+
                 agent 2     agent 3     agent 4     agent 5
 row 3    |  key 6  |   |  key 7  |  |  key 8  |  |  key 9  |
          +---------+   +---------+  +---------+  +---------+
                 stack       tabs        land        macro
 row 4    |     key 10 + key 11     |  |  key 12  |
          +-------------------------+  +---------+
             the wide key (voice)         macro
```

| key | what it is | what a press does |
|---|---|---|
| `1`, `0`, `2`, `3`, `4`, `5` | the top two keys and the second row | **agent 0…5**, in the order Herdr's panel lists them |
| `6` | row 3, first | *nothing yet* — floats the GitButler stack on macOS; not ported |
| `7` | row 3, second | cycles the tabs of the focused Herdr window |
| `8` | row 3, third | *nothing yet* — lands the agent's branches on macOS; not ported |
| `9` | row 3, fourth | types its configured macro text (see below) |
| `10` + `11` | row 4, the wide key | types its configured macro text; with none bound, *nothing* (it taps right-command for Superwhisper on macOS, which does not exist here) |
| `12` | row 4, third | types its configured macro text |
| dial | — | climbs or drops **reasoning effort** for the focused agent |
| joystick | — | cycles the **model** for the focused agent |

Agent slot N is the Nth key in reading order — `Pad.AgentKeyIds` in
`windows/src/MicroManager.Core/Pad/Oai.cs`. That is the same mapping the lighting
uses, which is what makes the key you look at the key you press.

If you want a different physical arrangement, the layout is code, not config:
edit the constants in `Pad`. The pad's own keymap is re-asserted by the app (see
below) so a custom layout will not survive on its own.

## Binding the pad: the keymap

Per-key colour only works on keys bound to a `KV_OAI_AG*` keycode, and only when
those bindings are on the **active layer of the active profile**. Nothing reports
a mismatch: `v.oai.thstatus` answers `{"ok":1}` for a key it cannot light. A wrong
keymap therefore looks exactly like a working one from the host side, except that
the key stays dark.

On every connect the app reads the pad's `keymap.json` over `fs.read`, checks it,
and — only if something is missing — writes it back over `fs.write`:

| what it binds | to |
|---|---|
| all 13 keys | `KV_OAI_AG00`…`KV_OAI_AG12`, key N to `AG`N |
| the dial's two rotation slots | `KV_OAI_AG13` / `KV_OAI_AG14` (its press is left alone) |
| the joystick's cardinal sectors | `KV_OAI_AG15`…`AG18`, matched by angle — diagonals keep their keycodes, and a sector the firmware does not expose cannot be invented |

This is a **flash write to the pad's saved keymap** and it is deliberately
narrow: every other key, layer, encoder and lighting setting is untouched, and
anything outside the active profile's first layer is left as it is. In practice
it writes once: the next launch finds the bindings already in place and writes
nothing.

Two consequences worth knowing:

- If you later remap one of keys `0`–`12` in Work Louder's `Input` app, the app
  will put its own keycode back on the next connect. The app owns those keys.
- If you switch the pad to a **different profile or layer**, the app's codes are
  no longer on the active layer, so the keys will stop lighting until you switch
  back. The app always uses `activeProfileId` and that profile's first layer.

To see all of this at the transport level, run the console harness against the
hardware; it prints `device.status`, `sys.version` and whether the keymap had to
change:

```powershell
dotnet run --project src\MicroManager.Console
```

## The lights

Each agent gets its own key. The colour is the agent's state, and the **underglow
carries the worst state across all agents** — blocked beats working beats unknown
beats idle beats done — so "does anything need me?" reads from across the room.

| state | colour | effect | what it means |
|---|---|---|---|
| `blocked` | `#FF2D2D` red | **breathing** | the agent needs you |
| `working` | `#FFA000` amber | solid | busy |
| `done` | `#00B0FF` blue | solid | finished and **not yet looked at** |
| `idle` | `#00C853` green | solid | finished and seen |
| `unknown` | `#00C853` green | solid | a state the app does not know |

Herdr reports `done` for an agent that has finished and that you have not focused
yet, and `idle` once it is focused. That is a real difference — one is waiting to
be read, the other is just quiet — which is why they are different colours.

The keys that are not agents are dimmer, so the agent row stays the thing you
read:

| key | colour | brightness |
|---|---|---|
| stack `6` | `#7C4DFF` purple | 30% (full, breathing, while the panel is open — not ported) |
| tabs `7` | `#00BFA5` teal | 30% |
| land `8` | `#E91E63` pink | 30% (full, breathing, while the panel is open — not ported) |
| macro `9`, `12` | `#90A4AE` grey-blue | 25% |
| voice `10`, `11` | `#ECEFF1` off-white | 25%, and only while nothing is bound to them |

Agent keys sit at full brightness. There are two addressable **zones** as well as
the per-key threads: the key backlight and the underglow. The port paints the
**underglow** with the aggregate and leaves the key backlight zone dark, because a
zone colour would only compete with the per-agent keys underneath it.

Turning the bridge **off** (tray menu or the window button) clears every thread
from `AG00` to `AG19` and both zones, but deliberately leaves the keymap alone:
rebinding is a flash write, and the keys light instantly on the way back in
because the bindings are still there.

### Changing the colours

There is **no config file for colours** — not in this port and not in the macOS
original. The palette is compiled in, in
`windows/src/MicroManager.Core/Config/StatusMapper.cs`:

- `BridgeConfig.Colors` — state to packed `0xRRGGBB`
- `BridgeConfig.Effects` — state to `Oai.Effect`
  (`Off` `Solid` `Snake` `Rainbow` `Breath` `Gradient` `ShallowBreath`)
- `BridgeConfig.Priority` — the worst-state-wins order for the underglow
- `Brightness`, `Speed` — the defaults every thread is built from
- `StackColor`, `TabCycleColor`, `LandColor`, `VoiceColor`, `MacroColor` — the
  fixed-purpose keys

`BridgeConfig` also carries `DriveBacklight` (paint the key zone with the
aggregate after all) and `ManageKeymap` (bind the keys on startup), but nothing
loads a config file yet, so both are compile-time only — and `ManageKeymap` is
not honoured at all: the port always applies the keymap.

Change a value and rebuild. The window's own chrome colours are separate, in
`App.xaml.cs`.

## Configuration

Everything works without a config file. To rebind the macro keys, or change what
the dial and joystick offer, drop a `config.json` into
`%USERPROFILE%\.config\micromanager\` — or `%XDG_CONFIG_HOME%\micromanager\`
when `XDG_CONFIG_HOME` is set. This is the macOS path and shape, deliberately:
one file drives both implementations. (Note this is *not* `%APPDATA%`.)

```json
{
  "keys": {
    "9":     "Open PRs for all active GitButler branches",
    "12":    "Run but pull",
    "10+11": "Summarise what you are working on"
  },
  "claude": { "models": ["fable", "opus", "sonnet", "haiku"], "efforts": ["low", "medium", "high", "xhigh", "max"] },
  "codex":  { "models": ["gpt-5.6-sol", "gpt-5.6-codex"] }
}
```

A bound string is **typed into the focused agent's prompt, unsubmitted** — you
still read it and press enter. `"10+11"` addresses the wide key as one; `"10"`
and `"11"` address its halves separately. Keys the file does not mention keep
their defaults (`9` and `12` have built-in text, the wide key has none), and an
empty string unbinds a key outright. Binding the wide key to text is what turns
it into a macro key; with no binding it does nothing on Windows.

The file is read **once per bridge start**, so a change needs an off/on toggle,
not a relaunch. A malformed file falls back to the defaults rather than leaving
you with a dead pad.

`claude.efforts` is the dial's ladder: turning past either end **pins** at the
end rather than wrapping, because a dial has ends even when the hardware spins
freely. `claude.models` **wraps** when the joystick runs off the end, because a
list of names has no natural top or bottom. `codex.models` is your copy of what
Codex's own `/model` menu offers, in its order — the joystick steers that menu
rather than owning it, so there is nothing to read it from.

### Environment variables

| variable | what it does |
|---|---|
| `MICROMANAGER_EMULATOR` | `1` runs against the in-process pad emulator, ignoring hardware |
| `WL_TERMINAL_PROCESS` | the terminal **executable name** to raise on an agent press |
| `HERDR_SOCKET_PATH` | the Herdr socket: a Windows pipe name, or an absolute path inside WSL (see [Reaching Herdr](#reaching-herdr)) |
| `MICROMANAGER_WSL_DISTRO` | the distro that holds Herdr, when it is not WSL's default |
| `XDG_CONFIG_HOME` | moves the config directory |

`WL_TERMINAL_PROCESS` matters more than it looks: see below.

## Reaching Herdr

On Windows the app has two transports and chooses between them from the *shape*
of `HERDR_SOCKET_PATH`. There is no separate switch, because the shape is the
answer: a path starting with `/` cannot be a Windows pipe, so it can only mean a
socket inside a distro.

| `HERDR_SOCKET_PATH` | how the app connects |
|---|---|
| `/home/you/.config/herdr/herdr.sock` | Herdr **inside WSL**, reached over `wsl.exe` |
| `\\.\pipe\…`, or a bare pipe name | a Windows **named pipe**, for Herdr running natively on Windows |

### Herdr inside WSL

Nothing has to be left running on the Linux side: no relay process, no listening
port, no daemon. Each request runs `nc -U <socket>` in the distro and speaks the
protocol over that child's pipes — and Herdr closes the socket after every reply,
so there was never a connection to keep for a second call anyway.

```powershell
$env:HERDR_SOCKET_PATH = '/home/you/.config/herdr/herdr.sock'
$env:MICROMANAGER_WSL_DISTRO = 'AlmaLinux-9'   # only when it is not the default distro
```

Two things it needs:

- **`nc` with `-U`** in the distro. `nmap-ncat` provides it
  (`sudo dnf install nmap-ncat`); it is what `nc` already is on most distros.
- **An absolute path.** `~` is expanded by a shell, and no shell is involved, so
  `/home/you/.config/herdr/herdr.sock` works and `~/.config/herdr/herdr.sock`
  does not.

To check it before launching the app at all:

```powershell
dotnet test tests\MicroManager.Core.Tests\MicroManager.Core.Tests.csproj --filter FullyQualifiedName~HerdrWslTests
```

Those two live tests skip themselves unless `HERDR_SOCKET_PATH` names a socket
inside WSL, so they are safe to run anywhere.

### Herdr on Windows

Herdr's native Windows build serves the same API on a **named pipe** instead of a
Unix socket, so give the app the pipe name and it takes the other branch. This
path is **less proven than the WSL one**: it has not been exercised against a
real Windows Herdr here, and the pipe name the app assumes by default is a
Unix-shaped path that will not resolve to a pipe. List what Herdr actually
created and point the app at it:

```powershell
Get-ChildItem \\.\pipe\ | Where-Object Name -match herdr
```

## Pressing keys

### Agent keys

Pressing an agent key focuses that agent in Herdr **and brings the terminal
forward**. Herdr selects the pane but leaves the window wherever it was in the
window order, so an agent key pressed from a browser would otherwise move a
cursor you cannot see.

Windows has no bundle identifiers, so the terminal is found by **executable
name**, checking `WindowsTerminal`, `wt`, `wezterm-gui`, `alacritty`, `kitty`,
`mintty`, `ghostty`, `ConEmu64`, `ConEmu`. If your terminals are not in that
list, set `WL_TERMINAL_PROCESS` to the executable name — the status line tells you
once, when it cannot find a window:

```
Focused the agent, but found no terminal window to raise.
Set WL_TERMINAL_PROCESS to your terminal's executable name.
```

Nothing is ever launched. The agent whose key you pressed is running in a pane of
a terminal that is by definition already running; a terminal that cannot be found
means the name is wrong, not that one should be started. A window that is
minimised is restored.

### The dial

Reasoning effort for the focused agent, one step per detent: clockwise up,
counter-clockwise down.

- **Claude** panes get `/effort <level>` typed and submitted, climbing
  `claude.efforts`.
- **Codex** panes get the chords its `~/.codex/config.toml` binds to
  `chat.increase_reasoning_effort` / `chat.decrease_reasoning_effort`
  (`ctrl+alt+u` / `ctrl+alt+d` by default).
- Anything else: "No effort control for *agent*".

The dial's own press is not touched by this app.

Effort is tracked per pane in memory from the app's own commands. Change the
effort by hand inside the session and the app's idea of where you are drifts
until the next detent.

### The joystick

Model for the focused agent.

- **Claude** panes get `/model <name>` typed and submitted, wrapping through
  `claude.models`. North and south move; east and west do nothing.
- **Codex** panes drive Codex's own `/model` picker: the first vertical
  deflection opens it, then north/south move, east confirms, west cancels. A
  picker left alone for 30 seconds is treated as stale, and the next deflection
  opens a fresh one.
- Anything else: "No model control for *agent*".

**Firmware limitation:** `0.6.3` exposes only two stick sectors, bound to `AG15`
and `AG17` — the vertical pair. So on this firmware the Codex picker can be
opened and moved but **not confirmed or cancelled** with the stick; press enter
or escape in the terminal instead. Claude's model list only needs the vertical
deflections and is unaffected.

### Macros and tabs

A macro key types its bound string into the focused agent's prompt, unsubmitted.
With nothing focused in Herdr you get "Nothing has focus in Herdr right now."

The tabs key cycles the focused Herdr window's tabs and wraps at the end. Tabs in
other workspaces are left alone: cycling is a within-window gesture, not a window
switcher.

## How to tell it is working

1. The status line says **Connected** and the pad's underglow takes the aggregate
   colour of your agents.
2. The agent keys light in Herdr's own order. Compare against Herdr's agent panel
   — slot N is row N.
3. Press an agent key: the status line says `Focused <name>`, Herdr selects that
   pane, and the terminal comes forward.
4. Press a macro key: the text appears in the focused pane's prompt, unsent.

If the lights are right but presses do nothing, that is the keymap: lit keys have
their `AG` binding, so a key that lights can report itself. A key that stays dark
is the one sending a keystroke instead.

## Troubleshooting

| what you see | what it means |
|---|---|
| "Creator Micro 2 not found; using the pad emulator." | the vendor collection did not open — wrong device, or Work Louder's `Input` app holding it. The app falls back to the emulator rather than exiting |
| keys never light at all | the bindings are not on the **active** layer. Switch the pad back to the active profile's first layer, or restart the bridge (off/on) |
| the console says `Keymap applied: False` | nothing needed writing, so the bindings *are* right — if keys are still dark, the pad is showing a different profile or layer |
| lighting looks stale | the port **polls** `agent.list` every 2.5s; unlike macOS it does not subscribe to Herdr's event streams yet, so a state change can take up to ~2.5s to appear |
| a key does something unexpected | another client (Work Louder's `Input` app, Codex desktop) is fighting for the pad and rewriting the lighting. Quit it |
| "Focused the agent, but found no terminal window to raise." | set `WL_TERMINAL_PROCESS` to your terminal's executable name |
| status shows `Error: …` | the last poll failed, usually Herdr unreachable. Check `HERDR_SOCKET_PATH` |
| `Error: There is no distribution with the supplied name.` | `MICROMANAGER_WSL_DISTRO` does not match a distro you have — check `wsl --list` |
| an `nc` complaint about `No such file or directory` | the socket is not where `HERDR_SOCKET_PATH` says: Herdr is not running, or it is in a different distro |
| `~/…` in `HERDR_SOCKET_PATH` fails | nothing expands it: the WSL route runs no shell, so use the absolute `/home/you/…` |
| the pad is dark and the window says `disabled` | the bridge is switched off. Toggle it back; the lights return immediately and the keymap is untouched |
| one key stays dark but its agent is running | the 6 agent slots cover 6 agents; further agents have no key |
| everything is dark after switching profiles on the pad | see the keymap note above — the app binds the *active* profile's first layer |

No hardware to hand? `MICROMANAGER_EMULATOR=1` runs against an in-process pad
emulator that reproduces the firmware's awkward behaviour on purpose, including
that an unbound key reports nothing and that `{"ok":1}` comes back for any
lighting payload. It boots on the stock F-key keymap, so the binding step does
real work. There is no emulator window on Windows yet — the emulator's lights are
visible only through the app's swatch row.

## Not here yet

These exist on macOS and are **not** in this port; the plan is in
[`../PLAN.md`](../PLAN.md):

- the **stack** key and the **land** key — both fall flat, and the keys do nothing
- the **voice** key — Superwhisper is macOS-only, so the wide key only works when
  you bind text to it
- the **Inspector** debug UI, and the emulator's own window
- **Herdr event streams** — the port polls, so transitions are not instant
- a **second-client warning**, and **open at login**
- **manageKeymap: false** — the port always applies the keymap
