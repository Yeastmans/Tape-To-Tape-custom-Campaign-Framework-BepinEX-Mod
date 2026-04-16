# T2T Custom Campaign Framework

A BepInEx mod for **Tape to Tape** that lets you create fully custom campaigns with a visual editor. Design teams, players, uniforms, stats, talents, relics, and entire campaign runs — no coding required.

## Download & Install

1. Download **`T2T_Custom_Campaign_Framework_Setup.exe`** from the [latest release](../../releases/latest).
2. Run the installer — it auto-detects your Tape to Tape install and installs everything (including BepInEx if needed).
3. Launch Tape to Tape once so the mod can scan the game's teams and players.
4. Open **T2T Campaign Creator** from the desktop shortcut the installer created.
5. Set "Example Campaign" as active and play!

## What You Get

- **T2T Campaign Creator** — a tabbed visual editor for campaigns, teams, and players. No file editing needed.
- **Import from game** — pick any in-game team or player from a dropdown and clone them into your campaign. Customize anything you want.
- **Full campaign control** — set the act sequence, replace Spartan challenges per-map, choose relics, control uniform skins and colors.
- **Live preview** — jersey color swatches update in real time as you pick colors.
- **Validation** — the editor warns you about problems before you save.
- **Library system** — save players and teams to a shared library, reuse them across campaigns.
- **Example Campaign** — a full 33-team NHL-style campaign ships out of the box.

## How It Works

The mod consists of two parts:

| File | What it does |
|------|-------------|
| `CustomCampaignFramework.dll` | BepInEx plugin that loads custom campaigns at runtime |
| `Custom Campaigns Mod/` | Content folder with the Creator tool, campaigns, and library |

The Creator tool writes plain-text config files. The plugin reads them when the game launches and replaces teams, players, colors, and campaign structure.

## Folder Structure

```
Tape to Tape/
  BepInEx/
    plugins/
      CustomCampaignFramework.dll
      Custom Campaigns Mod/
        T2T Campaign Creator.exe
        active.txt
        campaigns/
          Example Campaign/
            campaign.txt
            teams/01 Vancouver/team.txt + players/...
        library/
          players/
          teams/
```

## First Launch

After installing, **launch Tape to Tape once** before using the Creator. The mod automatically scans all in-game teams and players and saves name lists. This takes a second and only needs to happen once.

After that, the Creator's **Import Team** and **Import Player** dropdowns will show every team and player in the game.

## Creating a Campaign

1. Open the Creator → click **+ New Campaign**.
2. Build the **Act Sequence** with the visual map builder (presets available).
3. **Add teams** — create new ones or import from the game.
4. **Set active** and launch the game.

See the full guide in [`release/Custom Campaigns Mod/README.md`](release/Custom%20Campaigns%20Mod/README.md).

## For Developers

- **Mod source**: [`src/CustomCampaignFramework/Plugin.cs`](src/CustomCampaignFramework/) (C#, BepInEx 6 IL2CPP, Harmony)
- **Creator source**: [`release/Custom Campaigns Mod/creator_gui.py`](release/Custom%20Campaigns%20Mod/creator_gui.py) (Python 3, tkinter, no dependencies)
- Build the exe: `build_exe.bat` (requires PyInstaller)
- Build the installer: see [`installer/`](installer/) (requires Inno Setup 6)

## Credits

- **Tape to Tape** by [Excellent Rectangle](https://store.steampowered.com/app/1566200/Tape_to_Tape/)
- **BepInEx** — [LGPL-2.1 license](https://github.com/BepInEx/BepInEx), bundled in the installer
- Mod by **@yeastmann**

## Issues & Feedback

Report bugs or suggestions on the [Issues page](../../issues) or message **@yeastmann** on Discord.
