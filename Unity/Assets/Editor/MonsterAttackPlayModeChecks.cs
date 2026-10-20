using System;
using System.Collections.Generic;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MonsterAttackPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.MonsterAttackChecks.";
    private static PrototypeCombat player;
    private static PrototypeMonster monster;
    private static Transform forearm;
    private static Quaternion restForearm;
    private static float nextAt;
    private static int healthBefore;
    private static Dictionary<int, Vector3[]> restingVertices;
    static MonsterAttackPlayModeChecks() { EditorApplication.update += Tick; }

    public static void Run()
    {
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 75f);
        SessionState.SetBool(Key + "Active", true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "tempo máximo excedido");
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    player = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
                    Require(player != null && player.Monsters.Count == 3, "cena configurada");
                    foreach (PrototypeMonster enemy in player.Monsters) enemy.enabled = false;
                    monster = player.Monsters[0];
                    foreach (Transform bone in monster.GetComponentsInChildren<Transform>())
                        if (bone.name == "forearm.L") forearm = bone;
                    Require(forearm != null, "modelo possui cadeia articulada de antebraço");
                    restForearm = forearm.localRotation;
                    restingVertices = MonsterSkinningDiagnostics.CaptureVertices(monster.GetComponentsInChildren<SkinnedMeshRenderer>());
                    Fixture();
                    healthBefore = player.Stats.Health;
                    Require(monster.TakeDamage(1, player) == 1, "inimigo reage ao agressor");
                    Next(1, .50f); break;
                case 1:
                    Require(monster.IsPreparingAttack && monster.AttackEffects.TelegraphVisible, "preparação possui limite de alcance legível");
                    Require(Quaternion.Angle(restForearm, forearm.localRotation) > 1f, "antebraço participa da antecipação");
                    Require(MonsterSkinningDiagnostics.MeasureDifference(monster.GetComponentsInChildren<SkinnedMeshRenderer>(), restingVertices, "RuntimeWindup") > .3f,
                        "preparação deforma a malha em pelo menos .3m");
                    Require(monster.AttackEffects.PresentedStrikes == 0 && player.Stats.Health == healthBefore, "preparação não causa impacto ou dano");
                    CaptureFrame("monster-windup.png");
                    Move(player.transform, new Vector3(0f, .04f, -.8f));
                    Next(2, .12f); break;
                case 2:
                    Require(!monster.IsPreparingAttack && !monster.AttackEffects.TelegraphVisible, "evasão cancela a preparação");
                    Require(monster.AttackEffects.PresentedStrikes == 0 && !monster.IsPresentingStrike, "cancelamento nunca é apresentado como golpe");
                    Require(player.Stats.Health == healthBefore, "golpe cancelado preserva saúde");
                    Fixture();
                    Require(monster.TakeDamage(1, player) == 1, "novo encontro");
                    Next(3, .84f); break;
                case 3:
                    Require(monster.AttackEffects.PresentedStrikes == 1 && monster.IsPresentingStrike, "resolução inicia um golpe com recuperação");
                    Require(monster.AttackEffects.ImpactVisible, "golpe possui poeira e estilhaços no ponto de contato");
                    Require(player.Stats.Health == healthBefore - monster.AttackDamage, "efeitos preservam dano de combate");
                    CaptureFrame("monster-impact.png");
                    monster.enabled = false;
                    Require(!monster.AttackEffects.TelegraphVisible && !monster.AttackEffects.ImpactVisible, "desativação limpa apresentação imediatamente");
                    monster.enabled = true;
                    monster.TakeDamage(int.MaxValue, player);
                    Require(!monster.IsAlive && !monster.IsPresentingStrike && !monster.AttackEffects.ImpactVisible, "morte cancela golpe e efeitos");
                    Next(4, .1f); break;
                case 4:
                    monster.ResetEncounter();
                    Require(!monster.AttackEffects.TelegraphVisible && !monster.AttackEffects.ImpactVisible, "reaparecimento não reproduz efeitos históricos");
                    // Removing a windup from a snapshot must never infer a hit.
                    monster.enabled = false;
                    int strikes = monster.AttackEffects.PresentedStrikes;
                    monster.ApplyOnlineState(new OnlineMonsterState { health = monster.MaxHealth, x = 0f, z = 1.4f, windupUntil = 100.65d }, 100d);
                    monster.ApplyOnlineState(new OnlineMonsterState { health = monster.MaxHealth, x = 0f, z = 1.4f, windupUntil = 0d }, 100.2d);
                    Require(!monster.IsPresentingStrike && monster.AttackEffects.PresentedStrikes == strikes,
                        "snapshot cancelado não dispara apresentação de impacto");
                    Finish(true, "Antecipação articulada, alcance, evasão sem impacto falso, dano único, poeira/pedras, recuperação, morte, desativação e snapshots cancelados."); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Fixture()
    {
        player.ResetEncounter();
        Move(player.transform, new Vector3(0f, .04f, 0f));
        Move(monster.transform, new Vector3(0f, .06f, 1.4f));
        monster.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        monster.enabled = true;
        Physics.SyncTransforms();
    }
    private static void Move(Transform target, Vector3 position)
    {
        CharacterController controller = target.GetComponent<CharacterController>();
        controller.enabled = false; target.position = position; controller.enabled = true;
        Physics.SyncTransforms();
    }
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void CaptureFrame(string filename)
    {
        string folder = Environment.GetEnvironmentVariable("PROTOTYPE_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(folder) || Camera.main == null) return;
        System.IO.Directory.CreateDirectory(folder);
        Camera camera = Camera.main;
        Vector3 previousPosition = camera.transform.position;
        Quaternion previousRotation = camera.transform.rotation;
        float previousFieldOfView = camera.fieldOfView;
        PrototypeCamera follow = camera.GetComponent<PrototypeCamera>();
        bool previousFollow = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        camera.transform.position = monster.transform.position + new Vector3(5f, 3.5f, -6f);
        camera.transform.rotation = Quaternion.LookRotation(monster.transform.position + Vector3.up * 1.6f - camera.transform.position);
        camera.fieldOfView = 42f;
        RenderTexture previousActive = RenderTexture.active, previousTarget = camera.targetTexture;
        RenderTexture target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32);
        Texture2D pixels = new Texture2D(1440, 900, TextureFormat.RGB24, false);
        var snapshots = new List<GameObject>();
        var meshes = new List<Mesh>();
        var skinStates = new Dictionary<SkinnedMeshRenderer, bool>();
        try
        {
            // Synchronous Camera.Render can retain an old GPU-skinned pose in the Editor.
            // Freeze the measured CPU pose, as ThirdPartyVisualChecks already does for the knight.
            foreach (SkinnedMeshRenderer skin in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                skinStates[skin] = skin.enabled;
                Mesh baked = new Mesh();
                // Include FBX mesh-node scale, exactly as ThirdPartyVisualChecks.Save does.
                skin.BakeMesh(baked, true);
                meshes.Add(baked);
                GameObject snapshot = new GameObject("Pose CPU para captura");
                snapshots.Add(snapshot);
                snapshot.transform.SetParent(skin.transform, false);
                snapshot.AddComponent<MeshFilter>().sharedMesh = baked;
                snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                skin.enabled = false;
            }
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
            pixels.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, filename), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            camera.fieldOfView = previousFieldOfView;
            if (follow != null) follow.enabled = previousFollow;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(pixels);
            foreach (GameObject snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (Mesh baked in meshes) UnityEngine.Object.DestroyImmediate(baked);
            foreach (KeyValuePair<SkinnedMeshRenderer, bool> skin in skinStates) if (skin.Key != null) skin.Key.enabled = skin.Value;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (success) Debug.Log("[MONSTER ATTACK PLAY PASS] " + message);
        else Debug.LogError("[MONSTER ATTACK PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
