using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Real transport against the disposable server on 18090. The runner seeds the temporary
// identity's character from the non-secret request, then creates <request>.ready before connecting.
[InitializeOnLoad]
public static class CatacombsOnlinePlayModeChecks
{
    private const string Key = "GuerraDasNacoes.CatacombsOnlineChecks.";
    private const string Endpoint = "http://127.0.0.1:18090";
    private const string TargetId = "cat-skeleton-2";
    [Serializable] private sealed class FixtureRequest { public string profile, playerId, identityHash; }
    private static readonly Dictionary<string, PrototypeMonster> cached = new Dictionary<string, PrototypeMonster>();
    private static readonly Dictionary<string, Transform> cachedVisuals = new Dictionary<string, Transform>();
    private static readonly HashSet<long> observedEvents = new HashSet<long>();
    private static readonly HashSet<string> ownCommands = new HashSet<string>();
    private static FieldInfo pendingField;
    private static PrototypeOnlineClient client;
    private static PrototypeCombat combat;
    private static PrototypeCharacter character;
    private static PrototypeCity maps;
    private static PrototypeMonster target;
    private static GameObject ruins, frontier, catacombs;
    private static string profile, expectedPlayerId, fixturePath, invariantProgress, reconnectProgress;
    private static long eventFloor;
    private static int attacks, hits, swingsBefore, targetHealth, checks;
    private static float nextAt, quietUntil;
    private static Vector3 selectedPosition;

