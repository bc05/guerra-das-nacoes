using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

internal static class SelfTests
{
    private static int assertions;
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GuerraDasNacoes-ServerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestPersistenceAndCommands(Path.Combine(directory, "persistence.db"));
            TestCombat(Path.Combine(directory, "combat.db"));
            TestMonsterStrikeEvents(directory);
            TestCollision(Path.Combine(directory, "collision.db"));
            TestAbilitiesAndDeath(Path.Combine(directory, "abilities.db"));
            TestMovementBudget(Path.Combine(directory, "movement.db"));
            TestHealingRounding(Path.Combine(directory, "healing.db"));
            TestConcurrency(Path.Combine(directory, "concurrent.db"));
            TestSchemaRefusal(Path.Combine(directory, "future.db"));
            TestCorruptionRefusal(Path.Combine(directory, "invalid-snapshot.db"));
            Console.WriteLine($"PASS: {assertions} verificações do servidor, persistência, concorrência, combate e segurança de comandos.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static CollisionGrid EmptyGrid() => new()
    {
        originX = -18, originZ = -18, cellSize = .25f, width = 144, height = 144, blocked = new int[144 * 144]
    };
    private static string DeviceKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static OnlineConnectRequest ConnectRequest(string key, string name = "Teste") => new() { deviceKey = key, displayName = name };
    private static OnlineCommand Command(string type) => new() { commandId = Guid.NewGuid().ToString("D"), type = type };
    private static OnlineSnapshot Send(GameWorld world, string token, double time, params OnlineCommand[] commands) =>
        world.Commands(token, new OnlineCommandBatch { commands = commands }, time);
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("SELF-TEST: " + message);
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { assertions++; return; }
        throw new InvalidOperationException("SELF-TEST: esperava " + typeof(T).Name + ": " + message);
    }

