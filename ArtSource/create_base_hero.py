"""MakeHuman CC0 base + original underclothing, retargeted to existing HeroRig.
Run Blender --background --disable-autoexec --python-exit-code 1 --python create_base_hero.py.
Inputs are downloaded public MakeHuman base.obj and the project's Cavaleiro.fbx.
No changes are made to the licensed knight mesh or its animation clips.
"""
import bpy, math, json, sys, os
import bmesh
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
HERE=Path(__file__).resolve().parent
STAGE=Path(os.environ.get('PROTOTYPE_PROJECT_ROOT', str(HERE.parent)))
ART=STAGE/'Unity/Assets/Art/Equipment'
EXPORT_ART=Path(os.environ.get('GN_BASE_HERO_EXPORT_DIR',str(ART)))
EXPORT_ART.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.import_scene.fbx(filepath=str(STAGE/'Unity/Assets/Art/Cavaleiro.fbx'),use_anim=False)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
rig.animation_data_clear()
for b in rig.pose.bones:b.matrix_basis.identity()
# Preserve a rest-space surface reference for close-fitting accessories. Rays
# are restricted to the torso, so the shield cannot enlarge the belt contour.
armour=next(o for o in bpy.data.objects if o.type=='MESH' and o.name=='KnightArmor')
to_rig=rig.matrix_world.inverted()@armour.matrix_world
armour_vertices=[to_rig@v.co for v in armour.data.vertices]
torso_surface=BVHTree.FromPolygons(armour_vertices,[list(p.vertices)for p in armour.data.polygons])
for o in list(bpy.data.objects):
 if o!=rig:bpy.data.objects.remove(o,do_unlink=True)
# Source OBJ groups retain genuine anatomical topology and UVs.
vertices=[];uvs=[];groups={};group='none'
for line in (HERE/'MakeHuman-base.obj').open():
 p=line.split()
 if not p:continue
 if p[0]=='v':vertices.append(Vector(tuple(map(float,p[1:4]))))
 elif p[0]=='vt':uvs.append(tuple(map(float,p[1:3])))
 elif p[0]=='g':group=' '.join(p[1:])
 elif p[0]=='f':groups.setdefault(group,[]).append([(int(x.split('/')[0])-1,int(x.split('/')[1])-1) for x in p[1:]])
def joint(name):
 ids={i for f in groups['joint-'+name] for i,_ in f}
 return sum((vertices[i] for i in ids),Vector())/len(ids)
def conv(v):return Vector((v.x,-v.z,v.y))
# Segment maps use the existing knight rest pose; the resulting bind poses are exact.
segments={}
def add(name,start,end,radius=.108):
 a,b=conv(start),conv(end);t=rig.data.bones[name]
 rot=(b-a).normalized().rotation_difference((t.tail_local-t.head_local).normalized())
 segments[name]=(a,b,t.head_local.copy(),t.tail_local.copy(),rot,radius)
add('Pelvis',joint('pelvis'),joint('spine-3'),.115)
add('Spine',joint('spine-3'),joint('spine-1'),.126)
add('Chest',joint('spine-1'),joint('neck'),.139)
# Anatomical head is .29 m high; does not inherit the broad armoured helmet's shape.
add('Head',joint('neck'),joint('head-2'),.105)
for sign,suffix,source in [(-1,'R','r'),(1,'L','l')]:
 add('UpperArm.'+suffix,joint(source+'-shoulder'),joint(source+'-elbow'),.100)
 add('Forearm.'+suffix,joint(source+'-elbow'),joint(source+'-hand'),.102)
 # MH index finger knuckle-to-tip direction provides natural fingers, bound rigidly to hand.
 hand=joint(source+'-hand');end=joint(source+'-finger-3-4')
 add('Hand.'+suffix,hand,end,.102)
 add('Thigh.'+suffix,joint(source+'-upper-leg'),joint(source+'-knee'),.103)
 add('Shin.'+suffix,joint(source+'-knee'),joint(source+'-ankle'),.101)
 add('Foot.'+suffix,joint(source+'-ankle'),joint(source+'-foot-2'),.102)
