using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Visual QA for the saved frontier and its real shipped model variants.
/// Spawns are transient previews; the configured server schedule remains authoritative.</summary>
public static class WorldExpansionPreview
{
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        var meshes=new List<Mesh>();var snapshots=new List<GameObject>();
        try
        {
            PrototypeCharacter player=Object.FindFirstObjectByType<PrototypeCharacter>();
            PrototypeCombat combat=player.GetComponent<PrototypeCombat>();
            PrototypeMonster source=combat.Monsters.First();
            foreach(PrototypeMonster original in combat.Monsters)
                foreach(Renderer renderer in original.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            CreatePreview(source,new OnlineMonsterState{id="orc-raider-0",name="Orc Saqueador",x=-24,z=25,visualKind="orc",visualScale=1f,maxHealth=600,health=600,level=7});
            CreatePreview(source,new OnlineMonsterState{id="orc-warrior-0",name="Orc Guerreiro",x=-28,z=34,visualKind="orc",visualScale=1.08f,maxHealth=1000,health=1000,level=10});
            CreatePreview(source,new OnlineMonsterState{id="orc-shaman-0",name="Orc Xamã",x=-18,z=33,visualKind="orc",visualScale=.93f,maxHealth=700,health=700,level=9});
            CreatePreview(source,new OnlineMonsterState{id="troll-brute-0",name="Troll Brutamontes",x=25,z=30,visualKind="troll",visualScale=1.18f,maxHealth=1600,health=1600,level=11});
            CreatePreview(source,new OnlineMonsterState{id="troll-elder-0",name="Troll Ancião",x=29,z=40,visualKind="troll",visualScale=1.3f,maxHealth=2200,health=2200,level=13});
            CreatePreview(source,new OnlineMonsterState{id="boss-grond",name="Grond",x=0,z=61,visualKind="boss-troll",isBoss=true,visualScale=1.7f,maxHealth=12000,health=12000,level=25});
            player.transform.position=new Vector3(0,.04f,54.5f);
            foreach(ParticleSystem particles in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))particles.Simulate(1.2f,true,true);
            foreach(SkinnedMeshRenderer skin in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if(!skin.enabled||!skin.gameObject.activeInHierarchy||skin.sharedMesh==null)continue;
                Mesh mesh=new Mesh();skin.BakeMesh(mesh,true);meshes.Add(mesh);
                GameObject snapshot=new GameObject("Pose estática para inspeção");snapshots.Add(snapshot);snapshot.transform.SetParent(skin.transform,false);
                snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
            }
            Camera camera=Camera.main;PrototypeCamera follow=camera.GetComponent<PrototypeCamera>();if(follow!=null)follow.enabled=false;
            string folder=Environment.GetEnvironmentVariable("PROTOTYPE_CAPTURE_DIRECTORY");
            if(string.IsNullOrEmpty(folder))folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/previews/fronteira"));
            Directory.CreateDirectory(folder);
            Save(camera,new Vector3(0,58,-10),new Vector3(0,0,35),58,Path.Combine(folder,"fronteira-mapa.png"));
            Save(camera,new Vector3(-12,10,11),new Vector3(-26,1.6f,30),57,Path.Combine(folder,"fronteira-orcs.png"));
            Save(camera,new Vector3(20,5.3f,24.5f),new Vector3(25.5f,1.6f,33),55,Path.Combine(folder,"fronteira-trolls.png"));
            Save(camera,new Vector3(9,6.4f,47),new Vector3(0,3,61),44,Path.Combine(folder,"fronteira-chefe.png"));
            Debug.Log("[WORLD EXPANSION PREVIEW PASS] Quatro imagens Unity do mapa/Orcs/Trolls/Chefão; instâncias de inspeção não salvas. "+folder);
        }
        finally
        {
            foreach(GameObject snapshot in snapshots)if(snapshot!=null)Object.DestroyImmediate(snapshot);
            foreach(Mesh mesh in meshes)if(mesh!=null)Object.DestroyImmediate(mesh);
            EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        }
    }

    private static void CreatePreview(PrototypeMonster source,OnlineMonsterState state)
    {
        GameObject copy=Object.Instantiate(source.gameObject);copy.name="Prévia · "+state.name;
        copy.transform.SetPositionAndRotation(new Vector3(state.x,.06f,state.z),Quaternion.Euler(0,180,0));
        foreach(Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))renderer.enabled=true;
        PrototypeMonster monster=copy.GetComponent<PrototypeMonster>();monster.ConfigureStats(state.name,state.maxHealth,70,500);
        PrototypeWorldExpansion.ConfigureMonsterVisual(monster,state);
        monster.enabled=false;
    }

    private static void Save(Camera camera,Vector3 position,Vector3 focus,float fieldOfView,string path)
    {
        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position));camera.fieldOfView=fieldOfView;
        RenderTexture oldActive=RenderTexture.active,oldTarget=camera.targetTexture;
        RenderTexture target=RenderTexture.GetTemporary(1500,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        Texture2D image=new Texture2D(1500,1000,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();
            Shader foliage=Shader.Find("Guerra das Nações/Folhagem da fronteira");
            if(foliage==null||!foliage.isSupported||ShaderUtil.ShaderHasError(foliage))
                throw new BuildFailedException("A captura detectou erro de shader na folhagem; verifique o compilador Metal.");
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);}
    }
}
