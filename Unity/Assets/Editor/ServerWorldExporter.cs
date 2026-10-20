using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ServerWorldExporter
{
    [Serializable] private sealed class CollisionData
    {
        public float originX = -17.5f, originZ = -17.5f, cellSize = .25f;
        public int width = 140, height = 140;
        public int[] blocked;
    }

    [MenuItem("Guerra das Nações/Exportar colisões para o servidor")]
    public static void Export()
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        var originalStates = roots.ToDictionary(root => root, root => root.activeSelf);
        try
        {
            // Both maps occupy their own local coordinates. Never let the inactive dungeon's
            // floor or tombs leak into the existing Vale physics export.
            foreach (GameObject root in roots)
                if (root.name == CatacombsLayout.RootName) root.SetActive(false);
            ExportVale();
        }
        finally { Restore(originalStates); }
    }

    private static void ExportVale()
    {
        bool expanded = GameObject.Find(PrototypeWorldExpansion.RootName) != null;
        var value = expanded
            ? new CollisionData { originX = -50f, originZ = -20f, width = 400, height = 400 }
            : new CollisionData();
        int count = SamplePhysics(value);
        var spawns = new System.Collections.Generic.List<Vector2> {
            Vector2.zero, new Vector2(0,-3), new Vector2(-2.6f,3.8f), new Vector2(0,5.8f), new Vector2(2.6f,3.8f), new Vector2(6.2f,0) };
        if (expanded) spawns.AddRange(new[] { new Vector2(-24,25), new Vector2(-28,34), new Vector2(-18,33),
            new Vector2(25,30), new Vector2(29,40), new Vector2(0,61) });
        if (SceneManager.GetActiveScene().GetRootGameObjects().Any(root => root.name == CatacombsLayout.RootName))
        {
            spawns.Add(new Vector2(CatacombsLayout.ValePortal.X,CatacombsLayout.ValePortal.Z));
            spawns.Add(new Vector2(CatacombsLayout.ValeReturn.X,CatacombsLayout.ValeReturn.Z));
        }
        foreach (Vector2 spawn in spawns) RequireFree(value,spawn,"Spawn/portal do Vale bloqueado");
        string path = Write(value,"world-collision.json");
        Debug.Log($"[SERVER GRID PASS] {value.width}x{value.height}, {count} células bloqueadas; spawns e portais livres. {path}");
    }

    [MenuItem("Guerra das Nações/Exportar colisões físicas das Catacumbas")]
    public static void ExportCatacombs()
    {
        GameObject[] roots=SceneManager.GetActiveScene().GetRootGameObjects();
        GameObject catacombs=roots.SingleOrDefault(root=>root.name==CatacombsLayout.RootName);
        if(catacombs==null)throw new InvalidOperationException("Implante as Catacumbas antes de exportar sua colisão real.");
        var originalStates=roots.ToDictionary(root=>root,root=>root.activeSelf);
        try
        {
            // Exclusive roots prevent the courtyard's ground from opening the dungeon void.
            foreach(GameObject root in roots)root.SetActive(root==catacombs);
            var value=new CollisionData {originX=CatacombsLayout.OriginX,originZ=CatacombsLayout.OriginZ,
                cellSize=CatacombsLayout.CellSize,width=CatacombsLayout.GridWidth,height=CatacombsLayout.GridHeight};
            int count=SamplePhysics(value);
            var points=new List<Vector2> {
                new Vector2(CatacombsLayout.Entry.X,CatacombsLayout.Entry.Z),
                new Vector2(CatacombsLayout.ReturnPortal.X,CatacombsLayout.ReturnPortal.Z)
            };
            points.AddRange(CatacombsLayout.SpawnPoints.Select(point=>new Vector2(point.X,point.Z)));
            foreach(Vector2 point in points)RequireFree(value,point,"Spawn/portal das Catacumbas bloqueado");
            RequireConnected(value,points);
            // Closed borders catch floor leaks and a forgotten collider/root before publication.
            for(int x=0;x<value.width;x++)
                if(value.blocked[x]==0||value.blocked[(value.height-1)*value.width+x]==0)
                    throw new InvalidOperationException("Borda norte/sul das Catacumbas aberta.");
            for(int z=0;z<value.height;z++)
                if(value.blocked[z*value.width]==0||value.blocked[z*value.width+value.width-1]==0)
                    throw new InvalidOperationException("Borda oeste/leste das Catacumbas aberta.");
            string path=Write(value,"catacombs-collision.json");
            Debug.Log($"[CATACOMBS GRID PASS] {value.width}x{value.height}, {count} células bloqueadas; dois pontos de portal e sete encontros conectados pela física real. {path}");
        }
        finally {Restore(originalStates);}
    }

    public static void ExportAll()
    {
        if(Application.isBatchMode)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity");
        Export();ExportCatacombs();
    }

    private static int SamplePhysics(CollisionData value)
    {
        value.blocked=new int[value.width*value.height];Physics.SyncTransforms();
        const int mask=~((1<<8)|(1<<9)|(1<<10));int count=0;
        for(int z=0;z<value.height;z++)
            for(int x=0;x<value.width;x++)
            {
                Vector3 center=new Vector3(value.originX+(x+.5f)*value.cellSize,0,value.originZ+(z+.5f)*value.cellSize);
                // The low end permits the same ~0.3m step as the prototype CharacterController.
                bool ground=Physics.Raycast(center+Vector3.up*2,Vector3.down,4,1<<8,QueryTriggerInteraction.Ignore);
                bool blocked=!ground||Physics.CheckCapsule(center+Vector3.up*.6f,center+Vector3.up*1.55f,.28f,mask,QueryTriggerInteraction.Ignore);
                value.blocked[z*value.width+x]=blocked?1:0;if(blocked)count++;
            }
        return count;
    }

    private static int RequireFree(CollisionData value,Vector2 point,string reason)
    {
        int x=Mathf.FloorToInt((point.x-value.originX)/value.cellSize),z=Mathf.FloorToInt((point.y-value.originZ)/value.cellSize);
        if(x<0||z<0||x>=value.width||z>=value.height||value.blocked[z*value.width+x]!=0)
            throw new InvalidOperationException(reason+": "+point);
        return z*value.width+x;
    }

    private static void RequireConnected(CollisionData value,List<Vector2> points)
    {
        var seen=new bool[value.blocked.Length];var queue=new Queue<int>();
        int entry=RequireFree(value,points[0],"Entrada bloqueada");seen[entry]=true;queue.Enqueue(entry);
        while(queue.Count>0)
        {
            int cell=queue.Dequeue(),x=cell%value.width,z=cell/value.width;
            foreach(int next in new[]{x>0?cell-1:-1,x+1<value.width?cell+1:-1,z>0?cell-value.width:-1,z+1<value.height?cell+value.width:-1})
                if(next>=0&&!seen[next]&&value.blocked[next]==0){seen[next]=true;queue.Enqueue(next);}
        }
        foreach(Vector2 point in points)
            if(!seen[RequireFree(value,point,"Ponto bloqueado")])
                throw new InvalidOperationException("Encontro/retorno sem caminho até a entrada: "+point);
    }

    private static void Restore(Dictionary<GameObject,bool> states)
    {foreach(var state in states)if(state.Key!=null)state.Key.SetActive(state.Value);Physics.SyncTransforms();}

    private static string Write(CollisionData value,string filename)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Server/"+filename));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(value));
        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        return path;
    }
}