def distance(p,name):
 a,b,*_=segments[name];d=b-a;t=max(0,min(1,(p-a).dot(d)/d.length_squared))
 return (p-a-d*t).length,t

def warp(source):
 p=conv(source);x,y,z=source;side='R' if x<0 else 'L'
 # Smooth blend between anatomical chains. All source joints contribute near a
 # chain transition, so a tunic triangle never crosses an abrupt coordinate map.
 torso=['Pelvis','Spine','Chest','Head']
 arm=['UpperArm.'+side,'Forearm.'+side,'Hand.'+side]
 leg=['Thigh.'+side,'Shin.'+side,'Foot.'+side]
 def chain(names):
  ranks=sorted((distance(p,n)[0],n) for n in names)
  ws={n:1/max(.16,d)**4 for d,n in ranks[:2]};total=sum(ws.values());return {n:w/total for n,w in ws.items()}
 def smooth(a,b,v):
  t=max(0,min(1,(v-a)/(b-a)));return t*t*(3-2*t)
 armw=smooth(1.02,1.92,abs(x))*smooth(.4,1.8,y)
 legw=(1-smooth(-.05,1.1,y))*(1-armw)
 torsoWeight=1-armw-legw
 weights={}
 for names,gain in [(torso,torsoWeight),(arm,armw),(leg,legw)]:
  if gain<.00001:continue
  for n,w in chain(names).items():weights[n]=weights.get(n,0)+w*gain
 # FBX/Unity use at most four strongest weights.
 weights=dict(sorted(weights.items(),key=lambda pair:pair[1],reverse=True)[:4]);total=sum(weights.values());weights={n:w/total for n,w in weights.items()}
 if y>5.67 and abs(x)<1.5:weights={'Head':1}
 if abs(x)>4.02 and y<3.35:weights={'Hand.'+side:1}
 pos=Vector()
 for n,w in weights.items():
  a,b,c,d,q,r=segments[n];axis=(b-a).normalized();along=(p-a).dot(axis)
  targetAxis=(d-c).normalized();lengthScale=(d-c).length/(b-a).length
  radial=(p-a)-axis*along
  mapped=c+targetAxis*(along*lengthScale)+q@radial*r
  pos+=mapped*w
 return pos,weights

warped=[warp(v) for v in vertices]
# Materials are original, nonmetallic skin, cotton, leather; no armour atlas repainted as skin.
def material(name,color,rough=.8):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=rough
 return m
skin=material('BaseSkin',(.40,.235,.15),.66)
cloth=material('BaseLinen',(.16,.145,.118),.92)
pants=material('BaseTrousers',(.065,.075,.08),.9)
boots=material('BaseLeather',(.055,.031,.019),.77)
hair=material('BaseHair',(.029,.018,.012),.86)
eyewhite=material('BaseEye',(.48,.47,.42),.4)
iris=material('BaseIris',(.038,.055,.035),.4)
# Use the real closed tights proxy as clothing. It deliberately omits the genital helper.
# Small local tailoring removes cling around the hips and creates a straight tunic hem.
def tailor(p,source):
 q=p.copy();sx,sy,sz=source
 # A continuous padded cotton shirt, slightly offset from the clothing proxy.
 # Avoid hard coordinate clamps at waist/chest which create artificial creases.
 if .84<q.z<1.32 and abs(q.x)<.235:
  t=max(0,1-((q.z-1.08)/.26)**2)
  q.y+=(-.006 if q.y<0 else .006)*t
 # Tailor a straight, masculine cloth silhouette rather than the proxy's bust.
 if 1.05<q.z<1.46 and abs(q.x)<.255:
  blend=max(0,1-(abs(q.x)/.255)**6)
  q.y*=1-.32*blend
  q.x*=.94
 return q

