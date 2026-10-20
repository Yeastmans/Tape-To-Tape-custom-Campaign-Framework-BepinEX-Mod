"""Reconcile extracted build-25736191 data with Studio's existing behavior ownership.

Run extract_squad_rules.py and audit_opponent_effects.py first. This is a native
data / routing audit, not evidence that all combinations have been played.
"""
from pathlib import Path
import collections
import csv
import json
import re

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/behavior-audit-25736191'
catalog = json.loads((OUT / 'native-all/catalog.json').read_text(encoding='utf-8-sig'))
objects = catalog['objects']
templates = {t['id']: t for t in json.loads((ROOT / 'assets/squad-templates.json').read_text(encoding='utf-8-sig'))['templates']}
relic_table = collections.defaultdict(set)
for line in (ROOT / 'assets/template-starting-relics.tsv').read_text().splitlines():
    if line.strip():
        squad, identity, name, kind, level = line.split('\t')
        relic_table[squad].add((identity, name, kind, int(level)))

behaviors = []
for line in (ROOT / 'src/CampaignCore/SquadBehaviors.cs').read_text(encoding='utf-8').splitlines():
    if 'new SquadBehavior(' in line:
        values = re.findall(r'"([^"]*)"', line)
        behaviors.append(dict(key=values[0], name=values[1], help=values[2], templates=values[3:]))
effects = []
for line in (ROOT / 'src/CampaignCore/OpponentEffects.cs').read_text(encoding='utf-8').splitlines():
    if 'new OpponentEffect(' in line:
        values = re.findall(r'"([^"]*)"', line)
        effects.append(dict(key=values[0], name=values[1], group=values[2], help=values[3], classifier=values[4], consumers=values[5].split(';')))

def obj(ref):
    return objects[ref['ref']]

squads = []
differences = []
for key in catalog['squads']:
    squad = objects[key]['data']
    identity = squad['id']
    template = templates[identity]
    team = obj(squad['startingTeam'])['data']
    refs = [x['Relic'] for x in squad['m_RelicsData']] + team['relics']
    relics = [obj(x) for x in refs if x]
    relic_set = {(r['data']['id'], r['name'], r['type'], r['data'].get('level', 1)) for r in relics}
    if relic_set != relic_table[identity]:
        differences.append(dict(squad=identity, field='starting relics', native=sorted(relic_set), studio=sorted(relic_table[identity])))
    checked = dict(bench_capacity=team['benchSize'], starting_benchwarmer_limit=squad['m_MaxBenchwarmersOnBench'],
                   free_agent_intro=bool(squad['m_UsesFreeAgentIntro']), forward_array_length=len(team['forwards']),
                   modes=[{0: 'Timer', 1: 'Maxgoal'}[m] for m in squad['m_SupportedGameModes']],
                   description=catalog['english'].get('squads/'+identity+'/desc', ''))
    for field, value in checked.items():
        if template[field] != value:
            differences.append(dict(squad=identity, field=field, native=value, studio=template[field]))
    squads.append(dict(id=identity, name=template['name'], native_description=checked['description'],
                       behavior_controls=[b['key'] for b in behaviors if identity in b['templates']],
                       relics=[r['name'] for r in relics], asset=key,
                       placement='Custom teams / Squad behaviors; Team roster / Team relics; individual player talents and abilities',
                       inherited_data={k: squad[k] for k in ['excludedRelicCategory','excludedRelics','excludedTalentCategory','excludedTalents','excludedAbilities','m_OverridesIceColorTint','m_IceColorTint','m_AdBoardsOverride','m_SupportedGameModes']}))
assert set(templates) == {s['id'] for s in squads}

teams = []
for key in catalog['extra_roots']:
    item = objects[key]
    if item['type'] != 'Data.TeamData':
        continue
    data = item['data']
    teams.append(dict(asset=key, id=data['id'], name=data['teamName'], relics=data['relics'],
                      offensiveAI=data['offensiveAI'], defensiveAI=data['defensiveAI'], arena=data['arenaData'], audio=data['audioData'],
                      forwards=data['forwards'], goalie=data['goalie'], lines=data['lines'],
                      placement='Team roster and player editor for relics, talents and abilities; Opponent settings for match effects, formations, surface and puck'))

