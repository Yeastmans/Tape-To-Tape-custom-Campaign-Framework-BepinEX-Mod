"""Build a deterministic Basic Squad campaign that inventories imported choices."""
from __future__ import annotations

import csv
import json
import re
from collections import defaultdict
from pathlib import Path
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "test-campaigns" / "Imported Options Audit"
BASE = ROOT / "assets" / "base-rosters" / "Base Game Teams" / "Basic Squad"
REWARDS = Path.home() / "AppData/Local/T2T Campaign Studio/T2T_Dumps/_gui_data/_reward_catalog.tsv"
SKINS = Path.home() / "AppData/Local/T2T Campaign Studio/_game_skins.txt"
TEAM_COUNT = 66
with (ROOT / "assets/equipment-catalog.tsv").open(encoding="utf-8-sig", newline="") as stream:
    NATIVE_ROWS = list(csv.DictReader(stream, delimiter="\t"))
NATIVE_IDS = {row["asset"] for row in NATIVE_ROWS}
INCOMPLETE_EQUIPMENT = {row["asset"] for row in NATIVE_ROWS if row["role"] == "goalie" and row["missing_sources"] and row["category"] not in ("Logos", "Numbers")}


def read_fields(path: Path) -> dict[str, str]:
    result = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            result[key.strip()] = value.strip()
    return result


def write_fields(path: Path, fields: dict[str, str], heading: str = "") -> None:
    if fields.get("Name") and not fields.get("Team Name"):
        key, custom = ("Skin", "Body/Customization_colors") if fields.get("Position") == "Goalie" else ("Body", "Body/Customization/Customization_colors")
        normalize = lambda value: custom if not value or value.lower() in ("standard", "team colors") else value
        home = normalize(fields.get(key, "")); away = normalize(fields.get(key + " Away") or home)
        if home != custom or away != custom:
            fields["Logo From"] = fields["Logo Skin"] = ""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text((f"# {heading}\n" if heading else "") + "".join(f"{key:<28}= {value}\n" for key, value in fields.items()), encoding="utf-8")


def selectable(asset: str) -> bool:
    known = asset in NATIVE_IDS or asset == "Numbers/Custom_Number" or re.fullmatch(r"Numbers/Number_\d{1,2}(LH)?", asset)
    return bool(known) and asset not in INCOMPLETE_EQUIPMENT and not (asset.lower().endswith(("_iso", "_lso")) or asset in {
        "Faces/Custom/Helmet_Face", "Helmet/Helmet_Customization_Face"})


def catalog() -> dict[tuple[str, str], list[str]]:
    found = defaultdict(set)
    with (ROOT / "assets/equipment-catalog.tsv").open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream, delimiter="\t"):
            role, field, asset = row["role"], row["field"], row["asset"]
            if role not in ("skater", "goalie") or row["derived"] != "false" or not selectable(asset):
                continue
            if role == "goalie":
                field = field.removeprefix("Goalie ")
                field = {"Helmet": "Helmet Skin", "Glove": "Glove Skin", "Blocker": "Blocker Skin",
                         "Pads": "Pads Skin", "Stick": "Stick Skin"}.get(field, field)
            else:
                field = {"Number": "Number Skin", "Logo": "Logo Skin"}.get(field, field)
            if field:
                found[role, field].add(asset)
    # A stale runtime dump cannot reintroduce obsolete native asset IDs.
    role, field = "skater", ""
    for line in SKINS.read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("["):
            field = line[1:line.index("]")]
            role = "goalie" if field.startswith("Goalie ") else "skater"
            if role == "goalie":
                field = field.removeprefix("Goalie ")
                field = {"Helmet": "Helmet Skin", "Glove": "Glove Skin", "Blocker": "Blocker Skin",
                         "Pads": "Pads Skin", "Stick": "Stick Skin"}.get(field, field)
            else:
                field = {"Number": "Number Skin", "Logo": "Logo Skin"}.get(field, field)
        elif field and "/" in line and not line.startswith("#") and selectable(line.strip()):
            found[role, field].add(line.strip())
    return {key: sorted(values) for key, values in found.items() if values}


def reward_catalog() -> dict[str, list[dict[str, str]]]:
    result = defaultdict(list)
    with REWARDS.open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.reader(stream, delimiter="\t"):
            if len(row) == 7 and row[0] in ("Relics", "Talents", "Goalie Talents", "Abilities"):
                result[row[0]].append(dict(zip(("kind", "key", "id", "level", "asset", "name", "description"), row)))
    return {kind: sorted(rows, key=lambda row: row["key"]) for kind, rows in result.items()}


