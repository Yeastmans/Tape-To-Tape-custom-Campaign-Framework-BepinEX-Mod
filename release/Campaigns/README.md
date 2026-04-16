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
- [BepInEx 6 for IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) — **Use the latest experimental build** (Unity IL2CPP, Windows x64). The stable release does not support IL2CPP games.

### Steps

1. **Download the latest release** from the Releases page. Do NOT clone the repository 
2. Install BepInEx 6 into your Tape to Tape game folder (extract to root game folder).
3. Run the game once, then close it.
4. From the release, copy `CustomCampaignFramework.dll` into `GAMEFOLDER/BepInEx/plugins/`.
5. From the release, copy the `Campaigns` folder into `GAMEFOLDER/BepInEx/plugins/`.
6. Launch the game.

---

## Quick Start

**Easiest way: USE THE CAMPAIGN CREATOR.** It walks you through every step. No text editing needed.
- **Windows:** Double-click `Create Campaign.bat` in the campaign creator folder
- **Mac/Linux:** Open a terminal in the campaign creator folder and run `python3 create_campaign.py` 

**Manual path:** Copy an existing campaign, rename it, edit it.

1. Go to `BepInEx/plugins/Campaigns/`.
2. Copy `Example Campaign Or one of the blank templates` and rename the copy.
3. Edit `active.txt`: `Active Campaign = My Campaign`
4. Edit `My Campaign/campaign.txt`.
5. Launch the game.

**Even faster manually:** Use the `Blank Import Teams` template — just fill in team names and stat scales.

**Disable the mod:** Set `Active Campaign = default` in `active.txt` to play the base game.

---

## Folder Structure

```
BepInEx/plugins/
  CustomCampaignFramework.dll
  Campaigns/
    active.txt                         Which campaign to play
    defaults.txt                       Fallback values for missing/broken fields
    CAMPAIGN CREATOR.../               Campaign builder tool + value reference
        create_campaign.py             Interactive campaign builder (Python)
        Create Campaign.bat            Windows launcher (double-click to run)
        VALID_VALUES.txt               Every valid value for every field
    Example Campaign/                  33-team NHL-style campaign
        campaign.txt                   Teams and campaign structure
        save.txt                       Progress (auto-generated)
    Random Campaign/                   Fully randomized chaos campaign
    Nightmare Mode/                    Brutal difficulty campaign
    Blank Campaign/                    Every field shown blank
    Blank Import Teams/                Import-only quick setup
    Blank Import Players/              Import individual players
```

---

## Campaign Creator Tool

The campaign creator is an interactive script that walks you through building a complete campaign step by step. It asks questions, you answer, and it generates the `campaign.txt` file for you. **No text file editing needed.**

### Installing Python (Required)

The creator is a Python script. You need Python installed:

