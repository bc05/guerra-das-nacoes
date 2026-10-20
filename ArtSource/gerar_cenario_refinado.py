"""Patio original de ruinas antigas. Nao modifica personagem ou monstro.
Blender --background --python-exit-code 1 --python gerar_cenario_refinado.py
UVs por projecao de caixa mundial: uma unidade UV por metro. Piso superior Z=0.
"""
from __future__ import annotations
import importlib.util
import json
import math
import random
from pathlib import Path
import bpy
import numpy as np
import bmesh
from mathutils import Vector

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("original_environment_helpers", HERE / "gerar_assets.py")
BASE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BASE)
RNG = random.Random(18102026)


def metric_uv(obj):
    mesh = obj.data
    uv = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    matrix = obj.matrix_world
    normal_matrix = matrix.to_3x3()
    for polygon in mesh.polygons:
        normal = normal_matrix @ polygon.normal
        axis = max(range(3), key=lambda index: abs(normal[index]))
        for loop_index in polygon.loop_indices:
            co = matrix @ mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv.data[loop_index].uv = (co.y, co.z) if axis == 0 else (co.x, co.z) if axis == 1 else (co.x, co.y)


def outward_normals(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def finish(obj, mat, target, bevel=0):
    obj.data.materials.append(mat)
    BASE.move_to_collection(obj, target)
    if bevel:
        modifier = obj.modifiers.new("Bordas erodidas", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def block(name, center, size, mat, target, rough=0.035, bevel=0.03, rotation=0, flat_top=False):
    # Eight independently eroded corners break the repeated rectangular silhouette.
    sx, sy, sz = (value / 2 for value in size)
    vertices = []
    for z in (-1, 1):
        for y in (-1, 1):
            for x in (-1, 1):
                vertices.append((x * sx + RNG.uniform(-rough, rough), y * sy + RNG.uniform(-rough, rough),
                    z * sz + (0 if flat_top else RNG.uniform(-rough, rough))))
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    obj.location = center
    obj.rotation_euler.z = rotation
    finish(obj, mat, target, bevel)
    return obj


def noisy_material(name, color, roughness=.9, metallic=0):
    mat = BASE.material(name, color, metallic, roughness)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 12
    noise.inputs["Detail"].default_value = 4
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = .2
    ramp.color_ramp.elements[0].color = (*(value * .58 for value in color), 1)
    ramp.color_ramp.elements[1].position = .8
    ramp.color_ramp.elements[1].color = (*(min(1, value * 1.18) for value in color), 1)
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .18
    bump.inputs["Distance"].default_value = .045
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], shader.inputs["Base Color"])
    links.new(noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    return mat


def arch(target, mats):
    # Deep voussoirs and narrow projecting archivolts give the ruin a masonry construction.
    radius, spring, depth, thickness = 2.15, 2.35, 1.25, .64
    for index in range(15):
        a0, a1 = index * math.pi / 15 + .006, (index + 1) * math.pi / 15 - .006
        vertices = [(r * math.cos(angle), y, spring + r * math.sin(angle))
            for y in (-depth / 2, depth / 2)
            for angle, r in ((a0, radius), (a1, radius), (a1, radius + thickness), (a0, radius + thickness))]
        obj = BASE.mesh_object("Ruin_ArcoBloco", vertices,
            [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)],
            mats["stone"] if index % 3 else mats["light"], target)
        obj.location.y = 8.4
        bevel = obj.modifiers.new("Aresta talhada", "BEVEL")
        bevel.width, bevel.segments = .025, 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=bevel.name)
        for front in (-1, 1):
            trim_radius = radius + thickness * .79
            trim_points = [(r * math.cos(angle), 8.4 + front * .666, spring + r * math.sin(angle))
                for angle, r in ((a0, trim_radius), (a1, trim_radius),
                    (a1, trim_radius + .11), (a0, trim_radius + .11))]
            # A shallow solid trim retains visible side faces from the default game camera.
            trim = BASE.mesh_object("Ruin_Arquivolta", trim_points,
                [(0, 3, 2, 1)], mats["light"], target)
            solid = trim.modifiers.new("Profundidade moldura", "SOLIDIFY")
            solid.thickness = .045
            bpy.context.view_layer.objects.active = trim
            bpy.ops.object.modifier_apply(modifier=solid.name)
    for side in (-1, 1):
        x = side * (radius + thickness / 2)
        for level in range(4):
            block("Ruin_PilarEsculpido", (x, 8.4, .28 + level * .57), (.61, 1.22, .55),
                mats["stone" if level % 2 else "light"], target, rough=.018, bevel=.035)
        block("Ruin_BaseColuna", (x, 8.4, .17), (.94, 1.56, .34), mats["dark"], target, rough=.025, bevel=.035)
        block("Ruin_Capitel", (x, 8.4, 2.22), (.91, 1.57, .24), mats["light"], target, rough=.022, bevel=.025)
        # Recessed fluting on the pillar reads as shallow architectural carving.
        for flute in (-1, 0, 1):
            block("Ruin_Canelura", (x + flute * .15, 7.774, 1.29), (.035, .028, 1.50),
                mats["dark"], target, rough=0, bevel=.01)


