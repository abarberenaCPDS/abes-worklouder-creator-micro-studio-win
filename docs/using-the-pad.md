# Using Abe's WorkLouder Creator Micro Studio for Windows

The practical guide: what each key does, where the colours come from, how the pad
gets bound to the app, and what you can change. For the protocol underneath —
raw-HID JSON-RPC, the double-encoded keymap file, the vendor lighting API — see
[`docs/hacking.md`](hacking.md). For what is built and what is not,
see [`../PLAN.md`](../PLAN.md).

## Before you start

- A **Creator Micro 2** (vendor `0x303A`). Firmware `0.6.3` is what this port was
  written and tested against.
- **Either connection works.** The app matches the pad's vendor collection, not
  the cable, so USB and Bluetooth are found the same way and unplugging the pad
  changes nothing. Pairing is done in Windows Settings, as for any Bluetooth
  device.
- A running **Herdr** server for agent status and Herdr-backed actions. With
  Herdr unreachable the app can still drive the device and Windows system
  actions, but agent lights and Herdr actions report the connection error. On Windows
  that usually means Herdr inside WSL — see [Reaching Herdr](#reaching-herdr).
- **Quit Work Louder's `Input` app and the Codex desktop app** while you use
  this. All three drive the same lighting and will overwrite each other. Windows
  needs no permission grant (unlike macOS Input Monitoring), but the port does
  **not** yet detect a second client, so there is no warning if one is running.
- .NET 10 SDK to build and run. Launch the app from **F5 in Visual Studio** or by
  running the built `AbesWorkLouderCreatorMicroStudio.Windows.exe`.

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
| agent list | one row per Herdr agent, in Herdr's own order, each in its key's colour — click a row to jump to that agent |
| **Controls** | physical pad map and configurable action bindings |
| **Lighting** | saved Studio profile, with live preview, save and revert |
| **Herdr** | agents, slot/action mappings, transport and input availability |
| **Inspector** | temporary manual lighting controls |

Agents are named by the **pane's own title**, the same text Herdr's panel shows
for that pane: Claude's current task (`bug-prefill`), Codex's `repo | branch`.
The checkout is printed underneath. That matters when several agents share one
repo — naming them all after the directory leaves three rows reading
`arcOS.Web`. A Herdr old enough to omit the title falls back to the directory
name.

### The tray menu

The first entry shows the running version and is not a command. Then **Show /
Hide Window**, **Toggle Bridge**, **Exit**. Left-clicking the icon toggles the
window.

## The pad, key by key

The firmware numbers its keys row-major over a `[2, 4, 4, 3]` matrix, but **the
first row is wired right to left** — key `0` is the top-*right* key. Reading
order therefore starts `1, 0`. The stock agent layout is the two keys in the top
row plus the four in the second; any of the thirteen keycaps can be an agent slot
instead (see [Agent layout](#agent-layout)). Everything below is addressed by
these numbers, and so is every lighting payload the app sends. The diagram shows
the stock layout.

```
          +---------+   +---------+
 row 1    |  key 1  |   |  key 0  |          agent 0        agent 1
          +---------+   +---------+
 row 2    |  key 2  |   |  key 3  |  |  key 4  |  |  key 5  |
          +---------+   +---------+  +---------+  +---------+
                 agent 2     agent 3     agent 4     agent 5
 row 3    |  key 6  |   |  key 7  |  |  key 8  |  |  key 9  |
          +---------+   +---------+  +---------+  +---------+
                focus       next tab    focus       macro
 row 4    |     key 10 + key 11     |  |  key 12  |
          +-------------------------+  +---------+
             the wide key (voice)         macro
```

| key | what it is | what a press does |
|---|---|---|
| `1`, `0`, `2`, `3`, `4`, `5` | the stock agent keys: the top two keys and the second row | **agent 0…5** in the stock layout, in the order Herdr's panel lists them |
| `6` | row 3, first | Focuses Herdr's current agent by default; reassign in **Controls** |
| `7` | row 3, second | cycles to the next Herdr tab by default |
| `8` | row 3, third | Focuses Herdr's current agent by default; reassign in **Controls** |
| `9` | row 3, fourth | types its configured macro text (see below) |
| `10` + `11` | row 4, the wide key | starts Windows Voice Typing (`Win+H`) unless configured text/action overrides it |
| `12` | row 4, third | types its configured macro text |
| dial | turn | **scrolls** the focused pane, five lines a detent — right for down, left for up |
| dial | press | *nothing* — the slot is left as the pad shipped it (play/pause) |
| joystick | — | cycles the **model** for the focused agent |

Agent slot N is the Nth key of the configured layout, in physical reading order.
The stock layout lives in `Pad.AgentKeyIds` in
`src/CreatorMicroStudio.Core/Pad/Oai.cs`; the live layout is
`KeyBindings.AgentKeyIds`. That is the same mapping the lighting uses, which is
what makes the key you look at the key you press.

The **Controls** page lets you change what every physical input does without
changing the physical layout or writing action choices to the pad's flash.
Firmware input availability still varies by profile; the page labels inputs the
active profile does not report.

### Agent layout

Any of the thirteen keycaps can be an agent slot, and the slots follow physical
reading order: the first key in the layout answers slot 1, the second slot 2, and
so on. Open the **Agent layout** card in **Controls** and click keycaps to add or
remove them; **Apply layout** saves, and **Reset to default** restores the stock
six keys (`1, 0, 2, 3, 4, 5`). A slot chip whose current action is not Focus
Agent is marked, because that action still runs instead of focusing the slot.

The card writes the top-level `"agentKeys"` array in `config.json`, for example
`"agentKeys": [1, 0, 2, 3, 4, 5]`. An empty array (`"agentKeys": []`) is valid
and leaves no agent slots; a missing or malformed array falls back to the stock
layout. Any listed key focuses its agent unless an explicit `actions` entry says
otherwise; adding a key resets it to Focus Agent, and removing a key leaves its
current action in place. A custom action on a layout key takes precedence over
the agent status that key would otherwise show. Layout edits are host-side only:
they never rewrite the pad's flash keymap or change the keycodes the device
reports.

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
| the dial | `KV_OAI_AG13` / `KV_OAI_AG14` on the two rotation slots — its press is left alone |
| the joystick's cardinal sectors | `KV_OAI_AG15`…`AG18`, matched by angle — diagonals keep their keycodes, and a sector the firmware does not expose cannot be invented |

This is a **flash write to the pad's saved keymap** and it is deliberately
narrow: every other key, layer and lighting setting is untouched, the stick's
diagonals and any sector the firmware does not expose keep their own keycodes,
and anything outside the active profile's first layer is left as it is. In
practice it writes once: the next launch finds the bindings already in place and
writes nothing.

Two consequences worth knowing:

- If you later remap one of keys `0`–`12` in Work Louder's `Input` app, the app
  will put its own keycode back on the next connect. The app owns those keys.
- If you switch the pad to a **different profile or layer**, the app's codes are
  no longer on the active layer, so the keys will stop lighting until you switch
  back. The app finds the profile by `activeProfileId` and uses that profile's
  first layer.

To see all of this at the transport level, run the console harness against the
hardware; it prints `device.status`, `sys.version` and whether the keymap had to
change:

```powershell
dotnet run --project src\CreatorMicroStudio.Console
```

## Studio lighting and Inspector

Each agent gets its own key, colored by Herdr state. The underglow can follow the
worst state across agents, use a custom look, or be off; the key backlight can be
off or custom. Studio also supports fixed looks for individual keys that are not
currently showing an agent. The defaults remain red/breathing for `blocked`,
amber for `working`, blue for `done`, and green for `idle`/unknown.

The **Lighting** sidebar page owns the persistent Studio profile: global
brightness/speed, each status look, underglow, backlight, and per-key overrides.
Edits preview on the pad and save after about three seconds of inactivity; use
**Save now**, **Revert**, or **Restore defaults** when needed. Preferences live in
the host `config.json`; they do not consume device flash or change key bindings.
Studio loads and reapplies its saved look when it starts, reconnects, or takes the
pad back.

The **Inspector** sidebar page is the temporary manual-lighting console. Applying
a hand-built key/zone look suspends Studio painting while the page is active. It
is not saved; leaving Inspector returns the pad to Studio's saved profile. Use
Lighting—not Inspector—for permanent settings.

Turning the bridge **off** (tray menu or the Studio page) clears every thread
from `AG00` to `AG19` and both zones, but leaves the keymap and saved Studio
lighting alone. Turning it back on reapplies the saved profile.
## Configuration

Everything works without a config file. Studio stores settings in
`%USERPROFILE%\.config\creator-micro-studio\config.json` — or
`%XDG_CONFIG_HOME%\creator-micro-studio\config.json` when `XDG_CONFIG_HOME` is
set. Existing `%USERPROFILE%\.config\micromanager\config.json` settings are
read as a legacy fallback and migrate on the first Studio save; the source file
is left intact. This is not `%APPDATA%`.

```json
{
  "keys": {
    "9":     "Open a pull request for this branch",
    "12":    "Run the test suite",
    "10+11": "Summarise what you are working on"
  },
  "actions": {
    "0": "focus-agent",
    "6": "focus-slot",
    "7": "next-tab",
    "13": { "type": "scroll", "direction": "down", "amount": 5 },
    "dial-push": { "type": "media-key", "key": "play-pause" },
    "joy-up": "model-previous",
    "12": { "type": "submit-prompt", "text": "Run the test suite" }
  },
  "agentKeys": [1, 0, 2, 3, 4, 5],
  "dial": "scroll",
  "scroll": { "lines": 5 },
  "claude": { "models": ["fable", "opus", "sonnet", "haiku"], "efforts": ["low", "medium", "high", "xhigh", "max"] },
  "codex":  { "models": ["gpt-5.6-sol", "gpt-5.6-codex"] },
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
    "backlight": { "mode": "off", "c": "#FFA000", "e": "solid" }
  }
}
```

A bound string is **typed into the focused agent's prompt, unsubmitted** — you
still read it and press enter. `"10+11"` addresses the wide key as one; `"10"`
and `"11"` address its halves separately. Keys the file does not mention keep
their defaults (`9` and `12` have built-in text, the wide key has none), and an
empty string unbinds a legacy text binding. An explicit `actions` entry takes
precedence over the legacy text map. Without text/action overrides, the wide key
starts Windows Voice Typing (`Win+H`).

The Controls page saves edits to `actions` as they are applied and to
`agentKeys` on **Apply layout**, and updates the running bridge immediately.
External edits to the JSON are picked up on the next bridge start/off-on cycle.
A malformed file falls back to defaults when read; saves refuse to replace
malformed JSON.

### Configurable actions

Controls can assign every action to any input:

- **Herdr / agents:** Focus Agent, Focus slot 1–13, next/previous tab or workspace,
  focus pane by direction, zoom pane, send keys, Herdr command.
- **Prompts:** Insert text (unsubmitted) or Submit prompt.
- **Tune:** Effort up/down, next/previous model, confirm/cancel model picker.
- **System:** Scroll, keyboard shortcut, media key, Voice Typing, type text.
- **Launch:** Open URL/file/app or run a configured PowerShell command.
- **General:** Do nothing.

Parameterized action entries use a `type` object, for example
`{"type":"scroll","direction":"up","amount":3}` or
`{"type":"focus-slot","slot":2}`. Free text uses a `text`, `keys`, `url`, or
`command` field as appropriate. `actions` accepts numeric input IDs and aliases
such as `dial-right`, `dial-push`, and `joy-up`; a numeric entry takes precedence
over an alias. Legacy `keys`, `dial`, `scroll`, `claude`, and `codex` settings
remain supported.

Herdr commands are CLI arguments without a leading `herdr`; the command is split
with shell-like quoting but launched as an argument array, never through a shell.
Placeholders are fetched when the input is pressed: `{tab}`, `{tab_label}`,
`{workspace}`, `{workspace_label}`, `{pane}`, `{agent}`. `{ask:Prompt}` and
`{ask:Prompt=Default}` show a prompt; cancel stops the action. The executable is
found on `PATH` or by `WL_HERDR_PATH`. When `HERDR_SOCKET_PATH` points inside
WSL, Studio runs `herdr` inside the selected/default distro through `wsl.exe`;
install the Herdr CLI there. `WL_HERDR_PATH` can name its Linux executable path.

System actions use Windows input APIs. Windows Voice Typing uses Win+H; synthesized
input can be blocked when the target app is elevated. Standard media keys work;
brightness keys do not have a portable Windows media-key mapping and report an
unsupported-action error.

`"dial"` is `"scroll"` (the default) or `"effort"` — see [The dial](#the-dial).
An unrecognised word keeps the default rather than leaving the dial dead.
`"scroll"` says how a detent scrolls, and is ignored in effort mode: `"lines"` is
how far Herdr moves a pane whose output it holds. `"up"` / `"down"` name keys to
send to a pane that draws itself, and are **empty by default** — Herdr can send
neither a wheel event nor a page key, so there is nothing safe to send unless you
have bound the application's own line-scroll action (see [The dial](#the-dial)).
The dial's rightward turn sends `"down"` and its leftward turn `"up"`.
A step that is not a positive number keeps its default; a blank key stays blank,
which means "send nothing".

`claude.efforts` is the dial's ladder: turning past either end **pins** at the
end rather than wrapping, because a dial has ends even when the hardware spins
freely. `claude.models` **wraps** when the joystick runs off the end, because a
list of names has no natural top or bottom. `codex.models` is your copy of what
Codex's own `/model` menu offers, in its order — the joystick steers that menu
rather than owning it, so there is nothing to read it from.

### Environment variables

| variable | what it does |
|---|---|
| `WL_EMULATE` | `1` runs against the in-process pad emulator, ignoring hardware |
| `WL_TERMINAL_PROCESS` | the terminal **executable name** to raise on an agent press |
| `HERDR_SOCKET_PATH` | the Herdr socket: a Windows pipe name, or an absolute path inside WSL (see [Reaching Herdr](#reaching-herdr)) |
| `WL_WSL_DISTRO` | the distro that holds Herdr, when it is not WSL's default |
| `WL_HERDR_PATH` | optional full path to the `herdr` CLI used by Herdr command actions |
| `XDG_CONFIG_HOME` | moves the config directory |

The first five are the Studio's own, sharing the `WL_` prefix with the macOS
reference. The older `MICROMANAGER_EMULATOR` and `MICROMANAGER_WSL_DISTRO` were
retired in 0.5.0: scripts that still set them will silently fall back to hardware
and WSL's default distro, so rename them.

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
$env:WL_WSL_DISTRO = 'AlmaLinux-9'   # only when it is not the default distro
```

Two things it needs:

- **`nc` with `-U`** in the distro. `nmap-ncat` provides it
  (`sudo dnf install nmap-ncat`); it is what `nc` already is on most distros.
- **An absolute path.** `~` is expanded by a shell, and no shell is involved, so
  `/home/you/.config/herdr/herdr.sock` works and `~/.config/herdr/herdr.sock`
  does not.

To check it before launching the app at all:

```powershell
dotnet test tests\CreatorMicroStudio.Core.Tests\CreatorMicroStudio.Core.Tests.csproj --filter FullyQualifiedName~HerdrWslTests
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

**Scrolling** by default: five lines per detent in the focused pane. Turned
**right** the viewport moves **down** towards the newest output and turned **left**
up through the older output, the way rolling a wheel does. Long agent output is the
thing a dial is actually good at, and unlike effort it is not agent-specific. The
status line reports where you landed, because the pad has no screen of its own.

The pane the dial moves is the **focused agent's**, so there has to be one: with
a plain shell pane focused you get "Nothing has focus in Herdr right now."

A detent can only move output that Herdr holds. Panes drawing to the normal
screen keep their output in Herdr's scrollback and scroll exactly; a **full-screen
TUI** draws to the alternate screen, and those rows never reach the host's
scrollback — terminal behaviour rather than a Herdr limitation, and the same
reason a multiplexer cannot scroll `vim`:

| the pane | what a detent does |
|---|---|
| drawing to the normal screen, so its output is in Herdr's scrollback | `pane.scroll` by `"lines"` lines — exact, and needing nothing of the application. The status line reads `384209 · 12/240 lines back` |
| a full-screen TUI — Claude Code in fullscreen rendering, Codex by default | nothing to move and nothing to send: the status line says `nothing to scroll — a full-screen app keeps its own output`. It sends `"up"` / `"down"` instead if you have bound the application's own scroll keys — empty by default |

There is no third mechanism to reach for. Herdr cannot synthesise a mouse wheel —
there is no mouse or wheel event anywhere in its protocol — and its key vocabulary
has no page key: `pageup`, `pagedown` and every other spelling come back
`unsupported key`. The agents' own one-line scroll is bound to the wheel, so the
pad cannot reach it.

**`/tui default` in Claude Code** (the classic renderer), or **`/tui` →
Scrollback** in Codex (or `[tui] alternate_screen = "never"` in
`~/.codex/config.toml`), puts those rows into Herdr's scrollback — and then the
dial scrolls them by exact lines, five a detent, a nudge past the three a wheel
notch covers. Those panes also become readable to Herdr, which is what lets
`herdr pane read --lines` produce the whole conversation. The trade is real: the
classic renderer gives up fullscreen rendering's flicker-free drawing, its mouse
support and its flat memory use, and `/tui` saves the choice for the conversations
that follow. Scrolling the dial is the whole of the gain.

The switch clears the pane and it redraws itself from the next thing it prints, so
it can sit blank for a moment. It also starts a **fresh** scrollback: rows drawn
before it are gone, and a pane has nothing for the dial to move until it has
printed more than one screenful (the status line keeps saying `nothing to scroll`
until then).

Staying in fullscreen rendering is possible, but needs the far side's help: bind
the agent's own line-scroll action to a key the pad can send, and name that key in
`"up"` / `"down"`. In Claude Code those are `scroll:lineUp` / `scroll:lineDown`
(wheel-bound by default) in `~/.claude/keybindings.json`. A detent sends the key
`"lines"` times, so the distance still comes out the same.

**The dial push is usually unavailable.** The encoder's third slot stays the
stock play/pause key. Controls lists Dial push, but marks it unavailable unless
the active profile reports AG19; keymap setup never takes over stock play/pause.

Set `"dial": "effort"` in the config file to put the rotation back on **reasoning
effort** for the focused agent:

- **Claude** panes get `/effort <level>` typed and submitted, climbing
  `claude.efforts`.
- **Codex** panes get the chords its `~/.codex/config.toml` binds to
  `chat.increase_reasoning_effort` / `chat.decrease_reasoning_effort`
  (`ctrl+alt+u` / `ctrl+alt+d` by default).
- Anything else: "No effort control for *agent*".

In effort mode the level is tracked per pane in memory from the app's own
commands. Change the effort by hand inside the session and the app's idea of
where you are drifts until the next detent.

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
   — the Nth key in the configured layout is slot N.
3. Press an agent key: the status line says `Focused <name>`, Herdr selects that
   pane, and the terminal comes forward.
4. Press a macro key: the text appears in the focused pane's prompt, unsent; use
   Controls to assign any other action to that input.
5. Turn the dial: the status line reports the scroll — `12/240 lines back` in a
   pane drawing to the normal screen, or `nothing to scroll — a full-screen app
   keeps its own output` in one that draws itself (see [The dial](#the-dial)).
6. Click the dial: the stock play/pause key is preserved unless the active
   profile reports the dial push as an AG19 input.

If the lights are right but presses do nothing, that is the keymap: lit keys have
their `AG` binding, so a key that lights can report itself. A key that stays dark
is the one sending a keystroke instead.

## Troubleshooting

| what you see | what it means |
|---|---|
| the status names the **pad emulator** | the vendor collection did not open — wrong device, Work Louder's `Input` app holding it, or a Bluetooth pad that is asleep. The app runs against the emulator rather than exiting, and keeps looking for a real pad, so waking or plugging one in takes over by itself |
| the pad dropped off (slept, or out of range) | the app says `Pad went away — looking for it`, retries forever (1s apart, backing off to 10s), then says `Pad reconnected` and rebinds the keys. A pulled cable and a Bluetooth drop look the same from here |
| the dial says `nothing to scroll — a full-screen app keeps its own output` | the pane draws to the alternate screen, so Herdr holds none of its output — and the pad can send neither a wheel event nor any page key. Run `/tui default` in it (Codex: `/tui` → Scrollback), or name the application's own scroll keys in `"up"` / `"down"`. See [The dial](#the-dial) |
| keys never light at all | the bindings are not on the **active** layer. Switch the pad back to the active profile's first layer, or restart the bridge (off/on) |
| the console says `Keymap applied: False` | nothing needed writing, so the bindings *are* right — if keys are still dark, the pad is showing a different profile or layer |
| lighting looks stale | the port **polls** `agent.list` every 2.5s; unlike macOS it does not subscribe to Herdr's event streams yet, so a state change can take up to ~2.5s to appear |
| a key does something unexpected | another client (Work Louder's `Input` app, Codex desktop) is fighting for the pad and rewriting the lighting. Quit it |
| "Focused the agent, but found no terminal window to raise." | set `WL_TERMINAL_PROCESS` to your terminal's executable name |
| status shows `Error: …` | the last poll failed, usually Herdr unreachable. Check `HERDR_SOCKET_PATH` |
| `Error: There is no distribution with the supplied name.` | `WL_WSL_DISTRO` does not match a distro you have — check `wsl --list` |
| an `nc` complaint about `No such file or directory` | the socket is not where `HERDR_SOCKET_PATH` says: Herdr is not running, or it is in a different distro |
| `~/…` in `HERDR_SOCKET_PATH` fails | nothing expands it: the WSL route runs no shell, so use the absolute `/home/you/…` |
| the pad is dark and the window says `disabled` | the bridge is switched off. Toggle it back; the lights return immediately and the keymap is untouched |
| one key stays dark but its agent is running | the configured agent layout has fewer slots than you have agents; add keys in the **Agent layout** card in Controls. The tray window lists every agent, in the same order, and its rows jump to the agent |
| everything is dark after switching profiles on the pad | see the keymap note above — the app binds the *active* profile's first layer |

No hardware to hand? `WL_EMULATE=1` runs against an in-process pad
emulator that reproduces the firmware's awkward behaviour on purpose, including
that an unbound key reports nothing and that `{"ok":1}` comes back for any
lighting payload. It boots on the stock F-key keymap, so the binding step does
real work. There is no emulator window on Windows yet — the emulator's lights are
visible only through the app's swatch row.

## Not here yet

These exist on macOS and are **not** in this port; the plan is in
[`../PLAN.md`](../PLAN.md):

- the **GitButler stack** and **land branches** actions/panels
- the Inspector's traffic log, raw JSON-RPC console, key walk and "resend whole
  pad"; the Windows Inspector page is only the temporary manual-lighting console
- the emulator's own interactive window
- **Herdr event streams** — the port polls, so transitions are not instant
- a **second-client warning**, and **open at login**
- **manageKeymap: false** — the app always validates/applies its required event
  keymap on connect
