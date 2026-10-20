"""Guardião ancestral original: escultura, armadura fossilizada e mapas PBR exportáveis.

Blender --background --factory-startup --python-exit-code 1 --python gerar_monstro.py
Frente -Y, Z para cima, metros e pés Z0. Braco.L/R preservados para animação Unity.
"""
from __future__ import annotations
import importlib.util
import json
import math
import random
import sys
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.noise import noise_vector

HERE = Path(__file__).resolve().parent
ART = HERE.parent / "Unity/Assets/Art"
WORK = HERE.parent.parent.parent / "work"
RNG = random.Random(185025)
SPEC = importlib.util.spec_from_file_location("periodic_texture_helpers", HERE / "gerar_texturas.py")
TEX = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(TEX)


def configure_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 1050, 1150
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.world = bpy.data.worlds.new("EstudioGuardiao")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (.055, .065, .075, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .35
    return scene


def fracture_field(surface):
    u = surface.u + (surface.noise(7) - .5) * .018
    v = surface.v + (surface.noise(6) - .5) * .018
    cracks, worn = np.zeros_like(u), np.zeros_like(u)
    for branch in range(13):
        start = surface.rng.random(2)
        angle = surface.rng.uniform(0, math.tau)
        width = surface.rng.uniform(.0007, .0018)
        for segment in range(surface.rng.integers(3, 7)):
            angle += surface.rng.uniform(-.65, .65)
            length = surface.rng.uniform(.025, .11)
            sx, sy = math.cos(angle) * length, math.sin(angle) * length
            dx, dy = (u - start[0] + .5) % 1 - .5, (v - start[1] + .5) % 1 - .5
            t = np.clip((dx * sx + dy * sy) / (length * length), 0, 1)
            distance = np.sqrt((dx - sx * t) ** 2 + (dy - sy * t) ** 2)
            cracks = np.maximum(cracks, np.exp(-(distance / width) ** 2))
            worn = np.maximum(worn, np.exp(-(distance / (width * 4)) ** 2))
            start = (start + (sx, sy)) % 1
    return cracks, np.maximum(0, worn - cracks)


def monster_textures():
    TEX.SIZE = 1024
    surface = TEX.PeriodicSurface(185025)
    u, v, n = surface.u, surface.v, surface.n
    macro = surface.fbm((3, 11, 35, 101), (.4, .29, .21, .1))
    cracks, worn = fracture_field(surface)
    pores = np.clip((surface.noise(181) - .63) / .28, 0, 1) ** 2
    grit = surface.grain()
    moss = np.clip((surface.noise(7) * .65 + surface.noise(39) * .35 - .57) / .2, 0, 1)
    variation = (macro - .5) * .22 + (grit - .5) * .035 - cracks * .13 + worn * .025 - pores * .075
    albedo = np.array((.74, .745, .71), dtype=np.float32)[None, None, :] + variation[:, :, None]
    albedo[:, :, 0] -= moss * .065
    albedo[:, :, 1] -= moss * .015
    albedo[:, :, 2] -= moss * .10
    albedo = np.clip(albedo, .27, .88)
    height = macro * .28 + grit * .035 - cracks * .39 + worn * .075 - pores * .11
    normal = TEX.normal_from_height(height, .0022)
    packed = np.zeros((n, n, 4), np.float32)
    packed[:, :, 3] = np.clip(.18 + (macro - .5) * .1 - moss * .055 - cracks * .05, .06, .25)
    maps = {"Albedo": TEX.pad_period(TEX.rgba(albedo)), "Normal": TEX.pad_period(TEX.rgba(normal)),
            "MetallicSmoothness": TEX.pad_period(packed)}
    texture_dir = ART / "Textures"
    texture_dir.mkdir(parents=True, exist_ok=True)
    report = [TEX.save_png(texture_dir / f"Monster_{suffix}.png", values) for suffix, values in maps.items()]
    return report


def material(name, color, roughness=.85, emission=0, textured=True):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = roughness
    if emission:
        shader.inputs["Emission Color"].default_value = (*color, 1)
        shader.inputs["Emission Strength"].default_value = emission
    if textured:
        image = nodes.new("ShaderNodeTexImage")
        image.image = bpy.data.images.load(str(ART / "Textures/Monster_Albedo.png"), check_existing=True)
        image.image.colorspace_settings.name = "sRGB"
        image.extension = "REPEAT"
        tint = nodes.new("ShaderNodeMixRGB")
        tint.blend_type = "MULTIPLY"
        tint.inputs[0].default_value = 1
        tint.inputs[2].default_value = (*color, 1)
        links.new(image.outputs["Color"], tint.inputs[1])
        links.new(tint.outputs[0], shader.inputs["Base Color"])
        image_normal = nodes.new("ShaderNodeTexImage")
        image_normal.image = bpy.data.images.load(str(ART / "Textures/Monster_Normal.png"), check_existing=True)
        image_normal.image.colorspace_settings.name = "Non-Color"
        normal = nodes.new("ShaderNodeNormalMap")
        normal.inputs["Strength"].default_value = .85
        links.new(image_normal.outputs["Color"], normal.inputs["Color"])
        links.new(normal.outputs["Normal"], shader.inputs["Normal"])
    return mat


def parent_world(obj, parent):
    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_world = world
    return obj


def pivot(name, position, parent=None):
    obj = bpy.data.objects.new(name, None)
    ASSETS.objects.link(obj)
    obj.location = position
    bpy.context.view_layer.update()
    if parent is not None:
        parent_world(obj, parent)
    return obj


def mesh_object(name, vertices, faces, mat, parent, smooth=True, bevel=0):
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    ASSETS.objects.link(obj)
    obj.data.materials.append(mat)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    if bevel:
        modifier = obj.modifiers.new("Arestas erodidas", "BEVEL")
        modifier.width, modifier.segments = bevel, 3
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = .42
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    for face in obj.data.polygons:
        face.use_smooth = smooth
    bpy.context.view_layer.update()
    parent_world(obj, parent)
    return obj


def loft(name, sections, mat, parent, sides=32, wear=.02, angular=.82, vertical=False):
    vertices, faces = [], []
    for index, (center, rx, ry) in enumerate(sections):
        center = Vector(center)
        before = Vector(sections[max(0, index - 1)][0])
        after = Vector(sections[min(len(sections) - 1, index + 1)][0])
        tangent = Vector((0, 0, 1)) if vertical else (after - before).normalized()
        side = Vector((0, 1, 0)).cross(tangent).normalized()
        if side.length < .1:
            side = Vector((1, 0, 0))
        other = tangent.cross(side).normalized()
        for point in range(sides):
            angle = point / sides * math.tau
            a, b = math.cos(angle), math.sin(angle)
            a, b = math.copysign(abs(a) ** angular, a), math.copysign(abs(b) ** angular, b)
            co = center + side * a * rx + other * b * ry
            noise = noise_vector(co * 17.1).x * .65 + noise_vector(co * 39.3).y * .35
            co += (side * a + other * b) * noise * wear
            vertices.append(tuple(co))
        if index:
            row = index * sides
            for point in range(sides):
                following = (point + 1) % sides
                faces.append((row - sides + point, row - sides + following, row + following, row + point))
    faces += [tuple(reversed(range(sides))), tuple(range((len(sections) - 1) * sides, len(sections) * sides))]
    return mesh_object(name, vertices, faces, mat, parent, True)


def plate(name, outline, front, depth, mat, parent, bevel=.012, crown=.012):
    count = len(outline)
    vertices = [(x, front + RNG.uniform(-.006, .006), z) for x, z in outline]
    vertices += [(x, front + depth, z) for x, z in outline]
    center = (sum(x for x, z in outline) / count, front - crown, sum(z for x, z in outline) / count)
    vertices.append(center)
    faces = [(index, (index + 1) % count, count * 2) for index in range(count)]
    faces.append(tuple(reversed(range(count, count * 2))))
    faces += [(index, index + count, (index + 1) % count + count, (index + 1) % count) for index in range(count)]
    return mesh_object(name, vertices, faces, mat, parent, False, bevel)


def stroke(name, points, radius, mat, parent):
    return loft(name, [(p, radius, radius * .65) for p in points], mat, parent, sides=8, wear=0, angular=1)


def carved_fissure(plate_object, points, parent):
    # Real CSG depressions have a dark recessed floor and bevelled stone lips.
    for index, (start, end) in enumerate(zip(points, points[1:])):
        middle, direction = (Vector(start) + Vector(end)) * .5, Vector(end) - Vector(start)
        bpy.ops.mesh.primitive_cube_add(size=1, location=middle)
        cutter = bpy.context.object
        cutter.name = "TemporaryChisel"
        cutter.dimensions = (.016, .095, direction.length + .015)
        cutter.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        boolean = plate_object.modifiers.new("Fratura escavada", "BOOLEAN")
        boolean.operation, boolean.solver, boolean.object = "DIFFERENCE", "EXACT", cutter
        bpy.context.view_layer.objects.active = plate_object
        bpy.ops.object.modifier_apply(modifier=boolean.name)
        bpy.data.objects.remove(cutter, do_unlink=True)
        floor = [(p[0], p[1] + .038, p[2]) for p in (start, end)]
        stroke("FundoFratura", floor, .009, MATS["dark"], parent)


def build_guardian():
    root = pivot("Golem", (0, 0, 0))
    stone, light, dark, amber = (MATS[key] for key in ("stone", "light", "dark", "amber"))
    loft("ToraxEsculpido", [((0, .015, .94), .24, .17), ((0, .01, 1.07), .29, .20),
         ((0, -.01, 1.22), .285, .205), ((0, -.035, 1.38), .36, .24), ((0, -.025, 1.56), .46, .285),
         ((0, .005, 1.73), .41, .24), ((0, .045, 1.83), .27, .19), ((0, .04, 1.91), .14, .13)], stone, root, 40, .039, .65)
    loft("BaciaFossilizada", [((0, .035, .80), .22, .16), ((0, .03, .89), .31, .22),
         ((0, .035, 1.02), .34, .21), ((0, .02, 1.10), .27, .17)], dark, root, 32, .016)
    for side in (-1, 1):
        outline = [(side * x, z) for x, z in ((.035, 1.71), (.20, 1.87), (.43, 1.76), (.45, 1.58),
                    (.31, 1.44), (.12, 1.45), (.035, 1.55))]
        chest = plate("PeitoralFossilizado", outline, -.33, .13, light if side < 0 else stone, root, .018, .04)
        points = [(side * .18, -.33, 1.80), (side * .21, -.34, 1.69), (side * .15, -.34, 1.59), (side * .18, -.34, 1.49)]
        carved_fissure(chest, points, root)
        for rib in range(3):
            z = 1.37 - rib * .115
            plate("CostelaPetrificada", [(side * .035, z + .04), (side * .15, z + .12), (side * .30, z + .145),
                  (side * .35, z + .085), (side * .24, z + .035), (side * .19, z - .025),
                  (side * .12, z + .005), (side * .06, z - .035)], -.26 + rib * .018, .085, stone, root, .006)
        hip_z = .04 if side < 0 else -.035
        plate("QuadrilFraturado", [(side * .09, 1.06 + hip_z), (side * .28, 1.12 + hip_z),
              (side * .39, 1.005 + hip_z), (side * .33, .96 + hip_z), (side * .37, .89 + hip_z),
              (side * .25, .80 + hip_z), (side * .21, .91 + hip_z), (side * .13, .88 + hip_z)],
              -.20, .13, stone, root, .006)
    plate("SeloAncestralEscavado", [(-.045, 1.78), (.055, 1.76), (.095, 1.57), (.02, 1.47), (-.075, 1.58)],
          -.377, .055, dark, root, .006)
    stroke("FendaVitalAmbar", [(-.009, -.394, 1.72), (.018, -.397, 1.67), (-.006, -.397, 1.62), (.015, -.39, 1.57)],
           .0032, amber, root)

    loft("PescocoHunched", [((0, .04, 1.79), .19, .15), ((0, -.04, 1.93), .17, .14),
         ((0, -.135, 2.02), .13, .12)], dark, root, 28, .011)
    head_objects = set(ASSETS.objects)
    head = loft("CabecaEntalhada", [((0, -.20, 1.90), .075, .085), ((0, -.215, 1.95), .12, .12),
         ((0, -.20, 2.035), .17, .15), ((0, -.17, 2.13), .19, .165), ((0, -.13, 2.22), .155, .135),
         ((-.015, -.11, 2.29), .075, .075), ((-.025, -.10, 2.315), .025, .025)], dark, root, 32, .016, .66)
    # A disposable ellipsoidal chisel carves a true void. No sphere is part of the asset.
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1, location=(0, -.32, 2.07))
    cutter = bpy.context.object
    cutter.scale = (.115, .11, .145)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    boolean = head.modifiers.new("Cavidade carbonizada real", "BOOLEAN")
    boolean.operation, boolean.solver, boolean.object = "DIFFERENCE", "EXACT", cutter
    bpy.context.view_layer.objects.active = head
    bpy.ops.object.modifier_apply(modifier=boolean.name)
    bpy.data.objects.remove(cutter, do_unlink=True)
    for side in (-1, 1):
        shift = .015 if side < 0 else -.009
        plate("SupercilioFraturado", [(side * .018, 2.18 + shift), (side * .05, 2.26 + shift),
              (side * .14, 2.28 + shift), (side * .19, 2.17 + shift), (side * .135, 2.145 + shift),
              (side * .11, 2.11 + shift), (side * .075, 2.135 + shift), (side * .035, 2.11 + shift)],
              -.335, .07, stone, root, .004)
        plate("BrasaNoFundoOrbita", [(side * .065, 2.095), (side * .081, 2.099),
              (side * .075, 2.087)], -.27, .006, amber, root, 0, 0)
        plate("MalarFraturado", [(side * .13, 2.08), (side * .18, 2.115), (side * .163, 2.00),
              (side * .103, 1.93), (side * .08, 1.975), (side * .101, 2.025)], -.329, .06, stone, root, .004)
    stroke("RasgoCranio", [(-.11, -.24, 2.28), (-.085, -.30, 2.23), (-.10, -.344, 2.17)], .005, dark, root)
    loft("CristaCranialPartida", [((-.105, -.12, 2.18), .065, .06), ((-.14, -.08, 2.29), .035, .035),
         ((-.16, -.07, 2.37), .004, .006)], stone, root, 16, .005, .6)
    for obj in set(ASSETS.objects) - head_objects:
        if obj.type == "MESH":
            for vertex in obj.data.vertices:
                vertex.co.y -= .065
                vertex.co.z -= .065

    for side, suffix in ((-1, "L"), (1, "R")):
        arm = pivot("Braco." + suffix, (side * .49, .025, 1.70), root)
        loft("BracoMuscular." + suffix, [((side * .46, .04, 1.78), .13, .17),
             ((side * .56, .035, 1.65), .155, .16), ((side * .60, .02, 1.46), .145, .14),
             ((side * .63, -.005, 1.25), .12, .14), ((side * .65, -.03, 1.14), .14, .15)], stone, arm, 28, .043, .6)
        loft("AntebracoEsculpido." + suffix, [((side * .65, -.02, 1.22), .14, .16),
             ((side * .69, -.045, 1.13), .20, .21), ((side * .72, -.075, .94), .22, .22),
             ((side * .73, -.11, .78), .17, .17), ((side * .73, -.14, .69), .125, .14)], stone, arm, 32, .019)
        for layer in range(3 if side < 0 else 2):
            z = 1.82 + layer * .045
            x = .50 + layer * .06
            peak = z + (.22 if side < 0 else .13) - layer * .035
            outline = [(side * (x - .12), z - .13), (side * (x - .10), z + .09), (side * (x + .025), peak),
                       (side * (x + .23), z + .04), (side * (x + .15), z + .015), (side * (x + .20), z - .06),
                       (side * (x + .10), z - .13), (side * (x + .05), z - .10), (side * (x + .015), z - .18)]
            plate("OmbreiraEstratificada." + suffix, outline, -.18 + layer * .08, .21, light if layer == 1 else stone, arm, .006)
        for spike in range(2 if side < 0 else 1):
            x = .56 + spike * .16
            loft("LaminaRochaOmbro." + suffix, [((side * x, .15, 1.90), .10, .13),
                 ((side * (x + .09), .17, 2.06), .065, .07),
                 ((side * (x + .17), .20, 2.19 - spike * .07), .006, .009)], light, arm, 16, .006, .6)
        plate("BicepsLaminado." + suffix, [(side * .49, 1.62), (side * .68, 1.63), (side * .76, 1.39),
              (side * .63, 1.27), (side * .52, 1.38)], -.145, .11, stone, arm, .014)
        forearm = plate("ManoplaFossilizada." + suffix, [(side * .57, 1.16), (side * .75, 1.25), (side * .92, 1.03),
              (side * .89, .84), (side * .76, .72), (side * .57, .84)], -.27, .12, light, arm, .018, .025)
        carved_fissure(forearm, [(side * .68, -.27, 1.17), (side * .72, -.28, 1.05),
                      (side * .67, -.28, .96), (side * .73, -.27, .85)], arm)
        stroke("FraturaAmbarBraco." + suffix, [(side * .71, -.258, 1.01), (side * .685, -.258, .97)], .0025, amber, arm)
        loft("PalmaEscultural." + suffix, [((side * .73, -.13, .79), .12, .13),
             ((side * .74, -.18, .69), .175, .155), ((side * .74, -.21, .59), .16, .13),
             ((side * .74, -.24, .55), .12, .09)], stone, arm, 28, .012)
        for finger in range(3):
            x = side * .74 + (finger - 1) * .105
            bend = -.31 - (.025 if finger == 1 else 0)
            loft("FalangeSegmentada." + suffix, [((x, -.20, .66), .044, .055), ((x, bend, .57), .048, .045),
                 ((x + side * .01, bend - .04, .48), .035, .035)], stone, arm, 16, .004)
            loft("GarraFossilizada." + suffix, [((x + side * .01, bend - .035, .49), .035, .037),
                 ((x + side * .012, bend - .07, .42), .028, .026),
                 ((x + side * .015, bend - .12, .415), .004, .005)], light, arm, 16, .003, .65)
        loft("PolegarCurvo." + suffix, [((side * .60, -.20, .71), .055, .065),
             ((side * .53, -.28, .63), .047, .05), ((side * .54, -.37, .58), .008, .012)], light, arm, 18, .004)

        leg_objects_before = set(ASSETS.objects)
        x = side * .23
        loft("CoxaEsculpida." + suffix, [((x, .02, .94), .17, .18), ((x + side * .015, .03, .81), .185, .19),
             ((x + side * .02, .045, .62), .14, .155), ((x + side * .025, .035, .50), .105, .12)], stone, root, 28, .013)
        loft("CanelaOrganica." + suffix, [((x + side * .025, .025, .55), .12, .13),
             ((x + side * .025, .01, .43), .145, .145), ((x + side * .035, .035, .24), .105, .12),
             ((x + side * .04, .025, .13), .105, .115)], dark, root, 28, .012)
        plate("GrevaFraturada." + suffix, [(x - .10, .55), (x + .065, .60), (x + .14, .46),
              (x + .09, .39), (x + .12, .31), (x + .065, .18), (x + .01, .13),
              (x - .02, .29), (x - .095, .26), (x - .07, .40)], -.13, .09, stone, root, .005)
        if side < 0:
            plate("EstilhacoCanela", [(x - .13, .48), (x - .07, .41), (x - .045, .19), (x - .10, .14)],
                  -.155, .035, light, root, .003)
        loft("PePetrificado." + suffix, [((x + side * .04, -.12, 0), .16, .27),
             ((x + side * .04, -.14, .025), .185, .29), ((x + side * .04, -.11, .11), .19, .28),
             ((x + side * .04, -.055, .20), .14, .19), ((x + side * .04, .01, .25), .10, .115)], stone, root, 32, .005, vertical=True)
        for toe in range(3):
            tx = x + side * .04 + (toe - 1) * .10
            loft("UnhaPetrea." + suffix, [((tx, -.30, .11), .042, .046), ((tx, -.39, .055), .034, .03),
                 ((tx, -.46 - (toe == 1) * .025, .025), .005, .006)], stone, root, 14, .003, .65)
        leg = pivot("Perna." + suffix, (side * .23, .03, .94), root)
        bpy.context.view_layer.update()
        for obj in set(ASSETS.objects) - leg_objects_before:
            if obj.type == "MESH":
                parent_world(obj, leg)
    for z, radius in ((1.22, .12), (1.48, .13), (1.73, .14), (1.91, .11)):
        loft("PlacaDorsal", [((0, .18, z - .1), radius, .06), ((0, .31, z), radius * .7, .045),
             ((.01, .35, z + .12), .008, .008)], stone, root, 18, .006, .6)
    add_moss(root)
    return root


