"""Adapta Knight, de piacenti (CC BY 3.0), ao rig e aos clipes do protótipo.

Blender --background --disable-autoexec --python-exit-code 1 --python integrar_cavaleiro.py
Requer ThirdParty/KnightOriginal.blend e Personagem.blend; não altera esses fontes.
"""
from pathlib import Path
import math
import json
import sys
import os
import bpy
import numpy as np
from mathutils import Vector, Matrix

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from refinar_animacoes_cavaleiro import carry_guard, reset, refine_locomotion, mirror_anatomy, periodic_tangents, validate_rig
from retarget_mixamo_cavaleiro import retarget_mixamo
ART = HERE.parent / 'Unity/Assets/Art'
EXPORT_ART = Path(os.environ.get('GN_CHARACTER_EXPORT_DIR', str(ART)))
EXPORT_ART.mkdir(parents=True, exist_ok=True)
TEXTURES = ART / 'ThirdParty/Knight'
TEXTURES.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(HERE / 'ThirdParty/KnightOriginal.blend'), use_scripts=False)
bpy.context.preferences.filepaths.save_version = 0
unrelated_image = bpy.data.images.get('Penguins.jpg')
if unrelated_image is not None: bpy.data.images.remove(unrelated_image)
body = bpy.data.objects['Group61487']
sword = bpy.data.objects['Group61487.001']
for obj in list(bpy.data.objects):
    if obj not in (body, sword): bpy.data.objects.remove(obj, do_unlink=True)

# Extract the author's real UV textures, rather than projecting repeating noise.
for suffix, output in [('color','Albedo'), ('nmap','Normal')]:
    im = bpy.data.images['armor_default_' + suffix + '.png']
    destination = TEXTURES / ('Knight_' + output + '.png')
    image_bytes = bytes(im.packed_file.data)
    if EXPORT_ART == ART and (not destination.exists() or destination.read_bytes() != image_bytes):
        destination.write_bytes(image_bytes)
metal = bpy.data.images['armor_default_metalness.png']
rough = bpy.data.images['armor_default_rough.png']
metal.colorspace_settings.name = rough.colorspace_settings.name = 'Non-Color'
width, height = metal.size
def pixels(image):
    result = np.empty(len(image.pixels), dtype=np.float32)
    image.pixels.foreach_get(result)
    return result.reshape(height,width,4)
m, r = pixels(metal), pixels(rough)
out = np.zeros((height,width,4), dtype=np.float32)
out[:,:,0] = m[:,:,0]
out[:,:,3] = 1.0 - r[:,:,0]
packed = bpy.data.images.new('Knight_MetallicSmoothness', width, height, alpha=True)
packed.colorspace_settings.name = 'Non-Color'
packed.pixels.foreach_set(out.ravel())
packed.filepath_raw = str(TEXTURES / 'Knight_MetallicSmoothness.png')
packed.file_format = 'PNG'
existing = bpy.data.images.load(packed.filepath_raw, check_existing=False) if Path(packed.filepath_raw).exists() else None
unchanged = existing is not None and tuple(existing.size) == (width,height) and np.array_equal(
    np.rint(pixels(existing)*255), np.rint(out*255))
if not unchanged and EXPORT_ART == ART: packed.save()
if existing is not None: bpy.data.images.remove(existing)

material = bpy.data.materials.new('MAT_Knight')
material.use_nodes = True
nodes, links = material.node_tree.nodes, material.node_tree.links
shader = nodes.get('Principled BSDF')
for suffix, socket in [('color','Base Color'), ('nmap','Normal'), ('metalness','Metallic'), ('rough','Roughness')]:
    tex = nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images['armor_default_' + suffix + '.png']
    tex.image.colorspace_settings.name = 'sRGB' if suffix == 'color' else 'Non-Color'
    if suffix == 'nmap':
        normal = nodes.new('ShaderNodeNormalMap'); normal.inputs['Strength'].default_value = .8
        links.new(tex.outputs['Color'], normal.inputs['Color']); links.new(normal.outputs[0], shader.inputs[socket])
    else: links.new(tex.outputs['Color'], shader.inputs[socket])

