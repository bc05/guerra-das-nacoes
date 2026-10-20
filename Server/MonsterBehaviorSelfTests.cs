using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

internal static class MonsterBehaviorSelfTests
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("MONSTER-BEHAVIOR-TEST: " + message);
    }

    public static void Run()
    {
        assertions = 0;
        string directory = Path.Combine(Path.GetTempPath(), "gdn-monster-behavior-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestRanged(Path.Combine(directory, "ranged.db"));
            TestRangedDodgeAndCover(directory);
            TestArea(Path.Combine(directory, "area.db"));
            TestCancellation(directory);
            Console.WriteLine($"PASS: {assertions} verificações de preparo, distância, área, esquiva, cobertura e cancelamento de monstros.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Encounter
    {
        public SaveStore store;
        public GameWorld world;
        public CollisionGrid grid;
        public string monsterId;
        public string[] keys, tokens;
        public OnlineSnapshot warning;
        public OnlineMonsterState monster;
        public double resolvesAt;
    }

    private static OnlineSnapshot Send(Encounter encounter, double now, long after = 0, params OnlineCommand[] commands) =>
        encounter.world.Commands(encounter.tokens[0], new() { afterEventId = after, commands = commands }, now);

    private static OnlineCommand Move(float x, float z) => new()
    {
        commandId = Guid.NewGuid().ToString("D"), type = "move", x = x, z = z, running = true
    };

    // All persisted changes belong to a newly created temporary database. Reconnects and commands
    // exercise the production scheduler and movement validation with an explicit server clock.
    private static Encounter Prepare(string path, bool area = false, bool crowd = false,
        bool nearLeash = false, bool fragile = false)
    {
        const double now = 12000;
        var fixture = new Encounter
        {
            store = new SaveStore(path),
            grid = new CollisionGrid { originX = -50, originZ = -20, cellSize = .5f, width = 200, height = 200, blocked = new int[200 * 200] },
            monsterId = area ? "troll-brute-0" : "orc-shaman-0"
        };
        fixture.keys = Enumerable.Range(0, crowd ? 4 : 1)
            .Select(_ => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).ToArray();
        fixture.world = new GameWorld(fixture.store, fixture.grid, now);
        var playerIds = fixture.keys.Select((key, index) => fixture.world.Connect(new()
        {
            deviceKey = key, displayName = "Verificador " + index
        }, now).playerId).ToArray();
        var saved = fixture.store.Load();
        var profile = MonsterCatalog.Resolve(saved.world.economy.active).Single(m => m.id == fixture.monsterId);
        // Disable unrelated encounters, then let this fixture acquire its target normally.
        foreach (var config in saved.world.economy.releases.Select(release => release.config).Append(saved.world.economy.active))
        {
            foreach (var monster in config.monsters) monster.aggroRadius = 0;
            foreach (var species in config.monsterSpecies) species.aggroRadius = 0;
            var spawn = config.monsterSpawns.FirstOrDefault(s => s.id == fixture.monsterId);
            if (spawn != null) config.monsterSpecies.Single(s => s.id == spawn.speciesId).aggroRadius = 7;
        }
        for (int index = 0; index < playerIds.Length; index++)
        {
            var player = saved.players.Single(p => p.id == playerIds[index]);
            player.mapId = index == 3 ? "city" : "ruins";
            player.x = index switch
            {
                1 => profile.x - 2.3f,
                2 => profile.x + 3.5f,
                3 => 0,
                _ => profile.x + (nearLeash ? profile.leashRadius - .2f : area ? 1.8f : fragile ? 2.5f : 5)
            };
            player.z = index == 3 ? 0 : profile.z;
        }
        var encounter = saved.world.monsters.Single(m => m.id == fixture.monsterId);
        encounter.targetId = null;
        encounter.windupUntil = encounter.nextAttackAt = 0;
        if (nearLeash) encounter.x = saved.players.Single(p => p.id == playerIds[0]).x - 5;
        if (fragile) encounter.health = 1;
        fixture.store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        fixture.world = new GameWorld(fixture.store, fixture.grid, now);
        fixture.tokens = fixture.keys.Select((key, index) => fixture.world.Connect(new()
        {
            deviceKey = key, displayName = "Verificador " + index
        }, now).sessionToken).ToArray();
        fixture.warning = Send(fixture, now + .1);
        fixture.monster = fixture.warning.monsters.Single(m => m.id == fixture.monsterId);
        fixture.resolvesAt = fixture.monster.windupUntil;
        if (fixture.resolvesAt <= now + .1)
            throw new InvalidOperationException("Fixture não iniciou preparo do monstro esperado.");
        return fixture;
    }

    private static void TestRanged(string path)
    {
        var fixture = Prepare(path);
        var warning = fixture.warning;
        Check(fixture.monster.behavior == "ranged" && fixture.monster.attackRange == 7 &&
            Math.Abs(fixture.monster.attackX - warning.self.x) < .001 &&
            Math.Abs(fixture.monster.x - warning.self.x) >= 4.99,
            "xamã anuncia posição do alvo a cinco metros sem perseguir até corpo a corpo");
        var preparing = Send(fixture, fixture.resolvesAt - .01, warning.latestEventId);
        Check(preparing.character.health == warning.character.health && !preparing.events.Any(e => e.kind == "playerHit"),
            "preparo não aplica dano antes do tempo anunciado");
        var impact = Send(fixture, fixture.resolvesAt + .01, preparing.latestEventId);
        var strike = impact.events.Single(e => e.kind == "monsterStrike");
        Check(strike.ability == 2 && strike.amount == fixture.monster.damage &&
            impact.events.Count(e => e.kind == "playerHit" && e.targetId == warning.playerId) == 1 &&
            warning.character.health - impact.character.health == strike.amount,
            "ataque à distância conclui com um dano autoritativo e evento correspondente");
        var cooldown = Send(fixture, fixture.resolvesAt + .2, impact.latestEventId);
        Check(cooldown.monsters.Single(m => m.id == fixture.monsterId).windupUntil == 0 &&
            cooldown.character.health == impact.character.health && cooldown.events.Length == 0,
            "intervalo impede preparo imediato ou dano repetido");
        var next = Send(fixture, fixture.resolvesAt + 1, cooldown.latestEventId);
        Check(next.monsters.Single(m => m.id == fixture.monsterId).windupUntil > next.serverTime &&
            next.character.health == impact.character.health,
            "depois da recarga o próximo ataque exige novo preparo completo");
    }

    private static void TestRangedDodgeAndCover(string directory)
    {
        var dodge = Prepare(Path.Combine(directory, "ranged-dodge.db"));
        var moved = Send(dodge, dodge.resolvesAt - .4, dodge.warning.latestEventId,
            Move(dodge.warning.self.x + 1.3f, dodge.warning.self.z));
        var avoided = Send(dodge, dodge.resolvesAt + .01, moved.latestEventId);
        Check(moved.results.Single().accepted && avoided.character.health == dodge.warning.character.health &&
            avoided.events.Count(e => e.kind == "monsterStrike" && e.amount == 0) == 1 &&
            !avoided.events.Any(e => e.kind == "playerHit"),
            "movimento autorizado para fora da posição marcada evita o disparo");

        var cover = Prepare(Path.Combine(directory, "ranged-cover.db"));
        int column = (int)Math.Floor(((cover.monster.x + cover.warning.self.x) / 2 - cover.grid.originX) / cover.grid.cellSize);
        int row = (int)Math.Floor((cover.monster.z - cover.grid.originZ) / cover.grid.cellSize);
        cover.grid.blocked[row * cover.grid.width + column] = 1;
        var blocked = Send(cover, cover.resolvesAt + .01, cover.warning.latestEventId);
        Check(blocked.character.health == cover.warning.character.health &&
            blocked.events.Count(e => e.kind == "monsterStrike" && e.amount == 0) == 1 &&
            !blocked.events.Any(e => e.kind == "playerHit"),
            "parede na linha de visão bloqueia dano de disparo já preparado");
    }

    private static void TestArea(string path)
    {
        var fixture = Prepare(path, area: true, crowd: true);
        var warning = fixture.warning;
        Check(fixture.monster.behavior == "area" && fixture.monster.areaRadius == 3 &&
            fixture.monster.attackX == fixture.monster.x && fixture.monster.attackZ == fixture.monster.z,
            "troll do bosque prepara área fixa de três metros ao seu redor");
        var impact = Send(fixture, fixture.resolvesAt + .01, warning.latestEventId);
        var strike = impact.events.Single(e => e.kind == "monsterStrike");
        var hits = impact.events.Where(e => e.kind == "playerHit").ToArray();
        Check(strike.ability == 3 && hits.Length == 2 && hits.All(e => e.ability == 3 && e.amount == fixture.monster.damage) &&
            strike.amount == hits.Sum(e => e.amount),
            "golpe em área acerta os dois jogadores próximos uma única vez cada");
        var saved = fixture.store.Load().players;
        var outside = saved.Single(p => p.displayName == "Verificador 2");
        var city = saved.Single(p => p.displayName == "Verificador 3");
        Check(outside.character.health == 100 && city.character.health == 100 &&
            !hits.Any(e => e.targetId == outside.id || e.targetId == city.id),
            "jogador fora do raio e jogador na cidade preservam vida");

        var dodge = Prepare(path + ".dodge", area: true);
        var moved = Send(dodge, dodge.resolvesAt - .4, dodge.warning.latestEventId,
            Move(dodge.warning.self.x + 1.3f, dodge.warning.self.z));
        var missed = Send(dodge, dodge.resolvesAt + .01, moved.latestEventId);
        Check(moved.results.Single().accepted && missed.character.health == dodge.warning.character.health &&
            missed.events.Count(e => e.kind == "monsterStrike" && e.amount == 0) == 1,
            "jogador pode sair do raio do troll durante o preparo");
    }

    private static void TestCancellation(string directory)
    {
        var fleeing = Prepare(Path.Combine(directory, "leash.db"), nearLeash: true);
        var moved = Send(fleeing, fleeing.resolvesAt - .5, fleeing.warning.latestEventId,
            Move(fleeing.warning.self.x + .4f, fleeing.warning.self.z));
        Check(moved.results.Single().accepted, "fuga respeita o orçamento de movimento do servidor");
        var escaped = Send(fleeing, fleeing.resolvesAt + .01, moved.latestEventId);
        var escapedMonster = escaped.monsters.Single(m => m.id == fleeing.monsterId);
        Check(escapedMonster.returning && escapedMonster.windupUntil == 0 && escapedMonster.targetId == null &&
            escaped.character.health == fleeing.warning.character.health &&
            !escaped.events.Any(e => e.kind is "monsterStrike" or "playerHit"),
            "saída do território cancela preparo e devolve monstro sem dano");

        var killed = Prepare(Path.Combine(directory, "death.db"), fragile: true);
        var attack = new OnlineCommand
        {
            commandId = Guid.NewGuid().ToString("D"), type = "ability", targetId = killed.monsterId
        };
        var cast = Send(killed, killed.resolvesAt - .2, killed.warning.latestEventId, attack);
        Check(cast.results.Single().accepted, "jogador pode finalizar xamã durante o preparo");
        var defeated = Send(killed, killed.resolvesAt + .01, cast.latestEventId);
        var defeatedMonster = defeated.monsters.Single(m => m.id == killed.monsterId);
        // This kill grants enough XP to level up, which legitimately restores the hero's new maximum HP.
        Check(defeatedMonster.health == 0 && defeatedMonster.windupUntil == 0 &&
            defeated.character.health == defeated.self.maxHealth &&
            !defeated.events.Any(e => e.kind is "monsterStrike" or "playerHit"),
            "morte do xamã cancela disparo pendente sem dano fantasma");

        var restarted = Prepare(Path.Combine(directory, "restart.db"), area: true);
        restarted.world = new GameWorld(restarted.store, restarted.grid, restarted.resolvesAt - .05);
        var connected = restarted.world.Connect(new() { deviceKey = restarted.keys[0], preserveExistingName = true }, restarted.resolvesAt - .05);
        restarted.tokens[0] = connected.sessionToken;
        Check(connected.character.health == restarted.warning.character.health &&
            connected.monsters.Single(m => m.id == restarted.monsterId).windupUntil == 0,
            "reinício cancela preparo anterior quando a sessão do alvo ainda não existe");
        var afterRestart = Send(restarted, restarted.resolvesAt + .05, connected.latestEventId);
        Check(afterRestart.character.health == connected.character.health &&
            !afterRestart.events.Any(e => e.kind is "monsterStrike" or "playerHit"),
            "reconexão não reproduz golpe nem duplica dano do preparo cancelado");
    }
}
