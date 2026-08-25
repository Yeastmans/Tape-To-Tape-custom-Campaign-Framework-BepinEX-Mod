# Changelog — Tape to Tape Custom Campaign Framework

## v2.1.35 — 2026-08-19

Rebuilt for the **2026-08-18 game update**. If the mod stopped loading after that
update, that is why — the update removes part of BepInEx, and reinstalling puts it
back.

### Offside, for a whole run

The base game only offers offside in Play Now's Advanced Settings. A campaign can
now turn it on for every match of a run — new **Offside** checkbox in the campaign
editor, with an optional **Offside Penalty** of *Whistle*, *Lose Puck* or
*Knockout*.

Leave the penalty on *(player's setting)* and whatever you picked in Advanced
Settings is used. **Your own saved settings are never rewritten** — the rule is
forced on for the match and released when it ends, the same way the *Linesman*
talent does it.

### Fixed

* **Talents that exist but were reported "not found".** Talent lookup only ever
  searched the pool the game draws random rewards from, so a real talent outside
  that pool — *Linesman*, the offside one — could not be given to a player at all.
  Every loaded talent is now searched.

* **Uniform and colour overrides did nothing on players exported to Play Now.**
  The game's save format for a custom skater has room for six parts of a look:
  face, jersey, away jersey, stick, logo and number. The editor offers eighteen.
  Helmet, glasses, bicep, gloves, pants, skates, every away variant and all the
  colour overrides had nowhere to be stored, so they were silently lost on the way
  out — which is why a player could look right in a campaign and wrong in Play Now.
  Those parts now travel alongside the export and are applied by the mod, through
  the same code the campaign side uses.

* **Upgraded (LVL2) relics never applied.** Picking one gave you nothing, with no
  error anywhere — the relic was looked up at level 1 and the level-2 version was
  filtered out. Affects both team relics and a custom squad's starting relics.

* **Teams exported to Play Now arrived with no starting relics.** The team file
  stores relics by id and the Creator had no way to look one up, so it wrote an
  empty list every time.

* **Custom squad slots set up for looks only were ignored.** A lineup slot given a
  helmet, gloves or a jersey colour and nothing else was treated as unconfigured
  and skipped entirely. It now gets its look, while whoever you drafted into that
  slot keeps their own stats and talents.

---

## v2.1.34 — 2026-08-01

### Shared campaigns now bring their logos with them

Team logos live in the game's `CustomLogos/` folder, **outside** the campaign — so a
shared campaign arrived with every team pointing at a PNG the recipient did not have,
and the game quietly fell back to its default crest. The campaign looked broken
through no fault of the person who made it.

Sharing a campaign now bundles every logo it actually uses, and installing one puts
that artwork where the game looks for it. This covers all of it — uploading to the
community folder, downloading from it, and importing a zip from disk.

* Both `Logo From` (team crest) and `Squad Head` (squad tile icon) are followed.
* **Your own artwork is never overwritten.** If you already have a logo with the same
  name, yours is kept and the install dialog tells you how many were skipped.
* Names that aren't custom artwork are left alone — `Logo From` may legitimately name
  a base-game team, and those need nothing bundled.
* The upload dialog shows how many logos went along with the campaign.

Logos travel inside the campaign as a `_logos/` folder, so nothing about the zip
layout changes and older versions of the mod simply ignore it.

---

## v2.1.33 — 2026-08-01

### Map Opponents — choose who you play at every node

Until now a team's folder number *was* its play order: `01 …` played game 1, `02 …`
played game 2. Two things were impossible as a result — a branching layer could
only ever hold one team, and a team could only be used once.

Campaigns can now pin a team to a specific **map node**:

* **Branch layers get one dropdown per branch.** The two paths through a layer can
  face different opponents. Previously both siblings always got the same team.
* **Any team can be used on as many nodes as you like** — "every elite game is the
  soccer players" is now a thing you can just set.
* **Elite *and* boss nodes are individually assignable**, on whichever map the
  campaign selects.

New **Map Opponents** section in the Campaign Creator, laid out to match the map you
picked. Branch dropdowns are labelled *top* / *bottom* to match how the nodes are
drawn on screen. Anything left on **(default order)** keeps the original behaviour,
so existing campaigns are untouched until you change something.

Stored in a new per-campaign `assignments.txt`:

```
Map 1 / Layer 2 / Node 0 = 05 Sogger Team
Map 1 / Layer 2 / Node 1 = 02 The pain
```

Clearing every dropdown deletes the file outright, restoring the sequential order
exactly — there is no half-configured state.

**Notes**
* Assignments are anchored to map coordinates, so they cannot drift as you play.
* Nodes whose type is rolled at map generation cannot be pre-assigned and stay on
  the sequential order.
* Challenge nodes appear as assignable only when the campaign replaces them
  (`Replace Challenges`), since that is what turns them into real matches.

### Added
* **`_game_maps.txt`** — the layer/node layout of every map, written on each launch:
  node types, branch connections, positions, reward counts, and the ids the Gauntlet
  uses to pin its elites. Includes a `TEAM IDS` table so those ids are readable.
* The Creator reads that file **live at startup** rather than baking it into a build,
  so the editor always matches the installed game.

### Fixed
* **The Creator could report the wrong version of itself.** It read a `VERSION.txt`
  shipped next to the exe, which drifted from the code three separate times — once
  frozen at 2.1.23, which made every install prompt to update forever. It now reports
  the version compiled into the build, which cannot disagree with the code it
  describes. Update checks against GitHub are unchanged.

### Documented
* A custom squad that fills **all five forward slots** starts with a full lineup, so
  the run-start superstar pick has nowhere to sit and that screen does not appear.
  Configure four or fewer forwards and it returns. This is intended — the alternative
  was dropping one of your configured players to make room.

---

## v2.1.32 — 2026-07-31

Tagged but never published as a release, so these changes reach users here.

* **Map nodes show the campaign team's logo.** The node icon is a UI image rather
  than a world renderer, which is why earlier attempts never found it.
* **GM squad free-agent nodes.** Picking the GM squad guarantees a GM node on the
  opening layer of map 1 whatever act the campaign starts on, with that squad's own
  selection count, and those nodes are exempt from the free-agent node cap.
* **`Stat Scale` now works on hand-configured campaign teams.** It had only ever been
  applied to imported teams, so the setting silently did nothing on the common case.
* **Player faces no longer collapse.** Skin paths that resolved to a shortened name
  which pointed at no face are now written in full — the five Golfers, for example,
  kept ending up as one name that resolved to nothing.
* **Creator dropdowns are generated from the game itself.** Every value the game
  actually uses, harvested each launch, so no option is offered that does not exist
  and no real option is missing (faces roughly doubled).
* **Data dumps now run with the mod switched off**, so a fresh install can generate
  the lists the Creator needs before any campaign exists.
* **Fixed the player library dumping 0 players** into a fresh or emptied library.
