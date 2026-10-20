using System;

namespace GuerraDasNacoes
{
    public sealed class PlayerProgression
    {
        private const int MaximumLevel = 50;

        public int Level { get; private set; } = 1;
        public int Experience { get; private set; }
        public int ExperienceToNextLevel => IsMaxLevel ? 0 : 100 + (Level - 1) * 50;
        public int Health { get; private set; } = 100;
        public long Coins { get; private set; }
        public int HealthPotions { get; private set; }
        public int Scrap { get; private set; }
        public int BaseMaxHealth => 100 + (Level - 1) * 20;
        public int MaxHealth => global::GuerraDasNacoes.EquipmentBonuses.SaturatingAdd(BaseMaxHealth, EquipmentBonuses.Health);
        public int BaseAttackDamage => 18 + (Level - 1) * 4;
        public int EquippedBaseDamage => global::GuerraDasNacoes.EquipmentBonuses.SaturatingAdd(BaseAttackDamage, EquipmentBonuses.Damage);
        public int AttackDamage => GetBasicDamage();
        public int AttackPoints { get; private set; }
        public int DefensePoints { get; private set; }
        public int IntelligencePoints { get; private set; }
        public int UnspentPoints { get; private set; } = CombatScaling.PointsPerLevel;
        public int SwordProficiencyLevel { get; } = 1;
        public EquipmentState Equipment { get; }
        public EquipmentBonuses EquipmentBonuses => Equipment.Bonuses;
        public int EffectiveAttackPoints => global::GuerraDasNacoes.EquipmentBonuses.SaturatingAdd(AttackPoints, EquipmentBonuses.Attack);
        public int EffectiveDefensePoints => global::GuerraDasNacoes.EquipmentBonuses.SaturatingAdd(DefensePoints, EquipmentBonuses.Defense);
        public int EffectiveIntelligencePoints => global::GuerraDasNacoes.EquipmentBonuses.SaturatingAdd(IntelligencePoints, EquipmentBonuses.Intelligence);
        public WeaponDefinition EquippedWeapon => Equipment.GetEquipped(EquipmentSlot.Sword)?.Weapon;
        public double PhysicalAttackMultiplier => CombatScaling.PhysicalAttack(EffectiveAttackPoints);
        public double SkillAttackMultiplier => CombatScaling.SkillAttack(EffectiveAttackPoints);
        public double MagicDamageMultiplier => CombatScaling.MagicDamage(EffectiveIntelligencePoints);
        public double MagicDurationMultiplier => CombatScaling.MagicDuration(EffectiveIntelligencePoints);
        public double DamageTakenMultiplier => CombatScaling.DamageTaken(EffectiveDefensePoints);
        public bool IsAlive => Health > 0;
        public bool IsMaxLevel => Level >= MaximumLevel;

        public PlayerProgression()
        {
            Equipment = new EquipmentState(this);
        }

        private PlayerProgression(CharacterSaveData data)
        {
            Level = data.level;
            Experience = data.experience;
            AttackPoints = data.attackPoints;
            DefensePoints = data.defensePoints;
            IntelligencePoints = data.intelligencePoints;
            UnspentPoints = data.unspentPoints;
            // Restore ownership only after base attributes are present, so prerequisites are checked
            // against allocated points rather than equipment bonuses or a temporary level-one hero.
            Coins = data.schemaVersion == 1 ? 0 : data.coins;
            HealthPotions = data.schemaVersion == 1 ? 0 : data.healthPotions;
            Scrap = data.schemaVersion == 1 ? 0 : data.scrap;
            Equipment = new EquipmentState(this, data);
            if (data.health < 0 || data.health > MaxHealth)
                throw new ArgumentException("A vida salva está fora dos limites do personagem.", nameof(data));
            Health = data.health;
        }

        public CharacterSaveData CaptureSaveData()
        {
            CharacterSaveData data = new CharacterSaveData
            {
                level = Level,
                experience = Experience,
                attackPoints = AttackPoints,
                defensePoints = DefensePoints,
                intelligencePoints = IntelligencePoints,
                unspentPoints = UnspentPoints,
                swordProficiencyLevel = SwordProficiencyLevel,
                health = Health,
                coins = Coins, healthPotions = HealthPotions, scrap = Scrap
            };
            Equipment.CaptureSaveData(out data.inventoryItemIds, out data.equipped, out data.itemInstances);
            return data;
        }

