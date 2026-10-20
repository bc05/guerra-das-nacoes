using UnityEngine;

namespace GuerraDasNacoes
{
    // Small original icons, rasterized once from simple shapes; no external assets or fonts.
    internal sealed class PrototypeSkillIcons
    {
        private const int Size = 96;
        private readonly Texture2D[] icons = new Texture2D[5];
        private readonly Texture2D[] cooldownMasks = new Texture2D[25];
        private static readonly Color Steel = new Color(0.78f, 0.84f, 0.87f);
        private static readonly Color Bronze = new Color(0.63f, 0.47f, 0.26f);

        public PrototypeSkillIcons()
        {
            for (int index = 0; index < icons.Length; index++) icons[index] = CreateIcon(index);
            for (int step = 1; step < cooldownMasks.Length; step++) cooldownMasks[step] = CreateMask(step / 24f);
        }

        public Texture2D Get(int slot) => slot >= 0 && slot < icons.Length ? icons[slot] : null;
        public Texture2D Cooldown(float fraction) => cooldownMasks[Mathf.Clamp(Mathf.CeilToInt(fraction * 24f), 0, 24)];

        public void Dispose()
        {
            foreach (Texture2D icon in icons) if (icon != null) Object.Destroy(icon);
            foreach (Texture2D mask in cooldownMasks) if (mask != null) Object.Destroy(mask);
        }

