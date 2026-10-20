using UnityEngine;
using UnityEngine.Rendering;

namespace GuerraDasNacoes
{
    // Persistent, deliberately sparse auras: the armour and enemy telegraphs stay readable.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1010)]
    public sealed class PrototypeStatusEffects : MonoBehaviour
    {
        private const float HealingDuration = 1.2f;
        private static readonly int TintProperty = Shader.PropertyToID("_Color");
        [SerializeField] private Material glowMaterial;
        private PrototypeAbilities abilities;
        private PrototypeCombat combat;
        private GameObject effectsRoot;
        private AuraMesh guard;
        private AuraMesh fury;
        private AuraMesh healing;
        private Material ownedMaterial;
        private Transform swordBase;
        private Transform swordTip;
        private float nextAnchorSearch;
        private float healingStartedAt = -100f;

        public bool GuardVisible => IsVisible(guard);
        public bool DamageBoostVisible => IsVisible(fury);
        public bool HealingVisible => IsVisible(healing);
        public bool IsConfigured => glowMaterial != null;

        public void Configure(Material material)
        {
            if (material == null)
                return;
            glowMaterial = material;
            ApplyMaterial();
        }

        private void Awake()
        {
            abilities = GetComponent<PrototypeAbilities>();
            combat = GetComponent<PrototypeCombat>();
            EnsureInitialized();
            Cancel();
        }

        public void PlayHealing()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;
            EnsureInitialized();
            healingStartedAt = Time.time;
            UpdateVisuals();
        }

        public void Cancel()
        {
            healingStartedAt = -100f;
            SetVisible(guard, false);
            SetVisible(fury, false);
            SetVisible(healing, false);
        }

        private void LateUpdate()
        {
            UpdateVisuals();
        }

        public void RefreshActiveEffects()
        {
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (abilities == null)
                abilities = GetComponent<PrototypeAbilities>();
            if (combat == null)
                combat = GetComponent<PrototypeCombat>();
            if (!isActiveAndEnabled || abilities == null || !abilities.isActiveAndEnabled || combat == null || !combat.IsAlive)
            {
                Cancel();
                return;
            }
            EnsureInitialized();
            bool showGuard = abilities.IsGuarding && glowMaterial != null;
            bool showFury = abilities.IsDamageBoosted && glowMaterial != null;
            bool showHealing = Time.time - healingStartedAt < HealingDuration && glowMaterial != null;
            SetVisible(guard, showGuard);
            SetVisible(fury, showFury);
            SetVisible(healing, showHealing);
            if (showGuard) DrawGuard();
            if (showFury) DrawFury();
            if (showHealing) DrawHealing();
        }

