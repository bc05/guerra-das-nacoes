"""Guerreiro original com armadura segmentada, espada e ataque de 0,4 s.

Blender --background --python-exit-code 1 --python gerar_personagem.py
Nao modifica o cenario ou o golem. Escala metrica, frente -Y, pes em Z=0.
"""

from __future__ import annotations

import importlib.util
import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector


HERE = Path(__file__).resolve().parent
ART = HERE.parent / "Unity" / "Assets" / "Art"
SPEC = importlib.util.spec_from_file_location("prototype_original_assets", HERE / "gerar_assets.py")
BASE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BASE)


def generate_character_textures():
    """Mapas originais, repetiveis, com gravura e malha; utilizaveis na Unity."""
    folder = ART / "Textures"
    folder.mkdir(parents=True, exist_ok=True)
    for kind, size in (("Armor", 2048), ("Mail", 1024)):
        paths = [folder / ("Character" + kind + "_" + channel + ".png")
                 for channel in ("Albedo", "Normal", "MetallicSmoothness")]
        if all(path.exists() for path in paths):
            continue
        rng = np.random.default_rng(81937 if kind == "Armor" else 9271)
        yy, xx = np.mgrid[:size, :size].astype(np.float32) / size
        grain = rng.normal(0, 1, (size, size)).astype(np.float32)
        if kind == "Armor":
            diagonal = np.abs((xx + yy - 0.5 + 0.5) % 1 - 0.5)
            reverse = np.abs((xx - yy + 0.5) % 1 - 0.5)
            border = np.minimum(np.abs(diagonal - 0.28), np.abs(reverse - 0.28))
            engraved = np.exp(-(border / 0.0028) ** 2)
            ornament = np.exp(-((np.sqrt((xx - 0.5) ** 2 + (yy - 0.5) ** 2) - 0.165) / 0.0025) ** 2)
            engraved = np.maximum(engraved, ornament * 0.8)
            scratches = np.maximum(0, np.sin(xx * 701 + np.sin(yy * 57) * 3) - 0.992)
            scratches *= np.maximum(0, np.sin(yy * 29 + xx * 11)) * 24
            weather = (np.sin(xx * 17 + np.sin(yy * 11)) * np.cos(yy * 19 - xx * 3))
            height = grain * 0.0018 - engraved * 0.035 + scratches * 0.003
            shade = np.clip(0.62 + grain * 0.016 + weather * 0.035 - engraved * 0.14 + scratches * 0.13, 0, 1)
            smoothness = np.clip(0.46 + grain * 0.018 - engraved * 0.10 - weather * 0.035, 0.26, 0.65)
            metallic = np.full_like(shade, 0.88)
            albedo_rgb = np.stack((shade * 0.97, shade, shade * 1.025), axis=-1)
        elif kind == "Mail":
            row = np.floor(yy * 14)
            u = (xx * 14 + (row % 2) * 0.5) % 1 - 0.5
            v = (yy * 14) % 1 - 0.5
            radial = np.sqrt((u * 1.05) ** 2 + (v * 1.25) ** 2)
            ring = np.exp(-((radial - 0.34) / 0.057) ** 2)
            height = ring * 0.08 + grain * 0.0015
            shade = np.clip(0.16 + ring * 0.38 + grain * 0.013, 0, 1)
            smoothness = np.clip(0.33 + ring * 0.10 + grain * 0.01, 0, 1)
            metallic = np.full_like(shade, 0.78)
            albedo_rgb = np.stack((shade * 0.97, shade, shade * 1.025), axis=-1)
        derivative_y, derivative_x = np.gradient(height)
        strength = 18 if kind == "Armor" else 6
        normal = np.stack((-derivative_x * strength, -derivative_y * strength, np.ones_like(height)), axis=-1)
        normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
        channels = (
            np.concatenate((albedo_rgb, np.ones((size, size, 1), np.float32)), axis=-1),
            np.concatenate((normal * 0.5 + 0.5, np.ones((size, size, 1), np.float32)), axis=-1),
            np.stack((metallic, np.zeros_like(shade), np.zeros_like(shade), smoothness), axis=-1),
        )
        for path, pixels in zip(paths, channels):
            image = bpy.data.images.new(path.stem, width=size, height=size, alpha=True)
            image.colorspace_settings.name = "sRGB" if path.stem.endswith("Albedo") else "Non-Color"
            image.pixels.foreach_set(np.clip(pixels, 0, 1).astype(np.float32).ravel())
            image.filepath_raw = str(path)
            image.file_format = "PNG"
            image.save()
            bpy.data.images.remove(image)
        print("CHARACTER_TEXTURES", kind, size)


