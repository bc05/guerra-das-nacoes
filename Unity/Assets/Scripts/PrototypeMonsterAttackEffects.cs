using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuerraDasNacoes
{
    // Presentation only. Range, damage and the decision to strike belong to the encounter/server.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public sealed class PrototypeMonsterAttackEffects : MonoBehaviour
    {
        private const float ImpactDelay = .065f;
        private const float ImpactLifetime = .78f;
        private readonly List<Vector3> vertices = new List<Vector3>(1400);
        private readonly List<Color> colors = new List<Color>(1400);
        private readonly List<Vector2> uvs = new List<Vector2>(1400);
        private readonly List<int> triangles = new List<int>(2100);
        private GameObject root;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Material material;
        private float radius = 1.65f;
        private float preparation;
        private string behavior = "melee";
        private float areaRadius = 3f;
        private Vector3 preparationCenter;
        private bool preparing, elite, impactHit;
        private float strikeStarted = -100f;
        private Vector3 impactPosition;
        private int strikeNumber;
        public bool TelegraphVisible => preparing && isActiveAndEnabled;
        public Vector3 TelegraphCenter => preparationCenter;
        public float TelegraphRadius => behavior == "melee" ? radius : areaRadius;
        public bool ImpactVisible => Time.time - strikeStarted >= ImpactDelay && Time.time - strikeStarted < ImpactLifetime;
        public int PresentedStrikes { get; private set; }

        public void Configure(float reach, bool isElite, string attackBehavior = "melee", float impactRadius = 3f)
        {
            radius = Mathf.Max(.1f, reach);
            elite = isElite;
            behavior = attackBehavior;
            areaRadius = Mathf.Max(.1f, impactRadius);
        }

        public void SetPreparation(bool active, float progress)
        {
            SetPreparation(active, progress, transform.position);
        }

        public void SetPreparation(bool active, float progress, Vector3 center)
        {
            preparing = active && isActiveAndEnabled;
            preparation = Mathf.Clamp01(progress);
            preparationCenter = behavior == "melee" ? transform.position : center;
        }

        public void PlayStrike(Vector3 position, bool hit)
        {
            if (!isActiveAndEnabled) return;
            EnsureInitialized();
            preparing = false;
            impactPosition = position;
            impactPosition.y = transform.position.y + .025f;
            strikeStarted = Time.time;
            impactHit = hit;
            strikeNumber++;
            PresentedStrikes++;
        }

        public void Cancel()
        {
            preparing = false;
            strikeStarted = -100f;
            if (meshRenderer != null) meshRenderer.enabled = false;
        }

        private void LateUpdate()
        {
            float age = Time.time - strikeStarted;
            bool showImpact = age >= ImpactDelay && age < ImpactLifetime;
            if (!preparing && !showImpact)
            {
                if (meshRenderer != null) meshRenderer.enabled = false;
                return;
            }
            EnsureInitialized();
            if (material == null) return;
            vertices.Clear(); colors.Clear(); uvs.Clear(); triangles.Clear();
            if (preparing) DrawPreparation();
            if (showImpact) DrawImpact(age - ImpactDelay);
            mesh.Clear(false);
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            meshRenderer.enabled = vertices.Count > 0;
        }

        private void DrawPreparation()
        {
            if (behavior != "melee") { DrawAreaPreparation(); return; }
            Vector3 center = transform.position + Vector3.up * .035f;
            Color edge = elite ? new Color(.8f, .37f, .1f, .7f) : new Color(.68f, .32f, .12f, .58f);
            edge.a *= Mathf.Lerp(.3f, 1f, preparation);
            // The broken circumference marks reach; it is not a filled area-damage warning.
            for (int segment = 0; segment < 64; segment++)
            {
                if (segment % 8 == 7) continue;
                float a = segment * Mathf.PI * 2f / 64f;
                float b = (segment + 1) * Mathf.PI * 2f / 64f;
                Line(center + Radial(a) * radius, center + Radial(b) * radius, .021f, edge);
            }
            float chargeRadius = radius * Mathf.Lerp(.38f, .98f, Mathf.SmoothStep(0f, 1f, preparation));
            Color charge = new Color(.65f, .31f, .12f, .11f + preparation * .16f);
            for (int i = 0; i < 48; i++)
                Line(center + Radial(i * Mathf.PI / 24f) * chargeRadius,
                    center + Radial((i + 1) * Mathf.PI / 24f) * chargeRadius, .035f, charge);
            // Three grounded scratches under the raised fists tie the cue to the physical strike.
            for (int claw = -1; claw <= 1; claw++)
            {
                Vector3 across = transform.right * claw * .14f;
                Line(center + across + transform.forward * .72f,
                    center + across + transform.forward * (1.02f + preparation * .25f), .028f, edge);
            }
        }

        private void DrawAreaPreparation()
        {
            Vector3 center = preparationCenter + Vector3.up * .045f;
            Color edge = behavior == "ranged"
                ? new Color(.28f, .68f, 1f, .9f) : new Color(1f, .38f, .06f, .9f);
            Color fill = edge; fill.a = .09f + preparation * .14f;
            Color charge = edge; charge.a = .45f + preparation * .35f;
            for (int segment = 0; segment < 64; segment++)
            {
                float a = segment * Mathf.PI * 2f / 64f;
                float b = (segment + 1) * Mathf.PI * 2f / 64f;
                Vector3 from = center + Radial(a) * areaRadius, to = center + Radial(b) * areaRadius;
                // The filled disc announces the actual damage area, including its fixed centre.
                AddQuad(center, from, center, to, fill, false);
                Line(from, to, .065f, edge);
                float closingRadius = areaRadius * (1f - preparation);
                Line(center + Radial(a) * closingRadius, center + Radial(b) * closingRadius, .045f, charge);
            }
            Line(center - Vector3.right * .15f, center + Vector3.right * .15f, .035f, edge);
            Line(center - Vector3.forward * .15f, center + Vector3.forward * .15f, .035f, edge);
        }

        private void DrawImpact(float age)
        {
            float life = age / (ImpactLifetime - ImpactDelay);
            float weight = elite ? 1.35f : 1f;
            float contact = Mathf.Clamp01(1f - age / .14f);
            Color contactColor = new Color(.95f, .65f, .28f, contact * (impactHit ? .65f : .42f));
            float maximumRadius = behavior == "melee" ? (elite ? 1.1f : .82f) : areaRadius;
            float ringRadius = Mathf.Lerp(.12f, maximumRadius, Mathf.Clamp01(age / .25f));
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI / 20f, b = (i + 1) * Mathf.PI / 20f;
                Color ring = new Color(.82f, .48f, .16f, Mathf.Clamp01(1f - age / .4f) * .8f);
                Line(impactPosition + Radial(a) * ringRadius, impactPosition + Radial(b) * ringRadius,
                    Mathf.Lerp(.075f, .018f, Mathf.Clamp01(age / .4f)), ring);
            }
            if (contact > 0f)
            {
                for (int ray = 0; ray < 7; ray++)
                {
                    float angle = ray * Mathf.PI * 2f / 7f + .2f;
                    Vector3 direction = Radial(angle);
                    Line(impactPosition + direction * .08f,
                        impactPosition + direction * (.2f + .28f * Hash(ray + 31)), .034f * contact, contactColor);
                }
            }
            Vector3 right = Camera.main != null ? Camera.main.transform.right : Vector3.right;
            Vector3 up = Camera.main != null ? Camera.main.transform.up : Vector3.up;
            int dustCount = elite ? 11 : 8;
            for (int particle = 0; particle < dustCount; particle++)
            {
                float angle = particle * 2.39996f + strikeNumber * .53f;
                float speed = (.6f + Hash(particle + 43) * 1.1f) * weight;
                Vector3 outward = Radial(angle);
                Vector3 position = impactPosition + outward * (age * speed) + Vector3.up * (.1f + age * (.35f + Hash(particle + 64) * .55f));
                float size = (.32f + Hash(particle + 91) * .22f + age * .48f) * weight;
                float dustFade = Mathf.Clamp01(age / .045f) * Mathf.Pow(1f - life, .7f);
                Color dust = new Color(.56f, .36f, .16f, dustFade * .48f);
                Quad(position, right * size, up * size * .7f, dust, true);
            }
            int debrisCount = elite ? 18 : 12;
            for (int fragment = 0; fragment < debrisCount; fragment++)
            {
                float angle = fragment * 2.39996f + strikeNumber * .83f;
                Vector3 outward = Radial(angle);
                float speed = (.9f + Hash(fragment + 3) * 1.6f) * weight;
                float lift = 1.2f + Hash(fragment + 17) * 1.6f;
                Vector3 position = impactPosition + outward * (speed * age) + Vector3.up * Mathf.Max(.005f, lift * age - 5f * age * age);
                float size = (.035f + Hash(fragment + 81) * .035f) * weight * Mathf.Clamp01((1f - life) * 3f);
                float spin = fragment * .9f + age * (fragment % 2 == 0 ? 9f : -7f);
                Vector3 a = right * Mathf.Cos(spin) + up * Mathf.Sin(spin);
                Vector3 b = -right * Mathf.Sin(spin) + up * Mathf.Cos(spin);
                Color stone = Color.Lerp(new Color(.19f, .13f, .075f), new Color(.66f, .45f, .2f), Hash(fragment + 7));
                stone.a = Mathf.Clamp01((1f - life) * 4f);
                Quad(position, a * size, b * size * 1.7f, stone, false);
            }
            // Hairline fissures fade rapidly and never change collision or the arena surface.
            for (int crack = 0; crack < 6; crack++)
            {
                Vector3 outward = Radial(crack * Mathf.PI / 3f + .3f);
                Vector3 bent = Quaternion.AngleAxis(22f, Vector3.up) * outward;
                Color color = new Color(.17f, .12f, .07f, Mathf.Clamp01(1f - life * 1.8f) * .5f);
                Vector3 bend = impactPosition + outward * .23f * weight;
                Line(impactPosition + outward * .07f, bend, .022f, color);
                Line(bend, bend + bent * .22f * weight, .014f, color);
            }
        }

        private static float Hash(int index) => Mathf.Repeat(Mathf.Sin(index * 127.1f + 31.7f) * 43758.5453f, 1f);
        private static Vector3 Radial(float angle) => new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

        private void Line(Vector3 a, Vector3 b, float width, Color color)
        {
            Vector3 across = Vector3.Cross(Vector3.up, b - a).normalized * width * .5f;
            AddQuad(a - across, a + across, b - across, b + across, color, false);
        }

        private void Quad(Vector3 center, Vector3 right, Vector3 up, Color color, bool soft)
        {
            AddQuad(center - right - up, center + right - up, center - right + up, center + right + up, color, soft);
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, bool soft)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) colors.Add(color);
            uvs.Add(soft ? Vector2.zero : Vector2.one * .5f);
            uvs.Add(soft ? Vector2.right : Vector2.one * .5f);
            uvs.Add(soft ? Vector2.up : Vector2.one * .5f);
            uvs.Add(soft ? Vector2.one : Vector2.one * .5f);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        private void EnsureInitialized()
        {
            if (root != null) return;
            root = new GameObject("FX_Troll_Preparacao_Impacto");
            // World-space vertices keep dust at the contact point while the monster moves on.
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            mesh = new Mesh { name = "TrollFX_Reutilizado" };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = root.AddComponent<MeshRenderer>();
            Material source = Resources.Load<Material>("MonsterEarthEffects");
            if (source != null) material = new Material(source) { name = "TrollFX_Terra" };
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            if (root != null) Destroy(root);
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
