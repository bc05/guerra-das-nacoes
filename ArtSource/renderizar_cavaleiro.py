"""Review the authoring rig, grips and contact/support/passing gait poses.

Blender --background --disable-autoexec --python-exit-code 1 --python renderizar_cavaleiro.py
Only writes PNG previews; never edits the FBX, game scene or source .blend.
"""
from pathlib import Path
import os
import bpy
from mathutils import Vector
HERE=Path(__file__).resolve().parent
OUTPUT=Path(os.environ.get('GN_CHARACTER_PREVIEW_DIR',str(HERE)))
OUTPUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Cavaleiro.blend'),use_scripts=False)
scene=bpy.context.scene
rig=bpy.data.objects['HeroRig']
camera=bpy.data.objects['PreviewCamera']
scene.camera=camera
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=12;scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=6
scene.render.resolution_x=650;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
subject=[obj for obj in scene.objects if obj.type=='MESH' and not obj.hide_render]
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.006))
floor=bpy.context.object;floor.name='StudioFloor'
material=bpy.data.materials.new('StudioFloorMaterial');material.diffuse_color=(.13,.14,.16,1);material.use_nodes=True
material.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.13,.14,.16,1)
material.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.95
floor.data.materials.append(material)
views=[('CavaleiroPreview','Idle',1,(2.5,-5,2.4),2.35),
       ('CavaleiroFrentePreview','Idle',1,(0,-5,1.65),2.35),
       ('CavaleiroCostasPreview','Idle',1,(0,5,1.65),2.35),
       ('CavaleiroCorridaPreview','Run',6,(-3,-4,1.7),2.35),
       ('CavaleiroAtaquePreview','Attack',5,(2.5,-5,2.3),2.75),
       ('CavaleiroCaminhadaContatoPreview','Walk',1,(-3,-4,1.7),2.35),
       ('CavaleiroCaminhadaApoioPreview','Walk',7,(-3,-4,1.7),2.35),
       ('CavaleiroCaminhadaPassagemPreview','Walk',25,(-3,-4,1.7),2.35),
       ('CavaleiroCaminhadaLateralPreview','Walk',7,(-5,0,1.3),2.35),
       ('CavaleiroEmpunhaduraPreview','Idle',1,(-3,-4,1.8),1.3,(0,0,1.15))]
for filename,clip,frame,position,scale,*targets in views:
 target=targets[0] if targets else (0,0,.96)
 rig.animation_data.action=bpy.data.actions[clip]
 scene.frame_set(frame);bpy.context.view_layer.update()
 camera.location=position;camera.data.type='ORTHO';camera.data.ortho_scale=scale
 rotation=(Vector(target)-camera.location).to_track_quat('-Z','Y')
 camera.rotation_euler=rotation.to_euler()
 if not targets:
  # The retargeted guard/strike reaches farther than the old fixed framing.
  # Fit the evaluated geometry, including the blade, without changing the pose.
  graph=bpy.context.evaluated_depsgraph_get();points=[]
  for obj in subject:
   evaluated=obj.evaluated_get(graph)
   points.extend(evaluated.matrix_world@Vector(corner) for corner in evaluated.bound_box)
  right=rotation@Vector((1,0,0));up=rotation@Vector((0,1,0));focus=Vector(target)
  xs=[(point-focus).dot(right) for point in points];ys=[(point-focus).dot(up) for point in points]
  camera.location+=right*((min(xs)+max(xs))*.5)+up*((min(ys)+max(ys))*.5)
  aspect=scene.render.resolution_x/scene.render.resolution_y
  camera.data.ortho_scale=max(scale,(max(xs)-min(xs))/aspect*1.08,(max(ys)-min(ys))*1.08)
 scene.render.filepath=str(OUTPUT/(filename+'.png'))
 bpy.ops.render.render(write_still=True)
 print('[PREVIEW PASS]',filename,clip,frame,flush=True)
