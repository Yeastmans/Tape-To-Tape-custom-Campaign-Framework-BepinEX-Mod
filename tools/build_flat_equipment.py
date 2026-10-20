"""Lay native game atlas pieces flat in the editor's separated equipment format.

Torso and sleeve scans are uncurved onto flat sewing panels; small equipment
retains its native outline. No generated patterns, lighting, or character poses.
Run with the bundled Python and the workspace UnityPy dependencies.
"""
import sys, json, hashlib, math, argparse, re
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'artifacts/unity-asset-tools'))
import UnityPy
import numpy as np
from PIL import Image, ImageDraw, ImageOps, ImageChops

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--regions', type=Path, default=ROOT/'assets/game-art-maps/flat-regions.json')
parser.add_argument('--output', type=Path, default=ROOT/'assets/flat-equipment')
parser.add_argument('--game', type=Path, default=Path('C:/Steam/steamapps/common/Tape to Tape'))
parser.add_argument('--no-sheet', action='store_true')
options = parser.parse_args()
source = json.loads(options.regions.read_text())
rows = source['rows']
out = options.output; out.mkdir(parents=True, exist_ok=True)
bundles = options.game/'Tape to Tape_Data/StreamingAssets/aa/StandaloneWindows64'
textures = {}
def crop(r):
    page = r['page']
    if page not in textures:
        prefix = ('goaliesprites_assets_' if page.lower().startswith('goalie') else 'skatersprites_assets_') + Path(page).stem.lower() + '_'
        matches = sorted(p for p in bundles.glob(prefix+'*.bundle') if re.fullmatch(r'[0-9a-f]{32}',p.stem[len(prefix):]))
        if not matches: raise FileNotFoundError(page)
        env = UnityPy.load(str(matches[0]))
        textures[page] = next(o.read().image.convert('RGBA') for o in env.objects if o.type.name == 'Texture2D')
    atlas = textures[page]; scale = atlas.width/r['pageWidth']
    x,y,w,h = [round(r[k]*scale) for k in ('x','y','width','height')]
    im = atlas.crop((x,y,x+(h if r['degrees']==90 else w),y+(w if r['degrees']==90 else h)))
    if r['degrees']==90: im=im.rotate(-90,expand=True)
    # Some game pages store the outer matte as opaque black. Remove only
    # border-connected matte pixels, preserving the drawn black seams.
    pixels=np.array(im); dark=(pixels[:,:,:3].max(axis=2)<8)|(pixels[:,:,3]<16)
    matte=Image.new('L',(im.width+2,im.height+2),255)
    matte.paste(Image.fromarray((dark*255).astype('uint8')),(1,1));ImageDraw.floodfill(matte,(0,0),128)
    pixels[:,:,3][np.array(matte)[1:-1,1:-1]==128]=0
    im=Image.fromarray(pixels)
    return im.crop(im.getbbox()) if im.getbbox() else im

def flat_panel(im, size):
    """Remove the curved side boundaries of a posed garment panel row by row."""
    im=im.resize((im.width,size[1]),Image.Resampling.BICUBIC)
    result=Image.new('RGBA',size)
    for y in range(size[1]):
        scan=np.array(im.crop((0,y,im.width,y+1)))[0]
        # Compact only fabric pixels. Transparent spaces between the posed
        # panel and its outline must not become long black stretching marks.
        fabric=scan[scan[:,3]>32]
        if len(fabric):
            fabric[:,3]=255
            row=Image.fromarray(fabric.reshape(1,len(fabric),4))
            result.paste(row.resize((size[0],1),Image.Resampling.BICUBIC),(0,y))
    return result

