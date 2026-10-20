"""Texturas PBR originais, periódicas e determinísticas para o protótipo.

Executar com o Python do Blender, que já fornece bpy e numpy:
Blender --background --factory-startup --python-exit-code 1 --python gerar_texturas.py

Os albedos são valores sRGB neutros para receber a cor do material no Unity.
Normais e MetallicSmoothness são dados lineares, sem transformação de cor.
MetallicSmoothness: R=metallic, G=B=0, A=smoothness (Unity Standard).
"""

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np


SIZE = 512
SEED = 185021
PROJECT = Path(__file__).resolve().parents[1]
WORK = PROJECT.parent.parent / "work"


class PeriodicSurface:
    def __init__(self, seed):
        # One duplicate border texel makes each exported edge exactly equal.
        # Heights and derivatives are evaluated on the unique torus first.
        self.n = SIZE - 1
        self.rng = np.random.default_rng(seed)
        axis = np.arange(self.n, dtype=np.float32) / self.n
        self.u, self.v = np.meshgrid(axis, axis)

    def noise(self, frequency):
        fx, fy = (frequency, frequency) if isinstance(frequency, int) else frequency
        lattice = self.rng.random((fy, fx), dtype=np.float32)
        x, y = self.u * fx, self.v * fy
        ix, iy = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
        tx, ty = x - ix, y - iy
        tx = tx * tx * tx * (tx * (tx * 6 - 15) + 10)
        ty = ty * ty * ty * (ty * (ty * 6 - 15) + 10)
        a = lattice[iy % fy, ix % fx]
        b = lattice[iy % fy, (ix + 1) % fx]
        c = lattice[(iy + 1) % fy, ix % fx]
        d = lattice[(iy + 1) % fy, (ix + 1) % fx]
        return ((a * (1 - tx) + b * tx) * (1 - ty) +
                (c * (1 - tx) + d * tx) * ty).astype(np.float32)

    def fbm(self, frequencies=(4, 13, 37, 97), weights=(0.43, 0.29, 0.19, 0.09)):
        result = np.zeros_like(self.u)
        for frequency, weight in zip(frequencies, weights):
            result += self.noise(frequency) * weight
        return result

    def grain(self):
        return self.rng.random((self.n, self.n), dtype=np.float32)

    def scratches(self):
        result = np.zeros_like(self.u)
        for _ in range(40):
            center = self.rng.random(2)
            angle = self.rng.uniform(-0.8, 0.8)
            length = self.rng.uniform(0.025, 0.11)
            width = self.rng.uniform(0.0005, 0.0011)
            dx = (self.u - center[0] + 0.5) % 1 - 0.5
            dy = (self.v - center[1] + 0.5) % 1 - 0.5
            along = dx * math.cos(angle) + dy * math.sin(angle)
            across = -dx * math.sin(angle) + dy * math.cos(angle)
            end_fade = np.clip((length * 0.5 - np.abs(along)) / 0.008, 0, 1)
            scratch = np.exp(-(across / width) ** 2) * end_fade
            result = np.maximum(result, scratch * self.rng.uniform(0.3, 1.0))
        return result.astype(np.float32)


def pad_period(array):
    return np.pad(array, ((0, 1), (0, 1)) + ((0, 0),) * (array.ndim - 2), mode="wrap")


def normal_from_height(height, height_meters):
    # Central differences wrap on the torus. One UV represents one metre.
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * height.shape[1] * 0.5 * height_meters
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * height.shape[0] * 0.5 * height_meters
    normals = np.stack((-dx, -dy, np.ones_like(height)), axis=2)
    normals /= np.linalg.norm(normals, axis=2, keepdims=True)
    return normals * 0.5 + 0.5


def rgba(rgb, alpha=1.0):
    result = np.empty((*rgb.shape[:2], 4), dtype=np.float32)
    result[:, :, :3] = rgb
    result[:, :, 3] = alpha
    return result