def build(name,faces,matlist,assign,tailoring=False):
 used=sorted({i for f in faces for i,_ in f});remap={idx:n for n,idx in enumerate(used)}
 coords=[tailor(warped[i][0],vertices[i]) if tailoring else warped[i][0] for i in used]
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(coords,[],[[remap[i] for i,_ in f] for f in faces]);mesh.update()
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
 for m in matlist:mesh.materials.append(m)
 uv=mesh.uv_layers.new(name='UVMap')
 for poly,face in zip(mesh.polygons,faces):
  poly.use_smooth=True;center=sum((coords[remap[i]] for i,_ in face),Vector())/len(face)
  poly.material_index=assign(center,face)
  for loop,(_,ti) in zip(poly.loop_indices,face):uv.data[loop].uv=uvs[ti]
 for idx in used:
  for n,w in warped[idx][1].items():
   group=obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n);group.add([remap[idx]],w,'REPLACE')
 mod=obj.modifiers.new('HeroRig','ARMATURE');mod.object=rig;obj.parent=rig
 return obj
# Real head incl neck, ears, lips and fingers from body topology.
def headFace(f):return min(vertices[i].y for i,_ in f)>5.67 and max(abs(vertices[i].x) for i,_ in f)<1.5
headfaces=[f for f in groups['body'] if headFace(f)]
handfaces=[f for f in groups['body'] if min(abs(vertices[i].x) for i,_ in f)>4.02 and max(vertices[i].y for i,_ in f)<3.35]
# Base body is one renderer and head another so helmet never clips through the face/hair.
body=build('BaseHeroBody',groups['helper-tights']+handfaces,[cloth,pants,boots,skin],lambda p,f:3 if min(abs(vertices[i].x) for i,_ in f)>4.02 else 2 if p.z<.21 else 1 if p.z<.91 else 0,True)
head=build('BaseHeroHead',headfaces,[skin,hair],lambda p,f:0)
# Real eyeballs are small spheres under existing anatomical eyelids; dark irises face forward.
head_additions=[]
def sphere(name,pos,scale,mat):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,location=pos)
 o=bpy.context.object;o.name=name;o.scale=scale
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 # All shapes use world-coordinate vertices before joining to head and binding.
 bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
 o.data.materials.append(mat)
 for p in o.data.polygons:p.use_smooth=True
 group=o.vertex_groups.new(name='Head');group.add(list(range(len(o.data.vertices))),1,'REPLACE')
 head_additions.append(o);return o
for side in ['l','r']:
 center,_=warp(joint(side+'-eye'));sphere('Eye'+side,center,(.0115,.010,.0105),eyewhite)
 sphere('Iris'+side,center+Vector((0,-.009,0)),(.0047,.0022,.005),iris)
# Clip the existing anatomical scalp through a continuous hairline. Selecting
# whole head quads created the previous serrated black fringe on the forehead.
def hairline(point):
 front=max(0,min(1,(-point.y-.025)/.115))
 temple=min(1,(abs(point.x)/.09)**3)
 return 1.649+.057*front**.85-.010*temple*front
hair_coords=[];hair_faces=[];hair_uv=[]
for face in headfaces:
 polygon=[(warped[i][0].copy(),Vector(uvs[ti]))for i,ti in face]
 clipped=[]
 for previous,current in zip(polygon[-1:]+polygon[:-1],polygon):
  a,auv=previous;b,buv=current
  ga,gb=a.z-hairline(a),b.z-hairline(b)
  if (ga>=0)!=(gb>=0):
   t=ga/(ga-gb);clipped.append((a.lerp(b,t),auv.lerp(buv,t)))
  if gb>=0:clipped.append(current)
 if len(clipped)<3:continue
 ids=[]
 for point,uv in clipped:
  direction=(point-Vector((0,-.045,1.69))).normalized()
  # Submillimetre variation keeps the crop close to the actual skull.
  offset=.0009+.00015*math.sin(point.x*120)*math.sin(point.y*145+point.z*90)
  ids.append(len(hair_coords));hair_coords.append(tuple(point+direction*offset));hair_uv.append(tuple(uv))
 hair_faces.append(ids)