    static CatacombsOnlinePlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar Catacumbas com cliente e servidor reais")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Execute fora do PlayMode.");
        string requestPath = Environment.GetEnvironmentVariable("PROTOTYPE_CATACOMBS_FIXTURE_REQUEST");
        if (string.IsNullOrWhiteSpace(requestPath) || !Path.IsPathRooted(requestPath))
            throw new InvalidOperationException("Defina PROTOTYPE_CATACOMBS_FIXTURE_REQUEST com caminho absoluto descartável.");
        if (File.Exists(requestPath) || File.Exists(requestPath + ".ready"))
            throw new InvalidOperationException("Use um caminho novo para o manifesto; arquivos antigos não autorizam uma conexão.");
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        string ownedProfile = "cc-" + Guid.NewGuid().ToString("N").Substring(0, 16);
        string root = OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, Endpoint);
        OnlineIdentity identity = OnlineIdentityStore.LoadOrCreate(root, ownedProfile);
        string digest;
        byte[] secret = Convert.FromBase64String(identity.deviceKey);
        try
        {
            using (SHA256 sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(secret)).Replace("-", "");
        }
        finally { Array.Clear(secret, 0, secret.Length); }
        string playerId = Guid.NewGuid().ToString("D");
        var request = new FixtureRequest { profile = ownedProfile, playerId = playerId, identityHash = digest };
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath));
        File.WriteAllText(requestPath, JsonUtility.ToJson(request, true));
        SessionState.SetString(Key + "Profile", ownedProfile);
        SessionState.SetString(Key + "PlayerId", playerId);
        SessionState.SetString(Key + "Fixture", requestPath);
        SessionState.SetInt(Key + "Stage", 0); SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "FixtureTimeout", (float)EditorApplication.timeSinceStartup + 90f);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 180f);
        SessionState.SetBool(Key + "Active", true);
        nextAt = 0f; checks = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Guard(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "timeout; " + State());
            if (!EditorApplication.isPlaying)
            { Guard(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antecipadamente"); return; }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 4 || Time.time < nextAt) return;
            if (client != null && client.CanPlay && client.Snapshot != null) Observe();
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
                    Require(combat != null, "personagem real presente");
                    character = combat.GetComponent<PrototypeCharacter>(); client = combat.GetComponent<PrototypeOnlineClient>();
                    maps = combat.GetComponent<PrototypeCity>();
                    pendingField = typeof(PrototypeOnlineClient).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic);
                    Require(character != null && client != null && maps != null && maps.IsBuilt, "controle, transporte e mapas reais prontos");
                    client.ConfigureForTests(false); character.enabled = false;
                    profile = SessionState.GetString(Key + "Profile", "");
                    expectedPlayerId = SessionState.GetString(Key + "PlayerId", "");
                    fixturePath = SessionState.GetString(Key + "Fixture", "");
                    ruins = combat.gameObject.scene.GetRootGameObjects().Single(o => o.name == "Cenario");
                    frontier = combat.gameObject.scene.GetRootGameObjects().Single(o => o.name == PrototypeWorldExpansion.RootName);
                    catacombs = combat.gameObject.scene.GetRootGameObjects().Single(o => o.name == CatacombsLayout.RootName);
                    cached.Clear(); cachedVisuals.Clear(); observedEvents.Clear(); ownCommands.Clear(); attacks = hits = 0; eventFloor = long.MaxValue;
                    Require(ruins.activeSelf && frontier.activeSelf && !catacombs.activeSelf, "cena inicia somente no Vale");
                    Advance(1); break;
                case 1:
                    Guard(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "FixtureTimeout", 0),
                        "runner não preparou a fixture e o arquivo .ready em 90s");
                    if (!File.Exists(fixturePath + ".ready")) return;
                    character.enabled = true;
                    client.ConfigureForTests(true, Endpoint, profile, profile);
                    Require(!client.CanPlay, "transporte aguarda o primeiro snapshot antes de liberar jogo");
                    Advance(2); break;
                case 2:
                    if (!client.CanPlay) return;
                    Require(client.Snapshot.playerId == expectedPlayerId, "servidor carrega exclusivamente a identidade descartável preparada");
                    Require(client.Snapshot.mapId == "ruins" && Distance(client.Snapshot.self.x, client.Snapshot.self.z,
                        CatacombsLayout.ValePortal.X, CatacombsLayout.ValePortal.Z) < .25f,
                        "fixture inicia em (0,19) no portal do Vale");
                    Require(combat.Stats.Level == 50 && combat.Stats.DefensePoints == 250 && combat.Stats.Health >= 900,
                        "fixture isolada possui atributos válidos para sobreviver ao percurso");
                    invariantProgress = ProgressWithoutHealth();
                    ValidateMap("ruins");
                    Require(client.Snapshot.portals.Any(p => p.id == "portal-catacombs" && p.destinationMapId == CatacombsLayout.MapId),
                        "portal das Catacumbas foi anunciado pelo servidor");
                    Require(RequestPortal("portal-catacombs"), "comando real solicita entrada no novo mapa");
                    Advance(3); break;
                case 3:
                    if (!ReadyOn(CatacombsLayout.MapId)) return;
                    ValidateCatacombs(false);
                    Require(Distance(client.Snapshot.self.x, client.Snapshot.self.z, 0f, 0f) < .25f,
                        "entrada confirma posição canônica do vestíbulo");
                    Require(client.Snapshot.portals.Length == 1 && client.Snapshot.portals[0].id == "portal-catacombs-exit",
                        "mapa subterrâneo recebe seu próprio portal de retorno");
                    reconnectProgress = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    int originalSwings = combat.SwingCount;
                    combat.ApplyOnlineSnapshot(client.Snapshot); combat.ApplyOnlineSnapshot(client.Snapshot);
                    Require(combat.SwingCount == originalSwings && !combat.HasQueuedAttack, "snapshot repetido não inventa golpes ou aproximação");
                    client.ConfigureForTests(true, Endpoint, profile, profile);
                    Require(!client.CanPlay, "reconexão realmente reinicia o transporte e aguarda o servidor");
                    Advance(4); break;
                case 4:
                    if (!ReadyOn(CatacombsLayout.MapId)) return;
                    Require(client.Snapshot.playerId == expectedPlayerId && JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == reconnectProgress,
                        "reconectar conserva mapa, identidade e progresso completo do vestíbulo");
                    ValidateCatacombs(true);
                    target = combat.Monsters.Single(m => m.OnlineId == TargetId);
                    Require(target.IsAlive && target.IsAvailableForTargeting, "esqueleto central pode ser selecionado no cliente real");
                    eventFloor = client.Snapshot.latestEventId; observedEvents.Clear(); attacks = hits = 0;
                    swingsBefore = combat.SwingCount; targetHealth = WireTarget().health;
                    selectedPosition = character.transform.position;
                    Require(character.SelectAttackTarget(target) && combat.Target == target && !combat.HasQueuedAttack,
                        "seleção real identifica o esqueleto sem pedir ataque");
                    quietUntil = Time.time + .85f; Advance(5); break;
                case 5:
                    Guard(attacks == 0 && hits == 0 && combat.SwingCount == swingsBefore && WireTarget().health == targetHealth &&
                        !combat.HasQueuedAttack && !character.HasDestination && Flat(character.transform.position, selectedPosition) < .08f,
                        "seleção permanece sem dano, comando ou movimento");
                    if (Time.time < quietUntil) return;
                    character.MoveTo(new Vector3(0f, character.transform.position.y, 20f));
                    Advance(6); break;
                case 6:
                    Guard(client.CanPlay && combat.IsAlive && attacks == 0 && hits == 0, "caminhada explícita pelo corredor não ataca");
                    if (Distance(client.Snapshot.self.x, client.Snapshot.self.z, 0f, 20f) > 1f ||
                        Flat(character.transform.position, new Vector3(0f, character.transform.position.y, 20f)) > 1f) return;
                    Require(combat.Target == target && combat.CanAttack && !client.HasPendingMutation,
                        "acionamento manual aguarda alcance de aproximação e transporte livre");
                    targetHealth = WireTarget().health;
                    Require(targetHealth > combat.Stats.GetBasicDamage(), "fixture recebe golpe não letal sem recompensas artificiais");
                    Require(combat.RequestAttack(0), "um acionamento explícito solicita um básico ao esqueleto");
                    Advance(7); break;
                case 7:
                    Guard(client.CanPlay && combat.IsAlive && attacks <= 1 && hits <= 1, "aproximação mantém conexão e nunca repete o pedido");
                    if (attacks < 1 || hits < 1 || client.HasPendingMutation || combat.HasQueuedAttack) return;
                    Require(WireTarget().health == targetHealth - combat.Stats.GetBasicDamage() && target.CurrentHealth == WireTarget().health,
                        "impacto e vida do esqueleto são confirmados pelo servidor");
                    Require(combat.SwingCount == swingsBefore + 1 && !combat.HasSubmittedAttackPresentation,
                        "ACK não duplica animação nem arma repetição automática");
                    ValidateCatacombs(true);
                    character.MoveTo(new Vector3(0f, character.transform.position.y, -2f));
                    Advance(8); break;
                case 8:
                    Guard(client.CanPlay && combat.IsAlive && attacks == 1 && hits == 1 && combat.SwingCount == swingsBefore + 1,
                        "retirada não provoca outro ataque manual");
                    if (Distance(client.Snapshot.self.x, client.Snapshot.self.z, 0f, -2f) > .7f ||
                        Flat(character.transform.position, new Vector3(0f, character.transform.position.y, -2f)) > .7f) return;
                    if (client.Snapshot.monsters.Any(m => m.targetId == expectedPlayerId && !m.returning && m.health > 0)) return;
                    if (client.HasPendingMutation) return;
                    Require(RequestPortal("portal-catacombs-exit"), "transporte solicita saída após afastar-se do combate");
                    Advance(9); break;
                case 9:
                    if (!ReadyOn("ruins")) return;
                    ValidateMap("ruins");
                    Require(cached.Values.All(m => !m.gameObject.activeInHierarchy), "saída oculta todas as criaturas subterrâneas em cache");
                    Require(combat.Target == null && !combat.HasQueuedAttack && !combat.HasPendingStrike,
                        "troca de mapa limpa seleção e golpes pendentes");
                    Require(Distance(client.Snapshot.self.x, client.Snapshot.self.z,
                        CatacombsLayout.ValeReturn.X, CatacombsLayout.ValeReturn.Z) < .3f, "saída confirma retorno em (0,17) no Vale");
                    Require(ProgressWithoutHealth() == invariantProgress, "viagem e golpe não letal preservam nível, XP, pontos, patrimônio e equipamentos");
                    Require(RequestPortal("portal-catacombs"), "nova entrada usa a posição real próxima ao portal");
                    Advance(10); break;
                case 10:
                    if (!ReadyOn(CatacombsLayout.MapId)) return;
                    ValidateCatacombs(true);
                    Require(attacks == 1 && hits == 1 && combat.SwingCount == swingsBefore + 1,
                        "reentrada e snapshots não repetem o ataque anterior");
                    Require(ProgressWithoutHealth() == invariantProgress, "cache visual e segunda entrada conservam o progresso canônico");
                    Finish(true, checks + " verificações com transporte real: portal, sete criaturas, modelos autorais, reconexão, seleção, golpe único, retirada, saída e reentrada com cache/progresso preservados."); break;
            }
        }
        catch (Exception error) { Finish(false, "etapa=" + SessionState.GetInt(Key + "Stage", -1) + "; " + State() + "; " + error); }
    }

    private static bool ReadyOn(string map) => client.CanPlay && client.Snapshot != null && client.Snapshot.mapId == map && !client.HasPendingMutation;
    private static void ValidateMap(string map)
    {
        Require(PrototypeCity.CurrentMapId == map && !maps.CityRoot.activeSelf, "UI e apresentação reconhecem o mapa de combate confirmado");
        bool subterranean = map == CatacombsLayout.MapId;
        Require(catacombs.activeSelf == subterranean && ruins.activeSelf != subterranean && frontier.activeSelf != subterranean,
            "cenários de superfície e subterrâneo permanecem mutuamente exclusivos");
        Require(combat.Monsters.All(m => m.gameObject.activeInHierarchy && m.transform.parent == maps.MonsterRoot(map)),
            "todos os encontros ativos pertencem à raiz do mapa confirmado");
    }
    private static void ValidateCatacombs(bool reuse)
    {
        ValidateMap(CatacombsLayout.MapId);
        OnlineMonsterState[] wire = client.Snapshot.monsters;
        Require(wire.Length == 7 && combat.Monsters.Count == 7 && wire.All(m => m.mapId == CatacombsLayout.MapId),
            "servidor e cliente apresentam exclusivamente os sete encontros subterrâneos");
        Require(wire.Any(m => m.visualKind == "skeleton") && wire.Any(m => m.visualKind == "skeleton-caster"),
            "snapshot autoritativo distingue guerreiros e conjuradores");
        foreach (OnlineMonsterState state in wire)
        {
            PrototypeMonster enemy = combat.Monsters.Single(m => m.OnlineId == state.id);
            PrototypeUndeadVisual view = enemy.GetComponent<PrototypeUndeadVisual>();
            Require(view != null && view.IsApplied && view.Kind == state.visualKind && enemy.VisualRoot == view.ActiveRoot &&
                view.ActiveRoot.gameObject.activeSelf && view.OriginalRoot != null && !view.OriginalRoot.gameObject.activeSelf,
                "encontro " + state.id + " apresenta somente seu esqueleto autoral");
            Require(enemy.CurrentHealth == state.health && enemy.SpeciesId == state.speciesId && enemy.Behavior == state.behavior,
                "espécie, comportamento e saúde visual seguem o snapshot para " + state.id);
            Require(enemy.VisualRoot.GetComponentsInChildren<Collider>(true).Length == 0, "modelo de " + state.id + " preserva apenas o collider raiz");
            if (reuse)
                Require(cached[state.id] == enemy && cachedVisuals[state.id] == enemy.VisualRoot,
                    "reconexão/retorno reutilizam encontro e rig de " + state.id);
            else { cached.Add(state.id, enemy); cachedVisuals.Add(state.id, enemy.VisualRoot); }
        }
    }
    private static void Observe()
    {
        OnlineSnapshot snapshot = client.Snapshot;
        RememberPending();
        if (snapshot.results != null)
            foreach (OnlineCommandResult result in snapshot.results)
                if (ownCommands.Contains(result.commandId))
                    Guard(result.accepted, "portal ou básico real recusado: " + result.message);
        if (snapshot.events == null) return;
        foreach (OnlineWorldEvent value in snapshot.events)
        {
            if (value.eventId <= eventFloor || value.sourceId != expectedPlayerId || !observedEvents.Add(value.eventId)) continue;
            if (value.kind == "attack") { Guard(value.ability == 0, "fixture só solicita básico"); attacks++; }
            if (value.kind == "hit" && value.targetId == TargetId && value.amount > 0) hits++;
        }
    }
    private static bool RequestPortal(string id)
    {
        bool submitted = client.TryTravel(id); RememberPending(); return submitted;
    }
    private static void RememberPending()
    {
        if (pendingField == null || client == null) return;
        foreach (OnlineCommand command in (List<OnlineCommand>)pendingField.GetValue(client))
            if (command.type == "portal" || command.type == "ability") ownCommands.Add(command.commandId);
    }
    private static OnlineMonsterState WireTarget() => client.Snapshot.monsters.Single(m => m.id == TargetId);
    private static string ProgressWithoutHealth() { CharacterSaveData save = combat.Stats.CaptureSaveData(); save.health = 0; return JsonUtility.ToJson(save); }
    private static float Distance(float x, float z, float atX, float atZ) => Vector2.Distance(new Vector2(x, z), new Vector2(atX, atZ));
    private static float Flat(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    private static string State() => client == null ? "sem cliente" : "CanPlay=" + client.CanPlay + "; mapa=" +
        (client.Snapshot != null ? client.Snapshot.mapId : "sem snapshot") + "; ataques=" + attacks + "; impactos=" + hits + "; status=" + client.Status;
    private static void Advance(int stage)
    {
        SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + .01f;
        Debug.Log("[CATACOMBS ONLINE STAGE] " + stage + "; " + State());
    }
    private static void Guard(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
    private static void Require(bool value, string text) { Guard(value, text); checks++; }
    private static void Finish(bool success, string text)
    {
        SessionState.SetBool(Key + "Active", false);
        if (combat != null) combat.CancelRequestedAttack();
        if (character != null) character.enabled = false;
        if (client != null) client.enabled = false;
        string ownedProfile = string.IsNullOrEmpty(profile) ? SessionState.GetString(Key + "Profile", "") : profile;
        if (!string.IsNullOrEmpty(ownedProfile) && ownedProfile.StartsWith("cc-", StringComparison.Ordinal))
        {
            string root = OnlineIdentityStore.GetIdentityRoot(Application.persistentDataPath, Endpoint);
            string directory = Path.GetDirectoryName(OnlineIdentityStore.GetIdentityPath(root, ownedProfile));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        if (success) Debug.Log("[CATACOMBS ONLINE PLAY PASS] " + text); else Debug.LogError("[CATACOMBS ONLINE PLAY FAIL] " + text);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1); else EditorApplication.ExitPlaymode();
    }
}
