# Campaign Studio — Beta 1.0

Create Tape to Tape campaigns, teams, players, branching maps and reward pools
with the new visual Campaign Studio. This Windows installer includes Studio,
the Custom Campaign Framework mod, the editor runtime and BepInEx 6 IL2CPP.

**Requires:** your own Steam copy of Tape to Tape on the **`roguelite` branch,
build 25899138**, with branch access. **Windows x64 only for this release.**

Download **T2T_Custom_Campaign_Framework_Setup.exe**. Close the game and old
creator/Studio, run Setup, choose the Studio folder and the separate game folder,
then open Studio and select **Read game artwork**. No Python or separate .NET
installation is required. Source-code archives are not the installer.

## Highlights

- Up to two encounter choices per map layer, with connected routes, native artwork
  spacing and a single final Boss.
- Native goalie Pass Read fractions preserved; empty native helmet fields import
  as **No helmet**.

- Copy native campaigns, edit opponents and squads, build routes, customize
  home/away equipment and heads, share campaign ZIPs and export to Play Now.
- Help/F1 points to controls; the searchable Studio guide explains save flows,
  inheritance and native behavior requirements.
- Independent free-agent talent checkboxes constrain exact random levels and
  upgrade outcomes while preserving fixed authored talents and native eligibility.
- Copied native opponents retain extra reserves; per-player number styles
  survive supported Continue and mod-backed Play Now paths.
- Native helmet lifecycle handling and a brawl-only Prince flag, with the
  required Elite node, controlled bodycheck and charged-shot timing documented.

**Upgrading:** back up your authored content first. Setup migrates it to
`%LOCALAPPDATA%\T2T Campaign Studio`, then removes retired creator folders and
the old uninstaller after successful cleanup. It does not retain an automatic
backup. Review migrated content, save it, select it on Home and use **New Run**
for changed maps or starting rosters.

Beta 1.0 starts the new Studio release family after the old v2.1.x creator.
Technical audits are not a gameplay pass for every native ability or rule
combination. Future game updates can require a mod update; native Linux/macOS
and Steam Deck/Proton integration are not validated for this package.

[Installation and full guide](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod#readme)
· [Changelog](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/blob/HEAD/release/CHANGELOG.md)
· [Discord help](https://discord.gg/hscxnYghb)
· [Report a bug](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/issues)