    private static void TestPersistenceAndCommands(string path)
    {
        double now = 1000;
        string key = DeviceKey();
        var store = new SaveStore(path);
        var world = new GameWorld(store, EmptyGrid(), now);
        var initial = world.Connect(ConnectRequest(key), now);
        string otherKey = DeviceKey();
        var other = world.Connect(ConnectRequest(otherKey, "Outro"), now);
        Check(initial.playerId != other.playerId, "identidades distintas");
        Check(initial.character.level == 1 && initial.character.unspentPoints == 5, "padrão inicial");
        Check(initial.character.inventoryItemIds.Length == 0 && initial.character.equipped.Length == 12, "personagem novo possui somente conjunto de treinamento");
        // Trusted legacy fixture exercises the previous 35-item save through the real migration.
        var legacy = store.Load();
        var legacyHero = legacy.players.Single(p => p.id == initial.playerId);
        legacyHero.character.schemaVersion = 1;
        legacyHero.character.itemInstances = null;
        legacyHero.character.inventoryItemIds = EquipmentCatalog.CreateStartingInventory().Select(i => i.Id).ToArray();
        store.Save(legacy.players, legacy.world, Array.Empty<Receipt>());
        world = new GameWorld(store, EmptyGrid(), now);
        initial = world.Connect(ConnectRequest(key), now);
        other = world.Connect(ConnectRequest(otherKey, "Outro"), now);
        Check(initial.character.itemInstances.Length == 35 && initial.character.itemInstances.All(i => i.bound), "migração preserva 35 itens legados vinculados");
        var allocate = Command("allocate"); allocate.attribute = 0; allocate.amount = 2;
        var one = Send(world, initial.sessionToken, now += .1, allocate);
        Check(one.results[0].accepted && one.character.attackPoints == 2, "alocação autoritativa");
        var duplicate = Send(world, initial.sessionToken, now += .1, allocate);
        Check(duplicate.results[0].accepted && duplicate.character.attackPoints == 2 && duplicate.character.unspentPoints == 3, "idempotência");
        var equip = Command("equip"); equip.itemId = "steel-sword"; equip.slot = 0;
        var equipped = Send(world, initial.sessionToken, now += .1, equip);
        Check(equipped.results[0].accepted && equipped.character.equipped.Any(p => p.slot == 0 && p.itemId == "steel-sword"), "troca real de espada");
        Check(Send(world, other.sessionToken, now += .1).character.attackPoints == 0, "isolamento de personagens");
        var forged = Command("equip"); forged.itemId = "invented-sword"; forged.slot = 0;
        Check(!Send(world, initial.sessionToken, now += .1, forged).results[0].accepted, "item inventado rejeitado");
        var insufficient = Command("equip"); insufficient.itemId = "veteran-sword"; insufficient.slot = 0;
        Check(!Send(world, initial.sessionToken, now += .1, insufficient).results[0].accepted, "requisitos respeitados");
        var arbitraryXp = Command("grantExperience"); arbitraryXp.amount = 999999;
        var badXp = Send(world, initial.sessionToken, now += .1, arbitraryXp);
        Check(!badXp.results[0].accepted && badXp.character.level == 1 && badXp.character.experience == 0, "XP arbitrário rejeitado");
        var fury = Command("ability"); fury.ability = 4;
        var buff = Send(world, initial.sessionToken, now += .1, fury);
        Check(buff.results[0].accepted && buff.self.furyUntil > now && buff.self.mana < 100, "buff usa mana e duração no servidor");
        double furyUntil = buff.self.furyUntil, furyCooldown = buff.self.cooldownUntil[4];
        float mana = buff.self.mana;
        var reconnected = world.Connect(ConnectRequest(key), now += .1);
        Check(reconnected.playerId == initial.playerId && reconnected.character.attackPoints == 2, "reconectar preserva personagem");
        Check(reconnected.self.furyUntil == furyUntil && reconnected.self.cooldownUntil[4] == furyCooldown && reconnected.self.mana < mana + 1, "reconectar não reseta recursos");
        Throws<GameApiException>(() => Send(world, initial.sessionToken, now), "sessão antiga revogada");
        var rejected = Command("allocate"); rejected.attribute = 0; rejected.amount = 50;
        Check(!Send(world, reconnected.sessionToken, now += .1, rejected).results[0].accepted, "rejeição válida");
        rejected.amount = 1;
        Check(!Send(world, reconnected.sessionToken, now += .1, rejected).results[0].accepted, "UUID não aceita payload divergente");
        var retry = Command("allocate"); retry.attribute = 1;
        store.FailNextSaveForTests = true;
        Throws<IOException>(() => Send(world, reconnected.sessionToken, now += .1, retry), "falha de disco reverte");
        var afterFailure = Send(world, reconnected.sessionToken, now += .1);
        Check(afterFailure.character.defensePoints == 0 && afterFailure.character.unspentPoints == 3, "memória revertida após falha de disco");
        Check(Send(world, reconnected.sessionToken, now += .1, retry).character.defensePoints == 1, "retry não perdeu comando sem ACK");
        var ambiguous = Command("allocate"); ambiguous.attribute = 2;
        store.FailAfterCommitForTests = true;
        Throws<IOException>(() => Send(world, reconnected.sessionToken, now += .1, ambiguous), "commit ambíguo não produz ACK");
        var recovered = Send(world, reconnected.sessionToken, now += .1);
        Check(recovered.character.intelligencePoints == 1 && recovered.character.unspentPoints == 1, "memória recupera o estado confirmado no banco");
        Check(Send(world, reconnected.sessionToken, now += .1, ambiguous).character.intelligencePoints == 1, "retry após commit ambíguo não duplica pontos");
        var worldRestarted = new GameWorld(new SaveStore(path), EmptyGrid(), now);
        var persisted = worldRestarted.Connect(ConnectRequest(key), now += .1);
        Check(persisted.character.attackPoints == 2 && persisted.character.defensePoints == 1 && persisted.character.intelligencePoints == 1 && persisted.character.unspentPoints == 1, "SQLite preserva pontos após reinício");
        Check(persisted.character.equipped.Any(p => p.itemId == "steel-sword") && persisted.character.inventoryItemIds.Contains("training-sword"), "mochila e equipamento persistidos");
        Check(persisted.self.furyUntil == furyUntil && persisted.self.cooldownUntil[4] == furyCooldown, "cooldown/buff persistidos após reinício");
        Check(Send(worldRestarted, persisted.sessionToken, now += .1, allocate).character.attackPoints == 2, "receipt persistido após reinício");
        var respawnAlive = Command("respawn");
        Check(!Send(worldRestarted, persisted.sessionToken, now += .1, respawnAlive).results[0].accepted, "R vivo não cura");
        Throws<GameApiException>(() => world.Connect(ConnectRequest("not-a-key"), now), "chave fraca rejeitada");
        Throws<GameApiException>(() => Send(worldRestarted, "invented-token", now), "token inventado rejeitado");
        var futureCursor = worldRestarted.Commands(persisted.sessionToken, new OnlineCommandBatch { afterEventId = long.MaxValue, commands = Array.Empty<OnlineCommand>() }, now += .1);
        Check(futureCursor.latestEventId < long.MaxValue, "cursor de evento recupera após reinício");
    }