- **Windows:** Install from the [Microsoft Store](https://apps.microsoft.com/detail/python-3) (search "Python") or from [python.org](https://www.python.org/downloads/). Both are free.
- **Mac:** Python 3 is usually pre-installed. If not, install from [python.org](https://www.python.org/downloads/) or run `brew install python3`.
- **Linux:** Usually pre-installed. If not: `sudo apt install python3` (Ubuntu/Debian) or `sudo dnf install python3` (Fedora).

To check if Python is installed, open a terminal and type `python3 --version` or `py --version`.

### Running the Creator

The script is in the campaign creator folder inside `Campaigns/`.

**Windows:**
1. Navigate to `BepInEx/plugins/Campaigns/` and open the campaign creator folder
2. Double-click `Create Campaign.bat`
3. A terminal window opens and the creator starts

**Mac / Linux:**
1. Open a terminal
2. Navigate to the campaign creator folder:
   ```
   cd "/path/to/Tape to Tape/BepInEx/plugins/Campaigns/CAMPAIGN CREATOR AND DOCUMENTATION VERY IMPORTANT"
   ```
3. Run the script:
   ```
   python3 create_campaign.py
   ```

### Step-by-Step Walkthrough

When you run the creator, here's exactly what happens:

**1. Main Menu** — Choose `1` to create a new campaign or `2` to edit an existing one.

**2. Campaign Name** — Type a name. This becomes the folder name (e.g. `My Campaign` creates `Campaigns/My Campaign/`).

**3. Act Sequence** — Define your campaign structure. You type a list of numbers like `1, 2, 3`. Each number is one map. The creator explains what each number means and validates your input. Act 3 must be the last number and can only appear once.

**4. Spartan Replacement** — For each Act 1 map, choose whether to replace the 3v3 Spartan challenge with a regular 5v5 game. If you have multiple Act 1 maps, you choose for each one individually.

**5. Minigame Settings** — Choose whether to replace soccer balls and golf balls with regular pucks.

**6. Build Each Team** — For every game in your campaign, the creator asks you to set up a team. You pick one of 4 methods:

| Method | What You Do | Best For |
|--------|------------|----------|
| **1. Import** | Pick an in-game team name, set stat scale, add relics/talents | Quick setup, realistic teams |
| **2. Manual** | Set everything: name, city, colors, uniform, every player stat | Full creative control |
| **3. Mirror Match** | Clones the player's own team as the opponent | Fun boss fights |
| **4. Random** | Random team each time you play | Chaos, replayability |

**7. For each player on a manual team**, you choose:
- **Import** — type a player name to copy their look and stats, or type `random`
- **Manual** — set name, number, face, size, stats, ability, talents, uniform, colors
- **Skip** — leave this position empty (mod fills in defaults)

**8. Line 2** — For every team, the creator asks if you want to add 5 extra players (10-player roster). Boss teams get a reminder that this is recommended for teams like Tycoons.

**9. Save** — The creator writes `campaign.txt` to your campaign folder. Set it as active in `active.txt` and launch the game.

### Editing an Existing Campaign

Choose option `2` from the main menu. The creator:
1. Lists all campaigns that have a `campaign.txt`
2. Shows every team in the campaign by name
3. Lets you pick what to do:
   - Type a **team number** to rebuild that team from scratch
   - Type **s** to edit campaign settings (act sequence, toggles)
   - Type **d** to save all changes and exit
   - Type **q** to quit without saving

You can rebuild as many teams as you want. Only rebuilt teams are changed — everything else stays the same.

### How Colors Work in the Creator

Whenever the creator asks for a color:
- Type `R,G,B` — three numbers 0-255, e.g. `255,0,0` for red, `0,0,255` for blue
- Type `random` — random color each game
- Press **Enter** — skip, uses team default or fallback color

### Reference File

`VALID_VALUES.txt` in the same folder lists every valid value for every field — all face names, abilities, talents, relics, team names, sizes, and color examples. The creator tells you which section to check when relevant.

### Full Tutorial: Building Your First Campaign

This walks through every screen you'll see when creating a campaign from scratch.

---

**SCREEN 1 — Main Menu**
```
1. Create new campaign
2. Edit existing campaign
Pick 1-2 [1]:
```
Type `1` and press Enter.

---

**SCREEN 2 — Campaign Name**
```
Campaign name [My Campaign]:
```
Type a name for your campaign. This creates a folder with that name. Example: `NHL Remix`

---

**SCREEN 3 — Act Sequence**
```
---- ACT SEQUENCE ----
Your campaign is built from maps. Each map is one number.

MAP TYPES:
  1 = Act 1 — longest map. 4 games normally, 5 if Spartans replaced.
  2 = Act 2 — 3 games per map.
  3 = Act 3 — 3 games, FINAL BOSS. MUST be the last number.

Act Sequence [1, 2, 3]:
```
Type your map sequence. Examples:
- `1, 2, 3` — short campaign (~10 games)
- `1, 1, 2, 2, 3` — medium (~17 games)
- `1, 2, 1, 2, 2, 3` — long (~20 games)

**Rules:** Must end with `3`. Only one `3` allowed. The creator will reject invalid input and ask again.

---

**SCREEN 4 — Spartan Replacement**

If you have Act 1 maps, the creator asks whether to replace the 3v3 Spartan challenges with regular 5v5 games. If you have multiple Act 1s, it asks for each one:
```
Map 1 (Act 1 #1) — replace Spartans with 5v5? (yes/no) [yes]:
Map 4 (Act 1 #2) — replace Spartans with 5v5? (yes/no) [yes]:
```
Replacing adds 1 extra game per Act 1 map (you'll need more teams).

---

**SCREEN 5 — Minigame Settings**
```
Replace soccer ball with puck? (yes/no) [yes]:
Replace golf ball with puck? (yes/no) [yes]:
```
These replace the special balls in soccer/golf minigames with regular pucks.
:NOTE TO USE THE HOCKEY AND SOCCER ARENAS AND TEAMS YOU HAVE TO IMPORT THE GOLFERS OR HOCKEY FC. it will always be ice with custom teams. only ball is changed with these settings 
---

**SCREEN 6 — Team Count Summary**
```
Your campaign: 3 maps, 10 games, 10 teams needed.
```
Now you build each team one by one.

---

**SCREEN 7 — Team Setup (repeats for each team)**
```
##################################################
  TEAM 1 (Act 1)
##################################################

How to set up this team?
  1. Import an in-game team (easiest)
  2. Build manually (full control)
  3. Mirror match (clone player's own team)
  4. Random team (different each launch)
Pick 1-4 [1]:
```

**If you pick 1 (Import):**
```
Team name to import:
```
Type a team name. Open `VALID_VALUES.txt` Section 17 for all valid names. Examples: `Vancouver`, `Chicago`, `Colorado`, `Toronto`
```
Display name [Vancouver]:  (change imported teams name)
Stat Scale (1.0=normal) [1.0]:
Random talents for every player (0=none) [0]:
Relics (comma-separated, Enter=none):
Bench size (0-10, Enter=default):
```
- **Stat Scale** — `1.0` = normal stats, `1.5` = 50% stronger, `2.0` = double, `0.5` = half
- **Random talents** — give every player on this team N random talents. Type a number or `0` for none
- **Relics** — comma-separated relic names. See `VALID_VALUES.txt` Section 16. Example: `sorest_loser, bolt:2`
- **Bench size** — extra bench players (0-10)

That's it for imported teams — the rest is automatic.

**If you pick 2 (Manual):** the creator walks through identity, colors, uniform, relics, goalie, and all 5 skaters. See below.

**If you pick 3 (Mirror):** just sets a display name. The team clones the player's roster.

**If you pick 4 (Random):** sets stat scale and optional random talents. Different team each launch.

---

**MANUAL TEAM — Identity**
```
Team name:
City:
Abbreviation (3 letters):
Logo From:
```
- **Logo From** — borrow a logo from an in-game team. See `VALID_VALUES.txt` Section 17. Type `random` for random each game, or press Enter for none.

---

**MANUAL TEAM — Team Colors**

The creator asks for RGB colors for each equipment piece. Colors are optional — press Enter to skip any of them.

```
JERSEY (3 colors):
  Jersey Primary (main body) [R,G,B | random | Enter=skip]:
  Jersey Secondary (trim) [R,G,B | random | Enter=skip]:
  Jersey Accent (detail) [R,G,B | random | Enter=skip]:
```
Then optionally away jersey (3 colors), equipment colors (helmet, gloves, pants, skates, socks — each with 3 channels), bicep, number, and transition colors.

**Tip:** See `VALID_VALUES.txt` Section 8 for common color values like `255,0,0` = red, `0,0,255` = blue.

---

**MANUAL TEAM — Uniform**

Pick what each equipment piece looks like:
```
Body:
  1. Standard (colorable - takes RGB)
  2. Tycoons (business suit - fixed look)
  3. Princess (armored - fixed look) ect
  See `VALID_VALUES.txt
  ... 
Pick number [1]:
```
For body, helmet, and stick, you pick a skin from a numbered list. See `VALID_VALUES.txt` Sections 1-4 for all options.

- **Colorable skins** (`standard`, `team colors`, `team stick`) — the creator asks for an RGB color after you pick
- **Fixed skins** (`tycoons`, `cage`, `black`, etc.) — have their own look, no color needed

Gloves, pants, and bicep just ask for RGB directly (they always use the default model).

Skates always ask for 3 colors: body, blade, laces.

---

**MANUAL TEAM — Gameplay & Relics**
```
Random talents for EVERY player on this team (0=none) [0]:
Relics (Enter=none):
```
- **Random talents** — every player gets N random talents. If non-zero, asks which pool to pick from (`all` = entire game, or a comma-separated list)
- **Relics** — comma-separated. See `VALID_VALUES.txt` Section 16. Add `:2` for level 2 (e.g. `bolt:2`)

---

**MANUAL TEAM — Goalie**
```
Import goalie or build manual? (import/manual) [manual]:
```
**Import:** type a goalie name (see `VALID_VALUES.txt` Section 17 for teams, or type `random`)

**Manual:** set name, face (see `VALID_VALUES.txt` Section 7), then all 14 stats:
```
Skill (overall modifier) [50]:
Catching [50]:
Glove (glove-side saves) [50]:
Blocker (blocker-side saves) [50]:
Five Hole [50]:
Standing Speed [50]:
Butterfly Speed [50]:
Control (rebound control) [50]:
Recovery [50]:
Pass Power [50]:
Shot Power [50]:
Poke Check [50]:
Depth (positioning) [50]:
Pass Read (0.0-1.0, higher=better) [0.5]:
```
Stats range 0-999. Base game is ~30-80. Then optionally set goalie talents (see `VALID_VALUES.txt` Section 15).

---

**MANUAL TEAM — Skaters (5 positions)**

For each position (Left Wing, Right Wing, Center, Left Defense, Right Defense):
```
Import player or build manual? (import/manual/skip) [manual]:
```

**Import:** type a player name or `random`. See any in-game team's roster. Optionally override display name and stats.

**Manual:**
```
Player name (First Last) [Player]:
Jersey number (1-99) [88]:
Face [random]:
Left handed? (yes/no/random) [no]:
Skin tone (light/dark/random) [light]:
Player size: (pick 1-7)
Speed [50]:
Shot Power [50]:
Accuracy [50]:
Checking [50]:
Ability:
Talents:
Random talents per game (0=none) [0]:
```
- **Face** — see `VALID_VALUES.txt` Section 7 for all face names. Type `random` for random each game.
- **Size** — pick from ExtraSmall to ExtraExtraBig. See `VALID_VALUES.txt` Section 6.
- **Stats** — 0-999. Use `random(min,max)` for random (e.g. `random(40,90)`).
- **Ability** — one ability name. See `VALID_VALUES.txt` Section 13. Press Enter for none.
- **Talents** — comma-separated talent names. See `VALID_VALUES.txt` Section 14. Press Enter for none.
- **Random talents** — if non-zero, asks for pool (`all` or a talent list).

Then optionally customize uniform (stick, helmet, body, skates, gloves, pants, bicep with colors) and per-player color overrides.

**Skip:** omits this position. The mod fills it with defaults from `defaults.txt`.

---

**LINE 2 — Extra Players**
```
---- LINE 2 (5 extra players) ----
RECOMMENDED: This is a FINAL BOSS team.
Add Line 2 players? (yes/no) [yes]:
```
Line 2 adds 5 extra players for a 10-player roster. The creator gives context:
- **Final bosses (Act 3):** Recommended — many boss teams use 10 players
- **Other bosses:** Available but optional
- **Regular teams:** Available for immersion or Tycoons-style teams

---

**SCREEN 8 — Save**
```
==================================================
  SAVED!
  Folder: .../Campaigns/NHL Remix
  File:   campaign.txt
==================================================

To play, edit active.txt:
  Active Campaign = NHL Remix
```

Your campaign is ready. Open `active.txt`, set your campaign name, and launch the game.

---

### Tips for Using the Creator Effectively

- **Start small.** Your first campaign should be `1, 2, 3` (10 games). You can always make longer ones later.
- **Import first, customize later.** Use option 1 (Import) for most teams, then edit the `campaign.txt` by hand to tweak specific things.
- **Keep `VALID_VALUES.txt` open** in another window while using the creator. You'll reference it for face names, talents, abilities, relics, and team names.
- **Press Enter to accept defaults.** Most prompts have a default value in brackets `[like this]`. Just press Enter to use it.
- **Use `random` liberally.** Typing `random` for faces, colors, and stats makes campaigns that feel different every time.
- **Stat scale is your friend.** When importing teams, stat scale controls difficulty. Start at `0.8` for early games, ramp to `1.5-2.0` for bosses.
- **Edit mode for tweaks.** Made a mistake on one team? Use Edit mode (option 2) to rebuild just that team without redoing the whole campaign.
- **Boss teams should be harder.** Give bosses higher stat scales, more relics, more talents, and Line 2 players.

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