factor = 1.87 / 5.594307
for obj in (body, sword):
    bpy.ops.object.select_all(action='DESELECT')
    obj.hide_set(False); obj.hide_viewport = obj.hide_render = False
    bpy.context.view_layer.objects.active = obj; obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # Mesh.transform also transforms the author's custom split normals. The
    # historical +X rig is corrected anatomically after binding all geometry.
    obj.data.transform(Matrix.Diagonal((-1,1,1,1)) @ Matrix.Scale(factor,4) @
                       Matrix.Translation((0,0,2.385746)))
    # Mirroring changes winding; preserve the author's custom split normals otherwise.
    for polygon in obj.data.polygons: polygon.flip()
    obj.data.materials.clear(); obj.data.materials.append(material)
    obj.data.update()

with bpy.data.libraries.load(str(HERE / 'Personagem.blend'), link=False) as (source, target):
    target.objects = ['HeroRig','SwordBase','SwordTip']
    target.actions = ['Idle','Walk','Attack']
for action in target.actions:
    if action:
        action.use_fake_user = True  # Keep unassigned clips in the saved Blender source.
for obj in target.objects:
    if obj and obj.name not in bpy.context.scene.objects: bpy.context.collection.objects.link(obj)
rig = bpy.data.objects['HeroRig']
rig.animation_data.action = None
for track in list(rig.animation_data.nla_tracks): rig.animation_data.nla_tracks.remove(track)
for bone in rig.pose.bones:
    bone.location = (0,0,0); bone.rotation_euler = (0,0,0); bone.scale = (1,1,1)
bpy.context.view_layer.update()

def bone_weights(obj, index, weights):
    total = sum(weights.values())
    for name, weight in weights.items():
        group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        group.add([index],weight / total,'REPLACE')

def smoothstep(low, high, value):
    t = min(1, max(0, (value - low) / (high - low)))
    return t * t * (3 - 2 * t)

def joint_blend(point, first, second, radius):
    """Blend only the flexible joint band; broad armour plates remain rigid."""
    a, b = rig.data.bones[first], rig.data.bones[second]
    axis = ((a.tail_local - a.head_local).normalized() +
            (b.tail_local - b.head_local).normalized()).normalized()
    gain = smoothstep(-radius, radius, (point - b.head_local).dot(axis))
    return {first: 1 - gain, second: gain}

# Lower the original A-pose around each shoulder before binding the armor.
# Armor plates stay rigid; blending is restricted to the mail at the joints.
parents = list(range(len(body.data.vertices)))
def component(index):
    while parents[index] != index:
        parents[index] = parents[parents[index]]
        index = parents[index]
    return index
for edge in body.data.edges:
    a,b = map(component,edge.vertices); parents[a] = b