mesh=bpy.data.meshes.new('ShortHair');mesh.from_pydata(hair_coords,[],hair_faces);mesh.materials.append(hair);mesh.update()
uv=mesh.uv_layers.new(name='UVMap')
for polygon in mesh.polygons:
 polygon.use_smooth=True
 for loop in polygon.loop_indices:uv.data[loop].uv=hair_uv[mesh.loops[loop].vertex_index]
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bm.to_mesh(mesh);bm.free();mesh.update()
cap=bpy.data.objects.new('ShortHair',mesh);bpy.context.collection.objects.link(cap)
g=cap.vertex_groups.new(name='Head');g.add(list(range(len(mesh.vertices))),1,'REPLACE')
# Join cap+eyes into same renderer and armature binding.
bpy.ops.object.select_all(action='DESELECT')
head.select_set(True);cap.select_set(True)
for o in head_additions:o.select_set(True)
bpy.context.view_layer.objects.active=head;bpy.ops.object.join()
# Smooth cloth only at shoulders/elbows; retain the source's face topology and fingers.
for o in (body,head):
 bpy.context.view_layer.objects.active=o
 o.data.update()
# A gentle topology-preserving cloth relaxation removes proxy spikes at seams.
bpy.context.view_layer.objects.active=body
relax=body.modifiers.new('ClothRelaxation','SMOOTH');relax.factor=.75;relax.iterations=8
# Smooth before Armature; exported rig remains identical.
bpy.ops.object.modifier_move_up(modifier=relax.name)
bpy.ops.object.modifier_apply(modifier=relax.name)
# Trim the anatomical neck through one continuous plane, rather than keeping
# the stepped boundary produced by whole-face selection in the source OBJ.
# BMesh bisect interpolates the existing UVs and skin weights on cut edges.
neck_height=1.505
bm=bmesh.new();bm.from_mesh(head.data)
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                      plane_co=(0,0,neck_height),plane_no=(0,0,1),
                      dist=.000001,clear_inner=True,clear_outer=False)
bm.to_mesh(head.data);bm.free();head.data.update()
def boundary_indices(mesh,predicate):
 counts={}
 for polygon in mesh.polygons:
  for edge in polygon.edge_keys:counts[tuple(sorted(edge))]=counts.get(tuple(sorted(edge)),0)+1
 return sorted({index for edge,count in counts.items() if count==1 for index in edge if predicate(mesh.vertices[index].co)})
neck_ids=boundary_indices(head.data,lambda p:abs(p.z-neck_height)<.00001)
cloth_ids=boundary_indices(body.data,lambda p:p.z>1.48 and abs(p.x)<.10)
assert len(neck_ids)>16 and len(cloth_ids)>16, ('Neck seam loops',len(neck_ids),len(cloth_ids))
neck_points=[head.data.vertices[i].co.copy()for i in neck_ids]
center=Vector((0,(min(p.y for p in neck_points)+max(p.y for p in neck_points))*.5,neck_height))
profile=sorted((math.atan2(p.y-center.y,p.x),Vector((p.x,p.y-center.y)).length)for p in neck_points)
profile=[(profile[-1][0]-math.tau,profile[-1][1])]+profile+[(profile[0][0]+math.tau,profile[0][1])]
def neck_radius(angle):
 for (a,ra),(b,rb) in zip(profile,profile[1:]):
  if a<=angle<=b:
   t=(angle-a)/max(.000001,b-a);return ra*(1-t)+rb*t
 raise AssertionError(('Unmapped neck angle',angle))
for index in cloth_ids:
 point=body.data.vertices[index].co;angle=math.atan2(point.y-center.y,point.x)
 radius=neck_radius(angle)*.999
 point.x=radius*math.cos(angle);point.y=center.y+radius*math.sin(angle);point.z=neck_height
# Both seam edges follow Chest; blend back to Head over the actual neck only.
# This closes the seam during breathing and small idle head turns.
for obj in (head,body):
 for vertex in obj.data.vertices:
  if obj==head and vertex.co.z<1.535:
   t=max(0,min(1,(vertex.co.z-neck_height)/.030));weights={'Chest':1-t,'Head':t}
  elif obj==body and vertex.index in cloth_ids:weights={'Chest':1}
  else:continue
  old_groups=[group.group for group in vertex.groups]
  for group_index in old_groups:obj.vertex_groups[group_index].remove([vertex.index])
  for name,weight in weights.items():
   group=obj.vertex_groups.get(name)or obj.vertex_groups.new(name=name);group.add([vertex.index],weight,'REPLACE')
 obj.data.update()
