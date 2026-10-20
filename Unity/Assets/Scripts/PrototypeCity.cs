using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace GuerraDasNacoes
{
    // Presentation only. Map access, destinations, NPC positions and rewards come from the server.
    [DefaultExecutionOrder(-420)]
    public sealed class PrototypeCity : MonoBehaviour
    {
        private readonly List<GameObject> ruinsRoots = new List<GameObject>();
        private readonly Dictionary<string, Transform> npcs = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Transform> stalls = new Dictionary<string, Transform>();
        private readonly Dictionary<string, TextMesh> npcLabels = new Dictionary<string, TextMesh>();
        private readonly Dictionary<string, Transform> lootViews = new Dictionary<string, Transform>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private GameObject cityRoot, ruinsPortal, lootRoot, catacombsRoot, catacombsPortal, ruinsRoot;
        private readonly Dictionary<Light, bool> outdoorDirectionalLights = new Dictionary<Light, bool>();
        private bool catacombsLighting;
        private AmbientMode outdoorAmbientMode;
        private Color outdoorAmbientLight, outdoorFogColor;
        private float outdoorAmbientIntensity, outdoorFogDensity, outdoorFogStart, outdoorFogEnd;
        private bool outdoorFog;
        private FogMode outdoorFogMode;
        private Material outdoorSkybox;
        private Material stone, paving, timber, roof, iron, cloth, glow, windowGlass;
        private PrototypeCharacter hero;
        private bool built;
        private bool mapApplied;
        private string mapId = "ruins";
        public static bool IsInCity { get; private set; }
        public static string CurrentMapId { get; private set; } = "ruins";
        public static string MapName(string value) => value == "city" ? "Cidade da Fronteira" :
            value == "catacombs" ? "Catacumbas" : value == "ruins" ? "Vale dos Clãs" : "Mapa: " + value;
        public int NpcCount => npcs.Count;
        public bool IsBuilt => built;
        public GameObject CityRoot => cityRoot;
        private PrototypeOnlineClient Network => GetComponent<PrototypeOnlineClient>();

        private void Start() { Build(); }

        private void Build()
        {
            if (built) return;
            hero = GetComponent<PrototypeCharacter>();
            if (hero == null || hero.VisualRoot == null) return;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root.name == "Cenario" || root.GetComponent<PrototypeMonster>() != null ||
                    root.name.Contains("Rochas escaneadas") || root.name == PrototypeWorldExpansion.RootName) ruinsRoots.Add(root);
                if (root.name == "Cenario") ruinsRoot = root;
                if (root.name == "Catacumbas") catacombsRoot = root;
            }
            stone = Surface("MuroEscaneado", new Color(.42f, .41f, .36f));
            paving = Surface("PisoEscaneado", new Color(.39f, .39f, .34f));
            timber = Solid("Carvalho envelhecido", new Color(.18f, .12f, .075f), .12f);
            roof = Solid("Telhas de ardósia", new Color(.13f, .17f, .19f), .24f);
            iron = Solid("Ferro das oficinas", new Color(.19f, .22f, .23f), .58f);
            cloth = Solid("Tendas da cidade", new Color(.36f, .18f, .13f), .05f);
            windowGlass = Solid("Vidraças âmbar", new Color(.33f, .22f, .12f), .5f);
            windowGlass.EnableKeyword("_EMISSION"); windowGlass.SetColor("_EmissionColor", new Color(.09f, .045f, .013f));
            glow = Solid("Luz dos portais e saques", new Color(.3f, .78f, .92f), .35f);
            if (glow.HasProperty("_EmissionColor"))
            { glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", new Color(.09f, .53f, .65f)); }
            cityRoot = new GameObject("Cidade da Fronteira · mapa seguro");
            cityRoot.SetActive(false);
            Box(cityRoot.transform, "Praça de pedra", new Vector3(0f, -.18f, 0f), new Vector3(38f, .36f, 38f), paving, true, 8);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(cityRoot.transform, "Limite da cidade", new Vector3(side * 18.5f, .8f, 0f), new Vector3(.5f, 1.6f, 38f), stone, true);
                Box(cityRoot.transform, "Limite da cidade", new Vector3(0f, .8f, side * 18.5f), new Vector3(38f, 1.6f, .5f), stone, true);
            }
            // Buildings remain outside the playable ±18 square, matching the server's open city grid.
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 8.5f;
                House(new Vector3(x, 0f, 23f), 0f, i % 2 == 0);
                if (i != 2) House(new Vector3(x, 0f, -23f), 180f, i % 2 != 0);
                House(new Vector3(-23f, 0f, x), 90f, i % 2 != 0);
                House(new Vector3(23f, 0f, x), -90f, i % 2 == 0);
            }
            Fountain();
            Stall("npc-ferreiro", "Ferreiro", new Vector3(-5f, 0f, 3f), new Color(.42f, .24f, .13f), 0);
            Stall("npc-armeira", "Armeira", new Vector3(-2f, 0f, 6f), new Color(.24f, .30f, .33f), 1);
            Stall("npc-joalheira", "Joalheira", new Vector3(3f, 0f, 6f), new Color(.36f, .25f, .39f), 2);
            Stall("npc-alquimista", "Alquimista", new Vector3(6f, 0f, 2f), new Color(.21f, .34f, .25f), 3);
            Stall("npc-mercador", "Mercador", new Vector3(3f, 0f, -4f), new Color(.39f, .31f, .19f), 4);
            CreateNpc("npc-guardiao", new Vector3(-1.8f, 0f, -7f), cityRoot.transform, 5);
            Portal(cityRoot.transform, new Vector3(0f, 0f, -7f), "Portal para as ruínas");
            ruinsPortal = new GameObject("Portal para a cidade · ruínas");
            Portal(ruinsPortal.transform, new Vector3(0f, 0f, -3f), "Retorno à cidade");
            Portal(ruinsPortal.transform, new Vector3(0f, 0f, 19f), "Entrada das Catacumbas");
            lootRoot = new GameObject("Saques pessoais confirmados");
            built = true;
            ApplyMap(mapId);
        }

        public void ApplyMap(string nextMap)
        {
            nextMap = string.IsNullOrEmpty(nextMap) ? "ruins" : nextMap;
            bool changed = !mapApplied || mapId != nextMap;
            mapId = nextMap;
            CurrentMapId = mapId;
            IsInCity = mapId == "city";
            if (!built) return;
            DiscoverCatacombs();
            cityRoot.SetActive(IsInCity);
            bool inRuins = mapId == "ruins";
            ruinsPortal.SetActive(inRuins);
            foreach (GameObject root in ruinsRoots) if (root != null) root.SetActive(inRuins);
            if (catacombsRoot != null) catacombsRoot.SetActive(mapId == "catacombs");
            SetCatacombsLighting(mapId == "catacombs");
            if (changed)
            {
                PrototypeCombat combat = GetComponent<PrototypeCombat>();
                if (combat != null) combat.CancelMapPresentation();
                foreach (Transform loot in lootViews.Values) if (loot != null) Destroy(loot.gameObject);
                lootViews.Clear();
                lootRoot.SetActive(false);
            }
            mapApplied = true;
        }

        // Root enumeration also finds the saved inactive dungeon; GameObject.Find cannot.
        private void DiscoverCatacombs()
        {
            if (catacombsRoot == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    if (root.name == "Catacumbas") { catacombsRoot = root; break; }
            if (catacombsRoot == null || catacombsPortal != null) return;
            catacombsPortal = new GameObject("Portal de saída das Catacumbas");
            catacombsPortal.transform.SetParent(catacombsRoot.transform, false);
            Portal(catacombsPortal.transform, new Vector3(0f, 0f, -3f), "Retorno ao Vale dos Clãs");
        }

        public Transform MonsterRoot(string monsterMap)
        {
            if (!built) Build();
            if (monsterMap == "catacombs") { DiscoverCatacombs(); return catacombsRoot != null ? catacombsRoot.transform : null; }
            if (monsterMap == "city") return cityRoot != null ? cityRoot.transform : null;
            return monsterMap == "ruins" && ruinsRoot != null ? ruinsRoot.transform : null;
        }

        private void SetCatacombsLighting(bool enabled)
        {
            if (catacombsLighting == enabled) return;
            if (enabled)
            {
                outdoorAmbientMode = RenderSettings.ambientMode; outdoorAmbientLight = RenderSettings.ambientLight;
                outdoorAmbientIntensity = RenderSettings.ambientIntensity; outdoorSkybox = RenderSettings.skybox;
                outdoorFog = RenderSettings.fog; outdoorFogColor = RenderSettings.fogColor; outdoorFogMode = RenderSettings.fogMode;
                outdoorFogDensity = RenderSettings.fogDensity; outdoorFogStart = RenderSettings.fogStartDistance; outdoorFogEnd = RenderSettings.fogEndDistance;
                outdoorDirectionalLights.Clear();
                foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (light.type == LightType.Directional && light.gameObject.scene == gameObject.scene)
                    { outdoorDirectionalLights[light] = light.enabled; light.enabled = false; }
                // Keep bones and cast warnings readable away from the six local torches.
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.34f, .38f, .45f);
                RenderSettings.ambientIntensity = 1f; RenderSettings.skybox = null;
                RenderSettings.fog = true; RenderSettings.fogColor = new Color(.045f, .055f, .075f);
                RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = .022f;
            }
            else
            {
                RenderSettings.ambientMode = outdoorAmbientMode; RenderSettings.ambientLight = outdoorAmbientLight;
                RenderSettings.ambientIntensity = outdoorAmbientIntensity; RenderSettings.skybox = outdoorSkybox;
                RenderSettings.fog = outdoorFog; RenderSettings.fogColor = outdoorFogColor; RenderSettings.fogMode = outdoorFogMode;
                RenderSettings.fogDensity = outdoorFogDensity; RenderSettings.fogStartDistance = outdoorFogStart; RenderSettings.fogEndDistance = outdoorFogEnd;
                foreach (KeyValuePair<Light, bool> entry in outdoorDirectionalLights) if (entry.Key != null) entry.Key.enabled = entry.Value;
                outdoorDirectionalLights.Clear();
            }
            catacombsLighting = enabled;
        }

        private void Update()
        {
            if (!built) Build();
            if (!built || Network == null || Network.Snapshot == null) return;
            OnlineSnapshot snapshot = Network.Snapshot;
            if (snapshot.mapId != mapId) return;
            lootRoot.SetActive(true);
            if (snapshot.npcs != null)
            {
                foreach (KeyValuePair<string, Transform> entry in npcs)
                    if (entry.Key != "npc-guardiao") entry.Value.gameObject.SetActive(false);
                foreach (Transform stall in stalls.Values) stall.gameObject.SetActive(false);
                foreach (OnlineNpc npc in snapshot.npcs)
                {
                    if (npc == null || npc.mapId != "city") continue;
                    if (!npcs.TryGetValue(npc.id, out Transform view))
                    {
                        Stall(npc.id, npc.name, new Vector3(npc.x, 0f, npc.z), new Color(.32f, .25f, .18f), RoleIndex(npc.role));
                        view = npcs[npc.id];
                    }
                    view.gameObject.SetActive(true);
                    view.position = new Vector3(npc.x, .04f, npc.z);
                    if (stalls.TryGetValue(npc.id, out Transform stall))
                    { stall.gameObject.SetActive(true); stall.position = new Vector3(npc.x, 0f, npc.z + 1.25f); }
                    if (npcLabels.TryGetValue(npc.id, out TextMesh label)) label.text = npc.name;
                }
            }
            HashSet<string> present = new HashSet<string>();
            if (snapshot.loot != null)
                foreach (OnlineLoot loot in snapshot.loot)
                {
                    if (loot == null || loot.mapId != mapId) continue;
                    present.Add(loot.id);
                    if (!lootViews.TryGetValue(loot.id, out Transform view))
                    {
                        GameObject chest = new GameObject("Saque · " + loot.name);
                        chest.transform.SetParent(lootRoot.transform, false);
                        Box(chest.transform, "Baú pessoal", new Vector3(0f, .15f, 0f), new Vector3(.4f, .3f, .3f), timber);
                        Box(chest.transform, "Tampa", new Vector3(0f, .32f, 0f), new Vector3(.43f, .08f, .33f), iron);
                        Box(chest.transform, "Fivela", new Vector3(0f, .2f, -.16f), new Vector3(.07f, .16f, .025f), glow);
                        Box(chest.transform, "Sinal de saque", new Vector3(0f, .85f, 0f), new Vector3(.035f, 1.0f, .035f), glow);
                        view = chest.transform;
                        lootViews.Add(loot.id, view);
                    }
                    view.position = new Vector3(loot.x, .04f, loot.z);
                    view.GetChild(3).localScale = new Vector3(.035f, .8f + .2f * Mathf.Sin(Time.time * 3f), .035f);
                }
            List<string> removed = new List<string>();
            foreach (KeyValuePair<string, Transform> entry in lootViews)
                if (!present.Contains(entry.Key)) { if (entry.Value != null) Destroy(entry.Value.gameObject); removed.Add(entry.Key); }
            foreach (string id in removed) lootViews.Remove(id);
        }

        private Material Surface(string wanted, Color fallback)
        {
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (Material candidate in renderer.sharedMaterials)
                    if (candidate != null && candidate.name.Contains(wanted))
                    {
                        Material copy = new Material(candidate) { name = "Cidade · " + wanted };
                        copy.mainTextureScale = wanted.Contains("Piso") ? Vector2.one * 22f : Vector2.one * 2.5f;
                        ownedMaterials.Add(copy); return copy;
                    }
            // A scanned cobblestone surface may have a localized name in older generated scenes.
            if (wanted.Contains("Piso"))
                foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (renderer.name.StartsWith("Ground_"))
                        foreach (Material candidate in renderer.sharedMaterials)
                            if (candidate != null && candidate.mainTexture != null)
                            { Material copy = new Material(candidate); copy.mainTextureScale = Vector2.one * 22f; ownedMaterials.Add(copy); return copy; }
            return Solid("Cidade · " + wanted, fallback, .12f);
        }
        private Material Solid(string name, Color color, float smoothness)
        {
            Material material = new Material(Shader.Find("Standard")) { name = name, color = color };
            material.SetFloat("_Glossiness", smoothness); ownedMaterials.Add(material); return material;
        }
        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision = false, int layer = 0)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            box.transform.SetParent(parent, false); box.transform.localPosition = position; box.transform.localScale = size;
            box.layer = layer; box.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = box.GetComponent<Collider>(); collider.enabled = collision;
            if (!collision) Destroy(collider);
            return box;
        }
        private void House(Vector3 position, float yaw, bool tall)
        {
            GameObject house = new GameObject("Casa de pedra e madeira"); house.transform.SetParent(cityRoot.transform, false);
            house.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            float height = tall ? 5.8f : 4.5f;
            Box(house.transform, "Alvenaria", new Vector3(0f, height * .5f, 0f), new Vector3(7.2f, height, 6.5f), stone);
            Box(house.transform, "Viga frontal", new Vector3(0f, height - .3f, -3.3f), new Vector3(7.4f, .22f, .2f), timber);
            foreach (float x in new[] { -3.4f, 0f, 3.4f })
                Box(house.transform, "Pilar de carvalho", new Vector3(x, height * .5f, -3.3f), new Vector3(.22f, height, .22f), timber);
            foreach (float x in new[] { -2f, 2f })
            {
                Box(house.transform, "Janela", new Vector3(x, height * .62f, -3.29f), new Vector3(1.1f, 1.4f, .12f), timber);
                Box(house.transform, "Vidro iluminado", new Vector3(x, height * .62f, -3.37f), new Vector3(.85f, 1.1f, .07f), windowGlass);
                Box(house.transform, "Caixilho vertical", new Vector3(x, height * .62f, -3.42f), new Vector3(.07f, 1.15f, .08f), timber);
                Box(house.transform, "Caixilho horizontal", new Vector3(x, height * .62f, -3.42f), new Vector3(.9f, .07f, .08f), timber);
            }
            Box(house.transform, "Porta", new Vector3(0f, 1.15f, -3.35f), new Vector3(1.4f, 2.3f, .18f), timber);
            Mesh mesh = new Mesh { name = "Telhado de duas águas" };
            mesh.vertices = new[] {new Vector3(-4f,height,-3.9f),new Vector3(4f,height,-3.9f),new Vector3(0f,height+2f,-3.9f),new Vector3(-4f,height,3.9f),new Vector3(4f,height,3.9f),new Vector3(0f,height+2f,3.9f)};
            mesh.triangles = new[] {0,2,1,3,4,5,0,3,5,0,5,2,2,5,4,2,4,1};
            mesh.RecalculateNormals(); ownedMeshes.Add(mesh);
            GameObject top = new GameObject("Telhado"); top.transform.SetParent(house.transform,false);
            top.AddComponent<MeshFilter>().sharedMesh = mesh; top.AddComponent<MeshRenderer>().sharedMaterial = roof;
            Box(house.transform, "Chaminé", new Vector3(2.4f,height+1.7f,.7f), new Vector3(.7f,2.2f,.7f), stone);
        }
        private void Fountain()
        {
            // Shallow decorative basin has no collider, preserving the server's open navigation grid.
            GameObject group = new GameObject("Fonte da praça"); group.transform.SetParent(cityRoot.transform, false);
            group.transform.position = new Vector3(-7f,0f,-6f);
            for(int i=0;i<12;i++)
            {
                float angle=i*Mathf.PI/6f; GameObject rim=Box(group.transform,"Borda esculpida",new Vector3(Mathf.Sin(angle)*1.1f,.22f,Mathf.Cos(angle)*1.1f),new Vector3(.6f,.4f,.28f),stone);
                rim.transform.localRotation=Quaternion.Euler(0f,angle*Mathf.Rad2Deg,0f);
            }
            Box(group.transform,"Água",new Vector3(0f,.13f,0f),new Vector3(1.7f,.05f,1.7f),glow);
            Box(group.transform,"Coluna",new Vector3(0f,.6f,0f),new Vector3(.32f,1.2f,.32f),stone);
        }
        private void Stall(string id, string label, Vector3 position, Color color, int role)
        {
            GameObject stall = new GameObject("Oficina · " + label); stall.transform.SetParent(cityRoot.transform,false); stall.transform.position=position+new Vector3(0f,0f,1.25f);
            Material canopy=Solid("Toldo · " + label,color,.1f);
            Box(stall.transform,"Balcão de carvalho",new Vector3(0f,.45f,.2f),new Vector3(2.4f,.9f,.65f),timber);
            foreach(float x in new[]{-1.3f,1.3f})Box(stall.transform,"Poste",new Vector3(x,1.1f,.4f),new Vector3(.12f,2.2f,.12f),timber);
            GameObject awning=Box(stall.transform,"Toldo de tecido",new Vector3(0f,2.2f,.4f),new Vector3(2.9f,.12f,1.9f),canopy); awning.transform.localRotation=Quaternion.Euler(8f,0f,0f);
            PrototypeEquipmentVisual sourceGear = hero.VisualRoot.GetComponentInChildren<PrototypeEquipmentVisual>(true);
            if (sourceGear != null && (role == 0 || role == 1))
            {
                DisplayPiece(stall.transform, role == 0 ? sourceGear.SwordRenderer : sourceGear.GetArmourPiece(EquipmentSlot.Head), new Vector3(-.6f, 1.22f, .15f), .65f);
                DisplayPiece(stall.transform, role == 0 ? sourceGear.ShieldRenderer : sourceGear.GetArmourPiece(EquipmentSlot.Chest), new Vector3(.55f, 1.22f, .15f), .6f);
            }
            else if (role == 3)
            {
                for (int i = 0; i < 4; i++)
                {
                    Material liquid = Solid("Elixir da oficina", i % 2 == 0 ? new Color(.48f, .08f, .055f) : new Color(.08f, .28f, .32f), .7f);
                    GameObject bottle = GameObject.CreatePrimitive(PrimitiveType.Sphere); bottle.name = "Frasco de elixir";
                    bottle.transform.SetParent(stall.transform, false); bottle.transform.localPosition = new Vector3(-.8f + i * .52f, 1.08f, .2f); bottle.transform.localScale = new Vector3(.18f, .25f, .18f);
                    bottle.GetComponent<Renderer>().sharedMaterial = liquid; bottle.GetComponent<Collider>().enabled = false; Destroy(bottle.GetComponent<Collider>());
                    Box(stall.transform, "Rolha do frasco", new Vector3(-.8f + i * .52f, 1.24f, .2f), new Vector3(.07f, .1f, .07f), timber);
                }
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 boxPosition = new Vector3(-.65f + i * .65f, 1.05f, .2f);
                    Box(stall.transform, role == 2 ? "Estojo de joias" : "Caixa de mercadorias", boxPosition, new Vector3(.38f, .25f, .3f), timber);
                    Box(stall.transform, "Dobradiça do estojo", boxPosition + new Vector3(0f, .03f, -.16f), new Vector3(.06f, .15f, .025f), iron);
                }
            }
            stalls[id]=stall.transform;
            CreateNpc(id,position,cityRoot.transform,role);
            GameObject sign=new GameObject(label);sign.transform.SetParent(stall.transform,false);sign.transform.localPosition=new Vector3(0f,2.5f,.2f);
            TextMesh text=sign.AddComponent<TextMesh>();text.text=label;text.fontSize=40;text.characterSize=.035f;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(.88f,.78f,.56f);
            npcLabels[id]=text;
            sign.AddComponent<CityBillboard>();
        }
        private void DisplayPiece(Transform parent, SkinnedMeshRenderer source, Vector3 position, float size)
        {
            if (source == null) return;
            Mesh mesh = new Mesh { name = "Mercadoria · " + source.name };
            source.BakeMesh(mesh); mesh.RecalculateBounds();
            if (mesh.vertexCount == 0) { Destroy(mesh); return; }
            Vector3 center = mesh.bounds.center;
            float extent = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
            if (extent <= .001f) { Destroy(mesh); return; }
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - center) * (size / extent);
            mesh.vertices = vertices; mesh.RecalculateBounds(); ownedMeshes.Add(mesh);
            GameObject display = new GameObject("Expositor · " + source.name);
            display.transform.SetParent(parent, false); display.transform.localPosition = position;
            display.transform.localRotation = Quaternion.Euler(0f, 15f, -12f);
            display.AddComponent<MeshFilter>().sharedMesh = mesh;
            display.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
        }
        private static int RoleIndex(string role)
        {
            switch (role)
            {
                case "blacksmith": return 0;
                case "armorer": return 1;
                case "jeweler": return 2;
                case "alchemist": return 3;
                case "merchant": return 4;
                default: return 5;
            }
        }
        private void CreateNpc(string id, Vector3 position, Transform parent, int role)
        {
            GameObject npc=new GameObject(id);npc.transform.SetParent(parent,false);npc.transform.position=position+Vector3.up*.04f;
            npc.transform.rotation=Quaternion.LookRotation(new Vector3(-position.x,0f,-position.z).normalized);
            GameObject visual=Instantiate(hero.VisualRoot.gameObject,npc.transform,false);visual.name="Habitante · modelo 3D";visual.transform.localRotation=Quaternion.identity;
            foreach(MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
                if(!(behaviour is PrototypeEquipmentVisual)){behaviour.enabled=false;Destroy(behaviour);}
            foreach(Collider collider in visual.GetComponentsInChildren<Collider>(true)){collider.enabled=false;Destroy(collider);}
            foreach(Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))Destroy(body);
            Animator animator=visual.GetComponentInChildren<Animator>();if(animator!=null){animator.applyRootMotion=false;animator.speed=.93f+role*.025f;}
            List<EquipmentSlot> slots=new List<EquipmentSlot>{EquipmentSlot.Feet,EquipmentSlot.Belt};
            if(role==0||role==1||role==5){slots.Add(EquipmentSlot.Chest);slots.Add(EquipmentSlot.Hands);slots.Add(EquipmentSlot.Legs);}
            if(role==1||role==5)slots.Add(EquipmentSlot.Shoulders);
            if(role==5){slots.Add(EquipmentSlot.Head);slots.Add(EquipmentSlot.Sword);slots.Add(EquipmentSlot.Shield);}
            List<string> appearances=new List<string>();foreach(EquipmentSlot slot in slots)appearances.Add(role==5?"steel":"training");
            PrototypeEquipmentVisual gear=visual.GetComponentInChildren<PrototypeEquipmentVisual>(true);if(gear!=null)gear.ApplySlots(slots,appearances);
            npcs[id]=npc.transform;
        }
        private void Portal(Transform parent, Vector3 position, string name)
        {
            GameObject portal=new GameObject(name);portal.transform.SetParent(parent,false);portal.transform.localPosition=position;
            Box(portal.transform,"Soleira",new Vector3(0f,.06f,0f),new Vector3(2.4f,.12f,1.2f),stone);
            Box(portal.transform,"Pilar oeste",new Vector3(-.9f,1.25f,0f),new Vector3(.35f,2.5f,.4f),stone);
            Box(portal.transform,"Pilar leste",new Vector3(.9f,1.25f,0f),new Vector3(.35f,2.5f,.4f),stone);
            Box(portal.transform,"Arco",new Vector3(0f,2.6f,0f),new Vector3(2.2f,.35f,.5f),stone);
            Box(portal.transform,"Energia central",new Vector3(0f,1.3f,.12f),new Vector3(.06f,1.9f,.06f),glow);
            for(int i=0;i<7;i++)Box(portal.transform,"Runas",new Vector3(-.65f+i*.22f,2.62f,-.27f),new Vector3(.09f,.14f,.035f),glow);
        }
        private void OnDestroy()
        {
            SetCatacombsLighting(false);
            foreach(Mesh mesh in ownedMeshes)if(mesh!=null)Destroy(mesh);
            foreach(Material material in ownedMaterials)if(material!=null)Destroy(material);
            if(cityRoot!=null)Destroy(cityRoot);if(ruinsPortal!=null)Destroy(ruinsPortal);if(lootRoot!=null)Destroy(lootRoot);if(catacombsPortal!=null)Destroy(catacombsPortal);
            IsInCity=false;
            CurrentMapId="ruins";
        }
    }
    public sealed class CityBillboard : MonoBehaviour
    {
        private void LateUpdate(){if(Camera.main!=null)transform.rotation=Camera.main.transform.rotation;}
    }
}
