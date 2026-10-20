"""Clear only player logo selections; preserve team identity and every jersey."""
import argparse
import csv
import json
import shutil
from pathlib import Path

def fields(path):
    result = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            k, v = line.split("=", 1)
            result[k.strip()] = v.strip()
    return result

def clear(root, backup):
    rows = []
    removed = set()
    for path in sorted(root.rglob("*.txt")):
        raw = fields(path)
        if not raw.get("Name") or "Team Name" in raw:
            continue
        role = "goalie" if raw.get("Position", "").lower() == "goalie" else "skater"
        body = "Skin" if role == "goalie" else "Body"
        custom = "Body/Customization_colors" if role == "goalie" else "Body/Customization/Customization_colors"
        team = {}
        for parent in path.parents:
            if parent == root.parent:
                break
            if (parent / "team.txt").is_file():
                team = fields(parent / "team.txt")
                if role == "goalie" and (parent / "goalie-equipment.cfg").is_file():
                    team.update(fields(parent / "goalie-equipment.cfg"))
                break
        independent = raw.get("Override Team Settings", "yes").lower() in ("yes", "true", "1")
        def value(k):
            return (raw.get(k) if independent else "") or team.get(k, "")
        home = value(body) or custom
        away = value(body + " Away") or home
        normalize = lambda s: custom if s.lower() in ("standard", "team colors") else s
        if normalize(home) == custom and normalize(away) == custom:
            continue
        rel = path.relative_to(root).as_posix()
        text = path.read_text(encoding="utf-8-sig")
        changed = []
        found = set()
        for line in text.splitlines():
            key = line.split("=", 1)[0].strip() if "=" in line and not line.lstrip().startswith("#") else ""
            if key.startswith("Export Logo "):
                continue
            if key in ("Logo From", "Logo Skin"):
                found.add(key)
                if raw.get(key):
                    removed.add((rel, key, raw[key]))
                line = line.split("=", 1)[0] + "= "
            changed.append(line)
        for key in ("Logo From", "Logo Skin"):
            if key not in found:
                changed.append(f"{key:<28}= ")
        new = "\n".join(changed) + "\n"
        if new == text:
            continue
        target = backup / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists():
            shutil.copy2(path, target)
        path.write_text(new, encoding="utf-8")
        rows.append(dict(location=rel, player=raw["Name"], role=role, home=home, away=away,
                         cleared_logo_from=raw.get("Logo From", ""), cleared_logo_skin=raw.get("Logo Skin", ""),
                         retained_team_logo=team.get("Logo From", "")))
    with (root / "PLAYER_LOGO_CLEANUP.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=("location", "player", "role", "home", "away", "cleared_logo_from", "cleared_logo_skin", "retained_team_logo"))
        writer.writeheader(); writer.writerows(rows)
    coverage = root / "COVERAGE.csv"
    if coverage.exists():
        with coverage.open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream); columns = reader.fieldnames; placements = list(reader)
        retained = [r for r in placements if (r["location"], r["field"], r["value"]) not in removed]
        backup.mkdir(parents=True, exist_ok=True)
        if not (backup / coverage.name).exists(): shutil.copy2(coverage, backup / coverage.name)
        with coverage.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=columns); writer.writeheader(); writer.writerows(retained)
        summary_path = root / "COVERAGE_SUMMARY.json"
        summary = json.loads(summary_path.read_text(encoding="utf-8-sig"))
        if not (backup / summary_path.name).exists(): shutil.copy2(summary_path, backup / summary_path.name)
        unplaced = {f"{r['scope'].replace(' appearance','')} / {r['field']} / {r['value']}" for r in placements if r not in retained
                    and not any(a["scope"] == r["scope"] and a["field"] == r["field"] and a["value"] == r["value"] for a in retained)}
        summary["unplaced"] = sorted(set(summary.get("unplaced", [])) | unplaced)
        summary["player_logo_cleanup"] = {"players": len(rows), "removed_placements": len(placements)-len(retained), "team_logos_changed": 0, "report": "PLAYER_LOGO_CLEANUP.csv"}
        summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"root": str(root), "players_cleared": len(rows), "explicit_selections_removed": len(removed), "team_logos_changed": 0}))

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path); parser.add_argument("backup", type=Path)
    args = parser.parse_args()
    clear(args.root.resolve(), args.backup.resolve())
