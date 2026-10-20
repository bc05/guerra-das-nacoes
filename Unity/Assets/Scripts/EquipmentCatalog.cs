using System;
using System.Collections.Generic;

namespace GuerraDasNacoes
{
    public static class EquipmentCatalog
    {
        private static readonly string[] SlotNames = {
            "Espada", "Escudo", "Elmo", "Ombreiras", "Peitoral", "Luvas", "Calças", "Botas",
            "Anel esquerdo", "Anel direito", "Cinturão", "Colar"
        };

        public static string GetSlotName(EquipmentSlot slot) => Enum.IsDefined(typeof(EquipmentSlot), slot)
            ? SlotNames[(int)slot] : "Espaço inválido";

        // The training set carries zero extra stats to preserve the original level-one balance.
        // Inventory variants are real owned instances: one unique ID can occupy only one space.
        internal static List<EquipmentItem> CreateTrainingSet()
        {
            List<EquipmentItem> items = new List<EquipmentItem> { EquipmentItem.FromWeapon(WeaponDefinition.TrainingSword) };
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (slot == EquipmentSlot.Sword) continue;
                items.Add(new EquipmentItem("training-" + slot.ToString().ToLowerInvariant(),
                    GetSlotName(slot) + " de Treinamento", slot));
            }
            return items;
        }

        internal static List<EquipmentItem> CreateStartingInventory()
        {
            return new List<EquipmentItem>
            {
                new EquipmentItem("steel-sword", "Espada de Aço", EquipmentSlot.Sword,
                    new EquipmentBonuses(attack: 2, damage: 4), new WeaponRequirements(minimumAttack: 2), "steel"),
                new EquipmentItem("steel-shield", "Escudo de Aço", EquipmentSlot.Shield,
                    new EquipmentBonuses(defense: 3, health: 12), appearanceId: "steel"),
                new EquipmentItem("steel-head", "Elmo de Aço", EquipmentSlot.Head,
                    new EquipmentBonuses(defense: 1, health: 8), appearanceId: "steel"),
                new EquipmentItem("steel-shoulders", "Ombreiras de Aço", EquipmentSlot.Shoulders,
                    new EquipmentBonuses(attack: 1, defense: 1), appearanceId: "steel"),
                new EquipmentItem("steel-chest", "Peitoral de Aço", EquipmentSlot.Chest,
                    new EquipmentBonuses(defense: 3, health: 24), appearanceId: "steel"),
                new EquipmentItem("steel-hands", "Manoplas de Aço", EquipmentSlot.Hands,
                    new EquipmentBonuses(attack: 2), appearanceId: "steel"),
                new EquipmentItem("steel-legs", "Grevas de Aço", EquipmentSlot.Legs,
                    new EquipmentBonuses(defense: 2, health: 12), appearanceId: "steel"),
                new EquipmentItem("steel-feet", "Botas de Aço", EquipmentSlot.Feet,
                    new EquipmentBonuses(attack: 1, defense: 1), appearanceId: "steel"),
                new EquipmentItem("steel-ring", "Anel da Força", EquipmentSlot.RingLeft,
                    new EquipmentBonuses(attack: 2), appearanceId: "steel"),
                new EquipmentItem("arcane-ring", "Anel da Sabedoria", EquipmentSlot.RingRight,
                    new EquipmentBonuses(intelligence: 2), appearanceId: "arcane"),
                new EquipmentItem("steel-belt", "Cinturão da Vitalidade", EquipmentSlot.Belt,
                    new EquipmentBonuses(health: 20), appearanceId: "steel"),
                new EquipmentItem("steel-necklace", "Colar do Aprendiz", EquipmentSlot.Necklace,
                    new EquipmentBonuses(intelligence: 2, health: 5), appearanceId: "steel"),
                new EquipmentItem("arcane-sword", "Lâmina Rúnica", EquipmentSlot.Sword,
                    new EquipmentBonuses(attack: 4, intelligence: 3, damage: 9), new WeaponRequirements(3, 6, 0, 3), "arcane"),
                new EquipmentItem("veteran-sword", "Espada do Veterano", EquipmentSlot.Sword,
                    new EquipmentBonuses(attack: 8, damage: 14), new WeaponRequirements(3, 6, 0, 0, 2), "arcane"),
                new EquipmentItem("arcane-shield", "Égide Rúnica", EquipmentSlot.Shield,
                    new EquipmentBonuses(defense: 6, intelligence: 2, health: 25), new WeaponRequirements(3, 0, 3, 0, 0), "arcane"),
                new EquipmentItem("arcane-head", "Elmo Rúnico", EquipmentSlot.Head,
                    new EquipmentBonuses(defense: 3, intelligence: 3, health: 15), MagicRequirements(), "arcane"),
                new EquipmentItem("arcane-shoulders", "Ombreiras Rúnicas", EquipmentSlot.Shoulders,
                    new EquipmentBonuses(attack: 3, defense: 3), MagicRequirements(), "arcane"),
                new EquipmentItem("arcane-chest", "Peitoral Rúnico", EquipmentSlot.Chest,
                    new EquipmentBonuses(defense: 6, intelligence: 2, health: 40), MagicRequirements(), "arcane"),
                new EquipmentItem("arcane-hands", "Manoplas Rúnicas", EquipmentSlot.Hands,
                    new EquipmentBonuses(attack: 3, intelligence: 3), MagicRequirements(), "arcane"),
                new EquipmentItem("arcane-legs", "Grevas Rúnicas", EquipmentSlot.Legs,
                    new EquipmentBonuses(defense: 4, intelligence: 2, health: 25), MagicRequirements(), "arcane"),
                new EquipmentItem("arcane-feet", "Botas Rúnicas", EquipmentSlot.Feet,
                    new EquipmentBonuses(attack: 2, defense: 3, intelligence: 1), MagicRequirements(), "arcane"),
                new EquipmentItem("arcane-belt", "Cinturão Rúnico", EquipmentSlot.Belt,
                    new EquipmentBonuses(defense: 2, intelligence: 2, health: 35), MagicRequirements(), "arcane"),
                new EquipmentItem("arcane-necklace", "Colar Rúnico", EquipmentSlot.Necklace,
                    new EquipmentBonuses(intelligence: 5, damage: 2), MagicRequirements(), "arcane")
            };
        }