    private static void TestCombat(string path)
    {
        double now = 2000;
        var world = new GameWorld(new SaveStore(path), EmptyGrid(), now);
        string key = DeviceKey();
        var connected = world.Connect(ConnectRequest(key), now);
        string token = connected.sessionToken;
        var allocate = Command("allocate"); allocate.amount = 5;
        Send(world, token, now += .1, allocate);
        var teleport = Command("move"); teleport.x = -2.6f; teleport.z = 1.4f;
        Check(!Send(world, token, now += .1, teleport).results[0].accepted, "teleporte rejeitado");
        float x = 0, z = 0;
        for (int step = 1; step <= 4; step++)
        {
            var move = Command("move"); move.x = -2.6f * step / 4; move.z = 1.4f * step / 4; move.running = true;
            var snapshot = Send(world, token, now += .2, move);
            Check(snapshot.results[0].accepted, "movimento gradual válido");
            x = snapshot.self.x; z = snapshot.self.z;
        }
        Check(x == -2.6f && z == 1.4f, "posição autoritativa atualizada");
        var attack = Command("ability"); attack.ability = 0;
        var start = Send(world, token, now += .2, attack);
        Check(start.results[0].accepted && start.monsters[0].health == 240, "golpe tem atraso de impacto");
        var impact = Send(world, token, now += .14);
        Check(impact.monsters[0].health < 240 && impact.events.Any(e => e.kind == "hit"), "dano calculado pelo servidor");
        Check(impact.character.experience == 0, "acerto sem morte não ganha XP");
        OnlineSnapshot current = impact;
        for (int strike = 0; strike < 12 && current.character.level == 1; strike++)
        {
            var next = Command("ability");
            current = Send(world, token, now += .66, next);
            current = Send(world, token, now += .14);
        }
        Check(current.character.level == 2 && current.character.experience == 20, "morte gera exatamente 120 XP no servidor");
        Check(current.character.unspentPoints == 5, "nível concede cinco pontos");
        Check(current.monsters[0].health == 0 && current.monsters[0].respawnAt > now, "NPC morto aguarda respawn");
        string sameCommandId = attack.commandId;
        int xp = current.character.experience;
        var duplicate = Send(world, token, now += .1, attack);
        Check(duplicate.character.experience == xp && duplicate.monsters[0].health == 0, "replay não duplica morte/XP");
        var before = duplicate.character;
        var restarted = new GameWorld(new SaveStore(path), EmptyGrid(), now);
        var restored = restarted.Connect(ConnectRequest(key), now += .1);
        Check(restored.character.level == before.level && restored.character.experience == before.experience && restored.character.health == before.health, "nível/XP/HP persistidos");
        Check(restored.monsters[0].health == 0 && restored.monsters[0].respawnAt == duplicate.monsters[0].respawnAt, "morte do NPC persistida");
        var unarmed = Command("unequip"); unarmed.slot = 0;
        var basic = Command("ability"); var heavy = Command("ability"); heavy.ability = 1;
        var result = Send(restarted, restored.sessionToken, now += 1, unarmed, basic, heavy);
        Check(result.results[0].accepted && !result.results[1].accepted && !result.results[2].accepted, "ataques exigem espada");
    }

