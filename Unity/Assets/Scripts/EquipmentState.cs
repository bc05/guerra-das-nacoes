using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GuerraDasNacoes
{
    public sealed class EquipmentState
    {
        private readonly PlayerProgression owner;
        private readonly Dictionary<string, EquipmentItem> owned = new Dictionary<string, EquipmentItem>(StringComparer.Ordinal);
        private readonly Dictionary<EquipmentSlot, EquipmentItem> equipped = new Dictionary<EquipmentSlot, EquipmentItem>();
        private readonly List<EquipmentItem> inventory = new List<EquipmentItem>();
        public IReadOnlyList<EquipmentItem> Inventory { get; }
        public int Revision { get; private set; }
        public EquipmentBonuses Bonuses { get; private set; } = EquipmentBonuses.None;

        public const int InventoryCapacity = 128;
        public int OwnedCount => owned.Count;
        public bool HasInventorySpace => inventory.Count < InventoryCapacity;

        internal EquipmentState(PlayerProgression player)
        {
            owner = player ?? throw new ArgumentNullException(nameof(player));
            Inventory = new ReadOnlyCollection<EquipmentItem>(inventory);
            foreach (EquipmentItem definition in EquipmentCatalog.CreateTrainingSet())
            {
                EquipmentItem item = definition.CreateInstance(definition.Id, true, "starter");
                owned.Add(item.Id, item);
                equipped.Add(item.Slot, item);
            }
        }

        internal EquipmentState(PlayerProgression player, CharacterSaveData data)
        {
            owner = player ?? throw new ArgumentNullException(nameof(player));
            Inventory = new ReadOnlyCollection<EquipmentItem>(inventory);
            if (data.inventoryItemIds == null || data.equipped == null)
                throw new ArgumentException("O progresso salvo precisa conter mochila e equipamentos.");
            int slotCount = Enum.GetValues(typeof(EquipmentSlot)).Length;
            if (data.equipped.Length > slotCount || data.inventoryItemIds.Length > InventoryCapacity)
                throw new ArgumentException("O patrimônio salvo excede a capacidade do personagem.");

            if (data.schemaVersion == 1)
            {
                // Version one stored definition IDs as ownership IDs. Retaining those IDs produces
                // a deterministic migration and preserves every existing equipment command/slot.
                EquipmentItem[] original = EquipmentCatalog.GetLegacyDefinitions();
                if ((long)data.inventoryItemIds.Length + data.equipped.Length != original.Length)
                    throw new ArgumentException("O patrimônio legado não corresponde ao catálogo original.");
                foreach (EquipmentItem definition in original)
                    owned.Add(definition.Id, definition.CreateInstance(definition.Id, true, "legacy"));
            }
            else
            {
                if (data.itemInstances == null || data.itemInstances.Length > InventoryCapacity + slotCount ||
                    data.itemInstances.Length != data.inventoryItemIds.Length + data.equipped.Length)
                    throw new ArgumentException("O progresso salvo precisa localizar todas as instâncias.");
                foreach (EquipmentInstanceSave saved in data.itemInstances)
                {
                    if (saved == null || string.IsNullOrWhiteSpace(saved.instanceId) || saved.instanceId.Length > 128 ||
                        string.IsNullOrWhiteSpace(saved.origin) || saved.origin.Length > 64 ||
                        owned.ContainsKey(saved.instanceId))
                        throw new ArgumentException("O progresso salvo contém instâncias inválidas ou duplicadas.");
                    EquipmentItem definition = EquipmentCatalog.GetDefinition(saved.definitionId, saved.definitionRevision);
                    if (definition == null)
                        throw new ArgumentException("O progresso referencia uma revisão de equipamento não instalada.");
                    owned.Add(saved.instanceId, definition.CreateInstance(saved.instanceId, saved.bound, saved.origin));
                }
            }

            HashSet<string> located = new HashSet<string>(StringComparer.Ordinal);
            foreach (string itemId in data.inventoryItemIds)
                inventory.Add(LocateSavedItem(itemId, located));
            foreach (EquipmentSlotSave saved in data.equipped)
            {
                if (saved == null || !Enum.IsDefined(typeof(EquipmentSlot), saved.slot))
                    throw new ArgumentException("O progresso salvo contém um espaço de equipamento inválido.");
                EquipmentSlot slot = (EquipmentSlot)saved.slot;
                if (equipped.ContainsKey(slot))
                    throw new ArgumentException("O progresso salvo repete um espaço de equipamento.");
                EquipmentItem item = LocateSavedItem(saved.itemId, located);
                if (!item.FitsSlot(slot))
                    throw new ArgumentException("Um item salvo não corresponde ao espaço equipado.");
                if (!WeaponRequirementEvaluator.CanEquip(owner, item.Requirements, out string reason))
                    throw new ArgumentException("Um equipamento salvo não atende aos requisitos: " + reason);
                equipped.Add(slot, item);
            }
            if (located.Count != owned.Count)
                throw new ArgumentException("O progresso salvo perdeu a localização de itens do personagem.");
            Bonuses = SumBonuses(null, null);
        }

        internal void CaptureSaveData(out string[] inventoryItemIds, out EquipmentSlotSave[] slots,
            out EquipmentInstanceSave[] instances)
        {
            instances = new EquipmentInstanceSave[owned.Count];
            int instanceIndex = 0;
            foreach (EquipmentItem item in owned.Values)
            {
                if (EquipmentCatalog.GetDefinition(item.DefinitionId, item.DefinitionRevision) == null)
                    throw new InvalidOperationException("O personagem contém um item fora do catálogo persistente.");
                instances[instanceIndex++] = new EquipmentInstanceSave
                {
                    instanceId = item.Id, definitionId = item.DefinitionId,
                    definitionRevision = item.DefinitionRevision, bound = item.Bound, origin = item.Origin
                };
            }
            Array.Sort(instances, (a, b) => string.CompareOrdinal(a.instanceId, b.instanceId));
            inventoryItemIds = new string[inventory.Count];
            for (int i = 0; i < inventory.Count; i++) inventoryItemIds[i] = inventory[i].Id;
            slots = new EquipmentSlotSave[equipped.Count];
            int index = 0;
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                if (equipped.TryGetValue(slot, out EquipmentItem item))
                    slots[index++] = new EquipmentSlotSave { slot = (int)slot, itemId = item.Id,
                        definitionId = item.DefinitionId, definitionRevision = item.DefinitionRevision };
        }

        // Kept for controlled integrations that only need slot/instance layout.
        internal void CaptureSaveData(out string[] inventoryItemIds, out EquipmentSlotSave[] slots)
        { CaptureSaveData(out inventoryItemIds, out slots, out EquipmentInstanceSave[] ignored); }

        private EquipmentItem LocateSavedItem(string id, HashSet<string> located)
        {
            if (string.IsNullOrWhiteSpace(id) || !owned.TryGetValue(id, out EquipmentItem item))
                throw new ArgumentException("O progresso salvo contém um item desconhecido.");
            if (!located.Add(id))
                throw new ArgumentException("O progresso salvo duplica a localização de um item.");
            return item;
        }



        public EquipmentItem GetOwned(string instanceId) => instanceId != null && owned.TryGetValue(instanceId,
            out EquipmentItem item) ? item : null;
        public string GetDefinitionId(string instanceId) => GetOwned(instanceId)?.DefinitionId;

        public bool TryAddItem(string definitionId, string instanceId, out string reason, bool bound = false,
            string origin = "drop", int definitionRevision = 1)
        {
            reason = string.Empty;
            EquipmentItem definition = EquipmentCatalog.GetDefinition(definitionId, definitionRevision);
            if (definition == null) { reason = "Definição de equipamento desconhecida."; return false; }
            if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 128 ||
                string.IsNullOrWhiteSpace(origin) || origin.Length > 64)
            { reason = "Instância de equipamento inválida."; return false; }
            if (owned.ContainsKey(instanceId)) { reason = "Esta instância já pertence ao personagem."; return false; }
            if (!HasInventorySpace) { reason = "A mochila está cheia."; return false; }
            EquipmentItem item = definition.CreateInstance(instanceId, bound, origin);
            owned.Add(item.Id, item);
            inventory.Add(item);
            OnChanged();
            return true;
        }

        public bool TryRemoveInventoryItem(string instanceId, out string reason)
        {
            EquipmentItem item = GetOwned(instanceId);
            if (item == null || !inventory.Contains(item))
            { reason = "O item precisa pertencer ao personagem e estar na mochila."; return false; }
            inventory.Remove(item);
            owned.Remove(item.Id);
            OnChanged();
            reason = string.Empty;
            return true;
        }

        public EquipmentItem GetEquipped(EquipmentSlot slot) => equipped.TryGetValue(slot, out EquipmentItem item) ? item : null;

        // Read-only comparison, including items whose requirements have not been met yet.
        // Mutation and ownership validation remain exclusively in TryEquip/TryUnequip.
        public EquipmentBonuses PreviewBonuses(EquipmentSlot slot, EquipmentItem replacement)
        {
            if (!Enum.IsDefined(typeof(EquipmentSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            if (replacement != null && !replacement.FitsSlot(slot))
                throw new ArgumentException("A peça não corresponde ao espaço comparado.", nameof(replacement));
            return SumBonuses(slot, replacement);
        }

        public bool CanEquip(string itemId, EquipmentSlot slot, out string reason)
        {
            if (!Enum.IsDefined(typeof(EquipmentSlot), slot)) { reason = "Espaço de equipamento inválido."; return false; }
            if (itemId == null || !owned.TryGetValue(itemId, out EquipmentItem item))
            { reason = "Este item não está no inventário."; return false; }
            if (!item.FitsSlot(slot)) { reason = "Este item não pode ocupar este espaço."; return false; }
            foreach (KeyValuePair<EquipmentSlot, EquipmentItem> entry in equipped)
            {
                if (entry.Value.Id != itemId) continue;
                reason = entry.Key == slot ? "Este item já está equipado neste espaço." : "Este item já está equipado em outro espaço.";
                return false;
            }
            return WeaponRequirementEvaluator.CanEquip(owner, item.Requirements, out reason);
        }

        public bool TryEquip(string itemId, EquipmentSlot slot, out string reason)
        {
            if (!CanEquip(itemId, slot, out reason)) return false;
            EquipmentItem item = owned[itemId];
            EquipmentItem previous = GetEquipped(slot);
            if (previous != null) inventory.Add(previous);
            inventory.Remove(item);
            equipped[slot] = item;
            OnChanged();
            return true;
        }

        public bool TryUnequip(EquipmentSlot slot, out string reason)
        {
            if (!Enum.IsDefined(typeof(EquipmentSlot), slot)) { reason = "Espaço de equipamento inválido."; return false; }
            EquipmentItem item = GetEquipped(slot);
            if (item == null) { reason = "Este espaço já está vazio."; return false; }
            if (!HasInventorySpace) { reason = "A mochila está cheia."; return false; }
            equipped.Remove(slot);
            inventory.Add(item);
            OnChanged();
            reason = string.Empty;
            return true;
        }

        // Compatibility for controlled code that supplied weapon definitions before inventory existed.
        // New UI uses owned item IDs exclusively. Existing IDs can never replace their definition.
        internal bool TryEquipLegacyWeapon(WeaponDefinition weapon, out string reason)
        {
            if (!WeaponRequirementEvaluator.CanEquip(owner, weapon, out reason)) return false;
            if (owned.TryGetValue(weapon.Id, out EquipmentItem item))
            {
                if (!ReferenceEquals(item.Weapon, weapon))
                { reason = "Identificador de item já pertence a outra definição."; return false; }
                if (ReferenceEquals(GetEquipped(EquipmentSlot.Sword), item)) { reason = string.Empty; return true; }
            }
            else
            {
                item = EquipmentItem.FromWeapon(weapon);
                owned.Add(item.Id, item);
                inventory.Add(item);
            }
            return TryEquip(item.Id, EquipmentSlot.Sword, out reason);
        }

        private void OnChanged()
        {
            Bonuses = SumBonuses(null, null);
            Revision = Revision == int.MaxValue ? 0 : Revision + 1;
            owner.ClampHealthToMaximum();
        }

        private EquipmentBonuses SumBonuses(EquipmentSlot? replacingSlot, EquipmentItem replacement)
        {
            EquipmentBonuses total = EquipmentBonuses.None;
            foreach (KeyValuePair<EquipmentSlot, EquipmentItem> entry in equipped)
                if (entry.Key != replacingSlot) total = total.Add(entry.Value.Bonuses);
            return replacement == null ? total : total.Add(replacement.Bonuses);
        }
    }
}