def contour(size, start, curves):
    """Antialiased curved sewing outline, sampled at four times output size."""
    points=[start]; current=start
    for c1,c2,end in curves:
        for step in range(1,33):
            t=step/32; u=1-t
            points.append(tuple(u*u*u*current[i]+3*u*u*t*c1[i]+3*u*t*t*c2[i]+t*t*t*end[i] for i in (0,1)))
        current=end
    mask=Image.new('L',(size[0]*4,size[1]*4))
    ImageDraw.Draw(mask).polygon([(round(x*4),round(y*4)) for x,y in points],fill=255)
    return mask.resize(size,Image.Resampling.LANCZOS)

def shape_panel(im, mask):
    """Fit the native fabric across curved sides instead of cutting off its pattern."""
    result=Image.new('RGBA',mask.size)
    for y in range(mask.height):
        bounds=mask.crop((0,y,mask.width,y+1)).getbbox()
        if bounds:
            row=im.crop((0,y,im.width,y+1)).resize((bounds[2]-bounds[0],1),Image.Resampling.BICUBIC)
            result.paste(row,(bounds[0],y))
    result.putalpha(ImageChops.multiply(result.getchannel('A'),mask))
    return result

def torso_shape():
    return contour((150,250),(7,19),[
        ((9,8),(34,3),(51,2)),((68,0),(84,0),(99,2)),
        ((117,3),(141,8),(143,19)),((151,40),(139,75),(140,112)),
        ((140,156),(145,207),(147,236)),((149,246),(136,248),(119,249)),
        ((87,250),(62,250),(31,249)),((13,248),(1,246),(3,236)),
        ((5,207),(10,156),(10,112)),((11,75),(-1,40),(7,19))])

def sleeve_shape(size):
    w,h=size
    return contour(size,(6,1),[
        ((w*.3,0),(w*.8,0),(w-4,7)),((w,h*.3),(w-5,h*.7),(w-8,h-6)),
        ((w-9,h),(w*.3,h),(10,h-2)),((6,h-2),(5,h-5),(5,h-9)),
        ((4,h*.6),(0,h*.2),(6,1))])

def shorts_shape():
    return contour((230,140),(27,3),[
        ((67,0),(163,0),(203,3)),((209,24),(218,62),(218,112)),
        ((219,126),(218,135),(207,137)),((184,140),(159,140),(139,137)),
        ((132,124),(125,108),(115,90)),((105,108),(98,124),(91,137)),
        ((71,140),(46,140),(23,137)),((12,135),(11,126),(12,112)),
        ((12,62),(21,24),(27,3))])

def fit(im, size):
    im=im.copy(); im.thumbnail(size,Image.Resampling.LANCZOS);return im
def remove_exposed_skin(im):
    pixels=np.array(im);rgb=pixels[:,:,:3].astype(float)
    flesh=(rgb[:,:,0]>rgb[:,:,1]*1.08)&(rgb[:,:,0]<rgb[:,:,1]*1.8)&(rgb[:,:,1]>rgb[:,:,2]*1.05)&(rgb[:,:,1]<rgb[:,:,2]*1.9)
    visible=pixels[:,:,3]>32
    skin_rows=np.count_nonzero(flesh&visible,axis=1)>np.maximum(2,np.count_nonzero(visible,axis=1)*.25)
    pixels[:,:,3][flesh]=0
    pixels[skin_rows,:,3]=0
    coloured=(pixels[:,:,3]>32)&(pixels[:,:,:3].max(axis=2)>40)
    ys=np.flatnonzero(coloured.any(axis=1))
    if not len(ys):return Image.new('RGBA',im.size)
    return Image.fromarray(pixels).crop((0,int(ys[0]),im.width,int(ys[-1])+1))
def upright(im):
    points=np.argwhere(np.array(im.getchannel('A'))>80)
    if len(points)<10:return im
    _,axes=np.linalg.eigh(np.cov(points.T)); axis=axes[:,-1]
    angle=math.degrees(math.atan2(axis[0],axis[1]))
    while angle<0:angle+=180
    im=im.rotate(angle-90,expand=True,resample=Image.Resampling.BICUBIC)
    return im.crop(im.getbbox())
