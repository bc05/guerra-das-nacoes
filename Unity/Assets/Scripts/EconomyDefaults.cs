using System;
using System.Collections.Generic;

namespace GuerraDasNacoes
{
    public static class EconomyDefaults
    {
        public const int ChanceScale = 1000000;
        public const long ScrapSellPrice = 4;

        public static EconomyConfig Create()
        {
            List<EconomyItemConfig> items = new List<EconomyItemConfig>();
            foreach (EquipmentItem item in EquipmentCatalog.GetDefinitions())
            {
                if (item.DefinitionRevision != 1 || item.DefinitionId.StartsWith("sovereign-", StringComparison.Ordinal)) continue;
                EconomyItemConfig entry = EquipmentCatalog.ToConfig(item);
                entry.buyPrice = BuyPrice(item.DefinitionId);
                entry.sellPrice = item.DefinitionId.StartsWith("steel-", StringComparison.Ordinal) ? entry.buyPrice / 10 :
                    item.DefinitionId.StartsWith("arcane-", StringComparison.Ordinal) ? 90 : 0;
                entry.enabled = item.DefinitionId != "veteran-sword";
                // Family/appearance is independent from market rarity (the wisdom ring is a basic accessory).
                entry.rarity = item.DefinitionId == "arcane-ring" ? "common" : entry.rarity;
                if (item.DefinitionId == "arcane-ring") { entry.buyPrice = 300; entry.sellPrice = 30; }
                items.Add(entry);
            }
            return new EconomyConfig
            {
                revision = 1, label = "Cidade da Fronteira · economia inicial", items = items.ToArray(),
                monsters = new[]
                {
                    Monster("troll-0", "Troll Saqueador", 2, 240, 10, 120, -2.6f, 3.8f, "loot-saqueador"),
                    Monster("troll-1", "Troll Ancião", 4, 600, 16, 240, 0, 5.8f, "loot-anciao", true),
                    Monster("troll-2", "Troll Saqueador", 2, 240, 10, 120, 2.6f, 3.8f, "loot-saqueador"),
                    Monster("sentinel-0", "Sentinela Rúnica", 6, 900, 20, 300, 6.2f, 0, "loot-sentinela", true)
                },
                lootTables = new[]
                {
                    new EconomyLootTable { id = "loot-saqueador", coinsMin = 5, coinsMax = 9,
                        scrapChancePpm = 350000, scrapMin = 1, scrapMax = 2,
                        groups = new[] { Group("aco", 100000,
                            Entry("steel-sword",20), Entry("steel-hands",30), Entry("steel-feet",30), Entry("steel-belt",20)) } },
                    new EconomyLootTable { id = "loot-anciao", coinsMin = 14, coinsMax = 22,
                        scrapChancePpm = 550000, scrapMin = 1, scrapMax = 3,
                        groups = new[] { Group("aco", 140000,
                            Entry("steel-shield",30), Entry("steel-chest",25), Entry("steel-legs",25), Entry("steel-head",20)),
                            Group("runico", 30000, Entry("arcane-sword",50), Entry("arcane-shield",50)) } },
                    new EconomyLootTable { id = "loot-sentinela", coinsMin = 22, coinsMax = 34,
                        scrapChancePpm = 600000, scrapMin = 2, scrapMax = 3,
                        groups = new[] { Group("aco", 100000,
                            Entry("steel-necklace",40), Entry("steel-head",30), Entry("steel-shoulders",30)),
                            Group("runico", 60000, Entry("arcane-head",20), Entry("arcane-shoulders",20),
                                Entry("arcane-chest",20), Entry("arcane-belt",20), Entry("arcane-necklace",20)) } }
                },
                npcs = new[]
                {
                    Npc("npc-ferreiro", "Darian · Ferreiro", "blacksmith", -5, 3, "shop-ferreiro"),
                    Npc("npc-armeira", "Helena · Armeira", "armorer", -2, 6, "shop-armeira"),
                    Npc("npc-joalheira", "Lyra · Joalheira", "jeweler", 3, 6, "shop-joalheira"),
                    Npc("npc-alquimista", "Orin · Alquimista", "alchemist", 6, 2, "shop-alquimista"),
                    Npc("npc-mercador", "Borin · Mercador", "merchant", 3, -4, "shop-mercador")
                },
                shops = new[]
                {
                    Shop("shop-ferreiro", "Armas e escudos", "steel-sword", "steel-shield"),
                    Shop("shop-armeira", "Armaduras", "steel-head", "steel-shoulders", "steel-chest", "steel-hands", "steel-legs", "steel-feet"),
                    Shop("shop-joalheira", "Joias e acessórios", "steel-ring", "arcane-ring", "steel-belt", "steel-necklace"),
                    new EconomyShop { id = "shop-alquimista", name = "Poções", offers = new[] {
                        new EconomyShopOffer { id = "potion-health", definitionId = "health-potion", price = 20, potionQuantity = 1 } } },
                    new EconomyShop { id = "shop-mercador", name = "Compra de equipamentos e sucata" }
                }
            };
        }

