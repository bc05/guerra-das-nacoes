"""Converte o Troll Mauler de piacenti (CC BY 3.0) para o prototipo.

Executar no Blender, abrindo antes o download original troll.blend e com
--disable-autoexec. Preserva o rig, os pesos e a acao original de 40 frames.
"""
import bpy, json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent.parent
ART=ROOT/'ArtSource'
TEXTURES=ROOT/'Unity/Assets/Art/ThirdParty/Troll'
ART.mkdir(parents=True,exist_ok=True)
TEXTURES.mkdir(parents=True,exist_ok=True)

arm=bpy.data.objects['Armature']
action=bpy.data.actions['ArmatureAction']
arm.animation_data_create()
arm.animation_data.action=action
if len(action.slots):arm.animation_data.action_slot=action.slots[0]
keep={'Armature','med','cloth','eye med'}
for obj in list(bpy.data.objects):
    if obj.name not in keep:bpy.data.objects.remove(obj,do_unlink=True)
meshes=[bpy.data.objects[n] for n in ('med','cloth','eye med')]
for source,target in [('arm.L','Braco.L'),('arm.R','Braco.R'),('thigh.L','Perna.L'),('thigh.R','Perna.R')]:
    arm.data.bones[source].name=target
for obj,name in zip(meshes,('TrollBody','TrollCloth','TrollEyes')):obj.name=name
arm.name='TrollArmature'
action=arm.animation_data.action
action.name='TrollIdle'
action.use_fake_user=True

# Exportar somente os mapas usados, sem referencias locais do autor.
mapping={
 'troll_baseTexBaked.png':'TrollSkin_Albedo.png',
 'troll_normals.png':'TrollSkin_Normal.png',
 'troll_occlusion.png':'TrollSkin_AO.png',
 'cloth uv.png':'TrollCloth_Albedo.png',
 'cloth uv_NRM.png':'TrollCloth_Normal.png',
 'cloth uv_OCC.png':'TrollCloth_AO.png',
 'Material Diffuse Color':'TrollEyes_Albedo.png',
}
images={}
for original,filename in mapping.items():
    image=bpy.data.images[original]
    image.filepath_raw=str(TEXTURES/filename)
    image.file_format='PNG'
    image.save()
    image.name=filename
    images[filename]=image

def make_material(name,prefix,roughness):
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    nodes=mat.node_tree.nodes;nodes.clear();links=mat.node_tree.links
    out=nodes.new('ShaderNodeOutputMaterial');out.location=(650,100)
    bsdf=nodes.new('ShaderNodeBsdfPrincipled');bsdf.location=(350,100)
    bsdf.inputs['Roughness'].default_value=roughness
    bsdf.inputs['Specular IOR Level'].default_value=.28
    links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
    color=nodes.new('ShaderNodeTexImage');color.image=images[prefix+'_Albedo.png'];color.location=(-500,250)
    color.image.colorspace_settings.name='sRGB'
    links.new(color.outputs['Color'],bsdf.inputs['Base Color'])
    if prefix+'_AO.png' in images:
        ao=nodes.new('ShaderNodeTexImage');ao.image=images[prefix+'_AO.png'];ao.image.colorspace_settings.name='Non-Color';ao.location=(-500,-50)
        mul=nodes.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1;mul.location=(20,250)
        links.new(color.outputs['Color'],mul.inputs[1]);links.new(ao.outputs['Color'],mul.inputs[2]);links.new(mul.outputs['Color'],bsdf.inputs['Base Color'])
        normal=nodes.new('ShaderNodeTexImage');normal.image=images[prefix+'_Normal.png'];normal.image.colorspace_settings.name='Non-Color';normal.location=(-500,-350)
        normal_map=nodes.new('ShaderNodeNormalMap');normal_map.location=(20,-120);links.new(normal.outputs['Color'],normal_map.inputs['Color']);links.new(normal_map.outputs['Normal'],bsdf.inputs['Normal'])
    return mat