print('[NECK SEAM PASS]',len(neck_ids),len(cloth_ids),neck_height)
# Original equipment accessories: visible leather belt, metal pendant and two rings.
# They are bound to existing torso/hand bones, so swapping never creates objects.
accessories=[]
def accessory(name,coords,faces,mat,bone):
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(coords,[],faces);mesh.materials.append(mat);mesh.update()
 bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();mesh.update()
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
 for p in mesh.polygons:p.use_smooth=True
 g=o.vertex_groups.new(name=bone);g.add(list(range(len(coords))),1,'REPLACE')
 mod=o.modifiers.new('HeroRig','ARMATURE');mod.object=rig;o.parent=rig;accessories.append(o);return o
metal=material('BaseAccessoryMetal',(.23,.19,.12),.36)
metal.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value=.8
def torso_radius(angle,z):
 direction=Vector((math.cos(angle),math.sin(angle),0))
 point,normal,index,distance=torso_surface.ray_cast(Vector((0,0,z)),direction,.32)
 return distance if point is not None else 1/math.sqrt((math.cos(angle)/.21)**2+(math.sin(angle)/.145)**2)

def tube(coords,faces,path,radius,sides=8,closed=True):
 start=len(coords);count=len(path)
 for i,point in enumerate(path):
  before=path[(i-1)%count] if closed or i>0 else point
  after=path[(i+1)%count] if closed or i<count-1 else point
  tangent=(after-before).normalized();reference=Vector((0,0,1))
  if abs(tangent.dot(reference))>.9:reference=Vector((1,0,0))
  side=tangent.cross(reference).normalized();up=tangent.cross(side).normalized()
  for j in range(sides):
   a=math.tau*j/sides;coords.append(tuple(point+radius*(math.cos(a)*side+math.sin(a)*up)))
 for i in range(count if closed else count-1):
  for j in range(sides):
   a=start+i*sides+j;b=start+i*sides+(j+1)%sides
   c=start+((i+1)%count)*sides+(j+1)%sides;d=start+((i+1)%count)*sides+j
   faces.append((a,b,c,d))

# Thin leather strap, following the actual waist rather than an oversized
# ellipse. Section order follows the perimeter; the former order crossed itself.
coords=[];faces=[];count=96;belt_center=1.013
for ringz,offset in [(belt_center-.014,.005),(belt_center+.014,.005),(belt_center+.014,.001),(belt_center-.014,.001)]:
 radii=[torso_radius(math.tau*i/count,ringz)for i in range(count)]
 radii=[sum(radii[(i+k)%count]for k in range(-2,3))/5 for i in range(count)]
 for i,radius in enumerate(radii):
  t=i*math.tau/count;coords.append(((radius+offset)*math.cos(t),(radius+offset)*math.sin(t),ringz))
for k in range(4):
 for i in range(count):faces.append((k*count+i,k*count+(i+1)%count,((k+1)%4)*count+(i+1)%count,((k+1)%4)*count+i))
leather_faces=len(faces)
# A discrete rounded bronze buckle; it stays within the strap's height.
front=-(torso_radius(-math.pi/2,belt_center)+.009)
path=[]
for x,z,offset in [(.013,.007,0),(-.013,.007,math.pi/2),(-.013,-.007,math.pi),(.013,-.007,math.pi*1.5)]:
 for step in range(7):
  angle=offset+step/6*math.pi/2
  path.append(Vector((x+.003*math.cos(angle),front,belt_center+z+.003*math.sin(angle))))
tube(coords,faces,path,.0015,8)
belt=accessory('BaseHeroBelt',coords,faces,boots,'Pelvis');belt.data.materials.append(metal)
for p in belt.data.polygons:
 if p.index>=leather_faces:p.material_index=1
# Fine chain hugs the collar. Its pendant is a small rounded coin instead of
# the previous open pyramid, which looked like spikes over the armour emblem.
coords=[];faces=[];count=96;path=[]
for i in range(count):
 t=i*math.tau/count;z=1.505-.050*max(0,-math.sin(t))
 radius=torso_radius(t,z)+.0035
 path.append(Vector((radius*math.cos(t),radius*math.sin(t),z)))
