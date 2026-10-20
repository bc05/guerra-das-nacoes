using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Uses the actual client, movement controller and authoritative server. Run only against
// the isolated test server on 18090; no principal identity or production database is used.
[InitializeOnLoad]
public static class ManualCombatOnlinePlayModeChecks
{
    private const string Key = "GuerraDasNacoes.ManualCombatOnlineChecks.";
    private const string Endpoint = "http://127.0.0.1:18090";
    private static readonly HashSet<long> observedEvents = new HashSet<long>();
    private static readonly HashSet<string> observedCommands = new HashSet<string>();
    private static readonly Dictionary<string, int> hitCounts = new Dictionary<string, int>();
    private static PrototypeOnlineClient client;
    private static PrototypeCombat combat;
    private static PrototypeCharacter character;
    private static PrototypeMonster firstTarget, secondTarget;
    private static FieldInfo pendingField;
    private static string profile, playerId;
    private static long eventFloor;
    private static int attacks, hits, initialSwings, initialHealth;
    private static int requestedAttacks, observedAttackCommands;
    private static int settledAttacks, settledHits, settledSwings, settledCommands;
    private static Vector3 initialPosition, settledPosition;
    private static float phaseUntil, nextAt;
    private static string requestedTargetId, rejectedCommandId;
    private static bool rejectionConfirmed;

