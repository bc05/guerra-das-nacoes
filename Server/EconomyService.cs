using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

public sealed class PendingLoot
{
    public string id, deathId, definitionId, mapId = "ruins", kind = "equipment";
    public int definitionRevision = 1, quantity = 1;
    public float x, z;
}
public sealed class DamageContribution
{
    public string playerId;
    public long damage;
    public double firstHit, lastHit;
}
public sealed class EconomyLedger
{
    public string id, playerId, source, reference;
    public long amount, balance, atUnixMs;
}
public sealed class EconomyAudit
{
    public string action, reason, requestId;
    public int version;
    public long atUnixMs;
}
public sealed class EconomyRelease
{
    public EconomyConfig config;
    public string reason, requestId, status = "active";
    public long createdAtUnixMs, activateAtUnixMs;
}
public sealed class EconomyWorldState
{
    public EconomyConfig active, draft;
    public int draftRevision = 1;
    public EconomyRelease[] releases = Array.Empty<EconomyRelease>();
    public EconomyAudit[] audit = Array.Empty<EconomyAudit>();
    public EconomyLedger[] ledger = Array.Empty<EconomyLedger>();
    public string[] rewardedDeaths = Array.Empty<string>();
    public int worldContentVersion;
    public BossSpawnReceipt[] bossSpawnWindows = Array.Empty<BossSpawnReceipt>();
}
public sealed class BossSpawnReceipt
{
    public string id, monsterId, outcome = "active";
    public double startsAt, endsAt;
}
public sealed class EconomyValidation
{
    public bool valid;
    public string[] errors = Array.Empty<string>(), warnings = Array.Empty<string>();
}
public static class EconomyRules
{
    public static T Clone<T>(T value) => WireJson.Read<T>(WireJson.Write(value));
    public static EconomyWorldState Create()
    {
        var config = EconomyDefaults.Create();
        return new EconomyWorldState { active = config, draft = Clone(config), releases = new[] { new EconomyRelease { config = Clone(config), reason = "Economia inicial v12", createdAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } } };
    }
    public static void Register(EconomyWorldState state)
    {
        foreach (var release in state.releases ?? Array.Empty<EconomyRelease>())
            foreach (var item in release.config.items)
                if (!EquipmentCatalog.TryRegisterDefinition(item, out string reason)) throw new InvalidDataException("Revisão de item inválida: " + reason);
        foreach (var item in state.active.items)
            if (!EquipmentCatalog.TryRegisterDefinition(item, out string reason)) throw new InvalidDataException("Item ativo inválido: " + reason);
    }
    public static EconomyConfig Normalize(EconomyConfig input,EconomyWorldState history)
    {
        if(input==null)return null;
        var config=Clone(input);
        config.bossSchedules ??= Array.Empty<EconomyBossSchedule>();
        if(config.items==null)return config;
        foreach(var item in config.items.Where(i=>i!=null))
        {
            var previous=(history?.releases??Array.Empty<EconomyRelease>()).SelectMany(r=>r.config.items).Where(i=>i.definitionId==item.definitionId).ToArray();
            var same=previous.FirstOrDefault(i=>ItemStats(i)==ItemStats(item));
            if(same!=null)item.definitionRevision=same.definitionRevision;
            else
            {
                int latest=previous.Length==0?0:previous.Max(i=>i.definitionRevision);
                item.definitionRevision=Math.Max(1,latest+1);
            }
            foreach(var shop in config.shops??Array.Empty<EconomyShop>())
                foreach(var offer in shop?.offers??Array.Empty<EconomyShopOffer>())if(offer!=null&&offer.potionQuantity==0&&offer.definitionId==item.definitionId)offer.definitionRevision=item.definitionRevision;
            foreach(var table in config.lootTables??Array.Empty<EconomyLootTable>())
                foreach(var group in table?.groups??Array.Empty<EconomyLootGroup>())
                    foreach(var entry in group?.entries??Array.Empty<EconomyLootEntry>())if(entry!=null&&entry.definitionId==item.definitionId)entry.definitionRevision=item.definitionRevision;
        }
        return config;
    }
    public static EconomyValidation Validate(EconomyConfig config, EconomyWorldState history, Func<string,float,float,bool> free)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (config == null) return new EconomyValidation { errors = new[] { "Configuração ausente." } };
        if (string.IsNullOrWhiteSpace(config.label) || config.label.Length > 100) errors.Add("Nome da configuração inválido.");
        if (config.items == null || config.monsters == null || config.monsterSpecies == null || config.monsterSpawns == null || config.lootTables == null || config.npcs == null || config.shops == null)
            return new EconomyValidation { errors = new[] { "Todas as coleções da configuração são obrigatórias." } };
        if (config.items.Length > 1000 || config.monsters.Length > 100 || config.monsterSpecies.Length > 100 || config.monsterSpawns.Length > 100 || config.lootTables.Length > 100 || config.npcs.Length > 30 || config.shops.Length > 30)
            errors.Add("Configuração excede os limites do protótipo.");
        void Unique(IEnumerable<string> ids, string label)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids) if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !seen.Add(id)) errors.Add(label + ": identificador ausente ou repetido.");
        }
        if (config.items.Any(x=>x==null) || config.monsters.Any(x=>x==null) || config.monsterSpecies.Any(x=>x==null) || config.monsterSpawns.Any(x=>x==null) || config.lootTables.Any(x=>x==null) || config.npcs.Any(x=>x==null) || config.shops.Any(x=>x==null))
            return new EconomyValidation { errors = new[] { "Configuração contém registros nulos." } };
        Unique(config.items.Select(i=>i.definitionId), "Itens"); Unique(config.monsters.Select(i=>i.id), "Monstros legados");
        Unique(config.monsterSpecies.Select(i=>i.id), "Espécies"); Unique(config.monsterSpawns.Select(i=>i.id), "Nascimentos");
        EconomyMonsterConfig[] monsters;
        try { monsters = MonsterCatalog.Resolve(config); }
        catch (ArgumentException e) { errors.Add(e.Message); return new EconomyValidation { errors = errors.Distinct().ToArray() }; }
        Unique(config.lootTables.Select(i=>i.id), "Drops"); Unique(config.npcs.Select(i=>i.id), "NPCs"); Unique(config.shops.Select(i=>i.id), "Lojas");
        var items = config.items.GroupBy(x=>x.definitionId??"").ToDictionary(g=>g.Key,g=>g.First());
        EconomyItemConfig Item(string id,int revision) => items.TryGetValue(id??"",out var item) && item.definitionRevision==revision ? item : null;
        foreach (var i in config.items)
        {
            if(!EquipmentCatalog.CanRegisterDefinition(i,out string canonicalReason))errors.Add(i.definitionId+": "+canonicalReason);
            if (i.definitionRevision < 1 || string.IsNullOrWhiteSpace(i.name) || i.name.Length > 100 || !Enum.IsDefined(typeof(EquipmentSlot),i.slot) ||
                !new[]{"training","steel","arcane"}.Contains(i.appearanceId) || !new[]{"training","common","uncommon","rare","epic","legendary"}.Contains(i.rarity) ||
                new[]{i.attack,i.defense,i.intelligence,i.damage,i.health,i.minAttack,i.minDefense,i.minIntelligence,i.minSwordProficiency}.Any(v=>v<0||v>100000) ||
                i.minLevel<1 || i.minLevel>50 || i.buyPrice<0 || i.sellPrice<0 || i.buyPrice>1000000000 || i.sellPrice>1000000000)
                errors.Add("Atributos inválidos do item " + i.definitionId);
            var previous = history?.releases?.SelectMany(r=>r.config.items).FirstOrDefault(p=>p.definitionId==i.definitionId&&p.definitionRevision==i.definitionRevision);
            if (previous != null && ItemStats(previous)!=ItemStats(i)) errors.Add("Item " + i.definitionId + ": altere definitionRevision para mudar atributos/requisitos/aparência.");
            if((long)i.minAttack+i.minDefense+i.minIntelligence>250)errors.Add("Requisitos de atributos inalcançáveis: "+i.definitionId);
            if (i.enabled && i.minSwordProficiency>1 && config.shops.Any(s=>(s.offers??Array.Empty<EconomyShopOffer>()).Any(o=>o.enabled&&o.definitionId==i.definitionId)))
                errors.Add("Proficiência ainda indisponível: " + i.definitionId);
        }
        foreach (var table in config.lootTables)
        {
            if (table.coinsMin<0 || table.coinsMax<table.coinsMin || table.coinsMax>1000000 || table.scrapChancePpm<0 || table.scrapChancePpm>1000000 ||
                table.scrapMin<0 || table.scrapMax<table.scrapMin || table.scrapMax>1000 || table.groups==null || table.groups.Length>16)
                {errors.Add("Faixas de drop inválidas: "+table.id);continue;}
            if(table.groups.Any(g=>g==null)){errors.Add("Grupo nulo: "+table.id);continue;}
            Unique(table.groups.Select(g=>g.id),"Grupos "+table.id);
            foreach(var group in table.groups)
            {
                if(group.chancePpm<0||group.chancePpm>1000000||group.entries==null||group.entries.Length==0||group.entries.Length>100)
                    {errors.Add("Grupo de drop inválido: "+group.id);continue;}
                if(group.entries.Any(e=>e==null)){errors.Add("Entrada nula: "+group.id);continue;}
                Unique(group.entries.Select(e=>e.definitionId),"Entradas "+group.id);
                long sum=0;
                foreach(var entry in group.entries)
                {
                    var item=Item(entry.definitionId,entry.definitionRevision);
                    if(entry.weight<=0||entry.weight>1000000||item==null||!item.enabled||item.minSwordProficiency>1) errors.Add("Item/peso/requisito inválido em "+group.id+": "+entry.definitionId);
                    sum+=entry.weight;
                }
                if(sum<=0||sum>int.MaxValue)errors.Add("Soma de pesos inválida: "+group.id);
            }
        }
        // Validate unused species too: a bad profile cannot be hidden until a future spawn references it.
        var profiles = config.monsterSpecies.Length == 0 ? monsters : config.monsterSpecies.Select(p =>
            MonsterCatalog.Compose(p, new EconomyMonsterSpawn { id = p.id })).ToArray();
        foreach(var monster in profiles)
            if(string.IsNullOrWhiteSpace(monster.name)||monster.name.Length>100||monster.level<1||monster.level>50||monster.health<1||monster.health>1000000||monster.damage<1||monster.damage>100000||monster.experience<0||monster.experience>100000||!config.lootTables.Any(t=>t.id==monster.lootTableId)||
                !float.IsFinite(monster.visualScale)||monster.visualScale<.5f||monster.visualScale>3||!new[]{"orc","troll","boss-troll","skeleton","skeleton-caster"}.Contains(monster.visualKind)||
                !float.IsFinite(monster.aggroRadius)||monster.aggroRadius<0||monster.aggroRadius>30||!float.IsFinite(monster.leashRadius)||monster.leashRadius<monster.aggroRadius||monster.leashRadius>45||
                !float.IsFinite(monster.moveSpeed)||monster.moveSpeed<.5f||monster.moveSpeed>6||!float.IsFinite(monster.attackRange)||monster.attackRange<1||monster.attackRange>12||
                !new[]{"melee","ranged","area"}.Contains(monster.behavior)||
                !float.IsFinite(monster.attackPreparation)||monster.attackPreparation<.2f||monster.attackPreparation>5||
                !float.IsFinite(monster.attackInterval)||monster.attackInterval<.3f||monster.attackInterval>10||monster.attackInterval+0.00001f<monster.attackPreparation+.1f||
                !float.IsFinite(monster.respawnDelay)||monster.respawnDelay<1||monster.respawnDelay>600||
                !float.IsFinite(monster.areaRadius)||monster.areaRadius<.5f||monster.areaRadius>8) errors.Add("Perfil de monstro inválido: "+monster.id);
        foreach(var monster in monsters)
            if((monster.mapId!="ruins"&&monster.mapId!=CatacombsLayout.MapId)||!float.IsFinite(monster.x)||!float.IsFinite(monster.z)||!free(monster.mapId,monster.x,monster.z))
                errors.Add("Ponto de nascimento inválido: "+monster.id);
        BossScheduleRules.Validate(config, errors);
        var previousMonsters = history?.active == null ? Array.Empty<EconomyMonsterConfig>() : MonsterCatalog.Resolve(history.active);
        if(previousMonsters.Any(m=>monsters.Any(n=>n.id==m.id&&n.isBoss!=m.isBoss))) errors.Add("Não converta um nascimento normal em chefão ou vice-versa; cada encontro precisa de identidade própria.");
        if(previousMonsters.Any(m=>monsters.Any(n=>n.id==m.id&&n.mapId!=m.mapId))) errors.Add("Não mova uma identidade de nascimento existente para outro mapa.");
        // Preserve spawn identities: deleting a live monster cannot erase an encounter or its pinned revision.
        if(previousMonsters.Any(m=>!monsters.Any(n=>n.id==m.id))) errors.Add("Não remova identidades de nascimento existentes; ajuste o perfil para o próximo nascimento.");
        foreach(var npc in config.npcs)
            if(npc.mapId!="city"||!free(npc.mapId,npc.x,npc.z)||!config.shops.Any(s=>s.id==npc.shopId))errors.Add("NPC/loja inválido: "+npc.id);
        foreach(var shop in config.shops)
        {
            if(shop.offers==null||shop.offers.Length>100){errors.Add("Ofertas inválidas: "+shop.id);continue;}
            if(shop.offers.Any(o=>o==null)){errors.Add("Oferta nula: "+shop.id);continue;}
            Unique(shop.offers.Select(o=>o.id),"Ofertas "+shop.id);
            foreach(var offer in shop.offers)
            {
                var item=Item(offer.definitionId,offer.definitionRevision);
                if(offer.price<=0||offer.price>1000000000||offer.potionQuantity<0||offer.potionQuantity>100 ||
                   (offer.potionQuantity==0&&(item==null||!item.enabled||item.minSwordProficiency>1))) errors.Add("Oferta inválida: "+offer.id);
                if(item!=null && offer.potionQuantity==0 && item.sellPrice>offer.price) errors.Add("Arbitragem de compra/venda: "+offer.id);
            }
        }
        if(monsters.Length==0)errors.Add("Configure ao menos um monstro.");
        warnings.Add("Valores de balanceamento iniciais: valide tempo de combate, experiência e consumo em partidas reais.");
        return new EconomyValidation {valid=errors.Count==0,errors=errors.Distinct().ToArray(),warnings=warnings.ToArray()};
    }
    internal static string ItemStats(EconomyItemConfig item)
    {
        var copy=Clone(item);copy.definitionRevision=1;copy.buyPrice=copy.sellPrice=0;copy.enabled=true;return WireJson.Write(copy);
    }
    public static object Simulate(EconomyConfig config,int kills,double killRate)
    {
        kills=Math.Clamp(kills,1,1000000);killRate=Math.Clamp(killRate,1,10000);
        return new {kills,killRate,monsters=MonsterCatalog.Resolve(config).Select(m=>
        {
            var table=config.lootTables.First(t=>t.id==m.lootTableId);
            var itemResults=table.groups.SelectMany(g=>g.entries.Select(e=>new {definitionId=e.definitionId,chance=g.chancePpm/1000000d*e.weight/g.entries.Sum(a=>a.weight)})).GroupBy(e=>e.definitionId).Select(g=>new{definitionId=g.Key,chance=g.Sum(e=>e.chance),expected=g.Sum(e=>e.chance)*kills}).ToArray();
            double coins=(table.coinsMin+table.coinsMax)/2d;
            return new {id=m.id,name=m.name,coinsPerKill=coins,coinsPerHour=coins*killRate,expectedEquipment=itemResults.Sum(i=>i.expected),items=itemResults};
        }).ToArray()};
    }
    public static long RandomCoins(long min,long max) => min + RandomNumberGenerator.GetInt32(checked((int)(max-min+1)));
}