other = {
    'Arena.IceSetup.<SetupGolfIceOverlay>d__63$$MoveNext': 'Opponent settings / Playing surface; explicit surface and Golf overlay handling',
    'PuckManager$$InitializePuck': 'Opponent settings / Puck or ball; explicit puck handling',
    'UI.InGameMenu.<>c__DisplayClass37_0.<<ShowGameMenu>g__ShowMenu|0>d$$MoveNext': 'Opponent settings / Tycoons sponsors; separate menu-metadata hook',
    'ProfileData$$ResetPlayableTeamsToDefaultLocks': 'Native profile identity; intentionally not an opponent gameplay effect',
    'ProfileData$$ResetSavedUnlocks': 'Native profile identity; intentionally not an opponent gameplay effect',
    'ProfileData.<Load>d__114$$MoveNext': 'Native profile identity; intentionally not an opponent gameplay effect',
    'STS.Map.MapObject.<>c$$<GetIntroBoss>b__51_0': 'Native map opponent generation; authored Maps choose explicit opponents',
    'STS.Map.MapObject.<>c$$<GetIntroBoss>b__51_1': 'Native map opponent generation; authored Maps choose explicit opponents',
    'STS.Map.MapObject.<>c$$<SetOpponents>b__39_0': 'Native map opponent generation; authored Maps choose explicit opponents',
    'State.EndOfGame.EndOfGameFlowController$$CheckRunWon': 'Campaign finality; custom node completion scope, not an opponent checkbox',
    'State.EndOfGame.EndOfGameFlowController$$UpdateCampaignContext': 'Campaign finality; custom node completion scope, not an opponent checkbox',
    'State.GameModeControllers.GameControllerBase$$InitializeEndOfGame': 'Campaign finality; custom node completion scope, not an opponent checkbox',
    'Tape2Tape.CampaignUtil$$IsFinalMatch': 'Campaign finality; custom node completion scope, not an opponent checkbox',
    'State.GolferOutro.GolferOutro$$AppliesToCurrentMatch': 'Native winning-Golfer campaign outro eligibility; not routed by opponent effects',
    'State.GolferOutro.GolferOutro$$IsGolferCampaignMatch': 'Native Golfer campaign outro eligibility; not routed by opponent effects',
    'Tape2Tape.Analytics.Extensions.UnityAnalyticsServiceExtensions$$RecordRunCompletedIfCurrentMatchEndsRun': 'Native completion telemetry; not an authorable gameplay control',
    'State.EndOfGame.Steps.CampaignStatsStep$$UpdateFreeAgentUnlockStats': 'Native permanent unlock statistics; preserve native identity',
    'Stats.PersistentStats.StatCollector$$OnKnockoutForUnlocks': 'Native permanent unlock statistics; preserve native identity',
    'Stats.PersistentStats.StatCollector$$OnSkaterFrogged': 'Native permanent unlock statistics; preserve native identity',
    'Tape2Tape.Customization.SaveUtils.<>c$$<ResetSave>b__0_0': 'Native profile reset identity; not an opponent gameplay effect'
}
route_audit = json.loads((OUT / 'opponents/route-audit.json').read_text())
assert not route_audit['unmatched'] and not route_audit['shared_addresses']
assert set(route_audit['other_consumers']) == set(other), 'New native consumers require explicit review'
callers = json.loads((OUT / 'opponents/native-callers.json').read_text())
consumer_effects = collections.defaultdict(set)
for effect in effects:
    for consumer in effect['consumers']:
        owner, name = consumer.rsplit('.', 1)
        consumer_effects[owner.replace('/', '.')+'$$'+name].add(effect['key'])
routes = []
for row in callers:
    for caller in row['callers']:
        if caller in consumer_effects:
            disposition = 'Opponent settings / ' + ', '.join(sorted(consumer_effects[caller]))
        elif caller.startswith(('Data.TeamData$$Is', 'Tape2Tape.Team$$Is')):
            disposition = 'Classifier implementation or delegation; retains identity outside scoped consumers'
        else:
            disposition = other[caller]
        routes.append(dict(method=caller, method_rva=row['method_rva'], instruction_rva=row['instruction_rva'],
                           classifiers='; '.join(row['classifiers']), placement=disposition))
with (OUT / 'native-opponent-placement.csv').open('w', encoding='utf-8-sig', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=list(routes[0])); writer.writeheader(); writer.writerows(routes)

result = dict(game_sha256=catalog['game_sha256'], metadata_sha256=catalog['metadata_sha256'], squad_count=len(squads),
              team_count=len(teams), linked_objects=len(objects), squad_behavior_controls=len(behaviors),
              opponent_controls=len(effects), configured_routes=route_audit['routes'], native_classifier_call_sites=len(callers),
              template_differences=differences, unresolved_asset_references=catalog['errors'],
              squads=squads, teams=teams, opponent_effects=effects, non_effect_consumers=other)