    static ManualCombatOnlinePlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar combate manual online")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie fora do PlayMode com servidor isolado em 127.0.0.1:18090.");
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        // The profile and nickname are both unique and fit the server's 24-character nickname limit.
        SessionState.SetString(Key + "Profile", "mc-" + Guid.NewGuid().ToString("N").Substring(0, 16));
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 90f);
        SessionState.SetBool(Key + "Active", true);
        nextAt = 0f;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0f),
                "timeout no estágio " + SessionState.GetInt(Key + "Stage", 0) + "; " +
                (client != null ? client.Status : "sem cliente"));
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            if (client != null && !string.IsNullOrEmpty(playerId)) ObserveClient();
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    InitializeClient();
                    Advance(1);
                    break;
                case 1:
                    if (!client.CanPlay) return;
                    Require(client.Snapshot != null && client.Snapshot.self != null, "snapshot inicial completo");
                    playerId = client.Snapshot.playerId;
                    Require(client.Snapshot.self.health > 0 && combat.Stats.EquippedWeapon != null,
                        "personagem temporário vivo e com espada");
                    firstTarget = combat.Monsters.FirstOrDefault(enemy => enemy != null && enemy.OnlineId == "troll-0");
                    secondTarget = combat.Monsters.FirstOrDefault(enemy => enemy != null && enemy.OnlineId == "troll-2");
                    Require(firstTarget != null && secondTarget != null && firstTarget.IsAvailableForTargeting &&
                        secondTarget.IsAvailableForTargeting, "dois trolls originais disponíveis no servidor isolado");
                    eventFloor = client.Snapshot.latestEventId;
                    initialHealth = FindMonster(firstTarget.OnlineId).health;
                    initialPosition = character.transform.position;
                    initialSwings = combat.SwingCount;
                    Require(Vector3.ProjectOnPlane(firstTarget.transform.position - initialPosition, Vector3.up).magnitude >
                        combat.AttackRange + .25f, "alvo começa fora do alcance do básico");
                    Require(character.enabled, "Character.Update permanece habilitado durante a validação");
                    Require(character.SelectAttackTarget(firstTarget) && combat.Target == firstTarget,
                        "clique no handler real seleciona o monstro sem solicitar um golpe");
                    BeginQuietPeriod();
                    Advance(2);
                    break;
                case 2:
                    RequireQuiet(firstTarget, "selecionar um inimigo distante");
                    Require(attacks == 0 && hits == 0 && combat.SwingCount == initialSwings &&
                        FindMonster(firstTarget.OnlineId).health == initialHealth,
                        "seleção não envia comando de ataque, animação ou dano ao servidor");
                    if (Time.time < phaseUntil) return;
                    RequestOneAttack(firstTarget);
                    Advance(3);
                    break;
                case 3:
                    Require(client.CanPlay && combat.IsAlive, "conexão e personagem permanecem ativos durante a aproximação manual");
                    if (!SingleAttackConfirmed(1, firstTarget, 1)) return;
                    Require(Vector3.ProjectOnPlane(character.transform.position - initialPosition, Vector3.up).magnitude > .75f,
                        "somente o pedido manual faz o controlador real se aproximar");
                    Require(FindMonster(firstTarget.OnlineId).health == initialHealth - combat.Stats.GetBasicDamage(),
                        "o primeiro pedido aplica exatamente um impacto básico canônico ao alvo");
                    CheckRepeatedSnapshot();
                    BeginQuietPeriod();
                    Advance(4);
                    break;
                case 4:
                    RequireQuiet(firstTarget, "um único pedido manual concluído");
                    if (Time.time < phaseUntil) return;
                    RequestOneAttack(firstTarget);
                    Advance(5);
                    break;
                case 5:
                    if (attacks < 2) return;
                    Require(combat.CooldownRemaining > 0f, "troca é exercitada durante recarga de um ataque confirmado");
                    Require(character.SelectAttackTarget(secondTarget) && combat.Target == secondTarget &&
                        !combat.HasQueuedAttack && !character.HasDestination,
                        "trocar seleção durante recarga cancela fila sem iniciar outro ataque");
                    settledPosition = character.transform.position;
                    Advance(6);
                    break;
                case 6:
                    RequireSelectionOnly(secondTarget);
                    if (!SingleAttackConfirmed(2, firstTarget, 2)) return;
                    BeginQuietPeriod();
                    Advance(7);
                    break;
                case 7:
                    RequireQuiet(secondTarget, "trocar a seleção durante o golpe anterior");
                    if (Time.time < phaseUntil) return;
                    RequestOneAttack(secondTarget);
                    Advance(8);
                    break;
                case 8:
                    Require(client.CanPlay && combat.IsAlive, "novo acionamento manual mantém conexão e personagem ativos");
                    if (!SingleAttackConfirmed(3, secondTarget, 1)) return;
                    CheckRepeatedSnapshot();
                    BeginQuietPeriod();
                    Advance(9);
                    break;
                case 9:
                    RequireQuiet(secondTarget, "um pedido manual ao novo alvo");
                    if (Time.time < phaseUntil) return;
                    // Deliberately bypass the targeting helper only for this negative fixture.
                    // The real transport assigns the UUID and the real server rejects the absent target.
                    Require(client.Submit(new OnlineCommand { type = "ability", ability = 0,
                        targetId = "manual-check-missing-target" }), "comando inválido de fixture é enviado ao servidor isolado");
                    OnlineCommand rejected = PendingCommands().Single(command => command.type == "ability");
                    rejectedCommandId = rejected.commandId;
                    Require(combat.HasSubmittedAttackPresentation, "pedido ainda não confirmado apresenta apenas o gesto local");
                    Require(character.SelectAttackTarget(firstTarget) && combat.Target == firstTarget,
                        "seleção mais recente é aceita enquanto a rejeição anterior está em voo");
                    settledPosition = character.transform.position;
                    Advance(10);
                    break;
                case 10:
                    RequireSelectionOnly(firstTarget);
                    Require(attacks == 3, "pedido recusado não cria ataque canônico");
                    if (!rejectionConfirmed || client.HasPendingMutation) return;
                    Require(!combat.HasSubmittedAttackPresentation && !combat.IsAttacking,
                        "recusa encerra a apresentação pendente e devolve o personagem à guarda");
                    Require(combat.SwingCount == initialSwings + 4,
                        "três golpes aceitos e uma apresentação recusada não são duplicados pelos ACKs");
                    BeginQuietPeriod();
                    Advance(11);
                    break;
                case 11:
                    RequireQuiet(firstTarget, "recusa de comando após uma seleção mais recente");
                    if (Time.time < phaseUntil) return;
                    CheckRepeatedSnapshot();
                    combat.ResolveSubmittedAttack(rejectedCommandId, false);
                    combat.ResolveSubmittedAttack(rejectedCommandId, false);
                    RequireQuiet(firstTarget, "repetição da rejeição já conciliada");
                    Finish(true, "Selecionar não move nem envia golpes; cada um dos três pedidos explícitos confirma um ataque e impacto; esperas superiores a duas recargas não repetem; troca durante recarga preserva o golpe comprometido; ACK/snapshot repetidos e recusa real não duplicam nem reativam ataques. Perfil temporário isolado.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void InitializeClient()
    {
        observedEvents.Clear();
        observedCommands.Clear();
        hitCounts.Clear();
        attacks = hits = requestedAttacks = observedAttackCommands = 0;
        playerId = requestedTargetId = rejectedCommandId = null;
        rejectionConfirmed = false;
        combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
        Require(combat != null, "personagem real da cena existe");
        character = combat.GetComponent<PrototypeCharacter>();
        client = combat.GetComponent<PrototypeOnlineClient>();
        Require(character != null && client != null, "controle de movimento e cliente real configurados");
        pendingField = typeof(PrototypeOnlineClient).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(pendingField != null, "fila pode ser observada sem substituir o transporte");
        character.enabled = true;
        profile = SessionState.GetString(Key + "Profile", "");
        client.ConfigureForTests(true, Endpoint, profile, profile);
        Require(!client.CanPlay, "conexão aguarda progresso canônico antes de jogar");
    }

    private static void RequestOneAttack(PrototypeMonster target)
    {
        Require(combat.Target == target && combat.CanAttack && !client.HasPendingMutation,
            "cada acionamento explícito começa pronto e com o alvo selecionado");
        requestedAttacks++;
        requestedTargetId = target.OnlineId;
        Require(combat.RequestAttack(0), "pedido manual único aceito para o alvo selecionado");
        ObserveClient();
    }

    private static bool SingleAttackConfirmed(int expected, PrototypeMonster target, int targetHits)
    {
        Require(attacks <= expected, "um pedido não pode confirmar mais de um ataque");
        if (attacks < expected || HitsOn(target.OnlineId) < targetHits || client.HasPendingMutation) return false;
        Require(HitsOn(target.OnlineId) == targetHits && combat.SwingCount == initialSwings + expected &&
            !combat.HasQueuedAttack && !combat.HasSubmittedAttackPresentation,
            "um comando confirmado aplica um impacto e uma apresentação sem armar repetição");
        return true;
    }

    private static void BeginQuietPeriod()
    {
        settledAttacks = attacks;
        settledHits = hits;
        settledSwings = combat.SwingCount;
        settledCommands = observedAttackCommands;
        settledPosition = character.transform.position;
        phaseUntil = Time.time + 2f * combat.AttackInterval + .25f;
    }

    private static void RequireSelectionOnly(PrototypeMonster target)
    {
        Require(client.CanPlay && combat.IsAlive && combat.Target == target && !combat.HasQueuedAttack &&
            !character.HasDestination, "seleção permanece sem aproximação ou intenção de golpe; " + StateDetails());
        Require(Vector3.ProjectOnPlane(character.transform.position - settledPosition, Vector3.up).magnitude < .08f,
            "seleção não desloca o personagem até o monstro");
    }

    private static void RequireQuiet(PrototypeMonster target, string cause)
    {
        RequireSelectionOnly(target);
        Require(attacks == settledAttacks && hits == settledHits && combat.SwingCount == settledSwings &&
            observedAttackCommands == settledCommands && !PendingCommands().Any(command => command.type == "ability"),
            cause + " não cria outro comando, golpe, dano ou animação sem novo acionamento");
    }

    private static void CheckRepeatedSnapshot()
    {
        int before = combat.SwingCount;
        combat.ApplyOnlineSnapshot(client.Snapshot);
        combat.ApplyOnlineSnapshot(client.Snapshot);
        Require(combat.SwingCount == before && !combat.HasQueuedAttack,
            "snapshot/ACK repetidos não reproduzem a animação nem criam intenção manual");
    }

    private static void ObserveClient()
    {
        OnlineSnapshot snapshot = client.Snapshot;
        if (snapshot == null) return;
        OnlineCommand[] pending = PendingCommands();
        Require(pending.Count(command => command.type == "ability") <= 1,
            "combate manual mantém no máximo um golpe pendente");
        foreach (OnlineCommand command in pending)
        {
            if (command.type != "ability") continue;
            Require(command.ability == 0, "fixture usa apenas os básicos explicitamente solicitados");
            if (command.commandId == rejectedCommandId || !observedCommands.Add(command.commandId)) continue;
            observedAttackCommands++;
            Require(command.targetId == requestedTargetId && observedAttackCommands <= requestedAttacks,
                "cada comando novo corresponde ao alvo de um acionamento manual explícito");
        }
        if (snapshot.results != null)
            foreach (OnlineCommandResult result in snapshot.results)
                if (result.commandId == rejectedCommandId)
                {
                    Require(!result.accepted, "servidor recusa o alvo inexistente da fixture negativa");
                    rejectionConfirmed = true;
                }
                else if (observedCommands.Contains(result.commandId))
                    Require(result.accepted, "básico enviado foi recusado pelo servidor: " + result.message);
        if (snapshot.events == null) return;
        foreach (OnlineWorldEvent value in snapshot.events)
        {
            if (value.eventId <= eventFloor || value.sourceId != playerId || !observedEvents.Add(value.eventId)) continue;
            if (value.kind == "attack")
            {
                Require(value.ability == 0, "somente básicos manuais são confirmados na fixture");
                attacks++;
                Require(attacks <= requestedAttacks, "servidor não confirma ataques sem um novo acionamento do jogador");
            }
            if (value.kind == "hit" && value.amount > 0)
            {
                hits++;
                hitCounts[value.targetId] = HitsOn(value.targetId) + 1;
            }
        }
    }

    private static int HitsOn(string id) => hitCounts.TryGetValue(id, out int count) ? count : 0;
    private static OnlineMonsterState FindMonster(string id) => client.Snapshot.monsters.First(monster => monster.id == id);
    private static OnlineCommand[] PendingCommands() => ((List<OnlineCommand>)pendingField.GetValue(client)).ToArray();
    private static void Advance(int stage)
    {
        SessionState.SetInt(Key + "Stage", stage);
        nextAt = Time.time + .01f;
        Debug.Log("[MANUAL COMBAT ONLINE STAGE] " + stage + "; " + StateDetails());
    }
    private static string StateDetails()
    {
        if (client == null || combat == null) return "cliente ainda não inicializado";
        OnlineSnapshot snapshot = client.Snapshot;
        string target = combat.Target != null ? combat.Target.OnlineId : "nenhum";
        float distance = combat.Target == null ? -1f : Vector3.ProjectOnPlane(
            combat.Target.transform.position - combat.transform.position, Vector3.up).magnitude;
        return "CanPlay=" + client.CanPlay + "; IsAlive=" + combat.IsAlive +
            "; HP=" + combat.Stats.Health + "/" + combat.Stats.MaxHealth +
            "; serverHP=" + (snapshot != null && snapshot.self != null ? snapshot.self.health.ToString() : "sem snapshot") +
            "; queued=" + combat.HasQueuedAttack + "; target=" + target + "; requested=" + requestedTargetId +
            "; distance=" + distance.ToString("F2") +
            "; cooldown=" + combat.CooldownRemaining.ToString("F2") +
            "; requests=" + requestedAttacks + "; commands=" + observedAttackCommands +
            "; attacks=" + attacks + "; hits=" + hits + "; swings=" + combat.SwingCount +
            "; status=" + client.Status + "; message=" + combat.Message;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(bool passed, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (combat != null) combat.CancelRequestedAttack();
        if (character != null) character.enabled = false;
        if (client != null) client.enabled = false;
        string ownedProfile = string.IsNullOrEmpty(profile) ? SessionState.GetString(Key + "Profile", "") : profile;
        if (!string.IsNullOrEmpty(ownedProfile))
        {
            string root = OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, Endpoint);
            string directory = Path.GetDirectoryName(OnlineIdentityStore.GetIdentityPath(root, ownedProfile));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        if (passed) Debug.Log("[MANUAL COMBAT ONLINE PLAY PASS] " + message);
        else Debug.LogError("[MANUAL COMBAT ONLINE PLAY FAIL] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }
}
