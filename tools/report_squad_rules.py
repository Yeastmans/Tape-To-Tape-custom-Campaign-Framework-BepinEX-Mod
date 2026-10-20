"""Produce a readable squad-template contract from the extracted game evidence."""
import collections
from functools import lru_cache
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/squad-rules'
c = json.loads((OUT / 'catalog.json').read_text(encoding='utf-8'))
objects = c['objects']
refs = json.loads((OUT / 'code-references.json').read_text())
dump = (Path(c['decoder_metadata']) / 'dump.cs').read_text(encoding='utf-8')


@lru_cache(None)
def enum(name, value, flags=False):
    entries = {int(n): label for label, n in re.findall(r'public const ' + name + r' (\w+) = (-?\d+);', dump)}
    if value in entries:
        return entries[value]
    if flags:
        names = [label for n, label in entries.items() if n > 0 and n & (n-1) == 0 and value & n]
        known = sum(n for n in entries if n > 0 and n & (n-1) == 0 and value & n)
        if value & ~known:
            names.append(f'unnamed mask {value & ~known}')
        return ' + '.join(names) or str(value)
    return f'Unknown ({value})'


def data(ref):
    return objects[ref['ref']]['data'] if ref and 'ref' in ref else {}


def names(values):
    return ', '.join(v['name'] for v in values if v) or 'None'


positions = ['leftWinger', 'rightWinger', 'center', 'leftDefensemen', 'rightDefensemen']
labels = ['Left wing', 'Right wing', 'Center', 'Left defense', 'Right defense']
summary = []
for key in c['squads']:
    s = objects[key]['data']
    t = data(s['startingTeam'])
    sid = s['id']
    roster = t['forwards']
    ids = {data(v).get('id'): i for i, v in enumerate(roster) if v}
    lines = [{label: ids.get(line[pos]) for pos, label in zip(positions, labels)} for line in t['lines']]
    maps = []
    for index, m in enumerate(s['maps']):
        md = data(m)
        variants = []
        for v in md['mapTemplates']:
            vd = data(v)
            nodes = [n for layer in vd['layers'] for n in layer['nodes']]
            variants.append(dict(name=v['name'], layers=len(vd['layers']), nodes=len(nodes),
                node_counts=dict(collections.Counter(enum('NodeType', n['type']) for n in nodes)),
                layout_ref=v['ref']))
        maps.append(dict(sequence=index+1, asset=m['name'], act=md['act'], variants=variants,
                         random_node_config=md.get('nodeTypeConfig'), scale=md['mapPositionScale']))
    methods = sorted({m for r in refs.get(sid, []) for m in r['methods'] if not m.startswith('Language.Lua.')})
    entry = dict(id=sid, name=c['english'].get(f'squads/{sid}/name', s['squadName']),
        description=c['english'].get(f'squads/{sid}/desc', ''),
        forward_array_length=len(roster), authored_players=sum(v is not None for v in roster),
        empty_entries=[i for i, v in enumerate(roster) if v is None], authored_lines=lines,
        locked_positions=[label for pos, label in zip(positions, labels) if t[pos+'LineUpCreationLocked']],
        bench_capacity=t['benchSize'], starting_benchwarmer_limit=s['m_MaxBenchwarmersOnBench'],
        free_agent_intro=bool(s['m_UsesFreeAgentIntro']), modes=[enum('GameRule', n) for n in s['m_SupportedGameModes']],
        maps=maps, opponents=s['campaignOpponents'], starting_relics=s['m_RelicsData'], team_relics=t['relics'],
        elite_reward_category=enum('RelicCategory', s['eliteRewardRelicCategory'], True),
        excluded_relic_category=enum('RelicCategory', s['excludedRelicCategory'], True),
        excluded_talent_category=enum('TalentCategory', s['excludedTalentCategory'], True),
        excluded_relics=s['excludedRelics'], excluded_talents=s['excludedTalents'], excluded_abilities=s['excludedAbilities'],
        swap_pool_count=len(s['m_CustomSwapPool']), custom_swap_pool=s['m_CustomSwapPool'],
        ice_tint=s['m_IceColorTint'] if s['m_OverridesIceColorTint'] else None,
        skater_marker=s['m_SkaterMarkerPrefab'], code_methods=methods, source=key)
    summary.append(entry)