def curved(name, sections, mat, target, sides=12, smooth=True):
    """Volume fechado em aneis elipticos: z, raio X, raio Y, deslocamento Y."""
    vertices = []
    for z, rx, ry, y in sections:
        vertices.extend((math.cos(i * math.tau / sides) * rx,
                         y + math.sin(i * math.tau / sides) * ry, z) for i in range(sides))
    faces = [tuple(range(sides - 1, -1, -1))]
    for ring in range(len(sections) - 1):
        for i in range(sides):
            a, b = ring * sides + i, ring * sides + (i + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces.append(tuple(range((len(sections) - 1) * sides, len(sections) * sides)))
    obj = BASE.mesh_object(name, vertices, faces, mat, target)
    if smooth:
        for polygon in obj.data.polygons[1:-1]:
            polygon.use_smooth = True
    return obj


def capsule(name, start, end, radius_x, radius_y, mat, target, taper=0.9, sides=12):
    start, end = Vector(start), Vector(end)
    length = (end - start).length
    obj = curved(name, [(0, radius_x, radius_y, 0),
                        (length * 0.12, radius_x * 1.05, radius_y * 1.05, 0),
                        (length * 0.87, radius_x * taper, radius_y * taper, 0),
                        (length, radius_x * taper * 0.86, radius_y * taper * 0.86, 0)],
                 mat, target, sides=sides)
    obj.location = start
    obj.rotation_euler = (end - start).to_track_quat("Z", "Y").to_euler()
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.select_set(False)
    return obj


def make_materials():
    definitions = {
        "blue": ("MAT_ArmaduraAzul", (0.055, 0.069, 0.081), 0.82, 0.50),
        "blue_light": ("MAT_AzulClaro", (0.13, 0.15, 0.17), 0.82, 0.48),
        "copper": ("MAT_Cobre", (0.18, 0.13, 0.078), 0.75, 0.52),
        "dark": ("MAT_TecidoEscuro", (0.025, 0.034, 0.043), 0.0, 0.94),
        "leather": ("MAT_Couro", (0.045, 0.022, 0.013), 0.0, 0.77),
        "skin": ("MAT_Pele", (0.38, 0.24, 0.16), 0.0, 0.78),
        "eyes": ("MAT_CharacterOlho", (0.16, 0.17, 0.16), 0.0, 0.55),
        "mail": ("MAT_CharacterMalha", (0.13, 0.15, 0.17), 0.72, 0.58),
        "steel": ("MAT_Aco", (0.22, 0.24, 0.25), 0.88, 0.46),
        "cyan": ("MAT_Visor", (0.035, 0.32, 0.38), 0.35, 0.45),
    }
    result = {}
    for key, (name, color, metallic, roughness) in definitions.items():
        mat = BASE.material(name, color, metallic, roughness)
        # Microvariacao original para a pre-visualizacao Blender. A Unity usa UV0
        # metrico e seus proprios mapas tiling, sem depender desses nodes.
        if key not in {"cyan", "skin", "eyes"}:
            nodes, links = mat.node_tree.nodes, mat.node_tree.links
            noise = nodes.new("ShaderNodeTexNoise")
            noise.inputs["Scale"].default_value = 185 if metallic else 95
            noise.inputs["Detail"].default_value = 2.2
            bump = nodes.new("ShaderNodeBump")
            bump.inputs["Strength"].default_value = 0.12 if metallic else 0.22
            bump.inputs["Distance"].default_value = 0.001 if metallic else 0.0017
            links.new(noise.outputs["Fac"], bump.inputs["Height"])
            links.new(bump.outputs["Normal"], nodes["Principled BSDF"].inputs["Normal"])
        if key in {"blue", "blue_light", "steel", "copper", "mail"}:
            nodes, links = mat.node_tree.nodes, mat.node_tree.links
            shader = nodes["Principled BSDF"]
            coordinates = nodes.new("ShaderNodeTexCoord")
            mapping = nodes.new("ShaderNodeMapping")
            repeat = 8 if key == "mail" else 4
            mapping.inputs["Scale"].default_value = (repeat, repeat, repeat)
            links.new(coordinates.outputs["UV"], mapping.inputs["Vector"])
            maps = {}
            for channel in ("Albedo", "Normal", "MetallicSmoothness"):
                kind = "Mail" if key == "mail" else "Armor"
                path = ART / "Textures" / ("Character" + kind + "_" + channel + ".png")
                node = nodes.new("ShaderNodeTexImage")
                node.image = bpy.data.images.load(str(path), check_existing=True)
                node.image.colorspace_settings.name = "sRGB" if channel == "Albedo" else "Non-Color"
                links.new(mapping.outputs["Vector"], node.inputs["Vector"])
                maps[channel] = node
            multiply = nodes.new("ShaderNodeMixRGB")
            multiply.blend_type = "MULTIPLY"
            multiply.inputs[0].default_value = 1
            multiply.inputs[2].default_value = (*[min(1, value * 3.0) for value in color], 1)
            links.new(maps["Albedo"].outputs["Color"], multiply.inputs[1])
            links.new(multiply.outputs[0], shader.inputs["Base Color"])
            normal = nodes.new("ShaderNodeNormalMap")
            normal.inputs["Strength"].default_value = 0.35 if key == "mail" else 0.085
            links.new(maps["Normal"].outputs["Color"], normal.inputs["Color"])
            links.new(normal.outputs[0], shader.inputs["Normal"])
            roughness_node = nodes.new("ShaderNodeMath")
            roughness_node.operation = "SUBTRACT"
            roughness_node.inputs[0].default_value = 1
            links.new(maps["MetallicSmoothness"].outputs["Alpha"], roughness_node.inputs[1])
            links.new(roughness_node.outputs[0], shader.inputs["Roughness"])
        result[key] = mat
    shader = result["cyan"].node_tree.nodes["Principled BSDF"]
    shader.inputs["Emission Color"].default_value = (0.035, 0.32, 0.38, 1)
    shader.inputs["Emission Strength"].default_value = 0.55
    return result


def build_character():
    target = BASE.new_collection(BASE.HERO_COLLECTION)
    mats, pieces = make_materials(), []

    def bind(obj, bone):
        return BASE.bind_rigid(obj, bone, pieces)

    def panel(name, vertices, faces, color, bone, smooth=False):
        obj = BASE.mesh_object(name, vertices, faces, mats[color], target)
        if smooth:
            for polygon in obj.data.polygons:
                polygon.use_smooth = True
        return bind(obj, bone)

    def rings(name, sections, color, bone, sides=32, x=0):
        obj = curved(name, sections, mats[color], target, sides)
        obj.location.x = x
        return bind(obj, bone)

    def tube(name, start, end, rx, ry, color, bone, taper=0.9, sides=20):
        if name.startswith(("Rerebrace", "Vambrace", "Cuissard", "GrevaAnatomica")):
            start, end = Vector(start), Vector(end)
            axis = (end - start).normalized()
            across = Vector((1, 0, 0))
            across = (across - axis * across.dot(axis)).normalized()
            front = axis.cross(across).normalized()
            # Geratrizes retas mantem as faces da chapa planas; nenhum perfil
            # de capsula ou barriga cilindrica aparece sob o reflexo do metal.
            sections = [(distance, 1 + (taper * 0.94 - 1) * distance)
                        for distance in (0, 0.08, 0.36, 0.64, 0.92, 1)]
            count, vertices = 16, []
            for distance, radius in sections:
                center = start.lerp(end, distance)
                for index in range(count):
                    angle = index * math.tau / count
                    ridge = max(0, math.sin(angle)) ** 12 * 0.011
                    vertices.append(tuple(center + across * math.cos(angle) * rx * radius +
                                          front * (math.sin(angle) * ry * radius + ridge)))
            faces = [tuple(reversed(range(count)))]
            for section in range(len(sections) - 1):
                for index in range(count):
                    a, b = section * count + index, section * count + (index + 1) % count
                    faces.append((a, b, b + count, a + count))
            faces.append(tuple(range((len(sections) - 1) * count, len(sections) * count)))
            obj = panel(name, vertices, faces, color, bone, smooth=True)
            obj.data.polygons[0].use_smooth = False
            obj.data.polygons[-1].use_smooth = False
            # Duas faces centrais definem a crista rigida em vez de um tubo liso.
            for section in range(len(sections) - 1):
                for index in (3, 4):
                    obj.data.polygons[1 + section * count + index].use_smooth = False
            return obj
        return bind(capsule(name, start, end, rx, ry, mats[color], target, taper, sides), bone)

    def box(name, at, size, color, bone, bevel=0.006, rotation=None):
        bpy.ops.mesh.primitive_cube_add(size=1, location=at)
        obj = bpy.context.object
        obj.name = name
        obj.dimensions = size
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        if bevel:
            modifier = obj.modifiers.new("Arestas arredondadas de chapa", "BEVEL")
            modifier.width = bevel
            modifier.segments = 3
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.data.materials.append(mats[color])
        BASE.move_to_collection(obj, target)
        if rotation:
            obj.rotation_euler = rotation
        return bind(obj, bone)

    def line(name, points, radius, color, bone, sides=7):
        points = [Vector(point) for point in points]
        vertices = []
        for index, point in enumerate(points):
            tangent = points[min(index + 1, len(points) - 1)] - points[max(index - 1, 0)]
            tangent.normalize()
            first = tangent.cross(Vector((0, 1, 0)))
            if first.length < 0.01:
                first = tangent.cross(Vector((1, 0, 0)))
            first.normalize()
            second = tangent.cross(first).normalized()
            vertices.extend(tuple(point + radius * (first * math.cos(i * math.tau / sides) +
                                                    second * math.sin(i * math.tau / sides))) for i in range(sides))
        faces = [tuple(range(sides - 1, -1, -1))]
        for ring in range(len(points) - 1):
            for i in range(sides):
                faces.append((ring * sides + i, ring * sides + (i + 1) % sides,
                              (ring + 1) * sides + (i + 1) % sides, (ring + 1) * sides + i))
        faces.append(tuple(range((len(points) - 1) * sides, len(points) * sides)))
        return panel(name, vertices, faces, color, bone, smooth=True)

    def stud(name, at, bone, radius=0.004, color="copper"):
        obj = BASE.ico(name, at, (radius, radius * 0.7, radius), mats[color], target, 2)
        for polygon in obj.data.polygons:
            polygon.use_smooth = True
        return bind(obj, bone)

    def shield(name, center, width, height, color, bone, depth=0.018):
        cx, cy, cz = center
        outline = [(-0.43, 0.45), (0.43, 0.45), (0.52, 0.13), (0.38, -0.25),
                   (0, -0.55), (-0.38, -0.25), (-0.52, 0.13)]
        count = len(outline)
        # Aneis convexos formam chapa estampada arredondada; evitam a piramide
        # facetada que faria joelheiras, cotovelos e viseira parecerem cristais.
        vertices = []
        for scale, bulge in ((1.0, 0.0), (0.70, 0.65), (0.35, 0.95)):
            vertices += [(cx + x * width * scale, cy - depth * bulge,
                          cz + z * height * scale + height * 0.03 * (1 - scale)) for x, z in outline]
        center_index = len(vertices)
        vertices.append((cx, cy - depth, cz + height * 0.03))
        back_index = len(vertices)
        vertices += [(cx + x * width, cy + 0.007, cz + z * height) for x, z in outline]
        faces = []
        for ring in range(2):
            first = ring * count
            faces += [(first + i, first + count + i, first + count + (i + 1) % count,
                       first + (i + 1) % count) for i in range(count)]
        faces += [(2 * count + i, center_index, 2 * count + (i + 1) % count) for i in range(count)]
        faces += [tuple(range(back_index, back_index + count))]
        faces += [(i, (i + 1) % count, back_index + (i + 1) % count, back_index + i) for i in range(count)]
        obj = panel(name, vertices, faces, color, bone, smooth=True)
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new("Borda forjada", "BEVEL")
        modifier.width = 0.0015
        modifier.segments = 2
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = 0.5
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
        return obj

    def pauldron(name, center, rx, ry, rise, color, bone):
        cx, cy, cz = center
        side = -1 if cx < 0 else 1
        contour = [(1.20, 0), (0.72, 0.83), (-0.28, 1), (-0.92, 0.55),
                   (-0.95, -0.25), (-0.50, -0.92), (0.42, -1), (1.03, -0.52)]
        vertices = [(cx - side * 0.008, cy, cz + rise)]
        for radial in (0.25, 0.53, 0.78, 1.0):
            for x, y in contour:
                z = cz + rise * (1 - radial ** 1.7) + max(0, x) * radial ** 3 * 0.006
                vertices.append((cx + side * x * rx * radial, cy + y * ry * radial, z))
        upper = len(vertices)
        vertices += [(x, y, z - 0.008) for x, y, z in vertices]
        faces = [(0, 1 + i, 1 + (i + 1) % 8) for i in range(8)]
        for ring in range(3):
            first = 1 + ring * 8
            faces += [(first + i, first + 8 + i, first + 8 + (i + 1) % 8,
                       first + (i + 1) % 8) for i in range(8)]
        faces += [tuple(upper + i for i in reversed(face)) for face in faces[:]]
        last = 25
        faces += [(last + i, last + (i + 1) % 8, upper + last + (i + 1) % 8,
                   upper + last + i) for i in range(8)]
        if side < 0:
            faces = [tuple(reversed(face)) for face in faces]
        result = panel(name, vertices, faces, color, bone, smooth=True)
        perimeter = [vertices[last + i % 8] for i in range(9)]
        line(name + "_BordaPrata", perimeter, 0.0028, "steel", bone)
        line(name + "_CristaForjada", [(cx - side * 0.05, cy - 0.008, cz + rise * 0.55),
                                       (cx + side * 0.015, cy - 0.008, cz + rise + 0.002),
                                       (cx + side * rx * 1.16, cy - 0.008, cz + 0.008)],
             0.0025, "copper", bone)
        return result

    def cloth(name, x_min, x_max, top, bottom, front, bone, folds=6, color="dark"):
        columns, rows = 36, 20
        vertices = []
        for row in range(rows + 1):
            vertical = row / rows
            width_scale = 0.78 + 0.28 * vertical
            for column in range(columns + 1):
                horizontal = column / columns
                x = (x_min + (x_max - x_min) * horizontal) * width_scale
                fold = (0.007 + 0.020 * vertical) * math.sin(horizontal * folds * math.tau + vertical * 0.7)
                fold += 0.006 * math.sin(horizontal * 3 * math.tau + vertical * 2.3)
                hem = 0.012 * math.cos(horizontal * 5 * math.pi) + 0.007 * math.sin(horizontal * 9 * math.pi)
                hem -= max(0, math.sin(horizontal * 19 * math.pi)) ** 16 * 0.012
                neckline = 0.035 * (1 - abs(2 * horizontal - 1)) * (1 - vertical)
                z = top + (bottom - top) * vertical + hem * vertical ** 4 - neckline
                y = front + (0.10 * vertical + 0.025 * math.sin(vertical * math.pi) if front > 0 else -0.018 * vertical) + fold
                vertices.append((x, y, z))
        half = len(vertices)
        vertices += [(x, y + 0.004, z) for x, y, z in vertices]
        faces = []
        for row in range(rows):
            for column in range(columns):
                a = row * (columns + 1) + column
                face = (a, a + 1, a + columns + 2, a + columns + 1)
                faces += [face, tuple(half + index for index in reversed(face))]
        # Espessura e bainha preservam a capa quando vista pelas costas.
        for row in range(rows):
            for column in (0, columns):
                a, b = row * (columns + 1) + column, (row + 1) * (columns + 1) + column
                faces.append((a, half + a, half + b, b))
        for column in range(columns):
            a = rows * (columns + 1) + column
            faces.append((a, a + 1, half + a + 1, half + a))
        panel(name, vertices, faces, color, bone, smooth=True)
        hem_points = [vertices[rows * (columns + 1) + column] for column in range(columns + 1)]
        line(name + "_BainhaCosturada", hem_points, 0.0023, "leather", bone)

    # Volumes de torso com perfil anatomico; chapa continua, sem ombros em esfera.
    rings("GambesaoCintura", [(0.94, 0.145, 0.100, 0), (1.04, 0.150, 0.115, 0),
                              (1.16, 0.155, 0.120, 0), (1.31, 0.205, 0.135, 0),
                              (1.43, 0.210, 0.120, 0)], "dark", "Spine")
    torso = [(1.10, 0.153, 0.124, 0), (1.16, 0.163, 0.130, 0),
             (1.23, 0.190, 0.144, 0), (1.32, 0.218, 0.155, 0),
             (1.40, 0.225, 0.145, 0), (1.455, 0.195, 0.124, 0),
             (1.49, 0.128, 0.106, 0)]
    build_sculpted_torso(torso, panel, line)
    for index, (z, rx, ry) in enumerate(((1.08, 0.158, 0.127), (1.12, 0.163, 0.132), (1.16, 0.174, 0.138))):
        rings("LaminaAbdominal.%d" % index,
              [(z, rx, ry, 0), (z + 0.032, rx + 0.004, ry + 0.002, 0)], "blue", "Spine")
        line("ViradaAbdominal", [(math.cos(i * math.pi / 24) * rx,
                                  -math.sin(i * math.pi / 24) * (ry + 0.002), z + 0.003)
                                 for i in range(25)], 0.002, "blue_light", "Spine")
    rings("Gorjal", [(1.484, 0.119, 0.105, 0.005), (1.52, 0.104, 0.091, 0.005),
                     (1.564, 0.090, 0.080, 0.010)], "blue", "Chest", sides=36)
    rings("ViraGorjal", [(1.552, 0.091, 0.081, 0.010), (1.563, 0.092, 0.082, 0.010)],
          "steel", "Chest")
    rings("PescocoProtegido", [(1.53, 0.074, 0.068, 0.005), (1.615, 0.074, 0.070, 0.005)], "mail", "Head")

    rings("CintoCouroGrosso", [(1.061, 0.165, 0.133, 0), (1.105, 0.165, 0.133, 0)], "leather", "Pelvis", sides=48)
    for side in (-1, 1):
        box("FivelaLateral", (side * 0.043, -0.139, 1.084), (0.007, 0.014, 0.052), "copper", "Pelvis", 0.002)
        box("FivelaHorizontal", (0, -0.139, 1.084 + side * 0.022), (0.086, 0.014, 0.007), "copper", "Pelvis", 0.002)
        box("PassadorCouro", (side * 0.126, -0.105, 1.084), (0.016, 0.018, 0.05), "leather", "Pelvis", 0.003)
    box("LinguetaFivela", (0, -0.145, 1.084), (0.006, 0.012, 0.040), "steel", "Pelvis", 0.0015)
    # Pequena bolsa de couro, deslocada para a esquerda: sem volumes simetricos de brinquedo.
    box("BolsaUtilitaria", (-0.19, 0.025, 0.99), (0.072, 0.105, 0.124), "leather", "Pelvis", 0.024)
    box("AbaBolsa", (-0.19, -0.029, 1.025), (0.075, 0.012, 0.058), "leather", "Pelvis", 0.014)
    stud("FechoBolsa", (-0.19, -0.038, 1.014), "Pelvis", 0.005)
    cloth("SaiaGambesaoEsquerda", -0.185, -0.035, 1.047, 0.857, -0.105, "Pelvis", folds=3)
    cloth("SaiaGambesaoDireita", 0.035, 0.185, 1.047, 0.857, -0.105, "Pelvis", folds=3)
    for suffix, side in (("L", -1), ("R", 1)):
        for index in range(3):
            shield("Tasset.%s.%d" % (suffix, index),
                   (side * (0.16 + index * 0.008), -0.095, 1.033 - 0.044 * index),
                   0.105, 0.064, "blue", "Pelvis", depth=0.014)

    build_human_head(panel, line, stud, rings, box, bind, mats, target)

    # Capa unica com pregas continuas e bainha curva, sem placas retangulares separadas.
    cloth("CapaDeLaComPregas", -0.27, 0.27, 1.47, 0.87, 0.153, "Chest", folds=7)
    for side in (-1, 1):
        line("CorreiaCapa", [(side * 0.155, 0.12, 1.461), (side * 0.162, 0.025, 1.478),
                             (side * 0.158, -0.091, 1.455)], 0.010, "leather", "Chest", sides=10)
        stud("BrocheCapa", (side * 0.158, -0.107, 1.449), "Chest", 0.010)

    for suffix, side in (("L", -1), ("R", 1)):
        upper, fore, hand = "UpperArm." + suffix, "Forearm." + suffix, "Hand." + suffix
        thigh, shin, foot = "Thigh." + suffix, "Shin." + suffix, "Foot." + suffix
        tube("MangaAcolchoada." + suffix, (side * 0.275, 0, 1.41), (side * 0.423, 0, 1.16),
             0.061, 0.066, "mail", upper, taper=0.88, sides=28)
        for index in range(3):
            pauldron("OmbreiraArticulada.%s.%d" % (suffix, index),
                      (side * (0.289 + 0.042 * index), 0.0, 1.461 - 0.054 * index),
                      0.100 - 0.022 * index, 0.125 - 0.014 * index, 0.059 - 0.013 * index,
                      "blue", upper)
        tube("Rerebrace." + suffix, (side * 0.357, 0, 1.30), (side * 0.408, 0, 1.192),
             0.071, 0.075, "blue", upper, taper=0.86, sides=32)
        # Cotovelos e joelhos articulados com couters em chapa, nao esferas.
        tube("JuntaCotoveloTecido." + suffix, (side * 0.417, 0, 1.18), (side * 0.440, 0, 1.108),
             0.060, 0.064, "mail", fore, taper=0.96, sides=24)
        shield("Couter." + suffix, (side * 0.431, -0.060, 1.141), 0.108, 0.101, "blue_light", fore, depth=0.020)
        tube("AntebracoCouro." + suffix, (side * 0.439, 0, 1.11), (side * 0.49, -0.005, 0.948),
             0.058, 0.064, "leather", fore, taper=0.77, sides=28)
        tube("Vambrace." + suffix, (side * 0.444, -0.004, 1.089), (side * 0.483, -0.005, 0.962),
             0.067, 0.070, "blue", fore, taper=0.75, sides=32)
        tube("ViraPunho." + suffix, (side * 0.483, -0.005, 0.973), (side * 0.487, -0.005, 0.957),
             0.055, 0.059, "steel", fore, taper=1.0, sides=24)
        line("GravacaoVambrace." + suffix,
             [(side * 0.452, -0.072, 1.070), (side * 0.466, -0.070, 1.030), (side * 0.479, -0.063, 0.990)],
             0.0015, "copper", fore)
        # Mao proporcional, luva de couro, placas do dorso e quatro dedos separados.
        box("PalmaLuva." + suffix, (side * 0.49, 0.0, 0.909), (0.068, 0.070, 0.085),
            "leather", hand, bevel=0.023)
        shield("DorsoManopla." + suffix, (side * 0.49, -0.043, 0.922), 0.072, 0.073, "steel", hand, depth=0.006)
        for finger in range(4):
            x = side * 0.49 + (finger - 1.5) * 0.015
            box("DedoLuva.%s.%d" % (suffix, finger), (x, -0.022, 0.867), (0.013, 0.037, 0.034),
                "leather", hand, bevel=0.006)
            box("LaminaDedo.%s.%d" % (suffix, finger), (x, -0.045, 0.868), (0.014, 0.007, 0.022),
                "blue_light", hand, bevel=0.0025)
        tube("Polegar." + suffix, (side * 0.453, -0.001, 0.916), (side * 0.45, -0.039, 0.878),
             0.014, 0.016, "leather", hand, taper=0.80, sides=16)

        tube("CalcaCoxa." + suffix, (side * 0.135, 0, 0.935), (side * 0.145, 0, 0.56),
             0.075, 0.081, "dark", thigh, taper=0.79, sides=32)
        tube("Cuissard." + suffix, (side * 0.139, -0.005, 0.867), (side * 0.145, -0.008, 0.610),
             0.088, 0.096, "blue", thigh, taper=0.81, sides=36)
        line("NervuraCuissard." + suffix, [(side * 0.142, -0.111, 0.842),
                                           (side * 0.145, -0.105, 0.739), (side * 0.145, -0.093, 0.627)],
             0.0025, "blue_light", thigh)
        for z in (0.67, 0.80):
            tube("CorreiaCoxa." + suffix, (side * 0.14, 0.0, z), (side * 0.14, 0.0, z + 0.018),
                 0.096 if z > 0.7 else 0.084, 0.104 if z > 0.7 else 0.094,
                 "leather", thigh, taper=1.0, sides=32)
        tube("JuntaJoelhoTecido." + suffix, (side * 0.145, 0, 0.581), (side * 0.145, 0, 0.495),
             0.070, 0.076, "mail", shin, taper=1.0, sides=28)
        shield("Poleyn." + suffix, (side * 0.145, -0.075, 0.542), 0.139, 0.135, "blue_light", shin, depth=0.024)
        for z in (0.475, 0.592):
            shield("LaminaPoleyn." + suffix, (side * 0.145, -0.070, z), 0.12, 0.045,
                   "blue", shin, depth=0.011)
        tube("BotaAltaCouro." + suffix, (side * 0.145, 0.006, 0.466), (side * 0.145, 0.004, 0.15),
             0.058, 0.063, "leather", shin, taper=0.70, sides=32)
        tube("GrevaAnatomica." + suffix, (side * 0.145, -0.010, 0.457), (side * 0.145, -0.012, 0.177),
             0.081, 0.094, "blue", shin, taper=0.68, sides=40)
        line("NervuraGreva." + suffix, [(side * 0.145, -0.107, 0.432),
                                       (side * 0.145, -0.092, 0.31), (side * 0.145, -0.076, 0.198)],
             0.003, "steel", shin)
        for z, radius in ((0.432, 0.088), (0.205, 0.066)):
            tube("CorreiaGreva." + suffix, (side * 0.145, 0.0, z), (side * 0.145, 0.0, z + 0.015),
                 radius, radius + 0.012, "leather", shin, taper=1.0, sides=32)
        # Sapato e sola modelados em secoes; biqueira afilada e soleretes sobrepostos.
        foot_sections = [(-0.276, 0.048, 0.055), (-0.249, 0.066, 0.085),
                         (-0.17, 0.084, 0.115), (-0.08, 0.080, 0.143),
                         (0.035, 0.069, 0.163), (0.075, 0.058, 0.140)]
        for name, color, bottom, thickness in (("SapatoCouro", "leather", 0.024, 1.0),
                                                ("SolaCosturada", "dark", 0.0, 0.19)):
            vertices = []
            for y, width, height in foot_sections:
                vertices.extend((side * 0.145 + width * math.cos(math.pi * index / 12), y,
                                 bottom + (height * thickness) * math.sin(math.pi * index / 12)) for index in range(13))
            faces = []
            for row in range(len(foot_sections) - 1):
                for index in range(12):
                    a = row * 13 + index
                    faces.append((a, a + 13, a + 14, a + 1))
                faces.append((row * 13, row * 13 + 12, (row + 1) * 13 + 12, (row + 1) * 13))
            faces += [tuple(reversed(range(13))), tuple(range((len(foot_sections) - 1) * 13, len(foot_sections) * 13))]
            panel(name + "." + suffix, vertices, faces, color, foot, smooth=True)
        for index in range(4):
            center_y = -0.232 + index * 0.045
            width = 0.062 + index * 0.004
            base_z = 0.040 + index * 0.020
            vertices = []
            for offset in (-0.028, 0.028):
                vertices.extend((side * 0.145 + width * math.cos(math.pi * k / 16), center_y + offset,
                                 base_z + (0.085 + index * 0.002) * math.sin(math.pi * k / 16)) for k in range(17))
            faces = [(k, k + 17, k + 18, k + 1) for k in range(16)]
            faces += [(0, 16, 33, 17), tuple(reversed(range(17))), tuple(range(17, 34))]
            panel("Solerete.%s.%d" % (suffix, index), vertices, faces, "blue_light", foot, smooth=True)
        toe_sections = [(-0.286, 0.052, 0.079), (-0.268, 0.065, 0.097),
                        (-0.236, 0.073, 0.120), (-0.204, 0.077, 0.132)]
        toe_vertices = []
        for y, width, height in toe_sections:
            toe_vertices.extend((side * 0.145 + width * math.cos(math.pi * k / 16), y,
                                 0.022 + height * math.sin(math.pi * k / 16)) for k in range(17))
        toe_faces = []
        for section in range(len(toe_sections) - 1):
            toe_faces += [(section * 17 + k, (section + 1) * 17 + k,
                           (section + 1) * 17 + k + 1, section * 17 + k + 1) for k in range(16)]
        toe_faces += [tuple(reversed(range(17))), tuple(range(51, 68)), (0, 16, 67, 51)]
        panel("BiqueiraIntegralDeAco." + suffix, toe_vertices, toe_faces, "blue_light", foot, smooth=True)
        for at, bone in (((side * 0.345, -0.110, 1.418), upper),
                         ((side * 0.45, -0.072, 1.064), fore),
                         ((side * 0.145, -0.106, 0.542), shin)):
            stud("RebiteArmadura", at, bone)

    guard, tip = build_sword(tube, line, panel, stud)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in pieces:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    mesh = bpy.context.object
    mesh.name = "HeroMesh"
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    # Um UV por metro: mapas tiling sao consistentes entre todas as partes e nao
    # dependem dos UVs heterogeneos das primitivas antes de serem combinadas.
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.0, correct_aspect=False, clip_to_bounds=False, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    mesh.data.uv_layers.active.name = "UV0_Metrico_1m"
    rig = create_rig(target)
    modifier = mesh.modifiers.new("HeroRig", "ARMATURE")
    modifier.object = rig
    mesh.parent = rig
    rig.show_in_front = True
    bpy.context.view_layer.update()
    for marker_name, marker_position in (("SwordBase", guard), ("SwordTip", tip)):
        marker = bpy.data.objects.new(marker_name, None)
        target.objects.link(marker)
        marker.parent, marker.parent_type, marker.parent_bone = rig, "BONE", "Hand.R"
        bpy.context.view_layer.update()
        marker.matrix_world = Matrix.Translation(marker_position)
        bpy.context.view_layer.update()
        assert (marker.matrix_world.translation - marker_position).length < 0.0001
    create_animations(rig)
    return target, rig, mesh


def build_sculpted_torso(profile, panel, line):
    def radii(z):
        for lower, upper in zip(profile, profile[1:]):
            if lower[0] <= z <= upper[0]:
                fraction = (z - lower[0]) / (upper[0] - lower[0])
                return (lower[1] * (1 - fraction) + upper[1] * fraction,
                        lower[2] * (1 - fraction) + upper[2] * fraction)
        return profile[-1][1:3]

    def relief(x, z):
        pectorals = 0.012 * math.exp(-((abs(x) - 0.105) / 0.075) ** 2 - ((z - 1.365) / 0.067) ** 2)
        sternum = 0.011 * math.exp(-(x / 0.027) ** 2 - ((z - 1.325) / 0.15) ** 2)
        rib_seam = -0.010 * math.exp(-((z - 1.255) / 0.023) ** 2) * (1 - math.exp(-(x / 0.05) ** 2))
        return pectorals + sternum + rib_seam

    def front_y(x, z):
        rx, ry = radii(z)
        return -ry * math.sqrt(max(0.05, 1 - (x / rx) ** 2)) - relief(x, z) - 0.001

    vertices, faces = [], []
    rows, sides = 32, 64
    for row in range(rows + 1):
        z = profile[0][0] + (profile[-1][0] - profile[0][0]) * row / rows
        rx, ry = radii(z)
        for index in range(sides):
            angle = index * math.tau / sides
            x, y = rx * math.cos(angle), ry * math.sin(angle)
            y -= max(0, -math.sin(angle)) ** 3 * relief(x, z)
            vertices.append((x, y, z))
    for row in range(rows):
        for index in range(sides):
            a, b = row * sides + index, row * sides + (index + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces += [tuple(reversed(range(sides))), tuple(range(rows * sides, (rows + 1) * sides))]
    panel("CouracaEsculturalAnatomica", vertices, faces, "blue", "Chest", smooth=True)
    for side in (-1, 1):
        # Relevos seguem as curvas da chapa: a borda tem massa, a gravura fica fina.
        for offset in (0, 0.008):
            coordinates = [(0.025, 1.43), (0.075, 1.405), (0.13, 1.382),
                           (0.18, 1.35), (0.148, 1.316), (0.085, 1.282)]
            points = [(side * x, front_y(x, z - offset), z - offset) for x, z in coordinates]
            line("BrasaoDeAsasGravadas", points, 0.0028 if offset == 0 else 0.0011,
                 "blue_light" if offset == 0 else "copper", "Chest")
        line("FrisoLateralCouraca", [(side * x, front_y(x, z), z)
                                      for x, z in ((0.175, 1.42), (0.215, 1.37), (0.21, 1.30), (0.17, 1.235))],
             0.003, "steel", "Chest")
    line("CristaEsterno", [(0, front_y(0, z), z) for z in (1.18, 1.26, 1.34, 1.43)],
         0.0035, "steel", "Chest")
    line("SeloEnergiaCouraca", [(0, front_y(0, 1.374) - 0.001, 1.374),
                                (0, front_y(0, 1.397) - 0.001, 1.397)],
         0.0018, "cyan", "Chest")


def build_human_head(panel, line, stud, rings, box, bind, mats, target):
    """Rosto adulto modelado por marcos anatomicos, enquadrado por um elmo aberto."""
    profiles = [(1.625, 0.027, 0.048), (1.643, 0.049, 0.062),
                (1.661, 0.063, 0.068), (1.686, 0.072, 0.069),
                (1.711, 0.078, 0.072), (1.741, 0.081, 0.076),
                (1.765, 0.081, 0.078), (1.792, 0.078, 0.077),
                (1.814, 0.060, 0.065), (1.830, 0.025, 0.032)]

    def radii(z):
        for a, b in zip(profiles, profiles[1:]):
            if a[0] <= z <= b[0]:
                t = (z - a[0]) / (b[0] - a[0])
                return a[1] * (1 - t) + b[1] * t, a[2] * (1 - t) + b[2] * t
        return profiles[-1][1:]

    def features(x, z):
        nose = 0.038 * math.exp(-(x / 0.012) ** 2 - ((z - 1.718) / 0.017) ** 2)
        nose += 0.021 * math.exp(-(x / 0.009) ** 2 - ((z - 1.748) / 0.025) ** 2)
        brows = 0.009 * math.exp(-((abs(x) - 0.033) / 0.019) ** 2 - ((z - 1.761) / 0.008) ** 2)
        sockets = -0.008 * math.exp(-((abs(x) - 0.032) / 0.016) ** 2 - ((z - 1.746) / 0.008) ** 2)
        cheeks = 0.007 * math.exp(-((abs(x) - 0.047) / 0.022) ** 2 - ((z - 1.721) / 0.013) ** 2)
        lips = 0.011 * math.exp(-(x / 0.025) ** 2 - ((z - 1.684) / 0.008) ** 2)
        chin = 0.011 * math.exp(-(x / 0.038) ** 2 - ((z - 1.648) / 0.015) ** 2)
        return nose + brows + sockets + cheeks + lips + chin

    def surface_y(x, z):
        rx, ry = radii(z)
        front = math.sqrt(max(0.02, 1 - (x / rx) ** 2))
        return 0.006 - ry * front - features(x, z) * front ** 6

    vertices, faces = [], []
    rows, sides = 60, 64
    for row in range(rows + 1):
        z = 1.625 + (1.830 - 1.625) * row / rows
        rx, ry = radii(z)
        for index in range(sides):
            angle = index * math.tau / sides
            x, y = rx * math.cos(angle), 0.006 + ry * math.sin(angle)
            y -= max(0, -math.sin(angle)) ** 6 * features(x, z)
            vertices.append((x, y, z))
    for row in range(rows):
        for index in range(sides):
            a, b = row * sides + index, row * sides + (index + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces += [tuple(reversed(range(sides))), tuple(range(rows * sides, (rows + 1) * sides))]
    panel("RostoAdultoAnatomico", vertices, faces, "skin", "Head", smooth=True)

    def ellipsoid(name, at, scale, color):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1, location=at)
        obj = bpy.context.object
        obj.name, obj.scale = name, scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        obj.data.materials.append(mats[color])
        for polygon in obj.data.polygons:
            polygon.use_smooth = True
        BASE.move_to_collection(obj, target)
        return bind(obj, "Head")

    for side in (-1, 1):
        eye_x, eye_z = side * 0.031, 1.746
        eye_y = surface_y(eye_x, eye_z) - 0.0015
        ellipsoid("EscleraOlho", (eye_x, eye_y, eye_z), (0.011, 0.0038, 0.0046), "eyes")
        ellipsoid("IrisPupila", (eye_x, eye_y - 0.0038, eye_z), (0.0035, 0.0015, 0.0035), "dark")
        brow = [(side * x, surface_y(side * x, z) - 0.001, z)
                for x, z in ((0.017, 1.759), (0.030, 1.762), (0.045, 1.759), (0.053, 1.754))]
        line("Sobrancelha", brow, 0.0019, "leather", "Head", sides=8)
        upper_lid = [(eye_x + side * offset, eye_y - 0.0007, eye_z + height)
                     for offset, height in ((-0.011, 0), (-0.004, 0.0046), (0.005, 0.0042), (0.011, 0))]
        line("PalpebraSuperior", upper_lid, 0.0010, "skin", "Head", sides=7)
        nostril_x, nostril_z = side * 0.0075, 1.710
        ellipsoid("Narina", (nostril_x, surface_y(nostril_x, nostril_z) - 0.0005, nostril_z),
                  (0.003, 0.0014, 0.0016), "leather")
        ellipsoid("Orelha", (side * 0.077, 0.003, 1.721), (0.009, 0.012, 0.020), "skin")
    mouth = [(x, surface_y(x, 1.682) - 0.0005, 1.682 + abs(x) * 0.04) for x in (-0.023, -0.012, 0, 0.012, 0.023)]
    line("LinhaDosLabios", mouth, 0.0009, "leather", "Head", sides=7)

    # Panos angulares do elmo deixam rosto e maxilar visiveis. A altura total fica 1,85 m.
    sides = 24
    helmet_vertices = []
    for ring in range(4):
        for index in range(sides):
            angle = index * math.tau / sides
            if ring == 0:
                z = 1.645 + 0.15 * max(0, -math.sin(angle))
                rx, ry = 0.091, 0.088
            else:
                z, rx, ry = ((1.805, 0.087, 0.090), (1.834, 0.059, 0.067), (1.850, 0.004, 0.008))[ring - 1]
            helmet_vertices.append((rx * math.cos(angle), 0.019 + ry * math.sin(angle), z))
    helmet_faces = []
    for ring in range(3):
        for index in range(sides):
            a, b = ring * sides + index, ring * sides + (index + 1) % sides
            helmet_faces.append((a, b, b + sides, a + sides))
    helmet_faces.append(tuple(range(3 * sides, 4 * sides)))
    panel("ElmoAbertoAngular", helmet_vertices, helmet_faces, "blue", "Head", smooth=False)
    line("SobrancelhaDeAcoDoElmo", [(-0.085, -0.027, 1.765), (-0.062, -0.060, 1.788),
                                     (0, -0.074, 1.801), (0.062, -0.060, 1.788), (0.085, -0.027, 1.765)],
         0.0036, "steel", "Head")
    line("CristaAngularElmo", [(0, -0.073, 1.801), (0, -0.020, 1.846),
                               (0, 0.065, 1.833), (0, 0.107, 1.791)], 0.0035, "copper", "Head")
    for side in (-1, 1):
        cheek = [(side * 0.091, -0.014, 1.755), (side * 0.088, -0.057, 1.735),
                 (side * 0.068, -0.061, 1.662), (side * 0.059, -0.024, 1.644),
                 (side * 0.084, 0.010, 1.681)]
        count = len(cheek)
        cheek += [(x + side * 0.006, y + 0.004, z) for x, y, z in cheek]
        faces = [tuple(range(count)), tuple(reversed(range(count, count * 2)))]
        faces += [(i, count + i, count + (i + 1) % count, (i + 1) % count) for i in range(count)]
        if side < 0:
            faces = [tuple(reversed(face)) for face in faces]
        panel("GuardaDeMandibula", cheek, faces, "blue_light", "Head", smooth=False)
        line("FrisoMandibula", cheek[:4], 0.0025, "steel", "Head")
        stud("EixoElmo", (side * 0.094, -0.005, 1.754), "Head", 0.0045)
    line("RunaElmo", [(0, -0.076, 1.802), (0, -0.064, 1.815)], 0.0014, "cyan", "Head")
    build_closed_visor(panel, line, box)


def build_closed_visor(panel, line, box):
    """Viseira de planos forjados e fendas em V, sem lentes ou luz nos olhos."""
    sections = [(1.623, 0.045, -0.090), (1.646, 0.067, -0.125),
                (1.683, 0.083, -0.145), (1.712, 0.087, -0.163),
                (1.736, 0.085, -0.140), (1.745, 0.082, -0.135),
                (1.754, 0.083, -0.130), (1.776, 0.086, -0.100), (1.792, 0.075, -0.092)]
    columns = (-1, -0.75, -0.55, -0.20, 0, 0.20, 0.55, 0.75, 1)
    vertices = []
    for z, radius, nose in sections:
        for fraction in columns:
            y = nose + abs(fraction) * 0.066
            vertices.append((fraction * radius, y, z + abs(fraction) * 0.006))
    count = len(vertices)
    vertices += [(x, y + 0.005, z) for x, y, z in vertices]
    front_faces = []
    for row in range(len(sections) - 1):
        for column in range(len(columns) - 1):
            if row == 5 and column in (1, 2, 5, 6):
                continue
            a = row * len(columns) + column
            front_faces.append((a, a + 1, a + 1 + len(columns), a + len(columns)))
    edges = {}
    for face in front_faces:
        for a, b in zip(face, face[1:] + face[:1]):
            edge = tuple(sorted((a, b)))
            edges[edge] = edges.get(edge, 0) + 1
    faces = front_faces + [tuple(count + i for i in reversed(face)) for face in front_faces]
    faces += [(a, count + a, count + b, b) for (a, b), uses in edges.items() if uses == 1]
    panel("ViseiraGuerreiroDePlanosAngulares", vertices, faces, "blue", "Head", smooth=False)
    shadow_vertices, shadow_faces = [], []
    for column in (1, 2, 5, 6):
        a = 5 * len(columns) + column
        ids = (a, a + 1, a + 1 + len(columns), a + len(columns))
        start = len(shadow_vertices)
        shadow_vertices += [(vertices[i][0], vertices[i][1] + 0.012, vertices[i][2]) for i in ids]
        shadow_faces.append(tuple(range(start, start + 4)))
    panel("SombraInteriorFendasOculares", shadow_vertices, shadow_faces, "dark", "Head")
    for side in (-1, 1):
        line("SobrolhoDaViseira", [(side * 0.065, -0.084, 1.760),
                                   (side * 0.043, -0.106, 1.758), (side * 0.017, -0.119, 1.752)],
             0.0024, "steel", "Head")
        line("ArestaDaFace", [(side * 0.067, -0.098, 1.718), (side * 0.052, -0.116, 1.680),
                              (side * 0.030, -0.099, 1.646)], 0.0018, "blue_light", "Head")
        for index in range(4):
            x = side * (0.020 + index * 0.011)
            y = -0.145 + abs(x / 0.083) * 0.066 - 0.001
            line("RespiradorForjado", [(x, y, 1.674), (x, y + 0.006, 1.661)],
                 0.0009, "dark", "Head", sides=5)
    line("NasaleDeAco", [(0, -0.132, 1.773), (0, -0.164, 1.712), (0, -0.126, 1.646)],
         0.0023, "steel", "Head")


def build_sword(tube, line, panel, stud):
    """Espada esbelta; guarda/ponta permanecem nos mesmos pontos do combate."""
    direction = Vector((0.12, -0.70, -0.70)).normalized()
    across = Vector((1, 0, 0))
    across = (across - across.dot(direction) * direction).normalized()
    depth = direction.cross(across).normalized()
    grip = Vector((0.49, -0.005, 0.925))
    guard, pommel = grip + direction * 0.115, grip - direction * 0.135
    tube("PunhoEspadaCouro", grip - direction * 0.11, guard - direction * 0.018,
         0.020, 0.023, "leather", "Hand.R", taper=0.90, sides=28)
    wrap = []
    for index in range(120):
        phase = index / 119
        center = grip + direction * (-0.105 + phase * 0.197)
        angle = phase * math.tau * 7
        wrap.append(tuple(center + across * math.cos(angle) * 0.0215 + depth * math.sin(angle) * 0.024))
    line("EnrolamentoCouroPunho", wrap, 0.0018, "dark", "Hand.R", sides=6)
    tube("PomoEspada", pommel - direction * 0.023, pommel + direction * 0.023,
         0.033, 0.029, "copper", "Hand.R", taper=0.72, sides=28)
    for side in (-1, 1):
        points = [tuple(guard + across * side * distance + direction * (0.024 * (distance / 0.145) ** 2))
                  for distance in (0, 0.035, 0.07, 0.105, 0.145)]
        line("GuardaForjadaEspada", points, 0.014, "steel", "Hand.R", sides=14)
        stud("RemateGuarda", points[-1], "Hand.R", 0.015, "copper")
    sections = [(0.022, 0.045, 0.011), (0.10, 0.049, 0.012), (0.24, 0.044, 0.011),
                (0.55, 0.035, 0.009), (0.80, 0.025, 0.007), (0.93, 0.012, 0.004)]
    vertices = []
    for distance, width, thickness in sections:
        center = guard + direction * distance
        vertices += [tuple(center - across * width), tuple(center - across * width * 0.70 + depth * thickness * 0.65),
                     tuple(center + depth * thickness), tuple(center + across * width * 0.70 + depth * thickness * 0.65),
                     tuple(center + across * width), tuple(center + across * width * 0.70 - depth * thickness * 0.65),
                     tuple(center - depth * thickness), tuple(center - across * width * 0.70 - depth * thickness * 0.65)]
    tip = guard + direction * 1.025
    vertices.append(tuple(tip))
    faces = [tuple(reversed(range(8)))]
    for ring in range(len(sections) - 1):
        for i in range(8):
            faces.append((ring * 8 + i, ring * 8 + (i + 1) % 8,
                          (ring + 1) * 8 + (i + 1) % 8, (ring + 1) * 8 + i))
    last = (len(sections) - 1) * 8
    faces += [(last + i, last + (i + 1) % 8, len(vertices) - 1) for i in range(8)]
    panel("LaminaAcoComFios", vertices, faces, "steel", "Hand.R")
    for side in (-1, 1):
        line("CanalEnergiaDiscreto", [tuple(guard + direction * distance + depth * side * thickness)
                                    for distance, width, thickness in sections[1:-1]],
             0.0018, "cyan", "Hand.R", sides=6)
        for distance in (0.15, 0.22, 0.29):
            center = guard + direction * distance + depth * side * 0.012
            line("GravacaoLamina", [tuple(center - across * 0.015), tuple(center + direction * 0.011),
                                   tuple(center + across * 0.015)], 0.001, "copper", "Hand.R", sides=5)
    return guard, tip


def create_rig(target):
    data = bpy.data.armatures.new("HeroSkeleton")
    rig = bpy.data.objects.new("HeroRig", data)
    target.objects.link(rig)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(name, head, tail, parent=None):
        item = data.edit_bones.new(name)
        item.head, item.tail = head, tail
        if parent:
            item.parent = data.edit_bones[parent]

    bone("Root", (0, 0, 0), (0, 0, 0.16))
    bone("Pelvis", (0, 0, 0.94), (0, 0, 1.08), "Root")
    bone("Spine", (0, 0, 1.08), (0, 0, 1.29), "Pelvis")
    bone("Chest", (0, 0, 1.29), (0, 0, 1.46), "Spine")
    bone("Head", (0, 0, 1.49), (0, 0, 1.81), "Chest")
    for suffix, side in (("L", -1), ("R", 1)):
        bone("UpperArm." + suffix, (side * 0.30, 0, 1.40), (side * 0.43, 0, 1.14), "Chest")
        bone("Forearm." + suffix, (side * 0.43, 0, 1.14), (side * 0.49, -0.005, 0.93), "UpperArm." + suffix)
        bone("Hand." + suffix, (side * 0.49, -0.005, 0.93), (side * 0.49, -0.005, 0.83), "Forearm." + suffix)
        bone("Thigh." + suffix, (side * 0.135, 0, 0.94), (side * 0.145, 0, 0.54), "Pelvis")
        bone("Shin." + suffix, (side * 0.145, 0, 0.54), (side * 0.145, 0, 0.14), "Thigh." + suffix)
        bone("Foot." + suffix, (side * 0.145, 0, 0.14), (side * 0.145, -0.21, 0.09), "Shin." + suffix)
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.animation_data_create()
    for item in rig.pose.bones:
        item.rotation_mode = "XYZ"
    return rig


def create_animations(rig):
    def reset():
        for item in rig.pose.bones:
            item.rotation_euler = (0, 0, 0)
            item.location = (0, 0, 0)

    def key(frame):
        for item in rig.pose.bones:
            item.keyframe_insert(data_path="rotation_euler", frame=frame, group=item.name)
            if item.name == "Pelvis":
                item.keyframe_insert(data_path="location", frame=frame, group=item.name)

    def action(name):
        result = bpy.data.actions.new(name)
        result.use_fake_user = True
        rig.animation_data.action = result
        return result

    idle = action("Idle")
    for frame in (1, 16, 31, 46, 61):
        reset()
        phase = (frame - 1) / 60 * math.tau
        rig.pose.bones["Chest"].rotation_euler.x = 0.009 * math.sin(phase)
        rig.pose.bones["Head"].rotation_euler.z = 0.010 * math.sin(phase)
        rig.pose.bones["UpperArm.L"].rotation_euler.x = 0.010 * math.sin(phase)
        rig.pose.bones["UpperArm.R"].rotation_euler.x = -0.010 * math.sin(phase)
        key(frame)

    action("Walk")
    for frame in (1, 8.5, 16, 23.5, 31):
        reset()
        phase = (frame - 1) / 30 * math.tau
        for suffix, sign in (("L", 1), ("R", -1)):
            swing = math.sin(phase) * sign
            rig.pose.bones["Thigh." + suffix].rotation_euler.x = 0.40 * swing
            rig.pose.bones["Shin." + suffix].rotation_euler.x = -0.44 * max(0, -swing)
            rig.pose.bones["Foot." + suffix].rotation_euler.x = -0.10 * swing
            rig.pose.bones["UpperArm." + suffix].rotation_euler.x = -0.21 * swing
            rig.pose.bones["Forearm." + suffix].rotation_euler.x = -0.07
        rig.pose.bones["Chest"].rotation_euler.z = 0.025 * math.sin(phase)
        key(frame)

    action("Attack")
    create_diagonal_attack(rig, reset, key)

    rig.animation_data.action = idle
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 61
    bpy.context.scene.frame_set(1)


def create_diagonal_attack(rig, reset, key):
    """Corte no espaco do personagem, resolvido em rotacoes locais dos 17 ossos.

    A direita e +X e a frente e -Y. Os alvos explicitam a posicao do punho e a
    direcao da lamina; a IK de dois segmentos mantem os comprimentos do braco.
    Somente as rotacoes resultantes sao gravadas. Nenhum constraint e exportado.
    """
    rest_direction = Vector((0.12, -0.70, -0.70)).normalized()
    upper = rig.data.bones["UpperArm.R"]
    fore = rig.data.bones["Forearm.R"]
    hand = rig.data.bones["Hand.R"]
    length_upper = (upper.tail_local - upper.head_local).length
    length_fore = (fore.tail_local - fore.head_local).length
    previous_euler = {}

    def orient_bone(name, start, end=None, rotation=None):
        rest = rig.data.bones[name]
        if rotation is None:
            rest_vector = (rest.tail_local - rest.head_local).normalized()
            rotation = rest_vector.rotation_difference((end - start).normalized())
        matrix = Matrix.Translation(start) @ rotation.to_matrix().to_4x4() @ rest.matrix_local.to_3x3().to_4x4()
        pose = rig.pose.bones[name]
        pose.matrix = matrix
        # A cadeia usa as mesmas juntas conectadas da geometria de repouso.
        pose.location = (0, 0, 0)
        pose.scale = (1, 1, 1)
        if name in previous_euler:
            pose.rotation_euler.make_compatible(previous_euler[name])
        previous_euler[name] = pose.rotation_euler.copy()
        bpy.context.view_layer.update()

    def solve_arm(wrist, blade_direction):
        bpy.context.view_layer.update()
        shoulder = rig.pose.bones["UpperArm.R"].head.copy()
        reach = wrist - shoulder
        distance = reach.length
        assert abs(length_upper - length_fore) + 0.005 < distance < length_upper + length_fore - 0.005, distance
        axis = reach.normalized()
        projection = (length_upper ** 2 - length_fore ** 2 + distance ** 2) / (2 * distance)
        height = math.sqrt(max(0, length_upper ** 2 - projection ** 2))
        pole = Vector((0.40, -0.15, -0.25))
        bend = (pole - axis * pole.dot(axis)).normalized()
        elbow = shoulder + axis * projection + bend * height
        orient_bone("UpperArm.R", shoulder, elbow)
        orient_bone("Forearm.R", elbow, wrist)
        rotation = rest_direction.rotation_difference(blade_direction.normalized())
        orient_bone("Hand.R", wrist, rotation=rotation)
        assert (rig.pose.bones["Hand.R"].head - wrist).length < 0.001, "Punho fora do alvo"

    def arc_pose(frame):
        # Velocidade angular uniforme no trecho visivel do rastro. A ponta descreve
        # um arco eliptico, em vez de ligar alvos independentes com uma quina.
        # Theta=90 graus coincide com o impacto no frame 5 (0,133 segundos).
        angle = math.radians(90 + (frame - 5) * 800 / 30)
        cosine, sine = math.cos(angle), math.sin(angle)
        wrist = (0.29 + 0.26 * cosine, -0.25 + 0.10 * cosine ** 2, 1.28 + 0.11 * cosine)
        direction = (cosine / math.sqrt(2), -sine, cosine / math.sqrt(2))
        return wrist, direction

    # A guarda alta termina antes de 0,06 s; o arco passa pelo impacto em 0,133 s.
    # Frames 2 e 8 dao continuidade nas bordas da janela de rastro (0,06-0,22 s).
    poses = (
        (1, 0.00, 0.00, None, None),
        (2, 0.45, -0.177, *arc_pose(2)),
        (3, 0.85, -0.144, *arc_pose(3)),
        (4, 1.00, -0.081, *arc_pose(4)),
        (5, 0.90, 0.000, *arc_pose(5)),
        (6, 0.75, 0.081, *arc_pose(6)),
        (7, 0.65, 0.144, *arc_pose(7)),
        (8, 0.55, 0.177, *arc_pose(8)),
        (9, 0.40, 0.12, (0.21, -0.10, 1.06), (-0.33, -0.70, -0.63)),
        (10, 0.28, 0.07, (0.31, -0.08, 1.02), (-0.10, -0.78, -0.62)),
        (11, 0.15, 0.03, (0.39, -0.045, 0.975), (0.02, -0.75, -0.66)),
        (12, 0.06, 0.01, (0.45, -0.02, 0.945), (0.08, -0.72, -0.69)),
        (13, 0.00, 0.00, None, None),
    )
    for frame, weight, turn, wrist, direction in poses:
        reset()
        # O eixo Y local dos ossos do tronco acompanha o eixo vertical global.
        rig.pose.bones["Chest"].rotation_euler = (0.025 * weight, turn, 0)
        rig.pose.bones["Spine"].rotation_euler.y = turn * 0.20
        rig.pose.bones["Head"].rotation_euler.y = -turn * 0.35
        rig.pose.bones["UpperArm.L"].rotation_euler = (-0.20 * weight, -0.12 * weight, -0.10 * weight)
        rig.pose.bones["Forearm.L"].rotation_euler.x = -0.30 * weight
        rig.pose.bones["Thigh.L"].rotation_euler.x = 0.04 * weight
        rig.pose.bones["Thigh.R"].rotation_euler.x = -0.06 * weight
        rig.pose.bones["Shin.L"].rotation_euler.x = -0.07 * weight
        rig.pose.bones["Shin.R"].rotation_euler.x = -0.06 * weight
        rig.pose.bones["Pelvis"].location.z = -0.008 * weight
        if wrist is not None:
            solve_arm(Vector(wrist), Vector(direction))
        key(frame)


def validate_attack(rig, mesh):
    """Confere marcadores, ponta deformada, arco e ausencia de root motion."""
    scene = bpy.context.scene
    markers = [bpy.data.objects[name] for name in ("SwordBase", "SwordTip")]
    rig.animation_data.action = bpy.data.actions["Attack"]
    scene.frame_set(1)
    bpy.context.view_layer.update()
    rest_points = [marker.matrix_world.translation.copy() for marker in markers]
    tip_index = min(range(len(mesh.data.vertices)),
                    key=lambda index: (mesh.matrix_world @ mesh.data.vertices[index].co - rest_points[1]).length_squared)
    assert (mesh.matrix_world @ mesh.data.vertices[tip_index].co - rest_points[1]).length < 0.0001
    rest_hand_inverse = rig.data.bones["Hand.R"].matrix_local.inverted()
    trajectory = []
    for frame in range(1, 14):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        hand_transform = rig.matrix_world @ rig.pose.bones["Hand.R"].matrix @ rest_hand_inverse
        expected = [hand_transform @ point for point in rest_points]
        actual = [marker.matrix_world.translation.copy() for marker in markers]
        error = max((a - b).length for a, b in zip(actual, expected))
        evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        evaluated_mesh = evaluated.to_mesh()
        mesh_tip = evaluated.matrix_world @ evaluated_mesh.vertices[tip_index].co
        error = max(error, (mesh_tip - actual[1]).length)
        evaluated.to_mesh_clear()
        length = (actual[1] - actual[0]).length
        assert abs(length - 1.025) < 0.001, length
        assert error < 0.001, f"Offset dos marcadores: {error}"
        root = rig.pose.bones["Root"]
        assert root.location.length < 0.00001 and root.rotation_euler.to_quaternion().angle < 0.0001
        assert all(abs(value - 1) < 0.0001 for bone in rig.pose.bones for value in bone.scale)
        trajectory.append({"frame": frame, "segundos": round((frame - 1) / 30, 3),
                           "base": [round(value, 4) for value in actual[0]],
                           "ponta": [round(value, 4) for value in actual[1]],
                           "lamina_m": round(length, 4), "erro_m": round(error, 6)})
    assert trajectory[2]["ponta"][0] > 0.9 and trajectory[2]["ponta"][2] > 1.9
    assert trajectory[6]["ponta"][0] < -0.5 and trajectory[6]["ponta"][2] < 0.8
    assert all(sample["ponta"][1] < -0.2 for sample in trajectory[1:8]), "Espada atravessou para tras"
    print("ATTACK_TRAJECTORY", json.dumps(trajectory, ensure_ascii=False))
    arc_points = [Vector(sample["ponta"]) for sample in trajectory[1:8]]
    arc_vectors = [end - start for start, end in zip(arc_points, arc_points[1:])]
    speeds = [vector.length * 30 for vector in arc_vectors]
    turns = [math.degrees(before.angle(after)) for before, after in zip(arc_vectors, arc_vectors[1:])]
    assert max(turns) < 35, f"Quina no arco da ponta: {turns}"
    assert max(speeds) / min(speeds) < 1.35, f"Pico de velocidade no corte: {speeds}"
    print("ATTACK_ARC_METRICS", json.dumps({
        "velocidades_m_s_33ms": [round(value, 3) for value in speeds],
        "mudanca_tangente_graus_33ms": [round(value, 3) for value in turns],
    }, ensure_ascii=False))
    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(1)
    bpy.context.view_layer.update()


def preview_scene():
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -0.012))
    floor = bpy.context.object
    floor.name = "Preview_Piso"
    floor.data.materials.append(BASE.material("Preview_Chao", (0.10, 0.14, 0.17), 0.0, 0.9))
    for name, at, color, energy, size in (
        ("Preview_Principal", (3, -4.5, 4.5), (1.0, 0.88, 0.72), 650, 3.5),
        ("Preview_Preenchimento", (-3, -1.5, 3), (0.52, 0.70, 1.0), 400, 3),
        ("Preview_Recorte", (1.5, 3, 3.2), (0.50, 0.85, 1.0), 900, 2.8),
    ):
        bpy.ops.object.light_add(type="AREA", location=at)
        light = bpy.context.object
        light.name = name
        light.data.energy = energy
        light.data.color = color
        light.data.size = size
        light.rotation_euler = (Vector((0, 0, 1.0)) - light.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.camera_add(location=(3.0, -5.5, 2.7))
    camera = bpy.context.object
    camera.name = "Preview_Camera"
    camera.rotation_euler = (Vector((0, -0.12, 0.94)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 2.80
    bpy.context.scene.camera = camera


def main():
    ART.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = BASE.configure_scene()
    scene.render.fps = 30
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 1000
    generate_character_textures()
    target, rig, mesh = build_character()
    bpy.context.view_layer.update()
    points = [mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
    assert abs(min(point.z for point in points)) < 0.001, "Os pes devem ficar em Z=0."
    assert 1.84 <= max(point.z for point in points) <= 1.86, "Altura deve ser 1,85 m."
    assert {"Idle", "Walk", "Attack"}.issubset(set(bpy.data.actions.keys())), "Clipes ausentes."
    assert len(rig.data.bones) == 17, "O rig deve preservar os 17 ossos originais."
    assert mesh.data.uv_layers.active and len(mesh.data.uv_layers.active.data) == len(mesh.data.loops), "UV0 incompleto."
    assert all(math.isfinite(value) for item in mesh.data.uv_layers.active.data for value in item.uv), "UV0 invalido."
    validate_attack(rig, mesh)
    BASE.export_collection(target, ART / "Personagem.fbx", animated=True)
    preview_scene()
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "Personagem.blend"))
    scene.render.filepath = str(HERE / "PersonagemPreview.png")
    try:
        bpy.ops.render.render(write_still=True)
    except RuntimeError as error:
        print("EEVEE indisponivel; tentando Workbench:", error)
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.display.shading.color_type = "MATERIAL"
        scene.display.shading.light = "STUDIO"
        bpy.ops.render.render(write_still=True)
    mesh.data.calc_loop_triangles()
    print("PERSONAGEM_PRONTO", json.dumps({
        "altura_m": round(max(point.z for point in points), 3),
        "vertices": len(mesh.data.vertices), "triangulos": len(mesh.data.loop_triangles),
        "ossos": len(rig.data.bones), "animacoes": ["Idle", "Walk", "Attack"],
        "uv": "UV0_Metrico_1m, projecao de caixa, uma unidade UV por metro",
        "fps": 30, "attack_segundos": 0.4, "blender": bpy.app.version_string,
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
