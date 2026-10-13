"""Build the cleanup allow-list from fields actually read by the current runtime.
Catalog values are validated by Studio's packaged catalogs, never old user dumps.
"""
import json, re
from pathlib import Path
root = Path(__file__).resolve().parents[1]
s = (root/'src/CustomCampaignFramework/Plugin.cs').read_text(encoding='utf-8-sig')
sections = {
 'campaign': s.split('internal static void LoadConfig()',1)[1].split('internal static void ApplyTeamField',1)[0],
 'team': s.split('internal static void ApplyTeamField',1)[1].split('internal static void ApplyPlayerField',1)[0],
 'player': s.split('internal static void ApplyPlayerField',1)[1].split('private static void ParseKvFile',1)[0]
}
fields = {kind: sorted(set(re.findall(r'key == "([^"]+)"', text))) for kind,text in sections.items()}
fields['player'] += ['position', 'override team settings', 'goalie skill', 'studio base free agent', 'studio base pool player', 'studio player source', 'logo from']
fields['team'] += ['talents', 'team talents']
# Export snapshots are current import-library data, not obsolete campaign options.
export_fields = ['export version', 'export model', 'export role', 'export talent ids', 'export ability id', 'export relic ids', 'export logo mode', 'export logo sha256', 'export logo id', 'export logo png']
fields['player'] += export_fields
fields['team'] += export_fields
assert all(len(v) > 10 for v in fields.values()), 'Runtime parser changed: review cleanup field extraction.'
(root/'assets/upgrade-fields.json').write_text(json.dumps(fields, indent=2)+'\n', encoding='utf-8')
print('Generated current upgrade fields:', {k: len(v) for k,v in fields.items()})