        private static readonly object DefinitionGate = new object();
        private static Dictionary<string, EquipmentItem> definitions;
        private static Dictionary<string, EquipmentItem> canonical;

        // The client may choose another independent server while at character selection.
        // Published revisions are immutable within one server, not globally across servers.
        public static void ResetPublishedDefinitions()
        {
            lock (DefinitionGate)
            {
                definitions = null;
                canonical = null;
                EnsureDefinitions();
            }
        }

        private static void EnsureDefinitions()
        {
            if (definitions != null) return;
            canonical = new Dictionary<string, EquipmentItem>(StringComparer.Ordinal);
            definitions = new Dictionary<string, EquipmentItem>(StringComparer.Ordinal);
            foreach (EquipmentItem item in CreateTrainingSet())
            { canonical.Add(item.DefinitionId, item); definitions.Add(Key(item.DefinitionId, 1), item); }
            foreach (EquipmentItem item in CreateStartingInventory())
            { canonical.Add(item.DefinitionId, item); definitions.Add(Key(item.DefinitionId, 1), item); }
            // Drop-only definitions never enter the initial inventory or legacy ownership migration.
            foreach (EquipmentItem item in CreateSovereignDefinitions())
            { canonical.Add(item.DefinitionId, item); definitions.Add(Key(item.DefinitionId, 1), item); }
        }