def add_moss(root):
    # Small irregular encrustations, rather than large green patches covering the anatomy.
    for index in range(22):
        side = -1 if index % 2 else 1
        x = side * RNG.uniform(.16, .38)
        z = RNG.uniform(1.46, 1.79) if index < 14 else RNG.uniform(.18, .52)
        if index >= 14:
            x = side * RNG.uniform(.19, .32)
        y = -.38 if index < 14 else -.155
        size = RNG.uniform(.008, .025)
        outline = [(x + math.cos(a) * size * RNG.uniform(.7, 1.2), z + math.sin(a) * size * RNG.uniform(.7, 1.2))
                   for a in [point / 7 * math.tau for point in range(7)]]
        plate("LiquenErodido", outline, y, .005, MATS["moss"], root, .002, .002)


def metric_uv(obj):
    uv = obj.data.uv_layers.get("UVMap") or obj.data.uv_layers.new(name="UVMap")
    for polygon in obj.data.polygons:
        axis = max(range(3), key=lambda value: abs(polygon.normal[value]))
        for loop in polygon.loop_indices:
            co = obj.matrix_world @ obj.data.vertices[obj.data.loops[loop].vertex_index].co
            uv.data[loop].uv = (co.y, co.z) if axis == 0 else (co.x, co.z) if axis == 1 else (co.x, co.y)