    private static (GameWorld world, CollisionGrid grid, OnlineSnapshot windup, double resolvesAt, string token) MonsterWindup(
        string path, int health = 240, float playerX = 0, float playerZ = 0)
    {
        const double now = 2500;
        string key = DeviceKey();
        var grid = EmptyGrid();
        var store = new SaveStore(path);
        var world = new GameWorld(store, grid, now);
        world.Connect(ConnectRequest(key), now);
        // Isolate strike-event timing from the separate proximity-aggro tests.
        // A real hero hit will acquire this fixture target and start its windup.
        var saved = store.Load();
        foreach(var config in new[]{saved.world.economy.active,saved.world.economy.releases[0].config})
            foreach(var profile in config.monsters)profile.aggroRadius=0;
        saved.players[0].x = playerX; saved.players[0].z = playerZ;
        saved.world.monsters[0].x = playerX; saved.world.monsters[0].z = playerZ + 1.5f;
        saved.world.monsters[0].health = health;
        store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        world = new GameWorld(store, grid, now);
        var connected = world.Connect(ConnectRequest(key), now);
        var basic = Command("ability");
        Send(world, connected.sessionToken, now + .1, basic);
        var windup = Send(world, connected.sessionToken, now + .24);
        Check(windup.monsters[0].windupUntil > now + .24, "ataque real inicia preparo do monstro");
        return (world, grid, windup, windup.monsters[0].windupUntil, connected.sessionToken);
    }

    private static void TestMonsterStrikeEvents(string directory)
    {
        var hit = MonsterWindup(Path.Combine(directory, "monster-hit.db"));
        var impact = Send(hit.world, hit.token, hit.resolvesAt + .01);
        var strike = impact.events.Single(e => e.kind == "monsterStrike");
        var damage = impact.events.Single(e => e.kind == "playerHit");
        Check(strike.sourceId == "troll-0" && strike.targetId == impact.playerId && strike.ability == 0,
            "golpe do monstro identifica atacante e alvo");
        Check(strike.amount == damage.amount && strike.amount == hit.windup.character.health - impact.character.health,
            "evento visual reconcilia exatamente o dano autoritativo");
        Check(strike.eventId < damage.eventId && strike.amount == 10, "um golpe causa um único dano sem alterar balanceamento");
        var after = hit.world.Commands(hit.token, new OnlineCommandBatch
        {
            afterEventId = impact.latestEventId, commands = Array.Empty<OnlineCommand>()
        }, hit.resolvesAt + .02);
        Check(after.events.Length == 0 && after.character.health == impact.character.health,
            "cursor confirmado não reapresenta golpe nem reaplica dano");

        var miss = MonsterWindup(Path.Combine(directory, "monster-miss.db"));
        var stepBack = Command("move"); stepBack.z = -.2f;
        Check(Send(miss.world, miss.token, miss.resolvesAt - .4, stepBack).results[0].accepted,
            "desvio curto durante preparo é permitido");
        var missed = Send(miss.world, miss.token, miss.resolvesAt + .01);
        Check(missed.events.Count(e => e.kind == "monsterStrike" && e.amount == 0) == 1 &&
            !missed.events.Any(e => e.kind == "playerHit") && missed.character.health == miss.windup.character.health,
            "golpe concluído fora do alcance produz efeito sem dano");

        var wall = MonsterWindup(Path.Combine(directory, "monster-wall.db"));
        int column = (int)Math.Floor((0 - wall.grid.originX) / wall.grid.cellSize);
        int row = (int)Math.Floor((.75f - wall.grid.originZ) / wall.grid.cellSize);
        wall.grid.blocked[row * wall.grid.width + column] = 1;
        var obstructed = Send(wall.world, wall.token, wall.resolvesAt + .01);
        Check(obstructed.events.Count(e => e.kind == "monsterStrike" && e.amount == 0) == 1 &&
            !obstructed.events.Any(e => e.kind == "playerHit") && obstructed.character.health == wall.windup.character.health,
            "obstáculo após preparo mantém efeito e bloqueia dano");

        var canceled = MonsterWindup(Path.Combine(directory, "monster-canceled.db"));
        var escape = Command("move"); escape.z = -.5f;
        Check(Send(canceled.world, canceled.token, canceled.resolvesAt - .4, escape).results[0].accepted,
            "saída do preparo respeita movimento válido");
        var escaped = Send(canceled.world, canceled.token, canceled.resolvesAt - .3);
        Check(escaped.monsters[0].windupUntil == 0 && !escaped.events.Any(e => e.kind is "monsterStrike" or "playerHit"),
            "distância acima de 1,8 m cancela preparo sem evento de golpe");

        var leashed = MonsterWindup(Path.Combine(directory, "monster-leash.db"), playerX: -2.6f, playerZ: -4.1f);
        var leaveTerritory = Command("move"); leaveTerritory.x = -2.6f; leaveTerritory.z = -4.3f;
        Check(Send(leashed.world, leashed.token, leashed.resolvesAt - .4, leaveTerritory).results[0].accepted,
            "saída do território respeita movimento válido");
        var returning = Send(leashed.world, leashed.token, leashed.resolvesAt - .3);
        Check(returning.monsters[0].returning && returning.monsters[0].windupUntil == 0 &&
            !returning.events.Any(e => e.kind is "monsterStrike" or "playerHit"),
            "limite de território cancela preparo sem golpe ou dano");

        var killed = MonsterWindup(Path.Combine(directory, "monster-killed.db"), 36);
        var finishing = Command("ability");
        Check(Send(killed.world, killed.token, killed.resolvesAt - .12, finishing).results[0].accepted,
            "herói pode finalizar monstro durante preparo");
        var defeated = Send(killed.world, killed.token, killed.resolvesAt + .02);
        Check(defeated.monsters[0].health == 0 && defeated.monsters[0].windupUntil == 0 &&
            !defeated.events.Any(e => e.kind is "monsterStrike" or "playerHit"),
            "morte do monstro cancela preparo sem golpe ou dano fantasma");
    }

