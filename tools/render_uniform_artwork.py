"""Build editor artwork from the game's native skater meshes and atlas textures.
Read-only game extraction. Requires the workspace UnityPy and Spine dependencies.
"""
import json,sys,hashlib,math
from pathlib import Path
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'artifacts/unity-asset-tools'))
import UnityPy
import numpy as np
from PIL import Image,ImageDraw,ImageChops
rows=json.loads((root/'artifacts/equipment-catalog/uniform-poses.json').read_text())
folder=Path('C:/Steam/steamapps/common/Tape to Tape/Tape to Tape_Data/StreamingAssets/aa/StandaloneWindows64')
out=root/'assets/uniform-preview';out.mkdir(exist_ok=True)
textures={}
def texture(page):
 if page not in textures:
  prefix=('goaliesprites_assets_' if page.lower().startswith('goalie') else 'skatersprites_assets_')+Path(page).stem.lower()+'_'
  bundle=next((f for f in folder.glob(prefix+'*.bundle') if '_lso_' not in f.name),None)
  if bundle is None: raise FileNotFoundError(page)
  env=UnityPy.load(str(bundle))
  textures[page]=next(o.read().image.convert('RGBA') for o in env.objects if o.type.name=='Texture2D')
 return textures[page]
# Every part uses the same camera frame, preserving the native body proportions.
frames={}
for role in ['skater','goalie']:
 for view in ['S','N']:
  ref=[m for r in rows if r['view']==view and r.get('role','skater')==role and (r['skin'].endswith('Customization_colors') or r['skin'] in ['Faces/Custom/Helmet_Colors','goalie-default']) for m in r['meshes']]
  xs=[v for m in ref for v in m['vertices'][::2]];ys=[v for m in ref for v in m['vertices'][1::2]]
  lo,hi,bottom,top=min(xs),max(xs),min(ys),max(ys); scale=min(480/(hi-lo),610/(top-bottom))
  frames[role,view]=(lo,top,scale,(512-(hi-lo)*scale)/2,(640-(top-bottom)*scale)/2)
index=[]
for idx,row in enumerate(rows):
 name=hashlib.sha1((row['skin']+row['part']+row['view']).encode()).hexdigest()[:16]+'.png'
 if (out/name).exists():
  index.append(dict(skin=row['skin'],part=row['part'],view=row['view'],role=row.get('role','skater'),file=name));continue
 try:
  for mesh in row['meshes']:texture(mesh['page'])
 except FileNotFoundError as error:
  print('Unavailable atlas:',row['skin'],str(error),flush=True);continue
 canvas=Image.new('RGBA',(512,640))
 lo,top,scale,ox,oy=frames[row.get('role','skater'),row['view']]
 for mesh in row['meshes']:
  atlas=texture(mesh['page']); v=np.array(mesh['vertices']).reshape(-1,2);v[:,0]=(v[:,0]-lo)*scale+ox;v[:,1]=(top-v[:,1])*scale+oy
  uv=np.array(mesh['uvs']).reshape(-1,2)*[atlas.width,atlas.height]
  for tri in np.array(mesh['triangles']).reshape(-1,3):
   dest=v[tri];src=uv[tri]
   x0=max(0,math.floor(dest[:,0].min())); y0=max(0,math.floor(dest[:,1].min()));x1=min(512,math.ceil(dest[:,0].max())+1);y1=min(640,math.ceil(dest[:,1].max())+1)
   if x1<=x0 or y1<=y0:continue
   local=dest-[x0,y0];matrix=np.column_stack([local,np.ones(3)])
   if abs(np.linalg.det(matrix))<1e-7:continue
   coeff=np.linalg.solve(matrix,src).T.flatten()
   tile=atlas.transform((x1-x0,y1-y0),Image.Transform.AFFINE,coeff,Image.Resampling.BILINEAR)
   mask=Image.new('L',tile.size);ImageDraw.Draw(mask).polygon([tuple(p) for p in local],fill=255)
   tile.putalpha(ImageChops.multiply(tile.getchannel('A'),mask));canvas.alpha_composite(tile,(x0,y0))
 name=hashlib.sha1((row['skin']+row['part']+row['view']).encode()).hexdigest()[:16]+'.png';canvas.save(out/name)
 index.append(dict(skin=row['skin'],part=row['part'],view=row['view'],role=row.get('role','skater'),file=name))
 if idx%25==0:print('Rendered',idx+1,'/',len(rows),flush=True)
(out/'index.json').write_text(json.dumps(index,indent=2),encoding='utf-8')
print('Saved',len(index),'native part images',flush=True)