def consolidate(root):
    bpy.context.view_layer.update()
    for obj in list(ASSETS.objects):
        if obj.type == "MESH":
            if obj.data.materials[0] not in (MATS["amber"], MATS["moss"]):
                bpy.context.view_layer.objects.active = obj
                modifier = obj.modifiers.new("Densidade para erosao esculpida", "SUBSURF")
                modifier.subdivision_type, modifier.levels = "SIMPLE", 1
                bpy.ops.object.modifier_apply(modifier=modifier.name)
                obj.data.update()
                for vertex in obj.data.vertices:
                    if vertex.co.z > .04:
                        co = vertex.co.copy()
                        erosion = noise_vector(co * 49.7).x * .0038 + noise_vector(co * 111.3).y * .0018
                        vertex.co += vertex.normal * erosion
                obj.data.update()
            metric_uv(obj)
    groups = {}
    for obj in list(ASSETS.objects):
        if obj.type == "MESH":
            groups.setdefault((obj.parent, obj.data.materials[0].name), []).append(obj)
    for (parent, mat), objects in groups.items():
        bpy.ops.object.select_all(action="DESELECT")
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = objects[0]
        if len(objects) > 1:
            bpy.ops.object.join()
        obj = bpy.context.object
        obj.name = f"{parent.name}_{mat.removeprefix('MAT_Golem')}"


