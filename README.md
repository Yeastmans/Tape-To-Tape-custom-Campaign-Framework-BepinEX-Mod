# Tape to Tape — Custom Campaign Framework

A modding framework for **Tape to Tape** (the hockey roguelike by Excellent Rectangle) that lets you build fully custom campaigns by editing plain text config files or using included python based campaign creator. Design your own teams, players, stats, appearances, talents, abilities, relics, and campaign structure. No coding required.

> **EARLY RELEASE** — This mod is mostly untested and will probably have quirks, bugs, and rough edges. Some fields may not work exactly as described. If something breaks, try simplifying your config (use imports instead of manual setup). Report issues and feedback on this github page or message me on Discord @yeastmann so things can be fixed. This was coded with the help of an LLM so SOME THINGS MAY NOT WORK AS INTENDED. I will be updating this often so if something is broken for you check back here for new versions!.

---

## Table of Contents

1. [Installation](#installation)
2. [Quick Start](#quick-start)
3. [Folder Structure](#folder-structure)
4. [Campaign Creator Tool](#campaign-creator-tool)
5. [How Campaigns Work](#how-campaigns-work)
6. [Campaign Settings](#campaign-settings)
7. [Team Setup Methods](#team-setup-methods)
8. [Team Fields](#team-fields)
9. [Skater Fields](#skater-fields)
10. [Goalie Fields](#goalie-fields)
11. [Colors and Uniforms](#colors-and-uniforms)
12. [Random Values](#random-values)
13. [Defaults and Fallbacks](#defaults-and-fallbacks)
14. [Save System and Testing](#save-system-and-testing)
15. [All Valid Values](#all-valid-values)
16. [Tips and Troubleshooting](#tips-and-troubleshooting)
17. [Planned Features](#planned-features)
18. [Sharing Campaigns](#sharing-campaigns)
19. [Building from Source](#building-from-source)

---

## Installation

### Requirements

- **Tape to Tape** — must be on the **EXPERIMENTAL BRANCH** on Steam
  (right-click the game → Properties → Betas → pick `experimental`).
- The Windows installer bundles BepInEx 6 (IL2CPP) automatically. On Linux
  you install BepInEx yourself — see the Linux bundle's `INSTALL.md`.

### Windows (recommended — use the installer)

1. Download **`T2T_Custom_Campaign_Framework_Setup.exe`** from the
   [latest release](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/releases/latest).
2. Run the installer. It auto-detects your Tape to Tape install (via Steam's
   `libraryfolders.vdf`), installs BepInEx 6 if missing, drops the DLL into
   `BepInEx/plugins/`, and installs the `T2T Campaign Creator` GUI with a
   desktop shortcut.
3. Launch Tape to Tape. The mod is active out of the box with the built-in
   Example Campaign.

The Campaign Creator checks for updates on startup. When a new version is
available you'll get a prompt — it downloads the installer and relaunches
automatically. You can also hit *Check for updates* on the Home tab.

### Linux / Steam Deck / Proton

1. Download **`T2T_Custom_Campaign_Framework_Linux_vX.Y.Z.zip`** from the
   [latest release](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/releases/latest).
2. Unzip and read `INSTALL.md` (or run the included `install.sh`).
3. The bundle contains the DLL, the `Custom Campaigns Mod/` folder, and the
   Python creator — the `.exe` is Windows-only.

### Manual install (advanced)

If you'd rather not use the installer:
1. Install BepInEx 6 IL2CPP (Windows x64 build) into your game folder.
2. Launch the game once, then quit.
3. Copy `CustomCampaignFramework.dll` into `GAMEFOLDER/BepInEx/plugins/`.
4. Copy the `Custom Campaigns Mod/` folder into `GAMEFOLDER/BepInEx/plugins/`.
5. Launch the game.

---

## Quick Start

**Easiest way: USE THE CAMPAIGN CREATOR GUI.** It walks you through every
step with forms, pickers, and live previews — no text editing needed.

- **Windows:** launch `T2T Campaign Creator` from the Start menu or desktop
  shortcut (installed by the setup exe).
- **Linux:**
  ```sh
  cd ~/.steam/steam/steamapps/common/Tape\ to\ Tape/BepInEx/plugins/Custom\ Campaigns\ Mod
  python3 creator_gui.py
  ```
  Requires Python 3.10+ and tkinter (`python3-tk` on Debian/Ubuntu).

**Manual path:** copy an existing campaign, rename it, edit it.

1. Open `BepInEx/plugins/Custom Campaigns Mod/campaigns/`.
2. Duplicate `Example Campaign` (or any blank template) and rename the copy.
3. Edit `active.txt` next to the templates: `active = My Campaign`.
4. Edit your campaign's `campaign.txt`, `team.txt` files, player files, etc.
5. Launch the game.

**Disable the mod:** set `active = default` in `active.txt` — the DLL loads
but registers no patches, so the game runs 100% vanilla.

---

## Folder Structure

```
BepInEx/plugins/
  CustomCampaignFramework.dll          The mod DLL
  Custom Campaigns Mod/                Everything you can edit
    T2T Campaign Creator.exe           Windows: launch the creator GUI
    creator_gui.py                     Linux: run via python3
    _game_data.py                      Data registry used by the GUI
    VERSION.txt                        Current installed version
    active.txt                         Which campaign to play
    defaults.txt                       Fallback values for missing fields
    save.txt                           Progress (auto-generated at runtime)
    campaigns/                         Your campaign folders live here
      Example Campaign/                Full 33-team NHL-style campaign
        campaign.txt                   Act sequence, map nodes, rules
        teams/                         Per-team .txt + players/
        player_teams/                  Optional custom player-selectable squads
      (more campaigns you create)
    library/                           Shared players + teams across campaigns
      players/
      teams/
```

---

## Campaign Creator Tool

The **T2T Campaign Creator** is a tkinter GUI that builds and edits every
part of a campaign — teams, players, goalies, skins, relics, talents,
reward pools, jersey colors, and logos — with pickers, live previews, and
jersey rendering. No text editing required.

- **Windows:** the installer puts `T2T Campaign Creator.exe` in the plugin
  folder and creates a Start Menu / desktop shortcut. Just launch it.
- **Linux / Steam Deck:**
  ```sh
  cd ~/.steam/steam/steamapps/common/Tape\ to\ Tape/BepInEx/plugins/Custom\ Campaigns\ Mod
  python3 creator_gui.py
  ```
  Requires Python 3.10+ and `tkinter` (`python3-tk` on Debian/Ubuntu,
  `python3-tkinter` on Fedora, `tk` on Arch/Steam Deck).

### What the GUI does

- **Home tab** — tree view of every campaign, team, player, and goalie.
  Double-click to edit. Right-click for delete / duplicate / rename.
- **Campaign editor** — set act sequence, Spartan/minigame toggles, choose
  active campaign, manage reward pools (which relics/talents the game is
  allowed to offer in random reward nodes).
- **Team editor** — identity, colors (with live swatches + jersey preview),
  uniform, relics, goalie, 5-skater lineup (with positional X/remove
  buttons), optional Line 2, import from any in-game team with one click.
- **Player editor** — face, size, handedness, skin tone, all stats,
  ability, talents, per-player uniform overrides.
- **Goalie editor** — face, all 14 stats, pads/blocker/glove skins, mask.
- **Player-team creator** — build the squad the player can pick in the
  *Choose Your Squad* menu. Starting relics (with Bench Bonus auto-added),
  starting head override, full lineup.
- **Library** — shared players and teams you can reuse across any campaign.
- **Import from game** — pulls a real in-game team's roster into your
  campaign as a starting point, which you then edit freely.
- **Export to Play Now** — writes your custom player into the game's save
  folder so you can use them in Play Now / online games.
- **Auto-updater** — checks GitHub on startup and at your request
  (Home tab → *Check for updates*). On a new release it downloads the
  installer with a progress bar and relaunches.

### Starting a campaign

1. Launch the Campaign Creator.
2. Home tab → *New Campaign*. Name it, pick an act sequence, and save.
3. Add teams: *New Team* or *Import Game Team* for each game slot.
4. Edit players and goalies per team as desired.
5. Home tab → *Active campaign* → pick your new campaign → *Set Active*.
6. Launch Tape to Tape. The mod loads your campaign automatically.

### Editing an existing campaign

- Double-click any node in the tree (campaign, team, player, goalie) to
  open its editor in a new tab. Edit as many at once as you want.
- Save in each tab. Use Ctrl+W or the × on the tab to close.
- Rebuilding a single team doesn't touch the rest — only what you save is
  changed.

### Manual text editing (optional)

Everything the GUI writes is plain `.txt`. Crack open any file under
`campaigns/<your campaign>/` and edit it directly if you prefer.

- `campaign.txt` — act sequence, gameplay toggles, team order.
- `teams/<team>/team.txt` — team identity, colors, uniform, relics.
- `teams/<team>/players/<Position> - <Name>.txt` — one file per skater or
  goalie with all their fields.
- `defaults.txt` — fallback values used when a field is missing.

See the sections below (Team Fields, Skater Fields, Goalie Fields, etc.)
for every field the text files accept.

---

## How Campaigns Work

### Act Sequence

Each number in `Act Sequence` creates one map. Must end with `3`.

| Act | Games | Notes |
|-----|-------|-------|
| 1 | 5 (challenges replaced) or 4 (kept) | Longest map |
| 2 | 3 | Standard map |
| 3 | 3 | Final — beating the boss ends the campaign |

Examples:
- `1, 2, 3` = 10 games
- `1, 1, 2, 2, 3` = 17-19 games
- `2, 2, 2, 2, 3` = 15 games

### Team Order

Teams play in the order they appear in `campaign.txt`. Team 1 = game 1, Team 2 = game 2, etc.

The **last game on each map is the boss**. Everything else is an elite. Plan difficulty accordingly.

For `Act Sequence = 1, 2, 3` with challenges replaced (10 games):
- Games 1-4: Map 1 (Act 1) — games 1-3 elites, game 4 boss
- Games 5-7: Map 2 (Act 2) — games 5-6 elites, game 7 boss
- Games 8-10: Map 3 (Act 3) — games 8-9 elites, game 10 **final boss**

### Section Headers

These headers tell the mod where player/goalie/relic sections start:

| Header | What It Does |
|--------|-------------|
| `--- Team Relics ---` | Relic names follow, one per line, no `=` sign |
| `--- Goalie ---` | Goalie fields follow |
| `--- Left Wing ---` | Left wing skater fields follow |
| `--- Right Wing ---` | Right wing skater fields follow |
| `--- Center ---` | Center skater fields follow |
| `--- Left Defense ---` | Left defense skater fields follow |
| `--- Right Defense ---` | Right defense skater fields follow |
| `--- Line 2 Left Wing ---` | Line 2 (boss teams only, 10-player slots) |

Other headers like `--- Team Colors ---` and `--- Team Uniform ---` are just labels for readability. They don't affect how fields work.

---

## Campaign Settings

These go at the top of `campaign.txt`:

```
--- Campaign Settings ---
Act Sequence            = 1, 2, 3
Replace Challenges      = yes
Replace Soccer Ball     = yes
Replace Golf Ball       = yes
```

| Setting | Values | Default | What It Does |
|---------|--------|---------|-------------|
| `Act Sequence` | Numbers ending in 3 | `1,1,2,2,1,2,2,2,3` | Map structure |
| `Replace Challenges` | `yes` / `no` / `1,2` | `yes` | Convert 3v3 Spartans to 5v5. Number list = only those acts |
| `Replace Soccer Ball` | `yes` / `no` | `yes` | Replace soccer ball with puck |
| `Replace Golf Ball` | `yes` / `no` | `yes` | Replace golf ball with puck |

---

## Team Setup Methods

### Import a Team

```
Import Team             = Vancouver
Team Name               = Vancouver Canucks
Stat Scale              = 1.5
```

Copies everything: players, stats, looks, logo, colors, uniform. `Stat Scale` multiplies all stats (1.0 = normal, 2.0 = double).

Special values: `PLAYER` (mirror match), `random` (random team each launch).

### Import Individual Players

```
--- Left Wing ---
Import Player           = Sven Pettersson
Speed                   = 100
```

Copies appearance and stats, then any field you set overrides. Use `random` for a random player.

### Logo Only

```
Logo From               = Vancouver
```

Grabs logo and default colors. You define everything else.

### Fully Manual

Define every field yourself. See the field references below.

---

## Team Fields

These go under `## TEAM ##` headers, outside any player section.

### Identity

| Field | Values | What It Does |
|-------|--------|-------------|
| `Team Name` | any text | Display name |
| `City` | any text | City shown in-game |
| `Abbreviation` | 3 letters | Scoreboard code |
| `Logo From` | team name / `random` | Borrow logo from in-game team |
| `Import Team` | team name / `PLAYER` / `random` | Import entire team |
| `Stat Scale` | 0.1 to 3.0 | Multiply imported stats |

### Team Colors

All colors: `R, G, B` (0-255 each), `random`, or `random(min,max)` per channel. See [Colors and Uniforms](#colors-and-uniforms).

**Jersey** (3 colors): `Jersey Primary`, `Jersey Secondary`, `Jersey Accent`

**Away Jersey** (3 colors): `Away Primary`, `Away Secondary`, `Away Accent`

**Helmet** (3 colors): `Helmet Color`, `Helmet Secondary Color`, `Helmet Tertiary Color`

**Gloves** (3 colors): `Gloves Color`, `Gloves Secondary Color`, `Gloves Tertiary Color`

**Pants** (3 colors): `Pants Color`, `Pants Secondary Color`, `Pants Tertiary Color`

**Skates** (3 colors): `Skates Color`, `Blade Color`, `Laces Color`

**Socks** (3 colors): `Socks Color`, `Socks Secondary Color`, `Socks Tertiary Color`

**Numbers** (2 colors): `Number Color`, `Number Secondary Color`, `Number Color Home`, `Number Color Away`

**Other**: `Bicep Color`, `Stick Color` (only with team stick skin), `Transition Primary/Secondary/Tertiary` (screen wipe effect)

Team colors are defaults for all players. Per-player color overrides take priority.

### Team Uniform

Sets what each equipment piece looks like. You can write a skin name OR an RGB color directly.

Writing RGB directly (e.g. `Gloves = 0, 51, 160`) automatically uses the default skin with that color.

| Field | Options |
|-------|---------|
| `Body` / `Body Away` | `standard`, `tycoons`, `princess`, `golfers`, `prisoners`, `mountaineers`, `hockey fc`, `figure skaters`, `referee`, `random body`, or RGB |
| `Helmet` / `Helmet Away` | `team colors`, `cage`, `random helmet`, or RGB |
| `Stick` | `black`, `gold`, `red`, `purple`, `teal`, `red gold`, `sword`, `golf`, `team stick`, `random stick`, or RGB |
| `Skates` / `Skates Away` | `standard`, `random skates`, or RGB |
| `Gloves` / `Gloves Away` | RGB color or blank (always uses default skin) |
| `Pants` / `Pants Away` | RGB color or blank (always uses default skin) |
| `Bicep` / `Bicep Away` | RGB color or blank (always uses default skin) |

### Team Gameplay

| Field | Values | What It Does |
|-------|--------|-------------|
| `Bench Size` | 0-10 | Extra bench players |
| `Team Random Talents` | 0-10 | Give N random talents to every player |
| `Team Random Pool` | talent list / `all` | Which talents to pick from |

### Team Relics

Under `--- Team Relics ---`. One name per line, no `=` sign. Add `:2` for level 2:

```
--- Team Relics ---
sorest_loser
bolt:2
```

See [All Valid Values](#all-valid-values) for the full relic list.

---

## Skater Fields

These go under position sections: `--- Left Wing ---`, `--- Right Wing ---`, `--- Center ---`, `--- Left Defense ---`, `--- Right Defense ---`.

Line 2 sections (`--- Line 2 Left Wing ---`, etc.) use the same fields for 10-player boss teams.

### Identity and Stats

| Field | Values | What It Does |
|-------|--------|-------------|
| `Name` | First Last | Player name |
| `Number` | 1-99 | Jersey number |
| `Import Player` | name / `random` | Copy from in-game player |
| `Speed` | 0-999 | Skating speed |
| `Shot Power` | 0-999 | Shot power |
| `Accuracy` | 0-999 | Shot accuracy |
| `Checking` | 0-999 | Body checking |

All stats accept `random(min, max)`. Base game range is ~30-80.

### Appearance

| Field | Values | What It Does |
|-------|--------|-------------|
| `Face` | face name / `random` | Head model |
| `Left Handed` | `yes` / `no` / `random` | Handedness |
| `Skin Color` | `light` / `dark` / `random` | Skin tone |
| `Size` | `ExtraSmall` / `Small` / `Medium` / `Big` / `ExtraBig` / `ExtraExtraBig` / `random` | Body size |
| `Size Offset` | 0.9 to 1.1 | Fine-tune size |
| `Glasses` | glasses skin name | Eyewear |

### Abilities and Talents

| Field | Values | What It Does |
|-------|--------|-------------|
| `Ability` | ability name | One special ability with cooldown |
| `Talents` | comma-separated list | Passive powers always applied |
| `Random Talents` | 0-10 | Number of random talents to give |
| `Random Pool` | talent list / `all` | Where to pick random talents from |

Level 2 talents: `Talents = Charge Shot (Level 2)`

See [All Valid Values](#all-valid-values) for every face, ability, and talent name.

### Per-Player Uniform Overrides

Override the team's default for this player. Leave blank to use team defaults. Can also write RGB directly.

| Field | Options |
|-------|---------|
| `Stick` | `black`, `gold`, `red`, `purple`, `teal`, `red gold`, `sword`, `golf`, `team stick`, `random stick` |
| `Helmet` / `Helmet Away` | `team colors`, `cage`, `random helmet`, or RGB |
| `Body` / `Body Away` | `standard`, `tycoons`, `princess`, etc., or RGB |
| `Skates` / `Skates Away` | `standard`, `random skates`, or RGB |
| `Gloves` / `Gloves Away` | RGB or blank |
| `Pants` / `Pants Away` | RGB or blank |
| `Bicep` / `Bicep Away` | RGB or blank |

### Per-Player Color Overrides

Override team colors for this player only. All use `R, G, B` (0-255) or `random`.

| Equipment | Color Fields |
|-----------|-------------|
| **Jersey** | `Jersey Color`, `Jersey Secondary Color`, `Jersey Accent Color` |
| **Helmet** | `Helmet Color`, `Helmet Secondary Color`, `Helmet Tertiary Color` |
| **Gloves** | `Gloves Color`, `Gloves Secondary Color`, `Gloves Tertiary Color` |
| **Pants** | `Pants Color`, `Pants Secondary Color`, `Pants Tertiary Color` |
| **Skates** | `Skates Color`, `Blade Color`, `Laces Color` |
| **Socks** | `Socks Color`, `Socks Secondary Color`, `Socks Tertiary Color` |
| **Numbers** | `Number Color`, `Number Secondary Color` |
| **Bicep** | `Bicep Color` |

---

## Goalie Fields

These go under `--- Goalie ---`.

| Field | Values | What It Does |
|-------|--------|-------------|
| `Name` | First Last | Goalie name |
| `Face` | face name / `random` | Head model |
| `Import Player` | goalie name / `random` | Import from in-game goalie |

### Stats

All accept `random(min, max)`. Base game range is ~30-80.

| Stat | Range | What It Does |
|------|-------|-------------|
| `Skill` | -100 to 999 | Overall modifier |
| `Catching` | 0-999 | Catching ability |
| `Glove` | 0-999 | Glove-side saves |
| `Blocker` | 0-999 | Blocker-side saves |
| `Five Hole` | 0-999 | Five hole coverage |
| `Standing Speed` | 0-999 | Movement while standing |
| `Butterfly Speed` | 0-999 | Movement in butterfly |
| `Control` | 0-999 | Rebound control |
| `Recovery` | 0-999 | Recovery speed |
| `Pass Power` | 0-999 | Pass strength |
| `Shot Power` | 0-999 | Shot strength |
| `Poke Check` | 0-999 | Poke check skill |
| `Depth` | 0-999 | Positioning depth |
| `Pass Read` | 0.0-1.0 | Pass anticipation |

### Goalie Talents

```
Goalie Talents          = Always Catch Pucks, Goalie Enraged On Goal
```

### Goalie Skins (Optional)

`Skin`, `Skin Away`, `Glove Skin`, `Glove Away`, `Blocker Skin`, `Blocker Away`, `Pads Skin`, `Pads Away`, `Stick Skin`, `Stick Away`, `Helmet Skin`, `Logo Skin`

---

## Colors and Uniforms

### How Colors Work

Colors use `R, G, B` format (0-255): `Jersey Primary = 0, 51, 160`

Special values:
- `random` — fully random color
- `random(min,max)` per channel — e.g. `random(100,255), random(0,50), random(0,255)`

**Priority:** Per-player colors > team colors > imported team colors > defaults

### How Uniforms Work

Uniform fields control what the equipment looks like. There are two types:

- **Skins with set looks** — `tycoons`, `princess`, `cage`, `black`, `sword`, etc. — these have a fixed texture and ignore color settings
- **Color-driven** — when you write RGB values or use `standard`/`team colors`/`team stick`, the equipment uses your color settings

**Shortcut:** Write RGB directly in any uniform field (e.g. `Body = 0, 51, 160`) to set the primary color. For equipment with multiple color channels (gloves, pants, helmet, skates, socks), use the separate color fields to set all 3:
```
Gloves Color            = 0, 51, 160
Gloves Secondary Color  = 255, 255, 255
Gloves Tertiary Color   = 0, 0, 0
```

**Blank fields:** The mod applies safe defaults so nothing is invisible.

---

## Random Values

Use `random(min, max)` anywhere you'd put a number:
Use `random` anywhere you'd put a face skin ect..:

```
Speed                   = random(40, 90)
Pass Read               = random(0.3, 0.7)
Jersey Primary          = random
Face                    = random
Import Team             = random
```

Random values are resolved once when the campaign loads — they stay the same for that run.

---

## Defaults and Fallbacks

`defaults.txt` in the Campaigns folder sets what values are used when a field is blank, misspelled, or missing. You can customize it to change what "default" looks like.

If a player has no name, the default name is used. If a color is missing, the default color is used. This prevents invisible players or broken equipment.

---

## Save System and Testing

Progress saves to `save.txt` as `ActsCompleted,GamesPlayed`.

| save.txt | What Happens |
|----------|-------------|
| `0,0` | Start from beginning |
| `0,4` | Jump to game 5 |
| `1,5` | Map 2, game 6 |

Delete `save.txt` or set to `0,0` to reset. Only resets automatically after beating the final boss.

### Testing a Specific Team

Set `GamesPlayed` in `save.txt` to team number minus 1 (test team 15 = write `0,14`).

### Enabling the Debug Console

To see mod logs and errors, enable the BepInEx console:

1. Open `BepInEx/config/BepInEx.cfg` (generated after first launch with BepInEx)
2. Find the `[Logging.Console]` section
3. Set `Enabled = true`
4. Save and launch the game — a console window will open alongside the game

```
[Logging.Console]
Enabled = true
```

Key log lines to look for:
- `[Config] Loaded X teams` — campaign loaded successfully
- `[Season] Next up: Game N` — which team plays next
- `[Config] Applied manual team` — team config was applied
- `[WARN]` — something needs fixing in your config

---

## All Valid Values

All face names, abilities, talents, relics, and team names are listed in `VALID_VALUES.txt` in the campaign creator folder. That file has 20 sections covering every accepted value for every field.

Quick reference for common fields:

### Team Names (for Import Team / Logo From)

**League:** Anaheim, Boston, Buffalo, Calgary, Carolina, Chicago, Colorado, Columbus, Dallas, Detroit, Edmonton, Florida, Long Island, Los Angeles, Minnesota, Montreal, Nashville, New Jersey, New York, Ottawa, Philadelphia, Pittsburgh, San Jose, Seattle, St-Louis, Tampa Bay, Toronto, Utah, Vancouver, Vegas, Washington, Winnipeg

**Campaign:** Calaveras, Greasy Lettuce, Top Cheese, Meatballs, The Officials, Crusaders, Princess, Cup Cultists, Mountaineers, Disco, Golfers, Hockey FC, Shooting Stars, Team Canada, Tycoons, Prisoners, Spartans

**Special:** `PLAYER` (mirror match), `random` (random each launch)

### Player Sizes

ExtraSmall, Small, Medium, Big, ExtraBig, ExtraExtraBig, random

### Common Colors

`255,0,0` Red | `0,255,0` Green | `0,0,255` Blue | `255,255,0` Yellow | `255,255,255` White | `0,0,0` Black | `128,128,128` Grey | `255,165,0` Orange | `128,0,128` Purple

---

## Included Campaigns

The release comes with these campaigns ready to use or copy as starting points:

| Campaign | What It Is |
|----------|-----------|
| **Example Campaign** | A full 33-team NHL-style season. Every team is manually configured with real NHL-inspired rosters, stats that scale from weakest to strongest, custom colors, uniforms, talents, and relics. Great reference for how a complete campaign looks. |
| **Blank Campaign** | Every possible field shown blank for all 10 games (Act 1, 2, 3). Copy this and fill in what you want — anything left blank uses defaults. |
| **Blank Import Teams** | Fastest manual setup. Just fill in team names and stat scales — one line per team. |
| **Blank Import Players** | Import individual players from any in-game team. Mix and match rosters. |

Copy any of these, rename the folder, and edit `active.txt` to point to your copy.

---

## Tips and Troubleshooting

- **USE THE CAMPAIGN CREATOR** — run `Create Campaign.bat` (Windows) or `python3 create_campaign.py` (Mac/Linux) to build campaigns without editing text files. Requires Python installed. 
- **Start with `Blank Import Teams`** if you prefer manual text editing
- **Check `VALID_VALUES.txt`** for every valid talent, relic, ability, and face name
- **Test with `Act Sequence = 2, 3`** (6 games) before building a long campaign
- **Bosses are the last game per map** — give them higher stats and more relics
- **Mirror match finale** (`Import Team = PLAYER`) makes a great final boss
- **Write RGB directly** in uniform fields — no need to type `standard` first
- **Blank fields are safe** — the mod applies defaults so nothing is invisible
- **Act 3 must be last** and can only appear once
- **Level 2 talents:** `Talent Name (Level 2)` — **Level 2 relics:** `relic_name:2`

---

## Planned Features

These are being developed for future versions:

- **Talent Pool Editor** — toggle which talents the player finds during runs
- **Relic Pool Editor** — toggle which relics the player finds during runs
- **Starting Squad Editor** — customize the 4 starting squads and pool players
- **Custom logos** — import custom logo images

---

## Sharing Campaigns

1. Copy your campaign folder
2. Delete `save.txt` from the copy
3. Share the folder

The recipient puts it in `BepInEx/plugins/Campaigns/` and edits `active.txt`.

---

## Building from Source

Source code is in the `src/` folder. Most users should download the release instead.

```bash
dotnet build src/EndlessMode/EndlessMode.csproj
cp src/EndlessMode/bin/Debug/net6.0/CustomCampaignFramework.dll "path/to/BepInEx/plugins/"
```

Requires .NET 6 SDK and BepInEx 6 IL2CPP references.

---

**Tape to Tape** by Excellent Rectangle | Built with BepInEx and HarmonyX | MIT License
