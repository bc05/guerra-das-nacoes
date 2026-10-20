using System;
using System.Collections.Generic;

namespace GuerraDasNacoes
{
    // No Unity dependency: the authoritative server and offline prototype resolve the same catalog.
    public static class MonsterCatalog
    {
        public static EconomyMonsterConfig[] Resolve(EconomyConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.monsters == null || config.monsterSpecies == null || config.monsterSpawns == null)
                throw new ArgumentException("Coleções de monstros ausentes.");
            bool separated = config.monsterSpecies.Length > 0 || config.monsterSpawns.Length > 0;
            if (separated && config.monsters.Length > 0)
                throw new ArgumentException("Não misture perfis legados com espécies e pontos de nascimento.");
            if (!separated)
            {
                var legacy = new EconomyMonsterConfig[config.monsters.Length];
                for (int i = 0; i < legacy.Length; i++)
                {
                    var monster = config.monsters[i];
                    if (monster == null) throw new ArgumentException("Perfil de monstro nulo.");
                    legacy[i] = Compose(Profile(monster), new EconomyMonsterSpawn {
                        id = monster.id, mapId = monster.mapId, x = monster.x, z = monster.z, scheduleId = monster.scheduleId });
                }
                return legacy;
            }
            var species = new Dictionary<string, EconomyMonsterSpecies>(StringComparer.Ordinal);
            foreach (var entry in config.monsterSpecies)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || species.ContainsKey(entry.id))
                    throw new ArgumentException("Espécie ausente ou repetida.");
                species.Add(entry.id, entry);
            }
            var resolved = new EconomyMonsterConfig[config.monsterSpawns.Length];
            var spawnIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < resolved.Length; i++)
            {
                var spawn = config.monsterSpawns[i];
                if (spawn == null || string.IsNullOrWhiteSpace(spawn.id) || !spawnIds.Add(spawn.id))
                    throw new ArgumentException("Identidade de nascimento ausente ou repetida.");
                if (string.IsNullOrEmpty(spawn.speciesId) || !species.TryGetValue(spawn.speciesId, out var profile))
                    throw new ArgumentException("Espécie desconhecida no nascimento " + spawn.id + ".");
                resolved[i] = Compose(profile, spawn);
            }
            return resolved;
        }

        // Call only on a new release or a draft clone; stored historical configs stay untouched.
        public static void Upgrade(EconomyConfig config, bool newBehaviors = false)
        {
            var monsters = Resolve(config);
            if (config.monsters.Length == 0 && !newBehaviors) return;
            var species = new List<EconomyMonsterSpecies>();
            var spawns = new List<EconomyMonsterSpawn>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var monster in monsters)
            {
                if (newBehaviors)
                {
                    if (monster.id == "orc-shaman-0")
                    {
                        monster.behavior = "ranged"; monster.attackRange = 7; monster.areaRadius = 1.1f;
                        monster.attackPreparation = 1.1f; monster.attackInterval = 2;
                    }
                    else if (monster.id == "troll-brute-0" || monster.id == "troll-elder-0")
                    {
                        monster.behavior = "area"; monster.areaRadius = 3;
                        monster.attackPreparation = 1.4f; monster.attackInterval = 2.4f;
                    }
                }
                var candidate = Profile(monster);
                var profile = species.Find(existing => SameProfile(existing, candidate));
                if (profile == null)
                {
                    string baseId = SpeciesId(monster.id);
                    string id = baseId;
                    for (int suffix = 2; !ids.Add(id); suffix++)
                    {
                        string ending = "-" + suffix;
                        id = baseId.Substring(0, Math.Min(baseId.Length, 100 - ending.Length)) + ending;
                    }
                    candidate.id = id; profile = candidate; species.Add(profile);
                }
                spawns.Add(new EconomyMonsterSpawn {
                    id = monster.id, speciesId = profile.id, mapId = monster.mapId,
                    x = monster.x, z = monster.z, scheduleId = monster.scheduleId });
            }
            config.monsterSpecies = species.ToArray();
            config.monsterSpawns = spawns.ToArray();
            config.monsters = new EconomyMonsterConfig[0];
        }

        private static string SpeciesId(string spawnId)
        {
            switch (spawnId)
            {
                case "troll-0": case "troll-2": return "troll-raider";
                case "troll-1": return "troll-ancient";
                case "sentinel-0": return "rune-sentinel";
                case "orc-raider-0": return "orc-raider";
                case "orc-warrior-0": return "orc-warrior";
                case "orc-shaman-0": return "orc-shaman";
                case "troll-brute-0": return "forest-troll-brute";
                case "troll-elder-0": return "forest-troll-guardian";
                case "boss-grond": return "grond";
                default: return (spawnId ?? "monster").Substring(0, Math.Min((spawnId ?? "monster").Length, 100));
            }
        }

        private static EconomyMonsterSpecies Profile(EconomyMonsterConfig m) => new EconomyMonsterSpecies {
            name = m.name, lootTableId = m.lootTableId, level = m.level, health = m.health,
            damage = m.damage, experience = m.experience, elite = m.elite, isBoss = m.isBoss,
            visualKind = m.visualKind, visualScale = m.visualScale, aggroRadius = m.aggroRadius,
            leashRadius = m.leashRadius, moveSpeed = m.moveSpeed, attackRange = m.attackRange,
            behavior = m.behavior, attackPreparation = m.attackPreparation, attackInterval = m.attackInterval,
            respawnDelay = m.respawnDelay, areaRadius = m.areaRadius };

        internal static EconomyMonsterConfig Compose(EconomyMonsterSpecies p, EconomyMonsterSpawn s) => new EconomyMonsterConfig {
            id = s.id, mapId = s.mapId, x = s.x, z = s.z, scheduleId = s.scheduleId,
            name = p.name, lootTableId = p.lootTableId, level = p.level, health = p.health,
            damage = p.damage, experience = p.experience, elite = p.elite, isBoss = p.isBoss,
            visualKind = p.visualKind, visualScale = p.visualScale, aggroRadius = p.aggroRadius,
            leashRadius = p.leashRadius, moveSpeed = p.moveSpeed, attackRange = p.attackRange,
            behavior = p.behavior, attackPreparation = p.attackPreparation, attackInterval = p.attackInterval,
            respawnDelay = p.respawnDelay, areaRadius = p.areaRadius };

        private static bool SameProfile(EconomyMonsterSpecies a, EconomyMonsterSpecies b) =>
            a.name == b.name && a.lootTableId == b.lootTableId && a.level == b.level && a.health == b.health &&
            a.damage == b.damage && a.experience == b.experience && a.elite == b.elite && a.isBoss == b.isBoss &&
            a.visualKind == b.visualKind && a.visualScale == b.visualScale && a.aggroRadius == b.aggroRadius &&
            a.leashRadius == b.leashRadius && a.moveSpeed == b.moveSpeed && a.attackRange == b.attackRange &&
            a.behavior == b.behavior && a.attackPreparation == b.attackPreparation && a.attackInterval == b.attackInterval &&
            a.respawnDelay == b.respawnDelay && a.areaRadius == b.areaRadius;
    }
}
