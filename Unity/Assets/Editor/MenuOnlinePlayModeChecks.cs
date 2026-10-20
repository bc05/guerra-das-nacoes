using System;
using System.IO;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Requires a disposable server on 18101 and --online-test --server=http://127.0.0.1:18101.
// Refuses an existing origin directory and removes only the directory created by this run.
[InitializeOnLoad]
public static class MenuOnlinePlayModeChecks
{
    private const string Key = "Guerra.MenuOnline.";
    private const string Endpoint = "http://127.0.0.1:18101";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static PrototypeOnlineClient client;
    private static PrototypeGameMenu menu;
    private static string root, firstId;
    private static int assertions;
    static MenuOnlinePlayModeChecks() { EditorApplication.update += Tick; }

    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("PlayMode já ativo.");
        string directory = OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, Endpoint);
        if (Directory.Exists(directory)) throw new InvalidOperationException("Origem de teste já existe; escolha uma instalação de teste limpa.");
        SessionState.SetString(Key + "Root", directory);
        SessionState.SetBool(Key + "Entered", false); SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 150);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Active", true); EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "Timeout", 0))
                throw new InvalidOperationException("Tempo máximo: " + (client == null ? "sem cliente" : client.Status));
            if (!EditorApplication.isPlaying)
            {
                if (SessionState.GetBool(Key + "Entered", false)) throw new InvalidOperationException("PlayMode encerrou antes do teste.");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 6) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    root = SessionState.GetString(Key + "Root", "");
                    client = UnityEngine.Object.FindFirstObjectByType<PrototypeOnlineClient>();
                    Check(client != null && client.IsOnlineMode && client.ServerUrl == Endpoint, "somente servidor isolado 18101");
                    menu = client.GetComponent<PrototypeGameMenu>();
                    Check(menu.IsFrontEnd && client.IsFrontendIdle && !client.IsConnected, "abertura não conecta automaticamente");
                    Check(!File.Exists(OnlineIdentityStore.GetIdentityPath(root, "principal")), "lista não cria identidade principal");
                    Call("OpenNickname", false); Set("newName", "Aster Teste"); Call("CheckNickname"); Advance(10); break;
                case 10:
                    if (CharacterRosterStore.Load(root).entries.Length != 2) return;
                    Check(!Get<bool>("checkingNickname") && !Get<bool>("creating"), "cadastro concluído após consulta real de disponibilidade");
                    Check(!File.Exists(OnlineIdentityStore.GetIdentityPath(root, menu.SelectedProfile)), "cadastro aguarda botão conectar");
                    Call("ConnectSelected"); Advance(1); break;
                case 1:
                    if (!client.CanPlay || menu.IsOpen) return;
                    firstId = client.Snapshot.playerId;
                    Check(!string.IsNullOrEmpty(firstId) && client.Snapshot.self.displayName == "Aster Teste", "conexão carrega personagem escolhido");
                    Check(client.Snapshot.character.attackPoints == 0 && client.Snapshot.character.unspentPoints == 5, "progressão inicial correta");
                    Check(client.TryAllocate(HeroAttribute.Attack, 2), "distribuição enviada");
                    Check(client.TryUnequip(EquipmentSlot.Sword, out _), "retirada de equipamento enviada");
                    Advance(2); break;
                case 2:
                    if (client.HasPendingMutation || client.Snapshot.character.attackPoints != 2) return;
                    Check(client.Snapshot.character.unspentPoints == 3 && !HasSword(client.Snapshot.character), "servidor confirmou pontos e retirada da espada");
                    Call("LeaveToCharacters"); Advance(3); break;
                case 3:
                    if (!menu.IsFrontEnd || !client.IsFrontendIdle || Preview() == null || !Preview().exists) return;
                    Check(!client.IsConnected && client.Snapshot == null, "saída encerra sessão antes de abrir seleção");
                    Check(Preview().playerId == firstId && Preview().displayName == "Aster Teste", "prévia consulta o mesmo personagem");
                    bool sword = false; foreach (var slot in Preview().equipped) sword |= slot.slot == (int)EquipmentSlot.Sword;
                    Check(!sword, "prévia respeita equipamento retirado");
                    Call("OpenNickname", false); Set("newName", "ASTER TESTE"); Call("CheckNickname"); Advance(9); break;
                case 9:
                    if (Get<bool>("checkingNickname")) return;
                    Check(Get<bool>("creating") && CharacterRosterStore.Load(root).entries.Length == 2, "nick repetido recusado sem criar cartão ou personagem");
                    Check(!string.IsNullOrEmpty(Get<string>("nicknameStatus")), "formulário explica o conflito do nick");
                    Set("newName", "Lyra Teste"); Call("CheckNickname"); Advance(11); break;
                case 11:
                    if (CharacterRosterStore.Load(root).entries.Length != 3) return;
                    Call("ConnectSelected"); Advance(4); break;
                case 4:
                    if (!client.CanPlay || menu.IsOpen) return;
                    Check(client.Snapshot.playerId != firstId && client.Snapshot.self.displayName == "Lyra Teste", "segundo personagem possui identidade distinta");
                    Check(client.Snapshot.character.attackPoints == 0 && client.Snapshot.character.unspentPoints == 5 && HasSword(client.Snapshot.character), "segundo personagem não herda atributos ou equipamento do primeiro");
                    Call("LeaveToCharacters"); Advance(5); break;
                case 5:
                    if (!menu.IsFrontEnd || !client.IsFrontendIdle) return;
                    CharacterRoster roster = CharacterRosterStore.Load(root);
                    Check(roster.entries.Length == 3, "lista mantém os dois personagens criados");
                    typeof(PrototypeGameMenu).GetMethod("SelectCharacter", Flags).Invoke(menu, new object[] { roster.entries[1] });
                    Advance(6); break;
                case 6:
                    if (Preview() == null || !Preview().exists) return;
                    Check(Preview().playerId == firstId, "selecionar novamente consulta a identidade original");
                    Call("ConnectSelected"); Advance(7); break;
                case 7:
                    if (!client.CanPlay || menu.IsOpen) return;
                    Check(client.Snapshot.playerId == firstId && client.Snapshot.self.displayName == "Aster Teste", "reconexão preserva identidade e nome");
                    Check(client.Snapshot.character.attackPoints == 2 && client.Snapshot.character.unspentPoints == 3 && !HasSword(client.Snapshot.character), "reconexão preserva pontos e equipamento");
                    Check(client.Snapshot.character.itemInstances.Length == 12 && client.Snapshot.character.inventoryItemIds.Length == 1, "mochila e posse dos doze itens preservadas");
                    Call("LeaveToCharacters"); Advance(8); break;
                case 8:
                    if (!menu.IsFrontEnd || !client.IsFrontendIdle) return;
                    Check(client.RemotePlayerCount == 0 && !client.CanPlay, "seleção encerra presença e bloqueia combate");
                    Finish(true, assertions + " verificações: abrir sem login, criar, conectar, distribuir pontos, retirar equipamento, trocar e retomar personagem em servidor isolado."); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static OnlineCharacterPreview Preview() => typeof(PrototypeGameMenu).GetField("preview", Flags).GetValue(menu) as OnlineCharacterPreview;
    private static bool HasSword(CharacterSaveData value) { foreach (var slot in value.equipped) if (slot.slot == (int)EquipmentSlot.Sword) return true; return false; }
    private static void Call(string name, params object[] args) => typeof(PrototypeGameMenu).GetMethod(name, Flags).Invoke(menu, args);
    private static T Get<T>(string name) => (T)typeof(PrototypeGameMenu).GetField(name, Flags).GetValue(menu);
    private static void Set(string name, object value) => typeof(PrototypeGameMenu).GetField(name, Flags).SetValue(menu, value);
    private static void Advance(int stage) => SessionState.SetInt(Key + "Stage", stage);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); assertions++; }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (!string.IsNullOrEmpty(root) && root == SessionState.GetString(Key + "Root", "") && Directory.Exists(root)) Directory.Delete(root, true);
        if (success) Debug.Log("[MENU ONLINE PASS] " + message); else Debug.LogError("[MENU ONLINE FAIL] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1); else EditorApplication.ExitPlaymode();
    }
}