def banner(target, x, y, fabric, mats):
    block("Deco_BaseEstandarte", (x, y, .15), (.52, .58, .30), mats["dark"], target, rough=.02, bevel=.04)
    BASE.cylinder("Deco_MastroFerro", (x, y, .2), (x, y, 3.05), .035, mats["metal"], target, 10, .023)
    BASE.cylinder("Deco_TravessaFerro", (x - .5, y, 2.83), (x + .5, y, 2.83), .025, mats["metal"], target, 8)
    verts, faces = [], []
    columns, rows = 10, 13
    for row in range(rows):
        down = row / (rows - 1)
        for col in range(columns):
            across = col / (columns - 1)
            fold = math.sin(across * math.pi * 4 + down * 1.7) * .045 * (.25 + down)
            z = 2.79 - down * (1.30 + .18 * (1 - abs(across * 2 - 1)))
            verts.append((x + (across - .5) * .91, y - .04 + fold, z))
            if row and col:
                a = row * columns + col
                faces.append((a - columns - 1, a - columns, a, a - 1))
    cloth = BASE.mesh_object("Deco_TecidoEstandarte", verts, faces, fabric, target)
    solid = cloth.modifiers.new("Tecido duplo", "SOLIDIFY")
    solid.thickness = .008
    bpy.context.view_layer.objects.active = cloth
    bpy.ops.object.modifier_apply(modifier=solid.name)
    for polygon in cloth.data.polygons:
        polygon.use_smooth = True
    # Slender heraldic blade, kept subordinate to the architecture.
    BASE.cylinder("Deco_InsigniaBordada", (x, y - .105, 1.91), (x, y - .105, 2.56), .018, mats["metal"], target, 6, .004)
    BASE.cylinder("Deco_InsigniaGuarda", (x - .14, y - .105, 2.36), (x + .14, y - .105, 2.36), .014, mats["metal"], target, 6)


def dead_tree(target, x, y, height, mats, name):
    pieces = []
    offsets = [(0, 0, 0), (.12, -.03, .8), (-.07, .07, 1.6), (.22, -.03, 2.35), (.30, .18, 3.10)]
    points = [Vector((x + a * height / 3.1, y + b * height / 3.1, z * height / 3.1)) for a, b, z in offsets]
    for index, (a, b) in enumerate(zip(points, points[1:])):
        pieces.append(BASE.cylinder(name + "Tronco", a, b, .20 - index * .035, mats["bark"], target, 10, .16 - index * .035))
    for branch in range(7):
        origin = points[1 + branch % 3]
        angle = branch * 2.399 + x
        reach = height * RNG.uniform(.20, .36)
        delta = Vector((math.cos(angle) * reach, math.sin(angle) * reach, height * RNG.uniform(.1, .28)))
        elbow = origin + delta * .55 + Vector((0, 0, .10))
        end = origin + delta
        pieces.append(BASE.cylinder(name + "Galho", origin, elbow, .075, mats["bark"], target, 8, .04))
        pieces.append(BASE.cylinder(name + "Galho", elbow, end, .04, mats["bark"], target, 8, .006))
        twig = end + Vector((-.14 * math.sin(angle), .14 * math.cos(angle), .25))
        pieces.append(BASE.cylinder(name + "Ramo", elbow.lerp(end, .8), twig, .023, mats["bark"], target, 6, .003))
    for root in range(5):
        angle = root * math.tau / 5
        end = Vector((x + math.cos(angle) * .7, y + math.sin(angle) * .7, .015))
        pieces.append(BASE.cylinder(name + "Raiz", Vector((x, y, .16)), end, .10, mats["bark"], target, 8, .018))
    bpy.ops.object.select_all(action="DESELECT")
    for piece in pieces: piece.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    for polygon in obj.data.polygons: polygon.use_smooth = True


def low_grass(target, x, y, mat):
    vertices, faces = [], []
    for blade in range(10):
        angle = RNG.uniform(0, math.tau)
        height = RNG.uniform(.08, .25)
        origin = Vector((x + RNG.uniform(-.19, .19), y + RNG.uniform(-.19, .19), .002))
        side = Vector((math.cos(angle), math.sin(angle), 0)) * .018
        bend = Vector((-math.sin(angle), math.cos(angle), 0)) * .09
        v = len(vertices)
        vertices += [tuple(origin - side), tuple(origin + side), tuple(origin + bend + Vector((0, 0, height)))]
        faces += [(v, v + 1, v + 2), (v + 2, v + 1, v)]
    BASE.mesh_object("Deco_VegetacaoBaixa", vertices, faces, mat, target)


