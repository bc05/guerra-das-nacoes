using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ThirdPartyVisualChecks
{
    public static void RefineAndCapture()
    {
        VisualRefinementSetup.Apply();
        CaptureOnly();
        EquipmentVisualPreviewChecks.Run();
        PrototypeSetup.RenderPreview();
    }

    public static void CaptureOnly()
    {
        try { Capture(); }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        }
    }

    [MenuItem("Guerra das Nações/Conferir modelos e gerar aplicativo")]
    public static void CaptureAndBuild()
    {
        PrototypeSetup.Validate();
        try { Capture(); }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        }
        PrototypeSetup.BuildMacOS();
        PrototypeSetup.RenderPreview();
    }

    private static void Capture()
    {
        PrototypeCharacter player = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
        GameObject hero = player.VisualRoot.GetChild(0).gameObject;
        Animator animator = hero.GetComponent<Animator>();
        animator.enabled = false;
        AnimationClip attack = animator.runtimeAnimatorController.animationClips.First(c => c.name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0);
        Transform tip = hero.GetComponentsInChildren<Transform>().First(t => t.name == "SwordTip");
        Transform bladeBase = hero.GetComponentsInChildren<Transform>().First(t => t.name == "SwordBase");
        SkinnedMeshRenderer sword = hero.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "KnightSword");
        Vector3 firstTip = Vector3.zero;
        float movement = 0f;
        Mesh baked = new Mesh();
        AnimationMode.StartAnimationMode();
        try
        {
            for (int frame = 0; frame <= 12; frame++)
            {
                Sample(hero, attack, frame / 30f);
                if (frame == 0) firstTip = tip.position;
                movement = Mathf.Max(movement, Vector3.Distance(firstTip, tip.position));
                if (Mathf.Abs(Vector3.Distance(tip.position, bladeBase.position) - 1.025f) > .005f)
                    throw new BuildFailedException("A lâmina mudou de tamanho na animação de ataque.");
                // FBX mesh nodes carry a 100x scale; compensate before TransformPoint.
                sword.BakeMesh(baked, true);
                float distance = baked.vertices.Min(v => Vector3.Distance(sword.transform.TransformPoint(v), tip.position));
                if (distance > .03f)
                {
                    Debug.Log($"[ASSET DIAG] frame={frame} heroScale={hero.transform.localScale} swordScale={sword.transform.lossyScale} baked={baked.bounds} tip={tip.position} swordPos={sword.transform.position} swordRot={sword.transform.eulerAngles}");
                    throw new BuildFailedException("Rastro afastado da ponta real da espada: " + distance + " m.");
                }
            }
            if (movement < .3f) throw new BuildFailedException("O clipe de ataque não move a lâmina: " + movement + " m.");
        }
        finally { UnityEngine.Object.DestroyImmediate(baked); }

        foreach (PrototypeMonster monster in UnityEngine.Object.FindObjectsByType<PrototypeMonster>(FindObjectsSortMode.None))
            foreach (Renderer renderer in monster.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        Camera camera = Camera.main;
        camera.GetComponent<PrototypeCamera>().enabled = false;
        camera.transform.position = player.transform.position + new Vector3(2.5f, 2.25f, 3.8f);
        camera.transform.rotation = Quaternion.LookRotation(player.transform.position + Vector3.up * .95f - camera.transform.position);
        camera.fieldOfView = 32f;
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        foreach (string kind in new[] { "idle", "walk", "run", "attack" })
        {
            AnimationClip clip = animator.runtimeAnimatorController.animationClips.First(c => c.name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0);
            Sample(hero, clip, kind == "attack" ? .133f : kind == "walk" || kind == "run" ? .23f : 0f);
            Save(camera, hero, Path.Combine(output, "Cavaleiro_" + kind + ".png"));
        }
        AnimationMode.StopAnimationMode();
        Debug.Log("[ASSETS VISUAL PASS] 13 poses de ataque, lâmina/rastro alinhados e quatro prévias dos modelos reais no Unity.");
    }

    private static void Sample(GameObject hero, AnimationClip clip, float time)
    {
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(hero, clip, time);
        AnimationMode.EndSampling();
    }

    private static void Save(Camera camera, GameObject hero, string path)
    {
        // Freeze CPU-skinned geometry for a synchronous editor render. The GPU skin cache
        // otherwise retains the previous pose until the next editor frame.
        var snapshots = new List<GameObject>();
        var meshes = new List<Mesh>();
        var states = new Dictionary<SkinnedMeshRenderer, bool>();
        SkinnedMeshRenderer[] skins = hero.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (SkinnedMeshRenderer skin in skins)
        {
            if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
            states[skin] = skin.enabled;
            Mesh mesh = new Mesh(); skin.BakeMesh(mesh, true); meshes.Add(mesh);
            GameObject snapshot = new GameObject("Pose de conferência"); snapshots.Add(snapshot);
            snapshot.transform.SetParent(skin.transform, false);
            snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = snapshot.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = skin.sharedMaterials;
            renderer.shadowCastingMode = skin.shadowCastingMode;
            renderer.receiveShadows = skin.receiveShadows;
            var properties = new MaterialPropertyBlock();
            skin.GetPropertyBlock(properties); renderer.SetPropertyBlock(properties);
            for (int index = 0; index < skin.sharedMaterials.Length; index++)
            {
                skin.GetPropertyBlock(properties, index); renderer.SetPropertyBlock(properties, index);
            }
            skin.enabled = false;
        }
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = camera.targetTexture;
        RenderTexture texture = RenderTexture.GetTemporary(1000, 1100, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D image = new Texture2D(1000, 1100, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = target; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(texture); UnityEngine.Object.DestroyImmediate(image);
            foreach (GameObject snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (Mesh mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            foreach (KeyValuePair<SkinnedMeshRenderer, bool> entry in states)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
        }
    }
}