        // Additive content: existing IDs, administrator values and item revisions are never replaced.
        public static void AddFrontierContent(EconomyConfig config)
        {
            List<EconomyItemConfig> items = new List<EconomyItemConfig>(config.items ?? new EconomyItemConfig[0]);
            string[] sovereign = { "sovereign-sword", "sovereign-shield", "sovereign-necklace" };
            foreach (string id in sovereign)
            {
                if (items.Exists(i => i.definitionId == id)) continue;
                EquipmentItem definition = EquipmentCatalog.GetDefinition(id, 1);
                if (definition == null) throw new InvalidOperationException("Definição do chefão não instalada: " + id);
                EconomyItemConfig item = EquipmentCatalog.ToConfig(definition);
                item.rarity = "legendary"; item.enabled = true;
                item.sellPrice = id == "sovereign-sword" ? 450 : id == "sovereign-shield" ? 400 : 350;
                items.Add(item);
            }
            config.items = items.ToArray();
            bool separated = config.monsterSpecies.Length > 0 || config.monsterSpawns.Length > 0;
            List<EconomyMonsterConfig> monsters = new List<EconomyMonsterConfig>(MonsterCatalog.Resolve(config));
            EconomyMonsterConfig[] additions = {
                FrontierMonster("orc-raider-0", "Orc Saqueador", 7, 950, 23, 340, -24, 25, "loot-orc", "orc", .93f),
                FrontierMonster("orc-warrior-0", "Orc Guerreiro", 10, 1600, 30, 510, -28, 34, "loot-orc", "orc", 1.08f),
                FrontierMonster("orc-shaman-0", "Orc Xamã", 11, 1350, 35, 550, -18, 33, "loot-orc", "orc", .98f),
                FrontierMonster("troll-brute-0", "Troll do Bosque", 12, 2400, 38, 680, 25, 30, "loot-troll-forest", "troll", 1.18f),
                FrontierMonster("troll-elder-0", "Troll Guardião", 15, 3600, 46, 880, 29, 40, "loot-troll-forest", "troll", 1.32f),
                FrontierMonster("boss-grond", "Grond · Soberano dos Clãs", 25, 12000, 70, 3600, 0, 61, "loot-grond", "boss-troll", 1.7f)
            };
            additions[5].isBoss = additions[5].elite = true;
            additions[5].scheduleId = "grond-daily"; additions[5].aggroRadius = 12;
            additions[5].leashRadius = 26; additions[5].attackRange = 2.5f; additions[5].moveSpeed = 2.5f;
            foreach (EconomyMonsterConfig monster in additions) if (!monsters.Exists(m => m.id == monster.id)) monsters.Add(monster);
            config.monsters = monsters.ToArray();
            if (separated)
            {
                config.monsterSpecies = new EconomyMonsterSpecies[0];
                config.monsterSpawns = new EconomyMonsterSpawn[0];
                MonsterCatalog.Upgrade(config);
            }
            List<EconomyLootTable> tables = new List<EconomyLootTable>(config.lootTables);
            EconomyLootTable[] drops = {
                new EconomyLootTable { id="loot-orc", coinsMin=25, coinsMax=42, scrapChancePpm=550000, scrapMin=2, scrapMax=4,
                    groups=new[]{Group("aco",160000,Entry("steel-sword",30),Entry("steel-chest",35),Entry("steel-shield",35)),Group("runico",45000,Entry("arcane-hands",50),Entry("arcane-feet",50))} },
                new EconomyLootTable { id="loot-troll-forest", coinsMin=48, coinsMax=75, scrapChancePpm=650000, scrapMin=3, scrapMax=5,
                    groups=new[]{Group("runico",90000,Entry("arcane-chest",35),Entry("arcane-legs",35),Entry("arcane-sword",30))} },
                new EconomyLootTable { id="loot-grond", coinsMin=450, coinsMax=700, scrapChancePpm=1000000, scrapMin=8, scrapMax=12,
                    groups=new[]{Group("reliquia-soberano",350000,Entry("sovereign-sword",35),Entry("sovereign-shield",35),Entry("sovereign-necklace",30)),Group("runico",750000,Entry("arcane-chest",25),Entry("arcane-legs",25),Entry("arcane-sword",25),Entry("arcane-shield",25))} }
            };
            foreach (EconomyLootTable table in drops)
            {
                if (tables.Exists(t => t.id == table.id)) continue;
                List<EconomyLootGroup> groups = new List<EconomyLootGroup>();
                foreach (EconomyLootGroup group in table.groups)
                {
                    List<EconomyLootEntry> entries = new List<EconomyLootEntry>();
                    foreach (EconomyLootEntry entry in group.entries)
                    {
                        EconomyItemConfig item = items.Find(i => i.definitionId == entry.definitionId && i.enabled);
                        if (item == null) continue;
                        entry.definitionRevision = item.definitionRevision; entries.Add(entry);
                    }
                    if (entries.Count == 0) continue;
                    group.entries = entries.ToArray(); groups.Add(group);
                }
                table.groups = groups.ToArray(); tables.Add(table);
            }
            config.lootTables = tables.ToArray();
            List<EconomyBossSchedule> schedules = new List<EconomyBossSchedule>(config.bossSchedules ?? new EconomyBossSchedule[0]);
            if (!schedules.Exists(a => a.id == "grond-daily")) schedules.Add(new EconomyBossSchedule { id="grond-daily", monsterId="boss-grond" });
            config.bossSchedules = schedules.ToArray();
        }
        private static EconomyMonsterConfig FrontierMonster(string id,string name,int level,int health,int damage,int xp,float x,float z,string loot,string kind,float scale) =>
            new EconomyMonsterConfig { id=id,name=name,mapId="ruins",level=level,health=health,damage=damage,experience=xp,x=x,z=z,lootTableId=loot,visualKind=kind,visualScale=scale,aggroRadius=6,leashRadius=16 };

