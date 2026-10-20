using System;
using System.Collections.Generic;
using System.Text;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// This check poses the imported skeleton directly and measures CPU-skinned vertices.
// It does not depend on Animator timing or the Editor's GPU skinning cache.
public static class MonsterSkinningDiagnostics
{
    public static void Run()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Troll.fbx");
        if (model == null) throw new InvalidOperationException("Troll.fbx ausente");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        GameObject instance = UnityEngine.Object.Instantiate(model);
        instance.name = "Diagnóstico temporário do troll";
        try
        {
            SkinnedMeshRenderer[] skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.Log("[MONSTER SKIN DIAG] renderers=" + skins.Length + " modelRotation=" + instance.transform.eulerAngles + " modelScale=" + instance.transform.localScale);
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath("Assets/Art/Troll.fbx");
            Debug.Log("[MONSTER IMPORT DIAG] animationType=" + importer.animationType + " importAnimation=" + importer.importAnimation +
                " optimizeGameObjects=" + importer.optimizeGameObjects + " isReadable=" + importer.isReadable);
            ReportRenderers(instance, "ImportedAsset");
            foreach (PrototypeMonster monster in UnityEngine.Object.FindObjectsByType<PrototypeMonster>(FindObjectsSortMode.None))
                ReportRenderers(monster.gameObject, "Scene/" + monster.name);
            if (skins.Length == 0)
                throw new InvalidOperationException("Troll foi importado como malhas rígidas. O Setup usa animationType=None: preservar Generic para manter bindings de skinning.");
            var baseline = CaptureVertices(skins);
            var posed = new List<Transform>();
            foreach (Transform bone in instance.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "Braco.L" || bone.name == "Braco.R")
                {
                    bone.rotation = Quaternion.AngleAxis(-85f, Vector3.right) * bone.rotation;
                    posed.Add(bone);
                }
            }
            if (posed.Count != 2) throw new InvalidOperationException("Braços duplicados ou ausentes: " + posed.Count);
            float maximum = MeasureDifference(skins, baseline, "ImportedModel/directPose");
            if (maximum < .1f) throw new InvalidOperationException("O esqueleto não deforma a malha: deslocamento máximo=" + maximum);
            foreach (SkinnedMeshRenderer skin in skins) ReportWeights(skin);
            Debug.Log("[MONSTER SKIN DIAG PASS] Rig real deforma malhas; deslocamento máximo em mundo=" + maximum.ToString("F4") + "m");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    private static void ReportRenderers(GameObject root, string label)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            string bones = renderer is SkinnedMeshRenderer skinned ? skinned.bones.Length.ToString() : "none";
            Debug.Log("[MONSTER RENDERER DIAG] " + label + " object=" + renderer.name + " type=" + renderer.GetType().Name +
                " mesh=" + (mesh != null ? mesh.name : "none") + " vertices=" + (mesh != null ? mesh.vertexCount : 0) +
                " bones=" + bones + " bindposes=" + (mesh != null ? mesh.bindposes.Length : 0));
        }
    }

    public static Dictionary<int, Vector3[]> CaptureVertices(SkinnedMeshRenderer[] skins)
    {
        var result = new Dictionary<int, Vector3[]>();
        foreach (SkinnedMeshRenderer skin in skins)
        {
            Mesh baked = new Mesh();
            try
            {
                // Imported FBX mesh nodes carry scale; match ThirdPartyVisualChecks' rendering convention.
                skin.BakeMesh(baked, true);
                Vector3[] values = baked.vertices;
                // Keep renderer-local vertices: scene root movement must not masquerade as deformation.
                result[skin.GetInstanceID()] = values;
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        return result;
    }

    public static float MeasureDifference(SkinnedMeshRenderer[] skins, Dictionary<int, Vector3[]> baseline, string label)
    {
        float allMaximum = 0f;
        foreach (SkinnedMeshRenderer skin in skins)
        {
            if (!baseline.TryGetValue(skin.GetInstanceID(), out Vector3[] original)) continue;
            Mesh baked = new Mesh();
            try
            {
                skin.BakeMesh(baked, true);
                Vector3[] values = baked.vertices;
                float maximum = 0f, total = 0f;
                int moved = 0;
                for (int vertex = 0; vertex < Mathf.Min(original.Length, values.Length); vertex++)
                {
                    float difference = skin.transform.TransformVector(values[vertex] - original[vertex]).magnitude;
                    maximum = Mathf.Max(maximum, difference);
                    total += difference;
                    if (difference > .01f) moved++;
                }
                allMaximum = Mathf.Max(allMaximum, maximum);
                Debug.Log("[MONSTER SKIN DIAG] " + label + " skin=" + skin.name + " vertices=" + values.Length + " moved=" + moved +
                    " maxWorld=" + maximum.ToString("F4") + " avgWorld=" + (total / Mathf.Max(1, values.Length)).ToString("F4") +
                    " bones=" + skin.bones.Length + " bounds=" + baked.bounds + " scale=" + skin.transform.lossyScale);
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        return allMaximum;
    }

    private static void ReportWeights(SkinnedMeshRenderer skin)
    {
        int[] verticesByBone = new int[skin.bones.Length];
        foreach (BoneWeight weight in skin.sharedMesh.boneWeights)
        {
            if (weight.weight0 > .001f && weight.boneIndex0 < verticesByBone.Length) verticesByBone[weight.boneIndex0]++;
            if (weight.weight1 > .001f && weight.boneIndex1 < verticesByBone.Length) verticesByBone[weight.boneIndex1]++;
            if (weight.weight2 > .001f && weight.boneIndex2 < verticesByBone.Length) verticesByBone[weight.boneIndex2]++;
            if (weight.weight3 > .001f && weight.boneIndex3 < verticesByBone.Length) verticesByBone[weight.boneIndex3]++;
        }
        var line = new StringBuilder("[MONSTER SKIN WEIGHTS] " + skin.name + ": ");
        for (int index = 0; index < skin.bones.Length; index++)
        {
            Transform bone = skin.bones[index];
            line.Append(index).Append(':').Append(bone != null ? bone.name : "NULL").Append(" id=").Append(bone != null ? bone.GetInstanceID() : 0)
                .Append(" vertices=").Append(verticesByBone[index]).Append("; ");
        }
        Debug.Log(line.ToString());
    }
}
