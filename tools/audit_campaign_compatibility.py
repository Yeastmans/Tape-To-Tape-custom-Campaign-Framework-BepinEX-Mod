"""Read-only comparison of authored campaign equipment against current game assets."""
import csv
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CAMPAIGN = ROOT / 'test-campaigns/Imported Options Audit'
rows = list(csv.DictReader((ROOT / 'assets/equipment-catalog.tsv').open(encoding='utf-8-sig'), delimiter='\t'))
catalog = {(r['role'], r['asset']): r for r in rows}
fields = {
    'Face': 'headSkin', 'Body': 'bodySkin', 'Body Away': 'bodyAwaySkin', 'Helmet': 'helmetSkin', 'Helmet Away': 'helmetAwaySkin',
    'Bicep': 'bicepSkin', 'Bicep Away': 'bicepAwaySkin', 'Gloves': 'gloveSkin', 'Gloves Away': 'gloveAwaySkin',
    'Pants': 'pantsSkin', 'Pants Away': 'pantsAwaySkin', 'Skates': 'skateSkin', 'Skates Away': 'skateAwaySkin',
    'Stick': 'stickSkin', 'Glasses': 'glassesSkin', 'Logo Skin': 'logoSkin + final composed logo skin',
    'Number Skin': 'explicit existing skin; generated numbers use number',
    'Skin': 'skin', 'Skin Away': 'awaySkin', 'Helmet Skin': 'helmetSkin', 'Glove Skin': 'gloveSkin', 'Glove Away': 'awayGloveSkin',
    'Blocker Skin': 'blockerSkin', 'Blocker Away': 'awayBlockerSkin', 'Pads Skin': 'padsSkin', 'Pads Away': 'awayPadsSkin',
    'Stick Skin': 'stickSkin', 'Stick Away': 'awayStickSkin',
}
placements = list(csv.DictReader((CAMPAIGN / 'COVERAGE.csv').open(encoding='utf-8-sig')))
issues = []
for p in placements:
    field, value = p['field'], p['value']
    if field not in fields or '/' not in value:
        continue
    role = 'goalie' if p['scope'] == 'goalie' or '/Goalie - ' in p['location'] else 'skater'
    native = catalog.get((role, value))
    status = ''
    if native is None:
        if field != 'Number Skin' or (value != 'Numbers/Custom_Number' and not re.fullmatch(r'Numbers/Number_\d{1,2}(LH)?', value)):
            status = 'Missing exact native identity'
    elif native['missing_sources']:
        status = 'View-specific decal' if native['category'] in ('Logos', 'Numbers') else 'Missing goalie view; unsuitable for all-stage equipment'
    elif value.lower().endswith(('_iso', '_lso')) or value.endswith(('Helmet_Face','Helmet_Customization_Face')):
        status = 'Internal support layer; not standalone equipment'
    if status:
        issues.append(dict(p, status=status, native_field=fields[field], missing_views=native['missing_sources'] if native else 'all'))
out = CAMPAIGN / 'COMPATIBILITY_ISSUES.csv'
with out.open('w', encoding='utf-8-sig', newline='') as stream:
    writer = csv.DictWriter(stream, fieldnames=['scope','field','value','location','status','native_field','missing_views'])
    writer.writeheader(); writer.writerows(issues)
with (CAMPAIGN / 'FIELD_TRANSLATION.csv').open('w',encoding='utf-8-sig',newline='') as stream:
    writer = csv.writer(stream); writer.writerow(['field','native_destination','stage','notes'])
    for key, destination in fields.items():
        writer.writerow([key,destination,'selection + bench + stars + recruitment + match + Play Now','Missing native views listed separately; runtime appearance events verify rendering reaches each path'])
    for key,destination,notes in [
        ('Number','SkaterData.number','Also retains serialized numberSkin compatibility'),
        ('Logo From','Texture argument supplied to native renderer','Player override or team badge; separate from baked Logo Skin'),
        ('Override Team Settings','AppearanceFields.OverridesTeam','No retains stored custom values while applying the team appearance'),
        ('All home/away color channels','TeamColorsData scheme channels after MergeWithCustomSkaterColors','48 scheme channels plus bicep alias; copied per render, no shared team mutation'),
        ('Talents + Goalie Talents','SkaterData.powerups','Imported lists combined; native talent behavior can replace equipment'),
        ('Ability','SkaterData.ability','Same exact ID/full native asset lookup for starters and draft/bench'),
    ]: writer.writerow([key,destination,'all applicable stages',notes])
summary = {'placements_checked':len(placements),'issue_placements':len(issues),'unique_issues':sorted({(i['value'],i['status']) for i in issues})}
(CAMPAIGN / 'compatibility-summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))