islands = {}
for v in body.data.vertices: islands.setdefault(component(v.index), []).append(v.index)
# The shield is one separate connected island. Keep it rigid, including its lower tip.
shield_indices = next(ids for ids in islands.values() if len(ids) == 930)
shield_indices = set(shield_indices)
body_corner_normals = [normal.vector.copy() for normal in body.data.corner_normals]
normal_rotations = [Matrix.Identity(3) for _ in body.data.vertices]
for v in body.data.vertices:
    p = v.co.copy(); x, y, z = p; side = 1 if x > 0 else -1
    suffix = 'R' if side > 0 else 'L'
    # The source A-pose's fingertips extend below the waist-height cutoff.
    # Their lateral separation still identifies the gauntlet unambiguously;
    # binding those vertices to Pelvis stretched them into spikes on arm swings.
    arm = (abs(x) > .255 and z > 1.06) or (abs(x) > .45 and z > .80)
    if v.index in shield_indices:
        shoulder = Vector((-.265,0,1.455))
        rotation = Matrix.Rotation(math.radians(-35),4,'Y')
        v.co = shoulder + rotation @ (p - shoulder) + Vector((-.08,0,-.06))
        normal_rotations[v.index] = rotation.to_3x3()
        bone_weights(body,v.index,{'Hand.L':1})
    elif arm:
        shoulder = Vector((side * .265,0,1.455))
        weight = min(1,max(0,(abs(x) - .255) / .11))
        rotation = Matrix.Rotation(math.radians(35) * side * weight,4,'Y')
        v.co = shoulder + rotation @ (p - shoulder)
        normal_rotations[v.index] = rotation.to_3x3()
        wrist_blend = min(1,max(0,(abs(x) - .265) / .385))
        v.co += Vector((side * .08,0,-.06)) * wrist_blend
        xx, yy, zz = v.co
        # Nearest segments only within the correct anatomical limb.
        candidates = ['UpperArm.'+suffix,'Forearm.'+suffix,'Hand.'+suffix]
        def distance(name):
            b = rig.data.bones[name]; delta=b.tail_local-b.head_local
            t=max(0,min(1,(v.co-b.head_local).dot(delta)/delta.length_squared))
            return (v.co-(b.head_local+t*delta)).length
        name=min(candidates,key=distance)
        if name.startswith('UpperArm'):
            weights = joint_blend(v.co, candidates[0], candidates[1], .045)
        elif name.startswith('Forearm'):
            elbow = rig.data.bones[candidates[1]].head_local
            wrist = rig.data.bones[candidates[2]].head_local
            weights = (joint_blend(v.co, candidates[0], candidates[1], .045)
                       if (v.co - elbow).length < (v.co - wrist).length
                       else joint_blend(v.co, candidates[1], candidates[2], .025))
        else:
            weights = joint_blend(v.co, candidates[1], candidates[2], .025)
        # The broad pauldrons are rigid plates mounted on the torso. They cross
        # the historical x=.255 arm boundary; binding them like the sleeve made
        # their inner corners fold into spikes when the running guard rose.
        chest_gain = smoothstep(1.35, 1.43, p.z)
        weights = {name:weight*(1-chest_gain) for name,weight in weights.items()}
        weights['Chest'] = chest_gain
        bone_weights(body,v.index,weights)
    elif z >= 1.54:
        t = smoothstep(1.54, 1.59, z)
        bone_weights(body,v.index,{'Chest':1-t, 'Head':t})
    elif z >= 1.27:
        bone_weights(body,v.index,{'Chest':1})
    elif z >= 1.08:
        bone_weights(body,v.index,{'Spine':1})
    elif z >= .87:
        bone_weights(body,v.index,{'Pelvis':1})
    elif z >= .485:
        t = smoothstep(.485, .595, z)
        bone_weights(body,v.index,{'Thigh.'+suffix:t,'Shin.'+suffix:1-t})
    elif z >= .145:
        t = smoothstep(.145, .195, z)
        bone_weights(body,v.index,{'Shin.'+suffix:t,'Foot.'+suffix:1-t})
    else:
        bone_weights(body,v.index,{'Foot.'+suffix:1})
body.data.normals_split_custom_set([
    (normal_rotations[loop.vertex_index] @ body_corner_normals[loop.index]).normalized()
    for loop in body.data.loops])
body.name = 'KnightArmor'

# Align the downloaded sword with the proven blade path; markers remain exact.
source_guard = Vector((.650,-.127,1.145))
source_tip = Vector((.650025,-.846458,1.5148845))
# Snap to the actual blade endpoint rather than the old approximate measurement.
source_tip = min((v.co for v in sword.data.vertices), key=lambda p:(p-source_tip).length_squared).copy()
source_axis = source_tip - source_guard
guard = bpy.data.objects['SwordBase'].matrix_world.translation.copy()
direction = (bpy.data.objects['SwordTip'].matrix_world.translation - guard).normalized()
rotation = source_axis.normalized().rotation_difference(direction)
scale = 1.025 / source_axis.length
sword.data.transform(Matrix.Translation(guard) @ rotation.to_matrix().to_4x4() @
                     Matrix.Scale(scale,4) @ Matrix.Translation(-source_guard))
for v in sword.data.vertices:
    bone_weights(sword,v.index,{'Hand.R':1})
sword.name = 'KnightSword'
for obj in (body,sword):
    modifier = obj.modifiers.new('HeroRig','ARMATURE'); modifier.object = rig
    obj.parent = rig

# Fit the actual handle to the downloaded glove, rather than to the procedural
# character's wrist. Move the blade and both trail anchors by exactly the same amount.
def close_sword_thumb(point):
    # The source glove has an extended inner thumb. Curl just that lobe over
    # the fingers; preserve the wrist, the broad palm and all UVs/weights.
    gain = (1-smoothstep(.475,.505,point.x))*smoothstep(.080,.130,-point.y)
    gain *= smoothstep(.86,.96,point.z)*(1-smoothstep(.965,1.01,point.z))
    return point+Vector((0,.005,-.055))*gain

