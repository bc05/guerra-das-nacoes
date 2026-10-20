using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;

// Run against an isolated, empty server on port 18090. The primary entity uses the real
// game client; the second connection uses the wire API, without constructing another local hero.
[InitializeOnLoad]
public static class OnlineClientPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.OnlineClientChecks.";
    private const string Endpoint = "http://127.0.0.1:18090";
    private static PrototypeOnlineClient client;
    private static PrototypeCombat combat;
    private static PrototypeCharacter character;
    private static UnityWebRequest secondRequest;
    private static OnlineSnapshot secondSnapshot;
    private static string secondToken, primaryId, profile;
    private static float nextAt;
    private static int[] hitHealth;
    private static int expectedHeavyDamage;
    private static int swingsBeforeReconnect;
    private static long strikeEventFloor;
    private static int strikesBeforeResume;
    private static float nextMoveAt;

    static OnlineClientPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar cliente online e dois jogadores")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie fora do PlayMode; use servidor isolado em 127.0.0.1:18090.");
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetString(Key + "Profile", "check-" + Guid.NewGuid().ToString("N").Substring(0, 20));
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 80f);
        SessionState.SetBool(Key + "Active", true);
        nextAt = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0),
                "timeout no estágio " + SessionState.GetInt(Key + "Stage", 0) + "; " + (client != null ? client.Status : "sem cliente"));
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
                    Require(combat != null, "personagem da cena existe");
                    character = combat.GetComponent<PrototypeCharacter>();
                    client = combat.GetComponent<PrototypeOnlineClient>();
                    Require(client != null, "cliente online configurado na cena");
                    character.enabled = false;
                    profile = SessionState.GetString(Key + "Profile", "");
                    client.ConfigureForTests(true, Endpoint, profile, "Verificação principal");
                    Require(!client.CanPlay && PrototypeHud.BlocksGameplay, "carregamento bloqueia controle sem progresso local");
                    Advance(1); break;
                case 1:
                    if (!client.CanPlay) return;
                    primaryId = client.Snapshot.playerId;
                    Require(combat.Stats.Level == 1 && combat.Stats.Experience == 0 && combat.Stats.AttackPoints == 0 &&
                        combat.Stats.UnspentPoints == 5 && CountItems(client.Snapshot.character) == 12,
                        "primeira conexão carrega nível, XP, pontos e 12 itens canônicos");
                    Require(client.TryAllocate(HeroAttribute.Attack, 2), "distribuição enviada ao servidor");
                    Require(combat.Stats.AttackPoints == 0 && client.HasPendingMutation,
                        "distribuição não altera personagem antes da confirmação");
                    Advance(2); break;
                case 2:
                    if (client.HasPendingMutation || combat.Stats.AttackPoints != 2) return;
                    Require(combat.Stats.UnspentPoints == 3, "servidor debita exatamente dois pontos");
                    Require(combat.HasMessage && combat.Message == "Alteração confirmada e salva no servidor.",
                        "confirmação da distribuição também atualiza a mensagem exibida pelo painel");
                    Require(client.TryUnequip(EquipmentSlot.Shield, out _), "remoção de escudo possuído enviada ao servidor");
                    Require(combat.Stats.Equipment.GetEquipped(EquipmentSlot.Shield) != null, "remoção aguarda confirmação real");
                    Advance(3); break;
                case 3:
                    if (client.HasPendingMutation || combat.Stats.Equipment.GetEquipped(EquipmentSlot.Shield) != null) return;
                    Require(combat.Stats.EquippedWeapon.Id == "training-sword" && combat.Stats.GetBasicDamage() == 19, "combate mantém espada possuída e usa atributos confirmados");
                    Require(combat.HasMessage && combat.Message == "Alteração confirmada e salva no servidor.",
                        "painel substitui a espera pela confirmação da troca");
                    Require(client.Snapshot.saveRevision > 0 && client.Snapshot.savedAtUnixMs > 0, "confirmação inclui revisão persistida");
                    client.ConfigureForTests(true, Endpoint, profile, "Verificação principal");
                    Require(!client.CanPlay, "reabrir bloqueia até recarregar o servidor");
                    Advance(4); break;
                case 4:
                    if (!client.CanPlay) return;
                    Require(client.Snapshot.playerId == primaryId && combat.Stats.AttackPoints == 2 && combat.Stats.UnspentPoints == 3 &&
                        combat.Stats.EquippedWeapon.Id == "training-sword" && combat.Stats.Equipment.GetEquipped(EquipmentSlot.Shield) == null && combat.Stats.Level == 1 && combat.Stats.Experience == 0 &&
                        CountItems(client.Snapshot.character) == 12, "mesma identidade restaura personagem e patrimônio sem duplicar itens");
                    byte[] secret = new byte[32];
                    using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(secret);
                    string secondKey = Convert.ToBase64String(secret);
                    Array.Clear(secret, 0, secret.Length);
                    BeginSecond("/v1/connect", JsonUtility.ToJson(new OnlineConnectRequest {
                        deviceKey = secondKey, displayName = "Verificação secundária" }), null);
                    Advance(5); break;
                case 5:
                    if (!SecondComplete()) return;
                    secondSnapshot = ReadSecond();
                    secondToken = secondSnapshot.sessionToken;
                    Require(!string.IsNullOrEmpty(secondToken) && secondSnapshot.playerId != primaryId,
                        "segunda identidade cria outro jogador sem substituir o primeiro");
                    Require(secondSnapshot.character.attackPoints == 0 && secondSnapshot.character.unspentPoints == 5 &&
                        secondSnapshot.character.equipped.Any(item => item.itemId == "training-sword"),
                        "segundo patrimônio é independente");
                    Require(secondSnapshot.players.Any(player => player.id == primaryId), "segundo jogador enxerga o primeiro no mundo");
                    Advance(6); break;
                case 6:
                    PrototypeRemotePlayer[] remotes = UnityEngine.Object.FindObjectsByType<PrototypeRemotePlayer>(FindObjectsSortMode.None);
                    // Earlier isolated checks may leave other players visible for the server's
                    // presence timeout. Wait for this check's own second identity to reach the
                    // real primary poll before inspecting its visual clone.
                    PrototypeRemotePlayer secondRemote = remotes.FirstOrDefault(remote => remote.PlayerId == secondSnapshot.playerId);
                    if (secondRemote == null) return;
                    Require(secondRemote.EquipmentVisual != null && secondRemote.EquipmentVisual.IsConfigured &&
                        remotes.All(remote => !remote.HasGameplayComponents),
                        "outro jogador tem modelo remoto sem combate local nem colisores");
                    Require(client.Snapshot.monsters.Select(monster => monster.health).SequenceEqual(
                        secondSnapshot.monsters.Select(monster => monster.health)), "os dois recebem a mesma vida dos monstros compartilhados");
                    nextMoveAt = Time.time;
                    Advance(7); break;
                case 7:
                    if (Time.time < nextMoveAt) return;
                    nextMoveAt = Time.time + .15f;
                    float z = character.transform.position.z;
                    if (z < 2.99f)
                    {
                        CharacterController controller = character.GetComponent<CharacterController>();
                        bool enabled = controller.enabled;
                        controller.enabled = false;
                        character.transform.SetPositionAndRotation(new Vector3(0f, character.transform.position.y, Mathf.Min(3f, z + .2f)), Quaternion.identity);
                        controller.enabled = enabled;
                        return;
                    }
                    if (client.Snapshot.self.z < 2.9f) return;
                    expectedHeavyDamage = combat.Stats.GetSkillDamage();
                    Require(client.TryUseAbility(1), "pesado enviado com posição confirmada");
                    Advance(8); break;
                case 8:
                    if (client.HasPendingMutation || !client.Snapshot.monsters.Any(monster => monster.health < monster.maxHealth)) return;
                    Require(client.Snapshot.monsters.Count(monster => IsCourtyardTroll(monster.id)) == 3 &&
                        client.Snapshot.monsters.Where(monster => IsCourtyardTroll(monster.id)).All(monster => monster.health == monster.maxHealth - expectedHeavyDamage),
                        "servidor resolve ataque em área nos três trolls próximos com espada e atributos reais");
                    hitHealth = client.Snapshot.monsters.Select(monster => monster.health).ToArray();
                    BeginSecond("/v1/commands", JsonUtility.ToJson(new OnlineCommandBatch {
                        afterEventId = secondSnapshot.latestEventId, commands = Array.Empty<OnlineCommand>() }), secondToken);
                    Advance(9); break;
                case 9:
                    if (!SecondComplete()) return;
                    secondSnapshot = ReadSecond();
                    Require(secondSnapshot.monsters.Select(monster => monster.health).SequenceEqual(hitHealth),
                        "outro jogador recebe o dano compartilhado sem cálculo ou alvo local");
                    Require(secondSnapshot.character.experience == 0 && secondSnapshot.character.attackPoints == 0,
                        "ataque do primeiro não concede XP ou atributos indevidos ao segundo");
                    swingsBeforeReconnect = combat.SwingCount;
                    FieldInfo eventCursor = typeof(PrototypeCombat).GetField("lastOnlineEventId", BindingFlags.Instance | BindingFlags.NonPublic);
                    Require(eventCursor != null, "cursor de apresentação disponível para simular abertura de um processo novo");
                    eventCursor.SetValue(combat, -1L);
                    client.enabled = false;
                    Require(!client.CanPlay && PrototypeHud.BlocksGameplay && !combat.TryAttack() &&
                        !combat.GetComponent<PrototypeAbilities>().TryUse(2), "perder conexão bloqueia ações sem fallback offline");
                    client.enabled = true;
                    client.Reconnect();
                    Advance(10); break;
                case 10:
                    if (!client.CanPlay) return;
                    Require(client.Snapshot.playerId == primaryId && combat.Stats.AttackPoints == 2 &&
                        combat.Stats.EquippedWeapon.Id == "training-sword" && combat.Stats.Equipment.GetEquipped(EquipmentSlot.Shield) == null, "reconexão explícita mantém a identidade e o progresso confirmado");
                    Require(combat.SwingCount == swingsBeforeReconnect,
                        "abertura com cursor novo não reproduz golpes históricos enviados pelo connect");
                    CheckRejectedMovementReconciliation();
                    // The connect payload includes bounded history, intentionally suppressed by the client.
                    // Wait for a new server event rather than mistaking that history for a live strike.
                    strikeEventFloor = client.Snapshot.latestEventId;
                    strikesBeforeResume = combat.Monsters.Sum(enemy => enemy.AttackEffects.PresentedStrikes);
                    Advance(11);
                    break;
                case 11:
                    if (client.Snapshot.events == null || !client.Snapshot.events.Any(item => item.kind == "monsterStrike" && item.eventId > strikeEventFloor)) return;
                    Require(combat.Monsters.Sum(enemy => enemy.AttackEffects.PresentedStrikes) > strikesBeforeResume,
                        "golpe real do servidor inicia a apresentação do monstro");
                    int presented = combat.Monsters.Sum(enemy => enemy.AttackEffects.PresentedStrikes);
                    combat.ApplyOnlineSnapshot(client.Snapshot);
                    Require(combat.Monsters.Sum(enemy => enemy.AttackEffects.PresentedStrikes) == presented,
                        "snapshot repetido não duplica impacto nem animação");
                    CheckTransportPresentationRecovery();
                    Finish(true, "Carga/salvamento, 12 itens, dois jogadores, ataque em área, golpe autoritativo do troll, impacto sem duplicação e retomada sem reproduzir sons/animações históricos.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static int CountItems(CharacterSaveData state) => state.inventoryItemIds.Length + state.equipped.Length;
    private static void CheckTransportPresentationRecovery()
    {
        int swings = combat.SwingCount;
        int strikes = combat.Monsters.Sum(enemy => enemy.AttackEffects.PresentedStrikes);
        PropertyInfo connection = typeof(PrototypeOnlineClient).GetProperty("IsConnected");
        MethodInfo apply = typeof(PrototypeOnlineClient).GetMethod("ApplySnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo cursor = typeof(PrototypeCombat).GetField("lastOnlineEventId", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(connection != null && apply != null && cursor != null, "pontos de retomada disponíveis");
        connection.GetSetMethod(true).Invoke(client, new object[] { false });
        cursor.SetValue(combat, -1L);
        try
        {
            Require(!client.CanPlay, "falha de transporte bloqueia controle");
            apply.Invoke(client, new object[] { client.Snapshot, false, Array.Empty<OnlineCommand>() });
            Require(combat.SwingCount == swings && combat.Monsters.Sum(enemy => enemy.AttackEffects.PresentedStrikes) == strikes,
                "primeira resposta após falha restaura estado sem efeitos históricos");
        }
        finally { connection.GetSetMethod(true).Invoke(client, new object[] { true }); }
    }
    private static void CheckRejectedMovementReconciliation()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo rejectedMove = typeof(PrototypeOnlineClient).GetMethod("HasRejectedMovement", flags);
        Require(rejectedMove != null, "resultado de movimento pode ser cruzado com o lote enviado");
        OnlineCommand[] commands = { new OnlineCommand { commandId = "move-test", type = "move" },
            new OnlineCommand { commandId = "equip-test", type = "equip" } };
        Require(!(bool)rejectedMove.Invoke(null, new object[] { commands, new[] {
            new OnlineCommandResult { commandId = "equip-test", accepted = false } } }),
            "recusa de equipamento não força movimento");
        Require((bool)rejectedMove.Invoke(null, new object[] { commands, new[] {
            new OnlineCommandResult { commandId = "move-test", accepted = false } } }),
            "recusa do movimento enviado exige posição autoritativa");
        MethodInfo reconcile = typeof(PrototypeOnlineClient).GetMethod("ReconcilePosition", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(reconcile != null, "reconciliação de posição disponível");
        OnlinePlayerState authority = client.Snapshot.self;
        CharacterController controller = client.GetComponent<CharacterController>();
        bool enabled = controller.enabled;
        controller.enabled = false;
        client.transform.position = new Vector3(authority.x + .2f, client.transform.position.y, authority.z);
        controller.enabled = enabled;
        reconcile.Invoke(client, new object[] { authority, false, false });
        Require(Mathf.Abs(client.transform.position.x - authority.x - .2f) < .001f,
            "movimento normal preserva tolerância de meio metro");
        reconcile.Invoke(client, new object[] { authority, false, true });
        Require(Mathf.Abs(client.transform.position.x - authority.x) < .001f &&
            Mathf.Abs(client.transform.position.z - authority.z) < .001f,
            "movimento recusado corrige até desvio pequeno sem alterar o mundo no servidor");
    }
    private static void BeginSecond(string path, string json, string token)
    {
        secondRequest = new UnityWebRequest(Endpoint + path, UnityWebRequest.kHttpVerbPOST) {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)), downloadHandler = new DownloadHandlerBuffer(),
            timeout = 5, redirectLimit = 0
        };
        secondRequest.SetRequestHeader("Content-Type", "application/json");
        if (token != null) secondRequest.SetRequestHeader("Authorization", "Bearer " + token);
        secondRequest.SendWebRequest();
    }
    private static bool IsCourtyardTroll(string id) => id == "troll-0" || id == "troll-1" || id == "troll-2";
    private static bool SecondComplete() => secondRequest != null && secondRequest.isDone;
    private static OnlineSnapshot ReadSecond()
    {
        try
        {
            Require(secondRequest.result == UnityWebRequest.Result.Success, "requisição secundária falhou: HTTP " + secondRequest.responseCode);
            OnlineSnapshot result = JsonUtility.FromJson<OnlineSnapshot>(secondRequest.downloadHandler.text);
            Require(result != null && result.character != null && result.players != null && result.monsters != null,
                "resposta secundária completa");
            return result;
        }
        finally { secondRequest.Dispose(); secondRequest = null; }
    }
    private static void Advance(int stage)
    {
        SessionState.SetInt(Key + "Stage", stage);
        nextAt = Time.time + .02f;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Finish(bool passed, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (secondRequest != null) { secondRequest.Abort(); secondRequest.Dispose(); secondRequest = null; }
        if (client != null) client.enabled = false;
        if (!string.IsNullOrEmpty(profile))
        {
            string root = OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, Endpoint);
            string path = OnlineIdentityStore.GetIdentityPath(root, profile);
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path), true);
        }
        if (passed) Debug.Log("[ONLINE CLIENT PLAY PASS] " + message);
        else Debug.LogError("[ONLINE CLIENT PLAY FAIL] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }
}