for obj,name,prefix,roughness in zip(meshes,('MAT_TrollSkin','MAT_TrollCloth','MAT_TrollEyes'),('TrollSkin','TrollCloth','TrollEyes'),(.82,.9,.35)):
    obj.data.materials.clear();obj.data.materials.append(make_material(name,prefix,roughness))
    for p in obj.data.polygons:p.use_smooth=True

scene=bpy.context.scene;scene.frame_set(1);bpy.context.view_layer.update()
dg=bpy.context.evaluated_depsgraph_get()
points=[]
for obj in meshes:
    evaluated=obj.evaluated_get(dg)
    points.extend(evaluated.matrix_world@Vector(corner) for corner in evaluated.bound_box)
minimum=Vector(tuple(min(p[i] for p in points) for i in range(3)))
maximum=Vector(tuple(max(p[i] for p in points) for i in range(3)))
scale=3.2/(maximum.z-minimum.z)
root=bpy.data.objects.new('TrollRoot',None);scene.collection.objects.link(root)
for obj in (arm,):
    matrix=obj.matrix_world.copy();obj.parent=root;obj.matrix_world=matrix
root.scale=(scale,scale,scale)
root.location=(-.5*(minimum.x+maximum.x)*scale,-.5*(minimum.y+maximum.y)*scale,-minimum.z*scale)
bpy.context.view_layer.update()
scene.frame_start=1;scene.frame_end=40;scene.render.fps=24

# Esqueleto e tres malhas: nenhum cenario, luz, camera ou malha reserva.
bpy.ops.object.select_all(action='DESELECT')
for obj in [root,arm,*meshes]:obj.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(ROOT/'Unity/Assets/Art/Troll.fbx'),use_selection=True,object_types={'EMPTY','ARMATURE','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0.0,path_mode='AUTO',embed_textures=False)
for image in images.values():image.pack()
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32
scene.render.threads_mode='FIXED';scene.render.threads=4
scene['asset_source']='https://opengameart.org/content/troll-mauler'
scene['asset_author']='piacenti'
scene['asset_license']='CC-BY-3.0'
scene['asset_modifications']='Materiais Principled, texturas extraidas, ossos de membros renomeados, escala 3.2m, selecao da malha media, FBX.'
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'Troll.blend'))

# Preview separado; os auxiliares de render nao sao salvos no .blend entregue.
def aim(obj,target):obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.005));floor=bpy.context.object
mat=bpy.data.materials.new('PreviewFloor');mat.diffuse_color=(.035,.04,.05,1);floor.data.materials.append(mat)
for loc,power,size,col in [((3,-4,6),600,4,(1,.84,.67)),((-4,-1,3),360,4,(.5,.65,1)),((1,3,5),900,3,(.65,.76,1))]:
    bpy.ops.object.light_add(type='AREA',location=loc);obj=bpy.context.object;obj.data.energy=power;obj.data.shape='DISK';obj.data.size=size;obj.data.color=col;aim(obj,(0,0,1.7))
bpy.ops.object.camera_add(location=(4.3,-7.6,3.1));camera=bpy.context.object;aim(camera,(0,0,1.65));camera.data.lens=52
scene.camera=camera;scene.world.color=(.07,.07,.07);scene.view_settings.view_transform='AgX'
scene.render.resolution_x=900;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ART/'TrollPreview.png')
triangles=0
for obj in meshes:obj.data.calc_loop_triangles();triangles+=len(obj.data.loop_triangles)
report={'height':3.2,'feet_z':0,'front':'-Y','triangles':triangles,'bones':len(arm.data.bones),'bone_names':[b.name for b in arm.data.bones],'action':action.name,'frames':list(action.frame_range),'fps':24,'textures':[{'name':image.name,'size':list(image.size)} for image in images.values()]}
(ART/'TrollMetadata.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('TROLL_EXPORT='+json.dumps(report))
bpy.ops.render.render(write_still=True)