def material(name, index):
    surface = PeriodicSurface(SEED + index * 1009)
    u, v = surface.u, surface.v
    macro = surface.fbm()
    medium = surface.noise(43)
    fine = surface.grain()
    metallic = np.zeros_like(u)

    if name == "Stone":
        pores = np.clip((surface.noise(117) - 0.69) / 0.24, 0, 1) ** 2
        variation = 0.105 * (macro - 0.5) + 0.045 * (medium - 0.5) + 0.014 * (fine - 0.5) - pores * 0.032
        base = (0.79, 0.775, 0.75)
        height = macro * 0.48 + medium * 0.25 + fine * 0.07 - pores * 0.34
        depth = 0.00125
        smoothness = np.clip(0.115 + (macro - 0.5) * 0.11 - pores * 0.04, 0.05, 0.2)
    elif name == "Dirt":
        grains = surface.noise(139)
        variation = (macro - 0.5) * 0.095 + (grains - 0.5) * 0.045 + (fine - 0.5) * 0.025
        base = (0.78, 0.755, 0.71)
        height = macro * 0.3 + medium * 0.16 + grains * 0.34 + fine * 0.2
        depth = 0.00065
        smoothness = np.clip(0.09 + (medium - 0.5) * 0.07, 0.05, 0.15)
    elif name == "Metal":
        scratches = surface.scratches()
        wear = surface.noise(103)
        variation = (macro - 0.5) * 0.04 + (wear - 0.5) * 0.022 + (fine - 0.5) * 0.007 - scratches * 0.026
        base = (0.825, 0.825, 0.82)
        height = medium * 0.18 + wear * 0.14 + fine * 0.05 - scratches * 0.34
        depth = 0.00035
        metallic = np.clip(0.84 + (macro - 0.5) * 0.075, 0.78, 0.9)
        smoothness = np.clip(0.43 + (wear - 0.5) * 0.095 - scratches * 0.075, 0.3, 0.55)
    elif name == "Leather":
        grain = surface.noise(109)
        pores = np.clip((grain - 0.61) / 0.34, 0, 1) ** 2
        variation = (macro - 0.5) * 0.065 + (grain - 0.5) * 0.025 + (fine - 0.5) * 0.01 - pores * 0.024
        base = (0.79, 0.775, 0.75)
        height = macro * 0.24 + medium * 0.26 + grain * 0.24 + fine * 0.08 - pores * 0.24
        depth = 0.00075
        smoothness = np.clip(0.18 + (macro - 0.5) * 0.11 - pores * 0.045, 0.08, 0.25)
    elif name == "Cloth":
        warp = surface.noise(4)
        wu = u + (warp - 0.5) * 0.0025
        wv = v + (surface.noise(5) - 0.5) * 0.0025
        horizontal = (0.5 + 0.5 * np.cos(wv * math.tau * 96)) ** 2
        vertical = (0.5 + 0.5 * np.cos(wu * math.tau * 96)) ** 2
        over = 0.5 + 0.5 * np.sin(u * math.tau * 48) * np.sin(v * math.tau * 48)
        weave = np.maximum(horizontal * (0.7 + 0.3 * over), vertical * (1 - 0.3 * over))
        variation = (macro - 0.5) * 0.05 + (weave - 0.5) * 0.026 + (fine - 0.5) * 0.01
        base = (0.845, 0.835, 0.815)
        height = weave * 0.64 + macro * 0.2 + fine * 0.075
        depth = 0.0005
        smoothness = np.clip(0.085 + (macro - 0.5) * 0.065, 0.05, 0.13)
    elif name == "Wood":
        warp = (surface.noise((5, 3)) - 0.5) * 0.024 + np.sin(v * math.tau * 2) * 0.014
        phase = u + warp
        rings = (0.5 + 0.5 * np.cos(math.tau * (phase * 18 + np.sin(v * math.tau * 3) * 0.12))) ** 5
        fibres = (0.5 + 0.5 * np.cos(math.tau * (phase * 73 + np.sin(v * math.tau * 5) * 0.2))) ** 7
        variation = (macro - 0.5) * 0.055 - rings * 0.047 - fibres * 0.018 + (fine - 0.5) * 0.008
        base = (0.84, 0.815, 0.77)
        height = macro * 0.26 - rings * 0.24 - fibres * 0.17 + fine * 0.04
        depth = 0.0008
        smoothness = np.clip(0.145 + (macro - 0.5) * 0.085 - rings * 0.025, 0.07, 0.23)
    else:
        raise ValueError(name)

    albedo = np.clip(np.array(base, dtype=np.float32)[None, None, :] + variation[:, :, None], 0.6, 0.9)
    normal = normal_from_height(height, depth)
    packed = np.zeros((*u.shape, 4), dtype=np.float32)
    packed[:, :, 0] = metallic
    packed[:, :, 3] = smoothness
    maps = {
        "Albedo": pad_period(rgba(albedo)),
        "Normal": pad_period(rgba(normal)),
        "MetallicSmoothness": pad_period(packed),
    }
    return maps, depth


