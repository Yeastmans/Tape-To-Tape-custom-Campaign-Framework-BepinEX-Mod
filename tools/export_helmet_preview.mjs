// Development-only numeric map export. Spine and game textures are never shipped.
// node export_helmet_preview.mjs <extracted-source-folder> <spine-core/dist/index.js> <head-templates.json>
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
const [source,runtime,output]=process.argv.slice(2);
if(!source||!runtime||!output)throw new Error('Supply source folder, developer Spine module, and head map.');
const {TextureAtlas,AtlasAttachmentLoader,SkeletonBinary,Skeleton,Skin,AnimationState,AnimationStateData,Physics}=await import(pathToFileURL(path.resolve(runtime)));
const bytes=fs.readFileSync(path.join(source,'Ultimate_Sprite.skel'));
const atlas=new TextureAtlas(fs.readFileSync(path.join(source,'Player_Sprite.atlas'),'utf8'));
const data=new SkeletonBinary(new AtlasAttachmentLoader(atlas)).readSkeletonData(new Uint8Array(bytes));
const skin=data.findSkin('Faces/Custom/Helmet_Colors');
const artwork=data.findSkin('Faces/Custom/Helmet_Reference');
const directions=['S','N','W','E','NE','NW','SW','SE'];
const map=JSON.parse(fs.readFileSync(output,'utf8'));
const regions=[],views={};
function pose(head,direction){
 const combined=new Skin('head-preview');
 for(const name of ['Body/Customization/Customization_colors',head,skin.name])combined.addSkin(data.findSkin(name));
 const skeleton=new Skeleton(data);skeleton.setSkin(combined);skeleton.setupPose();
 const state=new AnimationState(new AnimationStateData(data));state.setAnimation(0,'Idle_Puck_N/Idle_'+direction,true);
 state.apply(skeleton);skeleton.updateWorldTransform(Physics.none);
 const meshes=[];
 for(const [order,slot] of skeleton.drawOrder.appliedPose.entries()){
  if(!/^Helmet/.test(slot.data.name))continue;
  const a=slot.appliedPose.attachment;if(!a?.sequence)continue;
  const index=slot.appliedPose.sequenceIndex<0?a.sequence.setupIndex:slot.appliedPose.sequenceIndex;
  const vertices=new Float32Array(a.worldVerticesLength||8);
  if(a.worldVerticesLength)a.computeWorldVertices(skeleton,slot,0,vertices.length,vertices,0,2);
  else a.computeWorldVertices(slot,a.sequence.offsets[index??0],vertices,0,2);
  const binding=combined.getAttachments().find(e=>e.attachment===a);
  meshes.push({slot:slot.data.name,placeholder:binding.placeholder,order,vertices:Array.from(vertices),
   localUVs:a.worldVerticesLength?Array.from(a.regionUVs):[0,1,0,0,1,0,1,1],triangles:Array.from(a.triangles||[0,1,2,2,3,0])});
 }
 return meshes;
}
for(const direction of directions){
 const entry=skin.getAttachments().find(e=>e.placeholder===direction+'-Helmet');
 const painted=artwork.getAttachments().find(e=>e.placeholder===entry.placeholder);
 const a=entry.attachment,b=painted.attachment;
 // The internal finished helmet is the native neutral shell artwork. The
 // selectable color-mask helmet supplies ALL placement, UVs and draw order:
 // notably its front mesh differs from the internal reference's front mesh.
 for(const key of ['width','height'])
  if(a[key]!==b[key])throw new Error('Reference artwork dimensions differ: '+direction+' '+key);
 const r=b.sequence.regions[0];
 if(r.width!==r.originalWidth||r.height!==r.originalHeight)throw new Error('Review trimmed helmet region: '+r.name);
 regions.push({name:r.name,page:r.page.name,x:r.x,y:r.y,width:r.width,height:r.height,
  originalWidth:r.originalWidth,originalHeight:r.originalHeight,offsetX:r.offsetX,offsetY:r.offsetY,degrees:r.degrees,editableFile:direction+'.png'});
 const meshes=pose('Faces/Toronto/Mathieu_Helmet',direction);
 const old=map.templates.find(t=>t.skin==='Faces/Toronto/Mathieu_Helmet').views[direction];
 for(const mesh of old){
  const live=meshes.find(m=>m.slot===mesh.slot);
  if(JSON.stringify(live.vertices)!==JSON.stringify(mesh.vertices))throw new Error('Head pose changed; review existing map: '+direction);
 }
 const helmet=meshes.filter(m=>m.slot==='Helmet_Custom');
 if(helmet.length!==1)throw new Error('Expected one equipped helmet in '+direction);
 helmet[0].file=direction+'.png';views[direction]=helmet;
}
map.equippedHelmet={version:1,skin:skin.name,artworkSkin:artwork.name,
 description:'Native standard helmet fit preview; neutral reference artwork, not team palette or match lighting.',
 skeletonSha256:createHash('sha256').update(bytes).digest('hex'),regions,views};
fs.writeFileSync(output,JSON.stringify(map));
console.log(JSON.stringify({directions:directions.length,regions:regions.length,equippedSkinGeometry:skin.name,headPosesUnchanged:true,output}));
