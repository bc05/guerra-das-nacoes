using System;
using System.Collections.Generic;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class EconomyCoreChecks
{
    [MenuItem("Guerra das Nações/Validar instâncias e economia")]
    public static void Run()
    {
        foreach (EconomyItemConfig item in EconomyDefaults.Create().items)
            Require(item.rarity == "common" || item.rarity == "uncommon" || item.rarity == "rare" ||
                item.rarity == "epic" || item.rarity == "legendary", "raridade é independente da aparência de treinamento");
        PlayerProgression fresh = new PlayerProgression();
        Require(fresh.Equipment.OwnedCount == 12 && fresh.Equipment.Inventory.Count == 0,
            "personagem novo recebe somente doze peças vinculadas de treino");
        Require(fresh.CaptureSaveData().itemInstances.Length == 12, "doze instâncias são persistidas");
        CheckExpandedCatalogCompatibility();
        PlayerProgression old = LegacyFixture();
        Require(old.Equipment.OwnedCount == 35, "migração preserva todas as trinta e cinco peças antigas");
        old.GrantExperience(322); old.TryAllocate(HeroAttribute.Attack, 6);
        old.TryAllocate(HeroAttribute.Intelligence, 4); old.TryAllocate(HeroAttribute.Defense, 3);
        old.Equipment.TryUnequip(EquipmentSlot.Sword, out _);
        CharacterSaveData legacy = old.CaptureSaveData();
        legacy.schemaVersion = 1; legacy.itemInstances = null;
        PlayerProgression first = PlayerProgression.FromSaveData(legacy);
        PlayerProgression second = PlayerProgression.FromSaveData(legacy);
        Require(first.Level == 3 && first.Experience == 72 && first.AttackPoints == 6 &&
            first.DefensePoints == 3 && first.IntelligencePoints == 4 && first.UnspentPoints == 2,
            "migração não redistribui pontos nem experiência");
        Require(first.Equipment.GetEquipped(EquipmentSlot.Sword) == null && first.Equipment.OwnedCount == 35,
            "migração preserva espaços vazios sem criar equipamento");
        EquipmentInstanceSave[] one = first.CaptureSaveData().itemInstances;
        EquipmentInstanceSave[] two = second.CaptureSaveData().itemInstances;
        for (int i = 0; i < one.Length; i++)
            Require(one[i].instanceId == two[i].instanceId && one[i].bound && one[i].origin == "legacy",
                "migração é determinística e impede revenda do patrimônio gratuito");
        Require(first.Equipment.TryAddItem("steel-head", "test-drop-a", out _) &&
            first.Equipment.TryAddItem("steel-head", "test-drop-b", out _), "cópias da mesma definição coexistem");
        Require(!first.Equipment.TryAddItem("steel-head", "test-drop-a", out _), "instância não é concedida duas vezes");
        Require(first.Equipment.TryEquip("test-drop-a", EquipmentSlot.Head, out _) &&
            !first.Equipment.TryRemoveInventoryItem("test-drop-a", out _) &&
            first.Equipment.TryRemoveInventoryItem("test-drop-b", out _), "remoção distingue mochila e equipamento");
        PlayerProgression roundtrip = PlayerProgression.FromSaveData(first.CaptureSaveData());
        Require(roundtrip.Equipment.GetEquipped(EquipmentSlot.Head).DefinitionId == "steel-head" &&
            roundtrip.Equipment.GetEquipped(EquipmentSlot.Head).Id == "test-drop-a", "cópia equipada sobrevive à carga");
        Require(fresh.TryCreditCoins(long.MaxValue) && !fresh.TryCreditCoins(1) && fresh.Coins == long.MaxValue,
            "carteira recusa overflow sem alterar saldo");
        Require(!fresh.TrySpendCoins(-1) && fresh.TrySpendCoins(long.MaxValue) && !fresh.TrySpendCoins(1),
            "carteira recusa dívida e gasto negativo");
        Require(fresh.TryAddHealthPotions(1) && !fresh.TryConsumeHealthPotion() && fresh.HealthPotions == 1,
            "poção não é gasta com vida cheia");
        fresh.ReceiveDamage(30);
        Require(fresh.TryConsumeHealthPotion() && fresh.HealthPotions == 0 && fresh.Health == fresh.MaxHealth,
            "poção cura e consome uma unidade");
        int generated = 0;
        while (fresh.Equipment.HasInventorySpace)
        { Require(fresh.Equipment.TryAddItem("steel-head", "capacity-" + generated++, out _), "preenche mochila"); }
        Require(!fresh.Equipment.TryAddItem("steel-head", "overflow", out _) &&
            !fresh.Equipment.TryUnequip(EquipmentSlot.Head, out _), "mochila cheia não perde peça ao remover");
        Debug.Log("[ECONOMIA CORE TEST PASS] Migração v1-v2, 35 peças legadas, doze iniciais, cópias, carteira, poções e capacidade validadas.");
    }

    private static void CheckExpandedCatalogCompatibility()
    {
        EquipmentItem[] historical = EquipmentCatalog.GetLegacyDefinitions();
        Require(historical.Length == 35 && EquipmentCatalog.GetDefinitions().Length >= 38,
            "catálogo histórico fica em 35 com as três relíquias novas instaladas");
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (EquipmentItem item in historical)
            Require(item.DefinitionRevision == 1 && ids.Add(item.DefinitionId) &&
                !item.DefinitionId.StartsWith("sovereign-", StringComparison.Ordinal),
                "somente IDs originais e revisão um pertencem ao patrimônio v1");
        CharacterSaveData legacy = LegacyFixture().CaptureSaveData();
        legacy.schemaVersion = 1; legacy.itemInstances = null;
        foreach (EquipmentInstanceSave item in PlayerProgression.FromSaveData(legacy).CaptureSaveData().itemInstances)
            Require(ids.Contains(item.definitionId) && item.bound && item.origin == "legacy",
                "restaurar v1 não concede relíquias ou revisões novas");
        List<string> forgedInventory = new List<string>(legacy.inventoryItemIds) {
            "sovereign-sword", "sovereign-shield", "sovereign-necklace" };
        legacy.inventoryItemIds = forgedInventory.ToArray();
        bool refused = false;
        try { PlayerProgression.FromSaveData(legacy); } catch (ArgumentException) { refused = true; }
        Require(refused, "v1 com 38 peças não passa a ser aceito pela expansão");
        PlayerProgression bossOwner = new PlayerProgression();
        foreach (string id in new[] { "sovereign-sword", "sovereign-shield", "sovereign-necklace" })
            Require(bossOwner.Equipment.TryAddItem(id, "legitimate-" + id, out _, false, "world-boss"),
                "relíquia legítima pode entrar na mochila v2");
        CharacterSaveData restored = PlayerProgression.FromSaveData(bossOwner.CaptureSaveData()).CaptureSaveData();
        Require(restored.itemInstances.Length == 15 && restored.inventoryItemIds.Length == 3,
            "restaurar v2 preserva as três relíquias sem conceder outras peças");
        foreach (EquipmentInstanceSave item in restored.itemInstances)
            if (item.definitionId.StartsWith("sovereign-", StringComparison.Ordinal))
                Require(!item.bound && item.origin == "world-boss" && item.definitionRevision == 1,
                    "instâncias v2 preservam origem vínculo e revisão das relíquias");
    }

    // Old checks still exercise every former equipment rule using an explicit historical fixture.
    public static PlayerProgression LegacyFixture()
    {
        List<string> inventory = new List<string>();
        List<EquipmentSlotSave> equipped = new List<EquipmentSlotSave>();
        foreach (EquipmentItem definition in EquipmentCatalog.GetLegacyDefinitions())
        {
            if (definition.DefinitionId.StartsWith("training-", StringComparison.Ordinal))
                equipped.Add(new EquipmentSlotSave { slot = (int)definition.Slot, itemId = definition.DefinitionId });
            else inventory.Add(definition.DefinitionId);
        }
        equipped.Sort((a, b) => a.slot.CompareTo(b.slot));
        return PlayerProgression.FromSaveData(new CharacterSaveData { schemaVersion = 1, level = 1, health = 100,
            unspentPoints = 5, inventoryItemIds = inventory.ToArray(), equipped = equipped.ToArray() });
    }

    private static void Require(bool condition, string description)
    { if (!condition) throw new BuildFailedException("Falha na economia compartilhada: " + description); }
}
