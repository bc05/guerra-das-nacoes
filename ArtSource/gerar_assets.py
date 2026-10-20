"""Cria assets originais do prototipo, sem bibliotecas ou addons externos.

Executar com Blender 5.x:
  Blender --background --python gerar_assets.py

O eixo vertical no arquivo Blender e Z. O heroi olha para -Y no Blender,
convertido pelo exportador FBX (-Z forward / Y up) para +Z na Unity.
Unidades: metros. Os dois arquivos FBX compartilham origem e escala.
"""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

import bpy
from mathutils import Vector


ART_SOURCE = Path(__file__).resolve().parent
UNITY_ART = ART_SOURCE.parent / "Unity" / "Assets" / "Art"
RNG = random.Random(18062026)
HERO_COLLECTION = "Hero_Assets"
ENV_COLLECTION = "Cenario_Assets"
EXPORT_SETTINGS = dict(
    use_selection=True,
    object_types={"MESH", "ARMATURE", "EMPTY"},
    axis_forward="-Z",
    axis_up="Y",
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS",
    use_space_transform=True,
    bake_space_transform=False,
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    add_leaf_bones=False,
    primary_bone_axis="Y",
    secondary_bone_axis="X",
    armature_nodetype="NULL",
    path_mode="AUTO",
    embed_textures=False,
)


def configure_scene():
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = 24
    # O identificador voltou a BLENDER_EEVEE na serie 5.2.
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("Ceu")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (
        0.18, 0.25, 0.32, 1.0
    )
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.45
    scene.view_settings.view_transform = "AgX"
    return scene


def new_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def move_to_collection(obj, collection):
    for previous in list(obj.users_collection):
        previous.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def material(name, color, metallic=0.0, roughness=0.7):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return mat


def put_material(obj, mat):
    obj.data.materials.append(mat)
    return obj


def box(name, position, scale, mat, collection, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=position)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new("Arestas facetadas", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    put_material(obj, mat)
    return move_to_collection(obj, collection)


def ico(name, position, scale, mat, collection, subdivisions=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1, location=position)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    put_material(obj, mat)
    return move_to_collection(obj, collection)


def cylinder(name, start, end, radius, mat, collection, vertices=8, radius_top=None):
    start, end = Vector(start), Vector(end)
    delta = end - start
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius,
        radius2=radius if radius_top is None else radius_top,
        depth=delta.length,
        location=(start + end) / 2.0,
    )
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = delta.to_track_quat("Z", "Y").to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    put_material(obj, mat)
    return move_to_collection(obj, collection)


def mesh_object(name, vertices, faces, mat, collection):
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    put_material(obj, mat)
    return obj


def bind_rigid(obj, bone_name, parts):
    group = obj.vertex_groups.new(name=bone_name)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    parts.append(obj)
    return obj