# Smooth the small plate seams so the chain cannot acquire sharp radial corners.
path=[sum((path[(i+k)%count]for k in range(-2,3)),Vector())/5 for i in range(count)]
tube(coords,faces,path,.0017,8)
front_point=path[3*count//4]
coin_center=front_point+Vector((0,-.002,-.010))
base=len(coords);sections=24
for depth,radius in [(-.0015,.005),(-.001,.006),(.001,.006),(.0015,.005)]:
 for i in range(sections):
  angle=math.tau*i/sections;coords.append(tuple(coin_center+Vector((radius*math.cos(angle),depth,radius*math.sin(angle)))))
for ring in range(3):
 for i in range(sections):faces.append((base+ring*sections+i,base+ring*sections+(i+1)%sections,base+(ring+1)*sections+(i+1)%sections,base+(ring+1)*sections+i))
faces.extend([tuple(base+i for i in reversed(range(sections))),tuple(base+3*sections+i for i in range(sections))])
accessory('BaseHeroNecklace',coords,faces,metal,'Chest')
for side,suffix in [('Left','L'),('Right','R')]:
 center=rig.data.bones['Hand.'+suffix].head_local.copy()+Vector((-.016 if suffix=='R' else .016,-.016,-.085))
 coords=[];faces=[];count=24;sections=8
 for i in range(count):
  t=i*2*math.pi/count
  for j in range(sections):
   a=j*2*math.pi/sections;r=.012+.003*math.cos(a);coords.append(tuple(center+Vector((r*math.cos(t),r*math.sin(t),.003*math.sin(a)))))
 for i in range(count):
  for j in range(sections):faces.append((i*sections+j,i*sections+(j+1)%sections,((i+1)%count)*sections+(j+1)%sections,((i+1)%count)*sections+j))
 accessory('BaseHeroRing'+side,coords,faces,metal,'Hand.'+suffix)
# Export matching FBX axes. Skeleton remains same17bones and target rest matrices.
bpy.ops.object.select_all(action='DESELECT')
for o in (rig,body,head,*accessories):o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(EXPORT_ART/'BaseHero.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},apply_unit_scale=True,global_scale=1,axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,path_mode='AUTO',use_mesh_modifiers=True)
# Authoring source is self-contained and can be reopened without plugins.
bpy.ops.wm.save_as_mainfile(filepath=str(STAGE/'ArtSource/BaseHero.blend'))
for o in accessories:o.hide_render=True
# Capturable diagnostics + neutral studio view, no player scene or gameplay changes.
manifest={o.name:{'vertices':len(o.data.vertices),'polygons':len(o.data.polygons),'materials':[m.name for m in o.data.materials]} for o in (body,head)}
manifest['bones']=[b.name for b in rig.data.bones];manifest['source']='MakeHuman base.obj core asset CC0';manifest['excluded_groups']=['helper-genital','all nude torso/pelvis body polygons']
(HERE/'BaseHeroGeometry.json').write_text(json.dumps(manifest,indent=2))
world=bpy.data.worlds.new('Studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.10,.125,.15,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;bpy.context.scene.world=world
for name,pos,power in [('Key',(2,-3,4),400),('Fill',(-3,-2,2),220),('Rim',(1,2,3),450)]:
 l=bpy.data.lights.new(name,'AREA');l.energy=power;l.shape='DISK';l.size=3;o=bpy.data.objects.new(name,l);bpy.context.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
c=bpy.data.cameras.new('Camera');o=bpy.data.objects.new('Camera',c);bpy.context.collection.objects.link(o);o.location=(2,-5,2.5);o.rotation_euler=(Vector((0,0,.96))-o.location).to_track_quat('-Z','Y').to_euler();c.type='ORTHO';c.ortho_scale=2.1
s=bpy.context.scene;s.camera=o;s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.device='CPU';s.render.threads_mode='FIXED';s.render.threads=6;s.render.resolution_x=900;s.render.resolution_y=1100;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.filepath=str(HERE/'BaseHeroPreview.png');s.view_settings.view_transform='AgX';bpy.ops.render.render(write_still=True)
print('[BASE BODY PASS]',json.dumps(manifest))