summary.sort(key=lambda x: x['name'])
(OUT / 'templates.json').write_text(json.dumps(dict(game_sha256=c['game_sha256'], templates=summary), indent=2), encoding='utf-8')

report = ['# Base squad template rules', '',
    'Extracted from installed roguelite build 25470389. No game or campaign files were changed.', '',
    'GameAssembly SHA-256: `' + c['game_sha256'] + '`.', '',
    '## Scope and evidence', '',
    'All 19 concrete base squad assets and 664 linked gameplay assets were decoded with matching game metadata. '
    'The runtime catalog also lists an Upcoming placeholder; it is not a selectable rules template. '
    'Descriptions below are the game\'s own English text. Serialized settings are direct asset evidence. '
    'Native method references identify ID-dependent implementation sites, not proof of complete transplanted behavior. '
    'No gameplay verification was performed.', '',
    'Six unresolved references are helmet assets reached through linked talents; they are recorded in catalog.json. '
    'All squad, starting-team, map, opponent and directly listed rule-effect records decoded. '
    'The catalog retains unknown category bits instead of assigning invented meanings.', '',
    '## Custom-template requirements', '',
    '- Copy maps, supported modes, structural roster/line rules, relevant rule effects and reward restrictions. '
    'Do not import base players, names, uniforms, faces, or their personal abilities/talents.',
    '- Double Decker requires ten custom skaters arranged into two explicit five-player lines, plus a goalie. '
    'Its native forward array has twelve entries: ten populated and two null. Do not request twelve players or assume array order equals line position.',
    '- Preserve the distinction between forward-array capacity, active skater count, roster creation locks, bench capacity, '
    'starting benchwarmer limit and available draft slots. Referee and Eliminator demonstrate why array length alone is insufficient.',
    '- All Angus has an identity-specific swap pool of 17 Angus variants. With no importing, this needs a custom-player pool; copying that pool would violate the requested behavior.',
    '- Some effects originate in the base starter\'s talents. List them separately; do not silently import the player or personal powers.',
    '- Keep custom squad IDs stable for saves. Native ID-based rules need explicit template-aware handling; changing only maps or cloning a ScriptableObject does not reproduce every rule.',
    '- Existing custom-squad code always unlocks positions and replaces starting relics. Those operations conflict with templates such as Trio and special starting-rule relics. '
    'The existing five-player fallback and starter reseating also require changes before multi-line templates can be supported.',
    '- Campaign-authored map layouts and global Maps settings need an explicit precedence over the chosen base maps; the picker must disclose effective map overrides.', '',
    '## Summary', '', '| Squad | Native special rule | Roster structure |', '|---|---|---|']
for s in summary:
    shape = f"{s['forward_array_length']} array entries; {len(s['authored_lines'])} authored lines"
    if s['id'] == 'lines': shape = '10 skaters, 2 lines; 2 additional empty entries'
    if s['locked_positions']: shape += '; locked ' + ', '.join(s['locked_positions'])
    report.append(f"| {s['name']} | {s['description'].replace(chr(10), ' ').replace('|', '/')} | {shape} |")

