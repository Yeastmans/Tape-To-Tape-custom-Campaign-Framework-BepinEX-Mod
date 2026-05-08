# Tape to Tape — Custom Campaign Framework

Build fully custom campaigns for **Tape to Tape** (the hockey roguelike by Excellent Rectangle). Design your own teams, players, stats, appearances, talents, abilities, relics, and campaign structure using the included **T2T Campaign Creator** — no coding required.

---

## Installation

1. Download `T2T_Custom_Campaign_Framework_Setup.exe` from the [latest release](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/releases/latest)
2. Run the installer — it auto-detects your Tape to Tape install via Steam
3. The installer will:
   - Install BepInEx 6 (IL2CPP) if not already present
   - Copy the mod DLL to `BepInEx/plugins/`
   - Copy the Campaign Creator tool to `BepInEx/plugins/Custom Campaigns Mod/`
   - Create a desktop shortcut to **T2T Campaign Creator**
4. Launch the game once to let BepInEx initialize — you should see a new `BepInEx/` folder in your game directory

**Linux:** Download the `.zip` from the same release page and follow `INSTALL.md` inside.

---

## Campaign Creator

Launch **T2T Campaign Creator** from your desktop shortcut (or from `BepInEx/plugins/Custom Campaigns Mod/`).

The tool is a GUI application that handles everything — you don't need to edit text files manually.

### What you can do

**Campaign tab**
- Set campaign name, act sequence, and options
- Act sequence controls how many maps and what type each one is (Act 1 = easy, Act 2 = medium, Act 3 = boss)
- Toggle Spartan replacement (replace challenge mini-games with full elite matches)
- Toggle soccer/golf ball replacement
- Enable player-controlled squads

**Teams tab**
- Add, remove, and drag-to-reorder teams
- Set team name, city, abbreviation, jersey colors (home + away), logo, accent colors
- Set per-slot equipment colors: helmet, gloves, pants, skates, socks, numbers, bicep, stick
- Import from a base game team to pre-fill everything
- Add team relics, team-wide random talents, and talent pool restrictions

**Player editor**
- Full skater editor: name, number, position, stats (speed, shot power, accuracy, checking), face, body skin, stick skin, size, handedness, skin color
- Talent, ability, and individual color overrides per player
- Goalie editor: all goalie stats and skin slots (helmet, pads, glove, blocker, stick, body)
- Import from a base game player to pre-fill stats and appearance
- Roster preview showing all 5 skaters + goalie with their overall ratings, talents, and abilities

**Player Teams tab**
- Create and edit player-controlled squads with custom players and starting relics
- Set squad head (face shown on the squad select screen)
- Draft pool management — players available to find as free agents mid-run

**Community tab**
- Browse and download campaigns shared by other players
- Share your own campaign with the community

### Auto-updater

The Creator checks for new versions on launch. When an update is available, it downloads and runs the installer automatically.

---

## Active Campaign

The mod reads which campaign to run from:

```
BepInEx/plugins/Custom Campaigns Mod/active.txt
```

Format:
```
Active Campaign = My Campaign Name
```

The campaign folder must exist at:
```
BepInEx/plugins/Custom Campaigns Mod/campaigns/My Campaign Name/
```

You can switch campaigns directly in the Creator's home tab.

---

## Reporting Issues

Report bugs and feedback on the [GitHub Issues page](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/issues) or message **@yeastmann** on Discord.