        private void DrawGuard()
        {
            guard.Begin();
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 3f);
            Color blue = new Color(0.12f, 0.7f, 1.5f, 0.52f * pulse);
            Color faint = new Color(0.07f, 0.4f, 0.95f, 0.23f * pulse);
            DrawRing(guard, 0.61f, 0.065f, 0.014f, 64, Time.time * 0.15f, blue);
            DrawRing(guard, 0.49f, 0.07f, 0.009f, 48, -Time.time * 0.1f, faint);
            // Six small runes rotate on the floor, rather than a solid disc hiding the feet.
            for (int index = 0; index < 6; index++)
            {
                float angle = index * Mathf.PI / 3f + Time.time * 0.15f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 side = Vector3.Cross(Vector3.up, radial);
                Vector3 center = radial * 0.56f + Vector3.up * 0.075f;
                guard.Line(center - radial * 0.045f, center + radial * 0.045f, side, 0.014f, blue);
                guard.Line(center - side * 0.025f, center + side * 0.025f + radial * 0.03f, radial, 0.012f, blue);
            }
            // Outline of a ward floats in front of the torso, with no opaque shield surface.
            Vector3 centerWard = new Vector3(0f, 1.05f, 0.48f);
            for (int edge = 0; edge < 6; edge++)
            {
                float a = (edge * 60f + 30f) * Mathf.Deg2Rad;
                float b = ((edge + 1) * 60f + 30f) * Mathf.Deg2Rad;
                Vector3 p = centerWard + new Vector3(Mathf.Cos(a) * 0.31f, Mathf.Sin(a) * 0.4f, 0f);
                Vector3 q = centerWard + new Vector3(Mathf.Cos(b) * 0.31f, Mathf.Sin(b) * 0.4f, 0f);
                guard.Line(p, q, Vector3.Cross(q - p, Vector3.forward), 0.018f, blue);
            }
            guard.Line(centerWard + Vector3.down * 0.16f, centerWard + Vector3.up * 0.16f, Vector3.right, 0.019f, blue);
            guard.Line(centerWard + Vector3.left * 0.1f, centerWard + Vector3.right * 0.1f, Vector3.up, 0.018f, blue);
            guard.Commit();
        }

        private void DrawFury()
        {
            fury.Begin();
            Color amber = new Color(1.5f, 0.62f, 0.09f, 0.6f);
            Color ember = new Color(1.25f, 0.3f, 0.035f, 0.3f);
            // Broken circular sigil and three fine ascending strands remain visible for the whole buff.
            for (int segment = 0; segment < 6; segment++)
                DrawArc(fury, 0.54f, 0.08f, 0.017f, 8, segment * 60f + Time.time * 12f, 44f, amber);
            for (int strand = 0; strand < 3; strand++)
            {
                for (int part = 0; part < 12; part++)
                {
                    float progress = part / 12f;
                    float nextProgress = (part + 1) / 12f;
                    float angle = Time.time * 1.8f + strand * Mathf.PI * 2f / 3f;
                    Vector3 a = HelixPoint(progress, angle);
                    Vector3 b = HelixPoint(nextProgress, angle);
                    float fade = Mathf.Sin((progress + 0.03f) * Mathf.PI);
                    Color color = ember;
                    color.a *= Mathf.Max(0.12f, fade);
                    fury.Line(a, b, new Vector3(a.x, 0f, a.z), 0.026f, color);
                }
            }
            if ((swordBase == null || swordTip == null) && Time.time >= nextAnchorSearch)
            {
                swordBase = FindChild(transform, "SwordBase");
                swordTip = FindChild(transform, "SwordTip");
                nextAnchorSearch = Time.time + 0.5f;
            }
            if (swordBase != null && swordTip != null && combat.Stats.EquippedWeapon != null)
            {
                Vector3 a = transform.InverseTransformPoint(swordBase.position);
                Vector3 b = transform.InverseTransformPoint(swordTip.position);
                Vector3 blade = b - a;
                if (blade.sqrMagnitude > 0.01f)
                {
                    Vector3 across = Vector3.Cross(blade, Vector3.forward);
                    if (across.sqrMagnitude < 0.01f) across = Vector3.Cross(blade, Vector3.right);
                    fury.Line(a, b, across, 0.025f, amber);
                    fury.Line(a, b, Vector3.Cross(blade, across), 0.025f, amber);
                }
            }
            fury.Commit();
        }

        private static Vector3 HelixPoint(float progress, float angle)
        {
            angle += progress * 2.4f;
            float radius = Mathf.Lerp(0.33f, 0.42f, Mathf.Sin(progress * Mathf.PI));
            return new Vector3(Mathf.Cos(angle) * radius, 0.22f + progress * 1.22f, Mathf.Sin(angle) * radius);
        }

        private void DrawHealing()
        {
            healing.Begin();
            float progress = Mathf.Clamp01((Time.time - healingStartedAt) / HealingDuration);
            float fade = Mathf.Sin(progress * Mathf.PI);
            Color green = new Color(0.2f, 1.35f, 0.46f, 0.6f * fade);
            DrawRing(healing, 0.42f + progress * 0.2f, 0.09f + progress * 0.2f, 0.023f, 48, 0f, green);
            DrawRing(healing, 0.36f, 0.2f + progress * 1.35f, 0.018f, 48, 0f, green);
            for (int spark = 0; spark < 8; spark++)
            {
                float angle = spark * Mathf.PI * 0.25f;
                float height = Mathf.Repeat(progress * 1.8f + spark * 0.17f, 1.5f) + 0.2f;
                Vector3 center = new Vector3(Mathf.Cos(angle) * 0.43f, height, Mathf.Sin(angle) * 0.43f);
                healing.Line(center - Vector3.up * 0.035f, center + Vector3.up * 0.035f, Vector3.right, 0.014f, green);
                healing.Line(center - Vector3.right * 0.025f, center + Vector3.right * 0.025f, Vector3.up, 0.014f, green);
            }
            healing.Commit();
        }

        private static void DrawRing(AuraMesh mesh, float radius, float height, float width, int segments, float angle, Color color)
        {
            DrawArc(mesh, radius, height, width, segments, angle * Mathf.Rad2Deg, 360f, color);
        }

        private static void DrawArc(AuraMesh mesh, float radius, float height, float width, int segments, float startDegrees, float spanDegrees, Color color)
        {
            float start = startDegrees * Mathf.Deg2Rad;
            float step = spanDegrees * Mathf.Deg2Rad / segments;
            for (int index = 0; index < segments; index++)
            {
                float a = start + index * step;
                float b = a + step;
                Vector3 p = new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius);
                Vector3 q = new Vector3(Mathf.Cos(b) * radius, height, Mathf.Sin(b) * radius);
                mesh.Line(p, q, Vector3.Cross(Vector3.up, q - p), width, color);
            }
        }

        private void EnsureInitialized()
        {
            if (effectsRoot != null)
                return;
            if (glowMaterial == null)
            {
                Shader shader = Shader.Find("GuerraDasNacoes/AttackGlow");
                if (shader != null)
                {
                    ownedMaterial = new Material(shader) { name = "Aura temporária (fallback)" };
                    ownedMaterial.SetColor(TintProperty, Color.white);
                    glowMaterial = ownedMaterial;
                }
            }
            effectsRoot = new GameObject("Efeitos persistentes");
            effectsRoot.transform.SetParent(transform, false);
            guard = new AuraMesh("Guarda · runa azul", effectsRoot.transform, 160);
            fury = new AuraMesh("Fúria · energia âmbar", effectsRoot.transform, 96);
            healing = new AuraMesh("Cura · energia verde", effectsRoot.transform, 120);
            ApplyMaterial();
        }

        private void ApplyMaterial()
        {
            if (effectsRoot == null)
                return;
            guard.Renderer.sharedMaterial = glowMaterial;
            fury.Renderer.sharedMaterial = glowMaterial;
            healing.Renderer.sharedMaterial = glowMaterial;
        }

        private static bool IsVisible(AuraMesh aura)
        {
            return aura != null && aura.Renderer != null && aura.Renderer.enabled && aura.Renderer.gameObject.activeInHierarchy;
        }

        private static void SetVisible(AuraMesh aura, bool visible)
        {
            if (aura != null && aura.Renderer != null)
                aura.Renderer.enabled = visible;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                Transform result = FindChild(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void OnDestroy()
        {
            if (guard != null) guard.Dispose();
            if (fury != null) fury.Dispose();
            if (healing != null) healing.Dispose();
            if (effectsRoot != null) Destroy(effectsRoot);
            if (ownedMaterial != null) Destroy(ownedMaterial);
        }

        private sealed class AuraMesh
        {
            public readonly MeshRenderer Renderer;
            private readonly Mesh mesh;
            private readonly Vector3[] vertices;
            private readonly Color[] colors;
            private int count;
            private int previousCount;

            public AuraMesh(string name, Transform parent, int maximumLines)
            {
                GameObject root = new GameObject(name);
                root.transform.SetParent(parent, false);
                Renderer = root.AddComponent<MeshRenderer>();
                Renderer.shadowCastingMode = ShadowCastingMode.Off;
                Renderer.receiveShadows = false;
                Renderer.lightProbeUsage = LightProbeUsage.Off;
                Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                Renderer.enabled = false;
                vertices = new Vector3[maximumLines * 4];
                colors = new Color[vertices.Length];
                int[] triangles = new int[maximumLines * 6];
                for (int line = 0; line < maximumLines; line++)
                {
                    int vertex = line * 4;
                    int triangle = line * 6;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + 1;
                    triangles[triangle + 2] = vertex + 2;
                    triangles[triangle + 3] = vertex + 2;
                    triangles[triangle + 4] = vertex + 1;
                    triangles[triangle + 5] = vertex + 3;
                }
                mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.triangles = triangles;
                mesh.bounds = new Bounds(Vector3.up, new Vector3(8f, 8f, 8f));
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
            }

            public void Begin() { count = 0; }

            public void Line(Vector3 from, Vector3 to, Vector3 across, float width, Color color)
            {
                if (count + 4 > vertices.Length)
                    return;
                Vector3 offset = across.normalized * (width * 0.5f);
                vertices[count] = from - offset;
                vertices[count + 1] = from + offset;
                vertices[count + 2] = to - offset;
                vertices[count + 3] = to + offset;
                colors[count] = color;
                colors[count + 1] = color;
                colors[count + 2] = color;
                colors[count + 3] = color;
                count += 4;
            }

            public void Commit()
            {
                // Only the sword can disappear/reappear. Degenerate its former quads without allocating arrays.
                for (int index = count; index < previousCount; index++)
                {
                    vertices[index] = Vector3.zero;
                    colors[index] = Color.clear;
                }
                mesh.vertices = vertices;
                mesh.colors = colors;
                previousCount = count;
            }

            public void Dispose() { Object.Destroy(mesh); }
        }
    }
}