    private static void TestCollision(string path)
    {
        var grid = EmptyGrid();
        for (int row = 0; row < grid.height; row++) grid.blocked[row * grid.width + 76] = 1;
        Check(!grid.ClearLine(0, 0, 2, 0), "raycast atravessando parede bloqueado");
        Check(grid.ClearLine(0, 0, 0, 1), "raycast livre aceito");
        var world = new GameWorld(new SaveStore(path), grid, 3000);
        var connect = world.Connect(ConnectRequest(DeviceKey()), 3000);
        var move = Command("move"); move.x = 1.2f; move.running = true;
        Check(!Send(world, connect.sessionToken, 3000.25, move).results[0].accepted, "movimento não atravessa parede apesar da velocidade válida");
        var outside = Command("move"); outside.x = 18;
        Check(!Send(world, connect.sessionToken, 3001, outside).results[0].accepted, "limites do mapa");
    }

    private static void TestConcurrency(string path)
    {
        var world = new GameWorld(new SaveStore(path), EmptyGrid(), 4000);
        var connected = world.Connect(ConnectRequest(DeviceKey()), 4000);
        var same = Command("allocate"); same.amount = 1;
        Parallel.For(0, 8, _ => Send(world, connected.sessionToken, 4000.1, same));
        var snapshot = Send(world, connected.sessionToken, 4000.2);
        Check(snapshot.character.attackPoints == 1 && snapshot.character.unspentPoints == 4, "comando concorrente executa uma vez");
        var commands = Enumerable.Range(0, 6).Select(_ => Command("allocate")).ToArray();
        Parallel.ForEach(commands, command => Send(world, connected.sessionToken, 4000.3, command));
        snapshot = Send(world, connected.sessionToken, 4000.4);
        Check(snapshot.character.attackPoints == 5 && snapshot.character.unspentPoints == 0, "concorrência nunca gera pontos negativos");
    }

