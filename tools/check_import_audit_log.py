"""Compare runtime audit evidence with the generated campaign assignments."""
from __future__ import annotations

import argparse
import csv
from collections import defaultdict
from pathlib import Path

DEFAULT_CAMPAIGN = Path.home() / "AppData/Local/T2T Campaign Studio/campaigns/Imported Options Audit"


def split_sessions(events):
    sessions = []
    for event in events:
        if not sessions or event["stage"] == "PLUGIN_LOADED":
            sessions.append([])
        sessions[-1].append(event)
    return sessions


def is_fixture(session):
    # Older fixture builds predate FIXTURE_SESSION. Exclude their entire session,
    # including normal campaign loading that also happens during a fixture run.
    return any(event["stage"] == "FIXTURE_SESSION"
               or event["field"].startswith("fixture")
               or event["field"] == "native-composition"
               or event["subject"].startswith("Appearance Fixture ")
               for event in session)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("campaign", type=Path, nargs="?", default=DEFAULT_CAMPAIGN)
    args = parser.parse_args()
    root = args.campaign
    with (root / "COVERAGE.csv").open(encoding="utf-8-sig", newline="") as stream:
        planned = list(csv.DictReader(stream))
    trace = root / "audit-runtime.tsv"
    events = []
    if trace.exists():
        with trace.open(encoding="utf-8-sig", newline="") as stream:
            events = list(csv.DictReader(stream, delimiter="\t"))
    sessions = split_sessions(events)
    latest_is_fixture = bool(sessions and is_fixture(sessions[-1]))
    current = sessions[-1] if sessions and not latest_is_fixture else []
    events = [event for session in sessions if not is_fixture(session) for event in session]
    by_stage = defaultdict(list)
    for event in events:
        by_stage[event["stage"]].append(event)
    entered = {e["subject"] for e in by_stage["MATCH_STARTED"]}
    applied = {e["subject"] for e in by_stage["TEAM_APPLIED"]}
    parsed = {e["subject"] for e in by_stage["TEAM_PARSED"]}
    teams = sorted({row["location"].split("/")[1] for row in planned if row["location"].startswith("teams/")})
    print(f"Runtime log: {trace if trace.exists() else 'not created yet'}")
    print(f"Maps applied: {len(by_stage['MAP_APPLIED'])}; rejected: {len(by_stage['MAP_REJECTED'])}")
    print(f"Opponents parsed: {len(parsed)}/{len(teams)}; applied: {len(applied)}/{len(teams)}; entered: {len(entered)}/{len(teams)}")
    results = root / "PLAYTEST_RESULTS.csv"
    if not results.exists():
        results = Path(__file__).resolve().parents[1] / "test-campaigns/Imported Options Audit/PLAYTEST_RESULTS.csv"
    if results.exists():
        with results.open(encoding="utf-8-sig", newline="") as stream:
            observations = list(csv.DictReader(stream))
        print(f"Player-confirmed visual failures: {sum(row['status'] == 'failed visual' for row in observations)}")
        for row in observations:
            if row["status"] == "failed visual":
                print(f"VISUAL ISSUE {row['screen']}: {row['subject']} / {row['observed']}")
    if latest_is_fixture:
        print("Latest session is an automated fixture; excluded from gameplay observations and cumulative reach.")
    rendered = [e for e in current if e["stage"] in ("MATERIAL_FLAGS_CHECKED", "MATERIAL_GROUP_CONFLICT")]
    selection = sum(e['field'] in ('selection', 'render-flags-selection') for e in rendered)
    world = sum(e['field'] in ('world', 'render-flags-world', 'world-materials') for e in rendered)
    print(f"Current gameplay session material checks: {len(rendered)}; selection={selection}; world={world}; slots={sum(e['field'] == 'slots' for e in rendered)}")
    print(f"Current gameplay session field snapshots: {sum(e['stage'] == 'PLAYER_FIELDS_APPLIED' for e in current)}; compositions: {sum(e['stage'] == 'PLAYER_FIELDS_AT_COMPOSITION' for e in current)}; material routes: {sum(e['stage'] == 'MATERIAL_ROUTE_CHECKED' for e in current)}")
    print(f"Current session translation loaded: {any(e['stage'] == 'APPEARANCE_TRANSLATION_LOADED' for e in current)}")
    for stage in ("MAP_REJECTED", "RELIC_MISSING", "TALENT_MISSING", "GOALIE_TALENT_MISSING", "ABILITY_MISSING", "SKATER_ERROR", "HEAD_SKIN_MISSING", "HELMET_SKIN_MISSING", "GOALIE_SKIN_MISSING", "FIELD_AUDIT_ERROR", "MATERIAL_ROUTE_MISMATCH"):
        for event in current:
            if event["stage"] != stage:
                continue
            print(f"ISSUE {event['utc']} {stage}: {event['subject']} {event['field']} {event['value']}")
    if by_stage["MATCH_STARTED"]:
        last = by_stage["MATCH_STARTED"][-1]
        team = last["subject"]
        print(f"Latest match: {team} at {last['utc']}")
        rows = [row for row in planned if row["location"].startswith("teams/" + team + "/")]
        print(f"Assigned choices in that opponent: {len(rows)} (see COVERAGE.csv for each file and exact value)")
        relevant = [event for event in events if event["subject"] == team or event["subject"].startswith(team + " ")]
        for event in relevant[-20:]:
            if event["stage"].endswith(("APPLIED", "MISSING", "ERROR")):
                print(f"  {event['stage']}: {event['subject']} / {event['field']} / {event['value']}")
    unseen = [team for team in teams if team not in entered]
    if unseen:
        print(f"Next unentered opponent: {unseen[0]} ({len(unseen)} remain)")
    print("Runtime application and match entry are evidence of reaching content; visual behavior still needs player confirmation.")


if __name__ == "__main__":
    main()