def save_png(path, data):
    assert data.shape == (SIZE, SIZE, 4), data.shape
    assert np.isfinite(data).all() and data.min() >= 0 and data.max() <= 1
    assert np.array_equal(data[0], data[-1]) and np.array_equal(data[:, 0], data[:, -1])
    image = bpy.data.images.new(path.stem, width=SIZE, height=SIZE, alpha=True, float_buffer=False)
    # Preserve the authored PNG channel values; Unity chooses sRGB only for Albedo.
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(np.ascontiguousarray(data).ravel())
    image.file_format = "PNG"
    image.filepath_raw = str(path)
    image.save()
    bpy.data.images.remove(image)
    loaded = bpy.data.images.load(str(path), check_existing=False)
    loaded.colorspace_settings.name = "Non-Color"
    pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    loaded.pixels.foreach_get(pixels)
    pixels = pixels.reshape(SIZE, SIZE, 4)
    bpy.data.images.remove(loaded)
    assert np.max(np.abs(pixels - data)) <= 1 / 255 + 1e-6, path
    assert np.array_equal(pixels[0], pixels[-1]) and np.array_equal(pixels[:, 0], pixels[:, -1]), path
    return {
        "file": path.name,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "bytes": path.stat().st_size,
        "channel_min": pixels.min(axis=(0, 1)).tolist(),
        "channel_max": pixels.max(axis=(0, 1)).tolist(),
        "opposite_edge_max_error": 0,
    }


def save_preview(path, materials):
    cell = 256
    preview = np.ones((cell * 2, cell * 3, 4), dtype=np.float32)
    for index, data in enumerate(materials):
        row, column = divmod(index, 3)
        preview[row * cell:(row + 1) * cell, column * cell:(column + 1) * cell] = data[::2, ::2]
    image = bpy.data.images.new("PreviewTexturasNeutras", width=cell * 3, height=cell * 2, alpha=True)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(np.ascontiguousarray(preview).ravel())
    image.file_format = "PNG"
    image.filepath_raw = str(path)
    image.save()
    bpy.data.images.remove(image)


def main():
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=PROJECT / "Unity/Assets/Art/Textures")
    parser.add_argument("--report", type=Path, default=WORK / "texture-generation-report.json")
    parser.add_argument("--preview", type=Path, default=WORK / "texture-albedos-preview.png")
    options = parser.parse_args(arguments)
    for directory in (options.output, options.report.parent, options.preview.parent):
        directory.mkdir(parents=True, exist_ok=True)
    names = ("Stone", "Dirt", "Metal", "Leather", "Cloth", "Wood")
    report = {
        "seed": SEED, "size": SIZE, "unique_period_texels": SIZE - 1,
        "original_procedural_assets": True,
        "source": "bpy + numpy; periodic quintic value noise, analytic weave/veins and wrapped height gradients",
        "import": {"Albedo": "sRGB", "Normal": "Normal map, tangent RGB, sRGB disabled",
                   "MetallicSmoothness": "Default, sRGB disabled; R metallic, G/B zero, A smoothness"},
        "preview_order_bottom_to_top": [["Stone", "Dirt", "Metal"], ["Leather", "Cloth", "Wood"]],
        "materials": [],
    }
    previews = []
    for index, name in enumerate(names):
        maps, depth = material(name, index)
        entry = {"name": name, "height_scale_meters": depth, "maps": []}
        for suffix, data in maps.items():
            if suffix == "MetallicSmoothness":
                assert np.count_nonzero(data[:, :, 1:3]) == 0
                if name != "Metal":
                    assert np.count_nonzero(data[:, :, 0]) == 0
            path = options.output / f"{name}_{suffix}.png"
            entry["maps"].append(save_png(path, data))
        normals = maps["Normal"][:, :, :3] * 2 - 1
        entry["normal_unit_error_max"] = float(np.max(np.abs(np.linalg.norm(normals, axis=2) - 1)))
        entry["normal_tilt_degrees_max"] = float(np.degrees(np.arccos(np.clip(normals[:, :, 2].min(), -1, 1))))
        assert entry["normal_unit_error_max"] < 1e-5
        report["materials"].append(entry)
        previews.append(maps["Albedo"])
        print(f"[TEXTURE PASS] {name}: 3 PNGs {SIZE}x{SIZE}, seamless, normal tilt max={entry['normal_tilt_degrees_max']:.2f}°")
    save_preview(options.preview, previews)
    options.report.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"[TEXTURES PASS] {len(names) * 3} PNGs originais verificados. Report: {options.report}")


if __name__ == "__main__":
    main()
