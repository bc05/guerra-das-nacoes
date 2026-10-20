using System;
using System.Collections.Generic;

namespace GuerraDasNacoes
{
    public sealed class WeaponRequirements
    {
        public int MinimumLevel { get; }
        public int MinimumAttack { get; }
        public int MinimumDefense { get; }
        public int MinimumIntelligence { get; }
        public int MinimumSwordProficiency { get; }

        public WeaponRequirements(int minimumLevel = 1, int minimumAttack = 0, int minimumDefense = 0,
            int minimumIntelligence = 0, int minimumSwordProficiency = 1)
        {
            if (minimumLevel < 1 || minimumAttack < 0 || minimumDefense < 0 || minimumIntelligence < 0 ||
                minimumSwordProficiency < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumLevel), "Requisitos de arma não podem ser negativos e o nível mínimo deve ser pelo menos um.");
            MinimumLevel = minimumLevel;
            MinimumAttack = minimumAttack;
            MinimumDefense = minimumDefense;
            MinimumIntelligence = minimumIntelligence;
            MinimumSwordProficiency = minimumSwordProficiency;
        }

        public string Describe()
        {
            List<string> parts = new List<string> { $"Nível {MinimumLevel}" };
            if (MinimumAttack > 0) parts.Add($"Ataque {MinimumAttack}");
            if (MinimumDefense > 0) parts.Add($"Defesa {MinimumDefense}");
            if (MinimumIntelligence > 0) parts.Add($"Inteligência {MinimumIntelligence}");
            if (MinimumSwordProficiency > 0) parts.Add($"Proficiência em espada {MinimumSwordProficiency}");
            return string.Join(" · ", parts);
        }
    }

    public sealed class WeaponDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public WeaponRequirements Requirements { get; }
        public EquipmentBonuses Bonuses { get; }
        public string AppearanceId { get; }

        public WeaponDefinition(string id, string name, WeaponRequirements requirements,
            EquipmentBonuses bonuses = null, string appearanceId = "training")
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A arma precisa de um identificador.", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A arma precisa de um nome.", nameof(name));
            Id = id;
            Name = name;
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            Bonuses = bonuses ?? EquipmentBonuses.None;
            if (string.IsNullOrWhiteSpace(appearanceId)) throw new ArgumentException("A aparência precisa de um identificador.", nameof(appearanceId));
            AppearanceId = appearanceId;
        }

        // Shared instance preserves the original weapon API; equipment state owns the actual slot.
        public static WeaponDefinition TrainingSword { get; } = new WeaponDefinition("training-sword",
            "Espada de Treinamento", new WeaponRequirements());
    }

    public static class WeaponRequirementEvaluator
    {
        public static bool CanEquip(PlayerProgression player, WeaponDefinition weapon, out string reason)
        {
            if (player == null || weapon == null)
            {
                reason = "Personagem ou arma indisponível.";
                return false;
            }
            return CanEquip(player, weapon.Requirements, out reason);
        }

        public static bool CanEquip(PlayerProgression player, WeaponRequirements required, out string reason)
        {
            if (player == null || required == null)
            {
                reason = "Personagem ou requisitos indisponíveis.";
                return false;
            }
            // Equipment bonuses do not satisfy prerequisites, preventing self-qualification and cycles.
            List<string> missing = new List<string>();
            if (player.Level < required.MinimumLevel) missing.Add($"Nível {required.MinimumLevel}");
            if (player.AttackPoints < required.MinimumAttack) missing.Add($"Ataque {required.MinimumAttack}");
            if (player.DefensePoints < required.MinimumDefense) missing.Add($"Defesa {required.MinimumDefense}");
            if (player.IntelligencePoints < required.MinimumIntelligence) missing.Add($"Inteligência {required.MinimumIntelligence}");
            if (player.SwordProficiencyLevel < required.MinimumSwordProficiency)
                missing.Add($"Proficiência em espada {required.MinimumSwordProficiency}");
            reason = missing.Count == 0 ? string.Empty : "Requer: " + string.Join(" · ", missing);
            return missing.Count == 0;
        }
    }
}