        private static long BuyPrice(string id)
        {
            switch (id)
            {
                case "steel-sword": return 360;
                case "steel-shield": case "steel-legs": case "steel-ring": case "arcane-ring": return 300;
                case "steel-head": case "steel-hands": case "steel-feet": return 180;
                case "steel-shoulders": case "steel-belt": return 240;
                case "steel-chest": return 420;
                case "steel-necklace": return 360;
                default: return 0;
            }
        }

        private static EconomyMonsterConfig Monster(string id, string name, int level, int health, int damage,
            int experience, float x, float z, string loot, bool elite = false) => new EconomyMonsterConfig
            { id = id, name = name, mapId = "ruins", level = level, health = health, damage = damage,
                experience = experience, x = x, z = z, lootTableId = loot, elite = elite };
        private static EconomyNpc Npc(string id, string name, string role, float x, float z, string shopId) =>
            new EconomyNpc { id = id, name = name, role = role, x = x, z = z, mapId = "city", shopId = shopId };
        private static EconomyLootEntry Entry(string id, int weight) => new EconomyLootEntry { definitionId = id, weight = weight };
        private static EconomyLootGroup Group(string id, int chancePpm, params EconomyLootEntry[] entries) =>
            new EconomyLootGroup { id = id, chancePpm = chancePpm, entries = entries };
        private static EconomyShop Shop(string id, string name, params string[] definitions)
        {
            EconomyShopOffer[] offers = new EconomyShopOffer[definitions.Length];
            for (int i = 0; i < offers.Length; i++) offers[i] = new EconomyShopOffer
                { id = "offer-" + definitions[i], definitionId = definitions[i], price = BuyPrice(definitions[i]) };
            return new EconomyShop { id = id, name = name, offers = offers };
        }
    }
}