def create_hero():
    collection = new_collection(HERO_COLLECTION)
    blue = material("MAT_ArmaduraAzul", (0.055, 0.24, 0.43), 0.35, 0.38)
    blue_light = material("MAT_AzulClaro", (0.12, 0.46, 0.65), 0.25, 0.4)
    copper = material("MAT_Cobre", (0.62, 0.29, 0.12), 0.65, 0.36)
    dark = material("MAT_TecidoEscuro", (0.055, 0.085, 0.11), 0.0, 0.9)
    leather = material("MAT_Couro", (0.18, 0.105, 0.065), 0.0, 0.83)
    skin = material("MAT_Pele", (0.63, 0.40, 0.25), 0.0, 0.75)
    steel = material("MAT_Aco", (0.48, 0.61, 0.66), 0.8, 0.25)
    eye = material("MAT_Visor", (0.06, 0.84, 0.94), 0.3, 0.24)
    parts = []

    def piece(obj, bone):
        return bind_rigid(obj, bone, parts)

    piece(box("Quadril", (0, 0, 0.96), (0.36, 0.25, 0.22), dark, collection, 0.045), "Pelvis")
    piece(box("Cinto", (0, -0.005, 1.045), (0.41, 0.28, 0.075), leather, collection, 0.018), "Pelvis")
    piece(box("Fivela", (0, -0.16, 1.05), (0.11, 0.035, 0.09), copper, collection, 0.01), "Pelvis")
    # O peitoral afunila na cintura; oito vertices mantem a silhueta simples.
    body_vertices = [
        (-0.17, -0.13, 1.08), (0.17, -0.13, 1.08), (0.17, 0.13, 1.08), (-0.17, 0.13, 1.08),
        (-0.26, -0.15, 1.43), (0.26, -0.15, 1.43), (0.26, 0.14, 1.43), (-0.26, 0.14, 1.43),
    ]
    body_faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    piece(mesh_object("Peitoral", body_vertices, body_faces, blue, collection), "Chest")
    piece(box("PlacaPeito", (0, -0.16, 1.31), (0.31, 0.045, 0.16), blue_light, collection, 0.025), "Chest")
    insignia = piece(box("EmblemaCobre", (0, -0.192, 1.31), (0.075, 0.025, 0.075), copper, collection, 0.008), "Chest")
    insignia.rotation_euler.y = math.radians(45)
    piece(cylinder("Pescoco", (0, 0, 1.43), (0, 0, 1.55), 0.075, dark, collection), "Head")
    piece(ico("Cabeca", (0, -0.005, 1.65), (0.145, 0.13, 0.17), skin, collection, 2), "Head")
    piece(ico("Elmo", (0, 0.025, 1.715), (0.16, 0.145, 0.13), blue, collection, 1), "Head")
    piece(box("FaixaElmo", (0, -0.107, 1.715), (0.27, 0.055, 0.055), copper, collection, 0.012), "Head")
    for side in (-1, 1):
        piece(box("Visor", (side * 0.061, -0.128, 1.675), (0.069, 0.027, 0.022), eye, collection, 0.006), "Head")
    piece(box("CristaElmo", (0, 0.02, 1.805), (0.055, 0.24, 0.07), copper, collection, 0.015), "Head")

    for suffix, side in (("L", -1), ("R", 1)):
        upper_arm = f"UpperArm.{suffix}"
        forearm = f"Forearm.{suffix}"
        hand = f"Hand.{suffix}"
        thigh = f"Thigh.{suffix}"
        shin = f"Shin.{suffix}"
        foot = f"Foot.{suffix}"
        shoulder = (side * 0.30, 0, 1.40)
        elbow = (side * 0.43, 0, 1.14)
        wrist = (side * 0.49, -0.005, 0.93)
        piece(cylinder("Braco", shoulder, elbow, 0.080, dark, collection), upper_arm)
        piece(ico("Ombreira", (side * 0.31, 0, 1.40), (0.15, 0.19, 0.13), blue, collection, 1), upper_arm)
        piece(ico("BordaOmbreira", (side * 0.37, -0.01, 1.40), (0.095, 0.195, 0.07), copper, collection, 1), upper_arm)
        piece(ico("Cotovelo", elbow, (0.09, 0.09, 0.09), copper, collection, 1), forearm)
        piece(cylinder("Bracadeira", (side * 0.445, 0, 1.10), wrist, 0.085, blue, collection, radius_top=0.065), forearm)
        piece(ico("Luva", (side * 0.49, -0.005, 0.905), (0.07, 0.08, 0.075), leather, collection, 1), hand)
        hip, knee, ankle = (side * 0.135, 0, 0.94), (side * 0.145, 0, 0.54), (side * 0.145, 0, 0.14)
        piece(cylinder("Coxa", hip, knee, 0.10, dark, collection, radius_top=0.088), thigh)
        piece(box("PlacaCoxa", (side * 0.145, -0.075, 0.76), (0.155, 0.085, 0.26), blue, collection, 0.025), thigh)
        piece(ico("Joelheira", (side * 0.145, -0.02, 0.54), (0.105, 0.12, 0.10), copper, collection, 1), shin)
        piece(cylinder("Greva", (side * 0.145, 0, 0.48), ankle, 0.095, blue, collection, radius_top=0.078), shin)
        piece(box("Bota", (side * 0.145, -0.07, 0.09), (0.21, 0.34, 0.18), leather, collection, 0.04), foot)
        piece(box("Biqueira", (side * 0.145, -0.175, 0.105), (0.22, 0.13, 0.13), copper, collection, 0.025), foot)

    # Espada voltada para baixo: permanece presa a mao direita na animacao.
    piece(cylinder("PunhoEspada", (0.50, -0.035, 0.84), (0.50, -0.035, 1.00), 0.025, leather, collection), "Hand.R")
    piece(box("GuardaEspada", (0.50, -0.035, 0.82), (0.27, 0.065, 0.045), copper, collection, 0.01), "Hand.R")
    blade_vertices = [
        (0.455, -0.035, 0.80), (0.50, -0.065, 0.80), (0.545, -0.035, 0.80), (0.50, -0.005, 0.80),
        (0.472, -0.035, 0.28), (0.50, -0.052, 0.28), (0.528, -0.035, 0.28), (0.50, -0.018, 0.28),
        (0.50, -0.035, 0.18),
    ]
    blade_faces = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 8), (5, 6, 8), (6, 7, 8), (7, 4, 8), (0, 3, 2, 1)]
    piece(mesh_object("LaminaEspada", blade_vertices, blade_faces, steel, collection), "Hand.R")
    piece(ico("PomoEspada", (0.50, -0.035, 1.015), (0.038, 0.038, 0.038), copper, collection, 1), "Hand.R")

    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    mesh = bpy.context.object
    mesh.name = "HeroMesh"
    # Mantem os vertices e a origem no mesmo sistema de coordenadas do rig.
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    armature_data = bpy.data.armatures.new("HeroSkeleton")
    rig = bpy.data.objects.new("HeroRig", armature_data)
    collection.objects.link(rig)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(name, head, tail, parent=None):
        edit_bone = armature_data.edit_bones.new(name)
        edit_bone.head, edit_bone.tail = head, tail
        if parent:
            edit_bone.parent = armature_data.edit_bones[parent]
        return edit_bone

    bone("Root", (0, 0, 0), (0, 0, 0.16))
    bone("Pelvis", (0, 0, 0.94), (0, 0, 1.08), "Root")
    bone("Spine", (0, 0, 1.08), (0, 0, 1.29), "Pelvis")
    bone("Chest", (0, 0, 1.29), (0, 0, 1.46), "Spine")
    bone("Head", (0, 0, 1.49), (0, 0, 1.81), "Chest")
    for suffix, side in (("L", -1), ("R", 1)):
        bone(f"UpperArm.{suffix}", (side * 0.30, 0, 1.40), (side * 0.43, 0, 1.14), "Chest")
        bone(f"Forearm.{suffix}", (side * 0.43, 0, 1.14), (side * 0.49, -0.005, 0.93), f"UpperArm.{suffix}")
        bone(f"Hand.{suffix}", (side * 0.49, -0.005, 0.93), (side * 0.49, -0.005, 0.83), f"Forearm.{suffix}")
        bone(f"Thigh.{suffix}", (side * 0.135, 0, 0.94), (side * 0.145, 0, 0.54), "Pelvis")
        bone(f"Shin.{suffix}", (side * 0.145, 0, 0.54), (side * 0.145, 0, 0.14), f"Thigh.{suffix}")
        bone(f"Foot.{suffix}", (side * 0.145, 0, 0.14), (side * 0.145, -0.21, 0.09), f"Shin.{suffix}")
    bpy.ops.object.mode_set(mode="OBJECT")
    modifier = mesh.modifiers.new("HeroRig", "ARMATURE")
    modifier.object = rig
    mesh.parent = rig
    rig.show_in_front = True
    rig.animation_data_create()
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "XYZ"

    def reset_pose():
        for pose_bone in rig.pose.bones:
            pose_bone.rotation_euler = (0, 0, 0)
            pose_bone.location = (0, 0, 0)

    def key_pose(frame):
        for pose_bone in rig.pose.bones:
            pose_bone.keyframe_insert(data_path="rotation_euler", frame=frame, group=pose_bone.name)
            if pose_bone.name == "Pelvis":
                pose_bone.keyframe_insert(data_path="location", frame=frame, group=pose_bone.name)

    idle = bpy.data.actions.new("Idle")
    idle.use_fake_user = True
    rig.animation_data.action = idle
    for frame in (1, 13, 25, 37, 49):
        reset_pose()
        phase = (frame - 1) / 48.0 * math.tau
        rig.pose.bones["Chest"].rotation_euler.x = 0.012 * math.sin(phase)
        rig.pose.bones["Head"].rotation_euler.z = 0.014 * math.sin(phase)
        rig.pose.bones["UpperArm.L"].rotation_euler.x = 0.018 * math.sin(phase)
        rig.pose.bones["UpperArm.R"].rotation_euler.x = -0.018 * math.sin(phase)
        key_pose(frame)

    walk = bpy.data.actions.new("Walk")
    walk.use_fake_user = True
    rig.animation_data.action = walk
    for frame in (1, 7, 13, 19, 25):
        reset_pose()
        phase = (frame - 1) / 24.0 * math.tau
        for suffix, sign in (("L", 1), ("R", -1)):
            swing = math.sin(phase) * sign
            rig.pose.bones[f"Thigh.{suffix}"].rotation_euler.x = 0.46 * swing
            rig.pose.bones[f"Shin.{suffix}"].rotation_euler.x = -0.48 * max(0.0, -swing)
            rig.pose.bones[f"Foot.{suffix}"].rotation_euler.x = -0.12 * swing
            rig.pose.bones[f"UpperArm.{suffix}"].rotation_euler.x = -0.30 * swing
            rig.pose.bones[f"Forearm.{suffix}"].rotation_euler.x = -0.10
        rig.pose.bones["Chest"].rotation_euler.z = 0.035 * math.sin(phase)
        key_pose(frame)

    rig.animation_data.action = idle
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 49
    bpy.context.scene.frame_set(1)
    # Exportacao usa a armature como raiz: sem rotacao extra ou escala no prefab.
    return collection, rig, mesh


