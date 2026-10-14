# Opponent line behavior repair - October 14, 2026

The Montreal NHL node produced repeated inactive `Mike Matheson II` coroutine errors followed by `Did not found skater type` during the post-replay faceoff. Those logs establish the failed faceoff; they do not establish a native crash or prove this repair in-game.

Unchecked opponent two-line behavior now saves an explicit `no`. Empty, Off and no values disable custom opponent line changes. Custom opponents use the five authored starter slots, fresh lineup references and matching positions; donor reserves are removed from the match clone. Native match initialization reapplies that policy, and native line selection/switching cannot activate a second line while disabled. A configured second line still uses native switching when enabled. Incomplete second lines fall back to disabled behavior. Player squads and default-mode games are excluded.

The NHL content update adds five inactive cloned reserves, named with `II`, to Ottawa, Montreal, Toronto and Tampa. Their stats, heads and loadouts match their own starters. All four settings remain off. Goalies, campaign progression, rewards, starting squad and saves are preserved. The NHL ZIP is not part of the framework GitHub release.

## Manual confirmation pending

Tests and gameplay checks were skipped at the user's request. Production compilation and package/hash verification are technical evidence only.

1. With the new framework installed, reopen the NHL campaign in Studio. Open Montreal's Opponent settings: two-line behavior must be unchecked. Line 2 should contain five matching reserves. Save, reopen and confirm it remains unchecked.
2. Resume the saved Montreal node. Confirm five starting skaters and the goalie appear. Score and concede goals, let replays end, and reach a period-ending faceoff. Each faceoff should complete without an inactive reserve entering play or the skater-type exception. If the old run fails before the match loads, preserve the save and logs rather than resetting it.
3. Repeat a match with Ottawa, Toronto or Tampa. Their first line should stay active while the option is off.
4. On a disposable campaign copy, enable two-line behavior with all five reserves filled and launch a match. Native switches should use the configured second line with correct positions, bodies and heads. Disable the option again, save and relaunch: only the first line should play, while the authored reserve files remain in Studio.
5. Confirm a regular base-game campaign and a two-line player squad still use their usual line behavior. An incomplete opponent second line should log a warning and remain single-line.

Downloading a campaign ZIP alone cannot alter saves. Studio's Replace action moves the old folder, including its progress files, into `campaign_backups` and imports a fresh campaign folder. The supplied NHL update script instead changes files in place, backs up replaced authoring files and leaves `save.txt`, `save.txt.bak` and native game saves intact.
