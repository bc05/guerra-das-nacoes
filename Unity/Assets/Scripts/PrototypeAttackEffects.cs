using UnityEngine;
using UnityEngine.Rendering;

namespace GuerraDasNacoes
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class PrototypeAttackEffects : MonoBehaviour
    {
        private const float TrailStart = 0.06f;
        private const float TrailEnd = 0.22f;
        private const float TrailLife = 0.13f;
        private const float SampleInterval = 1f / 120f;
        private const float ImpactTime = 0.13f;
        private const float EffectDuration = 0.4f;
        private const float ImpactDuration = .18f;
        private const int MaximumSamples = 32;
        private const int MaximumSamplesPerFrame = 24;
        private const int RibbonBands = 5;
        private const int SparkCount = 6;
        private static readonly int TintProperty = Shader.PropertyToID("_Color");

        [SerializeField] private Material slashMaterial;
        [SerializeField] private Material impactMaterial;
        [SerializeField] private Transform swordBase;
        [SerializeField] private Transform swordTip;

        private GameObject effectsRoot;
        private Transform trailRoot;
        private Transform cutRoot;
        private Transform sparksRoot;
        private MeshRenderer haloRenderer;
        private MeshRenderer coreRenderer;
        private MeshRenderer cutRenderer;
        private MeshRenderer sparksRenderer;
        private Mesh haloMesh;
        private Mesh coreMesh;
        private Mesh cutMesh;
        private Mesh sparksMesh;
        private Vector3[] haloVertices;
        private Vector3[] coreVertices;
        private Color[] haloColors;
        private Color[] coreColors;
        private Vector3[] sparkVertices;
        private Vector3[] sparkDirections;
        private BladeSample[] samples;
        private MaterialPropertyBlock tint;
        private Vector3 trailOrigin;
        private Vector3 impactOrigin;
        private Quaternion impactRotation;
        private Vector3 previousBase;
        private Vector3 previousTip;
        private float previousTime;
        private float nextSampleTime;
        private float startedAt;
        private float elapsed;
        private int sampleCount;
        private bool endSampled;
        private bool hasImpact;
        private bool swingActive;
        private float impactStartedAt;

        private struct BladeSample
        {
            public Vector3 Base;
            public Vector3 Tip;
            public float Time;
        }

        public bool IsPlaying { get; private set; }
        public bool IsConfigured => slashMaterial != null && impactMaterial != null;
        public bool HasBladeAnchors => ResolveBladeAnchors();
        public int TrailSampleCount { get; private set; }
        public float Elapsed => elapsed;
        public bool IsImpactVisible => IsPlaying && ((cutRenderer != null && cutRenderer.enabled) ||
            (sparksRenderer != null && sparksRenderer.enabled));

        public void Configure(Material slash, Material impact)
        {
            slashMaterial = slash;
            impactMaterial = impact;
            if (effectsRoot != null)
                ApplyMaterials();
        }

        private void Awake()
        {
            EnsureInitialized();
            ResolveBladeAnchors();
            Cancel();
        }

        public void Play(Vector3 impactPoint)
        {
            if (!Application.isPlaying)
                return;
            EnsureInitialized();
            if (!IsConfigured)
                return;

            ResolveBladeAnchors();
            trailOrigin = transform.position;
            previousBase = swordBase != null ? swordBase.position : transform.position;
            previousTip = swordTip != null ? swordTip.position : previousBase;
            previousTime = 0f;
            nextSampleTime = TrailStart;
            sampleCount = 0;
            TrailSampleCount = 0;
            endSampled = false;
            elapsed = 0f;
            startedAt = Time.time;
            swingActive = true;
            IsPlaying = true;
            effectsRoot.SetActive(true);
            haloRenderer.enabled = false;
            coreRenderer.enabled = false;
            cutRenderer.enabled = false;
            sparksRenderer.enabled = false;
            SetImpact(impactPoint, Time.time + ImpactTime);
        }

        public void PlaySwing()
        {
            Play(transform.position + transform.forward * 2f + Vector3.up);
            hasImpact = false;
        }

        public void ShowImpact(Vector3 impactPoint)
        {
            if (!Application.isPlaying) return;
            EnsureInitialized();
            if (!IsConfigured) return;
            SetImpact(impactPoint, Time.time);
        }

        private void SetImpact(Vector3 impactPoint, float impactTime)
        {
            hasImpact = true;
            impactStartedAt = impactTime;
            Vector3 towardsHero = Vector3.ProjectOnPlane(transform.position - impactPoint, Vector3.up).normalized;
            if (towardsHero.sqrMagnitude < 0.01f)
                towardsHero = -transform.forward;
            // The cut remains on the front of the torso, instead of crossing the enemy's centre.
            impactOrigin = impactPoint + towardsHero * 0.48f;
            impactOrigin.y = Mathf.Max(impactOrigin.y, transform.position.y + 0.95f);
            impactRotation = Quaternion.LookRotation(towardsHero, Vector3.up);
            // A delayed authoritative hit can outlive the predicted swing. Reactivate only the impact.
            if (!IsPlaying)
            {
                swingActive = false;
                elapsed = EffectDuration;
                sampleCount = 0;
                TrailSampleCount = 0;
                haloRenderer.enabled = coreRenderer.enabled = false;
                IsPlaying = true;
                effectsRoot.SetActive(true);
            }
            UpdateImpact(Time.time - impactStartedAt);
        }

        public void Cancel()
        {
            IsPlaying = false;
            elapsed = 0f;
            sampleCount = 0;
            TrailSampleCount = 0;
            hasImpact = false;
            swingActive = false;
            if (effectsRoot == null)
                return;
            effectsRoot.SetActive(false);
            ResetTransform(trailRoot);
            ResetTransform(cutRoot);
            ResetTransform(sparksRoot);
        }

        private void LateUpdate()
        {
            if (!IsPlaying)
                return;
            if (swingActive)
            {
                elapsed = Time.time - startedAt;
                if (elapsed >= EffectDuration)
                {
                    swingActive = false;
                    haloRenderer.enabled = coreRenderer.enabled = false;
                    sampleCount = 0;
                    TrailSampleCount = 0;
                }
                else
                {
                    CaptureBlade(elapsed);
                    UpdateTrail(elapsed);
                }
            }
            float impactAge = Time.time - impactStartedAt;
            if (hasImpact && impactAge >= ImpactDuration) hasImpact = false;
            UpdateImpact(impactAge);
            if (!swingActive && !hasImpact) Cancel();
        }

        private void CaptureBlade(float age)
        {
            if (swordBase == null || swordTip == null)
                return;
            Vector3 currentBase = swordBase.position;
            Vector3 currentTip = swordTip.position;
            float limit = Mathf.Min(age, TrailEnd);
            // Fill short frame gaps with a bounded interpolation pass, independent of render FPS.
            for (int index = 0; index < MaximumSamplesPerFrame; index++)
            {
                if (nextSampleTime > limit || nextSampleTime > TrailEnd)
                    break;
                float fraction = Mathf.InverseLerp(previousTime, age, nextSampleTime);
                AddSample(Vector3.Lerp(previousBase, currentBase, fraction),
                    Vector3.Lerp(previousTip, currentTip, fraction), nextSampleTime);
                nextSampleTime += SampleInterval;
            }
            if (!endSampled && age >= TrailEnd)
            {
                float fraction = Mathf.InverseLerp(previousTime, age, TrailEnd);
                AddSample(Vector3.Lerp(previousBase, currentBase, fraction),
                    Vector3.Lerp(previousTip, currentTip, fraction), TrailEnd);
                endSampled = true;
            }
            previousBase = currentBase;
            previousTip = currentTip;
            previousTime = age;
        }

        private void AddSample(Vector3 bladeBase, Vector3 bladeTip, float time)
        {
            if (sampleCount >= MaximumSamples)
                return;
            if (sampleCount > 0 && time <= samples[sampleCount - 1].Time + 0.0001f)
                return;
            samples[sampleCount++] = new BladeSample { Base = bladeBase, Tip = bladeTip, Time = time };
        }

        private void UpdateTrail(float age)
        {
            int first = 0;
            for (; first < sampleCount; first++)
                if (age - samples[first].Time < TrailLife)
                    break;
            int visible = sampleCount - first;
            TrailSampleCount = visible;
            haloRenderer.enabled = visible >= 2;
            coreRenderer.enabled = haloRenderer.enabled;
            if (!haloRenderer.enabled)
                return;

            trailRoot.SetPositionAndRotation(trailOrigin, Quaternion.identity);
            trailRoot.localScale = Vector3.one;
            Bounds bounds = new Bounds(samples[first].Base - trailOrigin, Vector3.zero);
            Vector3 lastVertex = Vector3.zero;
            for (int sample = 0; sample < MaximumSamples; sample++)
            {
                bool active = sample < visible;
                BladeSample blade = active ? samples[first + sample] : default;
                float life = active ? Mathf.Clamp01(1f - (age - blade.Time) / TrailLife) : 0f;
                float endFade = visible < 4 ? 0.65f :
                    Mathf.SmoothStep(0f, 1f, sample / 1.5f) *
                    Mathf.SmoothStep(0f, 1f, (visible - sample - 1) / 1.5f);
                for (int band = 0; band < RibbonBands; band++)
                {
                    float across = band / (float)(RibbonBands - 1);
                    float edge = Mathf.Max(0f, Mathf.Sin(across * Mathf.PI));
                    int vertex = sample * RibbonBands + band;
                    Vector3 halo = active ? Vector3.LerpUnclamped(blade.Base, blade.Tip, Mathf.Lerp(0.08f, 1.045f, across))
                        - trailOrigin : lastVertex;
                    Vector3 core = active ? Vector3.LerpUnclamped(blade.Base, blade.Tip, Mathf.Lerp(0.8f, 1.015f, across))
                        - trailOrigin : lastVertex;
                    haloVertices[vertex] = halo;
                    coreVertices[vertex] = core;
                    haloColors[vertex] = new Color(0.04f, 0.34f, 1f, edge * life * life * endFade * 0.22f);
                    Color coreColor = Color.Lerp(new Color(0.12f, 0.8f, 1f), new Color(0.9f, 1f, 1f), edge * edge);
                    coreColor.a = edge * life * endFade * 0.82f;
                    coreColors[vertex] = coreColor;
                    if (active)
                    {
                        bounds.Encapsulate(halo);
                        bounds.Encapsulate(core);
                        lastVertex = halo;
                    }
                }
            }
            bounds.Expand(0.05f);
            haloMesh.vertices = haloVertices;
            haloMesh.colors = haloColors;
            haloMesh.bounds = bounds;
            coreMesh.vertices = coreVertices;
            coreMesh.colors = coreColors;
            coreMesh.bounds = bounds;
        }

        private void UpdateImpact(float impactAge)
        {
            if (!hasImpact)
            {
                cutRenderer.enabled = false;
                sparksRenderer.enabled = false;
                return;
            }
            cutRenderer.enabled = impactAge >= 0f && impactAge < 0.1f;
            sparksRenderer.enabled = impactAge >= 0f && impactAge < 0.18f;
            if (impactAge < 0f)
                return;

            cutRoot.SetPositionAndRotation(impactOrigin, impactRotation);
            float cutProgress = Mathf.Clamp01(impactAge / 0.1f);
            cutRoot.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(impactAge / 0.025f));
            SetFade(cutRenderer, Mathf.Pow(1f - cutProgress, 1.4f) * 0.85f);
            if (!sparksRenderer.enabled)
                return;

            sparksRoot.SetPositionAndRotation(impactOrigin, impactRotation);
            sparksRoot.localScale = Vector3.one;
            for (int spark = 0; spark < SparkCount; spark++)
            {
                float progress = Mathf.Clamp01(impactAge / (0.13f + spark % 3 * 0.025f));
                Vector3 direction = sparkDirections[spark];
                Vector3 center = direction * Mathf.Lerp(0.035f, 0.27f, EaseOut(progress));
                Vector3 side = new Vector3(-direction.y, direction.x, 0f).normalized;
                float length = Mathf.Lerp(0.055f, 0f, progress);
                int vertex = spark * 5;
                sparkVertices[vertex] = center + direction * length;
                sparkVertices[vertex + 1] = center + side * length * 0.23f;
                sparkVertices[vertex + 2] = center - direction * length * 0.6f;
                sparkVertices[vertex + 3] = center - side * length * 0.23f;
                sparkVertices[vertex + 4] = center;
            }
            sparksMesh.vertices = sparkVertices;
            SetFade(sparksRenderer, 1f - Mathf.Clamp01(impactAge / 0.18f));
        }

        private bool ResolveBladeAnchors()
        {
            if (swordBase == null)
                swordBase = FindChild(transform, "SwordBase");
            if (swordTip == null)
                swordTip = FindChild(transform, "SwordTip");
            return swordBase != null && swordTip != null;
        }

        private static Transform FindChild(Transform root, string objectName)
        {
            if (root.name == objectName)
                return root;
            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindChild(root.GetChild(index), objectName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void EnsureInitialized()
        {
            if (effectsRoot != null)
                return;
            tint = new MaterialPropertyBlock();
            samples = new BladeSample[MaximumSamples];
            // No parent: historical samples cannot inherit later movement, rotation or scale from the hero.
            effectsRoot = new GameObject("FX_LaminaEnergia");
            effectsRoot.SetActive(false);
            trailRoot = CreateRoot("RastroRealEspada");
            cutRoot = CreateRoot("CorteFrontal");
            sparksRoot = CreateRoot("FaiscasCurtas");
            haloVertices = new Vector3[MaximumSamples * RibbonBands];
            coreVertices = new Vector3[haloVertices.Length];
            haloColors = new Color[haloVertices.Length];
            coreColors = new Color[haloVertices.Length];
            int[] trailTriangles = StripTriangles(MaximumSamples, RibbonBands);
            haloMesh = CreateMesh("HaloLamina", haloVertices, haloColors, trailTriangles, true);
            coreMesh = CreateMesh("NucleoLamina", coreVertices, coreColors, trailTriangles, true);
            cutMesh = CreateCut();
            sparksMesh = CreateSparks();
            haloRenderer = CreateRenderer("HaloAzul", trailRoot, haloMesh, 12);
            coreRenderer = CreateRenderer("NucleoBrancoCiano", trailRoot, coreMesh, 13);
            cutRenderer = CreateRenderer("CorteDiagonal", cutRoot, cutMesh, 14);
            sparksRenderer = CreateRenderer("Faiscas", sparksRoot, sparksMesh, 15);
            ApplyMaterials();
        }

        private Transform CreateRoot(string objectName)
        {
            Transform root = new GameObject(objectName).transform;
            root.SetParent(effectsRoot.transform, false);
            return root;
        }

        private void ApplyMaterials()
        {
            haloRenderer.sharedMaterial = slashMaterial;
            coreRenderer.sharedMaterial = slashMaterial;
            cutRenderer.sharedMaterial = impactMaterial;
            sparksRenderer.sharedMaterial = impactMaterial;
        }

        private static MeshRenderer CreateRenderer(string objectName, Transform parent, Mesh mesh, int order)
        {
            GameObject meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(parent, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static Mesh CreateCut()
        {
            const int points = 7;
            Vector3[] vertices = new Vector3[points * RibbonBands];
            Color[] colors = new Color[vertices.Length];
            Vector3 direction = new Vector3(1f, 1f, 0f).normalized;
            Vector3 side = new Vector3(-direction.y, direction.x, 0f);
            for (int point = 0; point < points; point++)
            {
                float along = point / (float)(points - 1);
                float taper = Mathf.Max(0f, Mathf.Sin(along * Mathf.PI));
                for (int band = 0; band < RibbonBands; band++)
                {
                    float across = band / (float)(RibbonBands - 1);
                    float edge = Mathf.Max(0f, Mathf.Sin(across * Mathf.PI));
                    int vertex = point * RibbonBands + band;
                    vertices[vertex] = direction * Mathf.Lerp(-0.43f, 0.43f, along) +
                        side * ((across - 0.5f) * 0.055f * taper);
                    Color color = Color.Lerp(new Color(0.12f, 0.65f, 1f), new Color(0.85f, 1f, 1f), edge * edge);
                    color.a = edge * taper;
                    colors[vertex] = color;
                }
            }
            return CreateMesh("CorteDiagonalCurto", vertices, colors, StripTriangles(points, RibbonBands), false);
        }

        private Mesh CreateSparks()
        {
            sparkVertices = new Vector3[SparkCount * 5];
            sparkDirections = new Vector3[SparkCount];
            Color[] colors = new Color[sparkVertices.Length];
            int[] triangles = new int[SparkCount * 12];
            for (int spark = 0; spark < SparkCount; spark++)
            {
                float angle = (spark + 0.2f) / SparkCount * Mathf.PI * 2f;
                // Positive local Z points towards the attacker, keeping every spark in front of the torso.
                sparkDirections[spark] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0.35f).normalized;
                Color color = spark == 1 ? new Color(1f, 0.85f, 0.55f, 0.8f) : new Color(0.65f, 0.95f, 1f, 0.95f);
                for (int corner = 0; corner < 5; corner++)
                {
                    colors[spark * 5 + corner] = new Color(color.r, color.g, color.b, corner == 4 ? color.a : 0f);
                    if (corner == 4)
                        continue;
                    int triangle = spark * 12 + corner * 3;
                    triangles[triangle] = spark * 5 + 4;
                    triangles[triangle + 1] = spark * 5 + corner;
                    triangles[triangle + 2] = spark * 5 + (corner + 1) % 4;
                }
            }
            Mesh mesh = CreateMesh("FaiscasLamina", sparkVertices, colors, triangles, true);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            return mesh;
        }

        private static int[] StripTriangles(int points, int bands)
        {
            int[] triangles = new int[(points - 1) * (bands - 1) * 6];
            int index = 0;
            for (int point = 0; point < points - 1; point++)
                for (int band = 0; band < bands - 1; band++)
                {
                    int first = point * bands + band;
                    triangles[index++] = first;
                    triangles[index++] = first + bands;
                    triangles[index++] = first + 1;
                    triangles[index++] = first + 1;
                    triangles[index++] = first + bands;
                    triangles[index++] = first + bands + 1;
                }
            return triangles;
        }

        private static Mesh CreateMesh(string meshName, Vector3[] vertices, Color[] colors, int[] triangles, bool dynamic)
        {
            Mesh mesh = new Mesh { name = meshName };
            if (dynamic)
                mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetFade(MeshRenderer renderer, float alpha)
        {
            tint.SetColor(TintProperty, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            renderer.SetPropertyBlock(tint);
        }

        private static float EaseOut(float progress)
        {
            return 1f - Mathf.Pow(1f - Mathf.Clamp01(progress), 3f);
        }

        private static void ResetTransform(Transform target)
        {
            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void OnDestroy()
        {
            if (effectsRoot != null)
                Destroy(effectsRoot);
            if (haloMesh != null)
                Destroy(haloMesh);
            if (coreMesh != null)
                Destroy(coreMesh);
            if (cutMesh != null)
                Destroy(cutMesh);
            if (sparksMesh != null)
                Destroy(sparksMesh);
        }
    }
}