def arch(name, center, radius, spring, depth, thickness, mat, collection, rotation=0.0):
    center_x, center_y = center
    result = []
    for index in range(10):
        a0 = index * math.pi / 10
        a1 = (index + 1) * math.pi / 10
        vertices = []
        for y in (-depth / 2, depth / 2):
            for angle, r in ((a0, radius), (a1, radius), (a1, radius + thickness), (a0, radius + thickness)):
                vertices.append((r * math.cos(angle), y, spring + r * math.sin(angle)))
        obj = mesh_object(name + f"_Pedra{index:02d}", vertices, [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], mat, collection)
        obj.rotation_euler.z = rotation
        obj.location = (center_x, center_y, 0)
        result.append(obj)
    for side in (-1, 1):
        x, y = side * (radius + thickness / 2), 0
        x, y = x * math.cos(rotation), x * math.sin(rotation)
        result.append(box(name + "_Pilar", (center_x + x, center_y + y, spring / 2), (thickness * 1.2, depth * 1.1, spring), mat, collection, 0.055))
        result[-1].rotation_euler.z = rotation
    return result


def tree(name, x, y, height, bark, pine, pine_light, collection):
    cylinder(name + "_Tronco", (x, y, 0), (x, y, height * 0.70), 0.16, bark, collection, radius_top=0.09)
    for index, (bottom, scale) in enumerate(((0.27, 0.90), (0.45, 0.72), (0.63, 0.51))):
        cylinder(name + f"_Copa{index}", (x, y, height * bottom), (x, y, height * (bottom + 0.41)), height * scale * 0.34, pine if index % 2 == 0 else pine_light, collection, vertices=7, radius_top=0)