(OUT / 'base-behavior-placement.json').write_text(json.dumps(result, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
lines = ['# Installed base-game behavior placement', '',
         'Build 25736191. Native code and asset audit; this is not an all-combinations gameplay pass.', '',
         f'{len(squads)} squads; {len(teams)} team assets; {len(objects)} linked objects; {len(behaviors)} squad controls; {len(effects)} opponent controls.', '',
         '## Player squads', '', '| Squad | Base-game description | Starting relics |', '|---|---|---|']
for squad in squads:
    lines.append('| '+squad['name']+' | '+squad['native_description'].replace('|','/')+' | '+(', '.join(squad['relics']) or 'None')+' |')
lines += ['', 'Squad behavior overrides are grouped under Starting choices, Roster and bench, Squad match rules, Recruitment and rewards, Maps and opponents, and Presentation and identity. Every control names its native squad source. Relics remain editable in Team roster; talents and abilities remain on the player. Native excluded reward categories, template appearance metadata, and supported modes come from the selected template unless a dedicated authored override replaces them.', '',
          '## Opponents', '', 'Every direct native team-classifier call site has a disposition in native-opponent-placement.csv. Independent effects are scoped to their native consumers; they do not replace profile identity or turn a match into a campaign finale.', '']
for group in dict.fromkeys(e['group'] for e in effects):
    lines += ['### '+group, '']
    for effect in effects:
        if effect['group'] == group:
            lines.append('- **'+effect['name']+'**: '+effect['help'])
    lines.append('')
lines += ['## Match rules and ownership', '',
          '- **Campaign Game options:** ordinary offside and Whistle / Lose Puck / Knockout penalty, applied for Challenge, Elite and Boss matches. Native Tchel and Linesman precedence still forces Knockout. Temporary reads leave saved GameplaySettings untouched; match end and scene teardown clear the scope.',
          '- **Squad match rules:** Tchel offside/manual pokecheck, Oldschool injury exemption, Eliminator scoring, Speedrun deadline, Solo control, roster limits and Sin Bin scorer removal retain their native squad source.',
          '- **Slapshot:** Slapshot Plushie is the source of the weak-other-shots condition. Native ShouldApplyNoSlapshotPenalty reads HaveRelicCached<SlapshotPlushie>; the shot helper limits horizontal magnitude to a randomized 4–8 range and a minimum vertical component of 4. Native starting talents, AI branches and wet-noodle audio also consult this relic. The separate Slapshot ID checks govern breakaway choice and reward weighting.',
          '- **Internal restrictions:** the shipped shared GameRestrictionData has an empty list. The NoActiveAbilities / NoBatting / NoBodycheck / NoLobbedPasses / NoSlapshot / NoWristShot enum is not proof of an enabled squad mechanic. No guessed controls were added for it.',
          '- **Campaign format:** native timed/goal-target run choice and node type determine the match setup; Challenge / Elite / Boss allow 1 / 2 / 3 overtime periods before shootout. This is node type, not act number. The template can restrict the offered mode; its existing Goal-target mode only control stays with the squad.',
          '- **Play Now:** periods, period length, skater counts, injuries and powerup-free choices originate in ordinary game setup. The native injury/powerup switches are not interchangeable with campaign rules (campaign uses Oldschool injury logic and skips Quick Game powerup removal). They were not presented as working campaign toggles.',
          '- **Native match systems:** icing, faceoffs, stoppages, mercy/goal targets, overtime and shootout code are not all independent menu switches. Only settings with an established campaign implementation are exposed as editable controls.',
          '- **Arena:** playing surface and puck/ball remain opponent match setup. Golf Fairway auto-on/manual-off and period layouts, plus Tycoons sponsors auto-on/manual-off, retain their existing behavior. Tycoons brawl and two-line gameplay remain separate.',
          '- **Identity and completion:** profile unlocks, save resets, telemetry and native outro eligibility remain native systems. Custom finality is owned by the terminal campaign node. Native Golfer outro eligibility is not enabled merely by choosing golfer gameplay effects.', '',
          '## Evidence and limits', '',
          'The audit pins installed GameAssembly and metadata hashes. Reproducible source files: native-all/catalog.json; opponents/native-callers.json and route-audit.json; squad-sites-verification.json; base-behavior-placement.json. The sibling squad-compatibility-25736191 folder contains native disassembly and shot/restriction/offside evidence.', '',
          f'Template comparison differences: {len(differences)}. Unresolved asset references: {len(catalog["errors"])} (helmet/upgrade-helmet visual references; no decoded rule fields were replaced with guesses). Direct-call analysis cannot prove that every compiler-inlined identity test has been found. Newly wired combinations and native gameplay outcomes are not claimed human-confirmed.', '']
(OUT / 'BASE_GAME_BEHAVIOR_PLACEMENT.md').write_text('\n'.join(lines), encoding='utf-8')
print(json.dumps({k: v for k,v in result.items() if k not in ['squads','teams','opponent_effects','non_effect_consumers','unresolved_asset_references']}, indent=2))
assert not differences, 'Native template drift must be resolved before publishing the audit'