        public static PlayerProgression FromSaveData(CharacterSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion != 1 && data.schemaVersion != CharacterSaveData.CurrentSchemaVersion)
                throw new ArgumentException("A versão do progresso salvo não é suportada.", nameof(data));
            if (data.schemaVersion >= 2 && (data.coins < 0 || data.healthPotions < 0 || data.scrap < 0))
                throw new ArgumentException("O patrimônio monetário salvo não pode ser negativo.", nameof(data));
            if (data.level < 1 || data.level > MaximumLevel)
                throw new ArgumentException("O nível salvo está fora dos limites do jogo.", nameof(data));
            int experienceLimit = data.level == MaximumLevel ? 1 : 100 + (data.level - 1) * 50;
            if (data.experience < 0 || data.experience >= experienceLimit)
                throw new ArgumentException("A experiência salva não corresponde ao nível.", nameof(data));
            if (data.attackPoints < 0 || data.defensePoints < 0 || data.intelligencePoints < 0 ||
                data.unspentPoints < 0 || (long)data.attackPoints + data.defensePoints +
                data.intelligencePoints + data.unspentPoints != data.level * CombatScaling.PointsPerLevel)
                throw new ArgumentException("Os pontos salvos não correspondem à progressão do personagem.", nameof(data));
            // Proficiency advancement has not been introduced yet. Accepting arbitrary values here
            // would unlock the veteran sword without any legitimate game action.
            if (data.swordProficiencyLevel != 1)
                throw new ArgumentException("A proficiência salva não corresponde às regras atuais.", nameof(data));
            return new PlayerProgression(data);
        }

        public bool TryCreditCoins(long amount)
        {
            if (amount <= 0 || amount > long.MaxValue - Coins) return false;
            Coins = checked(Coins + amount);
            return true;
        }

        public bool TrySpendCoins(long amount)
        {
            if (amount <= 0 || amount > Coins) return false;
            Coins = checked(Coins - amount);
            return true;
        }

        public bool TryAddHealthPotions(int amount)
        {
            if (amount <= 0 || amount > int.MaxValue - HealthPotions) return false;
            HealthPotions = checked(HealthPotions + amount);
            return true;
        }

        public bool TryConsumeHealthPotion()
        {
            if (HealthPotions <= 0 || !IsAlive || Health >= MaxHealth) return false;
            HealthPotions--;
            Heal(Math.Max(1, MaxHealth / 2));
            return true;
        }

        public bool TryAddScrap(int amount)
        {
            if (amount <= 0 || amount > int.MaxValue - Scrap) return false;
            Scrap = checked(Scrap + amount);
            return true;
        }

        public bool TryRemoveScrap(int amount)
        {
            if (amount <= 0 || amount > Scrap) return false;
            Scrap -= amount;
            return true;
        }

        public bool TryAllocate(HeroAttribute attribute, int amount = 1)
        {
            if (amount <= 0 || amount > UnspentPoints) return false;
            switch (attribute)
            {
                case HeroAttribute.Attack: AttackPoints += amount; break;
                case HeroAttribute.Defense: DefensePoints += amount; break;
                case HeroAttribute.Intelligence: IntelligencePoints += amount; break;
                default: return false;
            }
            UnspentPoints -= amount;
            return true;
        }

        public int GetBasicDamage(double outgoingMultiplier = 1d)
        {
            if (!CombatScaling.IsPositive(outgoingMultiplier)) return 0;
            return CombatScaling.RoundDamage(EquippedBaseDamage * PhysicalAttackMultiplier * outgoingMultiplier);
        }

        public int GetSkillDamage(double power = 1.8d, bool magical = true, double outgoingMultiplier = 1d)
        {
            if (!CombatScaling.IsPositive(power) || !CombatScaling.IsPositive(outgoingMultiplier)) return 0;
            // Skills start from base damage, not the already scaled basic attack.
            return CombatScaling.RoundDamage(EquippedBaseDamage * power * SkillAttackMultiplier *
                (magical ? MagicDamageMultiplier : 1d) * outgoingMultiplier);
        }

        public double GetMagicDuration(double baseSeconds)
        {
            return CombatScaling.ScaleDuration(baseSeconds, MagicDurationMultiplier);
        }

        public bool TryEquipWeapon(WeaponDefinition weapon, out string reason)
        {
            return Equipment.TryEquipLegacyWeapon(weapon, out reason);
        }

        internal void ClampHealthToMaximum()
        {
            // Equipping a health bonus never heals or resurrects. Removing it only clamps excess HP.
            Health = Math.Min(Health, MaxHealth);
        }

        public int ReceiveDamage(int amount, double incomingMultiplier = 1d)
        {
            if (amount <= 0 || !IsAlive || !CombatScaling.IsPositive(incomingMultiplier))
                return 0;

            int scaledDamage = CombatScaling.RoundDamage(amount * DamageTakenMultiplier * incomingMultiplier);
            int appliedDamage = Math.Min(scaledDamage, Health);
            Health -= appliedDamage;
            return appliedDamage;
        }

        public int GrantExperience(int amount)
        {
            if (amount <= 0 || IsMaxLevel)
                return 0;

            long accumulatedExperience = (long)Experience + amount;
            int levelsGained = 0;
            while (!IsMaxLevel && accumulatedExperience >= ExperienceToNextLevel)
            {
                accumulatedExperience -= ExperienceToNextLevel;
                Level++;
                levelsGained++;
                UnspentPoints += CombatScaling.PointsPerLevel;
                RestoreHealth();
            }

            Experience = IsMaxLevel ? 0 : (int)accumulatedExperience;
            return levelsGained;
        }

        public int Heal(int amount)
        {
            if (amount <= 0 || !IsAlive)
                return 0;
            int restored = Math.Min(amount, MaxHealth - Health);
            Health += restored;
            return restored;
        }

        public void RestoreHealth()
        {
            Health = MaxHealth;
        }
    }
}
