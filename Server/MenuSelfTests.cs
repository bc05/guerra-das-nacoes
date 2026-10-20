using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

public static class MenuSelfTests
{
    private static int assertions;
    private static void Check(bool condition, string reason)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("MENU-SELF-TEST: " + reason);
    }
    private static void Reject(Action action, int status, string reason)
    {
        try { action(); }
        catch (GameApiException error) { Check(error.Status == status, reason); return; }
        Check(false, reason);
    }
    private static CollisionGrid Grid() => new() { originX = -18, originZ = -18, cellSize = .25f,
        width = 144, height = 144, blocked = new int[144 * 144] };
    private static OnlineConnectRequest Identity(string name) => new()
        { deviceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), displayName = name };
    private static OnlineSnapshot Send(GameWorld world, string token, double now, params OnlineCommand[] commands) =>
        world.Commands(token, new OnlineCommandBatch { commands = commands }, now);

    public static void Run()
    {
        assertions = 0;
        string directory = Path.Combine(Path.GetTempPath(), "gdn-menu-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            double now = 1000;
            var store = new SaveStore(Path.Combine(directory, "characters.db"));
            var world = new GameWorld(store, Grid(), now);
            var identity = Identity("Herói original");
            string emptyState = WireJson.Write(store.Load());
            Check(!world.CharacterPreview(identity).exists, "preview desconhecido não cria personagem");
            Check(WireJson.Write(store.Load()) == emptyState, "preview desconhecido não grava mundo ou progresso");
            Reject(() => world.CharacterPreview(new OnlineConnectRequest { protocolVersion = 1, deviceKey = identity.deviceKey }), 400, "preview valida protocolo");
            Reject(() => world.CharacterPreview(new OnlineConnectRequest { deviceKey = Convert.ToBase64String(new byte[16]) }), 400, "preview exige identidade de 256 bits");
            Reject(() => world.CharacterPreview(new OnlineConnectRequest { deviceKey = "invalid" }), 400, "preview recusa identidade malformada");

            var connected = world.Connect(identity, now);
            var allocate = new OnlineCommand { type = "allocate", commandId = Guid.NewGuid().ToString("D"), attribute = 0, amount = 2 };
            var changed = Send(world, connected.sessionToken, now += .1, allocate);
            var buff = new OnlineCommand { type = "ability", commandId = Guid.NewGuid().ToString("D"), ability = 4 };
            var buffed = Send(world, connected.sessionToken, now += .1, buff);
            Check(buffed.results[0].accepted, "fixture ativa habilidade para observar cooldown");
            string beforePreview = WireJson.Write(store.Load());
            identity.displayName = "Tentativa de renomear";
            var preview = world.CharacterPreview(identity);
            Check(preview.exists && preview.playerId == connected.playerId && preview.displayName == "Herói original", "preview mantém identidade e nome existentes");
            Check(preview.level == changed.character.level && preview.mapId == changed.mapId, "preview oferece nível e mapa salvos");
            Check(preview.equipped.Length == changed.self.equipped.Length && preview.itemDefinitions.Length > 0, "preview oferece equipamentos públicos para apresentação");
            Check(WireJson.Write(store.Load()) == beforePreview, "preview conhecido não altera revisão ou qualquer dado persistido");
            preview.itemDefinitions[0].name = "alteração externa";
            Check(world.CharacterPreview(identity).itemDefinitions[0].name != "alteração externa", "preview não expõe referência mutável da configuração");
            Check(Send(world, connected.sessionToken, now).playerId == connected.playerId, "preview não substitui a sessão já conectada");
            identity.preserveExistingName = true;
            var preservedName = world.Connect(identity, now);
            Check(preservedName.self.displayName == "Herói original", "conectar pelo menu preserva nome real mesmo com rótulo local antigo");
            identity.preserveExistingName = false;
            identity.displayName = "Nome legado explícito";
            var renamed = world.Connect(identity, now);
            Check(renamed.self.displayName == "Nome legado explícito", "requisição legada sem proteção mantém contrato de nome explícito");
            identity.displayName = "Herói original";
            connected = world.Connect(identity, now);
            string beforeDisconnect = WireJson.Write(store.Load());
            Check(world.Disconnect(connected.sessionToken, now).disconnected, "logout encerra sessão fora de combate");
            Check(world.Disconnect(connected.sessionToken, now).disconnected, "logout repetido é idempotente");
            Check(WireJson.Write(store.Load()) == beforeDisconnect, "logout não altera vida, recursos ou progresso persistido");
            Reject(() => Send(world, connected.sessionToken, now), 401, "sessão removida não envia comandos");
            Reject(() => world.Disconnect(null, now), 401, "logout exige autorização");
            identity.displayName = "Herói original";
            var reopened = world.Connect(identity, now);
            Check(reopened.character.attackPoints == 2 && reopened.character.coins == changed.character.coins, "reentrada preserva atributos e moedas");
            Check(reopened.self.cooldownUntil[4] == buffed.self.cooldownUntil[4] && reopened.self.furyUntil == buffed.self.furyUntil,
                "trocar para o menu não reseta cooldown ou buff");

            var strike = new OnlineCommand { type = "ability", commandId = Guid.NewGuid().ToString("D"), ability = 0 };
            var attacking = Send(world, reopened.sessionToken, now += 1, strike);
            Check(attacking.results[0].accepted, "fixture arma golpe pendente");
            Reject(() => world.Disconnect(reopened.sessionToken, now), 409, "logout recusa golpe pendente");
            Check(Send(world, reopened.sessionToken, now).playerId == reopened.playerId, "logout recusado mantém sessão");

            // Trusted saved fixtures cover combat and death without depending on monster pathfinding.
            var fixture = store.Load();
            var hero = fixture.players.Single(p => p.id == reopened.playerId);
            hero.strike = null;
            hero.x = fixture.world.monsters[0].x;
            hero.z = fixture.world.monsters[0].z - 1.5f;
            fixture.world.monsters[0].targetId = null;
            fixture.world.monsters[0].returning = false;
            store.Save(fixture.players, fixture.world, Array.Empty<Receipt>());
            world = new GameWorld(store, Grid(), now);
            var active = world.Connect(identity, now);
            strike = new OnlineCommand { type = "ability", commandId = Guid.NewGuid().ToString("D"), ability = 0 };
            Check(Send(world, active.sessionToken, now += 1, strike).results[0].accepted, "fixture inicia ataque próximo ao monstro");
            Send(world, active.sessionToken, now += .2);
            Reject(() => world.Disconnect(active.sessionToken, now), 409, "logout recusa monstro em combate");
            fixture = store.Load();
            fixture.players.Single(p => p.id == active.playerId).character.health = 0;
            store.Save(fixture.players, fixture.world, Array.Empty<Receipt>());
            world = new GameWorld(store, Grid(), now);
            var dead = world.Connect(identity, now);
            Check(dead.self.health == 0 && world.Disconnect(dead.sessionToken, now).disconnected, "personagem derrotado pode voltar à seleção");
            var deadAgain = world.Connect(identity, now);
            Check(deadAgain.self.health == 0, "retornar à seleção não ressuscita personagem");
            TestIndependentServerCatalogs();
            Console.WriteLine($"PASS: {assertions} verificações de seleção, prévia somente leitura e saída sem reset de progresso.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void TestIndependentServerCatalogs()
    {
        var first = EquipmentCatalog.ToConfig(EquipmentCatalog.GetDefinition("steel-sword"));
        first.definitionRevision = 9001;
        first.name = "Espada do servidor A";
        Check(EquipmentCatalog.TryRegisterDefinition(first, out _), "servidor A registra sua revisão própria");
        var second = EquipmentCatalog.ToConfig(EquipmentCatalog.GetDefinition("steel-sword"));
        second.definitionRevision = 9001;
        second.name = "Espada do servidor B";
        Check(!EquipmentCatalog.TryRegisterDefinition(second, out _), "mesma origem continua protegendo imutabilidade de revisão");
        EquipmentCatalog.ResetPublishedDefinitions();
        Check(EquipmentCatalog.GetDefinition("steel-sword", 9001) == null, "trocar origem descarta revisões do servidor anterior");
        Check(EquipmentCatalog.TryRegisterDefinition(second, out _), "servidor B pode usar mesmo identificador com definição independente");
        Check(EquipmentCatalog.GetDefinition("steel-sword", 9001).Name == second.name, "nova origem resolve sua própria definição");
        EquipmentCatalog.ResetPublishedDefinitions();
    }
}