def banner(name, x, y, color, pole_mat, stone, collection, mirror=1):
    box(name + "_Base", (x, y, 0.12), (0.55, 0.55, 0.24), stone, collection, 0.05)
    cylinder(name + "_Mastro", (x, y, 0.20), (x, y, 3.05), 0.045, pole_mat, collection)
    cylinder(name + "_Travessa", (x - 0.59, y, 2.84), (x + 0.59, y, 2.84), 0.035, pole_mat, collection)
    ico(name + "_Ponta", (x, y, 3.08), (0.10, 0.10, 0.15), pole_mat, collection, 1)
    # Volume muito fino evita superficie invisivel pela orientacao das faces.
    points = [(-0.52, 2.80), (0.52, 2.80), (0.52, 1.55), (0, 1.30), (-0.52, 1.55)]
    vertices = [(x + px, y + py, z) for py in (-0.028, 0.028) for px, z in points]
    faces = [(4, 3, 2, 1, 0), (5, 6, 7, 8, 9)]
    faces += [(i, (i + 1) % 5, (i + 1) % 5 + 5, i + 5) for i in range(5)]
    mesh_object(name + "_Tecido", vertices, faces, color, collection)
    emblem = box(name + "_Emblema", (x, y - 0.035, 2.18), (0.27, 0.015, 0.27), pole_mat, collection, 0.015)
    emblem.rotation_euler.y = math.radians(45 * mirror)


