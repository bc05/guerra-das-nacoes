using System;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// An isolated arena exercises actual AI and presentation without touching stored characters.
[InitializeOnLoad]
public static class MonsterBehaviorPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.MonsterBehaviorChecks.";
    private static readonly Vector3 PlayerStart = new Vector3(500f, .04f, 500f);
    private static PrototypeCombat player;
    private static PrototypeMonster monster;
    private static GameObject floor, wall;
    private static float nextAt;
    private static int healthBefore, strikesBefore, checks;
    private static string originalProgress;
    static MonsterBehaviorPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar comportamentos e áreas dos monstros")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 75f);
        SessionState.SetBool(Key + "Active", true);
        nextAt = 0f; checks = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Guard(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0f), "tempo máximo excedido");
            if (!EditorApplication.isPlaying)
            {
                Guard(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antecipadamente"); return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 4 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    player = Object.FindFirstObjectByType<PrototypeCombat>();
                    Require(player != null, "personagem da cena presente");
                    player.GetComponent<PrototypeOnlineClient>().ConfigureForTests(false);
                    player.GetComponent<PrototypeCharacter>().enabled = false;
                    foreach (PrototypeMonster enemy in player.Monsters) enemy.enabled = false;
                    player.ResetEncounter();
                    originalProgress = JsonUtility.ToJson(player.Stats.CaptureSaveData());
                    floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Arena isolada dos comportamentos";
                    floor.layer = 8; floor.transform.position = new Vector3(500f, -.5f, 500f);
                    floor.transform.localScale = new Vector3(40f, 1f, 40f);
                    GameObject actor = new GameObject("Monstro descartável do teste");
                    actor.transform.position = new Vector3(500f, .04f, 505f);
                    GameObject environment = GameObject.Find("Cenario");
                    Require(environment != null, "raiz do mapa disponível para verificar transição");
                    actor.transform.SetParent(environment.transform, true);
                    CharacterController body = actor.AddComponent<CharacterController>();
                    body.height = 2f; body.center = Vector3.up; body.radius = .45f;
                    monster = actor.AddComponent<PrototypeMonster>();
                    GameObject visual = new GameObject("Visual do monstro de teste"); visual.transform.SetParent(actor.transform, false);
                    monster.Configure(player, visual.transform);
                    Fixture("ranged", 5f); Next(1, .2f); break;
                case 1:
                    RequireWarning(PlayerStart, 1.1f);
                    Require(Mathf.Abs(monster.transform.position.z - 505f) < .05f && monster.AttackReach == 7f,
                        "xamã prepara a cinco metros sem perseguir até alcance corpo a corpo");
                    Move(player.transform, PlayerStart + Vector3.right * 3f);
                    Next(2, 1.15f); break;
                case 2:
                    RequireStrike(false, "sair do círculo fixado esquiva o ataque à distância");
                    Require(Vector3.ProjectOnPlane(monster.PreparedAttackPosition - PlayerStart, Vector3.up).sqrMagnitude < .001f,
                        "ataque à distância não acompanha a nova posição do alvo");
                    Fixture("ranged", 5f); Next(3, 1.35f); break;
                case 3:
                    RequireStrike(true, "permanecer no círculo recebe um único impacto à distância");
                    Fixture("ranged", 5f); Next(4, .2f); break;
                case 4:
                    RequireWarning(PlayerStart, 1.1f);
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Obstrução inserida durante o cast";
                    wall.transform.position = new Vector3(500f, 1.5f, 502.5f); wall.transform.localScale = new Vector3(3f, 3f, .25f);
                    Physics.SyncTransforms(); Next(5, 1.15f); break;
                case 5:
                    RequireStrike(false, "obstáculo revalidado no impacto bloqueia o ataque à distância");
                    Object.DestroyImmediate(wall); wall = null;
                    Fixture("area", 2.5f); Next(6, .2f); break;
                case 6:
                    RequireWarning(monster.transform.position, 3f);
                    Move(player.transform, PlayerStart + Vector3.right * 4f);
                    Next(7, 1.45f); break;
                case 7:
                    RequireStrike(false, "sair dos três metros esquiva o ataque em área do troll");
                    Fixture("area", 2.5f); Next(8, 1.65f); break;
                case 8:
                    RequireStrike(true, "permanecer na área recebe um único impacto do troll");
                    Fixture("ranged", 5f); Next(9, .2f); break;
                case 9:
                    RequireWarning(PlayerStart, 1.1f);
                    monster.TakeDamage(int.MaxValue, player);
                    Require(!monster.IsAlive && !monster.AttackEffects.TelegraphVisible && !monster.AttackEffects.ImpactVisible,
                        "morte remove imediatamente o aviso e o impacto");
                    Next(10, .2f); break;
                case 10:
                    Require(!monster.IsAlive && monster.RespawnRemaining > 0f && monster.RespawnRemaining < .5f,
                        "tempo de reaparecimento segue o perfil configurado");
                    Next(11, .4f); break;
                case 11:
                    Require(monster.IsAlive && !monster.IsPreparingAttack && !monster.AttackEffects.TelegraphVisible,
                        "reaparecimento não retoma o cast antigo");
                    Fixture("ranged", 5f); Next(12, .2f); break;
                case 12:
                    RequireWarning(PlayerStart, 1.1f);
                    monster.enabled = false;
                    Require(!monster.AttackEffects.TelegraphVisible && !monster.AttackEffects.ImpactVisible,
                        "desativar o componente remove o aviso imediatamente");
                    Fixture("area", 2.5f); Next(13, .2f); break;
                case 13:
                    RequireWarning(monster.transform.position, 3f);
                    player.GetComponent<PrototypeCity>().ApplyMap("city");
                    Require(!monster.gameObject.activeInHierarchy && !monster.AttackEffects.TelegraphVisible && !monster.AttackEffects.ImpactVisible,
                        "transição para cidade remove aviso e colisão do monstro");
                    player.GetComponent<PrototypeCity>().ApplyMap("ruins");
                    Require(!monster.IsPreparingAttack && !monster.AttackEffects.TelegraphVisible,
                        "voltar ao vale não retoma preparação cancelada");
                    monster.enabled = false;
                    int presented = monster.AttackEffects.PresentedStrikes;
                    monster.ApplyOnlineState(new OnlineMonsterState { speciesId = "orc-shaman", behavior = "ranged",
                        health = 1000, maxHealth = 1000, attackRange = 7f, moveSpeed = 2f, leashRadius = 20f,
                        attackPreparation = 1.1f, attackInterval = 2f, areaRadius = 1.1f, respawnDelay = 6f,
                        attackX = 502f, attackZ = 501f, x = 500f, z = 505f, targetId = "other-player", windupUntil = 101.1d }, 100d);
                    Require(monster.Behavior == "ranged" && monster.SpeciesId == "orc-shaman" && monster.AttackReach == 7f &&
                        monster.PreparedAttackPosition.x == 502f && monster.PreparedAttackPosition.z == 501f,
                        "snapshot informa espécie, comportamento, alcance e posição fixa do aviso");
                    monster.ApplyOnlineState(new OnlineMonsterState { health = 1000, maxHealth = 1000, windupUntil = 0d }, 100.2d);
                    Require(!monster.IsPresentingStrike && monster.AttackEffects.PresentedStrikes == presented,
                        "cancelar preparação via snapshot não inventa um impacto");
                    player.ResetEncounter();
                    Require(JsonUtility.ToJson(player.Stats.CaptureSaveData()) == originalProgress,
                        "nível, experiência, atributos, equipamentos e vida restaurada permanecem iguais");
                    Finish(true, checks + " verificações de alcance, aviso fixo, esquiva, obstrução, área, dano único, morte, reaparecimento, mapa e progresso."); break;
            }
        }
        catch (Exception error) { Finish(false, "etapa=" + SessionState.GetInt(Key + "Stage", -1) + " " + error); }
    }

    private static void Fixture(string behavior, float distance)
    {
        player.ResetEncounter(); monster.enabled = false; monster.ResetEncounter();
        monster.ConfigureStats("Espécie descartável", 1000, 8, 0);
        monster.ConfigureThreat(1, false, 0f);
        monster.ConfigureEncounter(2.25f, 20f, behavior == "ranged" ? 7f : 1.65f);
        monster.ConfigureBehavior(behavior == "ranged" ? "orc-shaman" : "forest-troll", behavior,
            behavior == "ranged" ? 1.1f : 1.4f, behavior == "ranged" ? 2f : 2.4f, .5f,
            behavior == "ranged" ? 1.1f : 3f);
        Move(player.transform, PlayerStart);
        Move(monster.transform, PlayerStart + Vector3.forward * distance);
        Physics.SyncTransforms(); monster.enabled = true;
        healthBefore = player.Stats.Health; strikesBefore = monster.AttackEffects.PresentedStrikes;
        Require(monster.TakeDamage(1, player) == 1, "espécie engaja no agressor sem conceder experiência");
    }

    private static void RequireWarning(Vector3 center, float radius)
    {
        Require(monster.IsPreparingAttack && monster.AttackEffects.TelegraphVisible && player.Stats.Health == healthBefore,
            "aviso aparece antes de qualquer dano");
        Require(Vector3.ProjectOnPlane(monster.AttackEffects.TelegraphCenter - center, Vector3.up).sqrMagnitude < .0025f &&
            Mathf.Abs(monster.AttackEffects.TelegraphRadius - radius) < .001f, "aviso corresponde ao centro e raio reais");
    }

    private static void RequireStrike(bool hit, string message)
    {
        Require(monster.AttackEffects.PresentedStrikes == strikesBefore + 1 && monster.AttackEffects.ImpactVisible, message + ": impacto único e visível");
        Require(hit ? player.LastDamage > 0 && player.Stats.Health == healthBefore - player.LastDamage : player.Stats.Health == healthBefore,
            message + ": saúde confirmada");
    }

    private static void Move(Transform target, Vector3 position)
    {
        CharacterController body = target.GetComponent<CharacterController>();
        body.enabled = false; target.position = position; body.enabled = true; Physics.SyncTransforms();
    }
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Guard(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (monster != null) Object.DestroyImmediate(monster.gameObject);
        if (wall != null) Object.DestroyImmediate(wall);
        if (floor != null) Object.DestroyImmediate(floor);
        if (success) Debug.Log("[MONSTER BEHAVIOR PLAY PASS] " + message);
        else Debug.LogError("[MONSTER BEHAVIOR PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
