using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GuerraDasNacoes
{
    // One serialized request stream. Persistent commands remain queued, with the same UUID,
    // until an authoritative result arrives. No local character file is used as a fallback.
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PrototypeCombat))]
    public sealed class PrototypeOnlineClient : MonoBehaviour
    {
        private const double PollSeconds = .1d;
        private const int MaximumQueuedCommands = 64;
        private const int CommandsPerBatch = 31; // Leave one place for the most recent movement.
        private readonly List<OnlineCommand> pending = new List<OnlineCommand>();
        private readonly Dictionary<string, PrototypeRemotePlayer> remotePlayers = new Dictionary<string, PrototypeRemotePlayer>();
        private readonly HashSet<string> seenPlayers = new HashSet<string>();
        private PrototypeCombat combat;
        private PrototypeCharacter character;
        private Coroutine connectionLoop;
        private UnityWebRequest activeRequest;
        private OnlineIdentity identity;
        private string serverUrl = "http://127.0.0.1:8088";
        private string profile = "principal";
        private string displayName = "Guerreiro";
        private string token;
        private bool manualReconnectRequired;
        private bool permanentConfigurationError;
        private bool frontendIdle = true;
        private bool leavingToMenu;
        private bool allowInsecureLan;
        private bool autoConnect;
        private string catalogIdentityRoot;
        private double snapshotReceivedAt;
        private double snapshotServerTime;
        private double nextPollAt;
        private long afterEventId;
        private bool hasPosition;
        private Vector3 lastSentPosition;
        private float lastSentYaw;
        private double lastMovementSentAt;
        private float lastRoundTripMilliseconds;
        private string actionMessage;
        private double actionMessageUntil;
        private GUIStyle overlayStyle;
        private GUIStyle titleStyle;

        public static PrototypeOnlineClient Active { get; private set; }
        public bool IsOnlineMode { get; private set; }
        public bool IsConnected { get; private set; }
        public bool CanPlay => !IsOnlineMode || (!frontendIdle && !leavingToMenu && IsConnected && Snapshot != null && !manualReconnectRequired);
        public bool IsConnecting => !frontendIdle && !IsConnected && connectionLoop != null && !manualReconnectRequired;
        public bool IsFrontendIdle => IsOnlineMode && frontendIdle;
        public bool HasPendingMutation => pending.Count != 0;
        public string Status { get; private set; } = "Preparando conexão…";
        public OnlineSnapshot Snapshot { get; private set; }
        public OnlineCommandResult LastEquipmentResult { get; private set; }
        public OnlineCommandResult LastEconomyResult { get; private set; }
        public string LastConnectionErrorCode { get; private set; }
        public string MapId => Snapshot == null || string.IsNullOrEmpty(Snapshot.mapId) ? "ruins" : Snapshot.mapId;
        public double ServerNow => snapshotServerTime + Math.Max(0d, Time.realtimeSinceStartupAsDouble - snapshotReceivedAt);
        public double Remaining(double until) => Math.Max(0d, until - ServerNow);
        public int RemotePlayerCount => remotePlayers.Count;
        public string Profile => profile;
        public string ServerUrl => serverUrl;
        public string DisplayName => displayName;

        private void Awake()
        {
            if (Active != null && Active != this) { enabled = false; return; }
            Active = this;
            Application.runInBackground = true;
            combat = GetComponent<PrototypeCombat>();
            character = GetComponent<PrototypeCharacter>();
            if (GetComponent<PrototypeCity>() == null) gameObject.AddComponent<PrototypeCity>();
            if (GetComponent<PrototypeEconomyUi>() == null) gameObject.AddComponent<PrototypeEconomyUi>();
            string[] arguments = Environment.GetCommandLineArgs();
            IsOnlineMode = !Application.isEditor || HasArgument(arguments, "-online-test") || HasArgument(arguments, "--online-test");
            if (HasArgument(arguments, "-offline") || HasArgument(arguments, "--offline")) IsOnlineMode = false;
            try
            {
                profile = ArgumentValue(arguments, "--profile", "principal");
                displayName = ArgumentValue(arguments, "--name", "Guerreiro");
                allowInsecureLan = HasArgument(arguments, "--allow-insecure-lan");
                autoConnect = HasArgument(arguments, "--auto-connect");
                serverUrl = ValidateServer(ArgumentValue(arguments, "--server", serverUrl),
                    allowInsecureLan);
                OnlineIdentityStore.ValidateProfile(profile);
            }
            catch (Exception error) { SetConfigurationError(error.Message); }
            if (!IsOnlineMode) Status = "TESTE OFFLINE · progresso descartado ao sair";
            PrototypeGraphicsSettings.GetOrCreate();
            if (GetComponent<PrototypeGameMenu>() == null) gameObject.AddComponent<PrototypeGameMenu>();
        }

        private void Start()
        {
            if (IsOnlineMode && !permanentConfigurationError && autoConnect)
            { frontendIdle = false; BeginConnection(); }
            else if (IsOnlineMode && !permanentConfigurationError) Status = "Escolha um personagem para conectar.";
        }

        // Explicit opt-in for integration checks. Existing batch PlayMode checks stay offline.
        public void ConfigureForTests(bool online, string endpoint = "http://127.0.0.1:8088",
            string profileName = "online-check", string playerName = "Verificação")
        {
            StopNetwork();
            pending.Clear();
            Snapshot = null;
            LastEquipmentResult = null;
            LastConnectionErrorCode = null;
            token = null;
            identity = null;
            afterEventId = 0;
            hasPosition = false;
            IsConnected = false;
            IsOnlineMode = online;
            frontendIdle = false;
            leavingToMenu = false;
            permanentConfigurationError = false;
            manualReconnectRequired = false;
            try
            {
                serverUrl = ValidateServer(endpoint, false);
                OnlineIdentityStore.ValidateProfile(profileName);
                profile = profileName;
                displayName = playerName;
            }
            catch (Exception error) { SetConfigurationError(error.Message); }
            PrototypeGameMenu menu = GetComponent<PrototypeGameMenu>();
            if (menu != null) menu.ConfigureForTests(false);
            if (online && !permanentConfigurationError && isActiveAndEnabled) BeginConnection();
            else if (!online) Status = "TESTE OFFLINE · progresso descartado ao sair";
        }

        public void Reconnect()
        {
            if (!IsOnlineMode || frontendIdle || leavingToMenu || permanentConfigurationError || connectionLoop != null) return;
            manualReconnectRequired = false;
            token = null;
            IsConnected = false;
            BeginConnection();
        }

        public bool StartCharacter(string profileName, string playerName, string endpoint, out string reason)
        {
            reason = null;
            if (!IsOnlineMode) { reason = "O modo de teste offline está ativo."; return false; }
            if (!frontendIdle && !IsConnected && connectionLoop == null && Snapshot == null && !leavingToMenu)
                ResetToFrontend(); // A refused opening request has no active character to leave.
            if (!frontendIdle || IsConnected || connectionLoop != null || leavingToMenu)
            { reason = "Volte à seleção antes de conectar outro personagem."; return false; }
            try
            {
                OnlineIdentityStore.ValidateProfile(profileName);
                // Saved names can predate current nickname rules. The authoritative connect
                // path preserves them; new characters are validated strictly by the server.
                string validatedName = (playerName ?? "").Trim();
                if (validatedName.Length < 1 || validatedName.Length > CharacterNickname.MaximumLength)
                    throw new ArgumentException("Informe o nick do personagem para conectar.");
                string validatedServer = ValidateServer(endpoint, allowInsecureLan);
                ActivateCatalog(validatedServer);
                profile = profileName; displayName = validatedName; serverUrl = validatedServer;
                permanentConfigurationError = false; manualReconnectRequired = false;
                token = null; identity = null; Snapshot = null;
                LastEquipmentResult = null; LastEconomyResult = null;
                LastConnectionErrorCode = null;
                pending.Clear(); afterEventId = 0; hasPosition = false; nextPollAt = 0;
                frontendIdle = false;
                BeginConnection();
                if (permanentConfigurationError)
                { frontendIdle = true; reason = Status; return false; }
                return true;
            }
            catch (Exception error) when (error is ArgumentException || error is System.IO.IOException)
            { reason = error.Message; return false; }
        }

        public void RequestCharacterPreview(string profileName, string endpoint, Action<OnlineCharacterPreview, string> callback)
        {
            string address;
            OnlineIdentity existing;
            try
            {
                address = ValidateServer(endpoint, allowInsecureLan);
                if (!frontendIdle || Snapshot != null)
                    throw new ArgumentException("Volte à seleção para consultar personagens de outro servidor.");
                ActivateCatalog(address);
                if (!OnlineIdentityStore.TryLoadExisting(OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, address),
                    profileName, out existing))
                { callback?.Invoke(new OnlineCharacterPreview(), null); return; }
            }
            catch (Exception error) when (error is ArgumentException || error is System.IO.IOException || error is UnauthorizedAccessException)
            { callback?.Invoke(null, error.Message); return; }
            StartCoroutine(PreviewCharacter(address, catalogIdentityRoot, existing, callback));
        }

        public void RequestNicknameAvailability(string profileName, string endpoint, string nickname,
            Action<OnlineNicknameAvailability, string> callback)
        {
            string address;
            string normalized;
            OnlineIdentity existing = null;
            try
            {
                if (IsConnected || Snapshot != null || connectionLoop != null)
                    throw new ArgumentException("Volte à seleção para escolher um nick.");
                address = ValidateServer(endpoint, allowInsecureLan);
                normalized = CharacterNickname.Normalize(nickname);
                if (!string.IsNullOrEmpty(profileName))
                    OnlineIdentityStore.TryLoadExisting(OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, address),
                        profileName, out existing);
            }
            catch (Exception error) when (error is ArgumentException || error is System.IO.IOException || error is UnauthorizedAccessException)
            { callback?.Invoke(null, error.Message); return; }
            StartCoroutine(CheckNickname(address, normalized, existing, callback));
        }

        private IEnumerator CheckNickname(string endpoint, string nickname, OnlineIdentity credential,
            Action<OnlineNicknameAvailability, string> callback)
        {
            using (UnityWebRequest request = new UnityWebRequest(endpoint + "/v1/characters/nickname-check", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                    new OnlineNicknameCheckRequest { nickname = nickname, deviceKey = credential == null ? null : credential.deviceKey })));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 5; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(null, ReadServerError(request.downloadHandler.text,
                        "Não foi possível verificar o nick. Confira o servidor e tente novamente."));
                    yield break;
                }
                OnlineNicknameAvailability result = null;
                string error = null;
                try
                {
                    result = JsonUtility.FromJson<OnlineNicknameAvailability>(request.downloadHandler.text);
                    if (result == null || string.IsNullOrWhiteSpace(result.nickname)) throw new FormatException();
                }
                catch (Exception) { error = "O servidor retornou uma consulta de nick incompatível."; }
                callback?.Invoke(result, error);
            }
        }

        private void ActivateCatalog(string endpoint)
        {
            string root = OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, endpoint);
            if (catalogIdentityRoot == root) return;
            if (Snapshot != null || IsConnected)
                throw new ArgumentException("Encerre a sessão atual antes de trocar de servidor.");
            EquipmentCatalog.ResetPublishedDefinitions();
            catalogIdentityRoot = root;
        }

        private IEnumerator PreviewCharacter(string endpoint, string authorityRoot, OnlineIdentity credential, Action<OnlineCharacterPreview, string> callback)
        {
            using (UnityWebRequest request = new UnityWebRequest(endpoint + "/v1/characters/preview", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                    new OnlineConnectRequest { deviceKey = credential.deviceKey })));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 5; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                if (catalogIdentityRoot != authorityRoot || !frontendIdle)
                { callback?.Invoke(null, "A seleção de servidor mudou; a prévia anterior foi descartada."); yield break; }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(null, ReadServerError(request.downloadHandler.text,
                        "Não foi possível consultar este personagem. Confira o servidor e tente novamente."));
                    yield break;
                }
                OnlineCharacterPreview preview = null;
                string error = null;
                try
                {
                    preview = JsonUtility.FromJson<OnlineCharacterPreview>(request.downloadHandler.text);
                    if (preview == null || preview.protocolVersion != 2 || (preview.exists &&
                        (string.IsNullOrWhiteSpace(preview.playerId) || preview.level < 1 || preview.equipped == null)))
                        throw new FormatException();
                    if (preview.itemDefinitions != null)
                        foreach (EconomyItemConfig definition in preview.itemDefinitions)
                            if (!EquipmentCatalog.TryRegisterDefinition(definition, out string reason))
                                throw new FormatException(reason);
                }
                catch (Exception) { error = "O servidor retornou uma prévia incompatível. Seu progresso foi preservado."; }
                callback?.Invoke(preview, error);
            }
        }

        public void TryLeaveToMenu(Action<bool, string> callback)
        {
            if (!IsOnlineMode || frontendIdle) { callback?.Invoke(true, null); return; }
            if (leavingToMenu) { callback?.Invoke(false, "Aguarde a saída em andamento."); return; }
            if (HasPendingMutation) { callback?.Invoke(false, "Aguarde a confirmação das ações pendentes antes de sair."); return; }
            if (!IsConnected || string.IsNullOrEmpty(token))
            {
                // Interrupted connection has no pending mutation; do not reconnect simply to open the menu.
                StopNetwork(); ResetToFrontend(); callback?.Invoke(true, null); return;
            }
            leavingToMenu = true;
            StartCoroutine(LeaveToMenu(callback));
        }

        private IEnumerator LeaveToMenu(Action<bool, string> callback)
        {
            // Finish an in-flight poll so a movement acknowledgement cannot race session closure.
            while (activeRequest != null) yield return null;
            using (UnityWebRequest request = new UnityWebRequest(serverUrl + "/v1/disconnect", UnityWebRequest.kHttpVerbPOST))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.timeout = 5; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                bool success = false;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { success = JsonUtility.FromJson<OnlineDisconnectResult>(request.downloadHandler.text)?.disconnected == true; }
                    catch (ArgumentException) { }
                }
                if (!success)
                {
                    leavingToMenu = false;
                    callback?.Invoke(false, ReadServerError(request.downloadHandler.text,
                        "Não foi possível confirmar a saída. Tente novamente; seu progresso continua preservado."));
                    yield break;
                }
            }
            StopNetwork(); ResetToFrontend(); callback?.Invoke(true, null);
        }

        private void ResetToFrontend()
        {
            if (combat != null) combat.ClearSubmittedAttackPresentation();
            foreach (PrototypeRemotePlayer remote in remotePlayers.Values) if (remote != null) Destroy(remote.gameObject);
            remotePlayers.Clear(); seenPlayers.Clear();
            pending.Clear(); token = null; identity = null; Snapshot = null;
            LastEquipmentResult = null; LastEconomyResult = null;
            LastConnectionErrorCode = null;
            IsConnected = false; manualReconnectRequired = false; permanentConfigurationError = false;
            frontendIdle = true; leavingToMenu = false; afterEventId = 0; hasPosition = false;
            Status = "Escolha um personagem para conectar.";
        }

        private void BeginConnection()
        {
            if (connectionLoop != null || permanentConfigurationError) return;
            try
            {
                ActivateCatalog(serverUrl);
                identity = identity ?? OnlineIdentityStore.LoadOrCreate(
                    OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, serverUrl), profile);
            }
            catch (Exception error)
            {
                SetConfigurationError(error is System.IO.InvalidDataException ? error.Message :
                    "Não foi possível ler ou gravar a identidade local. Confira o acesso à pasta Online do jogo.");
                return;
            }
            connectionLoop = StartCoroutine(NetworkLoop());
        }

        private IEnumerator NetworkLoop()
        {
            int failures = 0;
            while (isActiveAndEnabled && IsOnlineMode && !manualReconnectRequired)
            {
                if (leavingToMenu) { yield return null; continue; }
                bool connecting = string.IsNullOrEmpty(token);
                if (Time.realtimeSinceStartupAsDouble < nextPollAt) { yield return null; continue; }
                string path;
                string json;
                OnlineCommand[] commands = null;
                if (connecting)
                {
                    Status = "Conectando ao servidor e carregando personagem…";
                    path = "/v1/connect";
                    json = JsonUtility.ToJson(new OnlineConnectRequest { deviceKey = identity.deviceKey,
                        displayName = displayName, preserveExistingName = true });
                }
                else
                {
                    path = "/v1/commands";
                    commands = CaptureBatch();
                    json = JsonUtility.ToJson(new OnlineCommandBatch { afterEventId = afterEventId, commands = commands });
                }

                double requestStarted = Time.realtimeSinceStartupAsDouble;
                using (UnityWebRequest request = new UnityWebRequest(serverUrl + path, UnityWebRequest.kHttpVerbPOST))
                {
                    activeRequest = request;
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept", "application/json");
                    if (!connecting) request.SetRequestHeader("Authorization", "Bearer " + token);
                    request.timeout = 5;
                    // Credentials must stay on the explicitly configured origin.
                    request.redirectLimit = 0;
                    yield return request.SendWebRequest();
                    activeRequest = null;
                    if (!isActiveAndEnabled) break;
                    if (request.responseCode == 401)
                    {
                        if (connecting) LastConnectionErrorCode = ReadServerErrorCode(request.downloadHandler.text);
                        IsConnected = false;
                        token = null;
                        manualReconnectRequired = true;
                        Status = "Sessão encerrada ou substituída. Clique em Reconectar para continuar com este personagem.";
                        break;
                    }
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        IsConnected = false;
                        failures++;
                        if (request.responseCode >= 300 && request.responseCode < 500)
                        {
                            if (connecting) LastConnectionErrorCode = ReadServerErrorCode(request.downloadHandler.text);
                            manualReconnectRequired = true;
                            Status = ReadServerError(request.downloadHandler.text,
                                "O servidor recusou a conexão. Confira a versão do jogo e a configuração do servidor.");
                            break;
                        }
                        Status = "Servidor indisponível. Seu progresso confirmado continua salvo; tentando reconectar…";
                        nextPollAt = Time.realtimeSinceStartupAsDouble + Math.Min(8d, Math.Pow(2d, Math.Min(failures - 1, 3)));
                        continue;
                    }
                    OnlineSnapshot snapshot = null;
                    string invalidSnapshot = null;
                    try
                    {
                        snapshot = JsonUtility.FromJson<OnlineSnapshot>(request.downloadHandler.text);
                        if (snapshot == null || snapshot.protocolVersion != 2 || snapshot.character == null ||
                            snapshot.self == null || string.IsNullOrEmpty(snapshot.playerId) ||
                            (connecting && string.IsNullOrEmpty(snapshot.sessionToken)))
                            throw new FormatException();
                        if (double.IsNaN(snapshot.serverTime) || double.IsInfinity(snapshot.serverTime)) throw new FormatException();
                        if (Snapshot != null && snapshot.saveRevision < Snapshot.saveRevision) throw new FormatException();
                        if (connecting) token = snapshot.sessionToken;
                        ApplySnapshot(snapshot, connecting, commands);
                    }
                    catch (Exception)
                    {
                        invalidSnapshot = "O servidor enviou um progresso incompatível. O jogo interrompeu a sessão para preservar seus dados.";
                    }
                    if (invalidSnapshot != null)
                    {
                        IsConnected = false;
                        manualReconnectRequired = true;
                        Status = invalidSnapshot;
                        break;
                    }
                    failures = 0;
                    IsConnected = true;
                    lastRoundTripMilliseconds = (float)((Time.realtimeSinceStartupAsDouble - requestStarted) * 1000d);
                    Status = "Online · progresso confirmado pelo servidor";
                    nextPollAt = Math.Max(requestStarted + PollSeconds, Time.realtimeSinceStartupAsDouble);
                }
            }
            activeRequest = null;
            connectionLoop = null;
        }

        private OnlineCommand[] CaptureBatch()
        {
            List<OnlineCommand> batch = new List<OnlineCommand>(32);
            // Never preserve queued positions while disconnected: movement always represents this frame.
            if (CanPlay && Snapshot.self.health > 0)
            {
                Vector3 position = transform.position;
                float yaw = transform.eulerAngles.y;
                bool changed = !hasPosition || (position - lastSentPosition).sqrMagnitude > .0001f ||
                    Mathf.Abs(Mathf.DeltaAngle(lastSentYaw, yaw)) > .1f;
                if (changed || Time.realtimeSinceStartupAsDouble - lastMovementSentAt > .5d)
                {
                    batch.Add(new OnlineCommand { commandId = Guid.NewGuid().ToString("D"), type = "move",
                        x = position.x, z = position.z, yaw = yaw,
                        running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) });
                    lastSentPosition = position;
                    lastSentYaw = yaw;
                    lastMovementSentAt = Time.realtimeSinceStartupAsDouble;
                    hasPosition = true;
                }
            }
            for (int index = 0; index < pending.Count && index < CommandsPerBatch; index++) batch.Add(pending[index]);
            return batch.ToArray();
        }

        private void ApplySnapshot(OnlineSnapshot snapshot, bool connecting, OnlineCommand[] sentCommands)
        {
            // Validate the progression before acknowledging queued commands or exposing a usable snapshot.
            if (snapshot.itemDefinitions != null)
                foreach (EconomyItemConfig definition in snapshot.itemDefinitions)
                    if (!EquipmentCatalog.TryRegisterDefinition(definition, out string reason))
                        throw new FormatException(reason);
            PlayerProgression.FromSaveData(snapshot.character);
            snapshotServerTime = snapshot.serverTime;
            snapshotReceivedAt = Time.realtimeSinceStartupAsDouble;
            // Opening and transport recovery restore current state silently. The bounded history
            // may contain old attacks, so it must not replay animations or sounds after a gap.
            bool restoringPresentation = connecting || !IsConnected;
            bool changedMap = Snapshot != null && Snapshot.mapId != snapshot.mapId;
            if ((restoringPresentation || changedMap) && combat != null) combat.ClearSubmittedAttackPresentation();
            PrototypeCity city = GetComponent<PrototypeCity>();
            if (city != null) city.ApplyMap(snapshot.mapId);
            combat.ApplyOnlineSnapshot(restoringPresentation || changedMap ? WithoutHistoricalEvents(snapshot) : snapshot);
            Snapshot = snapshot;
            ReconcilePosition(snapshot.self, connecting || changedMap, HasRejectedMovement(sentCommands, snapshot.results));
            ProcessResults(snapshot.results);
            SynchronizeRemotes(snapshot, restoringPresentation);
            // World-event IDs can restart with the server process; character revisions cannot.
            afterEventId = restoringPresentation ? snapshot.latestEventId : Math.Max(afterEventId, snapshot.latestEventId);
        }

        private static OnlineSnapshot WithoutHistoricalEvents(OnlineSnapshot snapshot)
        {
            return new OnlineSnapshot {
                protocolVersion = snapshot.protocolVersion, playerId = snapshot.playerId, sessionToken = snapshot.sessionToken,
                serverTime = snapshot.serverTime, saveRevision = snapshot.saveRevision, savedAtUnixMs = snapshot.savedAtUnixMs,
                latestEventId = snapshot.latestEventId, character = snapshot.character, self = snapshot.self,
                players = snapshot.players, monsters = snapshot.monsters, results = snapshot.results,
                mapId = snapshot.mapId, npcs = snapshot.npcs, portals = snapshot.portals, loot = snapshot.loot,
                economyVersion = snapshot.economyVersion, itemDefinitions = snapshot.itemDefinitions,
                bossWindows = snapshot.bossWindows,
                events = Array.Empty<OnlineWorldEvent>()
            };
        }

        private static bool HasRejectedMovement(OnlineCommand[] commands, OnlineCommandResult[] results)
        {
            if (commands == null || results == null) return false;
            foreach (OnlineCommand command in commands)
            {
                if (command == null || command.type != "move") continue;
                foreach (OnlineCommandResult result in results)
                    if (result != null && !result.accepted && result.commandId == command.commandId) return true;
            }
            return false;
        }

        private void ReconcilePosition(OnlinePlayerState self, bool initial, bool rejectedMovement)
        {
            Vector3 authority = new Vector3(self.x, transform.position.y, self.z);
            if (!initial && !rejectedMovement && (authority - transform.position).sqrMagnitude <= .25f) return;
            CharacterController controller = GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            transform.position = authority;
            if (initial || rejectedMovement) transform.rotation = Quaternion.Euler(0f, self.yaw, 0f);
            if (wasEnabled) controller.enabled = true;
            lastSentPosition = authority;
            lastSentYaw = transform.eulerAngles.y;
            hasPosition = false;
        }

        private void ProcessResults(OnlineCommandResult[] results)
        {
            if (results == null) return;
            foreach (OnlineCommandResult result in results)
            {
                if (result == null) continue;
                int index = pending.FindIndex(command => command.commandId == result.commandId);
                if (index < 0) continue;
                OnlineCommand command = pending[index];
                pending.RemoveAt(index);
                if (command.type == "ability" && (command.ability == 0 || command.ability == 1))
                    combat.ResolveSubmittedAttack(command.commandId, result.accepted);
                if (command.type == "equip" || command.type == "unequip") LastEquipmentResult = result;
                if (command.type == "buy" || command.type == "sell" || command.type == "collect" ||
                    command.type == "portal" || command.type == "potion") LastEconomyResult = result;
                if (!result.accepted) SetActionMessage(string.IsNullOrWhiteSpace(result.message) ? "Ação recusada pelo servidor." : result.message);
                else if (command.type == "allocate" || command.type == "equip" || command.type == "unequip")
                {
                    SetActionMessage("Alteração confirmada e salva no servidor.");
                    if (string.IsNullOrWhiteSpace(result.message)) combat.Notify("Alteração confirmada e salva no servidor.");
                }
            }
        }

        private void SynchronizeRemotes(OnlineSnapshot snapshot, bool initial)
        {
            seenPlayers.Clear();
            if (snapshot.players != null && character != null && character.VisualRoot != null)
            {
                foreach (OnlinePlayerState state in snapshot.players)
                {
                    if (state == null || string.IsNullOrEmpty(state.id) || state.id == snapshot.playerId || state.mapId != snapshot.mapId) continue;
                    seenPlayers.Add(state.id);
                    if (!remotePlayers.TryGetValue(state.id, out PrototypeRemotePlayer remote))
                    {
                        remote = PrototypeRemotePlayer.Create(character, state.id);
                        remotePlayers.Add(state.id, remote);
                    }
                    remote.ApplyState(state, ServerNow);
                }
            }
            List<string> removed = null;
            foreach (KeyValuePair<string, PrototypeRemotePlayer> pair in remotePlayers)
                if (!seenPlayers.Contains(pair.Key))
                {
                    if (pair.Value != null) Destroy(pair.Value.gameObject);
                    if (removed == null) removed = new List<string>();
                    removed.Add(pair.Key);
                }
            if (removed != null) foreach (string id in removed) remotePlayers.Remove(id);
            if (initial || snapshot.events == null) return;
            foreach (OnlineWorldEvent worldEvent in snapshot.events)
                if (worldEvent != null && worldEvent.eventId > afterEventId &&
                    worldEvent.kind == "attack" && worldEvent.sourceId != null &&
                    remotePlayers.TryGetValue(worldEvent.sourceId, out PrototypeRemotePlayer remote))
                    remote.PlayAttack(worldEvent.ability);
        }

        public bool TryAllocate(HeroAttribute attribute, int amount = 1) => Submit(new OnlineCommand {
            type = "allocate", attribute = (int)attribute, amount = amount });
        public bool TryEquip(string itemId, EquipmentSlot slot, out string reason)
        {
            bool submitted = Submit(new OnlineCommand { type = "equip", itemId = itemId, slot = (int)slot });
            reason = submitted ? "Pedido enviado; aguardando confirmação do servidor." : "Aguarde a conexão ou a confirmação das ações pendentes.";
            return submitted;
        }
        public bool TryUnequip(EquipmentSlot slot, out string reason)
        {
            bool submitted = Submit(new OnlineCommand { type = "unequip", slot = (int)slot });
            reason = submitted ? "Pedido enviado; aguardando confirmação do servidor." : "Aguarde a conexão ou a confirmação das ações pendentes.";
            return submitted;
        }
        public bool TryUseAbility(int slot)
        {
            if (slot < 0 || slot > 4 || (Snapshot != null && Snapshot.self.health <= 0)) return false;
            foreach (OnlineCommand command in pending) if (command.type == "ability" && command.ability == slot) return false;
            if ((slot == 0 || slot == 1) && combat != null && !combat.PrepareTargetedAttack(slot)) return false;
            return Submit(new OnlineCommand { type = "ability", ability = slot,
                targetId = (slot == 0 || slot == 1) && combat != null ? combat.SelectedTargetId : null });
        }
        public bool TryTravel(string portalId) => SubmitEconomy(new OnlineCommand { type = "portal", portalId = portalId });
        public bool TryBuy(string npcId, OnlineShopOffer offer) => offer != null && SubmitEconomy(new OnlineCommand {
            type = "buy", npcId = npcId, offerId = offer.id, quoteVersion = offer.quoteVersion, amount = 1 });
        public bool TrySell(string npcId, string instanceId, int amount = 1) => SubmitEconomy(new OnlineCommand {
            type = "sell", npcId = npcId, itemId = instanceId, amount = amount });
        public bool TryCollect(string lootId) => SubmitEconomy(new OnlineCommand { type = "collect", lootId = lootId });
        public bool TryUsePotion() => SubmitEconomy(new OnlineCommand { type = "potion" });
        private bool SubmitEconomy(OnlineCommand command) => !HasPendingMutation && Submit(command);
        public bool TryRespawn()
        {
            foreach (OnlineCommand command in pending) if (command.type == "respawn") return false;
            return Submit(new OnlineCommand { type = "respawn" });
        }
        public bool Submit(OnlineCommand command)
        {
            if (!IsOnlineMode || !CanPlay || command == null || pending.Count >= MaximumQueuedCommands) return false;
            if (command.type != "allocate" && command.type != "equip" && command.type != "unequip" &&
                command.type != "ability" && command.type != "respawn" && command.type != "portal" &&
                command.type != "buy" && command.type != "sell" && command.type != "collect" && command.type != "potion") return false;
            // Own an immutable copy of the wire command; callers cannot mutate a retried payload.
            var acceptedCommand = new OnlineCommand { commandId = Guid.NewGuid().ToString("D"), type = command.type,
                attribute = command.attribute, amount = command.amount, itemId = command.itemId,
                slot = command.slot, ability = command.ability, npcId = command.npcId, offerId = command.offerId,
                quoteVersion = command.quoteVersion, portalId = command.portalId, lootId = command.lootId,
                targetId = command.targetId };
            pending.Add(acceptedCommand);
            if (acceptedCommand.type == "ability" && (acceptedCommand.ability == 0 || acceptedCommand.ability == 1))
                combat.PresentSubmittedAttack(acceptedCommand.ability, acceptedCommand.commandId);
            SetActionMessage("Pedido enviado; aguardando confirmação do servidor.");
            return true;
        }

        private void SetActionMessage(string message)
        {
            actionMessage = message;
            actionMessageUntil = Time.realtimeSinceStartupAsDouble + 4d;
        }

        private void SetConfigurationError(string message)
        {
            permanentConfigurationError = true;
            IsConnected = false;
            Status = message;
        }

        private static string ReadServerError(string json, string fallback)
        {
            try
            {
                OnlineError error = JsonUtility.FromJson<OnlineError>(json);
                if (error != null && !string.IsNullOrWhiteSpace(error.message))
                    return error.message.Length <= 320 ? error.message : error.message.Substring(0, 320);
            }
            catch (ArgumentException) { }
            return fallback;
        }

        private static string ReadServerErrorCode(string json)
        {
            try
            {
                OnlineError error = JsonUtility.FromJson<OnlineError>(json);
                return error != null && error.code != null && error.code.Length <= 64 ? error.code : null;
            }
            catch (ArgumentException) { return null; }
        }

        private void OnGUI()
        {
            if (Active != this || PrototypeGameMenu.HidesHud) return;
            if (overlayStyle == null)
            {
                overlayStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = false };
                titleStyle = new GUIStyle(overlayStyle) { fontSize = 19, fontStyle = FontStyle.Bold };
            }
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            if (!IsOnlineMode)
            {
                GUI.Box(new Rect(18, Screen.height - 55, 370, 36), GUIContent.none);
                GUI.Label(new Rect(28, Screen.height - 49, 354, 26), Status, overlayStyle);
            }
            else if (!CanPlay && !frontendIdle && !leavingToMenu)
            {
                GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
                Rect panel = new Rect((Screen.width - 570) * .5f, (Screen.height - 256) * .5f, 570, 256);
                GUI.Box(panel, GUIContent.none);
                GUI.Label(new Rect(panel.x + 24, panel.y + 22, 522, 30), "Guerra das Nações · conexão online", titleStyle);
                GUI.Label(new Rect(panel.x + 24, panel.y + 66, 522, 76), Status, overlayStyle);
                GUI.Label(new Rect(panel.x + 24, panel.y + 145, 522, 37), "Perfil: " + profile + " · " +
                    (Snapshot == null ? "aguardando progresso salvo" : "última revisão confirmada: " + Snapshot.saveRevision), overlayStyle);
                if (manualReconnectRequired && !permanentConfigurationError &&
                    GUI.Button(new Rect(panel.x + 24, panel.y + 196, 178, 34), "Reconectar")) Reconnect();
            }
            else if (CanPlay)
            {
                Rect panel = new Rect(18, Screen.height - 110, 358, 92);
                GUI.Box(panel, GUIContent.none);
                GUI.Label(new Rect(panel.x + 10, panel.y + 7, 340, 22),
                    "Online · " + Mathf.RoundToInt(lastRoundTripMilliseconds) + " ms · " + (remotePlayers.Count + 1) + " jogador(es)", overlayStyle);
                GUI.Label(new Rect(panel.x + 10, panel.y + 29, 340, 22),
                    "Salvo no servidor · revisão " + Snapshot.saveRevision + (HasPendingMutation ? " · enviando…" : ""), overlayStyle);
                GUI.Label(new Rect(panel.x + 10, panel.y + 51, 340, 37),
                    Time.realtimeSinceStartupAsDouble < actionMessageUntil ? actionMessage : "Perfil: " + profile, overlayStyle);
            }
            GUI.depth = previousDepth;
        }

        private void StopNetwork()
        {
            if (combat != null) combat.ClearSubmittedAttackPresentation();
            if (activeRequest != null) { activeRequest.Abort(); activeRequest = null; }
            if (connectionLoop != null) { StopCoroutine(connectionLoop); connectionLoop = null; }
        }
        private void OnDisable() { StopNetwork(); IsConnected = false; }
        private void OnDestroy()
        {
            StopNetwork();
            foreach (PrototypeRemotePlayer remote in remotePlayers.Values) if (remote != null) Destroy(remote.gameObject);
            remotePlayers.Clear();
            if (Active == this) Active = null;
        }

        public static string ValidateServer(string address, bool allowInsecureLan)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                (uri.AbsolutePath != "/" && uri.AbsolutePath != ""))
                throw new ArgumentException("Servidor inválido. Use --server=https://servidor:porta, sem credenciais ou caminho.");
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || allowInsecureLan)))
                throw new ArgumentException("Use HTTPS no servidor remoto. HTTP na rede local requer --allow-insecure-lan explícito.");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }
        private static bool HasArgument(string[] arguments, string flag)
        {
            foreach (string argument in arguments) if (argument == flag) return true;
            return false;
        }
        private static string ArgumentValue(string[] arguments, string name, string fallback)
        {
            string prefix = name + "=";
            foreach (string argument in arguments)
                if (argument.StartsWith(prefix, StringComparison.Ordinal)) return argument.Substring(prefix.Length);
            return fallback;
        }
    }
}
