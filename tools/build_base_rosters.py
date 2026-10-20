"""Build Studio's bundled base-game roster snapshot from decoded game data.

Input: artifacts/native-rosters/native.json (tools/extract_native_rosters.py),
which is read from the installed game files only. Nothing here comes from a
runtime dump, so campaign edits to the game's teams cannot leak in.

Output: a campaign-format snapshot (default artifacts/native-rosters/base-rosters):
  Base Game Teams/<team>/team.txt, goalie-equipment.cfg, players/<Position> - <Name>.txt
  Base Game Players/<Name>.txt   (one file per distinct native player)
"""
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
native = json.loads((ROOT / 'artifacts/native-rosters/native.json').read_text(encoding='utf-8'))
out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'artifacts/native-rosters/base-rosters'
records, refs = native['records'], native['refs']

SIZES = ['ExtraSmall', 'Small', 'Medium', 'Big', 'ExtraBig', 'ExtraExtraBig']  # FieldSchema.Sizes
SLOTS = ['Left Wing', 'Right Wing', 'Center', 'Left Defense', 'Right Defense']   # mod slot order
SLOTS += ['Line 2 ' + s for s in SLOTS]
# Placeholders the game fills at runtime; not importable teams.
SKIP_TEAMS = {'RandomHome', 'RandomVisitor', 'defaultTeamForCustomization'}
# Same display name, different native team: label the variants.
VARIANTS = {'CHICAO_CAMPAIGN_ACT1': 'Campaign Act 1', 'chicago calaveras onboarding': 'Onboarding',
            'referee Squad Team': 'Squad'}

def rgb(c):
    return ', '.join(str(round(max(0.0, min(1.0, c[k])) * 255)) for k in 'rgb')
def ids(items):
    return ', '.join(refs[x['ref']]['id'] for x in items or [] if x and refs.get(x['ref']))
def name_of(p):
    return ' '.join(x for x in (p['firstName'].strip(), p['lastName'].strip()) if x)
def safe(text):
    for ch in '<>:"/\\|?*':
        text = text.replace(ch, '_')
    return text.rstrip(' .')

def palette(home, away, number_home=None, number_away=None):
    """Player-level colour keys (AppearanceContract.ColorKey) for both kits."""
    doc = {}
    for kit, a in ((home, ''), (away, ' Away')):
        if not kit:
            continue
        for scheme, names in (('jerseyScheme', ['Jersey' + a + ' Color', 'Jersey' + a + ' Secondary Color', 'Jersey' + a + ' Accent Color']),
                              ('skatesScheme', ['Skates' + a + ' Color', 'Blade' + a + ' Color', 'Laces' + a + ' Color'])):
            for ch, key in zip(('primaryColor', 'secondaryColor', 'tertiaryColor'), names):
                doc[key] = rgb(kit[scheme][ch])
        for scheme, piece in (('helmetScheme', 'Helmet'), ('glovesScheme', 'Gloves'), ('pantsScheme', 'Pants'),
                              ('socksScheme', 'Socks'), ('stickScheme', 'Stick'), ('numberScheme', 'Number')):
            for ch, suffix in zip(('primaryColor', 'secondaryColor', 'tertiaryColor'), (' Color', ' Secondary Color', ' Tertiary Color')):
                doc[piece + a + suffix] = rgb(kit[scheme][ch])
    if number_home: doc['Number Tertiary Color'] = rgb(number_home)
    if number_away: doc['Number Away Tertiary Color'] = rgb(number_away)
    return doc

SKATER_SKINS = [('Face', 'headSkin'), ('Stick', 'stickSkin'), ('Helmet', 'helmetSkin'), ('Helmet Away', 'helmetAwaySkin'),
                ('Body', 'bodySkin'), ('Body Away', 'bodyAwaySkin'), ('Bicep', 'bicepSkin'), ('Bicep Away', 'bicepAwaySkin'),
                ('Gloves', 'gloveSkin'), ('Gloves Away', 'gloveAwaySkin'), ('Pants', 'pantsSkin'), ('Pants Away', 'pantsAwaySkin'),
                ('Skates', 'skateSkin'), ('Skates Away', 'skateAwaySkin'), ('Glasses', 'glassesSkin'),
                ('Number Skin', 'numberSkin'), ('Logo Skin', 'logoSkin')]
