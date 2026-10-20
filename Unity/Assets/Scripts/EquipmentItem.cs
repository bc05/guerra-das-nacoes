using System;
using System.Collections.Generic;

namespace GuerraDasNacoes
{
    public enum EquipmentSlot
    {
        Sword, Shield, Head, Shoulders, Chest, Hands, Legs, Feet,
        RingLeft, RingRight, Belt, Necklace
    }

    // Values are immutable and nonnegative. Totals saturate rather than overflowing.
    public sealed class EquipmentBonuses
    {
        public static EquipmentBonuses None { get; } = new EquipmentBonuses();
        public int Attack { get; }
        public int Defense { get; }
        public int Intelligence { get; }
        public int Damage { get; }
        public int Health { get; }

        public EquipmentBonuses(int attack = 0, int defense = 0, int intelligence = 0,
            int damage = 0, int health = 0)
        {
            if (attack < 0 || defense < 0 || intelligence < 0 || damage < 0 || health < 0)
                throw new ArgumentOutOfRangeException(nameof(attack), "Bônus de equipamento não podem ser negativos.");
            Attack = attack; Defense = defense; Intelligence = intelligence; Damage = damage; Health = health;
        }

        public EquipmentBonuses Add(EquipmentBonuses other)
        {
            if (other == null) return this;
            return new EquipmentBonuses(SaturatingAdd(Attack, other.Attack), SaturatingAdd(Defense, other.Defense),
                SaturatingAdd(Intelligence, other.Intelligence), SaturatingAdd(Damage, other.Damage),
                SaturatingAdd(Health, other.Health));
        }

        public string Describe()
        {
            List<string> parts = new List<string>();
            if (Attack != 0) parts.Add($"+{Attack} Ataque");
            if (Defense != 0) parts.Add($"+{Defense} Defesa");
            if (Intelligence != 0) parts.Add($"+{Intelligence} Inteligência");
            if (Damage != 0) parts.Add($"+{Damage} Dano base");
            if (Health != 0) parts.Add($"+{Health} Vida máxima");
            return parts.Count == 0 ? "Sem bônus adicionais" : string.Join(" · ", parts);
        }

        public static int SaturatingAdd(int left, int right) => (int)Math.Min(int.MaxValue, (long)left + right);
    }

    public sealed class EquipmentItem
    {
        public string Id { get; }
        public string DefinitionId { get; private set; }
        public int DefinitionRevision { get; private set; } = 1;
        public bool Bound { get; private set; }
        public string Origin { get; private set; } = "definition";
        public string Name { get; }
        public EquipmentSlot Slot { get; }
        public EquipmentBonuses Bonuses { get; }
        public WeaponRequirements Requirements { get; }
        public string AppearanceId { get; }
        public WeaponDefinition Weapon { get; }

        public EquipmentItem(string id, string name, EquipmentSlot slot, EquipmentBonuses bonuses = null,
            WeaponRequirements requirements = null, string appearanceId = "training")
            : this(id, name, slot, bonuses, requirements, appearanceId, null) { }

        private EquipmentItem(string id, string name, EquipmentSlot slot, EquipmentBonuses bonuses,
            WeaponRequirements requirements, string appearanceId, WeaponDefinition weapon)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("O item precisa de um identificador.", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("O item precisa de um nome.", nameof(name));
            if (!Enum.IsDefined(typeof(EquipmentSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            if (string.IsNullOrWhiteSpace(appearanceId)) throw new ArgumentException("A aparência precisa de um identificador.", nameof(appearanceId));
            Id = id; DefinitionId = id; Name = name; Slot = slot; Bonuses = bonuses ?? EquipmentBonuses.None;
            Requirements = requirements ?? new WeaponRequirements(minimumSwordProficiency: slot == EquipmentSlot.Sword ? 1 : 0);
            AppearanceId = appearanceId;
            Weapon = slot == EquipmentSlot.Sword ? weapon ?? new WeaponDefinition(id, name, Requirements, Bonuses, appearanceId) : null;
        }

        // Definitions are canonical immutable templates; ownership always refers to the instance ID.
        public EquipmentItem CreateInstance(string instanceId, bool bound = false, string origin = "drop")
        {
            if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 128)
                throw new ArgumentException("Identificador de instância inválido.", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(origin) || origin.Length > 64)
                throw new ArgumentException("Origem de equipamento inválida.", nameof(origin));
            return new EquipmentItem(instanceId, Name, Slot, Bonuses, Requirements, AppearanceId, Weapon)
            { DefinitionId = DefinitionId, DefinitionRevision = DefinitionRevision, Bound = bound, Origin = origin };
        }

        internal EquipmentItem WithDefinitionRevision(int revision)
        {
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            DefinitionRevision = revision;
            return this;
        }

        public bool FitsSlot(EquipmentSlot slot) => Slot == slot ||
            ((Slot == EquipmentSlot.RingLeft || Slot == EquipmentSlot.RingRight) &&
             (slot == EquipmentSlot.RingLeft || slot == EquipmentSlot.RingRight));

        public static EquipmentItem FromWeapon(WeaponDefinition weapon)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            return new EquipmentItem(weapon.Id, weapon.Name, EquipmentSlot.Sword, weapon.Bonuses,
                weapon.Requirements, weapon.AppearanceId, weapon);
        }
    }
}
