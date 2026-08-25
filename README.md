# Tape to Tape — Custom Campaign Framework

A modding framework for **Tape to Tape** (the hockey roguelike by Excellent Rectangle) that lets you build fully custom campaigns — teams, players, rosters, relics, acts, uniforms, and more — using a GUI campaign creator. No coding required.

---

## Table of Contents

1. [Installation](#installation)
2. [Campaign Creator GUI](#campaign-creator-gui)
3. [How Campaigns Work](#how-campaigns-work)
4. [Save System and Testing](#save-system-and-testing)
5. [Building from Source](#building-from-source)
6. [Troubleshooting](#troubleshooting)

---

## Installation

**Tape to Tape must be on the EXPERIMENTAL branch** on Steam.
Right-click the game → *Properties* → *Betas* → pick `experimental`.

### Windows (recommended)

1. Download **`T2T_Custom_Campaign_Framework_Setup.exe`** from the
   [latest release](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/releases/latest).
2. Run the installer. It auto-detects your Tape to Tape install, installs
   BepInEx 6 if missing, drops the DLL into `BepInEx/plugins/`, and
   installs the **T2T Campaign Creator** GUI with a desktop shortcut.
3. Launch Tape to Tape. The mod is active with the built-in Example Campaign.

The Campaign Creator checks for updates on startup. When a newer version is
available you get a prompt — it downloads the installer and relaunches
automatically.

### Linux / Steam Deck

1. Download **`T2T_Custom_Campaign_Framework_Linux_vX.Y.Z.zip`** from the
   [latest release](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/releases/latest).
2. Unzip, then run the installer:
   ```sh
   chmod +x install.sh
   ./install.sh
   ```
3. See `INSTALL.md` inside the zip for BepInEx setup and launch options.

---

## Campaign Creator GUI

The **T2T Campaign Creator** is the primary way to build and edit campaigns.
It covers every aspect — teams, players, goalies, skins, relics, talents,
jersey colors, logos, act sequences, and reward pools — with pickers, live
previews, and jersey rendering.

### Launching

- **Windows:** use the Start Menu or desktop shortcut created by the installer.
- **Linux / Steam Deck:**
  ```sh
  cd ~/.steam/steam/steamapps/common/Tape\ to\ Tape/BepInEx/plugins/Custom\ Campaigns\ Mod
  python3 creator_gui.py
  ```
  Requires Python 3.10+ and tkinter (`python3-tk` on Debian/Ubuntu,
  `python3-tkinter` on Fedora, `tk` on Arch/Steam Deck).

### Starting a campaign

1. Launch the Campaign Creator.
2. Home tab → *New Campaign*. Name it, pick an act sequence, save.
3. Add teams with *New Team* or *Import Game Team* (imports a real in-game
   team's roster as a starting point).
4. Edit each team — identity, colors, jersey, relics, lineup, goalie.
5. Edit players — stats, face, size, handedness, ability, talents, uniform.
6. Home tab → *Active campaign* → select your new campaign → *Set Active*.
7. Launch Tape to Tape.

### What you can do in the GUI

| Tab / Section | What it does |
|---------------|-------------|
| **Home** | Tree of all campaigns, teams, players, goalies. Double-click to edit. Right-click for delete / duplicate / rename. Set the active campaign. |
| **Campaign editor** | Act sequence, Spartan/minigame toggles, reward pools (which relics and talents can appear in random reward nodes), map layout, squad template, offside. |
| **Team editor** | Name, abbreviation, city, logo, jersey colors (with live swatch + jersey preview), uniform skins, team relics, goalie, 5-skater lineup, optional Line 2. |
| **Player editor** | Face, size, handedness, skin tone, all 4 stats, ability, talents, per-player uniform and color overrides. |
| **Goalie editor** | Face, all 14 stats, pad/blocker/glove/mask skins. |
| **Player-team creator** | Build the squad the player picks in *Choose Your Squad*. Starting relics, starting head override, full lineup. |
| **Library** | Shared players and teams reusable across any campaign. Import from game, dump in-game teams to library. |
| **Import from game** | One-click import of any in-game team's full roster. |
| **Export to Play Now** | Writes your custom player to the game's save folder for use in Play Now and online. Uniform and colour overrides the game's save format has no room for ride along in `play_now_overrides/` and are applied by the mod. |
| **Auto-updater** | Checks GitHub on startup and via *Check for updates*. Downloads and relaunches the installer automatically. |
| **Changelog** | Shows the full release history fetched from GitHub. |
| **Uninstaller** | Three options: full uninstall / mod + BepInEx only / clear save data. |

### Disable the mod without uninstalling

In the Home tab set the active campaign to **Default** — or manually edit
`BepInEx/plugins/Custom Campaigns Mod/active.txt`:
```
active = default
```
The DLL still loads but registers no patches. The game runs 100% vanilla.

---

## How Campaigns Work

### Act Sequence

Each number in the act sequence creates one map. The sequence must end with `3`.

| Act | Games | Notes |
|-----|-------|-------|
| 1 | 5 (or 4 if Spartans kept) | Longest map |
| 2 | 3 | Standard map |
| 3 | 3 | Final — beating the boss ends the campaign |

Examples: `1, 2, 3` = 10 games · `1, 1, 2, 2, 3` = 17–19 games

### Team Order

Teams play in the order they appear in the campaign. Team 1 = game 1, Team 2 = game 2, etc.

**The last game on each map is the boss.** Everything else is an elite.

### Offside

The base game only offers offside in Play Now's Advanced Settings. A campaign
can turn it on for a whole run with the **Offside** checkbox in the campaign
editor, or by hand in `campaign.txt`:

```
Offside                 = yes
Offside Penalty         = Whistle
```

`Offside Penalty` is optional — `Whistle`, `Lose Puck` or `Knockout`. Leave it
out and whatever the player chose in Advanced Settings is used. The rule is
forced on per match, the same way the *Linesman* talent does it, so your own
saved settings are never rewritten.

### Included campaigns

| Campaign | What it is |
|----------|-----------|
| **Example Campaign** | Full 33-team NHL-style season. Stats scale from weakest to strongest. Great reference for a complete campaign. |
| **Blank Campaign** | All fields shown blank for 10 games. Copy and fill in what you want. |
| **Blank Import Teams** | Fastest setup — fill in team names and stat scales only. |
| **Blank Import Players** | Import individual players from any in-game team and mix rosters. |

---

## Save System and Testing

Progress saves to `save.txt` as `ActsCompleted,GamesPlayed`.

| save.txt value | What happens |
|----------------|-------------|
| `0,0` | Start from beginning |
| `0,4` | Jump to game 5 |
| `1,5` | Map 2, game 6 |

Delete `save.txt` or set it to `0,0` to reset. It only auto-resets after
beating the final boss.

To test a specific team, set `GamesPlayed` to team number minus 1
(test team 15 → write `0,14`).

### Enabling the debug console

1. Open `BepInEx/config/BepInEx.cfg` (generated on first BepInEx launch).
2. Under `[Logging.Console]` set `Enabled = true`.
3. Save and launch — a console window opens alongside the game.

Key log lines to look for:
- `[Config] Loaded X teams` — campaign loaded successfully
- `[Season] Next up: Game N` — which team is next
- `[Config] Applied manual team` — team config applied
- `[WARN]` — something needs fixing in your config

---

## Building from Source

Source is in `src/CustomCampaignFramework/`. Most users should use the installer.

```
dotnet build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release
```

Requires .NET 6 SDK and the BepInEx 6 IL2CPP reference DLLs.

Full build (DLL + GUI exe + Windows installer + Linux zip):

```powershell
.\_build_all.ps1
.\_build_all.ps1 -Deploy          # also copies to game plugins folder
.\_build_all.ps1 -Deploy -Sync "changelog message"   # + GitHub release
```

---

## Troubleshooting

- **Mod doesn't load** — check `BepInEx/LogOutput.log`. Look for
  `Loading [Custom Campaign Framework 2.1.23]`. If missing:
  - Confirm you're on the `experimental` Steam branch.
  - Confirm `BepInEx/plugins/CustomCampaignFramework.dll` exists.
  - Confirm you installed **IL2CPP** BepInEx 6 (not Mono, not stable).
- **Campaign Creator crashes on launch** (Windows) — make sure the
  `_internal/` folder is next to `T2T Campaign Creator.exe`. The installer
  puts both in `BepInEx/plugins/Custom Campaigns Mod/`.
- **Creator won't launch** (Linux) — `ModuleNotFoundError: tkinter`.
  Install the tkinter package for your distro (see Linux section above).
- **Teams look wrong / stats not applying** — check `BepInEx/LogOutput.log`
  for `[WARN]` lines. Enable the BepInEx console to see live output.

---

**Tape to Tape** by Excellent Rectangle | Built with BepInEx and HarmonyX | MIT License