def put(canvas,im,box,mirror=False):
    im=fit(im,(box[2],box[3]))
    if mirror:im=ImageOps.mirror(im)
    canvas.alpha_composite(im,(round(box[0]+(box[2]-im.width)/2),round(box[1]+(box[3]-im.height)/2)))

groups={}
for r in rows:
    prefix=r['skin'].split('/')[0]
    part={'Body':'jersey','Body_Bicep':'sleeves','Body_Gloves':'gloves','Body_Glove':'gloves','Body_Pants':'pants','Body_Skates':'skates','Body_Pads':'socks','Body_Blocker':'blocker','Sticks':'stick','Body_Stick':'stick','Helmet':'helmet'}.get(prefix)
    if prefix=='Faces' and r['skin'].split('/')[-1].startswith('Helmet_'):part='helmet'
    if not part:continue
    key=(r['role'],r['skin'],r['view'],part)
    groups.setdefault(key,{})
    # Prefer the named base region over alternate animation frames.
    old=groups[key].get(r['slot'])
    if old is None or len(r['region'])<len(old['region']):groups[key][r['slot']]=r

index=[];missing=[]
for (role,skin,view,part),slots in groups.items():
    def get(*names):
        for name in names:
            if name in slots:return crop(slots[name])
        return None
    try:
        variants=[part]+(['socks'] if part=='jersey' and role=='skater' else ['pants'] if part=='jersey' else [])
        for piece in variants:
            size={'jersey':(320,320),'sleeves':(320,320),'helmet':(150,125),'gloves':(320,115),'blocker':(100,130),'pants':(230,140),'socks':(230,190 if role=='goalie' else 110),'skates':(230,110),'stick':(110,630)}[piece]
            canvas=Image.new('RGBA',size)
            if piece in ('jersey','sleeves'):
                if piece=='jersey':
                    body=get('Body')
                    if body is None:continue
                    # The atlas reserves a deep head opening for the posed
                    # character. Flatten that foreshortened collar/shoulder area.
                    split=round(body.height*.57)
                    upper=flat_panel(body.crop((0,0,body.width,split)),(150,48))
                    lower=flat_panel(body.crop((0,split,body.width,body.height)),(150,202))
                    panel=Image.new('RGBA',(150,250));panel.alpha_composite(upper);panel.alpha_composite(lower,(0,48))
                    if 'colors' in skin.lower():
                        # The hem stripe is curved in the atlas to follow the
                        # character's belly. Its centre scan preserves the exact
                        # native band widths and order on a straight flat hem.
                        band=panel.crop((74,130,76,250)).resize((150,120),Image.Resampling.NEAREST)
                        panel.paste(band,(0,130))
                    panel=shape_panel(panel,torso_shape())
                    # Supersampled collar avoids a stair-stepped cutout.
                    mask=Image.new('L',(600,1000),255);draw=ImageDraw.Draw(mask)
                    draw.ellipse((204,-76,396,88 if view=='S' else 40),fill=0)
                    mask=mask.resize(panel.size,Image.Resampling.LANCZOS)
                    panel.putalpha(ImageChops.multiply(panel.getchannel('A'),mask));canvas.alpha_composite(panel,(85,52))
                for left in (True,False):
                    side='left' if left else 'right'
                    arm=get('Bicep '+side,'Bicep_'+side) if piece=='sleeves' else get('Forearm '+side,'Arm_'+side,'Bicep_'+side,'Forearm_'+side)
                    if arm is None:continue
                    if 'colors' not in skin.lower():arm=remove_exposed_skin(arm)
                    if not arm.getbbox():continue
                    if piece=='jersey' and role=='skater' and 'colors' not in skin.lower():
                        rgb=np.array(arm).astype(float);visible=rgb[:,:,3]>80
                        flesh=(rgb[:,:,0]>rgb[:,:,1]*1.08)&(rgb[:,:,0]<rgb[:,:,1]*1.8)&(rgb[:,:,1]>rgb[:,:,2]*1.05)&(rgb[:,:,1]<rgb[:,:,2]*1.9)
                        if np.count_nonzero(flesh&visible)>np.count_nonzero(visible)*.55:continue
                    sleeve=flat_panel(arm,(57,141 if role=='goalie' else 95))
                    sleeve=shape_panel(sleeve,sleeve_shape(sleeve.size))
                    sleeve=sleeve.rotate(-10 if left else 10,expand=True,resample=Image.Resampling.BICUBIC)
                    upper=role=='goalie' or piece=='sleeves'
                    canvas.alpha_composite(sleeve,((27 if left else 218) if upper else (10 if left else 235),65 if upper else 155))
                # Skater upper sleeves come from the independent Bicep skin layer.
            elif piece in ('pants','socks','skates'):
                names={'pants':('Pants left','Pants_left','Pants right','Pants_right'), 'socks':('Leg left','Pad_left','Leg right','Pad_right'), 'skates':('Skate left','Skate_left','Skate right','Skate_right')}[piece]
                for i,pair in enumerate((names[:2],names[2:])):
                    item=get(*pair)
                    if item is not None:
                        if piece=='pants' and role=='skater':item=flat_panel(upright(item),(108,size[1]))
                        elif piece=='socks':item=upright(remove_exposed_skin(item) if role=='skater' and 'colors' not in skin.lower() else item)
                        put(canvas,item,(i*122,0,108,size[1]))
                if piece=='pants' and role=='skater':
                    # A continuous waistband joins the two native leg panels;
                    # the curved crotch and leg openings define the shorts.
                    canvas.alpha_composite(canvas.crop((106,0,108,95)).resize((14,95),Image.Resampling.BICUBIC),(108,0))
                    canvas=shape_panel(canvas,shorts_shape())
            elif piece=='gloves':
                if role=='goalie':
                    item=get('Glove')
                    if item is not None:put(canvas,item,(220,0,100,115))
                else:
                    for i,slot in enumerate(('Glove left','Glove right')):
                        item=get(slot)
                        if item is not None:put(canvas,upright(item),(i*220,0,100,115))
            else:
                for name in {'helmet':['Helmet_Custom','Helmet','Helmet_Top'],'blocker':['Blocker'],'stick':['Stick']}[piece]:
                    item=get(name)
                    if item is not None:put(canvas,upright(item) if piece=='stick' else item,(0,0,*size))
            if not canvas.getbbox():continue
            name=hashlib.sha1(f'{role}|{skin}|{view}|{piece}'.encode()).hexdigest()[:20]+'.png'
            canvas.save(out/name)
            index.append(dict(role=role,skin=skin,view=view,part=piece,file=name,custom='colors' in skin.lower(),regions=[r['region'] for r in slots.values()]))
    except FileNotFoundError as error:missing.append(dict(role=role,skin=skin,view=view,error=str(error)))

(out/'index.json').write_text(json.dumps(index,indent=2))
(out/'provenance.json').write_text(json.dumps(dict(sourceSha256=source['sourceSha256'],method='Native atlas panels flattened and laid out; source colours retained',missing=missing),indent=2))
print('Flat artwork:',len(index),'missing atlas groups:',len(missing),flush=True)
if options.no_sheet: sys.exit(0)
# Review contact sheet, recolouring native mask channels only for readability.
samples=[r for r in index if r['part']=='jersey' and r['view']=='S']
sheet=Image.new('RGB',(1280,math.ceil(len(samples)/4)*350),'#1b2a37');d=ImageDraw.Draw(sheet)
for i,row in enumerate(samples):
    im=Image.open(out/row['file']);x=(i%4)*320;y=(i//4)*350
    sheet.paste(im,(x,y+25),im);d.text((x+8,y+5),row['role']+' '+row['skin'].split('/')[-1],fill='white')
sheet.save(ROOT/'artifacts/equipment-catalog-current/flat-jerseys.png')
