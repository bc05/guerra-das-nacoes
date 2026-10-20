using System;

namespace GuerraDasNacoes
{
    // Shared transport types. Probabilities are integer parts per million; weights are conditional.
    [Serializable] public sealed class EconomyConfig
    {
        public int revision = 1;
        public string label = "Economia inicial";
        public EconomyItemConfig[] items = new EconomyItemConfig[0];
        // Legacy releases keep their flat profiles; converted releases use only species/spawns.
        public EconomyMonsterConfig[] monsters = new EconomyMonsterConfig[0];
        public EconomyMonsterSpecies[] monsterSpecies = new EconomyMonsterSpecies[0];
        public EconomyMonsterSpawn[] monsterSpawns = new EconomyMonsterSpawn[0];
        public EconomyLootTable[] lootTables = new EconomyLootTable[0];
        public EconomyNpc[] npcs = new EconomyNpc[0];
        public EconomyShop[] shops = new EconomyShop[0];
        public EconomyBossSchedule[] bossSchedules = new EconomyBossSchedule[0];
    }

    [Serializable] public sealed class EconomyItemConfig
    {
        public string definitionId, name, rarity, appearanceId;
        public int definitionRevision = 1;
        public int slot, attack, defense, intelligence, damage, health;
        public int minLevel = 1, minAttack, minDefense, minIntelligence, minSwordProficiency;
        public long buyPrice, sellPrice;
        public bool enabled = true;
    }

    [Serializable] public sealed class EconomyMonsterConfig
    {
        public string id, name, mapId, lootTableId;
        public int level = 1, health, damage, experience;
        public float x, z;
        public bool elite, isBoss;
        public string visualKind = "troll", scheduleId;
        public float visualScale = 1, aggroRadius = 4, leashRadius = 8, moveSpeed = 2.25f, attackRange = 1.65f;
        public string behavior = "melee";
        public float attackPreparation = .65f, attackInterval = 1.25f, respawnDelay = 6f, areaRadius = 3f;
    }

    [Serializable] public sealed class EconomyMonsterSpecies
    {
        public string id, name, lootTableId;
        public int level = 1, health, damage, experience;
        public bool elite, isBoss;
        public string visualKind = "troll", behavior = "melee";
        public float visualScale = 1, aggroRadius = 4, leashRadius = 8, moveSpeed = 2.25f, attackRange = 1.65f;
        public float attackPreparation = .65f, attackInterval = 1.25f, respawnDelay = 6f, areaRadius = 3f;
    }

    [Serializable] public sealed class EconomyMonsterSpawn
    {
        // id is the durable spawn identity used by saved encounters, rewards and schedules.
        public string id, speciesId, mapId, scheduleId;
        public float x, z;
    }

    [Serializable] public sealed class EconomyBossSchedule
    {
        public string id, monsterId;
        public string timeZone = "America/Sao_Paulo", startTime = "20:00";
        // 0 = Sunday, 6 = Saturday. Each occurrence is one durable encounter.
        public int[] daysOfWeek = new[] { 0, 1, 2, 3, 4, 5, 6 };
        public int durationMinutes = 60;
        public bool enabled = true;
    }

    [Serializable] public sealed class EconomyLootTable
    {
        public string id;
        public long coinsMin, coinsMax;
        public int scrapChancePpm, scrapMin, scrapMax;
        public EconomyLootGroup[] groups = new EconomyLootGroup[0];
    }

    [Serializable] public sealed class EconomyLootGroup
    {
        public string id;
        public int chancePpm;
        public EconomyLootEntry[] entries = new EconomyLootEntry[0];
    }

    [Serializable] public sealed class EconomyLootEntry
    {
        public string definitionId;
        public int definitionRevision = 1;
        public int weight;
    }

    [Serializable] public sealed class EconomyNpc
    {
        public string id, name, role, mapId, shopId;
        public float x, z;
    }

    [Serializable] public sealed class EconomyShop
    {
        public string id, name;
        public EconomyShopOffer[] offers = new EconomyShopOffer[0];
    }

    [Serializable] public sealed class EconomyShopOffer
    {
        public string id, definitionId;
        public int definitionRevision = 1;
        public long price;
        public int potionQuantity;
        public bool enabled = true;
    }
}