def environment_surfaces():
    """Original 1K PBR maps: periodic geology, wear and branched hairline fractures."""
    spec = importlib.util.spec_from_file_location("environment_surface_helpers", HERE / "gerar_texturas.py")
    gen = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gen)
    gen.SIZE = 1024
    folder = BASE.UNITY_ART / "Textures"
    folder.mkdir(parents=True, exist_ok=True)
    report = []
    for index, name in enumerate(("EnvironmentStone", "EnvironmentPaving", "EnvironmentEarth")):
        surface = gen.PeriodicSurface(720181 + index * 97)
        macro = surface.fbm((2, 7, 23, 83), (.35, .30, .25, .10))
        medium = surface.noise(113)
        grain = surface.grain()
        cracks = np.zeros_like(surface.u)
        rng = np.random.default_rng(4017 + index * 23)
        # Short branching fractures have worn shoulders, rather than repeating an entire masonry grid.
        for branch in range(17 if index != 2 else 8):
            origin = rng.uniform(0, 1, 2)
            angle = rng.uniform(0, math.tau)
            length = rng.uniform(.07, .28)
            width = rng.uniform(.0013, .0034)
            direction = np.array((math.cos(angle), math.sin(angle)))
            last = origin
            for step in range(3):
                next_point = last + direction * length / 3 + rng.uniform(-.025, .025, 2)
                delta = next_point - last
                dx = (surface.u - last[0] + .5) % 1 - .5
                dy = (surface.v - last[1] + .5) % 1 - .5
                projection = np.clip((dx * delta[0] + dy * delta[1]) / (delta @ delta), 0, 1)
                distance = np.sqrt((dx - projection * delta[0]) ** 2 + (dy - projection * delta[1]) ** 2)
                cracks = np.maximum(cracks, np.exp(-(distance / width) ** 2).astype(np.float32))
                last = next_point
        moss = np.clip((surface.noise(5) - .67) / .26, 0, 1) * np.clip((macro - .38) * 3, 0, 1)
        if index == 0:
            base = np.array((.655, .685, .685), dtype=np.float32)
            variation = (macro - .5) * .31 + (medium - .5) * .085 + (grain - .5) * .016 - cracks * .16
            height = macro * .44 + medium * .30 + grain * .035 - cracks * .30
            depth = .004
            smoothness = np.clip(.18 + (macro - .5) * .14 - moss * .08, .08, .26)
        elif index == 1:
            base = np.array((.66, .685, .69), dtype=np.float32)
            mineral = np.cos((surface.u * 3 + surface.v * 2 + macro * .34) * math.tau) * .012
            variation = (macro - .5) * .28 + (medium - .5) * .055 + mineral + (grain - .5) * .012 - cracks * .21
            height = macro * .34 + medium * .20 + grain * .025 - cracks * .37
            depth = .0034
            smoothness = np.clip(.21 + (macro - .5) * .13 - cracks * .10, .08, .3)
        else:
            base = np.array((.43, .405, .35), dtype=np.float32)
            pebbles = np.clip((surface.noise(173) - .72) / .20, 0, 1)
            variation = (macro - .5) * .23 + (medium - .5) * .08 + (grain - .5) * .035 + pebbles * .06
            height = macro * .32 + medium * .28 + grain * .06 + pebbles * .35
            depth = .0021
            smoothness = np.clip(.12 + (macro - .5) * .08, .07, .17)
        albedo = np.clip(base[None, None, :] + variation[:, :, None], .18, .88)
        albedo *= 1 - moss[:, :, None] * np.array((.22, .06, .27), dtype=np.float32)
        normal = gen.normal_from_height(height, depth)
        packed = np.zeros((*surface.u.shape, 4), dtype=np.float32)
        packed[:, :, 3] = smoothness
        maps = {"Albedo": gen.pad_period(gen.rgba(albedo)), "Normal": gen.pad_period(gen.rgba(normal)),
                "MetallicSmoothness": gen.pad_period(packed)}
        entries = [gen.save_png(folder / (name + "_" + channel + ".png"), data) for channel, data in maps.items()]
        report.append({"name": name, "size": 1024, "maps": entries, "height_m": depth, "uv_scale_m": 1})
    work = HERE.parents[2] / "work"
    work.mkdir(exist_ok=True)
    (work / "environment-surface-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("ENVIRONMENT_PBR_READY", json.dumps([{key: entry[key] for key in ("name", "size", "height_m")} for entry in report]))


def mapped_material(name, surface, tint=(1, 1, 1), roughness=.8):
    mat = BASE.material(name, tint, 0, roughness)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    folder = BASE.UNITY_ART / "Textures"
    images = {}
    for channel in ("Albedo", "Normal", "MetallicSmoothness"):
        image = bpy.data.images.load(str(folder / (surface + "_" + channel + ".png")), check_existing=True)
        image.colorspace_settings.name = "sRGB" if channel == "Albedo" else "Non-Color"
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = image
        texture.extension = "REPEAT"
        images[channel] = texture
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1
    multiply.inputs[2].default_value = (*tint, 1)
    links.new(images["Albedo"].outputs["Color"], multiply.inputs[1])
    links.new(multiply.outputs[0], shader.inputs["Base Color"])
    normal = nodes.new("ShaderNodeNormalMap")
    normal.inputs["Strength"].default_value = .75
    links.new(images["Normal"].outputs["Color"], normal.inputs["Color"])
    links.new(normal.outputs[0], shader.inputs["Normal"])
    rough = nodes.new("ShaderNodeMath")
    rough.operation = "SUBTRACT"
    rough.inputs[0].default_value = 1
    links.new(images["MetallicSmoothness"].outputs["Alpha"], rough.inputs[1])
    links.new(rough.outputs[0], shader.inputs["Roughness"])
    return mat


def join_meshes(objects, name):
    objects = [obj for obj in objects if obj is not None and obj.type == "MESH"]
    if not objects: return None
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    return result


def polygon_slab(name, center, width, depth, mat, target, floor=False, rotation=0):
    # Chipped corners are geometric; top faces of walkable slabs remain exactly on Z=0.
    half_x, half_y = width * .5, depth * .5
    cut = min(width, depth) * RNG.uniform(.08, .21)
    outline = [(-half_x + cut, -half_y), (half_x - cut * .4, -half_y), (half_x, -half_y + cut),
               (half_x, half_y - cut * .6), (half_x - cut, half_y), (-half_x + cut * .8, half_y),
               (-half_x, half_y - cut), (-half_x, -half_y + cut * .4)]
    bottom, top = (-.17, 0) if floor else (-.12, .12)
    verts = [(x + RNG.uniform(-.015, .015), y + RNG.uniform(-.015, .015), z)
             for z in (bottom, top) for x, y in outline]
    faces = [tuple(reversed(range(8))), tuple(range(8, 16))]
    faces += [(i, (i + 1) % 8, (i + 1) % 8 + 8, i + 8) for i in range(8)]
    obj = BASE.mesh_object(name, verts, faces, mat, target)
    obj.location = center
    obj.rotation_euler.z = rotation
    modifier = obj.modifiers.new("Fio lascado", "BEVEL")
    modifier.width, modifier.segments = (.009 if floor else .02), 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def gothic_arch(target, mats, center=(0, 12.0), half=3.0, spring=3.55, rise=4.2, broken=False, suffix="Principal"):
    def curve(t, side, offset):
        # Quadratic ogive: tangent starts vertical, then converges at a sharp apex.
        x = side * (half * (1 - t * t))
        z = spring + rise * (.7 * t + .3 * t * t)
        tangent = Vector((-side * 2 * half * max(t, .001), rise * (.7 + .6 * t))).normalized()
        outward = Vector((side * tangent.y, abs(tangent.x)))
        return Vector((x, z)) + outward * offset
    arch_parts = []
    segments = 15
    for side in (-1, 1):
        for index in range(segments):
            if broken and side == 1 and index > 6: continue
            if broken and side == -1 and index > 11: continue
            t0, t1 = index / segments + .002, (index + 1) / segments - .002
            for band, (inner, outer, front, thickness, material) in enumerate((
                (0, .63, -.14, 1.22, mats["stone"]),
                (-.10, .005, -.29, .14, mats["light"]),
                (.13, .23, -.30, .14, mats["dark"]),
                (.61, .76, -.28, .14, mats["light"]))):
                points = [curve(t0, side, inner), curve(t1, side, inner), curve(t1, side, outer), curve(t0, side, outer)]
                verts = [(center[0] + p.x, center[1] + y, p.y) for y in (front, front + thickness) for p in points]
                obj = BASE.mesh_object("Ruin_Ogiva" + suffix, verts,
                    [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], material, target)
                bevel = obj.modifiers.new("Desgaste ogiva", "BEVEL")
                bevel.width, bevel.segments = .018 if band else .032, 2
                bpy.context.view_layer.objects.active = obj
                bpy.ops.object.modifier_apply(modifier=bevel.name)
                arch_parts.append(obj)
    join_meshes(arch_parts, "Ruin_OgivaMoldurada" + suffix)
    for side in (-1, 1):
        x = center[0] + side * (half + .29)
        column(target, mats, x, center[1] + .2, spring, "Portal" + suffix)
    if not broken:
        # Slender stone finial strengthens the pointed silhouette without a giant floating crystal.
        block("Ruin_FinialBase", (center[0], center[1] + .3, spring + rise + .34), (.62, .70, .45),
              mats["light"], target, rough=.035, bevel=.035)
        BASE.cylinder("Ruin_FinialGothico", (center[0], center[1] + .3, spring + rise + .50),
                      (center[0], center[1] + .3, spring + rise + 1.26), .22, mats["dark"], target, 8, .018)


def column(target, mats, x, y, height, label):
    pieces = []
    for z, width, thickness in ((.11, 1.14, .22), (.29, .96, .15), (.43, .83, .13),
                                (height - .30, .86, .13), (height - .15, 1.07, .14)):
        pieces.append(block("Ruin_MolduraColuna", (x, y, z), (width, width, thickness),
                      mats["light"], target, rough=.025, bevel=.025))
    for sx, sy in ((-.24, -.22), (.24, -.22), (-.24, .22), (.24, .22), (0, -.28)):
        pieces.append(BASE.cylinder("Ruin_Colunelo", (x + sx, y + sy, .47), (x + sx, y + sy, height - .32),
                      .13 if sx else .11, mats["stone"], target, 12, .12 if sx else .10))
    # A carved eight-pointed capital with a recessed dark neck.
    pieces.append(BASE.cylinder("Ruin_ColarCapitel", (x, y, height - .42), (x, y, height - .25),
                  .40, mats["dark"], target, 12, .44))
    for direction in range(8):
        angle = direction * math.tau / 8
        start = Vector((x + math.cos(angle) * .30, y + math.sin(angle) * .30, height - .33))
        end = Vector((x + math.cos(angle) * .47, y + math.sin(angle) * .47, height - .12))
        pieces.append(BASE.cylinder("Ruin_FolhaCapitel", start, end, .055, mats["light"], target, 5, .025))
    return join_meshes(pieces, "Ruin_ColunaGothica" + label)


def broken_wall(target, mats, side, y0, y1):
    parts = []
    for row in range(6):
        y = y0 - (row % 2) * .45
        while y < y1:
            width = RNG.uniform(.8, 1.45)
            mid = y + width / 2
            top_loss = (row >= 4 and RNG.random() < .38) or (row == 5 and mid < y0 + 1.8)
            if not top_loss:
                z = .34 + row * .62
                parts.append(block("Wall_AlvenariaQuebrada", (side * 10.6, mid, z),
                    (.78 + RNG.uniform(-.06, .06), width - .035, .59),
                    mats[RNG.choice(("stone", "stone", "light", "dark"))], target, rough=.065, bevel=.035))
            y += width
    join_meshes(parts, "Wall_RuinaLateral" + str(side))
    for y in (y0 + .4, (y0 + y1) / 2, y1 - .3):
        # Sloping buttresses and narrow caps break the repeated wall silhouette.
        verts = [(side * x, y + dy, z) for dy in (-.68, .68)
                 for x, z in ((9.45, 0), (11.1, 0), (11.1, 4.8), (10.46, 4.8), (10.30, 2.8), (9.45, 1.0))]
        obj = BASE.mesh_object("Ruin_Contraforte", verts,
            [(5,4,3,2,1,0), (6,7,8,9,10,11)] + [(i, (i + 1) % 6, (i + 1) % 6 + 6, i + 6) for i in range(6)], mats["stone"], target)
        for z, size in ((1.0, 1.72), (2.85, .99), (4.86, .85)):
            block("Ruin_CapaContraforte", (side * (10.25 if z < 2 else 10.7), y, z),
                  (size, 1.55, .14), mats["light"], target, rough=.03, bevel=.025)


def wrought_iron(target, mats, side):
    pieces = []
    x = side * 9.20
    for z in (1.15, 1.90):
        pieces.append(BASE.cylinder("Deco_TrilhoFerro", (x, -1.4, z), (x, 4.6, z), .025, mats["metal"], target, 8))
    for index in range(16):
        if index in (4, 5, 11): continue
        y = -1.4 + index * .40
        height = 2.05 + RNG.uniform(-.12, .12)
        pieces.append(BASE.cylinder("Deco_GradeFerro", (x, y, .25), (x, y, height), .022,
                      mats["metal"], target, 8))
        pieces.append(BASE.cylinder("Deco_PontaFerro", (x, y, height - .03), (x, y, height + .22), .06,
                      mats["metal"], target, 6, .001))
    join_meshes(pieces, "Deco_GradeAbandonada" + str(side))


def scattered_masonry(target, mats):
    for cluster in range(20):
        side = -1 if cluster % 2 else 1
        x, y = side * RNG.uniform(9.5, 14.0), RNG.uniform(-9, 13.5)
        pieces = []
        for shard in range(RNG.randint(4, 8)):
            width, depth, height = RNG.uniform(.2, .8), RNG.uniform(.18, .65), RNG.uniform(.15, .48)
            obj = block("Deco_FragmentoAngular", (x + RNG.uniform(-.8, .8), y + RNG.uniform(-.7, .7), height * .31),
                (width, depth, height), mats[RNG.choice(("stone", "light", "dark"))], target,
                rough=min(width, depth, height) * .22, bevel=.012, rotation=RNG.uniform(-math.pi, math.pi))
            obj.rotation_euler.x = RNG.uniform(-.32, .32)
            obj.rotation_euler.y = RNG.uniform(-.32, .32)
            pieces.append(obj)
        join_meshes(pieces, "Deco_EntulhoTalhado%02d" % cluster)


def near_ruins(target, mats, side):
    """Low side architecture frames the normal play camera without entering the combat corridor."""
    x = side * 6.85
    column(target, mats, x, 2.8, 2.25 if side < 0 else 1.86, "QuebradaProxima")
    pieces = []
    for row in range(2):
        for index in range(4):
            if row == 1 and index == (1 if side < 0 else 2): continue
            y = 3.65 + index * .70
            pieces.append(block("Ruin_MuretaFraturada", (x, y, .30 + row * .56),
                (.66, .67, .54), mats["stone" if index % 3 else "dark"], target, rough=.065, bevel=.027))
    pieces.append(block("Ruin_MolduraPartida", (x, 4.22, 1.20), (.84, 1.25, .13),
        mats["light"], target, rough=.045, bevel=.025, rotation=side * .05))
    pieces.append(block("Ruin_MolduraPartida", (x, 5.85, .67), (.85, .72, .12),
        mats["light"], target, rough=.045, bevel=.02, rotation=side * -.08))
    join_meshes(pieces, "Ruin_MuretaProxima" + str(side))
    # A fallen fluted column in a front corner provides detail at the closer camera distance.
    fallen = column(target, mats, 0, 0, 2.2, "CaidaProxima")
    fallen.location = (side * 5.9, -4.8, .40)
    fallen.rotation_euler = (math.radians(87), side * .08, side * -.08)
    fragments = []
    for shard in range(7):
        y = RNG.uniform(2.5, 5.9)
        fragments.append(block("Deco_FragmentoMureta", (side * RNG.uniform(6.6, 7.6), y, .08),
            (RNG.uniform(.12, .31), RNG.uniform(.12, .36), RNG.uniform(.10, .22)),
            mats["stone" if shard % 2 else "light"], target, rough=.025, bevel=.012,
            rotation=RNG.uniform(-1, 1)))
    join_meshes(fragments, "Deco_EntulhoProximo" + str(side))


def ivy(target, mats, side):
    pieces = []
    x = side * 10.14
    for index in range(28):
        y, z = RNG.uniform(-1, 9.0), RNG.uniform(.2, 3.1)
        leaves, faces = [], []
        for leaf in range(5):
            height = RNG.uniform(.08, .15)
            origin = Vector((x - side * RNG.uniform(.01, .04), y + RNG.uniform(-.12, .12), z + RNG.uniform(-.15, .15)))
            idx = len(leaves)
            leaves += [tuple(origin + Vector((0, -height, 0))), tuple(origin + Vector((0, 0, height))),
                       tuple(origin + Vector((0, height * .75, 0))), tuple(origin + Vector((-side * .045, 0, height * .28)))]
            faces += [(idx, idx + 1, idx + 3), (idx + 1, idx + 2, idx + 3), (idx + 2, idx, idx + 3)]
        pieces.append(BASE.mesh_object("Deco_FolhaHera", leaves, faces, mats["grass"], target))
    join_meshes(pieces, "Deco_HeraMuralha" + str(side))


def distant_backdrop(target, mats):
    """Non-walkable world dressing outside the original 35 m arena footprint."""
    xs = (-55, -45, -35, -27, -21, -17.5, 17.5, 21, 27, 35, 45, 55)
    ys = (-30, -24, -17.5, 17.5, 21, 27, 35, 45, 55, 65)
    vertices, faces = [], []
    for y in ys:
        for x in xs:
            outside = max(abs(x) - 17.5, abs(y) - 17.5, 0)
            relief = max(0, outside - .05) * .028 * (1 + .45 * math.sin(x * .13 + y * .17))
            vertices.append((x, y, -.035 + relief))
    for row in range(len(ys) - 1):
        for col in range(len(xs) - 1):
            # The arena remains a separate solid: never overlay its floor with background geometry.
            mid_x, mid_y = (xs[col] + xs[col + 1]) * .5, (ys[row] + ys[row + 1]) * .5
            if abs(mid_x) < 17.5 and abs(mid_y) < 17.5: continue
            a = row * len(xs) + col
            faces.append((a, a + 1, a + 1 + len(xs), a + len(xs)))
    BASE.mesh_object("Deco_BackdropTerrenoDistante", vertices, faces, mats["ground"], target)
    rocks = []
    for index, (x, y, width, depth, height) in enumerate((
        (-28, 40, 12, 12, 6.5), (-13, 43, 12, 13, 6), (4, 46, 16, 15, 8.5),
        (18, 39, 11, 12, 6), (34, 37, 13, 13, 7.5), (-33, 9, 12, 16, 6),
        (34, 6, 13, 15, 5.5), (-6, 33, 10, 9, 3), (10, 32, 9, 8, 3.6))):
        obj = BASE.ico("Deco_BackdropRocha%02d" % index, (x, y, height * .35),
            (width, depth, height * .70), mats["dark"], target, 4)
        for vertex in obj.data.vertices:
            co = vertex.co
            distortion = 1 + .15 * math.sin(co.x * .31 + co.z * .43) * math.cos(co.y * .27) + RNG.uniform(-.025, .025)
            vertex.co *= distortion
        for polygon in obj.data.polygons:
            polygon.use_smooth = True
        obj.rotation_euler.z = RNG.uniform(-.15, .15)
        rocks.append(obj)
    join_meshes(rocks, "Deco_BackdropEncostasRochosas")
    trees = []
    for index, (x, y, height) in enumerate(((-8, 24, 4.1), (11, 25, 4.7), (-18, 29, 5.1),
            (23, 27, 4.4), (-31, 20, 5.3), (31, 12, 4.3), (-26, 37, 5.2), (24, 40, 4.8))):
        before = set(target.objects)
        dead_tree(target, x, y, height, mats, "Deco_BackdropArvore%02d" % index)
        trees.extend(obj for obj in target.objects if obj not in before and obj.type == "MESH")
    join_meshes(trees, "Deco_BackdropArvoredoSeco")


def create_environment():
    target = BASE.new_collection(BASE.ENV_COLLECTION)
    mats = {
        "ground": mapped_material("MAT_Chao", "EnvironmentEarth"),
        "stone": mapped_material("MAT_Pedra", "EnvironmentStone", (.91, .94, .95)),
        "light": mapped_material("MAT_PedraClara", "EnvironmentStone", (1, 1, .98)),
        "dark": mapped_material("MAT_PedraEscura", "EnvironmentStone", (.59, .64, .67)),
        "path": mapped_material("MAT_Caminho", "EnvironmentPaving"),
        "grass": noisy_material("MAT_Musgo", (.12, .17, .10)),
        "bark": noisy_material("MAT_Tronco", (.13, .10, .075)),
        "metal": noisy_material("MAT_MetalEstandarte", (.16, .155, .13), .7, .58),
        "blue": noisy_material("MAT_NacaoAzul", (.023, .052, .083)),
        "wine": noisy_material("MAT_NacaoVermelha", (.11, .020, .029)),
    }
    block("Ground_Terreno35m", (0, 0, -.22), (35, 35, .37), mats["ground"], target, rough=0, bevel=0, flat_top=True)
    slabs = {key: [] for key in ("stone", "light", "dark", "path")}
    for row in range(-12, 17):
        col = -12
        while col <= 12:
            wide = RNG.random() < .22 and col < 11
            width = 1.36 if wide else .665
            x, y = col * .70 + (row % 2) * .35 + (width - .665) / 2, row * .76
            edge = abs(x) > 7.7 or y < -8.0 or y > 11.2
            if not (edge and RNG.random() < .26):
                key = RNG.choices(("path", "stone", "light", "dark"), (7, 2, 1, .5))[0]
                obj = polygon_slab("Ground_PlacaFraturada", (x, y, 0), width, .725,
                    mats[key], target, floor=True, rotation=RNG.uniform(-.012, .012))
                slabs[key].append(obj)
            col += 2 if wide else 1
    for key, pieces in slabs.items():
        join_meshes(pieces, "Ground_PavimentoGothico_" + key)
    gothic_arch(target, mats)
    # Ruined secondary arcade gives depth and an asymmetric skyline.
    gothic_arch(target, mats, center=(-9.7, 13.2), half=1.6, spring=2.8, rise=2.5, broken=True, suffix="Partida")
    for side in (-1, 1):
        broken_wall(target, mats, side, -2.0, 11.2)
        for y, height in ((-1.5, 4.1), (6.0, 4.9)):
            column(target, mats, side * 9.25, y, height, "Lateral")
        banner(target, side * 8.5, 8.0, mats["blue" if side < 0 else "wine"], mats)
        wrought_iron(target, mats, side)
        ivy(target, mats, side)
        near_ruins(target, mats, side)
    # Back wall retains generous passages under the portal and to each side.
    rear = []
    for side in (-1, 1):
        for row in range(7):
            for index in range(6):
                if row >= 4 and (index + row) % 5 == 1: continue
                x = side * (4.4 + index * 1.35)
                rear.append(block("Wall_RuinasFundo", (x, 13.0, .34 + row * .62), (1.28, 1.0, .58),
                    mats["stone" if index % 3 else "dark"], target, rough=.08, bevel=.035))
    join_meshes(rear, "Wall_FachadaPartida")
    scattered_masonry(target, mats)
    for index, (x, y, height) in enumerate(((-13, 7, 5.2), (13.7, 10, 5.8), (-13, -8, 4.7),
            (13.7, -8.0, 4.2), (-6, -13.5, 4.1), (9.8, 14.0, 5.1))):
        dead_tree(target, x, y, height, mats, "Deco_ArvoreRetorcida%02d" % index)
    for index in range(40):
        side = -1 if index % 2 else 1
        low_grass(target, side * RNG.uniform(8.4, 14.6), RNG.uniform(-11.5, 13.8), mats["grass"])
    distant_backdrop(target, mats)
    return target


def validate(target):
    bpy.context.view_layer.update()
    meshes = [obj for obj in target.objects if obj.type == "MESH"]
    floor = [obj for obj in meshes if obj.name.startswith("Ground_")]
    backdrop = [obj for obj in meshes if obj.name.startswith("Deco_Backdrop")]
    arena = next(obj for obj in floor if obj.name == "Ground_Terreno35m")
    for axis in (0, 1):
        positions = [(arena.matrix_world @ vertex.co)[axis] for vertex in arena.data.vertices]
        assert abs(max(positions) - min(positions) - 35) < .0001, "Arena deixou de medir35m"
    for obj in backdrop:
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            assert not (abs(point.x) < 17.499 and abs(point.y) < 17.499), ("Backdrop invadiu arena", obj.name, point)
    safe_centers = [(Vector((0, 0)), 6.0), (Vector((-1.8, 3.2)), 3.0),
                    (Vector((0, 4.4)), 3.0), (Vector((1.8, 3.2)), 3.0)]
    for obj in floor:
        top = max((obj.matrix_world @ vertex.co).z for vertex in obj.data.vertices)
        assert -.036 <= top <= .0001, (obj.name, top)
    for obj in meshes:
        if obj.name.startswith("Ground_"): continue
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            if point.z <= 2.5:
                assert all((Vector((point.x, point.y)) - center).length > radius for center, radius in safe_centers), (obj.name, point)
                assert not (-4.3 < point.x < 4.3 and -6.0 < point.y < 10.4), ("Corredor bloqueado", obj.name, point)
    for obj in meshes:
        if not obj.name.startswith("Deco_VegetacaoBaixa"):
            outward_normals(obj)
        metric_uv(obj)
        obj.data.calc_loop_triangles()
    for obj in floor:
        assert any(face.normal.z > .9 for face in obj.data.polygons), "Piso sem face superior: " + obj.name
    stats = {"malhas": len(meshes), "triangulos": sum(len(obj.data.loop_triangles) for obj in meshes),
        "piso_topo_m": 0, "dimensoes_m": [35, 35], "uv_por_metro": 1,
        "raio_livre_spawn_m": 6, "raio_livre_alvos_m": 3, "corredor_livre_blender": [-4.3, 4.3, -6, 10.4],
        "backdrop_malhas": len(backdrop), "backdrop_sem_colisao": "Deco_Backdrop*",
        "texturas": ["EnvironmentStone", "EnvironmentPaving", "EnvironmentEarth"]}
    assert len(meshes) <= 600 and stats["triangulos"] <= 250000, stats
    return stats


def preview(scene):
    # A stronger sidelight makes the actual geometric relief and material roughness inspectable.
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (.15, .19, .24, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .45
    bpy.ops.object.light_add(type="SUN", location=(2, -4, 10))
    sun = bpy.context.object
    sun.rotation_euler = (math.radians(37), math.radians(-18), math.radians(-32))
    sun.data.energy, sun.data.angle = 2.2, math.radians(4)
    sun.data.color = (.93, .96, 1.0)
    bpy.ops.object.light_add(type="AREA", location=(-7, -3, 8))
    fill = bpy.context.object
    fill.data.energy, fill.data.shape, fill.data.size = 950, "DISK", 8
    fill.data.color = (.60, .72, 1.0)
    fill.rotation_euler = (Vector((0, 3, 1)) - fill.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.camera_add(location=(13.5, -16.5, 12.0))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 5.0, 2.0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 29
    scene.camera = camera
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1100
    scene.render.filepath = str(HERE / "PreviewCenario.png")
    bpy.ops.render.render(write_still=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    scene = BASE.configure_scene()
    environment_surfaces()
    target = create_environment()
    bpy.context.view_layer.update()
    stats = validate(target)
    BASE.export_collection(target, BASE.UNITY_ART / "Cenario.fbx")
    preview(scene)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "Cenario.blend"))
    print("CENARIO_REFINADO_PRONTO", json.dumps(stats, ensure_ascii=False))


if __name__ == "__main__": main()