    private static void TestAbilitiesAndDeath(string path)
    {
        double now = 5000;
        var store = new SaveStore(path);
        var grid = EmptyGrid();
        var world = new GameWorld(store, grid, now);
        string key = DeviceKey();
        var connected = world.Connect(ConnectRequest(key), now);
        // Seed a wounded character through the trusted database fixture, never a public API.
        var saved = store.Load();
        var trusted = PlayerProgression.FromSaveData(saved.players[0].character);
        trusted.TryAllocate(HeroAttribute.Intelligence, 5);
        trusted.ReceiveDamage(70);
        saved.players[0].character = trusted.CaptureSaveData();
        store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        world = new GameWorld(store, grid, now);
        connected = world.Connect(ConnectRequest(key), now);
        string token = connected.sessionToken;
        var guard = Command("ability"); guard.ability = 2;
        var guarded = Send(world, token, now += .1, guard);
        Check(guarded.results[0].accepted && Math.Abs(guarded.self.guardDuration - 4.1) < .001, "inteligência escala duração da guarda");
        var fury = Command("ability"); fury.ability = 4;
        var boosted = Send(world, token, now += .21, fury);
        Check(boosted.results[0].accepted && Math.Abs(boosted.self.furyDuration - 8.2) < .001 && boosted.self.guardUntil > now, "guarda e fúria coexistem");
        var heal = Command("ability"); heal.ability = 3;
        var healed = Send(world, token, now += .21, heal);
        Check(healed.results[0].accepted && healed.character.health == 65 && healed.self.healUntil > now, "cura restaura 35% e publica efeito");
        var secondHeal = Command("ability"); secondHeal.ability = 3;
        Check(!Send(world, token, now += .21, secondHeal).results[0].accepted, "recarga impede cura repetida");
        var teleportedHeal = world.Connect(ConnectRequest(key), now += .1);
        Check(teleportedHeal.character.health == 65 && teleportedHeal.self.mana < 40, "relogin preserva ferimentos e mana");
        token = teleportedHeal.sessionToken;
        // Approach the two normal trolls, use the area strike, and let their server AI defeat the hero.
        for (int step = 1; step <= 3; step++)
        {
            var move = Command("move"); move.z = .7f * step; move.running = true;
            Check(Send(world, token, now += .2, move).results[0].accepted, "aproximação antes do corte em área");
        }
        var heavy = Command("ability"); heavy.ability = 1;
        var casting = Send(world, token, now += .21, heavy);
        Check(casting.results[0].accepted, "corte em área válido");
        var hit = Send(world, token, now += .14);
        Check(hit.monsters[0].health < 240 && hit.monsters[2].health < 240, "corte em área acerta dois alvos");
        Check(hit.events.Count(e => e.kind == "hit" && e.ability == 1) == 2, "um evento de dano por alvo");
        var dead = hit;
        for (int i = 0; i < 240 && dead.character.health > 0; i++) dead = Send(world, token, now += .1);
        Check(dead.character.health == 0 && dead.self.guardUntil == 0 && dead.self.furyUntil == 0 && dead.self.healUntil == 0, "derrota encerra todos os efeitos");
        int[] monsterHp = dead.monsters.Select(m => m.health).ToArray();
        var reloggedDead = world.Connect(ConnectRequest(key), now += .1);
        Check(reloggedDead.character.health == 0, "reconectar não ressuscita");
        var respawn = Command("respawn");
        var reborn = Send(world, reloggedDead.sessionToken, now += .1, respawn);
        Check(reborn.results[0].accepted && reborn.character.health == 100 && reborn.self.x == 0 && reborn.self.z == 0, "R ressuscita no nascimento");
        Check(store.Load().world.monsters.Select(m => m.health).SequenceEqual(monsterHp), "R não reinicia monstros compartilhados mesmo ao voltar à cidade");
        Check(reborn.character.intelligencePoints == 5 && reborn.character.unspentPoints == 0, "ressuscitar preserva atributos");
    }