def export_and_report(root, textures):
    objects = list(ASSETS.objects)
    meshes = [obj for obj in objects if obj.type == "MESH"]
    points = [obj.matrix_world @ vertex.co for obj in meshes for vertex in obj.data.vertices]
    minimum = [min(point[axis] for point in points) for axis in range(3)]
    maximum = [max(point[axis] for point in points) for axis in range(3)]
    assert abs(minimum[2]) < .001, minimum
    assert 2.15 < maximum[2] < 2.40, maximum
    assert all(bpy.data.objects[name].parent == root for name in ("Braco.L", "Braco.R", "Perna.L", "Perna.R"))
    assert all(obj.data.uv_layers and len(obj.data.uv_layers.active.data) == len(obj.data.loops) for obj in meshes)
    assert all(math.isfinite(value) for point in points for value in point)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(ART / "Monstro.fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
        axis_forward="-Z", axis_up="Y", global_scale=1, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS", use_space_transform=True, bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type="FACE", bake_anim=False, path_mode="AUTO", embed_textures=False)
    report = {"height_m": maximum[2], "min_xyz": minimum, "max_xyz": maximum,
              "vertices": sum(len(obj.data.vertices) for obj in meshes),
              "triangles": sum(sum(len(face.vertices) - 2 for face in obj.data.polygons) for obj in meshes),
              "mesh_renderers": len(meshes), "uv_units_per_meter": 1,
              "arm_pivots": {name: list(bpy.data.objects[name].matrix_world.translation) for name in ("Braco.L", "Braco.R")},
              "leg_pivots": {name: list(bpy.data.objects[name].matrix_world.translation) for name in ("Perna.L", "Perna.R")},
              "materials": {mat.name: list(mat.diffuse_color) for mat in MATS.values()}, "texture_maps": textures}
    WORK.mkdir(parents=True, exist_ok=True)
    (WORK / "guardian-generation-report.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


def preview_scene():
    preview = bpy.data.collections.new("Guardiao_Preview")
    bpy.context.scene.collection.children.link(preview)
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.012))
    floor = bpy.context.object
    for collection in list(floor.users_collection):
        collection.objects.unlink(floor)
    preview.objects.link(floor)
    floor.data.materials.append(material("PreviewBasalto", (.045, .055, .065), textured=False))
    for name, position, color, energy, size in (
        ("Key", (3.2, -4.5, 4.8), (1, .86, .70), 650, 3.0),
        ("Fill", (-3.0, -2.2, 2.8), (.54, .70, 1), 300, 3.0),
        ("Rim", (1.8, 3, 4), (.70, .83, 1), 900, 2.3)):
        data = bpy.data.lights.new(name, "AREA")
        data.color, data.energy, data.shape, data.size = color, energy, "DISK", size
        obj = bpy.data.objects.new(name, data)
        preview.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (Vector((0, 0, 1.2)) - obj.location).to_track_quat("-Z", "Y").to_euler()
    data = bpy.data.cameras.new("CameraGuardiao")
    camera = bpy.data.objects.new("CameraGuardiao", data)
    preview.objects.link(camera)
    data.type, data.ortho_scale = "ORTHO", 2.9
    bpy.context.scene.camera = camera
    return camera


def main():
    global ASSETS, MATS
    scene = configure_scene()
    ASSETS = bpy.data.collections.new("Monstro_Assets")
    scene.collection.children.link(ASSETS)
    textures = monster_textures()
    MATS = {"stone": material("MAT_GolemPedra", (.085, .105, .092)),
            "light": material("MAT_GolemPedraClara", (.16, .175, .145)),
            "dark": material("MAT_GolemEscuro", (.018, .027, .022)),
            "amber": material("MAT_GolemCristal", (.64, .18, .035), .45, .65, False),
            "moss": material("MAT_GolemMusgo", (.15, .22, .08), .96)}
    root = build_guardian()
    consolidate(root)
    report = export_and_report(root, textures)
    camera = preview_scene()
    for position, path in (((0, -6, 2.5), WORK / "GuardianFront.png"),
                           ((3.1, -6.0, 3.0), HERE / "MonstroPreview.png")):
        camera.location = position
        camera.rotation_euler = (Vector((0, -.03, 1.13)) - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(path)
        if "--skip-preview" not in sys.argv:
            bpy.ops.render.render(write_still=True)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "Monstro.blend"))
    print("[GUARDIAN PASS]", json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