normals = [normal.vector.copy() for normal in body.data.corner_normals]
thumb_normals = {}
for vertex in body.data.vertices:
    if not (.42 < vertex.co.x < .505 and .86 < vertex.co.z < 1.01 and vertex.co.y < -.08): continue
    weights = {body.vertex_groups[g.group].name:g.weight for g in vertex.groups}
    if weights.get('Hand.R',0)+weights.get('Forearm.R',0) < .99: continue
    point = vertex.co.copy()
    columns = []
    for axis in range(3):
        delta = Vector(); delta[axis] = .00001
        columns.append((close_sword_thumb(point+delta)-close_sword_thumb(point-delta))/.00002)
    jacobian = Matrix(columns).transposed()
    assert jacobian.determinant() > .1, ('Folded thumb',vertex.index)
    thumb_normals[vertex.index] = jacobian.inverted().transposed()
    vertex.co = close_sword_thumb(point)
    # The old nearest-segment binding mistook the extended thumb for the wrist.
    # Its curled tip must follow the hand together with the hilt.
    curl = (vertex.co-point).length / Vector((0,.005,-.055)).length
    hand_weight = weights.get('Hand.R',0)
    hand_weight += (1-hand_weight)*smoothstep(.1,.5,curl)
    bone_weights(body,vertex.index,{'Hand.R':hand_weight,'Forearm.R':1-hand_weight})
body.data.update()
body.data.normals_split_custom_set([
    (thumb_normals[loop.vertex_index] @ normals[loop.index]).normalized()
    if loop.vertex_index in thumb_normals else normals[loop.index] for loop in body.data.loops])

def palm_center(suffix):
    points = [v.co for v in body.data.vertices if v.index not in shield_indices and v.co.z < 1.03 and
        any(body.vertex_groups[g.group].name == 'Hand.'+suffix and g.weight > .99 for g in v.groups)]
    assert points, ('Missing glove palm', suffix)
    return sum(points, Vector()) / len(points)

palm_right = palm_center('R')
handle_center = guard-direction*.16
grip_offset = palm_right-handle_center
sword.data.transform(Matrix.Translation(grip_offset))
for marker_name in ('SwordBase', 'SwordTip'):
    marker = bpy.data.objects[marker_name]
    marker.matrix_world = Matrix.Translation(grip_offset) @ marker.matrix_world
    bpy.context.view_layer.update()
grip = bpy.data.objects.new('SwordGrip', None)
bpy.context.collection.objects.link(grip)
grip.parent, grip.parent_type, grip.parent_bone = rig, 'BONE', 'Hand.R'
bpy.context.view_layer.update()
grip.matrix_world = Matrix.Translation(palm_right)
bpy.context.view_layer.update()

# Calibrate the shield in the actual carrying pose. Its exterior faces forward,
# its upper rim stays upright and the grip sits behind the face at the left palm.
# Bake back into bind space so every existing 17-bone equipment binding survives.
reset(rig)
carry_guard(rig)
hand = rig.pose.bones['Hand.L']
deformation = hand.matrix @ hand.bone.matrix_local.inverted()
palm_left = deformation @ palm_center('L')
points = np.array([tuple(body.data.vertices[i].co) for i in shield_indices])
center = Vector(points.mean(axis=0))
_, _, axes = np.linalg.svd(points-points.mean(axis=0), full_matrices=False)
normal = Vector(axes[-1])
if normal.y < 0: normal.negate()
up = Vector(axes[0])
# The shield was diagonal in the source A-pose. Its long axis, rather than
# world Z, identifies the top and the tapered lower point.
along = (points-points.mean(axis=0)) @ np.array(up)
across = (points-points.mean(axis=0)) @ axes[1]
span = float(along.max()-along.min())
low = across[along < along.min()+span*.16]
high = across[along > along.max()-span*.16]
if np.ptp(low) > np.ptp(high): up.negate()
source_basis = Matrix((normal.cross(up), normal, up)).transposed()
front = Vector((-.12,-1,0)).normalized()
up = Vector((0,0,1))
target_basis = Matrix((front.cross(up), front, up)).transposed()
rotation = target_basis @ source_basis.transposed()
target_center = palm_left+Vector((-.015,-.085,.02))
fit = deformation.inverted() @ Matrix.Translation(target_center) @ rotation.to_4x4() @ Matrix.Translation(-center)
normals = [normal.vector.copy() for normal in body.data.corner_normals]
for i in shield_indices:
    body.data.vertices[i].co = fit @ body.data.vertices[i].co
