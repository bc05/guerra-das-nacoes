using System;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Real yaw/attack sector and a deliberately late authoritative hit. No network or progression write.
[InitializeOnLoad]
public static class CombatFeedbackPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.CombatFeedbackChecks.";
    private static PrototypeCharacter character;
    private static PrototypeCombat combat;
    private static PrototypeMonster west, north;
    private static PrototypeAttackEffects effects;
    private static GameObject floor;
    private static float nextAt;
    private static int swings, checks;
    private static string progression, commandId;
    static CombatFeedbackPlayModeChecks() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 40f);
        SessionState.SetBool(Key + "Active", true);
        nextAt = 0; checks = 0;
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Guard(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "timeout");
            if (!EditorApplication.isPlaying)
            {
                Guard(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antecipadamente"); return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 4 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    character = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
                    combat = character.GetComponent<PrototypeCombat>();
                    character.GetComponent<PrototypeOnlineClient>().ConfigureForTests(false);
                    effects = character.GetComponent<PrototypeAttackEffects>();
                    foreach (PrototypeMonster enemy in combat.Monsters) enemy.enabled = false;
                    west = combat.Monsters[0]; north = combat.Monsters[1];
                    floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.layer = 8;
                    floor.transform.position = new Vector3(500, -.5f, 500); floor.transform.localScale = new Vector3(30, 1, 30);
                    Fixture(); swings = combat.SwingCount;
                    Require(character.RequestFreeAttack(Vector3.left) && character.HasFreeAttack,
                        "toque direcional a90 graus guarda intenção de ataque sem alvo");
                    Require(combat.SwingCount == swings && !combat.IsAttacking && !combat.HasPendingStrike,
                        "direção ainda não alinhada não dispara golpe prematuro");
                    Next(1, .38f); break;
                case 1:
                    Require(!character.HasFreeAttack && combat.SwingCount == swings + 1,
                        "um toque curto executa um único golpe depois do giro gradual");
                    Require(Vector3.Angle(character.transform.forward, Vector3.left) <= 9.1f,
                        "setor comprometido aponta para o lado escolhido");
                    Require(west.CurrentHealth == west.MaxHealth - combat.Stats.AttackDamage && north.CurrentHealth == north.MaxHealth,
                        "ataque livre acerta a vítima no oeste e preserva quem estava na direção antiga");
                    Fixture(); swings = combat.SwingCount;
                    Require(character.RequestFreeAttack(Vector3.right), "nova intenção pode ser criada");
                    character.CancelFreeAttack();
                    Require(!character.HasFreeAttack, "movimento/Escape podem cancelar a intenção direcional antes do cast");
                    Next(2, .3f); break;
                case 2:
                    Require(combat.SwingCount == swings && !combat.HasPendingStrike, "intenção direcional cancelada não gera golpe atrasado");
                    progression = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    commandId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, commandId); swings = combat.SwingCount;
                    Require(combat.IsAttacking && effects.IsPlaying, "previsão visual começa antes da confirmação de rede");
                    Next(3, PrototypeCombat.AttackPresentationDuration + .05f); break;
                case 3:
                    Require(!combat.IsAttacking && !effects.IsPlaying && combat.HasSubmittedAttackPresentation,
                        "previsão termina antes da confirmação atrasada além da recuperação visual");
                    ApplyLateHit();
                    Require(combat.SwingCount == swings && !combat.IsAttacking && !combat.HasSubmittedAttackPresentation,
                        "ack tardio não repete animação, som de swing ou contador");
                    Require(effects.IsPlaying && effects.IsImpactVisible && effects.TrailSampleCount == 0,
                        "hit confirmado reativa somente corte/faíscas mesmo depois do fim do swing");
                    Require(combat.LastHitCount == 1 && west.LastDamage == 18 &&
                        JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == progression,
                        "impacto vem do evento confirmado e não altera o progresso do jogador");
                    Next(4, .09f); break;
                case 4:
                    Require(effects.IsImpactVisible && effects.TrailSampleCount == 0 && combat.SwingCount == swings,
                        "impacto tardio tem sua própria janela completa sem ressuscitar o rastro");
                    Next(5, .15f); break;
                case 5:
                    Require(!effects.IsPlaying && !effects.IsImpactVisible,
                        "impacto expira em180ms independentemente do relógio do swing");
                    effects.ShowImpact(character.transform.position + Vector3.forward * 2 + Vector3.up);
                    Require(effects.IsImpactVisible, "impacto isolado pode apresentar uma confirmação sem novo swing");
                    effects.Cancel();
                    Require(!effects.IsPlaying && !effects.IsImpactVisible && effects.TrailSampleCount == 0,
                        "cancelamento remove impacto isolado e todos os seus renderers ativos");
                    Finish(true, checks + " verificações de mira livre, giro, cancelamento, confirmação tardia e impacto independente."); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void ApplyLateHit()
    {
        combat.ApplyOnlineSnapshot(new OnlineSnapshot {
            playerId = "feedback-check", mapId = "ruins", serverTime = 1000 + Time.time, latestEventId = 201,
            character = combat.Stats.CaptureSaveData(),
            self = new OnlinePlayerState { id = "feedback-check", health = combat.Stats.Health,
                maxHealth = combat.Stats.MaxHealth, mana = character.GetComponent<PrototypeAbilities>().Mana },
            monsters = new[] { new OnlineMonsterState { id = "troll-0", mapId = "ruins", name = west.DisplayName,
                health = west.MaxHealth - 18, maxHealth = west.MaxHealth, damage = west.AttackDamage, experience = west.ExperienceReward,
                x = west.transform.position.x, z = west.transform.position.z, level = west.Level } },
            events = new[] {
                new OnlineWorldEvent { eventId = 200, kind = "attack", sourceId = "feedback-check", ability = 0 },
                new OnlineWorldEvent { eventId = 201, kind = "hit", sourceId = "feedback-check", targetId = "troll-0", amount = 18 }
            }
        });
    }
    private static void Fixture()
    {
        combat.ResetEncounter(); character.CancelFreeAttack();
        Move(character.transform, new Vector3(500, .04f, 500)); character.transform.rotation = Quaternion.identity;
        Move(west.transform, new Vector3(498.1f, .06f, 500)); Move(north.transform, new Vector3(500, .06f, 501.9f));
        foreach (PrototypeMonster enemy in new[] { west, north })
            typeof(PrototypeMonster).GetField("homePosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(enemy, enemy.transform.position);
        Physics.SyncTransforms();
    }
    private static void Move(Transform target, Vector3 position)
    {
        CharacterController capsule = target.GetComponent<CharacterController>(); capsule.enabled = false;
        target.position = position; capsule.enabled = true;
    }
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Guard(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Require(bool value, string message) { Guard(value, message); checks++; }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
        if (success) Debug.Log("[COMBAT FEEDBACK PLAY PASS] " + message);
        else Debug.LogError("[COMBAT FEEDBACK PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
