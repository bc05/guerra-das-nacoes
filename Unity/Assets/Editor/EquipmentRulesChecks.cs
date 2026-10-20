using System;
using System.Collections.Generic;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class EquipmentRulesChecks
{
    [MenuItem("Guerra das Nações/Validar regras de equipamentos")]
    public static void Run()
    {
        CheckStartingInventory();
        CheckReplacementAndRemoval();
        CheckAtomicRejections();
        CheckIndependentRings();
        CheckAllocatedRequirements();
        CheckEffectiveScaling();
        CheckHealthNeverRefillsOnSwap();
        CheckSnapshotValues();
        CheckReadOnlyPreviews();
        CheckWeaponCompatibility();
        CheckNumericLimits();
        Debug.Log("[EQUIPAMENTO TEST PASS] Inventário, 12 espaços, trocas, requisitos, bônus, vida, anéis e limites numéricos validados.");
    }

    private static void CheckStartingInventory()
    {
        PlayerProgression player = new PlayerProgression();
        Equal(0, player.Equipment.Inventory.Count, "mochila inicial aguarda drops e compras");
        HashSet<string> ids = new HashSet<string>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            EquipmentItem item = player.Equipment.GetEquipped(slot);
            Require(item != null && item.FitsSlot(slot), "conjunto completo de treino ocupa todos os espaços");
            Require(ids.Add(item.Id), "peças equipadas têm identificadores únicos");
            Require(EquipmentCatalog.GetSlotName(slot).Length > 0, "espaço possui nome legível");
        }
        foreach (EquipmentItem item in player.Equipment.Inventory)
            Require(ids.Add(item.Id), "mochila e peças equipadas não duplicam itens");
        Equal(12, ids.Count, "personagem novo recebe somente o conjunto de treinamento");
        Equal(0, player.Equipment.Revision, "estado inicial não representa uma troca");
        Equal(18, player.GetBasicDamage(), "treino mantém dano original");
        Equal(100, player.MaxHealth, "treino mantém vida original");
        Equal(5, player.UnspentPoints, "itens não consomem pontos livres");
        Require(player.Equipment.Inventory is IList<EquipmentItem> list && list.IsReadOnly,
            "mochila pública é somente leitura");
    }

    private static void CheckReplacementAndRemoval()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        EquipmentItem original = player.Equipment.GetEquipped(EquipmentSlot.Head);
        Require(player.Equipment.TryEquip("steel-head", EquipmentSlot.Head, out _), "elmo da mochila pode substituir treino");
        Equal(23, player.Equipment.Inventory.Count, "troca mantém quantidade na mochila");
        Require(Find(player, original.Id) == original && Find(player, "steel-head") == null,
            "troca devolve peça anterior e retira candidata da mochila");
        Equal(1, player.EquipmentBonuses.Defense, "elmo fornece defesa");
        Equal(108, player.MaxHealth, "elmo fornece vida máxima");
        Equal(100, player.Health, "equipar não cura");
        Equal(1, player.Equipment.Revision, "troca sinaliza revisão");
        Require(player.Equipment.TryUnequip(EquipmentSlot.Head, out _), "peça pode ser removida");
        Require(player.Equipment.GetEquipped(EquipmentSlot.Head) == null, "espaço removido fica vazio");
        Equal(24, player.Equipment.Inventory.Count, "remover retorna peça à mochila");
        Equal(0, player.EquipmentBonuses.Defense, "remover elimina bônus");
        Require(player.Equipment.TryEquip(original.Id, EquipmentSlot.Head, out _), "peça de treino retornada pode ser equipada");
        Equal(23, player.Equipment.Inventory.Count, "reequipar conserva patrimônio");
    }

    private static void CheckAtomicRejections()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        EquipmentItem sword = player.Equipment.GetEquipped(EquipmentSlot.Sword);
        string[] rejectedIds = { "missing", null, "steel-sword", "steel-head", sword.Id };
        foreach (string id in rejectedIds)
        {
            Require(!player.Equipment.TryEquip(id, EquipmentSlot.Sword, out string reason) && reason.Length > 0,
                "item ausente, requisito insuficiente, espaço incompatível ou item já equipado é recusado");
            Equal(23, player.Equipment.Inventory.Count, "recusa não altera mochila");
            Equal(0, player.Equipment.Revision, "recusa não altera revisão");
            Require(player.Equipment.GetEquipped(EquipmentSlot.Sword) == sword, "recusa conserva equipamento anterior");
        }
        Require(!player.Equipment.TryEquip("steel-head", (EquipmentSlot)99, out _), "espaço inválido é recusado");
        Require(!player.Equipment.TryUnequip((EquipmentSlot)99, out _), "remoção de espaço inválido é recusada");
        Require(player.Equipment.TryUnequip(EquipmentSlot.Head, out _), "remoção válida");
        int revision = player.Equipment.Revision;
        Require(!player.Equipment.TryUnequip(EquipmentSlot.Head, out _), "remoção repetida é recusada");
        Equal(revision, player.Equipment.Revision, "remoção recusada não altera revisão");
    }

    private static void CheckIndependentRings()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        Require(player.Equipment.TryEquip("steel-ring", EquipmentSlot.RingLeft, out _), "anel de força ocupa mão esquerda");
        Require(player.Equipment.TryEquip("arcane-ring", EquipmentSlot.RingRight, out _), "segundo anel ocupa mão direita");
        Equal(2, player.EquipmentBonuses.Attack, "primeiro anel conta uma vez");
        Equal(2, player.EquipmentBonuses.Intelligence, "segundo anel tem bônus independente");
        int revision = player.Equipment.Revision;
        Require(!player.Equipment.TryEquip("steel-ring", EquipmentSlot.RingRight, out _), "mesma instância não ocupa os dois dedos");
        Equal(revision, player.Equipment.Revision, "duplicação recusada não muda revisão");
        Require(player.Equipment.TryUnequip(EquipmentSlot.RingLeft, out _), "anel pode ser devolvido à mochila");
        Require(player.Equipment.TryEquip("steel-ring", EquipmentSlot.RingRight, out _), "anel devolvido pode ocupar outro dedo");
        Equal(2, player.EquipmentBonuses.Attack, "anel movido não duplica bônus");
        Equal(0, player.EquipmentBonuses.Intelligence, "anel substituído perde bônus");
        Require(Find(player, "arcane-ring") != null, "anel substituído retorna à mochila");
    }

    private static void CheckAllocatedRequirements()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        Require(player.Equipment.TryEquip("steel-hands", EquipmentSlot.Hands, out _), "luvas fornecem ataque efetivo");
        Equal(2, player.EffectiveAttackPoints, "bônus aumenta ataque efetivo");
        Equal(0, player.AttackPoints, "bônus não altera pontos distribuídos");
        Require(!player.Equipment.TryEquip("steel-sword", EquipmentSlot.Sword, out string reason) && reason.Contains("Ataque 2"),
            "bônus de luvas não satisfaz requisito da espada");
        Require(player.TryAllocate(HeroAttribute.Attack, 2), "alocação atende requisito real");
        Require(player.Equipment.TryEquip("steel-sword", EquipmentSlot.Sword, out _), "espada equipa com pontos alocados");
        Equal(6, player.EffectiveAttackPoints, "alocação, luvas e espada somam uma vez");
        player.GrantExperience(250);
        Require(player.Equipment.TryEquip("arcane-ring", EquipmentSlot.RingLeft, out _), "anel mágico equipa");
        Require(player.Equipment.TryEquip("steel-necklace", EquipmentSlot.Necklace, out _), "colar mágico equipa");
        Equal(4, player.EffectiveIntelligencePoints, "anel e colar fornecem inteligência efetiva");
        Require(!player.Equipment.TryEquip("arcane-head", EquipmentSlot.Head, out reason) && reason.Contains("Inteligência 4"),
            "nível atendido não permite inteligência de equipamento autoqualificar");
        Require(player.TryAllocate(HeroAttribute.Intelligence, 4), "inteligência distribuída atende requisito");
        Require(player.Equipment.TryEquip("arcane-head", EquipmentSlot.Head, out _), "elmo rúnico após requisitos");
        Require(!player.Equipment.TryEquip("veteran-sword", EquipmentSlot.Sword, out reason) &&
            reason.Contains("Proficiência em espada 2"), "proficiência permanece requisito separado");
    }

    private static void CheckEffectiveScaling()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        player.TryAllocate(HeroAttribute.Attack, 2);
        Require(player.Equipment.TryEquip("steel-sword", EquipmentSlot.Sword, out _), "espada de aço para cálculo");
        Require(player.Equipment.TryEquip("arcane-ring", EquipmentSlot.RingLeft, out _), "anel para cálculo mágico");
        Require(player.Equipment.TryEquip("steel-shield", EquipmentSlot.Shield, out _), "escudo para cálculo defensivo");
        Equal(18, player.BaseAttackDamage, "base do nível permanece intacta");
        Equal(22, player.EquippedBaseDamage, "dano aditivo é aplicado na base");
        Equal(4, player.EffectiveAttackPoints, "ataque efetivo soma alocado e equipamento");
        Near(1.08d, player.PhysicalAttackMultiplier, "ataque de equipamento escala golpe básico");
        Near(1.04d, player.SkillAttackMultiplier, "habilidade usa multiplicador próprio de ataque");
        Near(1.03d, player.MagicDamageMultiplier, "inteligência de equipamento escala dano mágico");
        Near(1.01d, player.MagicDurationMultiplier, "inteligência de equipamento escala duração");
        Equal(24, player.GetBasicDamage(), "golpe equipado arredonda apenas ao final");
        Equal(42, player.GetSkillDamage(), "habilidade equipada combina base, ataque e inteligência");
        Equal(53, player.GetSkillDamage(1.8d, true, 1.25d), "fúria combina equipamento sem arredondamento intermediário");
        Near(100d / 106d, player.DamageTakenMultiplier, "escudo fornece defesa efetiva");
        Equal(3, player.ReceiveDamage(7, .5d), "guarda e equipamento defensivo arredondam juntos");
        Equal(3, player.UnspentPoints, "trocas não alteram pontos disponíveis");
    }

    private static void CheckHealthNeverRefillsOnSwap()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        player.ReceiveDamage(40);
        Require(player.Equipment.TryEquip("steel-belt", EquipmentSlot.Belt, out _), "cinturão aumenta máximo");
        Equal(120, player.MaxHealth, "vida máxima aumentada pelo cinturão");
        Equal(60, player.Health, "equipar bônus com herói ferido não cura");
        player.Heal(55);
        Equal(115, player.Health, "cura usa máximo equipado");
        Require(player.Equipment.TryUnequip(EquipmentSlot.Belt, out _), "remover bônus de vida");
        Equal(100, player.Health, "remoção limita vida ao novo máximo");
        Require(player.Equipment.TryEquip("steel-belt", EquipmentSlot.Belt, out _), "reequipar cinturão");
        Equal(100, player.Health, "reequipar não repõe vida removida");
        player.ReceiveDamage(int.MaxValue);
        Require(player.Equipment.TryEquip("steel-chest", EquipmentSlot.Chest, out _), "morto pode organizar equipamento");
        Equal(0, player.Health, "equipar não ressuscita");
        Require(player.Equipment.TryUnequip(EquipmentSlot.Chest, out _), "morto pode remover equipamento");
        Equal(0, player.Health, "remover não ressuscita");
    }

    private static void CheckSnapshotValues()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        int castDamage = player.GetSkillDamage();
        double castDuration = player.GetMagicDuration(8d);
        EquipmentBonuses snapshot = player.EquipmentBonuses;
        player.Equipment.TryEquip("steel-hands", EquipmentSlot.Hands, out _);
        player.Equipment.TryEquip("arcane-ring", EquipmentSlot.RingLeft, out _);
        Equal(32, castDamage, "dano já capturado não muda com troca posterior");
        Near(8d, castDuration, "duração já capturada não muda com troca posterior");
        Equal(0, snapshot.Attack, "totais antigos são valores imutáveis");
        Require(player.GetSkillDamage() > castDamage && player.GetMagicDuration(8d) > castDuration,
            "próxima ativação usa novos atributos efetivos");
    }

    private static void CheckWeaponCompatibility()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        WeaponDefinition sword = new WeaponDefinition("compatibility-sword", "Espada de compatibilidade",
            new WeaponRequirements(), new EquipmentBonuses(damage: 3));
        Require(player.TryEquipWeapon(sword, out _), "API legada registra e equipa definição validada");
        Require(player.EquippedWeapon == sword && player.Equipment.GetEquipped(EquipmentSlot.Sword).Weapon == sword,
            "arma legada e espaço de equipamento compartilham mesmo estado");
        Equal(21, player.EquippedBaseDamage, "bônus da definição legada participa dos cálculos");
        WeaponDefinition collision = new WeaponDefinition(sword.Id, "Definição divergente", new WeaponRequirements(),
            new EquipmentBonuses(damage: 999));
        int revision = player.Equipment.Revision;
        Require(!player.TryEquipWeapon(collision, out _), "mesmo identificador não injeta nova definição");
        Equal(revision, player.Equipment.Revision, "colisão não altera estado");
        Require(player.Equipment.TryUnequip(EquipmentSlot.Sword, out _), "espada pode ser removida");
        Require(player.EquippedWeapon == null, "API legada reflete espaço vazio");
        Require(player.TryEquipWeapon(sword, out _), "arma legada devolvida pode ser reequipada");
        Require(!player.TryEquipWeapon(null, out _), "definição nula não altera equipamento");
    }

    private static void CheckReadOnlyPreviews()
    {
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        EquipmentItem sword = Find(player, "steel-sword");
        EquipmentBonuses projected = player.Equipment.PreviewBonuses(EquipmentSlot.Sword, sword);
        Equal(2, projected.Attack, "prévia mostra bônus mesmo antes de atender requisitos");
        Equal(4, projected.Damage, "prévia inclui dano candidato");
        Equal(0, player.EquipmentBonuses.Damage, "prévia não altera os bônus atuais");
        Equal(23, player.Equipment.Inventory.Count, "prévia não move itens");
        Equal(0, player.Equipment.Revision, "prévia não cria revisão");
        player.TryAllocate(HeroAttribute.Attack, 2);
        Require(player.Equipment.TryEquip(sword.Id, EquipmentSlot.Sword, out _), "troca confirma prévia");
        Equal(projected.Attack, player.EquipmentBonuses.Attack, "bônus confirmado coincide com prévia");
        Equal(projected.Damage, player.EquipmentBonuses.Damage, "dano confirmado coincide com prévia");
        EquipmentBonuses removed = player.Equipment.PreviewBonuses(EquipmentSlot.Sword, null);
        Equal(0, removed.Damage, "prévia de remoção elimina peça substituída");
        Equal(4, player.EquipmentBonuses.Damage, "prévia de remoção não remove peça real");
    }

    private static void CheckNumericLimits()
    {
        EquipmentBonuses maximum = new EquipmentBonuses(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        EquipmentBonuses sum = maximum.Add(new EquipmentBonuses(1, 1, 1, 1, 1));
        Equal(int.MaxValue, sum.Attack, "soma de ataque satura");
        Equal(int.MaxValue, sum.Defense, "soma de defesa satura");
        Equal(int.MaxValue, sum.Intelligence, "soma de inteligência satura");
        Equal(int.MaxValue, sum.Damage, "soma de dano satura");
        Equal(int.MaxValue, sum.Health, "soma de vida satura");
        PlayerProgression player = EconomyCoreChecks.LegacyFixture();
        player.TryAllocate(HeroAttribute.Attack);
        Require(player.TryEquipWeapon(new WeaponDefinition("limit-sword", "Espada limite", new WeaponRequirements(), maximum), out _),
            "fixture numérica equipa sem overflow");
        Equal(int.MaxValue, player.EffectiveAttackPoints, "ataque alocado mais bônus satura");
        Equal(int.MaxValue, player.EquippedBaseDamage, "dano do nível mais bônus satura");
        Equal(int.MaxValue, player.MaxHealth, "vida do nível mais bônus satura");
        Equal(100, player.Health, "bônus extremo não cura");
        Equal(int.MaxValue, player.GetBasicDamage(), "dano multiplicado satura");
        EquipmentItem training = Find(player, "training-sword");
        EquipmentBonuses preview = player.Equipment.PreviewBonuses(EquipmentSlot.Sword, training);
        Equal(18, EquipmentBonuses.SaturatingAdd(player.BaseAttackDamage, preview.Damage),
            "prévia após remover bônus saturado recupera a base real do dano");
        Equal(100, EquipmentBonuses.SaturatingAdd(player.BaseMaxHealth, preview.Health),
            "prévia após remover bônus saturado recupera a base real da vida");
        Equal(1, EquipmentBonuses.SaturatingAdd(player.AttackPoints, preview.Attack),
            "prévia após remover bônus saturado preserva pontos alocados");
        Require(player.GetMagicDuration(8d) > 0d && !double.IsInfinity(player.GetMagicDuration(8d)), "duração permanece finita");
        bool rejectedNegative = false;
        try { new EquipmentBonuses(health: -1); }
        catch (ArgumentOutOfRangeException) { rejectedNegative = true; }
        Require(rejectedNegative, "definições negativas são recusadas");
    }

    private static EquipmentItem Find(PlayerProgression player, string id)
    {
        foreach (EquipmentItem item in player.Equipment.Inventory) if (item.Id == id) return item;
        return null;
    }

    private static void Equal(int expected, int actual, string description) => Require(expected == actual,
        $"{description}: esperado {expected}, recebido {actual}");
    private static void Near(double expected, double actual, string description) => Require(Math.Abs(expected - actual) < .000001d,
        $"{description}: esperado {expected}, recebido {actual}");
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new BuildFailedException("Falha nas regras de equipamento: " + description);
    }
}
