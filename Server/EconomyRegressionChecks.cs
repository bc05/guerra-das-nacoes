using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

// Regression checks use isolated databases and trusted fixtures; no running game state is touched.
public static class EconomyRegressionChecks
{
    private static int assertions;
    private static CollisionGrid Grid() => new() { originX = -18, originZ = -18, cellSize = .25f,
        width = 144, height = 144, blocked = new int[144 * 144] };
    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static OnlineCommand Command(string type) => new() { type = type, commandId = Guid.NewGuid().ToString("D") };
    private static OnlineSnapshot Send(GameWorld world, string token, double now, params OnlineCommand[] commands) =>
        world.Commands(token, new OnlineCommandBatch { commands = commands }, now);
    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("ECONOMY-REGRESSION: " + description);
    }
    private static void Throws<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, description); return; }
        Check(false, description);
    }

    public static void Run()
    {
        assertions = 0;
        string directory = Path.Combine(Path.GetTempPath(), "gdn-economy-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckLegacyCatalogDomain();
            CheckPotionExact(Path.Combine(directory, "potion.db"));
            CheckZeroRewards(Path.Combine(directory, "zero-rewards.db"));
            CheckInvalidSchedule(Path.Combine(directory, "invalid-schedule.db"));
            CheckInvalidDropConfiguration(Path.Combine(directory, "invalid-config.db"));
            CheckForgedOwnership(Path.Combine(directory, "legacy-forgery.db"), true);
            CheckForgedOwnership(Path.Combine(directory, "instance-forgery.db"), false);
            Console.WriteLine($"PASS: {assertions} regressões de poções, recompensas zero, publicação segura e patrimônio inválido.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void CheckLegacyCatalogDomain()
    {
        EquipmentItem[] original=EquipmentCatalog.GetLegacyDefinitions();
        Check(original.Length==35&&EquipmentCatalog.GetDefinitions().Length>=38,
            "as três relíquias instaladas não ampliam o domínio histórico de 35 peças");
        var ids=original.Select(i=>i.DefinitionId).ToHashSet(StringComparer.Ordinal);
        Check(ids.Count==35&&original.All(i=>i.DefinitionRevision==1&&!i.DefinitionId.StartsWith("sovereign-",StringComparison.Ordinal)),
            "domínio v1 contém apenas 35 IDs originais únicos na revisão um");
        var legacy=new CharacterSaveData { schemaVersion=1,level=1,health=100,unspentPoints=5,
            inventoryItemIds=original.Where(i=>!i.DefinitionId.StartsWith("training-",StringComparison.Ordinal)).Select(i=>i.DefinitionId).ToArray(),
            equipped=original.Where(i=>i.DefinitionId.StartsWith("training-",StringComparison.Ordinal)).Select(i=>new EquipmentSlotSave { slot=(int)i.Slot,itemId=i.DefinitionId }).ToArray() };
        CharacterSaveData migrated=PlayerProgression.FromSaveData(legacy).CaptureSaveData();
        Check(migrated.schemaVersion==2&&migrated.itemInstances.Length==35&&migrated.itemInstances.All(i=>ids.Contains(i.definitionId)&&i.bound&&i.origin=="legacy"),
            "migração real preserva somente as 35 peças vinculadas sem conceder drops novos");
        CharacterSaveData inflated=EconomyRules.Clone(legacy);
        inflated.inventoryItemIds=inflated.inventoryItemIds.Concat(new[]{"sovereign-sword","sovereign-shield","sovereign-necklace"}).ToArray();
        Throws<ArgumentException>(()=>PlayerProgression.FromSaveData(inflated),"v1 com 38 peças continua recusado");
        CharacterSaveData forged=EconomyRules.Clone(legacy);forged.inventoryItemIds[0]="sovereign-sword";
        Throws<ArgumentException>(()=>PlayerProgression.FromSaveData(forged),"substituir uma das 35 peças por relíquia v2 continua recusado");
        var owner=new PlayerProgression();
        foreach(string id in new[]{"sovereign-sword","sovereign-shield","sovereign-necklace"})
            Check(owner.Equipment.TryAddItem(id,"legitimate-"+id,out _,false,"world-boss"),"instância legítima de relíquia é concedida em v2");
        CharacterSaveData saved=owner.CaptureSaveData();
        CharacterSaveData restored=PlayerProgression.FromSaveData(saved).CaptureSaveData();
        Check(WireJson.Write(restored)==WireJson.Write(saved)&&restored.itemInstances.Length==15&&restored.inventoryItemIds.Length==3,
            "carga v2 preserva precisamente as três relíquias legítimas e todo patrimônio");
        foreach(string id in new[]{"sovereign-sword","sovereign-shield","sovereign-necklace"})
        {
            var item=restored.itemInstances.Single(i=>i.definitionId==id);
            Check(item.instanceId=="legitimate-"+id&&!item.bound&&item.origin=="world-boss"&&item.definitionRevision==1,
                "restauração v2 mantém identidade origem vínculo e revisão da relíquia");
        }
    }

    private static void CheckPotionExact(string path)
    {
        var store = new SaveStore(path);
        var world = new GameWorld(store, Grid(), 10000);
        string key = Key();
        world.Connect(new OnlineConnectRequest { deviceKey = key }, 10000);
        var saved = store.Load();
        SavedPlayer player = saved.players.Single();
        var progression = PlayerProgression.FromSaveData(player.character);
        progression.ReceiveDamage(80);
        progression.TryAddHealthPotions(2);
        player.character = progression.CaptureSaveData();
        player.mapId = "city"; player.x = 3; player.z = -4;
        store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        world = new GameWorld(store, Grid(), 10001);
        OnlineSnapshot connected = world.Connect(new OnlineConnectRequest { deviceKey = key }, 10001);
        OnlineCommand potion = Command("potion");
        OnlineSnapshot healed = Send(world, connected.sessionToken, 10001.1, potion);
        Check(healed.results.Single().accepted && healed.character.health == 70 && healed.character.healthPotions == 1,
            "uma poção cura exatamente metade da vida máxima, sem segunda aplicação");
        Check(healed.events.Any(e => e.kind == "heal" && e.amount == 50),
            "evento da poção informa a cura total realmente aplicada");
        OnlineSnapshot replayed = Send(world, connected.sessionToken, 10001.2, potion);
        Check(replayed.character.health == 70 && replayed.character.healthPotions == 1,
            "repetir comando da poção não consome nem cura novamente");
        var capped = Send(world, connected.sessionToken, 10002.2, Command("potion"));
        Check(capped.character.health == 100 && capped.character.healthPotions == 0 &&
            capped.events.Last(e => e.kind == "heal").amount == 30,
            "cura restante é limitada ao máximo e evento reporta o valor efetivo");
    }

    private static void CheckZeroRewards(string path)
    {
        var store = new SaveStore(path);
        var world = new GameWorld(store, Grid(), 11000);
        string key = Key();
        world.Connect(new OnlineConnectRequest { deviceKey = key }, 11000);
        var saved = store.Load();
        SavedPlayer player = saved.players.Single();
        player.x = -2.6f; player.z = 1.8f; player.yaw = 0;
        SavedMonster monster = saved.world.monsters.Single(m => m.id == "troll-0");
        monster.health = 1;
        foreach (EconomyConfig config in new[] { saved.world.economy.active, saved.world.economy.releases[0].config })
        {
            EconomyLootTable table = config.lootTables.Single(t => t.id == "loot-saqueador");
            table.coinsMin = table.coinsMax = 0;
            table.scrapChancePpm = 1000000;
            table.scrapMin = table.scrapMax = 0;
            table.groups = Array.Empty<EconomyLootGroup>();
        }
        store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        world = new GameWorld(store, Grid(), 11001);
        OnlineSnapshot connected = world.Connect(new OnlineConnectRequest { deviceKey = key }, 11001);
        OnlineCommand hit = Command("ability"); hit.ability = 0;
        Check(Send(world, connected.sessionToken, 11001.1, hit).results.Single().accepted,
            "ataque de monstro sem recompensa monetária é aceito");
        OnlineSnapshot killed = Send(world, connected.sessionToken, 11001.3);
        Check(killed.monsters.Single(m => m.id == "troll-0").health == 0 && killed.character.coins == 0 &&
            killed.character.scrap == 0 && killed.character.level == 2,
            "zero moedas e zero sucata confirmam morte e experiência sem rollback");
        Check(store.Load().world.economy.rewardedDeaths.Length == 1 &&
            store.Load().world.economy.ledger.All(l => l.amount == 0 && l.balance == 0),
            "morte zero possui recibo sem criar moedas no histórico");
        world = new GameWorld(store, Grid(), 11002);
        OnlineSnapshot restarted = world.Connect(new OnlineConnectRequest { deviceKey = key }, 11002);
        Check(restarted.character.level == 2 && restarted.character.coins == 0 &&
            store.Load().world.economy.rewardedDeaths.Length == 1,
            "morte de recompensa zero permanece confirmada após reinício");
    }

    private static void CheckInvalidSchedule(string path)
    {
        var store = new SaveStore(path);
        var world = new GameWorld(store, Grid(), 12000);
        AdminSnapshot before = (AdminSnapshot)world.AdminState(12000);
        EconomyConfig invalid = EconomyRules.Clone(before.draft);
        invalid.items.Single(i => i.definitionId == "steel-head").minAttack = 1000;
        Check(!world.AdminValidate(invalid).valid, "requisito fora do catálogo é rejeitado antes de agendar");
        world.AdminDraft(new AdminDraftRequest { baseRevision = before.draftRevision, config = invalid,
            reason = "Regressão de agendamento inválido", requestId = Guid.NewGuid().ToString("D") }, 12000);
        AdminSnapshot draft = (AdminSnapshot)world.AdminState(12000);
        Throws<GameApiException>(() => world.AdminPublish(new AdminPublishRequest {
            baseRevision = draft.draftRevision, requestId = Guid.NewGuid().ToString("D"),
            reason = "Esta publicação deve ser recusada", activateAtUnixMs = 12010000 }, 12000),
            "publicação agendada inválida não chega ao commit");
        Check(store.Load().world.economy.releases.Length == before.history.Length &&
            store.Load().world.economy.active.revision == before.active.revision,
            "rejeição preserva versões confirmadas e configuração ativa");
        world = new GameWorld(store, Grid(), 12001);
        Check(((AdminSnapshot)world.AdminState(12001)).active.revision == before.active.revision,
            "rascunho inválido não impede reabrir o servidor");
        AdminSnapshot recovered = (AdminSnapshot)world.AdminState(12001);
        EconomyConfig valid = EconomyRules.Clone(recovered.active);
        world.AdminDraft(new AdminDraftRequest { baseRevision = recovered.draftRevision, config = valid,
            reason = "Recuperar rascunho válido", requestId = Guid.NewGuid().ToString("D") }, 12001);
        recovered = (AdminSnapshot)world.AdminState(12001);
        var published = (AdminSnapshot)world.AdminPublish(new AdminPublishRequest {
            baseRevision = recovered.draftRevision, requestId = Guid.NewGuid().ToString("D"),
            reason = "Servidor segue apto a publicar" }, 12001);
        Check(published.active.revision > before.active.revision, "escritas válidas continuam após rejeição e reinício");
    }

    private static void CheckInvalidDropConfiguration(string path)
    {
        var world = new GameWorld(new SaveStore(path), Grid(), 13000);
        EconomyConfig original = ((AdminSnapshot)world.AdminState(13000)).draft;
        EconomyConfig config = EconomyRules.Clone(original);
        config.lootTables[0].groups[0].entries[0].weight = int.MaxValue;
        Check(!world.AdminValidate(config).valid, "peso extremo é rejeitado sem overflow");
        config = EconomyRules.Clone(original);
        config.lootTables[0].groups[0].entries[0].definitionId = "uninstalled-equipment";
        Check(!world.AdminValidate(config).valid, "referência a equipamento não instalado é recusada");
        config = EconomyRules.Clone(original);
        config.lootTables[0].groups[0].chancePpm = 1000001;
        Check(!world.AdminValidate(config).valid, "chance acima de cem por cento é recusada");
        config = EconomyRules.Clone(original);
        config.lootTables[0].coinsMin = 10; config.lootTables[0].coinsMax = 9;
        Check(!world.AdminValidate(config).valid, "faixa monetária invertida é recusada");
        config = EconomyRules.Clone(original);
        config.lootTables[0].groups[0].entries = new[] {
            new EconomyLootEntry { definitionId = "steel-head", weight = 1 },
            new EconomyLootEntry { definitionId = "steel-head", weight = 1 } };
        Check(!world.AdminValidate(config).valid, "entrada duplicada em grupo é recusada");
        Check(((AdminSnapshot)world.AdminState(13000)).active.revision == original.revision,
            "validações negativas não alteram a economia ativa");
    }

    private static void CheckForgedOwnership(string path, bool legacy)
    {
        var store = new SaveStore(path);
        var world = new GameWorld(store, Grid(), 14000);
        world.Connect(new OnlineConnectRequest { deviceKey = Key() }, 14000);
        var saved = store.Load();
        SavedPlayer player = saved.players.Single();
        if (legacy)
        {
            player.character.schemaVersion = 1;
            player.character.itemInstances = null;
            player.character.inventoryItemIds = EquipmentCatalog.CreateStartingInventory().Select(i => i.Id).ToArray();
            player.character.inventoryItemIds[1] = player.character.inventoryItemIds[0];
        }
        else
        {
            EquipmentInstanceSave[] instances = player.character.itemInstances;
            instances[1].instanceId = instances[0].instanceId;
        }
        string invalidSave = WireJson.Write(player.character);
        store.Save(saved.players, saved.world, Array.Empty<Receipt>());
        Throws<ArgumentException>(() => new GameWorld(store, Grid(), 14001),
            legacy ? "migração recusa patrimônio legado duplicado" : "carga recusa instância duplicada");
        Check(WireJson.Write(store.Load().players.Single().character) == invalidSave,
            "recusa não apaga personagem nem normaliza perda de patrimônio silenciosamente");
    }
}
