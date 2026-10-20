import sys,json,re
from pathlib import Path
sys.path.insert(0,'artifacts/unity-asset-tools')
import UnityPy
e=UnityPy.load(r'C:\Steam\steamapps\common\Tape to Tape\Tape to Tape_Data\data.unity3d')
rows=[]
for o in e.objects:
 if o.type.name!='MonoBehaviour': continue
 try:
  d=o.read(check_read=False); script=d.m_Script.read().m_Name
  if script not in ('NodeTypeConfig','MapTemplateData','MapConfig'): continue
  raw=o.get_raw_data()
  row={'file':o.assets_file.name,'id':o.path_id,'name':d.m_Name,'script':script,'raw':raw.hex(),'strings':[x.decode('ascii') for x in re.findall(rb'[ -~]{4,}',raw)]}
  rows.append(row)
  if script=='NodeTypeConfig': print(json.dumps(row))
 except Exception: pass
Path('artifacts/random-node-pools-raw.json').write_text(json.dumps(rows,indent=2))
print('configs',len(rows))
