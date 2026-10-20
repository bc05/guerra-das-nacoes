using System;
using System.Collections.Generic;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class ProgressionPersistenceChecks
{
    [MenuItem("Guerra das Nações/Validar persistência da progressão")]
    public static void Run()
    {
        CheckInitialRoundTrip();
        CheckAdvancedRoundTrip();
        CheckEmptySlotsAndDefeat();
        CheckMaximumLevel();
        CheckIndependentSnapshots();
        CheckInvalidProgression();
        CheckInvalidOwnership();
        CheckInvalidEquipment();
        CheckLegacyItemsAreNotPersisted();
        Debug.Log("[PERSISTENCIA TEST PASS] Versão, nível, XP, pontos, vida, 35 itens, requisitos, anéis e cópias independentes validados.");
    }

    private static void CheckInitialRoundTrip()
    {
        PlayerProgression original = new PlayerProgression();
        CharacterSaveData save = original.CaptureSaveData();
        Equal(2, save.schemaVersion, "snapshot tem versão explícita");
        Equal(0, save.inventoryItemIds.Length, "mochila inicial aguarda drops e compras");
        Equal(12, save.equipped.Length, "conjunto inicial tem 12 peças");
        PlayerProgression restored = PlayerProgression.FromSaveData(save);
        SameProgression(original, restored);
        Require(ReferenceEquals(restored.EquippedWeapon, WeaponDefinition.TrainingSword),
            "arma usa a definição canônica após carregar");
        Equal(0, restored.Equipment.Revision, "carga não representa uma troca de equipamento");
        Equal(0, restored.EquipmentBonuses.Attack, "treino não inventa bônus");
        Equal(18, restored.GetBasicDamage(), "carga mantém cálculo básico original");
    }

    private static void CheckAdvancedRoundTrip()
    {
        PlayerProgression original = EconomyCoreChecks.LegacyFixture();
        original.GrantExperience(322);
        Require(original.TryAllocate(HeroAttribute.Attack, 6), "distribui ataque do personagem avançado");
        Require(original.TryAllocate(HeroAttribute.Defense, 3), "distribui defesa do personagem avançado");
        Require(original.TryAllocate(HeroAttribute.Intelligence, 4), "distribui inteligência do personagem avançado");
        Equip(original, "arcane-sword", EquipmentSlot.Sword);
        Equip(original, "arcane-chest", EquipmentSlot.Chest);
        // Either ring fits either hand; native slot names do not constrain the saved placement.
        Equip(original, "arcane-ring", EquipmentSlot.RingLeft);
        Equip(original, "steel-ring", EquipmentSlot.RingRight);
        Equip(original, "steel-belt", EquipmentSlot.Belt);
        original.RestoreHealth();
        original.ReceiveDamage(71);
        CharacterSaveData save = original.CaptureSaveData();
        PlayerProgression restored = PlayerProgression.FromSaveData(save);
        SameProgression(original, restored);
        Equal(3, restored.Level, "nível não é reconstruído concedendo XP adicional");
        Equal(72, restored.Experience, "XP excedente é preservado");
        Equal(2, restored.UnspentPoints, "saldo distribuível é preservado");
        Equal(original.MaxHealth, restored.MaxHealth, "vida máxima é reconstruída pelos itens");
        Equal(original.GetBasicDamage(), restored.GetBasicDamage(), "dano é reconstruído pelas regras");
        Equal(original.GetSkillDamage(), restored.GetSkillDamage(), "dano mágico é reconstruído pelas regras");
        Near(original.GetMagicDuration(8d), restored.GetMagicDuration(8d), "duração é reconstruída pelas regras");
        Require(restored.Health < restored.MaxHealth, "carga de personagem ferido não cura");
        Require(restored.Equipment.GetEquipped(EquipmentSlot.RingLeft).Id == "arcane-ring" &&
            restored.Equipment.GetEquipped(EquipmentSlot.RingRight).Id == "steel-ring", "posição dos dois anéis é preservada");
    }

    private static void CheckEmptySlotsAndDefeat()
    {
        PlayerProgression original = EconomyCoreChecks.LegacyFixture();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            Require(original.Equipment.TryUnequip(slot, out _), "remove peça antes de salvar sem armadura");
        original.ReceiveDamage(int.MaxValue);
        CharacterSaveData save = original.CaptureSaveData();
        Equal(35, save.inventoryItemIds.Length, "todas as peças são preservadas na mochila");
        Equal(0, save.equipped.Length, "nenhum equipamento pode ser um estado legítimo");
        PlayerProgression restored = PlayerProgression.FromSaveData(save);
        SameProgression(original, restored);
        Equal(0, restored.Health, "carga não ressuscita personagem");
        Require(!restored.IsAlive && restored.EquippedWeapon == null, "derrota e espada vazia são preservadas");
    }

    private static void CheckMaximumLevel()
    {
        PlayerProgression original = new PlayerProgression();
        original.GrantExperience(int.MaxValue);
        original.TryAllocate(HeroAttribute.Attack, 84);
        original.TryAllocate(HeroAttribute.Defense, 83);
        original.TryAllocate(HeroAttribute.Intelligence, 82);
        PlayerProgression restored = PlayerProgression.FromSaveData(original.CaptureSaveData());
        SameProgression(original, restored);
        Equal(50, restored.Level, "nível máximo é restaurado");
        Equal(0, restored.Experience, "nível máximo não retém XP extra");
        Equal(1, restored.UnspentPoints, "saldo de 250 pontos totais no limite");
    }

    private static void CheckIndependentSnapshots()
    {
        PlayerProgression original = EconomyCoreChecks.LegacyFixture();
        CharacterSaveData first = original.CaptureSaveData();
        CharacterSaveData second = original.CaptureSaveData();
        Require(!ReferenceEquals(first, second) && !ReferenceEquals(first.inventoryItemIds, second.inventoryItemIds) &&
            !ReferenceEquals(first.equipped, second.equipped) && !ReferenceEquals(first.equipped[0], second.equipped[0]),
            "cada captura é uma cópia separada e profunda dos registros mutáveis");
        PlayerProgression restored = PlayerProgression.FromSaveData(first);
        string firstInventoryId = first.inventoryItemIds[0];
        string firstEquippedId = first.equipped[0].itemId;
        restored.TryAllocate(HeroAttribute.Attack, 2);
        Equip(restored, "steel-sword", EquipmentSlot.Sword);
        Equal(0, first.attackPoints, "alterar personagem restaurado não altera snapshot de entrada");
        Require(first.inventoryItemIds[0] == firstInventoryId && first.equipped[0].itemId == firstEquippedId,
            "troca não altera arrays ou registros de entrada");
        first.level = 50;
        first.inventoryItemIds[0] = "invalid";
        first.equipped[0].itemId = "invalid";
        Equal(1, restored.Level, "alteração posterior do snapshot não altera personagem carregado");
        Require(restored.Equipment.GetEquipped(EquipmentSlot.Sword).Id == "steel-sword",
            "definições carregadas não mantêm referência a registros mutáveis do snapshot");
        Equal(0, original.AttackPoints, "restauração não compartilha estado mutável com personagem de origem");
        Require(original.Equipment.GetEquipped(EquipmentSlot.Sword).Id == "training-sword", "origem preserva espada");
        Require(second.inventoryItemIds[0] == firstInventoryId && second.equipped[0].itemId == firstEquippedId,
            "mutação de uma captura não altera a outra");
    }

    private static void CheckInvalidProgression()
    {
        Reject(null, "snapshot ausente");
        RejectChanged(s => s.schemaVersion = 0, "versão ausente ou zero");
        RejectChanged(s => s.schemaVersion = 3, "versão futura");
        RejectChanged(s => s.level = 0, "nível zero");
        RejectChanged(s => s.level = 51, "nível acima do limite");
        RejectChanged(s => s.level = int.MaxValue, "nível com risco de overflow");
        RejectChanged(s => s.experience = -1, "XP negativo");
        RejectChanged(s => s.experience = 100, "XP que deveria ter concedido um nível");
        RejectChanged(s => s.attackPoints = -1, "ataque negativo");
        RejectChanged(s => s.defensePoints = -1, "defesa negativa");
        RejectChanged(s => s.intelligencePoints = -1, "inteligência negativa");
        RejectChanged(s => s.unspentPoints = -1, "saldo negativo");
        RejectChanged(s => s.unspentPoints = 6, "criação de pontos");
        RejectChanged(s => s.unspentPoints = 4, "perda de pontos");
        RejectChanged(s => { s.attackPoints = int.MaxValue; s.defensePoints = int.MaxValue; }, "soma de pontos não estoura");
        RejectChanged(s => s.swordProficiencyLevel = 0, "proficiência zero");
        RejectChanged(s => s.swordProficiencyLevel = 2, "proficiência não obtida por nenhuma regra do jogo");
        RejectChanged(s => s.health = -1, "vida negativa");
        RejectChanged(s => s.health = 101, "vida acima do máximo não é corrigida silenciosamente");
        PlayerProgression maximum = new PlayerProgression();
        maximum.GrantExperience(int.MaxValue);
        CharacterSaveData capped = maximum.CaptureSaveData();
        capped.experience = 1;
        Reject(capped, "XP positivo no limite de nível");
    }

    private static void CheckInvalidOwnership()
    {
        RejectChanged(s => s.inventoryItemIds = null, "mochila ausente");
        RejectChanged(s => s.equipped = null, "equipamentos ausentes");
        RejectChanged(s => s.inventoryItemIds = new string[0], "perda de todos os itens da mochila");
        RejectChanged(s => s.equipped = new EquipmentSlotSave[0], "perda das peças equipadas");
        RejectChanged(s => s.inventoryItemIds[0] = "unknown-sword", "item desconhecido");
        RejectChanged(s => s.inventoryItemIds[0] = null, "item nulo");
        RejectChanged(s => s.inventoryItemIds[0] = " ", "item em branco");
        RejectChanged(s => s.inventoryItemIds[0] = "Steel-sword", "ID respeita letras maiúsculas e minúsculas");
        RejectChanged(s => s.inventoryItemIds[0] = s.inventoryItemIds[1], "duplicação na mochila");
        RejectChanged(s => s.inventoryItemIds[0] = s.equipped[0].itemId, "duplicação entre mochila e equipamento");
        RejectChanged(s => s.equipped[0].itemId = s.equipped[1].itemId, "duplicação entre equipamentos");
        RejectChanged(s => s.equipped = new EquipmentSlotSave[13], "quantidade de espaços acima do máximo");
        CharacterSaveData extra = EconomyCoreChecks.LegacyFixture().CaptureSaveData();
        Array.Resize(ref extra.inventoryItemIds, 24);
        extra.inventoryItemIds[23] = "steel-head";
        Reject(extra, "localização extra sem instância correspondente");
    }

    private static void CheckInvalidEquipment()
    {
        RejectChanged(s => s.equipped[0] = null, "registro de equipamento nulo");
        RejectChanged(s => s.equipped[0].slot = -1, "espaço negativo");
        RejectChanged(s => s.equipped[0].slot = 99, "espaço inexistente");
        RejectChanged(s => s.equipped[0].slot = s.equipped[1].slot, "espaço repetido");
        RejectChanged(s =>
        {
            string sword = s.equipped[0].itemId;
            s.equipped[0].itemId = s.equipped[1].itemId;
            s.equipped[1].itemId = sword;
        }, "espada e escudo em espaços incompatíveis");
        RejectChanged(s => SwapInventoryWithSlot(s, "steel-sword", EquipmentSlot.Sword), "requisito de ataque insuficiente");
        RejectChanged(s => SwapInventoryWithSlot(s, "arcane-head", EquipmentSlot.Head), "requisitos de nível e inteligência insuficientes");
        PlayerProgression bonusOnly = EconomyCoreChecks.LegacyFixture();
        Equip(bonusOnly, "steel-hands", EquipmentSlot.Hands);
        CharacterSaveData selfQualified = bonusOnly.CaptureSaveData();
        SwapInventoryWithSlot(selfQualified, "steel-sword", EquipmentSlot.Sword);
        Reject(selfQualified, "bônus de outro equipamento não satisfaz requisito de pontos distribuídos");
        PlayerProgression veteran = EconomyCoreChecks.LegacyFixture();
        veteran.GrantExperience(250);
        veteran.TryAllocate(HeroAttribute.Attack, 6);
        CharacterSaveData proficiency = veteran.CaptureSaveData();
        SwapInventoryWithSlot(proficiency, "veteran-sword", EquipmentSlot.Sword);
        Reject(proficiency, "espada de veterano não ignora proficiência");
    }

    private static void CheckLegacyItemsAreNotPersisted()
    {
        PlayerProgression legacy = new PlayerProgression();
        Require(legacy.TryEquipWeapon(new WeaponDefinition("test-only", "Arma de teste", new WeaponRequirements()), out _),
            "API histórica continua disponível para testes de regras");
        bool rejected = false;
        try { legacy.CaptureSaveData(); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "definição injetada por API histórica não entra no formato persistente");
    }

    private static void SwapInventoryWithSlot(CharacterSaveData save, string itemId, EquipmentSlot slot)
    {
        int inventoryIndex = Array.IndexOf(save.inventoryItemIds, itemId);
        Require(inventoryIndex >= 0, "fixture contém item na mochila");
        foreach (EquipmentSlotSave saved in save.equipped)
        {
            if (saved.slot != (int)slot) continue;
            save.inventoryItemIds[inventoryIndex] = saved.itemId;
            saved.itemId = itemId;
            return;
        }
        throw new BuildFailedException("Fixture sem espaço para troca.");
    }

    private static void SameProgression(PlayerProgression expected, PlayerProgression actual)
    {
        Equal(expected.Level, actual.Level, "nível preservado");
        Equal(expected.Experience, actual.Experience, "XP preservado");
        Equal(expected.Health, actual.Health, "vida preservada");
        Equal(expected.AttackPoints, actual.AttackPoints, "ataque distribuído preservado");
        Equal(expected.DefensePoints, actual.DefensePoints, "defesa distribuída preservada");
        Equal(expected.IntelligencePoints, actual.IntelligencePoints, "inteligência distribuída preservada");
        Equal(expected.UnspentPoints, actual.UnspentPoints, "pontos livres preservados");
        Equal(expected.SwordProficiencyLevel, actual.SwordProficiencyLevel, "proficiência preservada");
        Equal(expected.Equipment.Inventory.Count, actual.Equipment.Inventory.Count, "tamanho da mochila preservado");
        for (int i = 0; i < expected.Equipment.Inventory.Count; i++)
            Require(expected.Equipment.Inventory[i].Id == actual.Equipment.Inventory[i].Id, "ordem da mochila preservada");
        HashSet<string> owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (EquipmentItem item in actual.Equipment.Inventory) Require(owned.Add(item.Id), "mochila sem duplicações");
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            EquipmentItem left = expected.Equipment.GetEquipped(slot);
            EquipmentItem right = actual.Equipment.GetEquipped(slot);
            Require(left?.Id == right?.Id, "equipamento ou espaço vazio preservado");
            if (right != null) Require(owned.Add(right.Id), "equipamentos sem duplicações");
        }
        Equal(expected.Equipment.OwnedCount, owned.Count, "todas as instâncias são preservadas após carregar");
    }

    private static void RejectChanged(Action<CharacterSaveData> mutation, string description)
    {
        CharacterSaveData save = EconomyCoreChecks.LegacyFixture().CaptureSaveData();
        mutation(save);
        Reject(save, description);
    }

    private static void Reject(CharacterSaveData save, string description)
    {
        bool rejected = false;
        try { PlayerProgression.FromSaveData(save); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "recusa: " + description);
    }

    private static void Equip(PlayerProgression player, string id, EquipmentSlot slot) =>
        Require(player.Equipment.TryEquip(id, slot, out string reason), "equipa " + id + ": " + reason);
    private static void Equal(int expected, int actual, string description) =>
        Require(expected == actual, $"{description}: esperado {expected}, recebido {actual}");
    private static void Near(double expected, double actual, string description) =>
        Require(Math.Abs(expected - actual) < .000001d, $"{description}: esperado {expected}, recebido {actual}");
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new BuildFailedException("Falha na persistência da progressão: " + description);
    }
}
