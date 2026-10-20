import sys,json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'artifacts/unity-asset-tools'))
import UnityPy
from PIL import Image,ImageDraw
rows=json.loads((root/'artifacts/equipment-catalog/uniform-regions.json').read_text())
rows=[r for r in rows if re.search(r'/(S|N)-(Body|Forearm left|Bicep left|Leg left|Glove left|Helmet)$',r['region'])]
pages={Path(r['page']).stem for r in rows}
textures={}
folder=Path(r'C:\Steam\steamapps\common\Tape to Tape\Tape to Tape_Data\StreamingAssets\aa\StandaloneWindows64')
for page in pages:
 bundle=next(f for f in folder.glob('skatersprites_assets_'+page.lower()+'_*.bundle') if '_lso_' not in f.name)
 env=UnityPy.load(str(bundle))
 for obj in env.objects:
  if obj.type.name=='Texture2D': textures[page]=obj.read().image
out=root/'artifacts/equipment-catalog/uniform-crops';out.mkdir(exist_ok=True)
sheet=Image.new('RGB',(1000,((len(rows)+3)//4)*290),'#334455');draw=ImageDraw.Draw(sheet)
for i,r in enumerate(rows):
 im=textures[Path(r['page']).stem];scale=im.width/4096
 x,y,w,h=[round(r[k]*scale) for k in ('x','y','width','height')]
 if r['degrees']==90: crop=im.crop((x,y,x+h,y+w)).rotate(-90,expand=True)
 else: crop=im.crop((x,y,x+w,y+h))
 crop.save(out/(r['region'].replace('/','_')+'.png'))
 crop.thumbnail((220,240));a=(i%4)*250;b=(i//4)*290
 sheet.paste(crop,(a,b+30),crop);draw.text((a,b),r['region'].split('/')[-1],fill='white')
sheet.save(out/'contact.png')
print('Extracted',len(rows),'game atlas regions')
