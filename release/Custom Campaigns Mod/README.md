# T2T Custom Campaign Framework

Create your own custom campaigns for **Tape to Tape** — design teams, players, uniforms, colors, abilities, and full campaign runs with the built-in visual editor.

---

## Quick Start

1. **Run the installer** (`T2T Custom Campaign Framework Setup.exe`).
   - It auto-detects your Tape to Tape install via Steam.
   - It installs BepInEx 6 if you don't have it yet.
   - It copies the mod + creator tool + example campaigns.
   - It creates a **desktop shortcut** to the T2T Campaign Creator.

2. **Launch Tape to Tape once** — just start and quit. This lets the mod scan the game's teams and players so the Creator can show them in dropdowns.

3. **Open the T2T Campaign Creator** from your desktop shortcut.

4. **Pick "Example Campaign"** in the Active Campaign dropdown and click **Set Active**.

5. **Launch Tape to Tape** again and start a new run — you're playing the custom campaign!

That's it. To make your own campaign, keep reading.

---

## Creating a Campaign

### From the Home tab:

1. Click **+ New Campaign** and type a name.
2. The **Campaign Editor** tab opens.

### In the Campaign Editor:

1. **Build the Act Sequence** using the map builder at the top:
   - Click **+ Act 1 (easy)** or **+ Act 2 (medium)** to add maps before the boss.
   - The final **BOSS** map (Act 3) is always present — you can't remove it.
   - Use a **Preset** (Short / Standard / Long) to start fast.
   - Each Act 1 map has a **Spartan checkbox** — tick it to replace the Spartan challenge with a full elite-team match (+1 game on that map).

2. **Add teams** — click **Add New** or **Import Team** in the team list on the right. Each game in the campaign needs a team, in play order.

3. **Set the other options** (Replace Soccer Ball, Replace Golf Ball, etc.) if needed.

4. Click **Save Settings**.

### How many teams do I need?

The builder tells you. Each map is a series of games:

| Map type | Games per map |
|----------|--------------|
| Act 1 (default Spartan) | 4 |
| Act 1 (Spartan replaced) | 5 |
| Act 2 | 3 |
| Act 3 (Boss) | 3 |

Example: the **Standard** preset (9 maps) with all Spartans replaced = 33 teams.

---

## Creating Teams

1. From the Home tab, click **+ New Team** and type a name.
2. Fill in the team editor:
   - **Import Team** (top) — type an in-game team name (e.g. `Vancouver`) to clone everything. This is the fastest way. Leave everything else blank if you just want a copy.
   - **Identity** — Team Name, City, Abbreviation, Logo.
   - **Colors** — Jersey, equipment, numbers. The **Uniform Preview** on the right updates live.
   - **Uniform Skins** — Body, Bicep, Helmet, Skates, Stick models. If set to `standard` / `team colors`, your color picks apply. If set to a named model (e.g. `hockey fc`, `tycoons`), that model's built-in look is used and colors are ignored for that piece.
   - **Relics + Random Talents** — starting items for this team.
3. Add **players** using the panel on the right (New Player, Import Player, Edit, Remove).
4. Click **Save Team**.

---

## Creating Players

1. From the Home tab, click **+ New Skater** or **+ New Goalie**.
2. Fill in the player editor:
   - **Import Player** (top) — type a game player's name (e.g. `W. Kidd`) to clone them. Only field needed for a quick copy.
   - **Identity** — Name, Number, Face, Size.
   - **Stats** — sliders, with a live **Overall** readout.
   - **Ability + Talents** — searchable picker with descriptions.
   - **Uniform Overrides** — override the team's default body/helmet/etc. for this one player.
   - **Color Overrides** — override team colors for this player only.
3. Click **Save** — saved to the **Library** (and to the team slot if editing from within a team).

---

## First Launch (important!)

After installing, **launch Tape to Tape once** before using the Campaign Creator. The mod automatically scans all in-game teams and players and saves the name lists. This only takes a second and only needs to happen once (or again after a game update).

After that first launch, the Creator's **Import Team** and **Import Player** dropdowns will be populated with every team and player in the game — ready to pick from.

---

## Importing Game Teams + Players

Once you've done the first launch:

1. **Import Team dropdown** — in the Team Editor, the `Import Team` field becomes a searchable dropdown of every in-game team. Pick one (e.g. `Vancouver`) and save — the game will clone that team's full data (players, stats, colors, everything) when you next play.

2. **Import Player dropdown** — in the Player Editor, the `Import Player` field becomes a dropdown of every in-game player. Pick one and save — the game clones their stats, face, talents, and skins.

3. **Import Game Team button** (Home tab) — one-click import of a complete team + all players into your library as editable files. You can then customize anything before adding the team to a campaign.

4. **Copy to Library** — right-click any player or team in the file tree → **Copy to Library** to grab them from an existing campaign (e.g. the included Example Campaign).

You only need to fill in the fields you want to **change**. Everything else comes from the imported team/player automatically.

---

## Library vs. Campaigns

| Location | What lives there | Purpose |
|----------|-----------------|---------|
| `library/players/` | Individual player files | Reusable pool — import into any team |
| `library/teams/` | Standalone team folders | Reusable pool — import into any campaign |
| `campaigns/<Name>/` | Full campaign folders | What the game actually loads and plays |

Players are always saved to the library first. When you add a player to a team (inside a campaign), a copy is placed in that team's folder too.

---

## Setting the Active Campaign

On the Home tab, use the **Active Campaign** dropdown and click **Set Active**. This writes `active.txt`, which the mod reads when the game launches. Pick `default` to disable the mod and play vanilla.

---

## File Structure

After installation, everything lives in your game's folder:

```
Tape to Tape/
  BepInEx/
    plugins/
      CustomCampaignFramework.dll    <-- the mod
      Custom Campaigns Mod/          <-- everything else
        T2T Campaign Creator.exe     <-- the visual editor
        active.txt                   <-- which campaign to play
        campaigns/                   <-- your campaigns
          Example Campaign/
            campaign.txt
            teams/
              01 Vancouver/
                team.txt
                players/
                  Goalie - Kelvin Lankinnen.txt
                  Left Wing - Ellias Peterson.txt
                  ...
        library/                     <-- reusable players + teams
          players/
          teams/
```

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+W | Close the current editor tab |
| Middle-click / right-click a tab | Close that tab |
| Double-click in the file tree | Open that item for editing |
| Right-click in the file tree | Context menu (Edit / Set Active / Open Folder / Delete) |

---

## Troubleshooting

**Mod doesn't load / game plays vanilla:**
- Make sure BepInEx is installed (you should see `winhttp.dll` in your Tape to Tape folder).
- Check `active.txt` isn't set to `default`.
- Look at `BepInEx/LogOutput.log` for `[Campaign]` lines.

**Creator won't launch:**
- Windows: use the `.exe`. If it won't run, try the `.bat` (requires Python 3 installed).
- Mac/Linux: run `python3 creator_gui.py` from a terminal.

**Colors don't apply to a player:**
- Make sure the matching **Uniform Skin** is set to `standard` / `team colors`. Named models (e.g. `hockey fc`) use their own baked-in colors and ignore your color picks.

---

## For Developers

- Source: `creator_gui.py` (Python 3, tkinter, no dependencies)
- Mod source: `Plugin.cs` (C#, BepInEx 6 IL2CPP, Harmony)
- Build the exe: run `build_exe.bat` (requires Python + PyInstaller)
- Build the installer: see `installer/README.md` (requires Inno Setup 6)
