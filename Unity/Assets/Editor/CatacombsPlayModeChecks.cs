using System;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Offline disposable snapshots prove map isolation and cached creature ownership.</summary>
[InitializeOnLoad]
public static class CatacombsPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.CatacombsChecks.";
    private static int assertions;
    static CatacombsPlayModeChecks() { EditorApplication.update += Tick; }

    public static void Run()
    {
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 60);
        SessionState.SetBool(Key + "Active", true);
        assertions = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Guard(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "tempo máximo excedido");
            if (!EditorApplication.isPlaying)
            { Guard(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antes da conclusão"); return; }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 5) return;
            var combat = Object.FindFirstObjectByType<PrototypeCombat>();
            Require(combat != null, "personagem e combate preservados");
            var client = combat.GetComponent<PrototypeOnlineClient>();
            Require(client != null && !client.IsOnlineMode, "fixture usa progresso descartável e não conecta ao servidor");
            var maps = combat.GetComponent<PrototypeCity>();
            Require(maps != null && maps.IsBuilt, "apresentação de mapas pronta");
            GameObject catacombs = combat.gameObject.scene.GetRootGameObjects().Single(root => root.name == "Catacumbas");
            GameObject ruins = combat.gameObject.scene.GetRootGameObjects().Single(root => root.name == "Cenario");
            GameObject frontier = combat.gameObject.scene.GetRootGameObjects().Single(root => root.name == PrototypeWorldExpansion.RootName);
            Require(!catacombs.activeSelf && ruins.activeSelf && frontier.activeSelf, "entrada mantém Catacumbas inativa e Vale visível");
            Require(maps.MonsterRoot("catacombs") == catacombs.transform, "raiz inativa do mapa encontrada sem GameObject.Find");
            foreach (PrototypeMonster enemy in combat.Monsters) if (enemy != null) enemy.enabled = false;
            var initialTarget = combat.Monsters.FirstOrDefault(enemy => enemy != null);
            combat.SetTarget(initialTarget);
            Require(initialTarget != null && combat.Target == initialTarget, "alvo inicial válido torna o cancelamento observável");
            var ambientMode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight, fogColor = RenderSettings.fogColor;
            float ambientIntensity = RenderSettings.ambientIntensity, fogDensity = RenderSettings.fogDensity;
            bool fog = RenderSettings.fog;
            var fogMode = RenderSettings.fogMode;
            Material skybox = RenderSettings.skybox;
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(light => light.type == LightType.Directional).ToDictionary(light => light, light => light.enabled);

            maps.ApplyMap("catacombs");
            Require(catacombs.activeSelf && !ruins.activeSelf && !frontier.activeSelf && !maps.CityRoot.activeSelf,
                "somente o cenário das Catacumbas fica ativo");
            Require(!PrototypeCity.IsInCity && PrototypeCity.CurrentMapId == "catacombs", "mapa de combate subterrâneo não é tratado como cidade");
            Require(combat.Target == null && !combat.HasQueuedAttack && !combat.HasPendingStrike, "mudança cancela alvo e intenção de golpe");
            Require(RenderSettings.skybox == null && RenderSettings.fog && lights.Keys.All(light => !light.enabled),
                "iluminação subterrânea desativa céu e direcionais do exterior");

            combat.ApplyOnlineSnapshot(Snapshot(combat, "catacombs", "catacombs-fixture"));
            PrototypeMonster cached = combat.Monsters.Single();
            cached.enabled = false;
            Require(cached.transform.parent == catacombs.transform && cached.gameObject.activeInHierarchy,
                "criatura dinâmica pertence ao cenário subterrâneo");
            maps.ApplyMap("city");
            combat.ApplyOnlineSnapshot(Snapshot(combat, "city", null));
            Require(maps.CityRoot.activeSelf && !catacombs.activeSelf && !ruins.activeSelf && !frontier.activeSelf,
                "cidade não reativa cenários de combate");
            Require(combat.Monsters.Count == 0 && !cached.gameObject.activeInHierarchy, "encontro anterior permanece oculto na cidade");
            Require(RenderSettings.ambientMode == ambientMode && RenderSettings.ambientLight == ambient &&
                Mathf.Approximately(RenderSettings.ambientIntensity, ambientIntensity) && RenderSettings.skybox == skybox &&
                RenderSettings.fog == fog && RenderSettings.fogMode == fogMode && RenderSettings.fogColor == fogColor &&
                Mathf.Approximately(RenderSettings.fogDensity, fogDensity) && lights.All(pair => pair.Key.enabled == pair.Value),
                "saída restaura iluminação e neblina originais");

            maps.ApplyMap("ruins");
            combat.ApplyOnlineSnapshot(Snapshot(combat, "ruins", "ruins-fixture"));
            PrototypeMonster surface = combat.Monsters.Single();
            surface.enabled = false;
            Require(surface.transform.parent == ruins.transform && surface.gameObject.activeInHierarchy && !cached.gameObject.activeInHierarchy,
                "criatura de superfície fica no Vale e cache subterrâneo permanece oculto");
            maps.ApplyMap("catacombs");
            combat.ApplyOnlineSnapshot(Snapshot(combat, "catacombs", "catacombs-fixture"));
            Require(combat.Monsters.Single() == cached && !surface.gameObject.activeInHierarchy,
                "retorno reutiliza a criatura e não mostra monstros do outro mapa");
            Require(PrototypeCity.MapName("catacombs") == "Catacumbas" && PrototypeCity.MapName("ruins") == "Vale dos Clãs",
                "menu, HUD, destinos e saques têm nomes de mapa legíveis");
            maps.ApplyMap("ruins");
            Require(ruins.activeSelf && frontier.activeSelf && !catacombs.activeSelf, "retorno ao Vale restaura seus cenários");
            Finish(true, assertions + " verificações de raízes, transição, alvo, iluminação e cache por mapa.");
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static OnlineSnapshot Snapshot(PrototypeCombat combat, string map, string monsterId)
    {
        CharacterSaveData progress = combat.Stats.CaptureSaveData();
        return new OnlineSnapshot {
            playerId = "catacombs-local-check", mapId = map, serverTime = 100,
            character = progress,
            self = new OnlinePlayerState { id = "catacombs-local-check", mapId = map, health = progress.health,
                maxHealth = combat.Stats.MaxHealth, mana = 100, cooldownUntil = new double[5] },
            monsters = monsterId == null ? Array.Empty<OnlineMonsterState>() : new[] {
                new OnlineMonsterState { id = monsterId, speciesId = "disposable-fixture", name = "Encontro descartável",
                    mapId = map, spawn = 1, level = 1, health = 240, maxHealth = 240, damage = 5, experience = 10,
                    x = 0, z = 3, visualKind = "troll", visualScale = 1, attackRange = 1.65f,
                    moveSpeed = 2.25f, aggroRadius = 0, leashRadius = 8 }
            },
            events = Array.Empty<OnlineWorldEvent>(), results = Array.Empty<OnlineCommandResult>()
        };
    }

    private static void Guard(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
    private static void Require(bool ok, string message)
    { assertions++; if (!ok) throw new InvalidOperationException(message); }
    private static void Finish(bool success, string detail)
    {
        SessionState.SetBool(Key + "Active", false);
        if (success) Debug.Log("[CATACOMBS PLAYMODE PASS] " + detail);
        else Debug.LogError("[CATACOMBS PLAYMODE FAIL] " + detail);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