        private static EquipmentItem[] CreateSovereignDefinitions() => new[]
        {
            new EquipmentItem("sovereign-sword", "Lâmina do Soberano", EquipmentSlot.Sword,
                new EquipmentBonuses(attack: 10, intelligence: 4, damage: 26),
                new WeaponRequirements(15, 20, 0, 0, 1), "arcane"),
            new EquipmentItem("sovereign-shield", "Égide do Soberano", EquipmentSlot.Shield,
                new EquipmentBonuses(defense: 14, health: 120), new WeaponRequirements(15, 0, 15), "arcane"),
            new EquipmentItem("sovereign-necklace", "Colar do Soberano", EquipmentSlot.Necklace,
                new EquipmentBonuses(intelligence: 12, health: 80), new WeaponRequirements(15, 0, 0, 15), "arcane")
        };

        private static string Key(string id, int revision) => id + "@" + revision;

        public static EquipmentItem GetDefinition(string definitionId, int definitionRevision = 1)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || definitionRevision < 1) return null;
            lock (DefinitionGate)
            {
                EnsureDefinitions();
                return definitions.TryGetValue(Key(definitionId, definitionRevision), out EquipmentItem item) ? item : null;
            }
        }

        // Frozen ownership domain of schema one. Installing a new drop or publishing another
        // revision must never change which 35 definition IDs an old character is allowed to own.
        private static readonly string[] LegacyDefinitionIds = {
            "training-sword", "training-shield", "training-head", "training-shoulders", "training-chest",
            "training-hands", "training-legs", "training-feet", "training-ringleft", "training-ringright",
            "training-belt", "training-necklace",
            "steel-sword", "steel-shield", "steel-head", "steel-shoulders", "steel-chest", "steel-hands",
            "steel-legs", "steel-feet", "steel-ring", "arcane-ring", "steel-belt", "steel-necklace",
            "arcane-sword", "veteran-sword", "arcane-shield", "arcane-head", "arcane-shoulders",
            "arcane-chest", "arcane-hands", "arcane-legs", "arcane-feet", "arcane-belt", "arcane-necklace"
        };

        public static EquipmentItem[] GetLegacyDefinitions()
        {
            lock (DefinitionGate)
            {
                EnsureDefinitions();
                EquipmentItem[] result = new EquipmentItem[LegacyDefinitionIds.Length];
                for (int i = 0; i < LegacyDefinitionIds.Length; i++)
                {
                    if (!definitions.TryGetValue(Key(LegacyDefinitionIds[i], 1), out result[i]))
                        throw new InvalidOperationException("A definição original do patrimônio legado está ausente.");
                }
                return result;
            }
        }

        public static EquipmentItem[] GetDefinitions()
        {
            lock (DefinitionGate)
            {
                EnsureDefinitions();
                EquipmentItem[] result = new EquipmentItem[definitions.Count];
                definitions.Values.CopyTo(result, 0);
                Array.Sort(result, (a, b) => { int order = string.CompareOrdinal(a.DefinitionId, b.DefinitionId);
                    return order == 0 ? a.DefinitionRevision.CompareTo(b.DefinitionRevision) : order; });
                return result;
            }
        }

        // Used only with trusted server publications / snapshots. Character commands never carry definitions.
        // A published revision cannot be overwritten, and appearance IDs are restricted to shipped assets.
        public static bool TryRegisterDefinition(EconomyItemConfig config, out string reason) =>
            ValidateOrRegisterDefinition(config, true, out reason);

        public static bool CanRegisterDefinition(EconomyItemConfig config, out string reason) =>
            ValidateOrRegisterDefinition(config, false, out reason);

        private static bool ValidateOrRegisterDefinition(EconomyItemConfig config, bool publish, out string reason)
        {
            reason = string.Empty;
            if (config == null || string.IsNullOrWhiteSpace(config.definitionId) || config.definitionRevision < 1)
            { reason = "Definição de equipamento inválida."; return false; }
            lock (DefinitionGate)
            {
                EnsureDefinitions();
                if (!canonical.TryGetValue(config.definitionId, out EquipmentItem basis))
                { reason = "A definição não corresponde a um equipamento instalado."; return false; }
                if (config.slot != (int)basis.Slot || string.IsNullOrWhiteSpace(config.name) || config.name.Length > 96 ||
                    (config.appearanceId != "training" && config.appearanceId != "steel" && config.appearanceId != "arcane") ||
                    config.minLevel < 1 || config.minLevel > 50 || config.minAttack < 0 || config.minDefense < 0 ||
                    config.minIntelligence < 0 || config.minSwordProficiency < 0 || config.minSwordProficiency > 50 ||
                    config.minAttack > 250 || config.minDefense > 250 || config.minIntelligence > 250 ||
                    config.attack < 0 || config.defense < 0 || config.intelligence < 0 || config.damage < 0 || config.health < 0 ||
                    config.attack > 100000 || config.defense > 100000 || config.intelligence > 100000 ||
                    config.damage > 100000 || config.health > 1000000)
                { reason = "Atributos, requisitos, espaço ou aparência de equipamento inválidos."; return false; }
                EquipmentItem proposed = new EquipmentItem(config.definitionId, config.name, basis.Slot,
                    new EquipmentBonuses(config.attack, config.defense, config.intelligence, config.damage, config.health),
                    new WeaponRequirements(config.minLevel, config.minAttack, config.minDefense,
                        config.minIntelligence, config.minSwordProficiency), config.appearanceId)
                    .WithDefinitionRevision(config.definitionRevision);
                string key = Key(config.definitionId, config.definitionRevision);
                if (definitions.TryGetValue(key, out EquipmentItem previous))
                {
                    if (!SameDefinition(previous, proposed))
                    { reason = "Uma revisão publicada é imutável; crie uma nova revisão do equipamento."; return false; }
                    return true;
                }
                if (publish) definitions.Add(key, proposed);
                return true;
            }
        }

        public static EconomyItemConfig ToConfig(EquipmentItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return new EconomyItemConfig
            {
                definitionId = item.DefinitionId, definitionRevision = item.DefinitionRevision,
                name = item.Name, slot = (int)item.Slot, appearanceId = item.AppearanceId,
                rarity = item.AppearanceId == "arcane" ? "rare" : "common",
                attack = item.Bonuses.Attack, defense = item.Bonuses.Defense, intelligence = item.Bonuses.Intelligence,
                damage = item.Bonuses.Damage, health = item.Bonuses.Health,
                minLevel = item.Requirements.MinimumLevel, minAttack = item.Requirements.MinimumAttack,
                minDefense = item.Requirements.MinimumDefense, minIntelligence = item.Requirements.MinimumIntelligence,
                minSwordProficiency = item.Requirements.MinimumSwordProficiency
            };
        }

        private static bool SameDefinition(EquipmentItem a, EquipmentItem b) =>
            a.Name == b.Name && a.Slot == b.Slot && a.AppearanceId == b.AppearanceId &&
            a.Bonuses.Attack == b.Bonuses.Attack && a.Bonuses.Defense == b.Bonuses.Defense &&
            a.Bonuses.Intelligence == b.Bonuses.Intelligence && a.Bonuses.Damage == b.Bonuses.Damage &&
            a.Bonuses.Health == b.Bonuses.Health && a.Requirements.MinimumLevel == b.Requirements.MinimumLevel &&
            a.Requirements.MinimumAttack == b.Requirements.MinimumAttack &&
            a.Requirements.MinimumDefense == b.Requirements.MinimumDefense &&
            a.Requirements.MinimumIntelligence == b.Requirements.MinimumIntelligence &&
            a.Requirements.MinimumSwordProficiency == b.Requirements.MinimumSwordProficiency;

        private static WeaponRequirements MagicRequirements() => new WeaponRequirements(3, 0, 0, 4, 0);
    }
}