UNIFORM = [k for k, _ in SKATER_SKINS if k not in ('Face', 'Glasses', 'Number Skin', 'Logo Skin')]
GOALIE_SKINS = [('Helmet Skin', 'helmetSkin'), ('Skin', 'skin'), ('Skin Away', 'awaySkin'), ('Glove Skin', 'gloveSkin'),
                ('Glove Away', 'awayGloveSkin'), ('Blocker Skin', 'blockerSkin'), ('Blocker Away', 'awayBlockerSkin'),
                ('Pads Skin', 'padsSkin'), ('Pads Away', 'awayPadsSkin'), ('Stick Skin', 'stickSkin'), ('Stick Away', 'awayStickSkin')]

def write(path, header, fields):
    width = max([len(k) for k, _ in fields] + [24])
    lines = [header] + [f'{k.ljust(width)} = {v}' for k, v in fields]
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text('\n'.join(lines) + '\n', encoding='utf-8')

def skater_fields(p, position, override, colors):
    f = [('Name', name_of(p)), ('Number', p['number']), ('Left Handed', 'yes' if p['isLefty'] else 'no'),
         ('Skin Color', 'dark' if p['isBlack'] else 'light'), ('Size', SIZES[p['skaterSize']]),
         ('Speed', p['speed']), ('Shot Power', p['shotPower']), ('Accuracy', p['shotAccuracy']), ('Checking', p['checking']),
         ('Ability', ids([p['ability']]) if p.get('ability') else ''), ('Talents', ids(p['powerups']))]
    if position: f.append(('Position', position))
    f.append(('Override Team Settings', 'yes' if override else 'no'))
    f += [(k, p[n]) for k, n in SKATER_SKINS]
    if abs(p.get('sizeOffsetPercentage', 1.0) - 1.0) > 1e-6: f.append(('Size Offset', p['sizeOffsetPercentage']))
    f += sorted(colors.items())
    return f

def goalie_fields(g, override, colors):
    f = [('Name', name_of(g)), ('Number', g['number']), ('Position', 'Goalie'),
         ('Skill', g['skill']), ('Catching', g['catchingSkill']), ('Glove', g['gloveSkill']), ('Blocker', g['blockerSkill']),
         ('Five Hole', g['fiveHoleSkill']), ('Standing Speed', g['standingSpeed']), ('Butterfly Speed', g['butterflySpeed']),
         ('Control', g['controlSkill']), ('Recovery', g['recoverySkill']), ('Pass Power', g['passPower']),
         ('Shot Power', g['shotPower']), ('Poke Check', g['pokecheckSkill']), ('Depth', g['depth']),
         ('Pass Read', g['passReadSkill']), ('Goalie Talents', ids(g['powerups'])),
         ('Override Team Settings', 'yes' if override else 'no')]
    f += [(k, g[n]) for k, n in GOALIE_SKINS] + [('Logo Skin', g['logoSkin'])]
    f += sorted(colors.items())
    return f

