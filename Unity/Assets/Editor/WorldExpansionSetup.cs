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

/// <summary>Incremental frontier builder. It replaces its own root only, never the saved courtyard,
/// equipment, spawn posts, player, city portal or character progress.</summary>
public static class WorldExpansionSetup
{
    private const string ScenePath = "Assets/Scenes/Patio.unity";
    private const string AssetRoot = "Assets/Art/WorldExpansion";
    private const string MaterialRoot = "Assets/Materials/WorldExpansion";
    private static System.Random random;
    private static Material earth, road, stone, bark, leaves, grass, iron, leather, bone, flag, ember;
    private static Mesh trunkMesh, leafMesh, grassMesh, plateMesh, bladeMesh, spikeMesh, bannerMesh;
    private static readonly Vector2[] EncounterPosts = {
        new Vector2(-24,25), new Vector2(-28,34), new Vector2(-18,33),
        new Vector2(25,30), new Vector2(29,40), new Vector2(0,61)
    };
    private static readonly Vector2[] MainPath = { new Vector2(0,16), new Vector2(0,22), new Vector2(-3,32),
        new Vector2(4,44), new Vector2(0,54), new Vector2(0,65) };
    private static readonly Vector2[] WestPath = { new Vector2(0,24), new Vector2(-12,24), new Vector2(-24,25), new Vector2(-28,34) };
    private static readonly Vector2[] EastPath = { new Vector2(-3,32), new Vector2(13,29), new Vector2(25,30), new Vector2(29,40) };

    [MenuItem("Guerra das Nações/Expandir fronteira sem recriar pátio")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new BuildFailedException("Saia do modo Play antes de expandir o mapa.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new BuildFailedException("Salve as cenas abertas antes de expandir o mapa.");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PrototypeCharacter hero = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PrototypeCharacter>(true)).FirstOrDefault();
        if (hero == null) throw new BuildFailedException("Personagem não encontrado na cena salva.");
        GameObject original = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Cenario");
        if (original == null) throw new BuildFailedException("Pátio original não encontrado.");
        var originalRootPoses = scene.GetRootGameObjects().Where(r => r.name != PrototypeWorldExpansion.RootName)
            .ToDictionary(r => r.GetInstanceID(), r => r.transform.localToWorldMatrix);
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == PrototypeWorldExpansion.RootName) Object.DestroyImmediate(root);
        // These decorative hills/trees previously filled the unplayable background. Their
        // meshes cross the new routes. Keep them as disabled, reversible source objects.
        foreach (Transform child in original.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith("Deco_Backdrop", StringComparison.Ordinal)) child.gameObject.SetActive(false);
        Directory.CreateDirectory(AssetRoot); Directory.CreateDirectory(MaterialRoot);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        random = new System.Random(18743);
        ConfigureAssets();
        GameObject frontier = new GameObject(PrototypeWorldExpansion.RootName);
        SceneManager.MoveGameObjectToScene(frontier, scene);
        Group(frontier.transform, "01 · solo escaneado e trilhas", out Transform ground);
        MeshObject(ground, "Ground_Vale100m", TerrainMesh(), earth, true, 8);
        MeshObject(ground, "Ground_TrilhaNorte", Ribbon(MainPath, 3.8f), road, false, 8);
        MeshObject(ground, "Ground_TrilhaAcampamento", Ribbon(WestPath, 3.3f), road, false, 8);
        MeshObject(ground, "Ground_TrilhaBosque", Ribbon(EastPath, 3.3f), road, false, 8);
        MeshObject(ground, "Ground_CírculoDeGrond", Disc(new Vector2(0,61), 10.7f, .016f, 64), road, false, 8);
        Group(frontier.transform, "02 · floresta e vegetação ao vento", out Transform forest);
        BuildForest(forest);
        Group(frontier.transform, "03 · afloramentos e limites do vale", out Transform rocks);
        BuildRocks(rocks);
        Group(frontier.transform, "04 · acampamento dos saqueadores", out Transform camp);
        BuildCamp(camp);
        Group(frontier.transform, "05 · arena do chefe", out Transform arena);
        BuildArena(arena);
        Group(frontier.transform, "06 · vida ambiente", out Transform life);
        BuildAtmosphere(life);
        PrototypeWorldExpansion palette = hero.GetComponent<PrototypeWorldExpansion>();
        if (palette == null) palette = hero.gameObject.AddComponent<PrototypeWorldExpansion>();
        palette.ConfigurePalette(iron, leather, bone, flag, ember, plateMesh, bladeMesh, spikeMesh, bannerMesh);
        EditorUtility.SetDirty(palette);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(.35f,.39f,.40f); RenderSettings.fogDensity = .0065f;
        AssetDatabase.SaveAssets();
        Physics.SyncTransforms();
        ValidateInScene(scene);
        foreach (var entry in originalRootPoses)
        {
            GameObject root = EditorUtility.InstanceIDToObject(entry.Key) as GameObject;
            if (root == null || root.transform.localToWorldMatrix != entry.Value)
                throw new BuildFailedException("A expansão alterou um objeto raiz original do pátio.");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new BuildFailedException("Não foi possível salvar a fronteira.");
        Debug.Log("[WORLD EXPANSION PASS] Vale 100×100m; 6 encontros desimpedidos; pátio, personagens e portal preservados.");
    }

    public static void Validate()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ValidateInScene(scene);
        Debug.Log("[WORLD EXPANSION VALIDATE PASS] Terreno, corredores, arenas, vegetação e limites consistentes.");
    }

