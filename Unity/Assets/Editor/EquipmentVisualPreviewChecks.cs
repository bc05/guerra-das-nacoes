using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EquipmentVisualPreviewChecks
{
    [MenuItem("Guerra das Nações/Gerar prévias do corpo e equipamentos")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Gere as prévias fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        GameObject previewLights = null;
        try
        {
            string folder = Environment.GetEnvironmentVariable("PROTOTYPE_CAPTURE_DIRECTORY");
            if (string.IsNullOrEmpty(folder)) folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../EquipmentPreviews"));
            Directory.CreateDirectory(folder);
            PrototypeCharacter player = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
            PrototypeCombat combat = player != null ? player.GetComponent<PrototypeCombat>() : null;
            PrototypeEquipmentAppearance appearance = combat != null ? combat.GetComponent<PrototypeEquipmentAppearance>() : null;
            Require(player != null && combat != null && appearance != null && appearance.IsConfigured, "personagem modular configurado");
            // Historical scenarios explicitly restore the pre-economy inventory; new characters keep twelve items.
            typeof(PrototypeCombat).GetField("stats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(combat, EconomyCoreChecks.LegacyFixture());
            GameObject hero = player.VisualRoot.GetChild(0).gameObject;
            Animator animator = hero.GetComponent<Animator>();
            Require(animator != null && animator.runtimeAnimatorController != null, "animações do cavaleiro disponíveis");
            animator.enabled = false;
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (!renderer.transform.IsChildOf(player.VisualRoot)) renderer.enabled = false;
            Camera camera = Camera.main;
            Require(camera != null, "câmera configurada");
            PrototypeCamera follow = camera.GetComponent<PrototypeCamera>();
            if (follow != null) follow.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.105f, .125f, .145f, 1f);
            previewLights = CreatePreviewLights(player.transform.position);
            SetFullCamera(camera, player.transform.position);
            appearance.RefreshAppearance();
            Require(Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>().All(slot => combat.Stats.Equipment.GetEquipped(slot) != null), "set inicial completo");
            AnimationMode.StartAnimationMode();
            foreach (string kind in new[] { "idle", "walk", "run", "attack" })
            {
                Sample(hero, animator, kind);
                Save(camera, player.VisualRoot, Path.Combine(folder, "equipment-full-" + kind + ".png"));
            }
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.Shoulders, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet })
            {
                string itemId = combat.Stats.Equipment.GetEquipped(slot).Id;
                Require(combat.Stats.Equipment.TryUnequip(slot, out _), "retirada isolada: " + slot);
                appearance.RefreshAppearance();
                Sample(hero, animator, "idle");
                Save(camera, player.VisualRoot, Path.Combine(folder, "equipment-without-" + slot.ToString().ToLowerInvariant() + ".png"));
                Require(combat.Stats.Equipment.TryEquip(itemId, slot, out _), "restaurar peça isolada: " + slot);
                appearance.RefreshAppearance();
            }
            Require(combat.Stats.Equipment.TryUnequip(EquipmentSlot.Head, out _) &&
                combat.Stats.Equipment.TryUnequip(EquipmentSlot.Chest, out _), "retirada parcial de elmo e peitoral");
            appearance.RefreshAppearance();
            Require(!appearance.IsArmourVisible(EquipmentSlot.Head) && !appearance.IsArmourVisible(EquipmentSlot.Chest) &&
                appearance.IsBaseRegionVisible(EquipmentSlot.Head) && appearance.IsBaseRegionVisible(EquipmentSlot.Chest), "base cobre as duas peças retiradas");
            Sample(hero, animator, "idle");
            Save(camera, player.VisualRoot, Path.Combine(folder, "equipment-partial-idle.png"));
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                if (combat.Stats.Equipment.GetEquipped(slot) != null)
                    Require(combat.Stats.Equipment.TryUnequip(slot, out _), "retirar peça " + slot);
            appearance.RefreshAppearance();
            Require(PrototypeEquipmentAppearance.ArmourSlots.All(slot => !appearance.IsArmourVisible(slot) && appearance.IsBaseRegionVisible(slot)),
                "seis regiões base estão visíveis sem armadura");
            Require(!appearance.SwordVisible && !appearance.ShieldVisible &&
                PrototypeEquipmentVisual.AccessorySlots.All(slot => !appearance.IsAccessoryVisible(slot)), "sem armas ou acessórios");
            foreach (string kind in new[] { "idle", "walk", "run", "attack" })
            {
                Sample(hero, animator, kind);
                Save(camera, player.VisualRoot, Path.Combine(folder, "equipment-base-" + kind + ".png"));
            }
            Sample(hero, animator, "idle");
            Vector3 root = player.transform.position;
            camera.transform.position = root + new Vector3(0f, 1.60f, 1.10f);
            camera.transform.rotation = Quaternion.LookRotation(root + Vector3.up * 1.58f - camera.transform.position);
            camera.fieldOfView = 28f;
            Save(camera, player.VisualRoot, Path.Combine(folder, "equipment-base-head.png"));
            Debug.Log("[EQUIPMENT PREVIEW PASS] Catorze capturas CPU com apenas skins ativas: conjunto completo/base em Idle, Walk, Run e Attack, quatro retiradas isoladas, retirada parcial de elmo/peitoral e detalhe da cabeça. " + folder);
        }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (previewLights != null) UnityEngine.Object.DestroyImmediate(previewLights);
            // All mutations were transient scene-instance state. Never save the preview scene.
            EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        }
    }

    private static GameObject CreatePreviewLights(Vector3 root)
    {
        GameObject rig = new GameObject("Luzes temporárias de inspeção");
        foreach (bool key in new[] { true, false })
        {
            GameObject source = new GameObject(key ? "Luz frontal" : "Preenchimento suave");
            source.transform.SetParent(rig.transform, false);
            source.transform.position = root + (key ? new Vector3(2.3f, 3.0f, 4.0f) : new Vector3(-2.5f, 1.8f, 2.6f));
            source.transform.rotation = Quaternion.LookRotation(root + Vector3.up - source.transform.position);
            Light light = source.AddComponent<Light>();
            light.type = LightType.Directional; light.shadows = LightShadows.None;
            light.intensity = key ? 1f : .4f;
            light.color = key ? new Color(1f, .96f, .90f) : new Color(.76f, .85f, 1f);
        }
        return rig;
    }

    private static void SetFullCamera(Camera camera, Vector3 root)
    {
        camera.transform.position = root + new Vector3(0f, 1.16f, 3.8f);
        camera.transform.rotation = Quaternion.LookRotation(root + Vector3.up * .96f - camera.transform.position);
        camera.fieldOfView = 32f;
    }

    private static void Sample(GameObject hero, Animator animator, string kind)
    {
        AnimationClip clip = animator.runtimeAnimatorController.animationClips.First(value => value.name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0);
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(hero, clip, kind == "attack" ? .133f : kind == "walk" || kind == "run" ? .23f : 0f);
        AnimationMode.EndSampling();
    }

    private static void Save(Camera camera, Transform visualRoot, string path)
    {
        var snapshots = new List<GameObject>();
        var meshes = new List<Mesh>();
        var states = new Dictionary<SkinnedMeshRenderer, bool>();
        RenderTexture previousActive = RenderTexture.active, previousTarget = camera.targetTexture;
        RenderTexture target = RenderTexture.GetTemporary(1000, 1100, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D image = new Texture2D(1000, 1100, TextureFormat.RGB24, false);
        try
        {
            // Synchronous editor renders can retain the previous GPU skin pose. Bake only
            // enabled parts: hidden reference/legacy meshes must never reappear in a preview.
            foreach (SkinnedMeshRenderer skin in visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                states[skin] = skin.enabled;
                Mesh mesh = new Mesh(); skin.BakeMesh(mesh, true); meshes.Add(mesh);
                GameObject snapshot = new GameObject("Prévia CPU de peça ativa"); snapshots.Add(snapshot);
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
            Require(states.Count > 0, "há skins ativas para capturar");
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image);
            foreach (GameObject snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (Mesh mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            foreach (KeyValuePair<SkinnedMeshRenderer, bool> entry in states) if (entry.Key != null) entry.Key.enabled = entry.Value;
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Prévia de equipamentos: " + message); }
}
