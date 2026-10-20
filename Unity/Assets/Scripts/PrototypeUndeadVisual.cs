using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuerraDasNacoes
{
    // Original procedural artwork. Visual changes never replace the monster, its identity or its health.
    [DisallowMultipleComponent]
    public sealed class PrototypeUndeadVisual : MonoBehaviour
    {
        private const float BodyHeight = 2.12f;
        private PrototypeMonster monster;
        private Transform originalRoot, skeletonRoot, casterRoot;
        private Vector3 originalScale, originalCenter;
        private float originalHeight, originalRadius, originalStep;
        private bool acquired;
        public string Kind { get; private set; }
        public float Scale { get; private set; }
        public bool IsApplied { get; private set; }
        public Transform OriginalRoot => originalRoot;
        public Transform ActiveRoot => Kind == "skeleton-caster" ? casterRoot : skeletonRoot;
        public static int SharedResourceOwners => Art.Owners;

        public bool Matches(string kind, float scale) => IsApplied && Kind == kind &&
            Mathf.Approximately(Scale, scale) && monster != null && monster.VisualRoot == ActiveRoot;

        public void Apply(PrototypeMonster character, string kind, float scale)
        {
            if (character == null || (kind != "skeleton" && kind != "skeleton-caster")) return;
            scale = Mathf.Clamp(scale, .5f, 3f);
            if (Matches(kind, scale)) return;
            if (monster == null)
            {
                monster = character;
                originalRoot = monster.VisualRoot;
                originalScale = transform.localScale;
                CharacterController body = GetComponent<CharacterController>();
                originalHeight = body.height; originalRadius = body.radius;
                originalCenter = body.center; originalStep = body.stepOffset;
                Art.Acquire(); acquired = true;
            }
            bool caster = kind == "skeleton-caster";
            Transform root = caster ? casterRoot : skeletonRoot;
            if (root == null)
            {
                root = Build(caster);
                if (caster) casterRoot = root; else skeletonRoot = root;
            }
            Kind = kind; Scale = scale; IsApplied = true;
            transform.localScale = originalScale * scale;
            // Keep the existing CharacterController and collision layer used by selection/combat.
            CharacterController controller = GetComponent<CharacterController>();
            controller.height = BodyHeight;
            controller.center = Vector3.up * (BodyHeight * .5f);
            controller.radius = .32f;
            controller.stepOffset = Mathf.Min(originalStep, BodyHeight * .25f);
            monster.ReplaceVisualRoot(root);
            if (originalRoot != null) originalRoot.gameObject.SetActive(false);
            if (skeletonRoot != null && skeletonRoot != root) skeletonRoot.gameObject.SetActive(false);
            if (casterRoot != null && casterRoot != root) casterRoot.gameObject.SetActive(false);
        }

        public void RestoreOriginal()
        {
            if (!IsApplied || monster == null) return;
            IsApplied = false;
            transform.localScale = originalScale;
            CharacterController body = GetComponent<CharacterController>();
            if (body != null)
            {
                body.height = originalHeight; body.center = originalCenter;
                body.radius = originalRadius; body.stepOffset = originalStep;
            }
            monster.ReplaceVisualRoot(originalRoot);
            if (skeletonRoot != null) skeletonRoot.gameObject.SetActive(false);
            if (casterRoot != null) casterRoot.gameObject.SetActive(false);
        }

        private Transform Build(bool caster)
        {
            Transform root = Node(transform, caster ? "Conjurador esquelético autoral" : "Esqueleto guerreiro autoral", Vector3.zero);
            root.gameObject.SetActive(false);
            Transform pelvis = Node(root, "Bone.001", new Vector3(0f, 1.03f, 0f));
            Transform chest = Node(pelvis, "Bone.002", new Vector3(0f, .36f, 0f));
            Transform head = Node(chest, "Bone.003", new Vector3(0f, .42f, 0f));
            for (int vertebra = 0; vertebra < 7; vertebra++)
            {
                Vector3 at = new Vector3(0f, vertebra * .06f, -.065f);
                Part(pelvis, "Vértebra " + vertebra, Art.Joint, Art.Bone, at, new Vector3(.08f, .043f, .07f));
                Part(pelvis, "Apófise vertebral " + vertebra, Art.Box, Art.Bone, at + Vector3.back * .045f, new Vector3(.025f, .025f, .06f));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Part(pelvis, side < 0 ? "Ílio esquerdo" : "Ílio direito", Art.Joint, Art.Bone,
                    new Vector3(side * .105f, -.015f, 0f), new Vector3(.16f, .18f, .09f));
                Bone(pelvis, "Arco púbico", new Vector3(side * .12f, -.04f, .035f), new Vector3(0f, -.15f, .045f), .035f);
                for (int rib = 0; rib < 7; rib++)
                {
                    float width = 1f - Mathf.Abs(rib - 2.5f) * .09f;
                    Part(chest, "Costela " + (side < 0 ? "L" : "R") + "." + rib,
                        side < 0 ? Art.LeftRib : Art.RightRib, Art.Bone,
                        new Vector3(0f, .20f - rib * .058f, 0f), new Vector3(width, 1f, width));
                }
                Bone(chest, "Clavícula", new Vector3(0f, .22f, .015f), new Vector3(side * .255f, .2f, 0f), .023f);
                BuildArm(chest, side, caster);
                BuildLeg(pelvis, side);
            }
            Bone(chest, "Esterno", new Vector3(0f, .19f, .15f), new Vector3(0f, -.15f, .15f), .027f);
            Bone(chest, "Pescoço ósseo", new Vector3(0f, .25f, 0f), new Vector3(0f, .43f, 0f), .035f);
            BuildSkull(head, caster);
            if (caster) BuildVestments(chest, head);
            MergeRigidParts(root, caster ? "caster" : "warrior");
            return root;
        }

        private static void BuildSkull(Transform head, bool caster)
        {
            Part(head, "Crânio", Art.Joint, Art.Bone, new Vector3(0f, .115f, 0f), new Vector3(.29f, .35f, .26f));
            Part(head, "Cavidade da boca", Art.Joint, Art.Shadow, new Vector3(0f, -.018f, .108f), new Vector3(.16f, .055f, .04f));
            Part(head, "Cavidade nasal", Art.Nose, Art.Shadow, new Vector3(0f, .04f, .139f), new Vector3(.042f, .065f, .018f));
            for (int side = -1; side <= 1; side += 2)
            {
                Part(head, "Órbita " + side, Art.Joint, Art.Shadow, new Vector3(side * .065f, .12f, .116f), new Vector3(.095f, .105f, .055f));
                Part(head, "Luz espectral dos olhos " + side, Art.Joint, caster ? Art.Arcane : Art.Eye,
                    new Vector3(side * .065f, .115f, .146f), new Vector3(.025f, .034f, .018f));
                Bone(head, "Arco supraorbital", new Vector3(side * .018f, .174f, .134f), new Vector3(side * .112f, .17f, .105f), .021f);
                Bone(head, "Osso zigomático", new Vector3(side * .115f, .08f, .105f), new Vector3(side * .085f, .015f, .125f), .025f);
            }
            Transform jaw = Node(head, "Mandíbula", new Vector3(0f, -.035f, .07f));
            Bone(jaw, "Ramo mandibular L", new Vector3(-.105f, .08f, -.025f), new Vector3(-.09f, -.065f, .055f), .028f);
            Bone(jaw, "Ramo mandibular R", new Vector3(.105f, .08f, -.025f), new Vector3(.09f, -.065f, .055f), .028f);
            Bone(jaw, "Corpo da mandíbula", new Vector3(-.09f, -.065f, .055f), new Vector3(.09f, -.065f, .055f), .032f);
            for (int tooth = 0; tooth < 6; tooth++)
            {
                float x = (tooth - 2.5f) * .022f;
                Part(head, "Dente superior " + tooth, Art.Box, Art.Bone, new Vector3(x, -.006f, .135f), new Vector3(.018f, .026f, .023f));
                Part(jaw, "Dente inferior " + tooth, Art.Box, Art.Bone, new Vector3(x, -.041f, .057f), new Vector3(.018f, .023f, .024f));
            }
        }

        private static void BuildArm(Transform chest, int side, bool caster)
        {
            string suffix = side < 0 ? ".L" : ".R";
            Transform shoulder = Node(chest, "shoulder" + suffix, new Vector3(side * .255f, .2f, 0f));
            Transform arm = Node(shoulder, "Braco" + suffix, new Vector3(side * .055f, -.015f, 0f));
            Part(arm, "Cabeça do úmero", Art.Joint, Art.Bone, Vector3.zero, Vector3.one * .085f);
            Vector3 elbow = new Vector3(side * .045f, -.29f, 0f);
            Bone(arm, "Úmero", Vector3.zero, elbow, .035f);
            Transform forearm = Node(arm, "forearm" + suffix, elbow);
            Part(forearm, "Articulação do cotovelo", Art.Joint, Art.Bone, Vector3.zero, Vector3.one * .065f);
            Bone(forearm, "Rádio", new Vector3(-.018f, -.02f, 0f), new Vector3(-.014f, -.275f, .015f), .019f);
            Bone(forearm, "Ulna", new Vector3(.018f, -.02f, 0f), new Vector3(.014f, -.275f, .015f), .019f);
            Transform hand = Node(forearm, "hand" + suffix, new Vector3(0f, -.285f, .015f));
            Part(hand, "Ossos do carpo", Art.Joint, Art.Bone, Vector3.zero, new Vector3(.065f, .045f, .04f));
            for (int finger = 0; finger < 4; finger++)
            {
                Vector3 start = new Vector3((finger - 1.5f) * .016f, -.015f, 0f);
                Bone(hand, "Falange " + finger, start, start + new Vector3(0f, -.06f, .025f), .009f);
            }
            if (side > 0)
            {
                if (caster) BuildStaff(hand);
                else
                {
                    Part(hand, "Punho da espada", Art.Shaft, Art.Wood, new Vector3(0f, -.065f, .035f), new Vector3(.045f, .14f, .045f));
                    Part(hand, "Guarda da espada", Art.Box, Art.Iron, new Vector3(0f, -.14f, .035f), new Vector3(.23f, .04f, .045f));
                    Part(hand, "Lâmina da espada", Art.Blade, Art.Iron, new Vector3(0f, -.15f, .035f), Vector3.one);
                }
            }
            else if (!caster)
            {
                Part(hand, "Escudo funerário", Art.Shaft, Art.Iron, new Vector3(0f, -.04f, .1f), new Vector3(.46f, .055f, .56f), new Vector3(90f, 0f, 0f));
                Part(hand, "Face do escudo", Art.Shaft, Art.Wood, new Vector3(0f, -.04f, .135f), new Vector3(.40f, .015f, .50f), new Vector3(90f, 0f, 0f));
                Part(hand, "Umbo do escudo", Art.Joint, Art.Iron, new Vector3(0f, -.04f, .16f), new Vector3(.13f, .13f, .07f));
            }
        }

        private static void BuildLeg(Transform pelvis, int side)
        {
            string suffix = side < 0 ? ".L" : ".R";
            Transform thigh = Node(pelvis, "Perna" + suffix, new Vector3(side * .115f, -.1f, 0f));
            Bone(thigh, "Fêmur", Vector3.zero, new Vector3(side * .022f, -.415f, .015f), .042f);
            Transform shin = Node(thigh, "shin" + suffix, new Vector3(side * .022f, -.415f, .015f));
            Part(shin, "Patela", Art.Joint, Art.Bone, new Vector3(0f, 0f, .015f), new Vector3(.075f, .075f, .065f));
            Bone(shin, "Tíbia", new Vector3(-side * .012f, -.025f, 0f), new Vector3(-side * .008f, -.425f, 0f), .028f);
            Bone(shin, "Fíbula", new Vector3(side * .025f, -.025f, 0f), new Vector3(side * .024f, -.425f, 0f), .016f);
            Transform foot = Node(shin, "foot" + suffix, new Vector3(0f, -.435f, 0f));
            for (int toe = 0; toe < 4; toe++)
                Bone(foot, "Metatarso " + toe, new Vector3((toe - 1.5f) * .017f, 0f, 0f),
                    new Vector3((toe - 1.5f) * .022f, -.018f, .15f - toe * .014f), .014f);
        }

        private static void BuildVestments(Transform chest, Transform head)
        {
            Part(head, "Capuz aberto do conjurador", Art.Hood, Art.Cloth, new Vector3(0f, .09f, -.01f), Vector3.one);
            for (int side = -1; side <= 1; side += 2)
            {
                Part(chest, "Tira de tecido ritual " + side, Art.Rag, Art.Cloth, new Vector3(side * .19f, .23f, .075f), new Vector3(.12f, .92f, 1f));
                Part(chest, "Barra de tecido cerimonial " + side, Art.Rag, Art.Trim, new Vector3(side * .19f, .22f, .085f), new Vector3(.027f, .91f, 1f));
            }
            Part(chest, "Manto posterior rasgado", Art.Rag, Art.Cloth, new Vector3(0f, .24f, -.15f), new Vector3(.57f, 1.22f, -1f));
        }

        private static void BuildStaff(Transform hand)
        {
            Part(hand, "Bastão de ossuário", Art.Shaft, Art.Wood, new Vector3(0f, .13f, .045f), new Vector3(.055f, 1.72f, .055f));
            Part(hand, "Cristal espectral do bastão", Art.Joint, Art.Arcane, new Vector3(0f, 1.03f, .045f), new Vector3(.14f, .22f, .14f));
            for (int side = -1; side <= 1; side += 2)
            {
                Bone(hand, "Garra do bastão", new Vector3(0f, .88f, .045f), new Vector3(side * .105f, 1.01f, .045f), .025f);
                Bone(hand, "Ponta da garra", new Vector3(side * .105f, 1.01f, .045f), new Vector3(side * .08f, 1.13f, .045f), .018f);
            }
            Part(hand, "Faixa do bastão", Art.Rag, Art.Cloth, new Vector3(.025f, .83f, .05f), new Vector3(.09f, .36f, 1f));
        }

        private static void MergeRigidParts(Transform root, string variant)
        {
            // Merge only pieces attached to the same joint. Articulated bones remain independent.
            foreach (Transform joint in root.GetComponentsInChildren<Transform>(true))
            {
                var groups = new Dictionary<Material, List<MeshFilter>>();
                foreach (Transform child in joint)
                {
                    MeshFilter filter = child.GetComponent<MeshFilter>();
                    MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                    if (filter == null || renderer == null) continue;
                    if (!groups.TryGetValue(renderer.sharedMaterial, out List<MeshFilter> group))
                        groups.Add(renderer.sharedMaterial, group = new List<MeshFilter>());
                    group.Add(filter);
                }
                foreach (var group in groups)
                {
                    if (group.Value.Count < 2) continue;
                    string path = joint.name;
                    for (Transform parent = joint.parent; parent != null && parent != root; parent = parent.parent)
                        path = parent.name + "/" + path;
                    string key = variant + "/" + path + "/" + group.Key.name;
                    Mesh combined = Art.Combined(key, joint, group.Value);
                    Part(joint, "Anatomia combinada · " + group.Key.name, combined, group.Key, Vector3.zero, Vector3.one);
                    foreach (MeshFilter filter in group.Value)
                    {
                        // Retain named anatomical landmarks for inspection, without renderer overhead.
                        MeshRenderer renderer = filter.GetComponent<MeshRenderer>(); renderer.enabled = false;
                        Dispose(renderer); Dispose(filter);
                    }
                }
            }
        }

        private static Transform Node(Transform parent, string name, Vector3 at)
        {
            var node = new GameObject(name); node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false); node.transform.localPosition = at;
            return node.transform;
        }
        private static void Bone(Transform parent, string name, Vector3 from, Vector3 to, float radius)
        {
            Transform bone = Part(parent, name, Art.Shaft, Art.Bone, (from + to) * .5f,
                new Vector3(radius * 2f, Vector3.Distance(from, to), radius * 2f));
            bone.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }
        private static Transform Part(Transform parent, string name, Mesh mesh, Material material, Vector3 at,
            Vector3 size, Vector3 angles = default)
        {
            Transform node = Node(parent, name, at); node.localScale = size; node.localRotation = Quaternion.Euler(angles);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = node.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            return node;
        }

        private void OnDestroy()
        {
            RestoreOriginal();
            if (skeletonRoot != null) Dispose(skeletonRoot.gameObject);
            if (casterRoot != null) Dispose(casterRoot.gameObject);
            if (acquired) { Art.Release(); acquired = false; }
        }
        private static void Dispose(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        // All instances share these authored geometries/materials. No mesh/material is allocated per snapshot.
        private static class Art
        {
            public static int Owners { get; private set; }
            private static readonly Dictionary<string, Mesh> combined = new Dictionary<string, Mesh>();
            public static Mesh Joint, Shaft, Box, LeftRib, RightRib, Nose, Blade, Hood, Rag;
            public static Material Bone, Shadow, Eye, Arcane, Iron, Wood, Cloth, Trim;
            public static void Acquire()
            {
                if (Owners++ > 0) return;
                Joint = Sphere(); Shaft = Cylinder(); Box = Cube();
                LeftRib = Rib(-1); RightRib = Rib(1);
                Nose = Mesh("Cavidade nasal autoral", new[] { new Vector3(-.5f,.5f,0f), new Vector3(.5f,.5f,0f), new Vector3(0f,-.5f,.1f) }, new[] { 0,1,2, 2,1,0 });
                Blade = Sword(); Hood = Cowl(); Rag = ClothStrip();
                Bone = Surface("Osso envelhecido", new Color(.78f,.72f,.55f), .06f);
                Shadow = Surface("Cavidades cranianas", new Color(.027f,.025f,.032f), 0f);
                Eye = Surface("Olhos espectrais âmbar", new Color(.76f,.40f,.08f), .1f, new Color(.55f,.20f,.015f));
                Arcane = Surface("Luz espectral violeta", new Color(.38f,.59f,.91f), .4f, new Color(.12f,.31f,.85f));
                Iron = Surface("Ferro funerário", new Color(.26f,.29f,.31f), .35f);
                Wood = Surface("Madeira escura", new Color(.17f,.10f,.065f), .04f);
                Cloth = Surface("Manto ritual púrpura", new Color(.18f,.105f,.24f), .02f);
                Trim = Surface("Bordas rituais desbotadas", new Color(.46f,.36f,.22f), .05f);
            }
            public static Mesh Combined(string key, Transform joint, List<MeshFilter> pieces)
            {
                if (combined.TryGetValue(key, out Mesh existing)) return existing;
                var instances = new CombineInstance[pieces.Count];
                for (int index = 0; index < pieces.Count; index++)
                    instances[index] = new CombineInstance { mesh = pieces[index].sharedMesh,
                        transform = joint.worldToLocalMatrix * pieces[index].transform.localToWorldMatrix };
                var result = new Mesh { name = "Catacumbas autoral · anatomia · " + key };
                result.CombineMeshes(instances, true, true, false); result.RecalculateBounds();
                combined.Add(key, result); return result;
            }
            public static void Release()
            {
                if (--Owners > 0) return;
                foreach (Mesh mesh in combined.Values) Dispose(mesh);
                combined.Clear();
                foreach (Mesh mesh in new[] { Joint, Shaft, Box, LeftRib, RightRib, Nose, Blade, Hood, Rag }) Dispose(mesh);
                foreach (Material material in new[] { Bone, Shadow, Eye, Arcane, Iron, Wood, Cloth, Trim }) Dispose(material);
                Joint = Shaft = Box = LeftRib = RightRib = Nose = Blade = Hood = Rag = null;
                Bone = Shadow = Eye = Arcane = Iron = Wood = Cloth = Trim = null;
                Owners = 0;
            }
            private static Material Surface(string name, Color color, float shine, Color emission = default)
            {
                var material = new Material(Shader.Find("Standard")) { name = "Catacumbas autoral · " + name, color = color, enableInstancing = true };
                material.SetFloat("_Glossiness", shine);
                if (emission.maxColorComponent > 0f) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", emission); }
                return material;
            }
            private static Mesh Mesh(string name, Vector3[] vertices, int[] triangles)
            {
                var mesh = new Mesh { name = "Catacumbas autoral · " + name, vertices = vertices, triangles = triangles };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
            private static Mesh Sphere()
            {
                const int rings = 8, segments = 12;
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (int y = 0; y <= rings; y++)
                {
                    float latitude = Mathf.PI * y / rings;
                    for (int x = 0; x <= segments; x++)
                    {
                        float longitude = Mathf.PI * 2f * x / segments;
                        vertices.Add(new Vector3(Mathf.Sin(latitude)*Mathf.Sin(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude)*Mathf.Cos(longitude)) * .5f);
                        if (y == rings || x == segments) continue;
                        int a = y*(segments+1)+x, b = a+segments+1;
                        triangles.AddRange(new[] {a,b,a+1, a+1,b,b+1});
                    }
                }
                return Mesh("Epífise e crânio", vertices.ToArray(), triangles.ToArray());
            }
            private static Mesh Cylinder()
            {
                const int segments = 8;
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (int ring = 0; ring < 4; ring++)
                {
                    float y = ring == 0 ? -.5f : ring == 1 ? -.38f : ring == 2 ? .38f : .5f;
                    float radius = ring == 0 || ring == 3 ? .5f : .35f;
                    for (int i = 0; i < segments; i++)
                    {
                        float angle = i * Mathf.PI * 2f / segments;
                        vertices.Add(new Vector3(Mathf.Sin(angle)*radius,y,Mathf.Cos(angle)*radius));
                        if (ring == 3) continue;
                        int a = ring*segments+i, b = ring*segments+(i+1)%segments;
                        triangles.AddRange(new[] {a,b,a+segments, b,b+segments,a+segments});
                    }
                }
                for (int i = 1; i < segments-1; i++)
                { triangles.AddRange(new[] {0,i+1,i, 3*segments,3*segments+i,3*segments+i+1}); }
                return Mesh("Diáfise facetada", vertices.ToArray(), triangles.ToArray());
            }
            private static Mesh Cube() => Mesh("Bloco de dentes e ferragens", new[] {
                new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f) },
                new[] {0,2,1,1,2,3,4,5,6,5,7,6,0,4,2,4,6,2,1,3,5,3,7,5,2,6,3,3,6,7,0,1,4,1,5,4});
            private static Mesh Rib(int side)
            {
                Vector3[] points = { new Vector3(0f,0f,-.08f), new Vector3(.17f,-.005f,-.095f), new Vector3(.27f,-.025f,-.015f),
                    new Vector3(.245f,-.06f,.105f),new Vector3(.12f,-.08f,.165f),new Vector3(.015f,-.06f,.145f) };
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (int point = 0; point < points.Length; point++)
                {
                    Vector3 center = points[point]; center.x *= side;
                    Vector3 tangent = point < points.Length-1 ? points[point+1]-points[point] : points[point]-points[point-1]; tangent.x *= side;
                    Vector3 across = Vector3.Cross(tangent,Vector3.up).normalized;
                    for (int edge = 0; edge < 6; edge++)
                    {
                        float angle = edge*Mathf.PI/3f;
                        vertices.Add(center+(Vector3.up*Mathf.Cos(angle)+across*Mathf.Sin(angle))*.014f);
                        if (point == points.Length-1) continue;
                        int a = point*6+edge, b = point*6+(edge+1)%6;
                        triangles.AddRange(new[] {a,b,a+6,b,b+6,a+6});
                    }
                }
                return Mesh(side < 0 ? "Arco costal esquerdo" : "Arco costal direito",vertices.ToArray(),triangles.ToArray());
            }
            private static Mesh Sword() => Mesh("Lâmina com ponta e fio",new[] {
                new Vector3(-.045f,0f,0f),new Vector3(.045f,0f,0f),new Vector3(0f,0f,.017f),new Vector3(0f,0f,-.017f),
                new Vector3(-.04f,-.47f,0f),new Vector3(.04f,-.47f,0f),new Vector3(0f,-.47f,.015f),new Vector3(0f,-.47f,-.015f),new Vector3(0f,-.58f,0f) },
                new[] {0,4,2,2,4,6,2,6,1,1,6,5,1,5,3,3,5,7,3,7,0,0,7,4,4,8,6,6,8,5,5,8,7,7,8,4,0,2,1,0,1,3});
            private static Mesh Cowl()
            {
                const int segments = 12;
                float[] heights = {-.16f,.08f,.27f,.31f}, widths = {.19f,.22f,.21f,.075f};
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (int ring = 0; ring < heights.Length; ring++)
                    for (int segment = 0; segment <= segments; segment++)
                    {
                        float angle = Mathf.Lerp(55f,305f,segment/(float)segments)*Mathf.Deg2Rad;
                        vertices.Add(new Vector3(Mathf.Sin(angle)*widths[ring],heights[ring],Mathf.Cos(angle)*widths[ring]));
                        if (ring == heights.Length-1 || segment == segments) continue;
                        int a = ring*(segments+1)+segment, b = a+segments+1;
                        triangles.AddRange(new[] {a,b,a+1,a+1,b,b+1,a+1,b,a,b+1,b,a+1});
                    }
                return Mesh("Capuz aberto articulado",vertices.ToArray(),triangles.ToArray());
            }
            private static Mesh ClothStrip() => Mesh("Tecido rasgado", new[] {new Vector3(-.5f,0f,0f),new Vector3(.5f,0f,0f),
                new Vector3(-.43f,-.55f,.035f),new Vector3(.48f,-.55f,.025f),new Vector3(-.5f,-.92f,.07f),new Vector3(-.12f,-.82f,.065f),new Vector3(.22f,-1f,.055f),new Vector3(.46f,-.89f,.06f)},
                new[] {0,2,1,1,2,3,2,4,5,2,5,3,3,5,6,3,6,7,1,2,0,3,2,1,5,4,2,3,5,2,6,5,3,7,6,3});
        }
    }
}
