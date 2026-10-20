using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;

// Uses only a randomly named profile on the already-running isolated test server.
// It never opens the principal credentials or the primary port/database.
[InitializeOnLoad]
public static class EquipmentOnlinePlayModeChecks
{
    private const string Key = "GuerraDasNacoes.EquipmentOnlineChecks.";
    private const string Endpoint = "http://127.0.0.1:18090";
    private static readonly EquipmentSlot[] VisibleSlots = {
        EquipmentSlot.Head, EquipmentSlot.Shoulders, EquipmentSlot.Chest,
        EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet,
        EquipmentSlot.Sword, EquipmentSlot.Shield,
        EquipmentSlot.Belt, EquipmentSlot.Necklace, EquipmentSlot.RingLeft, EquipmentSlot.RingRight
    };
    private static PrototypeCombat combat;
    private static PrototypeOnlineClient client;
    private static PrototypeEquipmentAppearance appearance;
    private static UnityWebRequest request;
    private static OnlineSnapshot secondary;
    private static string profile, primaryId, secondaryId, secondaryKey, secondaryToken, previousResultId;
    private static float nextAt;

    static EquipmentOnlinePlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar equipamentos online e visuais remotos")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie fora do PlayMode; use servidor isolado em 127.0.0.1:18090.");
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetString(Key + "Profile", "gear-v12-" + Guid.NewGuid().ToString("N").Substring(0, 20));
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
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0),
                "timeout no estágio " + SessionState.GetInt(Key + "Stage", 0) + "; " + (client != null ? client.Status : "sem cliente"));
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antes da validação");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
                    Require(combat != null, "personagem existe");
                    appearance = combat.GetComponent<PrototypeEquipmentAppearance>();
                    client = combat.GetComponent<PrototypeOnlineClient>();
                    combat.GetComponent<PrototypeCharacter>().enabled = false;
                    profile = SessionState.GetString(Key + "Profile", "");
                    client.ConfigureForTests(true, Endpoint, profile, "Equipamento teste A");
                    Require(!client.CanPlay && PrototypeHud.BlocksGameplay, "conexão aguarda estado canônico");
                    Advance(1); break;
                case 1:
                    if (!client.CanPlay) return;
                    primaryId = client.Snapshot.playerId;
                    Require(VisibleSlots.All(LocalVisible) && !appearance.BaseHeadVisible, "set inicial confirmado está no modelo local");
                    Require(CountItems(client.Snapshot.character) == 12, "12 itens iniciais locais");
                    byte[] key = new byte[32];
                    using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(key);
                    secondaryKey = Convert.ToBase64String(key); Array.Clear(key, 0, key.Length);
                    Begin("/v1/connect", JsonUtility.ToJson(new OnlineConnectRequest { deviceKey = secondaryKey, displayName = "Equipamento teste B" }), null);
                    Advance(2); break;
                case 2:
                    if (!Complete()) return;
                    secondary = Read(); secondaryToken = secondary.sessionToken; secondaryId = secondary.playerId;
                    Require(secondaryId != primaryId && CountItems(secondary.character) == 12, "segundo jogador tem identidade e patrimônio independentes");
                    Begin("/v1/commands", JsonUtility.ToJson(new OnlineCommandBatch { afterEventId = secondary.latestEventId,
                        commands = VisibleSlots.Select(slot => new OnlineCommand { commandId = Guid.NewGuid().ToString("D"), type = "unequip", slot = (int)slot }).ToArray() }), secondaryToken);
                    Advance(3); break;
                case 3:
                    if (!Complete()) return;
                    secondary = Read();
                    Require(secondary.results.Length == VisibleSlots.Length && secondary.results.All(result => result.accepted), "servidor confirmou doze remoções do segundo jogador");
                    Require(VisibleSlots.All(slot => secondary.character.equipped.All(piece => piece.slot != (int)slot)) && CountItems(secondary.character) == 12,
                        "servidor guardou as doze peças na mochila do segundo jogador");
                    Advance(4); break;
                case 4:
                    PrototypeRemotePlayer remote = FindSecondary();
                    if (remote == null || client.Snapshot.players.FirstOrDefault(player => player.id == secondaryId)?.equipped.Length != 0) return;
                    Require(remote.EquipmentVisual != null && !remote.HasGameplayComponents, "segundo jogador é visual remoto sem lógica local");
                    Require(VisibleSlots.All(slot => !RemoteVisible(remote, slot)) && remote.EquipmentVisual.BaseBodyVisible && remote.EquipmentVisual.BaseHeadVisible,
                        "remoções reais do segundo jogador apagam armadura e armas remotas preservando corpo e rosto");
                    foreach (EquipmentSlot slot in VisibleSlots) Require(client.TryUnequip(slot, out _), "enviar remoção local " + slot);
                    Require(client.HasPendingMutation && VisibleSlots.All(LocalVisible) && VisibleSlots.All(slot => combat.Stats.Equipment.GetEquipped(slot) != null),
                        "modelo e atributos locais aguardam confirmação do servidor");
                    Advance(5); break;
                case 5:
                    if (client.HasPendingMutation) return;
                    Advance(12); break; // Let the presentation's LateUpdate observe the confirmed state.
                case 12:
                    Require(VisibleSlots.All(slot => !LocalVisible(slot)) && appearance.BaseBodyVisible && appearance.BaseHeadVisible,
                        "confirmação real remove seis armaduras, armas e acessórios locais");
                    Require(CountItems(client.Snapshot.character) == 12, "remoções mantêm os 12 itens");
                    BeginPoll(); Advance(6); break;
                case 6:
                    if (!Complete()) return;
                    secondary = Read();
                    OnlinePlayerState first = secondary.players.Single(player => player.id == primaryId);
                    Require(VisibleSlots.All(slot => first.equipped.All(piece => piece.slot != (int)slot)), "outro jogador recebe o estado público vazio do primeiro");
                    previousResultId = client.LastEquipmentResult != null ? client.LastEquipmentResult.commandId : null;
                    Require(client.TryEquip("veteran-sword", EquipmentSlot.Sword, out _), "solicitação de item não possuído pode ser avaliada pelo servidor");
                    Require(!appearance.SwordVisible && combat.Stats.Equipment.GetEquipped(EquipmentSlot.Sword) == null,
                        "enviar troca não antecipa arma no modelo");
                    Advance(7); break;
                case 7:
                    if (client.HasPendingMutation || client.LastEquipmentResult == null || client.LastEquipmentResult.commandId == previousResultId) return;
                    Require(!client.LastEquipmentResult.accepted && !appearance.SwordVisible && combat.Stats.Equipment.GetEquipped(EquipmentSlot.Sword) == null,
                        "recusa de item não possuído mantém espaço e modelo vazios sem preview");
                    Require(CountItems(client.Snapshot.character) == 12, "recusa não remove nem duplica itens");
                    client.ConfigureForTests(true, Endpoint, profile, "Equipamento teste A");
                    Require(!client.CanPlay, "reabertura aguarda recarga canônica");
                    Advance(8); break;
                case 8:
                    if (!client.CanPlay) return;
                    Require(client.Snapshot.playerId == primaryId && VisibleSlots.All(slot => !LocalVisible(slot)) &&
                        appearance.BaseBodyVisible && appearance.BaseHeadVisible && CountItems(client.Snapshot.character) == 12,
                        "reabrir a mesma identidade preserva remoções salvas sem restaurar treino");
                    PrototypeRemotePlayer reloadedRemote = FindSecondary();
                    if (reloadedRemote == null) return;
                    Require(VisibleSlots.All(slot => !RemoteVisible(reloadedRemote, slot)), "reconexão recria remoto vazio sem herdar o set do clone");
                    Begin("/v1/connect", JsonUtility.ToJson(new OnlineConnectRequest { deviceKey = secondaryKey, displayName = "Equipamento teste B" }), null);
                    Advance(9); break;
                case 9:
                    if (!Complete()) return;
                    secondary = Read(); secondaryToken = secondary.sessionToken;
                    Require(secondary.playerId == secondaryId && CountItems(secondary.character) == 12 &&
                        VisibleSlots.All(slot => secondary.character.equipped.All(piece => piece.slot != (int)slot)), "reabrir segundo jogador preserva suas próprias remoções");
                    foreach (EquipmentSlot slot in VisibleSlots)
                        Require(client.TryEquip("training-" + slot.ToString().ToLowerInvariant(), slot, out _), "enviar reequipar local " + slot);
                    Require(VisibleSlots.All(slot => !LocalVisible(slot)), "reequipar também aguarda confirmação");
                    Advance(10); break;
                case 10:
                    if (client.HasPendingMutation) return;
                    Advance(13); break;
                case 13:
                    Require(VisibleSlots.All(LocalVisible) && !appearance.BaseBodyVisible && !appearance.BaseHeadVisible,
                        "reequipar confirmado restaura todas as peças locais e cobre base sem clipping");
                    Require(CountItems(client.Snapshot.character) == 12, "reequipar preserva 12 itens únicos");
                    BeginPoll(); Advance(11); break;
                case 11:
                    if (!Complete()) return;
                    secondary = Read();
                    OnlinePlayerState equipped = secondary.players.Single(player => player.id == primaryId);
                    Require(VisibleSlots.All(slot => equipped.equipped.Any(piece => piece.slot == (int)slot)), "segundo cliente recebe as doze peças reequipadas");
                    Finish(true, "Dois jogadores reais; doze peças locais/remotas; confirmação antes do visual; recusa sem preview; 12 itens preservados; reconexão sem restaurar peças removidas; reequipar propagado.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static bool LocalVisible(EquipmentSlot slot) => slot == EquipmentSlot.Sword ? appearance.SwordVisible :
        slot == EquipmentSlot.Shield ? appearance.ShieldVisible : PrototypeEquipmentVisual.AccessorySlots.Contains(slot) ?
        appearance.IsAccessoryVisible(slot) : appearance.IsArmourVisible(slot);
    private static bool RemoteVisible(PrototypeRemotePlayer remote, EquipmentSlot slot) => slot == EquipmentSlot.Sword ? remote.EquipmentVisual.SwordVisible :
        slot == EquipmentSlot.Shield ? remote.EquipmentVisual.ShieldVisible : PrototypeEquipmentVisual.AccessorySlots.Contains(slot) ?
        remote.EquipmentVisual.IsAccessoryVisible(slot) : remote.EquipmentVisual.IsArmourVisible(slot);
    private static PrototypeRemotePlayer FindSecondary() => UnityEngine.Object.FindObjectsByType<PrototypeRemotePlayer>(FindObjectsSortMode.None).FirstOrDefault(remote => remote.PlayerId == secondaryId);
    private static int CountItems(CharacterSaveData data)
    {
        string[] items = data.inventoryItemIds.Concat(data.equipped.Select(piece => piece.itemId)).ToArray();
        Require(items.Distinct().Count() == items.Length, "itens não duplicados"); return items.Length;
    }
    private static void BeginPoll() => Begin("/v1/commands", JsonUtility.ToJson(new OnlineCommandBatch {
        afterEventId = secondary.latestEventId, commands = Array.Empty<OnlineCommand>() }), secondaryToken);
    private static void Begin(string path, string json, string token)
    {
        request = new UnityWebRequest(Endpoint + path, UnityWebRequest.kHttpVerbPOST) {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)), downloadHandler = new DownloadHandlerBuffer(), timeout = 5, redirectLimit = 0 };
        request.SetRequestHeader("Content-Type", "application/json");
        if (token != null) request.SetRequestHeader("Authorization", "Bearer " + token);
        request.SendWebRequest();
    }
    private static bool Complete() => request != null && request.isDone;
    private static OnlineSnapshot Read()
    {
        try
        {
            Require(request.result == UnityWebRequest.Result.Success, "requisição isolada falhou: HTTP " + request.responseCode);
            OnlineSnapshot snapshot = JsonUtility.FromJson<OnlineSnapshot>(request.downloadHandler.text);
            Require(snapshot != null && snapshot.character != null && snapshot.players != null, "snapshot completo"); return snapshot;
        }
        finally { request.Dispose(); request = null; }
    }
    private static void Advance(int stage) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + .06f; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (request != null) { request.Abort(); request.Dispose(); request = null; }
        if (client != null) client.enabled = false;
        if (!string.IsNullOrEmpty(profile))
        {
            string path = OnlineIdentityStore.GetIdentityPath(Application.persistentDataPath, profile);
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path), true);
        }
        secondaryKey = secondaryToken = null;
        if (success) Debug.Log("[EQUIPMENT ONLINE PLAY PASS] " + message); else Debug.LogError("[EQUIPMENT ONLINE PLAY FAIL] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1); else EditorApplication.ExitPlaymode();
    }
}