def create_environment():
    collection = new_collection(ENV_COLLECTION)
    ground = material("MAT_Chao", (0.24, 0.29, 0.22), 0, 0.95)
    stone = material("MAT_Pedra", (0.39, 0.43, 0.40), 0, 0.9)
    stone_light = material("MAT_PedraClara", (0.48, 0.52, 0.47), 0, 0.87)
    stone_dark = material("MAT_PedraEscura", (0.27, 0.32, 0.30), 0, 0.95)
    path = material("MAT_Caminho", (0.42, 0.40, 0.32), 0, 0.95)
    grass = material("MAT_Musgo", (0.21, 0.35, 0.23), 0, 1)
    bark = material("MAT_Tronco", (0.20, 0.13, 0.085), 0, 1)
    pine = material("MAT_Pinheiro", (0.075, 0.21, 0.17), 0, 0.95)
    pine_light = material("MAT_PinheiroClaro", (0.12, 0.29, 0.20), 0, 0.95)
    copper = material("MAT_MetalEstandarte", (0.56, 0.33, 0.15), 0.6, 0.4)
    blue = material("MAT_NacaoAzul", (0.055, 0.27, 0.55), 0, 0.8)
    red = material("MAT_NacaoVermelha", (0.56, 0.09, 0.075), 0, 0.8)

    box("Ground_Terreno35m", (0, 0, -0.20), (35, 35, 0.4), ground, collection)
    # Faixa central e praca planas; os relevos decorativos ficam fora do spawn.
    # Pequenas diferencas de altura impedem superficies coplanares na Unity.
    box("Ground_Caminho", (0, 0, 0.0), (5.8, 30, 0.024), path, collection)
    box("Ground_Praca", (0, 0, 0.014), (12, 10, 0.02), path, collection)
    # Lajes niveladas oferecem textura geometrica sem degraus na locomocao.
    for x in range(-2, 3):
        for y in range(-5, 8):
            if RNG.random() < 0.16:
                continue
            slab = box("Ground_Laje", (x * 1.05 + RNG.uniform(-0.02, 0.02), y * 1.05, 0.027), (0.99, 0.99, 0.036), stone_light if RNG.random() < 0.5 else path, collection, 0.018)
            slab.rotation_euler.z = RNG.uniform(-0.025, 0.025)

    for side in (-1, 1):
        box("Wall_Lateral", (side * 16.2, 0, 0.7), (0.8, 31, 1.4), stone_dark, collection, 0.08)
        for y in (-12, -6, 0, 6, 12):
            box("Wall_PilarLateral", (side * 16.2, y, 1.0), (1.1, 1.1, 2), stone, collection, 0.07)
        for y in (-16.2, 16.2):
            box("Wall_Fundo", (side * 9.8, y, 0.65), (13.2, 0.8, 1.3), stone_dark, collection, 0.08)
    arch("Ruin_ArcoPrincipal", (0, 6.6), 1.9, 1.65, 0.75, 0.48, stone_light, collection)
    for side in (-1, 1):
        box("Ruin_Mureta", (side * 5.3, 6.6, 0.65), (5.0, 0.75, 1.3), stone, collection, 0.075)
        box("Ruin_Coluna", (side * 7.6, 6.6, 1.20), (1.0, 1.0, 2.4), stone_light, collection, 0.06)
        box("Ruin_Capitel", (side * 7.6, 6.6, 2.42), (1.3, 1.3, 0.20), stone, collection, 0.05)
    arch("Ruin_ArcoLeste", (10.5, 9), 1.3, 1.4, 0.8, 0.4, stone, collection, math.radians(90))
    banner("Deco_EstandarteAzul", -3.3, 4.5, blue, copper, stone_dark, collection)
    banner("Deco_EstandarteVermelho", 3.3, 4.5, red, copper, stone_dark, collection, -1)

    for index, (x, y, height) in enumerate([
        (-7.8, 9.4, 5.5), (-11.5, 12.8, 6.2), (-13, 7, 4.5),
        (7.8, 11.8, 5.7), (13, 13, 6.2), (13.5, 5, 4.9),
        (-12, -6, 4.8), (-9, -11, 5.3), (11, -10, 4.7), (14, -4, 4.2),
    ]):
        tree(f"Deco_Pinheiro{index:02d}", x, y, height, bark, pine, pine_light, collection)
    for index, (x, y, size) in enumerate([
        (-8, 3, 1.0), (-10, 4, 0.7), (9, 3, 1.1), (11, -1, 0.65),
        (-7, -6, 0.7), (-12, -10, 0.9), (8, -8, 0.8), (6, 11, 0.8),
        (-5.5, 8, 0.5), (4.8, 8.4, 0.6),
    ]):
        rock = ico(f"Rock_{index:02d}", (x, y, size * 0.40), (size, size * 0.73, size * 0.73), stone_dark if index % 2 else stone, collection, 1)
        rock.rotation_euler.z = RNG.uniform(-math.pi, math.pi)
    for index in range(28):
        side = -1 if index % 2 == 0 else 1
        x, y = side * RNG.uniform(6.5, 14.5), RNG.uniform(-13, 14)
        patch = ico("Deco_Musgo", (x, y, -0.035), (RNG.uniform(0.5, 1.2), RNG.uniform(0.5, 1.1), 0.08), grass, collection, 1)
        patch.rotation_euler.z = RNG.uniform(-math.pi, math.pi)
    return collection


