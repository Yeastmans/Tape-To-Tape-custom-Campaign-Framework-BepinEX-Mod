"""Extract original map atlas regions; run with Python 3.12 and UnityPy/Pillow.
Does not change the game, DLL, or editor. Regions are source artwork, not rendered Spine animations.
"""
import argparse, hashlib, json, re, sys, subprocess
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'artifacts/unity-asset-tools'))
import UnityPy
from PIL import Image, ImageDraw, ImageFont, ImageChops
parser=argparse.ArgumentParser()
parser.add_argument('--game-data', default=r'C:\Steam\steamapps\common\Tape to Tape\Tape to Tape_Data')
parser.add_argument('--node', default=str(Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'))
args=parser.parse_args()
out=ROOT/'assets/node-images'; source=out/'source'; regions=out/'regions'
source.mkdir(parents=True, exist_ok=True); regions.mkdir(exist_ok=True)
bundle=Path(args.game_data)/'data.unity3d'
env=UnityPy.load(str(bundle))
for obj in env.objects:
 if obj.assets_file.name!='sharedassets4.assets' or obj.path_id not in (54,112,116): continue
 data=obj.read()
 if obj.type.name=='Texture2D': data.image.save(source/'skeleton.png')
 else:
  raw=data.m_Script
  (source/data.m_Name).write_bytes(raw.encode('utf-8', errors='surrogateescape') if isinstance(raw,str) else raw)
subprocess.run([args.node,str(ROOT/'tools/extract_node_meshes.mjs')],cwd=ROOT,check=True)
atlas=Image.open(source/'skeleton.png').convert('RGBA')
entries={}; current=None
for line in (source/'skeleton.atlas').read_text().splitlines()[1:]:
 line=line.strip()
 if not line: continue
 if ':' not in line: current=line; entries[current]={}
 elif current:
  key,value=line.split(':',1); entries[current][key]=value
meshes=json.loads((source/'meshes.json').read_text())
manifest=[]
for name, entry in entries.items():
 x,y,w,h=map(int,entry['bounds'].split(',')); rotation=int(entry.get('rotate','0'))
 packed_w,packed_h=(h,w) if rotation in (90,270) else (w,h)
 assert x>=0 and y>=0 and x+packed_w<=atlas.width and y+packed_h<=atlas.height, name
 im=atlas.crop((x,y,x+packed_w,y+packed_h))
 mask=Image.new('L',im.size,0); md=ImageDraw.Draw(mask)
 matches=[m for m in meshes if m['region']==name]
 assert matches, f'Missing mesh: {name}'
 for mesh in matches:
  uv=mesh['uvs']; points=[(uv[i]*atlas.width-x,uv[i+1]*atlas.height-y) for i in range(0,len(uv),2)]
  tris=mesh['triangles']
  for t in range(0,len(tris),3): md.polygon([points[tris[t+j]] for j in range(3)],fill=255)
 im.putalpha(ImageChops.multiply(im.getchannel('A'),mask))
 if rotation: im=im.rotate(-rotation,expand=True)
 assert im.size==(w,h), (name,im.size,(w,h))
 filename=re.sub(r'[^A-Za-z0-9_.-]+','_',name)+'.png'
 im.save(regions/filename)
 manifest.append(dict(region=name,file='regions/'+filename,width=w,height=h,atlasBounds=[x,y,packed_w,packed_h],rotation=rotation,offsets=entry.get('offsets')))
primary=['blademaster','chaos','Coach/house','Fan','GM','Infirmary','TrainingCenter','Outdoor_Rink','Challenge_Act2','Smity_House','Smity_House_Act2']
primary += [n for n in entries if n.startswith('Stadium_') and n.count('_')==1]
primary += ['stadium_vintage2','Tycoons_Arena']
cols=5; cw=250; ch=230
sheet=Image.new('RGB',(cols*cw,70+((len(primary)+cols-1)//cols)*ch),'#192331'); draw=ImageDraw.Draw(sheet)
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
title=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',25)
draw.text((20,12),'Tape to Tape - extracted map artwork',font=title,fill='white')
draw.text((20,43),'Original atlas regions; separate animation layers retained in regions/',font=font,fill='#b5c5d7')
for i,name in enumerate(primary):
 row=next(r for r in manifest if r['region']==name); im=Image.open(out/row['file']); im.thumbnail((cw-24,ch-44),Image.Resampling.LANCZOS)
 x=(i%cols)*cw; y=70+(i//cols)*ch
 sheet.paste(im,(x+(cw-im.width)//2,y+(ch-36-im.height)//2),im)
 draw.text((x+10,y+ch-30),name,font=font,fill='white')
sheet.save(out/'preview.png')
(out/'manifest.json').write_text(json.dumps(dict(source=str(bundle),sha256=hashlib.sha256(bundle.read_bytes()).hexdigest(),assetFile='sharedassets4.assets',texturePathId=54,atlasPathId=116,skeletonPathId=112,regions=manifest),indent=2))
(out/'README.md').write_text('''# Extracted map artwork

Original artwork extracted from the locally installed Tape to Tape game.

- `preview.png`: overview of the main building/stadium regions, labelled by original asset name.
- `regions/`: all 141 atlas regions as transparent PNGs, with packing rotations reversed and original triangle meshes used to exclude neighbouring artwork.
- `source/`: original texture, Spine atlas, and binary skeleton for assembling animated nodes later.
- `manifest.json`: source hash, asset IDs, dimensions, packing rotations and trim offsets.

These are original image layers, not complete rendered Spine scenes. Flags, lights, flames, and other animated parts remain separate. No guessed composites or invented Random Event image were added. The atlas contains no region named NULL or Random Event; this does not by itself establish whether the game can select events randomly. Exact editor-type mapping, training variants and team-dependent stadium selection still need verification when wiring the images into the editor.

Re-extract with `tools/extract_node_images.py` using Python 3.12 and the workspace UnityPy libraries. It calls `tools/extract_node_meshes.mjs` using the bundled Node runtime (`--node` can override this). The extraction helper uses the official @esotericsoftware/spine-core 4.3.13 package under artifacts/spine-runtime; its original license is retained there. Mesh UVs and triangle indices come directly from the original skeleton, with no generated replacement artwork. See https://esotericsoftware.com/spine-texture-packer for polygon packing details. Game files, DLL and editor source are unchanged.
''')
print(f'Extracted {len(manifest)} transparent regions; {len(primary)} primary images on preview sheet. Output: {out}')