body.data.normals_split_custom_set([
    (fit.to_3x3() @ normals[loop.index]).normalized() if loop.vertex_index in shield_indices else normals[loop.index]
    for loop in body.data.loops])
reset(rig)

# The original procedural rig called +X "R", although the mesh faces -Y.
# Correct the anatomy in geometry, bind poses, every clip and the blade markers.
# This produces positive object scales and proper bone matrices in the FBX.
refine_locomotion(rig, body)
mirror_anatomy(rig, [body, sword], [bpy.data.objects[name] for name in ('SwordBase','SwordTip','SwordGrip')])
# Keep every bind pose and equipment bone; transfer the captured motions only
# after correcting the destination rig's anatomical sides.
mixamo_source = Path(os.environ.get('GN_MIXAMO_SOURCE_DIR', str(HERE / 'ThirdParty/Mixamo')))
motion_profile = retarget_mixamo(rig, body, mixamo_source)
periodic_tangents(rig)
diagnostics = validate_rig(rig, body, sword, motion_profile)
(HERE / 'CavaleiroRigDiagnostics.json').write_text(json.dumps(diagnostics, indent=2))
(EXPORT_ART / 'CavaleiroMotionProfile.json').write_text(json.dumps(motion_profile, indent=2))

rig.animation_data.action = bpy.data.actions['Idle']
bpy.context.scene.frame_set(1)
bpy.context.scene.render.fps = 30
export_objects = [rig, body, sword]+[bpy.data.objects[name] for name in ('SwordBase','SwordTip','SwordGrip')]
for obj in list(bpy.data.objects):
    if obj not in export_objects: bpy.data.objects.remove(obj,do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in export_objects: obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(EXPORT_ART / 'Cavaleiro.fbx'), use_selection=True,
    object_types={'ARMATURE','MESH','EMPTY'}, apply_unit_scale=True, global_scale=1,
    axis_forward='-Z', axis_up='Y', add_leaf_bones=False, bake_anim=True,
    bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0, bake_anim_step=.25,
    path_mode='AUTO', use_mesh_modifiers=True)

world = bpy.data.worlds.new('StudioKnight'); world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.25,.30,.38,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.6
bpy.context.scene.world = world
for name,pos,power,size in [('Key',(3,-4,4),300,4),('Fill',(-3,-2,2),180,3),('Rim',(0,3,3),350,3)]:
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
    obj=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(obj); obj.location=pos
    obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
camera_data=bpy.data.cameras.new('PreviewCamera'); camera=bpy.data.objects.new('PreviewCamera',camera_data)
bpy.context.collection.objects.link(camera); camera.location=(2.5,-5,2.4)
camera.rotation_euler=(Vector((0,0,.95))-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.type='ORTHO'; camera_data.ortho_scale=2.35
scene=bpy.context.scene; scene.camera=camera; scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=32; scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED'; scene.render.threads=6
scene.render.resolution_x=1000;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(HERE/'CavaleiroPreview.png')
scene.render.film_transparent=False;scene.view_settings.view_transform='AgX'
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Cavaleiro.blend'))
if '--skip-render' not in sys.argv: bpy.ops.render.render(write_still=True)
# Review from both sides; the frontal image places the right-hand sword on the
# viewer's left, and the rear image places it on the viewer's right.
scene.cycles.samples = 16
scene.render.resolution_x = 800; scene.render.resolution_y = 900
for label, position in [('Frente',(0,-5,1.7)),('Costas',(0,5,1.7))]:
    camera.location=position
    camera.rotation_euler=(Vector((0,0,.96))-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(HERE/('Cavaleiro'+label+'Preview.png'))
    if '--skip-render' not in sys.argv: bpy.ops.render.render(write_still=True)
print('[KNIGHT PASS] Source UV/PBR textures, 17 bones, anatomical right hand, blended joints, ' + '/'.join(motion_profile['clips']) + ' and exact sword markers exported.')
