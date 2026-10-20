using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authored underground wing. Replaces its own inactive root only; characters,
/// the courtyard, the frontier and their persistent identifiers are never recreated.</summary>
public static class CatacombsSetup
{
    private const string ScenePath = "Assets/Scenes/Patio.unity";
    private const string AssetRoot = "Assets/Art/Catacombs";
    private const string MaterialRoot = "Assets/Materials/Catacombs";
    private static Material floor, wall, stone, iron, bone, darkness, amber, ritual;

    [MenuItem("Guerra das Nações/Adicionar Catacumbas preservando o Vale")]
    public static void Apply()
    {
        RequireEditing();
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new BuildFailedException("Salve as cenas abertas antes de implantar as Catacumbas.");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<PrototypeCharacter>(true) != null))
            throw new BuildFailedException("Personagem original não encontrado; a cena não será substituída.");
        var originalRoots = scene.GetRootGameObjects().Where(root => root.name != CatacombsLayout.RootName)
            .ToDictionary(root => root, root => (root.transform.localToWorldMatrix, root.activeSelf));
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == CatacombsLayout.RootName) Object.DestroyImmediate(root);
        Directory.CreateDirectory(AssetRoot);
        Directory.CreateDirectory(MaterialRoot);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureMaterials();

        GameObject catacombs = new GameObject(CatacombsLayout.RootName);
        SceneManager.MoveGameObjectToScene(catacombs, scene);
        Transform ground = Group(catacombs.transform, "01 · lajes e corredores");
        BuildFloor(ground);
        Transform walls = Group(catacombs.transform, "02 · alvenaria e arcos sem teto");
        BuildWalls(walls);
        BuildArch(walls, new Vector3(0,0,7), false, 6f);
        BuildArch(walls, new Vector3(-5,0,17), true, 6f);
        BuildArch(walls, new Vector3(5,0,33), true, 6f);
        BuildArch(walls, new Vector3(0,0,44), false, 6f);
        Transform entry = Group(catacombs.transform, "03 · vestíbulo de retorno");
        BuildEntry(entry);
        Transform ossuary = Group(catacombs.transform, "04 · ossuário dos soldados");
        BuildOssuary(ossuary);
        Transform chamber = Group(catacombs.transform, "05 · câmara dos conjuradores");
        BuildRitual(chamber);
        Transform crypt = Group(catacombs.transform, "06 · cripta dos guardiões");
        BuildCrypt(crypt);
        Transform lights = Group(catacombs.transform, "07 · tochas e iluminação contida");
        Torch(lights, new Vector3(-5.7f,1.35f,3), false, true);
        Torch(lights, new Vector3(2.25f,1.35f,22), false, true);
        Torch(lights, new Vector3(-17.9f,1.35f,17), false, true);
        Torch(lights, new Vector3(17.8f,1.35f,33), true, true);
        Torch(lights, new Vector3(-8.8f,1.35f,53), false, true);
        Torch(lights, new Vector3(8.8f,1.35f,53), true, true);
        foreach (GameObject root in originalRoots.Keys)
            if (root == null || root.transform.localToWorldMatrix != originalRoots[root].Item1 || root.activeSelf != originalRoots[root].Item2)
                throw new BuildFailedException("A implantação alterou uma raiz original do Vale.");
        catacombs.SetActive(false);
        AssetDatabase.SaveAssets();
        ValidateInScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new BuildFailedException("Não foi possível salvar as Catacumbas.");
        Debug.Log("[CATACOMBS SETUP PASS] Piso, paredes, arcos, ossuário, ritual e cripta; root inativa; Vale e personagens preservados.");
    }

    public static void Validate()
    {
        RequireEditing();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ValidateInScene(scene);
        ServerWorldExporter.ExportCatacombs();
        Debug.Log("[CATACOMBS VALIDATE PASS] Arte, shaders, cápsula, portais, encontros e caminhos reais conferidos.");
    }

    public static void Capture()
    {
        RequireEditing();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CatacombsPreviewRunner.Begin();
    }

    public static void CaptureInPlayMode()
    {
        if(!EditorApplication.isPlaying)throw new BuildFailedException("Capture as Catacumbas em PlayMode descartável.");
        var snapshots=new List<GameObject>();
        var meshes=new List<Mesh>();
        try
        {
            PrototypeCharacter player=Object.FindFirstObjectByType<PrototypeCharacter>();
            PrototypeCombat combat=player.GetComponent<PrototypeCombat>();
            PrototypeMonster source=combat.Monsters.First();
            PrototypeCity maps=player.GetComponent<PrototypeCity>();
            if(maps==null||!maps.IsBuilt||player.GetComponent<PrototypeOnlineClient>().IsOnlineMode)
                throw new BuildFailedException("A captura exige mapas prontos e sessão local descartável offline.");
            Transform parent=maps.MonsterRoot(CatacombsLayout.MapId);
            if(parent==null)throw new BuildFailedException("Cenário das Catacumbas não encontrado para captura.");
            maps.ApplyMap(CatacombsLayout.MapId);
            foreach(Renderer renderer in player.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            for(int i=0;i<CatacombsLayout.SpawnPoints.Length;i++)
            {
                CatacombsLayout.Point point=CatacombsLayout.SpawnPoints[i];
                bool caster=i==3||i==4||i==6;
                GameObject copy=Object.Instantiate(source.gameObject,parent);copy.name="Prévia descartável · "+(caster?"Conjurador":"Esqueleto");
                snapshots.Add(copy);copy.SetActive(true);
                copy.transform.SetPositionAndRotation(new Vector3(point.X,.04f,point.Z),Quaternion.Euler(0,i%2==0?145:215,0));
                PrototypeMonster monster=copy.GetComponent<PrototypeMonster>();monster.ConfigureStats(caster?"Conjurador da Cripta":"Esqueleto Guardião",900,40,100);
                foreach(Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))renderer.enabled=true;
                PrototypeWorldExpansion.ConfigureMonsterVisual(monster,new OnlineMonsterState {id="catacombs-preview-"+i,
                    speciesId=caster?"skeleton-caster":"skeleton-guardian",visualKind=caster?"skeleton-caster":"skeleton",
                    behavior=caster?"ranged":"melee",visualScale=1f});
                monster.enabled=false;
            }
            foreach(SkinnedMeshRenderer skin in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if(!skin.enabled||!skin.gameObject.activeInHierarchy||skin.sharedMesh==null)continue;
                Mesh mesh=new Mesh();skin.BakeMesh(mesh,true);meshes.Add(mesh);
                GameObject snapshot=new GameObject("Pose estática para inspeção");snapshots.Add(snapshot);snapshot.transform.SetParent(skin.transform,false);
                snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
            }
            Camera camera=Camera.main;PrototypeCamera follow=camera.GetComponent<PrototypeCamera>();if(follow!=null)follow.enabled=false;
            string folder=Environment.GetEnvironmentVariable("PROTOTYPE_CAPTURE_DIRECTORY");
            if(string.IsNullOrEmpty(folder))folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/previews/catacumbas"));
            Directory.CreateDirectory(folder);
            // The architectural overview disables distance fog; close views retain the actual
            // map lighting, portal and atmosphere selected by PrototypeCity.ApplyMap.
            bool fog=RenderSettings.fog;RenderSettings.fog=false;
            SavePreview(camera,new Vector3(0,57,1),new Vector3(0,0,27),58,Path.Combine(folder,"catacumbas-mapa.png"));
            RenderSettings.fog=fog;
            SavePreview(camera,new Vector3(-8,10,5),new Vector3(-12,1,17),58,Path.Combine(folder,"catacumbas-ossuario.png"));
            SavePreview(camera,new Vector3(5,10,21),new Vector3(12,1,33),58,Path.Combine(folder,"catacumbas-ritual.png"));
            SavePreview(camera,new Vector3(0,11,39),new Vector3(0,1,52),58,Path.Combine(folder,"catacumbas-cripta.png"));
            Debug.Log("[CATACOMBS PREVIEW PASS] Quatro capturas Unity com cenário, iluminação e espécies reais; fixtures não salvas. "+folder);
        }
        finally
        {
            foreach(GameObject snapshot in snapshots)if(snapshot!=null)Object.DestroyImmediate(snapshot);
            foreach(Mesh mesh in meshes)if(mesh!=null)Object.DestroyImmediate(mesh);
        }
    }

    private static void SavePreview(Camera camera,Vector3 position,Vector3 focus,float fieldOfView,string path)
    {
        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position));camera.fieldOfView=fieldOfView;
        RenderTexture oldActive=RenderTexture.active,oldTarget=camera.targetTexture;
        RenderTexture target=RenderTexture.GetTemporary(1500,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        Texture2D image=new Texture2D(1500,1000,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);}
    }

    private static void ValidateInScene(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        GameObject catacombs = roots.SingleOrDefault(root => root.name == CatacombsLayout.RootName);
        if (catacombs == null) throw new BuildFailedException("Root Catacumbas ausente ou duplicada.");
        if (catacombs.activeSelf) throw new BuildFailedException("Catacumbas deve ser salva inativa para preservar a entrada no Vale.");
        if (catacombs.GetComponentsInChildren<Light>(true).Length > 6)
            throw new BuildFailedException("Catacumbas excedeu o orçamento de seis luzes sem sombra.");
        foreach (Light light in catacombs.GetComponentsInChildren<Light>(true))
            if (light.shadows != LightShadows.None) throw new BuildFailedException("As luzes das Catacumbas devem permanecer sem sombra.");
        foreach (Shader shader in catacombs.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
            .Select(material => material.shader).Distinct())
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new BuildFailedException("Shader incompatível nas Catacumbas: " + (shader == null ? "ausente" : shader.name));
        if (catacombs.GetComponentsInChildren<Collider>(true).Length < 25)
            throw new BuildFailedException("Colisão física das Catacumbas incompleta.");
    }

    private static void RequireEditing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new BuildFailedException("Saia do modo Play antes de alterar as Catacumbas.");
    }

    private static void ConfigureMaterials()
    {
        floor = CopyMaterial("Lajes da cripta", "Assets/Materials/ThirdPartyEnvironment/PisoEscaneado.mat", new Color(.69f,.72f,.67f));
        wall = CopyMaterial("Alvenaria enterrada", "Assets/Materials/ThirdPartyEnvironment/MuroEscaneado.mat", new Color(.65f,.68f,.65f));
        stone = CopyMaterial("Pedra dos sarcófagos", "Assets/Materials/ThirdPartyEnvironment/RochaEscaneada.mat", new Color(.75f,.74f,.66f));
        iron = SolidMaterial("Ferro funerário", new Color(.19f,.22f,.22f), .5f, .23f);
        bone = SolidMaterial("Ossos envelhecidos", new Color(.64f,.59f,.44f), 0f, .12f);
        darkness = SolidMaterial("Abismo subterrâneo", new Color(.035f,.045f,.05f), 0f, 0f);
        amber = SolidMaterial("Chama âmbar", new Color(.91f,.39f,.08f), 0f, .1f);
        amber.EnableKeyword("_EMISSION"); amber.SetColor("_EmissionColor", new Color(1.9f,.58f,.08f));
        ritual = SolidMaterial("Runas do ritual", new Color(.20f,.48f,.60f), .18f, .25f);
        ritual.EnableKeyword("_EMISSION"); ritual.SetColor("_EmissionColor", new Color(.10f,.45f,.66f));
    }

    private static void BuildFloor(Transform parent)
    {
        var builder = new Geometry();
        for (int z = -7; z < 58; z++)
            for (int x = -19; x < 19; x++)
                if (CatacombsLayout.IsFloor(x + .5f, z + .5f))
                    builder.FloorQuad(new Vector3(x,0,z),new Vector3(x,0,z+1),new Vector3(x+1,0,z+1),new Vector3(x+1,0,z));
        MeshObject(parent, "Ground_Catacumbas", SaveMesh("Piso contínuo das Catacumbas", builder.Build()), floor, true, 8);
        Box(parent, "Fundo escuro sem colisão", new Vector3(0,-.65f,26), new Vector3(55,.2f,82), darkness, false);
        var path = new Geometry();
        // Recessed iron markers guide the player without replacing the scanned stone floor.
        for (int z = 5; z <= 46; z += 3)
            foreach (float x in new[] { -1.55f, 1.55f })
                path.Box(new Vector3(x,.012f,z),new Vector3(.075f,.018f,.65f),Quaternion.identity);
        MeshObject(parent, "Marcas de orientação da galeria", SaveMesh("Marcas da galeria", path.Build()), iron, false);
    }

    private static void BuildWalls(Transform parent)
    {
        var blocks = new Geometry();
        var capstones = new Geometry();
        for (int z = -7; z < 58; z++)
            for (int x = -19; x < 19; x++)
            {
                if (!CatacombsLayout.IsFloor(x + .5f,z + .5f)) continue;
                if (!CatacombsLayout.IsFloor(x-.5f,z+.5f)) WallSegment(parent,blocks,capstones,new Vector3(x,0,z+.5f),false);
                if (!CatacombsLayout.IsFloor(x+1.5f,z+.5f)) WallSegment(parent,blocks,capstones,new Vector3(x+1,0,z+.5f),false);
                if (!CatacombsLayout.IsFloor(x+.5f,z-.5f)) WallSegment(parent,blocks,capstones,new Vector3(x+.5f,0,z),true);
                if (!CatacombsLayout.IsFloor(x+.5f,z+1.5f)) WallSegment(parent,blocks,capstones,new Vector3(x+.5f,0,z+1),true);
            }
        MeshObject(parent,"Paredes de pedra talhada",SaveMesh("Alvenaria das Catacumbas",blocks.Build()),wall,false);
        MeshObject(parent,"Coroamento dos muros baixos",SaveMesh("Coroamento funerário",capstones.Build()),stone,false);
    }

    private static void WallSegment(Transform parent, Geometry blocks, Geometry capstones, Vector3 p, bool horizontal)
    {
        Vector3 size = horizontal ? new Vector3(1,.56f,.52f) : new Vector3(.52f,.56f,1);
        for (int row = 0; row < 3; row++)
            blocks.Box(p+Vector3.up*(.29f+row*.57f),size,Quaternion.identity);
        capstones.Box(p+Vector3.up*1.77f,horizontal ? new Vector3(1,.14f,.62f) : new Vector3(.62f,.14f,1),Quaternion.identity);
        // Taller invisible blockers prevent diagonal escape while the visible wall stays low
        // enough for a readable top-down fight. The exporter samples these actual colliders.
        GameObject collider = new GameObject("Limite de alvenaria");
        collider.transform.SetParent(parent,false); collider.transform.localPosition=p+Vector3.up*1.4f;
        collider.AddComponent<BoxCollider>().size = horizontal ? new Vector3(1,2.8f,.52f) : new Vector3(.52f,2.8f,1);
    }

    private static void BuildArch(Transform parent, Vector3 p, bool acrossZ, float width)
    {
        GameObject arch = new GameObject("Arco de passagem aberto"); arch.transform.SetParent(parent,false);
        arch.transform.localPosition=p; arch.transform.localRotation=Quaternion.Euler(0,acrossZ?90:0,0);
        for (int side=-1;side<=1;side+=2)
        {
            float x = side*(width*.5f+.12f);
            Box(arch.transform,"Base do pilar",new Vector3(x,.18f,0),new Vector3(.86f,.36f,.86f),stone,true);
            Box(arch.transform,"Pilar talhado",new Vector3(x,1.30f,0),new Vector3(.57f,2.25f,.65f),wall,true);
            Box(arch.transform,"Capitel",new Vector3(x,2.45f,0),new Vector3(.88f,.25f,.86f),stone,false);
        }
        var curve = new Geometry();
        for(int segment=0;segment<11;segment++)
        {
            float angle=(segment+.5f)*Mathf.PI/11;
            Vector3 center=new Vector3(Mathf.Cos(angle)*width*.5f,2.45f+Mathf.Sin(angle)*.92f,0);
            curve.Box(center,new Vector3(.64f,.38f,.65f),Quaternion.Euler(0,0,angle*Mathf.Rad2Deg-90));
        }
        MeshObject(arch.transform,"Clave e aduelas",SaveMesh("Arco "+(acrossZ?"transversal":"longitudinal"),curve.Build()),stone,false);
    }

    private static void BuildEntry(Transform parent)
    {
        // PrototypeCity owns the return portal and interaction, including its arch/ring.
        // Leave the centre empty so the same validated entry remains clear in both clients.
        Tomb(parent,new Vector3(-5.2f,0,-4.8f),90,false);
        Tomb(parent,new Vector3(5.2f,0,-4.8f),-90,false);
    }

    private static void BuildOssuary(Transform parent)
    {
        foreach(Vector3 p in new[] {new Vector3(-17,0,12.5f),new Vector3(-17,0,21.5f),new Vector3(-7.2f,0,12),new Vector3(-7.2f,0,22)})
            Tomb(parent,p,90,true);
        BonePile(parent,new Vector3(-17.4f,.12f,17));
        BonePile(parent,new Vector3(-10,.12f,22.5f));
        Box(parent,"Plinto do ossuário",new Vector3(-12,.2f,23),new Vector3(2,.4f,1),stone,true);
        Skull(parent,new Vector3(-12,.69f,23),1.4f);
    }

    private static void BuildRitual(Transform parent)
    {
        Ring(parent,"Círculo dos conjuradores",new Vector3(12,.025f,33),3.2f,.065f,ritual);
        Ring(parent,"Selo central",new Vector3(12,.027f,33),2.45f,.035f,iron);
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI*.25f;
            Vector3 p=new Vector3(12+Mathf.Cos(a)*2.8f,.045f,33+Mathf.Sin(a)*2.8f);
            GameObject rune=Box(parent,"Traço rúnico",p,new Vector3(.07f,.03f,.42f),ritual,false);
            rune.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg+45,0);
        }
        Box(parent,"Base do altar",new Vector3(17,.4f,33),new Vector3(1.45f,.8f,3),stone,true);
        Box(parent,"Mesa do altar",new Vector3(17,.89f,33),new Vector3(1.75f,.18f,3.3f),wall,true);
        Skull(parent,new Vector3(17,1.12f,32.2f),1.1f);
        BonePile(parent,new Vector3(17.4f,1f,33.5f));
        foreach(float z in new[]{27.2f,38.8f})
        {
            Box(parent,"Estela do ritual",new Vector3(17.4f,.9f,z),new Vector3(.65f,1.8f,.5f),stone,true);
            Box(parent,"Runa da estela",new Vector3(17.4f,1.2f,z-.26f),new Vector3(.12f,.55f,.03f),ritual,false);
        }
    }

    private static void BuildCrypt(Transform parent)
    {
        foreach(float x in new[]{-8.1f,8.1f})
            foreach(float z in new[]{47.3f,54.7f}) Tomb(parent,new Vector3(x,0,z),0,false);
        Box(parent,"Sarcófago ancestral",new Vector3(0,.5f,56.5f),new Vector3(3.5f,1,1.6f),wall,true);
        Box(parent,"Tampa ancestral",new Vector3(0,1.07f,56.5f),new Vector3(3.8f,.22f,1.85f),stone,true);
        Ring(parent,"Marca do guardião",new Vector3(0,.024f,51),3.8f,.045f,iron);
        Skull(parent,new Vector3(0,1.35f,56.5f),1.7f);
    }

    private static void Tomb(Transform parent,Vector3 p,float yaw,bool opened)
    {
        GameObject tomb=new GameObject(opened?"Túmulo violado":"Sarcófago selado");
        tomb.transform.SetParent(parent,false);tomb.transform.localPosition=p;tomb.transform.localRotation=Quaternion.Euler(0,yaw,0);
        Box(tomb.transform,"Base cinzelada",new Vector3(0,.13f,0),new Vector3(1.3f,.26f,2.6f),wall,true);
        Box(tomb.transform,"Caixa funerária",new Vector3(0,.48f,0),new Vector3(1.1f,.7f,2.35f),stone,true);
        GameObject lid=Box(tomb.transform,"Tampa de pedra",new Vector3(opened?.25f:0,.91f,0),new Vector3(1.27f,.22f,2.52f),wall,true);
        lid.transform.localRotation=Quaternion.Euler(0,0,opened?13:0);
        Box(tomb.transform,"Inscrição",new Vector3(opened?.3f:0,1.045f,0),new Vector3(.09f,.025f,.68f),iron,false);
        if(opened)Skull(tomb.transform,new Vector3(-.32f,1.08f,.65f),.9f);
    }

    private static void BonePile(Transform parent,Vector3 p)
    {
        GameObject pile=new GameObject("Ossos antigos");pile.transform.SetParent(parent,false);pile.transform.localPosition=p;
        for(int i=0;i<7;i++)
        {
            Vector3 center=new Vector3(Mathf.Sin(i*2.4f)*.42f,.025f+i*.012f,Mathf.Cos(i*3.1f)*.38f);
            GameObject piece=Primitive(pile.transform,"Fêmur",PrimitiveType.Capsule,center,new Vector3(.065f,.29f,.065f),bone);
            piece.transform.localRotation=Quaternion.Euler(87,i*59,13);
        }
        Skull(pile.transform,new Vector3(.05f,.12f,-.14f),.8f);
    }

    private static void Skull(Transform parent,Vector3 p,float scale)
    {
        GameObject skull=new GameObject("Crânio esculpido");skull.transform.SetParent(parent,false);skull.transform.localPosition=p;skull.transform.localScale=Vector3.one*scale;
        Primitive(skull.transform,"Calota",PrimitiveType.Sphere,new Vector3(0,.12f,0),new Vector3(.3f,.34f,.30f),bone);
        Box(skull.transform,"Mandíbula",new Vector3(0,-.015f,-.025f),new Vector3(.20f,.09f,.23f),bone,false);
        for(int side=-1;side<=1;side+=2)
            Primitive(skull.transform,"Órbita",PrimitiveType.Sphere,new Vector3(side*.071f,.105f,-.133f),new Vector3(.075f,.081f,.025f),darkness);
    }

    private static void Torch(Transform parent,Vector3 p,bool cold,bool light)
    {
        GameObject torch=new GameObject(cold?"Braseiro de chama azul":"Tocha de vigília");torch.transform.SetParent(parent,false);torch.transform.localPosition=p;
        Box(torch.transform,"Braço de ferro",new Vector3(0,-.2f,0),new Vector3(.10f,.72f,.12f),iron,false);
        Primitive(torch.transform,"Cálice",PrimitiveType.Cylinder,new Vector3(0,.16f,0),new Vector3(.3f,.12f,.3f),iron);
        Primitive(torch.transform,"Chama",PrimitiveType.Sphere,new Vector3(0,.45f,0),new Vector3(.19f,.53f,.19f),cold?ritual:amber);
        if(!light)return;
        Light lamp=torch.AddComponent<Light>();lamp.type=LightType.Point;lamp.range=10.5f;lamp.intensity=1.6f;
        lamp.color=cold?new Color(.36f,.66f,1f):new Color(1f,.62f,.31f);lamp.shadows=LightShadows.None;lamp.renderMode=LightRenderMode.Auto;
    }

    private static void Ring(Transform parent,string name,Vector3 p,float radius,float thickness,Material material)
    {
        var mesh=new Geometry();const int segments=72;
        for(int i=0;i<segments;i++)
        {
            float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;
            mesh.Quad(new Vector3(Mathf.Cos(a)*(radius-thickness),0,Mathf.Sin(a)*(radius-thickness)),
                new Vector3(Mathf.Cos(b)*(radius-thickness),0,Mathf.Sin(b)*(radius-thickness)),
                new Vector3(Mathf.Cos(b)*radius,0,Mathf.Sin(b)*radius),new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));
        }
        GameObject ring=MeshObject(parent,name,SaveMesh(name,mesh.Build()),material,false);ring.transform.localPosition=p;
    }

    private static GameObject Primitive(Transform parent,string name,PrimitiveType type,Vector3 p,Vector3 size,Material material)
    {
        GameObject value=GameObject.CreatePrimitive(type);value.name=name;value.transform.SetParent(parent,false);
        value.transform.localPosition=p;value.transform.localScale=size;Object.DestroyImmediate(value.GetComponent<Collider>());
        value.GetComponent<Renderer>().sharedMaterial=material;value.isStatic=true;return value;
    }

    private static GameObject Box(Transform parent,string name,Vector3 p,Vector3 size,Material material,bool collision)
    {
        GameObject value=GameObject.CreatePrimitive(PrimitiveType.Cube);value.name=name;value.transform.SetParent(parent,false);
        value.transform.localPosition=p;value.transform.localScale=size;value.GetComponent<Renderer>().sharedMaterial=material;
        if(!collision)Object.DestroyImmediate(value.GetComponent<Collider>());value.isStatic=true;return value;
    }

    private static Transform Group(Transform parent,string name)
    {GameObject value=new GameObject(name);value.transform.SetParent(parent,false);return value.transform;}

    private static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material,bool collision,int layer=0)
    {
        GameObject value=new GameObject(name);value.transform.SetParent(parent,false);value.layer=layer;value.isStatic=true;
        value.AddComponent<MeshFilter>().sharedMesh=mesh;value.AddComponent<MeshRenderer>().sharedMaterial=material;
        if(collision)value.AddComponent<MeshCollider>().sharedMesh=mesh;return value;
    }

    private static Material CopyMaterial(string name,string source,Color tint)
    {
        Material template=AssetDatabase.LoadAssetAtPath<Material>(source);
        if(template==null)throw new BuildFailedException("Material PBR existente ausente: "+source);
        Material material=AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot+"/"+name+".mat");
        if(material==null){material=new Material(template);AssetDatabase.CreateAsset(material,MaterialRoot+"/"+name+".mat");}
        else material.CopyPropertiesFromMaterial(template);
        material.name=name;material.color=tint;material.enableInstancing=true;
        if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.18f);
        EditorUtility.SetDirty(material);return material;
    }

    private static Material SolidMaterial(string name,Color color,float metallic,float gloss)
    {
        string path=MaterialRoot+"/"+name+".mat";Material value=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(value==null){value=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(value,path);}
        value.name=name;value.color=color;value.SetFloat("_Metallic",metallic);value.SetFloat("_Glossiness",gloss);value.enableInstancing=true;
        EditorUtility.SetDirty(value);return value;
    }

    private static Mesh SaveMesh(string name,Mesh value)
    {
        value.name=name;string path=AssetRoot+"/"+name+".asset";Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(value,path);return value;}
        EditorUtility.CopySerialized(value,existing);EditorUtility.SetDirty(existing);Object.DestroyImmediate(value);return existing;
    }

    private sealed class Geometry
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<int> triangles=new List<int>();
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            int start=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
            float width=Vector3.Distance(a,d)*.5f,height=Vector3.Distance(a,b)*.5f;
            uv.AddRange(new[]{Vector2.zero,new Vector2(0,height),new Vector2(width,height),new Vector2(width,0)});
            triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        public void FloorQuad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            Quad(a,b,c,d);int start=uv.Count-4;
            Vector3[] points={a,b,c,d};
            for(int i=0;i<points.Length;i++)uv[start+i]=new Vector2(points[i].x*.5f,points[i].z*.5f);
        }
        public void Box(Vector3 center,Vector3 size,Quaternion rotation)
        {
            Vector3 h=size*.5f;Vector3[] p={new Vector3(-h.x,-h.y,-h.z),new Vector3(h.x,-h.y,-h.z),
                new Vector3(h.x,h.y,-h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(-h.x,-h.y,h.z),
                new Vector3(h.x,-h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(-h.x,h.y,h.z)};
            for(int i=0;i<p.Length;i++)p[i]=center+rotation*p[i];
            Quad(p[0],p[3],p[2],p[1]);Quad(p[5],p[6],p[7],p[4]);Quad(p[4],p[7],p[3],p[0]);
            Quad(p[1],p[2],p[6],p[5]);Quad(p[3],p[7],p[6],p[2]);Quad(p[4],p[0],p[1],p[5]);
        }
        public Mesh Build()
        {
            Mesh mesh=new Mesh();if(vertices.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}

// Entering PlayMode lets PrototypeCombat create the actual runtime map presentation before
// capture. Run without -quit; this runner exits after rendering and never saves the fixture.
[InitializeOnLoad]
public static class CatacombsPreviewRunner
{
    private const string Key="GuerraDasNacoes.CatacombsPreview.";
    static CatacombsPreviewRunner(){EditorApplication.update+=Tick;}
    public static void Begin()
    {
        SessionState.SetBool(Key+"Active",false);SessionState.SetBool(Key+"Entered",false);
        SessionState.SetBool(Key+"Prepared",false);
        SessionState.SetFloat(Key+"Timeout",(float)EditorApplication.timeSinceStartup+60);
        SessionState.SetBool(Key+"Active",true);EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key+"Active",false))return;
        try
        {
            if(EditorApplication.timeSinceStartup>=SessionState.GetFloat(Key+"Timeout",0))
                throw new BuildFailedException("Tempo máximo excedido ao preparar captura das Catacumbas.");
            if(!EditorApplication.isPlaying)
            {
                if(SessionState.GetBool(Key+"Entered",false))throw new BuildFailedException("PlayMode encerrado antes das capturas.");
                return;
            }
            SessionState.SetBool(Key+"Entered",true);
            if(EditorApplication.isCompiling||Time.frameCount<8)return;
            if(!SessionState.GetBool(Key+"Prepared",false))
            {
                PrototypeCity maps=Object.FindFirstObjectByType<PrototypeCity>();
                if(maps==null||!maps.IsBuilt)throw new BuildFailedException("Apresentação dos mapas não foi inicializada.");
                maps.ApplyMap(CatacombsLayout.MapId);
                SessionState.SetBool(Key+"Prepared",true);
                SessionState.SetFloat(Key+"CaptureAt",Time.time+.6f);
                return;
            }
            // Standard's ambient probe settles after the map has rendered with its own light.
            if(Time.time<SessionState.GetFloat(Key+"CaptureAt",0))return;
            SessionState.SetBool(Key+"Active",false);CatacombsSetup.CaptureInPlayMode();EditorApplication.Exit(0);
        }
        catch(Exception error)
        {SessionState.SetBool(Key+"Active",false);Debug.LogError("[CATACOMBS PREVIEW FAIL] "+error);EditorApplication.Exit(1);}
    }
}
