using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuerraDasNacoes
{
    /// <summary>Shared, presentation-only palette for the frontier and its authored monster variants.
    /// The server exclusively owns spawn windows, hit points, damage and rewards.</summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeWorldExpansion : MonoBehaviour
    {
        public const string RootName = "Fronteira expandida";
        [SerializeField] private Material iron, leather, bone, banner, ember;
        [SerializeField] private Mesh plateMesh, bladeMesh, spikeMesh, bannerMesh;
        private static PrototypeWorldExpansion active;
        private readonly Dictionary<string, Material> skins = new Dictionary<string, Material>();

        public void ConfigurePalette(Material metal, Material hide, Material ivory, Material flag, Material fire,
            Mesh plate, Mesh blade, Mesh spike, Mesh cloth)
        {
            iron = metal; leather = hide; bone = ivory; banner = flag; ember = fire;
            plateMesh = plate; bladeMesh = blade; spikeMesh = spike; bannerMesh = cloth;
        }

        private void Awake() { active = this; }
        private void OnDestroy()
        {
            if (active == this) active = null;
            foreach (Material material in skins.Values)
                if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        }

        public static void ConfigureMonsterVisual(PrototypeMonster monster, OnlineMonsterState state)
        {
            if (monster == null || state == null) return;
            string requestedKind = state.visualKind ?? "troll";
            float requestedSize = state.visualScale > 0f ? Mathf.Clamp(state.visualScale, .5f, 3f) : 1f;
            PrototypeUndeadVisual undead = monster.GetComponent<PrototypeUndeadVisual>();
            if (requestedKind == "skeleton" || requestedKind == "skeleton-caster")
            {
                if (undead != null && undead.Matches(requestedKind, requestedSize)) return;
                MonsterVariantView previous = monster.GetComponent<MonsterVariantView>();
                if (previous != null)
                {
                    previous.Clear(); previous.RestoreOriginalMaterials(); previous.Kind = null;
                    if (previous.HasBaseScale)
                    {
                        monster.transform.localScale = previous.BaseScale;
                        monster.GetComponent<CharacterController>().radius = previous.BaseRadius;
                    }
                }
                if (undead == null) undead = monster.gameObject.AddComponent<PrototypeUndeadVisual>();
                undead.Apply(monster, requestedKind, requestedSize);
                return;
            }
            if (undead != null) undead.RestoreOriginal();
            PrototypeWorldExpansion palette = active;
            if (palette == null) palette = FindFirstObjectByType<PrototypeWorldExpansion>();
            if (palette == null || palette.plateMesh == null || palette.iron == null) return;
            MonsterVariantView variant = monster.GetComponent<MonsterVariantView>();
            if (variant == null) variant = monster.gameObject.AddComponent<MonsterVariantView>();
            // The same instance survives death/respawn and a schedule reconfiguration. Rebuild
            // only when the visual profile changes, never on the ten snapshots each second.
            string kind = state.isBoss ? "boss-troll" : state.visualKind ?? "troll";
            float requestedScale = state.visualScale > 0f ? Mathf.Clamp(state.visualScale, .5f, 3f) : 1f;
            // Appearance follows the species, so renaming/moving a spawn never changes a shaman's staff.
            string species = string.IsNullOrEmpty(state.speciesId) ? state.id : state.speciesId;
            bool shaman = kind == "orc" && (state.behavior == "ranged" ||
                (species != null && species.Contains("shaman")));
            if (variant.Kind == kind && variant.IsShaman == shaman && Mathf.Approximately(variant.Scale, requestedScale)) return;
            palette.ApplyVariant(monster, variant, kind, requestedScale, shaman);
        }

        private void ApplyVariant(PrototypeMonster monster, MonsterVariantView view, string kind, float scale, bool shaman)
        {
            view.Clear();
            view.CaptureOriginalMaterials();
            view.RestoreOriginalMaterials();
            CharacterController collider = monster.GetComponent<CharacterController>();
            if (!view.HasBaseScale) { view.BaseScale = monster.transform.localScale; view.BaseRadius = collider.radius; view.HasBaseScale = true; }
            monster.transform.localScale = view.BaseScale * scale;
            // A very large silhouette must remain reachable by a melee character. The server
            // uses the same 1.65 m maximum body radius for approach/line-of-sight checks.
            collider.radius = Mathf.Min(view.BaseRadius, 1.65f / Mathf.Max(.01f, Mathf.Abs(monster.transform.lossyScale.x)));
            view.Kind = kind; view.Scale = scale; view.IsShaman = shaman;
            if (kind != "orc" && kind != "boss-troll") { monster.RefreshVisualMaterials(); return; }
            bool boss = kind == "boss-troll";
            Transform[] bones = monster.GetComponentsInChildren<Transform>(true);
            Transform chest = FindBone(bones, "Bone.002"), head = FindBone(bones, "Bone.003");
            Transform leftArm = FindBone(bones, "Braco.L"), rightArm = FindBone(bones, "Braco.R");
            Transform rightHand = FindBone(bones, "hand.R");
            Transform waist = FindBone(bones, "Bone.001");
            float height = monster.GetComponent<CharacterController>().height;
            // Offsets are in the original monster's local metres and are then attached to the
            // actual articulated bones, so equipment follows the strike and gait animations.
            Add(view, monster.transform, chest, "Peitoral de ferro batido", plateMesh, iron,
                new Vector3(0f, height * .65f, .63f), new Vector3(boss ? 1.32f : 1.24f, .74f, .20f), Vector3.zero);
            Add(view, monster.transform, waist, "Cinturão de couro", plateMesh, leather,
                new Vector3(0f, height * .48f, .76f), new Vector3(1.15f, .17f, .12f), Vector3.zero);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform arm = side < 0 ? leftArm : rightArm;
                Vector3 shoulderPoint = arm != null ? monster.transform.InverseTransformPoint(arm.position) : new Vector3(side*.66f,height*.76f,-.18f);
                float armSide = Mathf.Sign(shoulderPoint.x);
                Add(view, monster.transform, arm, "Ombreira rebitada", plateMesh, iron,
                    shoulderPoint + new Vector3(0f,.04f,.12f), new Vector3(.58f, .22f, .65f), new Vector3(0f, 0f, -armSide * 13f));
                for (int rivet = 0; rivet < 3; rivet++)
                    Add(view, monster.transform, arm, "Rebite e presa", spikeMesh, bone,
                        shoulderPoint + new Vector3((rivet-1)*.16f,.23f,.09f),
                        new Vector3(.10f, boss ? .36f : .20f, .10f), new Vector3(0f, 0f, -armSide * 24f));
                Add(view, monster.transform, waist, "Placa lateral do cinturão", plateMesh, iron,
                    new Vector3(side * .46f, height * .44f, .75f), new Vector3(.28f, .36f, .12f), new Vector3(0f, side * 20f, side * 5f));
            }
            if (rightHand != null)
            {
                Vector3 handPoint = monster.transform.InverseTransformPoint(rightHand.position);
                Vector3 grip = handPoint + new Vector3(-.07f,.03f,.17f);
                Add(view, monster.transform, rightHand, shaman ? "Bastão do xamã" : "Cabo do machado", plateMesh, leather,
                    grip + new Vector3(0f, shaman ? .45f : .16f, 0f), new Vector3(.13f, shaman ? 2.4f : 1.6f, .13f), Vector3.zero);
                Add(view, monster.transform, rightHand, shaman ? "Presa do bastão" : "Lâmina do machado", shaman ? spikeMesh : bladeMesh,
                    shaman ? bone : iron, grip + new Vector3(0f, shaman ? 1.7f : .93f, 0f),
                    shaman ? new Vector3(.30f, .48f, .30f) : new Vector3(.94f, .62f, .19f), Vector3.zero);
            }
            if (boss)
            {
                Add(view, monster.transform, head, "Coroa de guerra", plateMesh, iron,
                    new Vector3(0f, height * .998f, -.10f), new Vector3(.75f, .15f, .60f), Vector3.zero);
                for (int i = -2; i <= 2; i++)
                    Add(view, monster.transform, head, "Chifre da coroa", spikeMesh, bone,
                        new Vector3(i * .16f, height * 1.032f, -.10f), new Vector3(.13f, .3f + (.12f * (2 - Mathf.Abs(i))), .13f),
                        new Vector3(0f, 0f, -i * 12f));
                Add(view, monster.transform, chest, "Mastro do estandarte", plateMesh, iron,
                    new Vector3(-.25f, height * .95f, -.36f), new Vector3(.07f, 2.1f, .07f), Vector3.zero);
                Add(view, monster.transform, chest, "Estandarte rasgado", bannerMesh, banner,
                    new Vector3(.20f, height * 1.13f, -.36f), new Vector3(1.05f, .95f, 1f), Vector3.zero);
                GameObject aura = new GameObject("Brasa discreta do chefe");
                aura.transform.SetParent(monster.transform, false); aura.transform.localPosition = Vector3.up * .12f;
                view.Owned.Add(aura);
                ParticleSystem particles = aura.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main; main.duration = 3f; main.loop = true; main.startLifetime = 1.3f;
                main.startSpeed = .28f; main.startSize = .065f; main.maxParticles = 28;
                main.startColor = new Color(.94f, .30f, .08f, .28f); main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var emission = particles.emission; emission.rateOverTime = 12f;
                var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 1.05f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = ember;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                if (Application.isPlaying) particles.Play();
            }
            Material skin = GetSkin(boss ? "boss" : shaman ? "shaman" : "orc");
            if (skin != null)
                foreach (SkinnedMeshRenderer renderer in monster.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null && materials[i].name.Contains("TrollSkin")) materials[i] = skin;
                    renderer.sharedMaterials = materials;
                }
            monster.RefreshVisualMaterials();
        }

        private Material GetSkin(string kind)
        {
            if (skins.TryGetValue(kind, out Material material)) return material;
            Material source = null;
            foreach (PrototypeMonster monster in FindObjectsByType<PrototypeMonster>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (SkinnedMeshRenderer renderer in monster.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    foreach (Material candidate in renderer.sharedMaterials)
                        if (candidate != null && candidate.name.StartsWith("MAT_TrollSkin")) { source = candidate; break; }
            if (source == null) return null;
            material = new Material(source) { name = "Variante de pele · " + kind, enableInstancing = true };
            material.color = kind == "boss" ? new Color(.63f, .58f, .51f) :
                kind == "shaman" ? new Color(.72f, .76f, .56f) : new Color(.67f, .74f, .55f);
            skins.Add(kind, material); return material;
        }

        private static Transform FindBone(Transform[] bones, string name)
        {
            foreach (Transform candidate in bones) if (candidate.name == name) return candidate;
            return null;
        }

        private static void Add(MonsterVariantView view, Transform root, Transform anchor, string name, Mesh mesh,
            Material material, Vector3 position, Vector3 size, Vector3 rotation)
        {
            if (mesh == null || material == null || anchor == null) return;
            GameObject piece = new GameObject(name); piece.transform.SetParent(root, false);
            piece.transform.localPosition = position; piece.transform.localRotation = Quaternion.Euler(rotation);
            piece.transform.localScale = size; piece.transform.SetParent(anchor, true);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = piece.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            piece.layer = 9; view.Owned.Add(piece);
        }
    }

    // Separate marker records the pristine dimensions; no gameplay component or collider is replaced.
    public sealed class MonsterVariantView : MonoBehaviour
    {
        public string Kind { get; set; }
        public bool IsShaman { get; set; }
        public float Scale { get; set; }
        public Vector3 BaseScale { get; set; }
        public float BaseRadius { get; set; }
        public bool HasBaseScale { get; set; }
        public readonly List<GameObject> Owned = new List<GameObject>();
        private readonly Dictionary<SkinnedMeshRenderer, Material[]> originalMaterials = new Dictionary<SkinnedMeshRenderer, Material[]>();
        public void CaptureOriginalMaterials()
        {
            if (originalMaterials.Count != 0) return;
            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                originalMaterials[renderer] = renderer.sharedMaterials;
        }
        public void RestoreOriginalMaterials()
        {
            foreach (var entry in originalMaterials) if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
        }
        public void Clear()
        {
            foreach (GameObject piece in Owned) if (piece != null) { piece.SetActive(false); Destroy(piece); }
            Owned.Clear();
        }
    }
}
