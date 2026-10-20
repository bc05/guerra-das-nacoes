using UnityEngine;
using UnityEngine.Rendering;

namespace GuerraDasNacoes
{
    [DisallowMultipleComponent]
    public sealed class PrototypeAreaEffects : MonoBehaviour
    {
        private const int Segments = 72;
        private const int Bands = 5;
        [SerializeField] private Material material;
        private GameObject root;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Light glow;
        private readonly Vector3[] vertices = new Vector3[(Segments + 1) * Bands];
        private readonly Color[] colors = new Color[(Segments + 1) * Bands];
        private Vector3 origin, direction;
        private float startedAt, radius, angle;
        private bool heavy;
        public bool IsPlaying { get; private set; }

        public void Configure(Material effectMaterial) { material = effectMaterial; }

        public void Play(Vector3 position, Vector3 forward, float reach, float degrees, bool circular)
        {
            EnsureInitialized();
            origin = position;
            direction = forward;
            radius = reach;
            angle = degrees;
            heavy = circular;
            startedAt = Time.time;
            IsPlaying = true;
            root.SetActive(true);
            root.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            meshRenderer.sharedMaterial = material;
            UpdateMesh(0f);
        }

        private void Update()
        {
            if (!IsPlaying) return;
            float age = Time.time - startedAt;
            if (age >= 0.42f) { Cancel(); return; }
            UpdateMesh(age);
        }

        private void UpdateMesh(float age)
        {
            float travel = Mathf.Clamp01((age - 0.035f) / (heavy ? 0.14f : 0.10f));
            float fade = Mathf.Clamp01((0.40f - age) / 0.17f);
            float head = travel * angle - angle * 0.5f;
            float waveRadius = radius * Mathf.Lerp(0.32f, 1f, Mathf.SmoothStep(0f, 1f, travel * 1.8f));
            float width = (heavy ? 0.14f : 0.17f) * Mathf.Sin(Mathf.Clamp01(age / 0.42f) * Mathf.PI);
            for (int segment = 0; segment <= Segments; segment++)
            {
                float along = segment / (float)Segments;
                float degrees = Mathf.Lerp(-angle * 0.5f, angle * 0.5f, along);
                float behind = head - degrees;
                float sweep = heavy ? Mathf.Clamp01((age - 0.035f) / 0.055f)
                    : behind >= 0f ? 0.35f + 0.65f * Mathf.Clamp01(1f - behind / 140f) : 0f;
                float endFade = heavy ? 0.30f + 0.70f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(degrees * Mathf.Deg2Rad * 3f - travel * 4f), 1.5f)
                    : Mathf.Pow(Mathf.Max(0f, Mathf.Sin(along * Mathf.PI)), 0.4f);
                float theta = degrees * Mathf.Deg2Rad;
                for (int band = 0; band < Bands; band++)
                {
                    float across = band / (float)(Bands - 1);
                    float edge = Mathf.Sin(across * Mathf.PI);
                    float distance = waveRadius * (heavy ? 1f + Mathf.Sin(theta * 5f) * 0.012f : 1f) + (across - 0.5f) * width;
                    int vertex = segment * Bands + band;
                    vertices[vertex] = new Vector3(Mathf.Sin(theta) * distance,
                        0.88f + (heavy ? Mathf.Sin(theta * 3f + travel * 3f) * 0.10f : Mathf.Sin(theta) * 0.23f), Mathf.Cos(theta) * distance);
                    Color color = Color.Lerp(new Color(0.05f, 0.42f, 1.8f), new Color(1.5f, 2f, 2.2f), edge * edge);
                    color.a = edge * sweep * fade * endFade * (heavy ? 0.90f : 0.80f);
                    colors[vertex] = color;
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            glow.intensity = 1.8f * fade * Mathf.Sin(travel * Mathf.PI);
        }

        private void EnsureInitialized()
        {
            if (root != null) return;
            root = new GameObject("FX_VarreduraDeEnergia");
            mesh = new Mesh { name = "ArcoAreaReutilizado" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors = colors;
            int[] triangles = new int[Segments * (Bands - 1) * 6];
            int index = 0;
            for (int segment = 0; segment < Segments; segment++)
            for (int band = 0; band < Bands - 1; band++)
            {
                int v = segment * Bands + band;
                triangles[index++] = v;
                triangles[index++] = v + Bands;
                triangles[index++] = v + 1;
                triangles[index++] = v + 1;
                triangles[index++] = v + Bands;
                triangles[index++] = v + Bands + 1;
            }
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.up, new Vector3(8f, 3f, 8f));
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            glow = new GameObject("LuzDaLamina").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = Vector3.up * 1.2f;
            glow.type = LightType.Point;
            glow.range = 4.5f;
            glow.color = new Color(0.12f, 0.55f, 1f);
            glow.shadows = LightShadows.None;
        }

        public void Cancel()
        {
            IsPlaying = false;
            if (root != null) root.SetActive(false);
        }

        private void OnDisable() { Cancel(); }
        private void OnDestroy()
        {
            if (root != null) Destroy(root);
            if (mesh != null) Destroy(mesh);
        }
    }
}
