using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuerraDasNacoes
{
    // Small reusable silhouettes keep every equipment slot recognizable without external UI assets.
    public sealed class PrototypeEquipmentIcons : IDisposable
    {
        private const int Size = 80;
        private readonly Dictionary<EquipmentSlot, Texture2D> textures = new Dictionary<EquipmentSlot, Texture2D>();
        private readonly Color steel = new Color(0.67f, 0.73f, 0.75f);
        private readonly Color light = new Color(0.88f, 0.89f, 0.84f);
        private readonly Color shadow = new Color(0.21f, 0.25f, 0.27f);
        private readonly Color gold = new Color(0.76f, 0.62f, 0.35f);
        private Color[] pixels;
        private Texture2D silhouette;

        public Texture2D Silhouette
        {
            get
            {
                if (silhouette != null) return silhouette;
                const int width = 128, height = 300;
                Color[] image = new Color[width * height];
                // An original neutral paper doll, deliberately subdued behind the equipment slots.
                float[][] polygons = {
                    new float[] { 54, 239, 48, 225, 25, 215, 18, 196, 28, 178, 39, 202, 42, 160, 37, 130, 43, 113, 85, 113, 91, 130, 86, 160, 89, 202, 100, 178, 110, 196, 103, 215, 80, 225, 74, 239 },
                    new float[] { 37, 130, 32, 85, 38, 40, 35, 17, 43, 9, 57, 10, 61, 43, 64, 91, 67, 43, 71, 10, 85, 9, 93, 17, 90, 40, 96, 85, 91, 130 },
                    new float[] { 18, 196, 12, 158, 6, 125, 10, 110, 18, 109, 23, 119, 28, 158, 32, 181 },
                    new float[] { 110, 196, 116, 158, 122, 125, 118, 110, 110, 109, 105, 119, 100, 158, 96, 181 }
                };
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        bool inside = ((x - 64f) * (x - 64f) / 225f + (y - 260f) * (y - 260f) / 484f) <= 1f;
                        foreach (float[] polygon in polygons)
                        {
                            bool polygonInside = false;
                            for (int i = 0, j = polygon.Length - 2; i < polygon.Length; j = i, i += 2)
                                if ((polygon[i + 1] > y) != (polygon[j + 1] > y) &&
                                    x < (polygon[j] - polygon[i]) * (y - polygon[i + 1]) / (polygon[j + 1] - polygon[i + 1]) + polygon[i]) polygonInside = !polygonInside;
                            inside |= polygonInside;
                        }
                        if (!inside) continue;
                        float shade = .8f + .2f * Mathf.Cos((x - 64f) / 50f);
                        image[y * width + x] = new Color(.17f * shade, .22f * shade, .23f * shade, .70f);
                    }
                silhouette = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Silhueta de equipamentos", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                silhouette.SetPixels(image);
                silhouette.Apply(false, true);
                return silhouette;
            }
        }

        public Texture2D Get(EquipmentSlot slot)
        {
            if (textures.TryGetValue(slot, out Texture2D existing)) return existing;
            pixels = new Color[Size * Size];
            switch (slot)
            {
                case EquipmentSlot.Sword:
                    Polygon(steel, 23, 19, 57, 67, 67, 74, 64, 61, 31, 15);
                    Line(29, 22, 62, 68, 1.5f, light);
                    Line(17, 29, 39, 14, 3f, gold);
                    Line(19, 12, 27, 23, 3f, shadow);
                    Disc(17, 10, 4f, gold);
                    break;
                case EquipmentSlot.Shield:
                    Polygon(gold, 15, 64, 40, 73, 65, 64, 62, 29, 40, 9, 18, 29);
                    Polygon(steel, 20, 60, 40, 67, 60, 60, 57, 32, 40, 16, 23, 32);
                    Line(40, 21, 40, 63, 2f, light);
                    Polygon(shadow, 29, 45, 40, 55, 51, 45, 40, 30);
                    break;
                case EquipmentSlot.Head:
                    Polygon(steel, 17, 20, 18, 52, 29, 67, 50, 67, 62, 52, 63, 20, 51, 13, 40, 24, 29, 13);
                    Polygon(shadow, 22, 43, 36, 39, 36, 34, 23, 35);
                    Polygon(shadow, 44, 39, 58, 43, 57, 35, 44, 34);
                    Line(40, 24, 40, 65, 2f, light);
                    Line(22, 51, 58, 51, 2f, gold);
                    break;
                case EquipmentSlot.Shoulders:
                    Polygon(steel, 5, 30, 9, 54, 22, 62, 34, 53, 31, 28, 18, 22);
                    Polygon(steel, 46, 53, 58, 62, 71, 54, 75, 30, 62, 22, 49, 28);
                    Line(9, 35, 29, 32, 2f, gold);
                    Line(51, 32, 71, 35, 2f, gold);
                    break;
                case EquipmentSlot.Chest:
                    Polygon(steel, 13, 61, 28, 68, 33, 58, 47, 58, 52, 68, 67, 61, 59, 43, 57, 14, 23, 14, 21, 43);
                    Line(40, 18, 40, 53, 2f, light);
                    Line(25, 24, 55, 24, 3f, gold);
                    Line(23, 45, 38, 38, 2f, shadow);
                    Line(42, 38, 57, 45, 2f, shadow);
                    break;
                case EquipmentSlot.Hands:
                    Polygon(steel, 9, 48, 29, 52, 35, 34, 31, 19, 14, 16, 7, 30, 9, 39, 15, 35);
                    Polygon(steel, 51, 52, 71, 48, 71, 39, 65, 35, 73, 30, 66, 16, 49, 19, 45, 34);
                    Line(10, 48, 28, 51, 3f, gold);
                    Line(52, 51, 70, 48, 3f, gold);
                    Line(19, 23, 20, 41, 1f, shadow);
                    Line(60, 23, 60, 41, 1f, shadow);
                    break;
                case EquipmentSlot.Legs:
                    Polygon(steel, 21, 67, 59, 67, 62, 45, 55, 13, 42, 13, 40, 42, 38, 13, 25, 13, 18, 45);
                    Line(23, 60, 57, 60, 3f, gold);
                    Line(29, 20, 29, 44, 2f, light);
                    Line(51, 20, 51, 44, 2f, light);
                    break;
                case EquipmentSlot.Feet:
                    Polygon(steel, 15, 66, 33, 66, 33, 30, 38, 17, 33, 11, 8, 11, 8, 21, 15, 29);
                    Polygon(steel, 47, 66, 65, 66, 65, 29, 72, 21, 72, 11, 47, 11, 42, 17, 47, 30);
                    Line(16, 58, 32, 58, 3f, gold);
                    Line(48, 58, 64, 58, 3f, gold);
                    Line(12, 17, 31, 17, 2f, shadow);
                    Line(49, 17, 68, 17, 2f, shadow);
                    break;
                case EquipmentSlot.RingLeft:
                case EquipmentSlot.RingRight:
                    Ring(40, 33, 20f, 6f, gold);
                    Polygon(light, 27, 56, 40, 68, 53, 56, 40, 45);
                    Polygon(new Color(0.25f, 0.50f, 0.58f), 32, 56, 40, 63, 48, 56, 40, 49);
                    break;
                case EquipmentSlot.Belt:
                    Polygon(shadow, 6, 53, 74, 53, 74, 27, 6, 27);
                    Line(7, 49, 73, 49, 2f, gold);
                    Line(7, 31, 73, 31, 2f, gold);
                    Polygon(gold, 27, 57, 53, 57, 53, 23, 27, 23);
                    Polygon(shadow, 33, 50, 47, 50, 47, 30, 33, 30);
                    Line(40, 40, 52, 40, 2f, light);
                    break;
                case EquipmentSlot.Necklace:
                    for (int i = 0; i < 17; i++)
                    {
                        float angle = (20f + i * 8.75f) * Mathf.Deg2Rad;
                        Disc(40f + Mathf.Cos(angle) * 25f, 59f - Mathf.Sin(angle) * 30f, 2.2f, gold);
                    }
                    Polygon(gold, 28, 24, 40, 36, 52, 24, 40, 8);
                    Polygon(light, 34, 24, 40, 30, 46, 24, 40, 15);
                    break;
            }
            // A restrained metal bevel and vertical sheen make silhouettes readable at inventory size.
            Color[] shaded = new Color[pixels.Length];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int index = y * Size + x;
                    Color color = pixels[index];
                    if (color.a <= 0f) continue;
                    bool lowerEdge = y == 0 || x == Size - 1 || pixels[(Mathf.Max(0, y - 1)) * Size + x].a <= 0f || pixels[y * Size + Mathf.Min(Size - 1, x + 1)].a <= 0f;
                    bool upperEdge = y == Size - 1 || x == 0 || pixels[Mathf.Min(Size - 1, y + 1) * Size + x].a <= 0f || pixels[y * Size + Mathf.Max(0, x - 1)].a <= 0f;
                    float sheen = .79f + y / (float)Size * .30f + Mathf.Sin((x + y) * .14f) * .035f;
                    color = new Color(color.r * sheen, color.g * sheen, color.b * sheen, color.a);
                    if (lowerEdge) color = Color.Lerp(color, shadow, .55f);
                    else if (upperEdge) color = Color.Lerp(color, light, .45f);
                    shaded[index] = color;
                }
            pixels = shaded;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Ícone " + slot, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            textures.Add(slot, texture);
            pixels = null;
            return texture;
        }

        private void Polygon(Color color, params float[] vertices)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool inside = false;
                    int j = vertices.Length - 2;
                    for (int i = 0; i < vertices.Length; i += 2)
                    {
                        float xi = vertices[i], yi = vertices[i + 1], xj = vertices[j], yj = vertices[j + 1];
                        if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                        j = i;
                    }
                    if (inside) pixels[y * Size + x] = color;
                }
        }

        private void Disc(float cx, float cy, float radius, Color color) => Ring(cx, cy, radius * 0.5f, radius, color);

        private void Ring(float cx, float cy, float radius, float thickness, Color color)
        {
            float outer = radius + thickness * 0.5f, inner = Mathf.Max(0f, radius - thickness * 0.5f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float distance = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (distance <= outer * outer && distance >= inner * inner) pixels[y * Size + x] = color;
                }
        }

        private void Line(float x1, float y1, float x2, float y2, float radius, Color color)
        {
            float dx = x2 - x1, dy = y2 - y1, lengthSquared = dx * dx + dy * dy;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float t = Mathf.Clamp01(((x - x1) * dx + (y - y1) * dy) / lengthSquared);
                    float px = x1 + dx * t - x, py = y1 + dy * t - y;
                    if (px * px + py * py <= radius * radius) pixels[y * Size + x] = color;
                }
        }

        public void Dispose()
        {
            foreach (Texture2D texture in textures.Values) UnityEngine.Object.Destroy(texture);
            textures.Clear();
            if (silhouette != null) UnityEngine.Object.Destroy(silhouette);
            silhouette = null;
        }
    }
}