teams = [(k, t) for k, t in records.items() if t['_class'] == 'TeamData' and t['m_Name'] not in SKIP_TEAMS]
names = Counter(t['teamName'] for _, t in teams)
report, home_team = defaultdict(list), {}
for key, t in sorted(teams, key=lambda kt: (kt[1]['teamName'], kt[1]['m_Name'])):
    label = t['teamName'] if names[t['teamName']] == 1 or t['m_Name'] not in VARIANTS else f"{t['teamName']} ({VARIANTS[t['m_Name']]})"
    assert label not in report['teams'], ('two native teams would share a folder', label, t['m_Name'])
    folder = out / 'Base Game Teams' / safe(label)
    forwards = [(i, records[r['ref']]) for i, r in enumerate(t['forwards']) if r]
    for i, p in forwards:
        home_team.setdefault(id(p), t)
    if t['goalie']:
        home_team.setdefault(id(records[t['goalie']['ref']]), t)
    uniform = {k: Counter(p[n] for _, p in forwards).most_common(1)[0][0] for k, n in SKATER_SKINS if k in UNIFORM} if forwards else {}
    h, a = t['homeColors'], t['awayColors']
    fields = [('Team Name', label), ('City', t['city']), ('Abbreviation', t['nickname']), ('Logo From', t['teamName'])]
    fields += [('Jersey Primary', rgb(h['jerseyScheme']['primaryColor'])), ('Jersey Secondary', rgb(h['jerseyScheme']['secondaryColor'])),
               ('Jersey Accent', rgb(h['jerseyScheme']['tertiaryColor'])), ('Away Primary', rgb(a['jerseyScheme']['primaryColor'])),
               ('Away Secondary', rgb(a['jerseyScheme']['secondaryColor'])), ('Away Accent', rgb(a['jerseyScheme']['tertiaryColor']))]
    for kit, aw in ((h, ''), (a, ' Away')):
        for scheme, piece in (('helmetScheme', 'Helmet'), ('glovesScheme', 'Gloves'), ('pantsScheme', 'Pants'),
                              ('socksScheme', 'Socks'), ('stickScheme', 'Stick'), ('numberScheme', 'Number')):
            fields += [(piece + aw + ' Color', rgb(kit[scheme]['primaryColor'])), (piece + aw + ' Secondary Color', rgb(kit[scheme]['secondaryColor']))]
            if piece != 'Number':
                fields.append((piece + aw + ' Tertiary Color', rgb(kit[scheme]['tertiaryColor'])))
        fields += [('Skates' + aw + ' Color', rgb(kit['skatesScheme']['primaryColor'])), ('Blade' + aw + ' Color', rgb(kit['skatesScheme']['secondaryColor'])),
                   ('Laces' + aw + ' Color', rgb(kit['skatesScheme']['tertiaryColor']))]
    fields += [('Number Color Home', rgb(t['jerseyHomeNumberColor'])), ('Number Color Away', rgb(t['jerseyAwayNumberColor'])),
               ('Transition Primary', rgb(t['primaryColorTransition'])), ('Transition Secondary', rgb(t['secondaryColorTransition'])),
               ('Transition Tertiary', rgb(t['tertiaryColorTransition']))]
    fields += list(uniform.items())
    fields += [('Bench Size', t['benchSize']), ('Bench Head', t['vanillaBenchPlayerHead'] or ''), ('Team Relics', ids(t['relics']))]
    write(folder / 'team.txt', f"# Base game team: {t['teamName']} (native {t['m_Name']})", fields)
    colors = palette(h, a, t['jerseyHomeNumberColor'], t['jerseyAwayNumberColor'])
    for i, p in forwards:
        if i >= len(SLOTS):
            report['beyond 10 slots'].append(f"{label}: {name_of(p)}"); continue
        override = any(p[n] != uniform[k] for k, n in SKATER_SKINS if k in UNIFORM)
        write(folder / 'players' / f"{SLOTS[i]} - {safe(name_of(p))}.txt", f"# {label} / {SLOTS[i]} (native {p['m_Name']})",
              skater_fields(p, SLOTS[i], override, colors))
    if t['goalie']:
        g = records[t['goalie']['ref']]
        gcolors = palette(t['homeGoalieColors'], t['awayGoalieColors'])
        write(folder / 'goalie-equipment.cfg', f"# Goalie equipment: {label}", [(k, g[n]) for k, n in GOALIE_SKINS])
        write(folder / 'players' / f"Goalie - {safe(name_of(g))}.txt", f"# {label} / Goalie (native {g['m_Name']})", goalie_fields(g, False, gcolors))
    report['teams'].append(label)

# One loose file per distinct player: the copy used by the most teams wins.
usage = Counter(r['ref'] for _, t in teams for r in t['forwards'] + [t['goalie']] if r)
by_name = defaultdict(list)
for k, p in records.items():
    if p['_class'] != 'TeamData' and name_of(p):
        by_name[name_of(p)].append((k, p))
for n, group in sorted(by_name.items()):
    roles = {p['_class'] for _, p in group}
    for cls in sorted(roles):
        k, p = max((x for x in group if x[1]['_class'] == cls), key=lambda x: (usage[x[0]], x[1]['m_Name']))
        file = safe(n) + ('' if len(roles) == 1 or cls == 'ForwardData' else ' (Goalie)') + '.txt'
        t = home_team.get(id(p))
        if cls == 'ForwardData':
            colors = palette(t['homeColors'], t['awayColors'], t['jerseyHomeNumberColor'], t['jerseyAwayNumberColor']) if t else palette(p['colorSchemes'], None)
            write(out / 'Base Game Players' / file, f"# Base game player (native {p['m_Name']})", skater_fields(p, '', False, colors))
        else:
            colors = palette(t['homeGoalieColors'], t['awayGoalieColors']) if t else palette(p['colorSchemes'], None)
            write(out / 'Base Game Players' / file, f"# Base game goalie (native {p['m_Name']})", goalie_fields(p, False, colors))
        report['players'].append(file)

(out / 'provenance.json').write_text(json.dumps({
    'bundle_sha256': native['bundle_sha256'], 'decoder': native['decoder'],
    'source': 'Decoded from shipped game data (TeamData, ForwardData, GoaltenderData); no runtime dump',
    'counts': {'Base Game Teams': len(report['teams']), 'Base Game Players': len(report['players'])},
    'not_in_team_folders': report['beyond 10 slots']}, indent=2) + '\n', encoding='utf-8')
print(len(report['teams']), 'teams,', len(report['players']), 'players ->', out)
for line in report['beyond 10 slots']:
    print('  beyond 10 slots (kept in Base Game Players):', line)