base_effect_fields = {'m_GameObject', 'm_Enabled', 'm_Script', 'm_Name', 'debugAppearMoreOften', 'relicName', 'level', 'rarity', 'category', 'description', 'upgradedDescription', 'statBonusDescription', 'flavorTextLoc', 'intValue', 'id', 'controllerScriptName', 'hasUpgradeText', 'm_DisplayInHUD', 'sprite', 'isHidden', 'isClone'}
for s in summary:
    report += ['', '## ' + s['name'], '', s['description'], '',
        f"- Identity: `{s['id']}`; asset `{s['source']}`.",
        f"- Modes: {', '.join(s['modes'])}. Maxgoal is target-score mode; Timer is timed mode.",
        f"- Roster array: {s['forward_array_length']} entries, {s['authored_players']} populated, empty indices {s['empty_entries']}. These are stored entries, not required active players.",
        f"- Lines: {len(s['authored_lines'])} preauthored. Zero means the game constructs the lineup during run creation.",
        f"- Creation-locked positions: {', '.join(s['locked_positions']) or 'None'}.",
        f"- Team bench capacity: {s['bench_capacity']}; starting benchwarmer limit: {s['starting_benchwarmer_limit']}; free-agent intro: {s['free_agent_intro']}.",
        f"- Custom swap pool: {s['swap_pool_count']} players (identity data, not imported).",
        f"- Squad rule relics: {names([r['Relic'] for r in s['starting_relics']])}.",
        f"- Starting-team relics: {names(s['team_relics'])}.",
        f"- Elite reward category: {s['elite_reward_category']}.",
        f"- Excluded relic category: {s['excluded_relic_category']}; excluded talent category: {s['excluded_talent_category']}.",
        f"- Excluded relics: {names(s['excluded_relics'])}.",
        f"- Excluded talents: {names(s['excluded_talents'])}.",
        f"- Excluded abilities: {names(s['excluded_abilities'])}.",
        f"- Ice tint override: {json.dumps(s['ice_tint'])}; skater marker: {s['skater_marker']['name'] if s['skater_marker'] else 'None'}.",
        f"- Opponent pool: {s['opponents']['name']} (`{s['opponents']['ref']}`); full act-by-act opponents are in catalog.json."]
    for index, line in enumerate(s['authored_lines']):
        report.append(f"- Line {index+1}, position → forward array index: " + ', '.join(f'{p} → {i}' for p, i in line.items()) + '.')
    for relic in [r['Relic'] for r in s['starting_relics']] + s['team_relics']:
        params = {k:v for k,v in data(relic).items() if k not in base_effect_fields}
        report.append(f"- `{relic['name']}` parameters: `{json.dumps(params, ensure_ascii=False)}`.")
    team = data(objects[s['source']]['data']['startingTeam'])
    for player in team['forwards']:
        if not player: continue
        p = data(player)
        personal = {k:v for k,v in p.items() if any(x in k.lower() for x in ('talent', 'abilit', 'categor'))}
        report.append(f"- Base starter evidence only (do not import), `{player['name']}`: `{json.dumps(personal, ensure_ascii=False)}`.")
    report += ['', '### Maps', '']
    for m in s['maps']:
        report.append(f"- Sequence {m['sequence']}: {m['asset']} (stored act {m['act']}); scale {m['scale']}; random pool {m['random_node_config']['name'] if m['random_node_config'] else 'None'}.")
        for v in m['variants']:
            report.append(f"  - {v['name']}: {v['layers']} layers, {v['nodes']} nodes; " + ', '.join(f'{k}: {n}' for k,n in v['node_counts'].items()) + '.')
    report += ['', 'Every map node, edge, fixed opponent, reward count, free-agent choice count and random weight is retained in catalog.json.', '', '### Native ID-dependent implementation sites', '']
    report.extend('- `' + m + '`' for m in s['code_methods'])
    if not s['code_methods']: report.append('No direct references found by the verified RIP-relative literal scan. This does not prove there are no hardcoded or indirect rules.')
    if s['id'] == 'solo': report += ['', 'The localized description is a placeholder. Native sites above explicitly include solo control, controller limits, camera options and starter bench protection; precise gameplay semantics still need verification.']

(OUT / 'SQUAD_RULES.md').write_text('\n'.join(report)+'\n', encoding='utf-8')
print(f'Wrote {len(summary)} template contracts and SQUAD_RULES.md')
