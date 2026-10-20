using System;

namespace GuerraDasNacoes
{
    // Saves reference immutable definition revisions; they never accept client-supplied item bonuses.
    [Serializable]
    public sealed class CharacterSaveData
    {
        public const int CurrentSchemaVersion = 2;
        public int schemaVersion = CurrentSchemaVersion;
        public int level;
        public int experience;
        public int attackPoints;
        public int defensePoints;
        public int intelligencePoints;
        public int unspentPoints;
        public int swordProficiencyLevel = 1;
        public int health;
        public long coins;
        public int healthPotions;
        public int scrap;
        public string[] inventoryItemIds;
        public EquipmentSlotSave[] equipped;
        public EquipmentInstanceSave[] itemInstances;
    }

    [Serializable]
    public sealed class EquipmentInstanceSave
    {
        public string instanceId, definitionId, origin;
        public int definitionRevision = 1;
        public bool bound;
    }

    [Serializable]
    public sealed class EquipmentSlotSave
    {
        public int slot;
        public string itemId;
        // Public appearance metadata is separate from private ownership and wallet.
        public string definitionId;
        public int definitionRevision = 1;
    }
}
