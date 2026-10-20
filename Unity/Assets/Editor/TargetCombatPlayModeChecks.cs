using System;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Uses the actual CharacterController, ability pipeline and colliders; never connects or saves a character.
[InitializeOnLoad]
public static class TargetCombatPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.TargetCombatChecks.";
    private static PrototypeCombat combat;
    private static PrototypeCharacter character;
    private static PrototypeMonster enemy;
    private static PrototypeAbilities abilities;
    private static GameObject floor, wall;
    private static float nextAt, manaBefore;
    private static int swingsBefore, healthBefore, checks;
    private static Vector3 start;
    private static string progressionBefore, predictedCommand;
    private static Quaternion originalMonsterVisualRotation;

    static TargetCombatPlayModeChecks() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 55f);
        SessionState.SetBool(Key + "Active", true);
        checks = 0; nextAt = 0f;
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
                    abilities = character.GetComponent<PrototypeAbilities>();
                    foreach (PrototypeMonster monster in combat.Monsters) monster.enabled = false;
                    enemy = combat.Monsters[0];
                    originalMonsterVisualRotation = Visual(enemy).localRotation;
                    floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.layer = 8; floor.name = "Piso temporário de combate direcionado";
                    floor.transform.position = new Vector3(500, -.5f, 500);
                    floor.transform.localScale = new Vector3(40, 1, 40);
                    Fixture(new Vector3(500, .04f, 500), new Vector3(500, .06f, 507.5f));
                    combat.SetTarget(enemy);
                    manaBefore = abilities.Mana; swingsBefore = combat.SwingCount; healthBefore = enemy.CurrentHealth;
                    Require(abilities.TryUse(1) && combat.HasQueuedAttack, "habilidade aceita intenção de se aproximar do alvo distante");
                    Require(abilities.Mana == manaBefore && combat.CooldownRemaining == 0f &&
                        abilities.GetCooldownRemaining(1) == 0f && combat.SwingCount == swingsBefore && enemy.CurrentHealth == healthBefore,
                        "aproximação não consome mana/recarga nem aplica dano antecipado");
                    Require(combat.TryGetApproachDestination(out Vector3 approach) && approach.z > character.transform.position.z,
                        "destino termina antes do corpo do alvo");
                    Next(1, .5f); break;
                case 1:
                    Require(character.IsMoving && character.transform.position.z - start.z > .5f,
                        "CharacterController caminha até o alvo selecionado");
                    Require(enemy.CurrentHealth == healthBefore && abilities.Mana >= manaBefore,
                        "fora do alcance continua sem dano e sem consumo de mana");
                    Require(combat.TryGetApproachDestination(out Vector3 remainingApproach),
                        "alvo distante continua fornecendo destino de aproximação");
                    Next(2, Vector3.Distance(character.transform.position, remainingApproach) /
                        PrototypeCharacter.DefaultWalkSpeed + .5f); break;
                case 2:
                    Require(combat.SwingCount == swingsBefore + 1 && !combat.HasQueuedAttack,
                        "ao entrar em alcance executa uma única habilidade enfileirada");
                    Require(enemy.CurrentHealth < healthBefore && abilities.Mana < manaBefore,
                        "contato real aplica dano e só então a habilidade consome mana");
                    Require(Vector3.Dot(character.transform.forward, (enemy.transform.position-character.transform.position).normalized) > .98f,
                        "golpe e facing apontam para o alvo");
                    Fixture(new Vector3(500, .04f, 500), new Vector3(500, .06f, 498.1f));
                    combat.SetTarget(enemy);
                    Require(combat.TryAttack(), "ataque direto gira até alvo que estava atrás");
                    Require(Vector3.Dot(character.transform.forward, Vector3.back) > .99f,
                        "direção anterior não vence a seleção de alvo");
                    Next(3, .27f); break;
                case 3:
                    Require(enemy.CurrentHealth == enemy.MaxHealth - combat.Stats.AttackDamage,
                        "alvo atrás recebe exatamente um impacto básico");
                    Fixture(new Vector3(500, .04f, 500), new Vector3(500, .06f, 507));
                    combat.SetTarget(enemy); swingsBefore = combat.SwingCount;
                    Require(combat.RequestAttack(0) && combat.HasQueuedAttack, "novo alvo distante gera aproximação");
                    combat.CancelRequestedAttack();
                    Require(!combat.HasQueuedAttack && combat.Target == enemy, "cancelar caminhada mantém seleção sem ataque automático");
                    Next(4, .3f); break;
                case 4:
                    Require(combat.SwingCount == swingsBefore, "intenção cancelada não dispara golpe atrasado");
                    combat.RequestAttack(0); combat.SetTarget(null);
                    Require(!combat.HasQueuedAttack && combat.Target == null, "Escape/deseleção cancela a habilidade pendente");
                    combat.SetTarget(enemy); combat.RequestAttack(0); enemy.gameObject.SetActive(false);
                    Next(5, .1f); break;
                case 5:
                    Require(combat.ValidTarget == null && !combat.HasQueuedAttack,
                        "alvo ausente do mapa limpa seleção e aproximação");
                    enemy.gameObject.SetActive(true);
                    Fixture(new Vector3(500, .04f, 500), new Vector3(500, .06f, 502));
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = new Vector3(500, 1.2f, 501);
                    wall.transform.localScale = new Vector3(2, 3, .2f); Physics.SyncTransforms();
                    combat.SetTarget(enemy); healthBefore = enemy.CurrentHealth;
                    Require(!combat.PrepareTargetedAttack() && !combat.TryAttack(), "alvo atrás de parede não autoriza golpe direcionado");
                    Require(enemy.CurrentHealth == healthBefore && combat.CooldownRemaining == 0f, "parede impede dano e consumo de recarga");
                    UnityEngine.Object.DestroyImmediate(wall); wall = null;
                    combat.SetTarget(null);
                    character.transform.rotation = Quaternion.identity;
                    Require(combat.TryAttack(), "sem seleção permanece disponível a varredura livre em área");
                    Next(6, .27f); break;
                case 6:
                    Require(combat.LastHitCount == 1, "ataque livre acerta o inimigo à frente sem exigir seleção");
                    Fixture(new Vector3(500, .04f, 500), new Vector3(500, .06f, 507));
                    progressionBefore = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    swingsBefore = combat.SwingCount; manaBefore = abilities.Mana;
                    predictedCommand = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, predictedCommand);
                    combat.PresentSubmittedAttack(0, predictedCommand);
                    Require(combat.IsAttacking && combat.HasSubmittedAttackPresentation && combat.SwingCount == swingsBefore + 1,
                        "UUID enviado inicia uma única apresentação imediata");
                    Require(!combat.HasPendingStrike && combat.CooldownRemaining == 0f && abilities.Mana == manaBefore &&
                        JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == progressionBefore,
                        "predição não inventa impacto, recarga, mana, vida ou progresso");
                    combat.ResolveSubmittedAttack(Guid.NewGuid().ToString("D"), false);
                    Require(combat.HasSubmittedAttackPresentation, "rejeição de outro UUID não cancela a apresentação atual");
                    Next(7, PrototypeCombat.AttackPresentationDuration + .05f); break;
                case 7:
                    Require(!combat.IsAttacking && combat.HasSubmittedAttackPresentation,
                        "apresentação expira após a recuperação mas conserva deduplicação para confirmação tardia");
                    Acknowledge(100);
                    Require(combat.SwingCount == swingsBefore + 1 && !combat.HasSubmittedAttackPresentation && !combat.IsAttacking,
                        "evento autoritativo tardio confirma sem repetir animação, som ou rastro");
                    Require(JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == progressionBefore,
                        "confirmação de apresentação preserva todos os atributos/equipamentos/experiência");
                    string nextCommand = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(1, nextCommand);
                    combat.ResolveSubmittedAttack(predictedCommand, false);
                    Require(combat.HasSubmittedAttackPresentation && combat.SwingCount == swingsBefore + 2,
                        "novo UUID e habilidade têm sua própria apresentação e não sofrem rejeição antiga");
                    combat.ResolveSubmittedAttack(nextCommand, false);
                    Require(!combat.IsAttacking && !combat.HasSubmittedAttackPresentation &&
                        !combat.GetComponent<PrototypeAttackEffects>().IsPlaying && !combat.GetComponent<PrototypeAreaEffects>().IsPlaying,
                        "rejeição do UUID correto cancela somente seus efeitos visuais");
                    combat.PresentSubmittedAttack(0, Guid.NewGuid().ToString("D"));
                    combat.ClearSubmittedAttackPresentation();
                    Require(!combat.HasSubmittedAttackPresentation && !combat.IsAttacking, "desconexão limpa a previsão pendente");
                    // The real source can be dead/hidden and still carry the last combat pose when a scheduled boss is born.
                    enemy.gameObject.SetActive(true); enemy.ResetEncounter();
                    enemy.TakeDamage(int.MaxValue, combat);
                    Require(!enemy.IsAlive && !Visual(enemy).gameObject.activeSelf, "origem do modelo pode estar morta quando um novo chefão nasce");
                    Visual(enemy).localRotation = Quaternion.Euler(0, 0, 70);
                    SpawnBossFromSnapshot();
                    PrototypeMonster spawned = null;
                    foreach (PrototypeMonster candidate in combat.Monsters)
                        if (candidate.OnlineId == "pristine-boss-check") spawned = candidate;
                    Require(spawned != null && spawned.IsAlive && spawned.IsBoss && spawned.CurrentHealth == 12000,
                        "snapshot cria chefão vivo a partir do molde preservado, mesmo com original morto");
                    Require(Visual(spawned).gameObject.activeInHierarchy &&
                        Quaternion.Angle(Visual(spawned).localRotation, originalMonsterVisualRotation) < .1f,
                        "novo monstro recebe pose íntegra e corpo visível, sem herdar estado de morte/ataque da origem");
                    Finish(true, checks + " verificações de alvo, aproximação, orientação, cancelamento, parede, mana, área, predição visual e molde prístino."); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static Transform Visual(PrototypeMonster monster) =>
        (Transform)typeof(PrototypeMonster).GetField("visualRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(monster);
    private static void SpawnBossFromSnapshot()
    {
        combat.ApplyOnlineSnapshot(new OnlineSnapshot {
            playerId = "target-check", mapId = "ruins", serverTime = 1001 + Time.time, latestEventId = 100,
            character = combat.Stats.CaptureSaveData(),
            self = new OnlinePlayerState { id = "target-check", health = combat.Stats.Health,
                maxHealth = combat.Stats.MaxHealth, mana = abilities.Mana },
            monsters = new[] { new OnlineMonsterState { id = "pristine-boss-check", name = "Chefão de verificação",
                mapId = "ruins", health = 12000, maxHealth = 12000, damage = 180, experience = 500,
                level = 25, isBoss = true, visualKind = "boss-troll", visualScale = 1.7f, x = 507, z = 506 } },
            events = Array.Empty<OnlineWorldEvent>()
        });
    }
    private static void Acknowledge(long eventId)
    {
        combat.ApplyOnlineSnapshot(new OnlineSnapshot {
            playerId = "target-check", mapId = "ruins", serverTime = 1000 + Time.time, latestEventId = eventId,
            character = combat.Stats.CaptureSaveData(),
            self = new OnlinePlayerState { id = "target-check", health = combat.Stats.Health,
                maxHealth = combat.Stats.MaxHealth, mana = abilities.Mana },
            monsters = Array.Empty<OnlineMonsterState>(),
            events = new [] { new OnlineWorldEvent { eventId = eventId, kind = "attack", sourceId = "target-check", ability = 0 } }
        });
    }
    private static void Fixture(Vector3 playerPosition, Vector3 targetPosition)
    {
        combat.ResetEncounter();
        Move(character.transform, playerPosition); character.transform.rotation = Quaternion.identity;
        Move(enemy.transform, targetPosition); start = playerPosition;
        typeof(PrototypeMonster).GetField("homePosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(enemy, targetPosition);
        typeof(PrototypeMonster).GetField("homeRotation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(enemy, enemy.transform.rotation);
        typeof(PrototypeCharacter).GetField("movementVelocity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(character, Vector3.zero);
        Physics.SyncTransforms();
    }
    private static void Move(Transform item, Vector3 position)
    {
        CharacterController body = item.GetComponent<CharacterController>(); body.enabled = false;
        item.position = position; body.enabled = true;
    }
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Guard(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Require(bool value, string message) { Guard(value, message); checks++; }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
        if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
        if (success) Debug.Log("[TARGET COMBAT PLAY PASS] " + message);
        else Debug.LogError("[TARGET COMBAT PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