        private static Texture2D CreateIcon(int index)
        {
            var pixels = new Color[Size * Size];
            Color tint = index == 0 ? new Color(0.12f, 0.29f, 0.38f) : index == 1 ? new Color(0.35f, 0.20f, 0.10f)
                : index == 2 ? new Color(0.15f, 0.22f, 0.32f) : index == 3 ? new Color(0.11f, 0.27f, 0.21f) : new Color(0.47f, 0.19f, 0.04f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size);
                    float vignette = Mathf.Clamp01(1f - Vector2.Distance(p, Vector2.one * 0.5f) * 1.4f);
                    float grain = Mathf.Sin(x * 12.9898f + y * 78.233f) * 0.008f;
                    Color color = Color.Lerp(new Color(0.025f, 0.032f, 0.04f), tint, vignette * 0.7f);
                    color += new Color(grain, grain, grain, 0f);
                    if (index < 2 || index == 4)
                    {
                        // Diagonal blade with a bevel, crossguard and leather grip.
                        Vector2 direction = new Vector2(0.66f, 0.75f).normalized;
                        Vector2 across = new Vector2(-direction.y, direction.x);
                        Vector2 local = p - new Vector2(0.43f, 0.40f);
                        float along = Vector2.Dot(local, direction), side = Vector2.Dot(local, across);
                        float half = index == 0 ? 0.043f : 0.067f;
                        float edge = Mathf.Min(half - Mathf.Abs(side), Mathf.Min(along - 0.025f, 0.48f - along - Mathf.Abs(side) * 1.6f));
                        Color blade = index == 4 ? new Color(1f, 0.83f, 0.39f) : Steel;
                        Paint(ref color, Color.Lerp(new Color(0.33f, 0.44f, 0.51f), blade, Mathf.Clamp01(0.5f + side / half)), edge);
                        Paint(ref color, Bronze, Mathf.Min(0.16f - Mathf.Abs(side), 0.026f - Mathf.Abs(along)));
                        Paint(ref color, new Color(0.24f, 0.15f, 0.09f), Mathf.Min(0.027f - Mathf.Abs(side), Mathf.Min(-0.035f - along, along + 0.20f)));
                        Paint(ref color, Steel, 0.044f - Vector2.Distance(p, new Vector2(0.43f, 0.40f) - direction * 0.21f));
                        if (index == 0)
                        {
                            float rim = 0.012f - DistanceToSegment(p, new Vector2(0.28f, 0.34f), new Vector2(0.63f, 0.88f));
                            Paint(ref color, new Color(0.40f, 0.82f, 0.94f), rim);
                        }
                        else if (index == 1)
                        {
                            Vector2 q = p - new Vector2(0.54f, 0.55f);
                            float radius = q.magnitude;
                            if (q.x > -0.08f && q.y > -0.32f)
                                Paint(ref color, new Color(0.94f, 0.65f, 0.30f), 0.017f - Mathf.Abs(radius - 0.31f));
                        }
                        else
                        {
                            // Amber coils around the blade identify the sustained damage enchantment.
                            if (along > -0.04f && along < 0.45f)
                            {
                                float coil = Mathf.Sin(along * 34f) * 0.12f;
                                Paint(ref color, new Color(1f, 0.43f, 0.10f), 0.022f - Mathf.Abs(side - coil));
                                Paint(ref color, new Color(1f, 0.91f, 0.52f), 0.009f - Mathf.Abs(side - coil));
                            }
                            Paint(ref color, new Color(1f, 0.72f, 0.24f), 0.012f - DistanceToSegment(p, new Vector2(0.25f, 0.53f), new Vector2(0.19f, 0.75f)));
                            Paint(ref color, new Color(1f, 0.72f, 0.24f), 0.012f - DistanceToSegment(p, new Vector2(0.71f, 0.31f), new Vector2(0.84f, 0.47f)));
                        }
                    }
                    else if (index == 2)
                    {
                        float shape = ShieldDistance(p);
                        Paint(ref color, Bronze, shape);
                        Paint(ref color, new Color(0.28f, 0.37f, 0.46f), shape - 0.033f);
                        Paint(ref color, Steel, Mathf.Min(0.017f - Mathf.Abs(p.x - 0.5f), Mathf.Min(p.y - 0.28f, 0.77f - p.y)));
                        Paint(ref color, Steel, Mathf.Min(0.018f - Mathf.Abs(p.y - 0.61f), 0.18f - Mathf.Abs(p.x - 0.5f)));
                    }
                    else
                    {
                        float cross = Mathf.Max(Mathf.Min(0.045f - Mathf.Abs(p.x - 0.5f), 0.21f - Mathf.Abs(p.y - 0.53f)),
                            Mathf.Min(0.045f - Mathf.Abs(p.y - 0.53f), 0.21f - Mathf.Abs(p.x - 0.5f)));
                        Paint(ref color, new Color(0.72f, 0.88f, 0.67f), cross);
                        Vector2 q = p - new Vector2(0.5f, 0.5f);
                        if (q.y < 0.18f) Paint(ref color, new Color(0.41f, 0.64f, 0.40f), 0.014f - Mathf.Abs(q.magnitude - 0.30f));
                    }
                    color.a = 1f;
                    pixels[y * Size + x] = color;
                }
            return Finish(pixels, "IconeHabilidade" + index, Size);
        }

        private static float ShieldDistance(Vector2 p)
        {
            float half = Mathf.Lerp(0.04f, 0.27f, Mathf.Clamp01((p.y - 0.17f) / 0.34f));
            return Mathf.Min(half - Mathf.Abs(p.x - 0.5f), Mathf.Min(p.y - 0.16f, 0.81f - p.y));
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            return Vector2.Distance(p, a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude));
        }

        private static void Paint(ref Color background, Color foreground, float distance)
        {
            background = Color.Lerp(background, foreground, Mathf.Clamp01(distance * Size + 0.5f));
        }

        private static Texture2D CreateMask(float fraction)
        {
            const int size = 64;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float angle = Mathf.Atan2(x - 31.5f, y - 31.5f) / (Mathf.PI * 2f);
                    if (angle < 0f) angle += 1f;
                    pixels[y * size + x] = new Color(0.005f, 0.008f, 0.012f, angle < fraction ? 0.76f : 0f);
                }
            return Finish(pixels, "Recarga" + fraction, size);
        }

        private static Texture2D Finish(Color[] pixels, string name, int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