def main() -> None:
    if OUT.exists():
        raise SystemExit(f"Refusing to replace existing campaign: {OUT}")
    assets, rewards = catalog(), reward_catalog()
    effects_source = (ROOT / "src/CampaignCore/OpponentEffects.cs").read_text(encoding="utf-8")
    effects = re.findall(r'new OpponentEffect\("([^"]+)"', effects_source)
    if len(effects) != TEAM_COUNT:
        raise SystemExit(f"Expected {TEAM_COUNT} opponent effects, found {len(effects)}")
    logos = sorted(set(line.strip() for line in (Path.home() / "AppData/Local/T2T Campaign Studio/_game_team_logos.txt").read_text(encoding="utf-8-sig").splitlines() if line.strip()))
    if len(logos) > TEAM_COUNT + 2:
        raise SystemExit(f"Need more playable teams to cover {len(logos)} logos")
    presets = ["Standard", "Hockey FC", "Golfers", "Referees", "Tycoons", "Prisoners", "Cheese", "Calaveras",
               "Meatballs", "Lettuce", "Princess", "Disco", "Shooting Stars", "Crusaders", "Cultists", "Mountaineers", "Spartans"]
    offense = ["Standard", "Default", "Blitz", "Low", "Overload", "Spread", "Umbrella"]
    defense = ["Standard", "ManToMan", "Box"]
    pucks = ["Hockey puck", "Soccer ball", "Golf ball"]
    surfaces = ["Standard ice", "Soccer field", "Golf ice", "Tycoons ice"]
    assignments: list[dict[str, str]] = []
    counts = defaultdict(int)

    def assign(scope: str, key: str, value: str, location: Path) -> None:
        assignments.append({"scope": scope, "field": key, "value": value,
                            "location": str(location.relative_to(OUT)).replace("\\", "/")})

    def cycles(role: str, fields: dict[str, str], index: int, location: Path) -> None:
        keys = [key for (item_role, key) in assets if item_role == role]
        for key in sorted(keys):
            values = assets[role, key]
            value = values[index % len(values)]
            fields[key] = value
            assign(role + " appearance", key, value, location)
            for away in ({"Body", "Helmet", "Bicep", "Gloves", "Pants", "Skates"} if role == "skater"
                         else {"Skin", "Glove Skin", "Blocker Skin", "Pads Skin", "Stick Skin"}):
                if key == away:
                    away_key = key + " Away" if role == "skater" else key.replace(" Skin", "") + " Away"
                    away_value = values[(index + len(values) // 2 + 1) % len(values)]
                    fields[away_key] = away_value
                    assign(role + " away appearance", away_key, away_value, location)

    def reward(kind: str, fields: dict[str, str], key: str, location: Path, amount: int = 1) -> None:
        choices = rewards[kind]
        start = counts[kind]
        picked = [choices[(start + step) % len(choices)] for step in range(amount)]
        counts[kind] += amount
        fields[key] = ", ".join(item["key"] for item in picked)
        for item in picked:
            assign(kind, key, item["key"] + " | " + item["name"], location)

    skater_base = read_fields(BASE / "players/Left Wing - Angus McShaggy.txt")
    goalie_base = read_fields(BASE / "players/Goalie - Bobby Butcher.txt")
    team_base = read_fields(BASE / "team.txt")
    palettes = [(30, 197, 183), (244, 111, 62), (135, 111, 233), (228, 188, 51),
                (66, 148, 242), (218, 88, 145)]
    names = ["Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense", "Goalie"]
    global_skater = global_goalie = 0

    def make_team(folder: Path, label: str, number: int, player_side: bool = False) -> None:
        nonlocal global_skater, global_goalie
        team_path = folder / "team.txt"
        fields = team_base.copy()
        fields.update({"Team Name": label, "City": "Audit City", "Abbreviation": f"T{number:02d}",
                       "Description": f"Imported option audit team {number:02d}; see TEST_GUIDE.md.",
                       "Logo From": logos[number % len(logos)], "Opponent Two Lines": "no"})
        assign("team look", "Logo From", fields["Logo From"], team_path)
        if not player_side:
            options = {"Opponent Effects": effects[number - 1], "Opponent Preset": presets[(number - 1) % len(presets)],
                       "Opponent Offense": offense[(number - 1) % len(offense)],
                       "Opponent Defense": defense[(number - 1) % len(defense)],
                       "Match Puck": pucks[(number - 1) % len(pucks)],
                       "Match Surface": surfaces[(number - 1) % len(surfaces)]}
            fields.update(options)
            for key, value in options.items():
                assign("opponent settings", key, value, team_path)
        rgb = palettes[number % len(palettes)]
        fields["Jersey Primary"] = ", ".join(map(str, rgb))
        fields["Jersey Secondary"] = "18, 30, 46"
        fields["Away Primary"] = "235, 240, 245"
        fields["Away Secondary"] = ", ".join(map(str, rgb))
        fields["Transition Primary"] = fields["Jersey Primary"]
        fields["Transition Secondary"] = fields["Jersey Secondary"]
        fields["Transition Tertiary"] = fields["Jersey Accent"]
        fields["Bench Head"] = assets["skater", "Face"][number % len(assets["skater", "Face"])]
        fields["Squad Head"] = assets["skater", "Face"][(number + 1) % len(assets["skater", "Face"])]
        assign("team look", "Jersey Primary", fields["Jersey Primary"], team_path)
        assign("team look", "Bench Head", fields["Bench Head"], team_path)
        assign("team look", "Squad Head", fields["Squad Head"], team_path)
        relic_count = 5 if number < 60 else 4
        reward("Relics", fields, "Team Relics", team_path, relic_count)
        if player_side:
            fields["Squad Template"] = "Basic Squad"
        write_fields(team_path, fields, label)
        for slot, position in enumerate(names):
            player_path = folder / "players" / f"{position} - Audit {number:02d}-{slot + 1}.txt"
            if position == "Goalie":
                player = goalie_base.copy()
                player.update({"Name": f"Audit {number:02d} Goalie", "Position": "Goalie",
                               "Number": str((number + slot) % 99 + 1), "Override Team Settings": "yes"})
                cycles("goalie", player, global_goalie, player_path)
                reward("Goalie Talents", player, "Goalie Talents", player_path)
                global_goalie += 1
            else:
                player = skater_base.copy()
                player.update({"Name": f"Audit {number:02d} {position}", "Position": position,
                               "Number": str((number * 7 + slot) % 99 + 1),
                               "Size": ["ExtraSmall", "Small", "Medium", "Big", "ExtraBig", "ExtraExtraBig"][global_skater % 6],
                               "Skin Color": ["light", "dark"][global_skater % 2],
                               "Left Handed": ["yes", "no"][global_skater % 2], "Override Team Settings": "yes"})
                cycles("skater", player, global_skater, player_path)
                reward("Talents", player, "Talents", player_path)
                reward("Abilities", player, "Ability", player_path)
                for key in ("Size", "Skin Color", "Left Handed"):
                    assign("skater detail", key, player[key], player_path)
                global_skater += 1
            write_fields(player_path, player, f"{label} / {position}")

    OUT.mkdir(parents=True)
    write_fields(OUT / "campaign.txt", {"Act Sequence": "1,2,3", "Maps": "Basic",
                 "Squad Template": "Basic Squad", "Use Player Teams": "yes", "Custom Squads Only": "no"},
                 "Imported Options Audit — Basic Squad")
    make_team(OUT / "player_teams" / "Audit Home", "Audit Home", 0, True)
    for number in range(1, TEAM_COUNT + 1):
        make_team(OUT / "teams" / f"Audit {number:02d}", f"Audit {number:02d}", number)
    make_team(OUT / "player_teams" / "Audit Alternate", "Audit Alternate", TEAM_COUNT + 1, True)

    # Recruitable skaters exercise the separate free-agent pool, not the bench pool.
    for slot in range(30):
        path = OUT / "player_teams" / "free_agents" / f"Audit Free Agent {slot + 1:02d}.txt"
        player = skater_base.copy()
        player.update({"Name": f"Audit Free Agent {slot + 1:02d}", "Position": names[slot % 5],
                       "Number": str(20 + slot), "Studio Player Source": "custom", "Override Team Settings": "yes"})
        cycles("skater", player, global_skater, path)
        reward("Talents", player, "Talents", path)
        reward("Abilities", player, "Ability", path)
        write_fields(path, player, "Recruitable audit free agent")
        global_skater += 1

    # Bench and superstar manifests use exactly the same file names the editor saves.
    for folder, count in (("draft_pool", 9), ("superstars", 3)):
        pool = OUT / "player_teams" / folder
        selected = []
        for slot in range(count):
            name = f"{folder.title().replace('_', ' ')} {slot + 1:02d}"
            file = f"custom-{slot + 1:02d}.txt"
            path = pool / file
            player = skater_base.copy()
            player.update({"Name": name, "Position": names[slot % 5], "Number": str(slot + 70),
                           "Studio Player Source": "custom", "Studio Base Pool Player": "",
                           "Override Team Settings": "yes"})
            cycles("skater", player, global_skater, path)
            reward("Talents", player, "Talents", path)
            reward("Abilities", player, "Ability", path)
            write_fields(path, player, name)
            selected.append(file)
            global_skater += 1
        pool.mkdir(parents=True, exist_ok=True)
        (pool / "selected.list").write_text("\n".join(selected) + "\n", encoding="utf-8")
        (pool / "custom-slots.list").write_text("\n".join(selected) + "\n", encoding="utf-8")
        if folder == "draft_pool":
            (pool / "source.mode").write_text("custom", encoding="utf-8")

    # Each map has three connected routes and every native node type.
    event_types = ["Challenge", "RandomConditioning", "Chaos", "Nurse", "Blademaster", "FanNumber1",
                   "Coach", "GeneralManager", "NULL", "PlayerTraining", "TeamTraining", "StatTraining"]
    for act in range(3):
        lines = ["# Campaign Studio map layout. Start a new run after editing.", "Version = 1",
                 "Base Squad = Basic Squad", f"Base Map = {act}", "Template = 0", f"Map Look = {act + 1}"]
        for layer in range(14):
            branches = range(3) if layer < 13 else range(1)
            for branch in branches:
                combat = layer % 2 == 0
                number = act * 22 + (layer // 2) * 3 + branch + 1 if layer < 13 and combat else act * 22 + 22
                node_id = f"a{act + 1}l{layer + 1}b{branch + 1}"
                next_id = f"a{act + 1}l{layer + 2}b{branch + 1}" if layer < 12 else (f"a{act + 1}l14b1" if layer == 12 else "")
                node_type = "Boss" if layer == 13 else "Elite" if combat else event_types[((layer // 2) * 3 + branch) % len(event_types)]
                team = f"Audit {number:02d}" if node_type in ("Elite", "Boss") else ""
                record = [str(layer), node_type, str(layer * 70), str((branch - 1) * 150),
                          team, next_id, "-1", "-1", "1", "1", "1", "", ""]
                lines.append("Node " + node_id + " = " + "|".join(quote(value, safe="") for value in record))
                assign("map node", "Node Type", node_type, OUT / "map_layouts" / f"map-{act + 1}.txt")
                if team:
                    assign("map opponent", "Team", team, OUT / "map_layouts" / f"map-{act + 1}.txt")
                if node_type == "NULL":
                    lines.append("Random weights " + node_id + " = RandomConditioning:25|Blademaster:19|FanNumber1:16|Chaos:12|Nurse:10")
        path = OUT / "map_layouts" / f"map-{act + 1}.txt"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    # Incompatible logos are no longer placements; report any remaining gap.
    assignments = [row for row in assignments if row["field"] not in ("Logo Skin", "Logo From")
                   or read_fields(OUT / row["location"]).get(row["field"]) == row["value"]]
    # Report all actual placements, including duplicates, and check every source choice.
    with (OUT / "COVERAGE.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=("scope", "field", "value", "location"))
        writer.writeheader()
        writer.writerows(assignments)
    covered = {(row["scope"], row["field"], row["value"]) for row in assignments}
    missing = []
    for (role, field), values in assets.items():
        for value in values:
            if (role + " appearance", field, value) not in covered:
                missing.append(f"{role} / {field} / {value}")
    for kind, choices in rewards.items():
        for entry in choices:
            if not any(row["scope"] == kind and row["value"].startswith(entry["key"] + " | ") for row in assignments):
                missing.append(f"{kind} / {entry['key']} / {entry['name']}")
    for field, values in (("Opponent Effects", effects), ("Opponent Preset", presets),
                          ("Opponent Offense", offense), ("Opponent Defense", defense),
                          ("Match Puck", pucks), ("Match Surface", surfaces)):
        for value in values:
            if ("opponent settings", field, value) not in covered:
                missing.append(f"opponent settings / {field} / {value}")
    for logo in logos:
        if ("team look", "Logo From", logo) not in covered:
            missing.append(f"team look / Logo From / {logo}")
    summary = {"opponent_teams": TEAM_COUNT, "map_count": 3, "player_teams": 2, "custom_bench": 9,
               "superstars": 3, "catalog_counts": {f"{role}/{field}": len(values) for (role, field), values in assets.items()},
               "reward_counts": {kind: len(values) for kind, values in rewards.items()},
               "opponent_effects": len(effects), "logos": len(logos), "unplaced": missing}
    (OUT / "COVERAGE_SUMMARY.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    (OUT / "audit-trace.enabled").write_text("Record runtime evidence for this test campaign.\n", encoding="utf-8")
    (OUT / "TEST_GUIDE.md").write_text("""# Imported Options Audit — play guide

This campaign uses **Basic Squad**, playable **Audit Home** and **Audit Alternate** teams, **66 different opponents**, nine custom bench players, three starting superstar choices, and 30 recruitable free agents. The alternate playable team covers the last logo choice. The three maps each have 40 reachable nodes: seven Elite matches on one of three routes, six event nodes, then one Boss match. Each opponent has one distinct opponent effect; formation, puck, surface, team type, and logo options are spread across them. The complete assignment list is `COVERAGE.csv`; search by option value or file path. `COVERAGE_SUMMARY.json` records the source catalog counts and any unplaced values. At generation time, `unplaced` was empty.

## Start and route

1. In Campaign Studio, open **Imported Options Audit** and check that the squad template is **Basic Squad**, **Use player teams** is enabled, and **Audit Home** appears under Custom squads. Do not switch the custom bench to the base-game view.
2. Start a **new run** in the game and choose **Audit Home**. Check the three superstar choices on the opening screen. Note which one you take; the other two need later runs.
3. Pick a route at the start of each map and stay on that branch. Route 1 meets opponent numbers 1, 4, 7, 10, 13, 16, 19, then boss 22 in Map 1. Add 22 to those numbers for Map 2 and another 22 for Map 3. Routes 2 and 3 use numbers 2, 5, ... 20 and 3, 6, ... 21 respectively, with the same boss.
4. To visit every opponent, play three runs, one per route. Each run has 24 opponent matches across the three maps. Some event nodes differ by route. The Fan and General Manager nodes expose the free-agent pool; only a subset can be signed in one run.
5. For each match, compare the actual opponent name, jersey and equipment, five skater faces, goalie look, ability use, talent effects, and relic effects with the specific team's rows in `COVERAGE.csv`. Record a visual or gameplay result only after seeing it in game. A runtime `*_APPLIED` line proves the mod attached an object; it does not prove an AI opponent used the effect or that the renderer showed it.

## Evidence to share one item at a time

The enabled DLL writes `audit-runtime.tsv` beside `campaign.txt` only for campaigns with `audit-trace.enabled`. The columns are UTC time, stage, subject, field, and value. `TEAM_PARSED` means the file loaded; `MAP_APPLIED` means the custom map was accepted; `TEAM_APPLIED` means a runtime team was configured; `MATCH_STARTED` means the match was entered. `RELIC_APPLIED`, `TALENT_APPLIED`, `GOALIE_TALENT_APPLIED`, and `ABILITY_APPLIED` record native IDs after attachment. Matching `*_MISSING`, `MAP_REJECTED`, or `SKATER_ERROR` lines need investigation.

After each match or menu step, ask Codex to **check the audit log**. Codex will compare the newest event with `COVERAGE.csv`, tell you what reached the game, ask for your visible/gameplay observation, and move to the next item after your confirmation. The comparison command is `python tools/check_import_audit_log.py` from this workspace. Existing BepInEx logs remain useful for errors outside these trace points.

The campaign deliberately includes enemy abilities and relics whose native assets may exist but whose effects may not operate for AI opponents. Mark those as **attached but not observed** until gameplay proves them. The same applies to heads or equipment that report a runtime skin name but render incorrectly.
""", encoding="utf-8")
    if missing:
        raise SystemExit(f"Campaign created but {len(missing)} catalog choices were not placed. See COVERAGE_SUMMARY.json")
    print(json.dumps({"campaign": str(OUT), "teams": TEAM_COUNT, "placements": len(assignments), "unplaced": len(missing)}))


if __name__ == "__main__":
    main()