    private static void TestSchemaRefusal(string path)
    {
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery();
        }
        Throws<InvalidDataException>(() => new SaveStore(path), "versão futura não é resetada");
    }

    private static void TestMovementBudget(string path)
    {
        double now = 6000;
        var world = new GameWorld(new SaveStore(path), EmptyGrid(), now);
        string key = DeviceKey();
        var connected = world.Connect(ConnectRequest(key), now);
        var move = Command("move"); move.x = 1; move.running = true;
        var first = Send(world, connected.sessionToken, now += .25, move);
        Check(first.results[0].accepted, "crédito permite pacote com jitter");
        var second = Command("move"); second.x = 1.6f; second.running = true;
        Check(!Send(world, connected.sessionToken, now, second).results[0].accepted, "rajada não ultrapassa crédito restante");
        var third = Command("move"); third.x = 1.5f; third.running = true;
        Check(Send(world, connected.sessionToken, now, third).results[0].accepted, "rajada pode consumir saldo exato");
        var fourth = Command("move"); fourth.x = 1.51f; fourth.running = true;
        Check(!Send(world, connected.sessionToken, now, fourth).results[0].accepted, "nenhuma tolerância nova por comando");
        var relog = world.Connect(ConnectRequest(key), now);
        var fifth = Command("move"); fifth.x = 1.51f; fifth.running = true;
        Check(!Send(world, relog.sessionToken, now, fifth).results[0].accepted, "relogin não restaura crédito");
        var restart = new GameWorld(new SaveStore(path), EmptyGrid(), now);
        relog = restart.Connect(ConnectRequest(key), now);
        var sixth = Command("move"); sixth.x = 1.51f; sixth.running = true;
        Check(!Send(restart, relog.sessionToken, now, sixth).results[0].accepted, "reinício preserva crédito gasto");
        var refill = Command("move"); refill.x = 2.05f; refill.running = true;
        Check(Send(restart, relog.sessionToken, now += .1, refill).results[0].accepted, "só tempo decorrido recompõe crédito");
    }

    private static void TestHealingRounding(string path)
    {
        double now = 7000;
        var store = new SaveStore(path);
        var world = new GameWorld(store, EmptyGrid(), now);
        string key = DeviceKey();
        var connected = world.Connect(ConnectRequest(key), now);
        var heal = Command("ability"); heal.ability = 3;
        var full = Send(world, connected.sessionToken, now += .1, heal);
        Check(!full.results[0].accepted && full.self.mana == 100 && full.self.cooldownUntil[3] == 0, "cura cheia rejeita sem consumir mana ou recarga");
        var saved = store.Load();
        var stats = PlayerProgression.FromSaveData(saved.players[0].character);
        Check(stats.Equipment.TryAddItem("steel-head", "test-healing-head", out _), "fixture adquire elmo com instância própria");
        Check(stats.Equipment.TryEquip("test-healing-head", EquipmentSlot.Head, out _), "fixture equipa elmo");
        stats.ReceiveDamage(71);
        Check(stats.MaxHealth == 108 && stats.Health == 30, "fixture de arredondamento de cura");
        saved.players[0].character = stats.CaptureSaveData();
        store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        world = new GameWorld(store, EmptyGrid(), now);
        connected = world.Connect(ConnectRequest(key), now);
        heal = Command("ability"); heal.ability = 3;
        var result = Send(world, connected.sessionToken, now += .1, heal);
        Check(result.results[0].accepted && result.character.health == 67, "cura usa floor(108*35/100)=37 como Unity");
    }

    private static void TestCorruptionRefusal(string path)
    {
        var store = new SaveStore(path);
        var world = new GameWorld(store, EmptyGrid(), 8000);
        world.Connect(ConnectRequest(DeviceKey()), 8000);
        var saved = store.Load();
        saved.players[0].character.level = 99;
        store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        Throws<ArgumentException>(() => new GameWorld(new SaveStore(path), EmptyGrid(), 8001), "snapshot inválido não vira personagem novo");
        Check(store.Load().players[0].character.level == 99, "snapshot inválido preservado para recuperação manual");
        string corruptPath = path + ".corrupt";
        const string bytes = "arquivo inválido preservado";
        File.WriteAllText(corruptPath, bytes);
        Throws<Microsoft.Data.Sqlite.SqliteException>(() => new SaveStore(corruptPath), "arquivo corrompido não é reiniciado");
        Check(File.ReadAllText(corruptPath) == bytes, "arquivo corrompido permanece intacto");
    }
}