    private static void ValidateInScene(Scene scene)
    {
        GameObject frontier = scene.GetRootGameObjects().FirstOrDefault(r => r.name == PrototypeWorldExpansion.RootName);
        if (frontier == null) throw new BuildFailedException("Fronteira expandida ausente.");
        if (scene.GetRootGameObjects().Count(r => r.name == PrototypeWorldExpansion.RootName) != 1)
            throw new BuildFailedException("Expansão duplicada.");
        Physics.SyncTransforms();
        const int obstacleMask = ~((1 << 8) | (1 << 9) | (1 << 10));
        var probes = new List<Vector2> { Vector2.zero, new Vector2(0,-3), new Vector2(0,16), new Vector2(0,20) };
        probes.AddRange(EncounterPosts);
        foreach (Vector2[] path in new[] { MainPath, WestPath, EastPath })
            for (int segment = 1; segment < path.Length; segment++)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(path[segment-1], path[segment]));
                for (int k = 0; k <= steps; k++) probes.Add(Vector2.Lerp(path[segment-1], path[segment], k/(float)steps));
            }
        foreach (Vector2 post in probes)
        {
            Vector3 foot = new Vector3(post.x, 0f, post.y);
            if (!Physics.Raycast(foot + Vector3.up * 2f, Vector3.down, 4f, 1 << 8, QueryTriggerInteraction.Ignore))
                throw new BuildFailedException("Rota sem solo: " + post);
            if (Physics.CheckCapsule(foot + Vector3.up*.62f, foot + Vector3.up*1.55f, .31f, obstacleMask, QueryTriggerInteraction.Ignore))
                throw new BuildFailedException("Rota ou encontro bloqueado: " + post);
        }
        MeshFilter terrain = frontier.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m => m.name == "Ground_Vale100m");
        if (terrain == null || terrain.sharedMesh.bounds.size.x < 99f || terrain.sharedMesh.bounds.size.z < 99f)
            throw new BuildFailedException("Terreno precisa cobrir os 100×100m.");
        if (frontier.GetComponentsInChildren<LODGroup>().Length < 40)
            throw new BuildFailedException("Floresta sem LODs de distância.");
        if (frontier.GetComponentsInChildren<Light>().Length > 2)
            throw new BuildFailedException("A expansão excedeu o orçamento de luzes.");
        // CPU compilation cannot detect a failed Metal surface shader. Force compilation of
        // every material used in these views and refuse a pink fallback before declaring PASS.
        foreach (Shader shader in frontier.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
            .Select(material => material.shader).Where(shader => shader != null).Distinct())
        {
            if (!shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new BuildFailedException("Shader da fronteira com erro ou incompatível: " + shader.name);
        }
    }

    private static void ConfigureAssets()
    {
        earth = Require<Material>("Assets/Materials/ThirdPartyEnvironment/TerraEscaneada.mat");
        stone = Require<Material>("Assets/Materials/ThirdPartyEnvironment/RochaEscaneada.mat");
        road = CopyMaterial("Trilha de terra pisada", earth, new Color(.88f,.83f,.69f));
        road.SetFloat("_BumpScale", .5f);
        bark = CopyMaterial("Casca de carvalho", Require<Material>("Assets/Materials/MAT_Tronco.mat"), new Color(.55f,.48f,.37f));
        bark.enableInstancing = true;
        leaves = Foliage("Folhas de carvalho", new Color(.17f,.235f,.105f), .075f, 1f);
        grass = Foliage("Gramíneas do vale", new Color(.25f,.29f,.13f), .10f, 0f);
        iron = CopyMaterial("Ferro orc envelhecido", Require<Material>("Assets/Materials/MAT_MetalEstandarte.mat"), new Color(.31f,.30f,.27f));
        iron.SetFloat("_Metallic", .65f); iron.SetFloat("_Glossiness", .23f);
        leather = CopyMaterial("Couro dos saqueadores", Require<Material>("Assets/Materials/MAT_Couro.mat"), new Color(.28f,.19f,.12f));
        bone = CopyMaterial("Presas de osso", Require<Material>("Assets/Materials/MAT_PedraClara.mat"), new Color(.69f,.65f,.48f));
        flag = CopyMaterial("Estandarte do chefe", Require<Material>("Assets/Materials/MAT_NacaoVermelha.mat"), new Color(.40f,.11f,.07f));
        ember = MaterialAsset("Brasa ambiente", Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default"));
        ember.SetColor("_Color", Color.white);
        ember.SetTexture("_MainTex", RadialTexture());
        if (ember.HasProperty("_Mode")) { ember.SetFloat("_Mode", 2); ember.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); ember.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); ember.SetInt("_ZWrite",0); ember.renderQueue=3000; }
        trunkMesh = SaveMesh("CarvalhoTronco", TreeTrunk());
        leafMesh = SaveMesh("CarvalhoCopa", TreeLeaves());
        grassMesh = SaveMesh("TufoGramíneas", GrassTuft());
        plateMesh = SaveMesh("PlacaOrcChanfrada", ChamferedPlate());
        bladeMesh = SaveMesh("MachadoOrc", AxeBlade());
        spikeMesh = SaveMesh("PresaOrc", Spike());
        bannerMesh = SaveMesh("EstandarteRasgado", Banner());
    }

    private static void BuildForest(Transform forest)
    {
        int count = 0;
        for (int z = -12; z <= 73; z += 7)
            for (int x = -43; x <= 43; x += 7)
            {
                Vector2 point = new Vector2(x + Range(-2.1f,2.1f), z + Range(-2.1f,2.1f));
                if (Protected(point, 3.2f) || (point.y > 48 && Mathf.Abs(point.x) < 16)) continue;
                // Open glades around the encounters, denser forest around the perimeter.
                if (random.NextDouble() < .25 && Mathf.Abs(point.x) < 35) continue;
                GameObject tree = new GameObject("Carvalho " + (++count).ToString("000"));
                tree.transform.SetParent(forest,false); tree.transform.position = new Vector3(point.x,-.045f,point.y);
                float size = Range(.85f,1.36f); tree.transform.localScale = Vector3.one * size;
                tree.transform.localRotation = Quaternion.Euler(0,Range(0,360),0);
                MeshRenderer trunk = MeshObject(tree.transform,"Tronco com raízes",trunkMesh,bark,false).GetComponent<MeshRenderer>();
                MeshRenderer crown = MeshObject(tree.transform,"Copa ao vento",leafMesh,leaves,false).GetComponent<MeshRenderer>();
                CapsuleCollider collider = tree.AddComponent<CapsuleCollider>(); collider.radius=.29f; collider.height=3.4f; collider.center=Vector3.up*1.7f;
                LODGroup lod = tree.AddComponent<LODGroup>(); lod.SetLODs(new[] {new LOD(.025f,new Renderer[]{trunk,crown})});
                lod.fadeMode=LODFadeMode.None; lod.RecalculateBounds();
            }
        // Batches keep vegetation draw calls bounded; every patch fades out at distance.
        for (int patch = 0; patch < 28; patch++)
        {
            Vector2 center = new Vector2(Range(-43,43),Range(18,73));
            var combine = new List<CombineInstance>();
            for (int tuft = 0; tuft < 26; tuft++)
            {
                Vector2 p = center + new Vector2(Range(-4,4),Range(-4,4));
                if (Protected(p,2f)) continue;
                combine.Add(new CombineInstance {mesh=grassMesh, transform=Matrix4x4.TRS(new Vector3(p.x,-.025f,p.y),
                    Quaternion.Euler(0,Range(0,360),0), Vector3.one*Range(.7f,1.3f))});
            }
            if (combine.Count == 0) continue;
            Mesh combined = new Mesh {name="Mancha de vegetação " + patch}; combined.CombineMeshes(combine.ToArray());
            combined = SaveMesh("GramaLote"+patch.ToString("00"),combined);
            GameObject batch = MeshObject(forest,combined.name,combined,grass,false);
            var renderer = batch.GetComponent<MeshRenderer>(); renderer.shadowCastingMode=ShadowCastingMode.Off;
            LODGroup lod=batch.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.045f,new Renderer[]{renderer})});lod.RecalculateBounds();
        }
    }

    private static void BuildRocks(Transform group)
    {
        var positions = new List<Vector3>();
        for (int z=-13;z<=74;z+=9) {positions.Add(new Vector3(-47,0,z));positions.Add(new Vector3(47,0,z));}
        for (int x=-40;x<=40;x+=9) positions.Add(new Vector3(x,0,77));
        positions.AddRange(new[]{new Vector3(-39,0,35),new Vector3(-37,0,52),new Vector3(37,0,53),new Vector3(20,0,64),new Vector3(-22,0,61)});
        for(int i=0;i<positions.Count;i++)
        {
            GameObject model=Require<GameObject>("Assets/Art/ThirdParty/Environment/Rocks/Rock"+(i%6+1).ToString("00")+".fbx");
            GameObject rock=(GameObject)PrefabUtility.InstantiatePrefab(model,group.gameObject.scene);rock.name="Afloramento escaneado "+i.ToString("00");
            rock.transform.SetParent(group,false);rock.transform.position=positions[i]+Vector3.down*.12f;
            float size= i<30?Range(2.7f,4.2f):Range(1.2f,2.2f);
            rock.transform.localScale=new Vector3(size,Range(1.2f,2.1f),size);rock.transform.localRotation=Quaternion.Euler(0,Range(0,360),0);
            foreach(Transform child in rock.GetComponentsInChildren<Transform>(true))child.gameObject.SetActive(true);
            foreach(Renderer renderer in rock.GetComponentsInChildren<Renderer>(true))
            {renderer.enabled=true;renderer.sharedMaterial=stone;renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;}
            foreach(MeshFilter filter in rock.GetComponentsInChildren<MeshFilter>())
            {
                filter.gameObject.isStatic=true;
                MeshCollider collider=filter.GetComponent<MeshCollider>();if(collider==null)collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;
            }
        }
        // Explicit boundaries agree with server grid extents. Dressing hides the thin blockers.
        Box(group,"Limite oeste",new Vector3(-49.7f,1.5f,30),new Vector3(.6f,3,100),stone,true).GetComponent<Renderer>().enabled=false;
        Box(group,"Limite leste",new Vector3(49.7f,1.5f,30),new Vector3(.6f,3,100),stone,true).GetComponent<Renderer>().enabled=false;
        Box(group,"Limite norte",new Vector3(0,1.5f,79.7f),new Vector3(100,3,.6f),stone,true).GetComponent<Renderer>().enabled=false;
        Box(group,"Limite sul",new Vector3(0,1.5f,-19.7f),new Vector3(100,3,.6f),stone,true).GetComponent<Renderer>().enabled=false;
        BuildBackdrop(group);
    }

    private static void BuildBackdrop(Transform group)
    {
        // Entirely decorative terrain outside x +/-50, z -20..80. The collider export and
        // walkable ground are unchanged; the skirt closes the abrupt sky/floor horizon.
        GameObject backdrop=new GameObject("Horizonte visual sem colisores");backdrop.transform.SetParent(group,false);
        var xs=new List<float>();for(int x=-95;x<=95;x+=5)xs.Add(x);
        var zs=new List<float>();for(int z=-50;z<=125;z+=5)zs.Add(z);
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        foreach(float z in zs)foreach(float x in xs)
        {vertices.Add(new Vector3(x,BackdropHeight(x,z),z));uv.Add(new Vector2(x,z));}
        for(int z=0;z<zs.Count-1;z++)for(int x=0;x<xs.Count-1;x++)
        {
            float cx=(xs[x]+xs[x+1])*.5f,cz=(zs[z]+zs[z+1])*.5f;
            if(Mathf.Abs(cx)<50&&cz>-20&&cz<80)continue;
            int a=z*xs.Count+x;triangles.AddRange(new[]{a,a+xs.Count,a+1,a+1,a+xs.Count,a+xs.Count+1});
        }
        Mesh skirt=SaveMesh("HorizonteValeDecorativo",CreateMesh("HorizonteValeDecorativo",vertices,uv,triangles));
        MeshObject(backdrop.transform,"Encostas do vale · apenas visual",skirt,earth,false);
        var points=new List<Vector2>();
        for(int x=-44;x<=44;x+=12)points.Add(new Vector2(x,91+Mathf.Sin(x*.2f)*3));
        for(int z=1;z<=67;z+=22){points.Add(new Vector2(-61,z));points.Add(new Vector2(61,z));}
        for(int i=0;i<points.Count;i++)
        {
            // Fit the imported scan to measured bounds instead of relying on FBX file units.
            // Both available source meshes and their shared PBR material are reused.
            string path="Assets/Art/ThirdParty/Environment/Rocks/Rock"+(i%2==0?"03":"06")+".fbx";
            GameObject holder=new GameObject("Rocha distante · apenas visual "+i.ToString("00"));holder.transform.SetParent(backdrop.transform,false);
            GameObject rock=(GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>(path),group.gameObject.scene);
            // Keep the imported FBX axis/unit transform on the child. Only this plain wrapper
            // receives world-aligned dimensions and yaw; overwriting the FBX root rotated the
            // flat scans into upright slabs in the previous synchronous preview.
            rock.transform.SetParent(holder.transform,false);
            foreach(Transform child in rock.GetComponentsInChildren<Transform>(true))child.gameObject.SetActive(true);
            foreach(Collider collider in rock.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
            Renderer[] renderers=rock.GetComponentsInChildren<Renderer>(true);
            foreach(Renderer renderer in renderers){renderer.enabled=true;renderer.sharedMaterial=stone;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;}
            if(renderers.Length==0)throw new BuildFailedException("Rocha distante sem Renderer: "+path);
            Bounds bounds=WorldMeshBounds(holder);
            if(bounds.size.y<.00001f)throw new BuildFailedException("Rocha distante sem altura: "+path);
            Vector3 desired=new Vector3(10+(i%3),4.5f+(i%3)*.5f,9+(i%3));
            holder.transform.localScale=new Vector3(desired.x/Mathf.Max(.00001f,bounds.size.x),desired.y/bounds.size.y,desired.z/Mathf.Max(.00001f,bounds.size.z));
            holder.transform.localRotation=Quaternion.Euler(0,i*137,0);
            bounds=WorldMeshBounds(holder);
            Vector2 point=points[i];holder.transform.position+=new Vector3(point.x-bounds.center.x,0,point.y-bounds.center.z);
            bounds=WorldMeshBounds(holder);
            float ground=BackdropHeight(point.x,point.y);
            foreach(Vector2 corner in new[]{new Vector2(bounds.min.x,bounds.min.z),new Vector2(bounds.max.x,bounds.min.z),new Vector2(bounds.min.x,bounds.max.z),new Vector2(bounds.max.x,bounds.max.z)})
                ground=Mathf.Min(ground,BackdropHeight(corner.x,corner.y));
            holder.transform.position+=Vector3.up*(ground-1.5f-bounds.min.y);
            bounds=WorldMeshBounds(holder);
            if(bounds.min.y>ground-1.49f||bounds.size.y<4.4f||bounds.size.y>5.6f||bounds.max.y>13f||bounds.size.y>Mathf.Min(bounds.size.x,bounds.size.z)*.8f)
                throw new BuildFailedException("Rocha distante mal assentada ou vertical: "+holder.name+" "+bounds);
            LODGroup lod=holder.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.025f,renderers)});lod.RecalculateBounds();
        }
    }

    private static Bounds WorldMeshBounds(GameObject root)
    {
        // Renderer.bounds may retain last frame's transform while running an editor script.
        // Mesh AABB corners transformed by current matrices are deterministic immediately.
        bool found=false;Bounds bounds=new Bounds();
        foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if(filter.sharedMesh==null)continue;Bounds local=filter.sharedMesh.bounds;
            for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
            {
                Vector3 point=filter.transform.TransformPoint(new Vector3(x==0?local.min.x:local.max.x,y==0?local.min.y:local.max.y,z==0?local.min.z:local.max.z));
                if(!found){bounds=new Bounds(point,Vector3.zero);found=true;}else bounds.Encapsulate(point);
            }
        }
        if(!found)throw new BuildFailedException("Rocha sem geometria de origem: "+root.name);
        return bounds;
    }

    private static float BackdropHeight(float x,float z)
    {
        float outside=Mathf.Max(Mathf.Abs(x)-50,Mathf.Max(z-80,-20-z));
        if(outside<=0)return -.035f;
        float rise=Mathf.SmoothStep(0,1,Mathf.Clamp01(outside/14));
        return -.035f+rise*(5.5f+2.2f*Mathf.Sin(x*.10f+z*.13f)+1.5f*Mathf.Cos(x*.19f-z*.08f));
    }

    private static void BuildCamp(Transform group)
    {
        Tent(group,new Vector3(-32,0,24),23f);
        Tent(group,new Vector3(-31,0,39),143f);
        Tent(group,new Vector3(-17,0,40),-35f);
        for(int i=0;i<12;i++)
        {
            float x=-36.5f+i*.8f;
            GameObject post=MeshObject(group,"Paliçada de troncos",trunkMesh,bark,false);
            post.transform.position=new Vector3(x,0,43);post.transform.localScale=new Vector3(.5f,.35f,.5f);
            var collider=post.AddComponent<CapsuleCollider>();collider.radius=.28f;collider.height=5f;collider.center=Vector3.up*2.5f;
        }
        Campfire(group,new Vector3(-25,0,38));
        for(int i=0;i<4;i++)
        {
            Vector3 pos=new Vector3(-35+(i%2)*1.2f,.5f,31+(i/2)*1.2f);
            Box(group,"Caixote de suprimentos",pos,new Vector3(.9f,1,.8f),leather,true);
            for(int band=0;band<2;band++)Box(group,"Cinta de ferro",pos+new Vector3(0,0,band==0?-.27f:.27f),new Vector3(.94f,1.04f,.07f),iron,false);
        }
        Sign(group,new Vector3(-9,0,23),"Acampamento dos Orcs",-60);
        Sign(group,new Vector3(10,0,29),"Bosque dos Trolls",45);
        Sign(group,new Vector3(4.5f,0,48),"Arena de Grond",0);
    }

    private static void BuildArena(Transform group)
    {
        for(int i=0;i<24;i++)
        {
            float angle=i*Mathf.PI/12f;
            if(Mathf.Cos(angle)<-.75f)continue; // broad south entrance along the main route
            Vector3 point=new Vector3(Mathf.Sin(angle)*12.2f,.28f,61+Mathf.Cos(angle)*12.2f);
            GameObject slab=Box(group,"Menir da arena",point,new Vector3(.8f,Range(.48f,1.2f),.55f),stone,true);
            slab.transform.rotation=Quaternion.Euler(Range(-5,5),angle*Mathf.Rad2Deg,Range(-7,7));
        }
        foreach(int side in new[]{-1,1})
        {
            Vector3 pos=new Vector3(side*9.5f,0,66);
            Box(group,"Coluna da arena",pos+Vector3.up*2.3f,new Vector3(.75f,4.6f,.75f),stone,true);
            GameObject flagObject=MeshObject(group,"Estandarte da guerra",bannerMesh,flag,false);
            flagObject.transform.position=pos+new Vector3(.65f,3.6f,0);flagObject.transform.localScale=new Vector3(1.6f,1.8f,1);
            for(int tooth=0;tooth<3;tooth++)
            {
                GameObject spike=MeshObject(group,"Presa ritual",spikeMesh,bone,false);
                spike.transform.position=pos+new Vector3((tooth-1)*.23f,4.6f,0);spike.transform.localScale=new Vector3(.2f,.6f,.2f);
            }
        }
    }

    private static void BuildAtmosphere(Transform group)
    {
        GameObject motes=new GameObject("Sementes e poeira levadas pelo vento");motes.transform.SetParent(group,false);motes.transform.position=new Vector3(0,2.2f,43);
        ParticleSystem particles=motes.AddComponent<ParticleSystem>();var main=particles.main;main.duration=10;main.loop=true;
        main.startLifetime=new ParticleSystem.MinMaxCurve(6,12);main.startSpeed=.09f;main.startSize=new ParticleSystem.MinMaxCurve(.025f,.055f);
        main.startColor=new Color(.67f,.66f,.45f,.22f);main.maxParticles=95;main.simulationSpace=ParticleSystemSimulationSpace.World;
        var emission=particles.emission;emission.rateOverTime=7;
        var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(65,3.6f,52);
        var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.x=.16f;velocity.y=.02f;velocity.z=.06f;
        var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=ember;renderer.shadowCastingMode=ShadowCastingMode.Off;
    }

    private static void Tent(Transform group,Vector3 position,float yaw)
    {
        GameObject tent=new GameObject("Tenda de couro dos Orcs");tent.transform.SetParent(group,false);tent.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        Mesh canvas=new Mesh{name="Tenda triangular"};canvas.vertices=new[]{new Vector3(-2.3f,0,-2),new Vector3(2.3f,0,-2),new Vector3(0,3.2f,-2),new Vector3(-2.3f,0,2),new Vector3(2.3f,0,2),new Vector3(0,3.2f,2)};
        canvas.triangles=new[]{0,3,5,0,5,2,2,5,4,2,4,1,3,4,5};canvas.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,1),new Vector2(0,1),new Vector2(1,1),new Vector2(.5f,0)};canvas.RecalculateNormals();canvas.RecalculateBounds();
        // One reusable mesh for all tents, persisted so the saved scene never refers to a transient Mesh.
        canvas=SaveMesh("TendaOrc",canvas);MeshObject(tent.transform,"Lona de couro curtido",canvas,leather,false);
        Box(tent.transform,"Área ocupada pela tenda",new Vector3(0,1.15f,.2f),new Vector3(3.8f,2.3f,3.5f),null,true).GetComponent<Renderer>().enabled=false;
        foreach(int side in new[]{-1,1})
        {GameObject post=Box(tent.transform,"Mastro de madeira",new Vector3(0,1.55f,side*2.05f),new Vector3(.13f,3.1f,.13f),bark,false);
        Box(tent.transform,"Trave",new Vector3(0,3.1f,0),new Vector3(.15f,.15f,4.4f),bark,false);}
    }

    private static void Campfire(Transform group,Vector3 position)
    {
        GameObject fire=new GameObject("Fogueira com brasas");fire.transform.SetParent(group,false);fire.transform.position=position;
        for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Box(fire.transform,"Pedra da fogueira",new Vector3(Mathf.Sin(a)*.62f,.14f,Mathf.Cos(a)*.62f),new Vector3(.35f,.27f,.31f),stone,false);}
        for(int i=0;i<3;i++){GameObject log=Box(fire.transform,"Lenha queimada",new Vector3(0,.14f,0),new Vector3(.18f,.18f,1.1f),bark,false);log.transform.localRotation=Quaternion.Euler(0,i*60,0);}
        GameObject flame=new GameObject("Fumaça e brasa quente");flame.transform.SetParent(fire.transform,false);flame.transform.localPosition=Vector3.up*.2f;
        ParticleSystem particles=flame.AddComponent<ParticleSystem>();var main=particles.main;main.duration=3;main.loop=true;main.startLifetime=1.4f;
        main.startSpeed=.75f;main.startSize=new ParticleSystem.MinMaxCurve(.12f,.28f);main.startColor=new Color(.95f,.38f,.08f,.6f);main.maxParticles=38;
        var emission=particles.emission;emission.rateOverTime=22;var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=13;shape.radius=.3f;shape.rotation=new Vector3(-90,0,0);
        var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=ember;renderer.shadowCastingMode=ShadowCastingMode.Off;
        Light light=flame.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.48f,.18f);light.intensity=.7f;light.range=5;light.shadows=LightShadows.None;
    }

    private static void Sign(Transform group,Vector3 position,string text,float yaw)
    {
        GameObject sign=new GameObject("Placa · "+text);sign.transform.SetParent(group,false);sign.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        Box(sign.transform,"Poste",new Vector3(0,.9f,0),new Vector3(.16f,1.8f,.16f),bark,false);
        Box(sign.transform,"Madeira da placa",new Vector3(0,1.65f,0),new Vector3(2.7f,.6f,.13f),bark,false);
        GameObject title=new GameObject("Inscrição");title.transform.SetParent(sign.transform,false);title.transform.localPosition=new Vector3(0,1.65f,-.075f);title.transform.localRotation=Quaternion.Euler(0,180,0);
        TextMesh label=title.AddComponent<TextMesh>();label.text=text;label.fontSize=48;label.characterSize=.032f;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(.78f,.73f,.57f);
    }

    private static bool Protected(Vector2 point,float margin)
    {
        if(Mathf.Abs(point.x)<18+margin&&point.y<18+margin&&point.y>-18-margin)return true;
        if(EncounterPosts.Any(p=>Vector2.Distance(p,point)<5.1f+margin))return true;
        if(Vector2.Distance(point,new Vector2(0,61))<13.3f+margin)return true;
        foreach(Vector2[] path in new[]{MainPath,WestPath,EastPath})
            for(int i=1;i<path.Length;i++)if(SegmentDistance(point,path[i-1],path[i])<2.6f+margin)return true;
        return false;
    }

    private static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
    {Vector2 d=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);return Vector2.Distance(p,a+d*t);}
    private static float Range(float minimum,float maximum)=>minimum+(float)random.NextDouble()*(maximum-minimum);
    private static void Group(Transform parent,string name,out Transform child){GameObject go=new GameObject(name);go.transform.SetParent(parent,false);child=go.transform;}

    private static Mesh TerrainMesh()
    {
        var xs=new List<float>{-50,-48,-44,-40,-36,-32,-28,-24,-20,-18,-17.5f,17.5f,18,20,24,28,32,36,40,44,48,50};
        var zs=new List<float>{-20,-18,-17.5f,17.5f,18};for(int z=20;z<=80;z+=4)zs.Add(z);
        var vertices=new List<Vector3>();var uvs=new List<Vector2>();var indices=new List<int>();
        for(int z=0;z<zs.Count;z++)for(int x=0;x<xs.Count;x++)
        {float y=-.035f;vertices.Add(new Vector3(xs[x],y,zs[z]));uvs.Add(new Vector2(xs[x],zs[z]));}
        for(int z=0;z<zs.Count-1;z++)for(int x=0;x<xs.Count-1;x++)
        {
            float mx=(xs[x]+xs[x+1])*.5f,mz=(zs[z]+zs[z+1])*.5f;
            if(Mathf.Abs(mx)<17.5f&&Mathf.Abs(mz)<17.5f)continue;
            int a=z*xs.Count+x;indices.AddRange(new[]{a,a+xs.Count,a+1,a+1,a+xs.Count,a+xs.Count+1});
        }
        return SaveMesh("SoloDoVale100m",CreateMesh("Solo do vale",vertices,uvs,indices));
    }

    private static Mesh Ribbon(Vector2[] points,float width)
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
        for(int i=0;i<points.Length;i++)
        {Vector2 tangent=(i==points.Length-1?points[i]-points[i-1]:points[i+1]-points[Mathf.Max(0,i-1)]).normalized;
        Vector2 side=new Vector2(tangent.y,-tangent.x)*width*.5f;
        foreach(Vector2 p in new[]{points[i]-side,points[i]+side}){vertices.Add(new Vector3(p.x,.008f,p.y));uv.Add(p);}
        if(i>0){int a=(i-1)*2;indices.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}}
        return SaveMesh("Trilha"+Mathf.Abs(points[points.Length-1].x).ToString("00"),CreateMesh("Trilha pisada",vertices,uv,indices));
    }

    private static Mesh Disc(Vector2 center,float radius,float y,int sides)
    {
        var vertices=new List<Vector3>{new Vector3(center.x,y,center.y)};var uv=new List<Vector2>{center};var indices=new List<int>();
        for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;Vector2 p=center+new Vector2(Mathf.Sin(a),Mathf.Cos(a))*radius;vertices.Add(new Vector3(p.x,y,p.y));uv.Add(p);}
        for(int i=0;i<sides;i++)indices.AddRange(new[]{0,i+1,(i+1)%sides+1});
        return SaveMesh("ArenaCircular",CreateMesh("Arena de terra",vertices,uv,indices));
    }

    private static Mesh TreeTrunk()
    {
        var b=new MeshBuilder();b.Branch(new Vector3(0,0,0),new Vector3(.12f,5.6f,.08f),.38f,.09f,10,7);
        for(int i=0;i<10;i++)
        {float a=i*2.399963f;float y=1.9f+i*.29f;Vector3 tip=new Vector3(Mathf.Cos(a)*Range(1.4f,2.3f),y+Range(1.2f,2.1f),Mathf.Sin(a)*Range(1.4f,2.3f));
        b.Branch(new Vector3(.05f,y,0),tip,.13f,.025f,6,3);}
        for(int i=0;i<6;i++){float a=i*Mathf.PI/3;b.Branch(new Vector3(0,.3f,0),new Vector3(Mathf.Cos(a)*.85f,.025f,Mathf.Sin(a)*.85f),.23f,.02f,6,2);}
        return b.Build("Tronco retorcido");
    }

    private static Mesh TreeLeaves()
    {
        var b=new MeshBuilder();
        for(int cluster=0;cluster<15;cluster++)
        {
            float a=cluster*2.399963f;float radius=cluster<11?Range(1.15f,2.05f):Range(.25f,.9f);
            Vector3 center=new Vector3(Mathf.Cos(a)*radius,cluster<11?Range(3.7f,5.8f):Range(5.7f,6.5f),Mathf.Sin(a)*radius);
            for(int leaf=0;leaf<23;leaf++)
            {Vector3 p=center+new Vector3(Range(-.75f,.75f),Range(-.55f,.55f),Range(-.75f,.75f));
            b.Leaf(p,Quaternion.Euler(Range(-65,65),Range(0,360),Range(-55,55)),Range(.28f,.48f),Range(.5f,.8f));}
        }
        return b.Build("Feuillage détaillé");
    }

    private static Mesh GrassTuft()
    {
        var b=new MeshBuilder();for(int i=0;i<14;i++)
        b.Leaf(new Vector3(Range(-.2f,.2f),0,Range(-.2f,.2f)),Quaternion.Euler(Range(-18,18),Range(0,360),Range(-25,25)),Range(.045f,.10f),Range(.3f,.64f));
        return b.Build("Tufo de gramínea");
    }

    private static Mesh ChamferedPlate()
    {
        // Eight-sided outline gives shoulder plates and accessories a visible rolled edge.
        Vector2[] outline={new Vector2(-.38f,-.5f),new Vector2(.38f,-.5f),new Vector2(.5f,-.36f),new Vector2(.5f,.36f),new Vector2(.38f,.5f),new Vector2(-.38f,.5f),new Vector2(-.5f,.36f),new Vector2(-.5f,-.36f)};
        var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
        for(int ring=0;ring<4;ring++)foreach(Vector2 p in outline){float scale=ring==1||ring==2?1f:.88f;v.Add(new Vector3(p.x*scale,p.y*scale,ring==0?-.5f:ring==1?-.36f:ring==2?.36f:.5f));uv.Add(p+Vector2.one*.5f);}
        for(int ring=0;ring<3;ring++)for(int i=0;i<8;i++){int a=ring*8+i,n=ring*8+(i+1)%8;t.AddRange(new[]{a,n,n+8,a,n+8,a+8});}
        for(int i=1;i<7;i++){t.AddRange(new[]{0,i+1,i,24,24+i,24+i+1});}
        return CreateMesh("Placa chanfrada",v,uv,t);
    }

    private static Mesh AxeBlade()
    {
        Vector2[] outline={new Vector2(-.5f,-.45f),new Vector2(-.28f,-.26f),new Vector2(.0f,-.16f),new Vector2(.5f,-.40f),new Vector2(.40f,0),new Vector2(.5f,.42f),new Vector2(0,.18f),new Vector2(-.28f,.30f),new Vector2(-.5f,.46f),new Vector2(-.40f,0)};
        return Extrude("Machado assimétrico",outline,.5f);
    }
    private static Mesh Spike()
    {
        var b=new MeshBuilder();b.Branch(new Vector3(0,-.5f,0),new Vector3(.16f,.5f,.08f),.5f,.015f,7,4);return b.Build("Presa curva");
    }
    private static Mesh Banner()
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
        for(int y=0;y<5;y++)for(int x=0;x<7;x++)
        {float px=x/6f,py=y/4f;float torn=y==0?(x%2==0?.0f:.12f):0;v.Add(new Vector3(px-.5f,py-.5f+torn,Mathf.Sin(px*7+py*3)*.06f));uv.Add(new Vector2(px,py));}
        for(int y=0;y<4;y++)for(int x=0;x<6;x++){int a=y*7+x;t.AddRange(new[]{a,a+1,a+7,a+1,a+8,a+7});}
        // Double-sided geometry keeps the cloth visible with the shared Standard material.
        int count=t.Count;for(int i=0;i<count;i+=3)t.AddRange(new[]{t[i+2],t[i+1],t[i]});
        return CreateMesh("Bandeira rasgada",v,uv,t);
    }
    private static Mesh Extrude(string name,Vector2[] outline,float depth)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();int n=outline.Length;
        for(int side=0;side<2;side++)foreach(Vector2 p in outline){v.Add(new Vector3(p.x,p.y,side==0?-depth:depth));uv.Add(p+Vector2.one*.5f);}
        for(int i=1;i<n-1;i++)t.AddRange(new[]{0,i+1,i,n,n+i,n+i+1});
        for(int i=0;i<n;i++){int j=(i+1)%n;t.AddRange(new[]{i,j,n+j,i,n+j,n+i});}
        return CreateMesh(name,v,uv,t);
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> v=new List<Vector3>();private readonly List<Vector2> uv=new List<Vector2>();private readonly List<int> t=new List<int>();
        public void Leaf(Vector3 position,Quaternion rotation,float width,float height)
        {int a=v.Count;foreach(Vector3 p in new[]{new Vector3(-width*.5f,0,0),new Vector3(width*.5f,0,0),new Vector3(-width*.4f,height,0),new Vector3(width*.4f,height,0)})v.Add(position+rotation*p);
        uv.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)});t.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}
        public void Branch(Vector3 start,Vector3 end,float bottom,float top,int sides,int rings)
        {
            int first=v.Count;Quaternion rotation=Quaternion.FromToRotation(Vector3.up,(end-start).normalized);
            for(int r=0;r<=rings;r++)for(int side=0;side<sides;side++)
            {float f=r/(float)rings,a=side*Mathf.PI*2/sides;float radius=Mathf.Lerp(bottom,top,f)*(1+.10f*Mathf.Sin(side*7+r*2));
            v.Add(Vector3.Lerp(start,end,f)+rotation*new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));uv.Add(new Vector2(side/(float)sides,f*(end-start).magnitude));}
            for(int r=0;r<rings;r++)for(int side=0;side<sides;side++){int a=first+r*sides+side,b=first+r*sides+(side+1)%sides;t.AddRange(new[]{a,b,a+sides,b,b+sides,a+sides});}
        }
        public Mesh Build(string name)=>CreateMesh(name,v,uv,t);
    }
    private static Mesh CreateMesh(string name,List<Vector3> v,List<Vector2> uv,List<int> t)
    {Mesh mesh=new Mesh{name=name};if(v.Count>65535)mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
    private static Mesh SaveMesh(string name,Mesh mesh)
    {
        mesh.name=name;
        string path=AssetRoot+"/"+name+".asset";Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,existing);EditorUtility.SetDirty(existing);Object.DestroyImmediate(mesh);return existing;
    }
    private static Texture2D RadialTexture()
    {
        string path=AssetRoot+"/BrasaRadial.asset";Texture2D texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if(texture==null){texture=new Texture2D(32,32,TextureFormat.RGBA32,false,true){name="Brasa radial suave",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};AssetDatabase.CreateAsset(texture,path);}
        Color[] pixels=new Color[32*32];
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){float radius=new Vector2((x+.5f)/16f-1,(y+.5f)/16f-1).magnitude;pixels[y*32+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),1.8f));}
        texture.SetPixels(pixels);texture.Apply(false,false);EditorUtility.SetDirty(texture);return texture;
    }
    private static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material,bool collision,int layer=0)
    {GameObject go=new GameObject(name);go.transform.SetParent(parent,false);go.layer=layer;go.AddComponent<MeshFilter>().sharedMesh=mesh;
    MeshRenderer renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.receiveShadows=true;
    if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;}
    private static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool collision)
    {GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;
    go.GetComponent<Renderer>().sharedMaterial=material;if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;}
    private static Material CopyMaterial(string name,Material source,Color tint)
    {Material target=MaterialAsset(name,source.shader);EditorUtility.CopySerialized(source,target);target.name=name;target.color=tint;target.enableInstancing=true;EditorUtility.SetDirty(target);return target;}
    private static Material Foliage(string name,Color color,float wind,float shape)
    {Shader shader=Shader.Find("Guerra das Nações/Folhagem da fronteira");if(shader==null)throw new BuildFailedException("Shader de vegetação ausente.");Material material=MaterialAsset(name,shader);material.color=color;material.SetFloat("_Wind",wind);material.SetFloat("_LeafShape",shape);material.enableInstancing=true;return material;}
    private static Material MaterialAsset(string name,Shader shader)
    {string path=MaterialRoot+"/"+name+".mat";Material material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material==null){material=new Material(shader){name=name};AssetDatabase.CreateAsset(material,path);}else material.shader=shader;EditorUtility.SetDirty(material);return material;}
    private static T Require<T>(string path)where T:Object
    {T value=AssetDatabase.LoadAssetAtPath<T>(path);if(value==null)throw new BuildFailedException("Asset da fronteira ausente: "+path);return value;}
}