def add_light_and_camera():
    scene = bpy.context.scene
    bpy.ops.object.light_add(type="SUN", location=(4, -6, 9))
    sun = bpy.context.object
    sun.name = "Preview_Sol"
    sun.rotation_euler = (math.radians(28), math.radians(-23), math.radians(-28))
    sun.data.energy = 2.3
    sun.data.angle = math.radians(10)
    bpy.ops.object.light_add(type="AREA", location=(1, -5, 5))
    fill = bpy.context.object
    fill.name = "Preview_Preenchimento"
    fill.data.energy = 600
    fill.data.shape = "DISK"
    fill.data.size = 5
    fill.rotation_euler = (Vector((0, 0, 1)) - fill.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.camera_add(location=(4.6, -7.5, 3.5))
    camera = bpy.context.object
    camera.name = "Preview_Camera"
    camera.data.lens = 38
    camera.rotation_euler = (Vector((0, 1.7, 1.25)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera


def export_collection(collection, path, animated=False):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in collection.all_objects:
        if obj.type in {"MESH", "ARMATURE", "EMPTY"}:
            obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        **EXPORT_SETTINGS,
        bake_anim=animated,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=animated,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
    )


def main():
    ART_SOURCE.mkdir(parents=True, exist_ok=True)
    UNITY_ART.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    configure_scene()
    hero_collection, rig, mesh = create_hero()
    export_collection(hero_collection, UNITY_ART / "Personagem.fbx", animated=True)
    hero_stats = {
        "altura_m": round(max((mesh.matrix_world @ v.co).z for v in mesh.data.vertices), 3),
        "base_m": round(min((mesh.matrix_world @ v.co).z for v in mesh.data.vertices), 3),
        "vertices": len(mesh.data.vertices),
        "poligonos": len(mesh.data.polygons),
        "ossos": len(rig.data.bones),
        "animacoes": ["Idle", "Walk"],
        "frente_blender": "-Y",
        "frente_unity": "+Z",
    }
    bpy.ops.wm.save_as_mainfile(filepath=str(ART_SOURCE / "Personagem.blend"))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    configure_scene()
    environment = create_environment()
    export_collection(environment, UNITY_ART / "Cenario.fbx")
    add_light_and_camera()
    # O arquivo editavel do cenario permanece separado do personagem.
    bpy.ops.wm.save_as_mainfile(filepath=str(ART_SOURCE / "Cenario.blend"))
    with bpy.data.libraries.load(str(ART_SOURCE / "Personagem.blend"), link=False) as (source, target):
        target.collections = [HERO_COLLECTION]
    for collection in target.collections:
        if collection:
            bpy.context.scene.collection.children.link(collection)
    bpy.context.scene.frame_set(1)
    bpy.context.scene.render.filepath = str(ART_SOURCE / "Preview.png")
    try:
        bpy.ops.render.render(write_still=True)
    except RuntimeError as error:
        print("EEVEE indisponivel; tentando Workbench:", error)
        bpy.context.scene.render.engine = "BLENDER_WORKBENCH"
        bpy.context.scene.display.shading.color_type = "MATERIAL"
        bpy.context.scene.display.shading.light = "STUDIO"
        bpy.ops.render.render(write_still=True)
    result = {
        "blender": bpy.app.version_string,
        "personagem": hero_stats,
        "cenario": {"dimensoes_m": [35, 35], "chao_m": 0, "spawn_unity": [0, 0, 0], "objetos": len(environment.objects)},
        "arquivos": ["Personagem.blend", "Cenario.blend", "Preview.png", "../Unity/Assets/Art/Personagem.fbx", "../Unity/Assets/Art/Cenario.fbx"],
        "materiais": "Cores PBR simples sem texturas; Unity Built-in Standard pode remapear as cores por nome.",
        "licenca": "Assets geometricos originais criados para este prototipo, sem conteudo externo.",
    }
    (ART_SOURCE / "manifesto_assets.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("ASSETS_PRONTOS", json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
