using System;
using System.Linq;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EquipmentPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.EquipmentChecks.";
    private static PrototypeCombat combat;
    private static PrototypeCharacter character;
    private static PrototypeAbilities abilities;
    private static PrototypeEquipmentAppearance appearance;
    private static PrototypeMonster enemy;
    private static string trainingSword, trainingShield;
    private static int capturedDamage, materialCount;
    private static float capturedDuration, nextAt;

    static EquipmentPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar equipamentos em combate")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie fora do PlayMode.");
        nextAt = 0f;
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 45f);
        SessionState.SetBool(Key + "Active", true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "timeout");
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antes dos checks");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            EquipmentState gear = combat != null ? combat.Stats.Equipment : null;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0: Initialize(); break;
                case 1:
                    Require(enemy.CurrentHealth == enemy.MaxHealth - capturedDamage,
                        "troca após iniciar o golpe preserva o dano capturado da espada anterior");
                    Require(combat.Stats.GetBasicDamage() != capturedDamage, "próximo golpe usa equipamento atual");
                    Require(combat.Stats.EquippedWeapon == null, "remoção durante o golpe deixa espaço vazio sem cancelar impacto emitido");
                    character.ResetPosition();
                    PositionEnemy();
                    Equip("steel-sword", EquipmentSlot.Sword);
                    Equip("steel-necklace", EquipmentSlot.Necklace);
                    capturedDamage = combat.Stats.GetSkillDamage();
                    Require(capturedDamage > 32 && abilities.TryUse(1), "bônus da espada e colar entram no pesado");
                    Advance(2, .25f); break;
                case 2:
                    Require(enemy.CurrentHealth == enemy.MaxHealth - capturedDamage, "pesado aplica dano efetivo real no troll");
                    character.ResetPosition();
                    Require(abilities.TryUse(4), "Fúria com inteligência do colar");
                    capturedDuration = abilities.DamageBoostDurationSeconds;
                    Require(capturedDuration > 8f, "colar amplia duração ao ativar");
                    Require(gear.TryUnequip(EquipmentSlot.Necklace, out _), "remoção do colar");
                    Require(Mathf.Abs(capturedDuration - abilities.DamageBoostDurationSeconds) < .001f,
                        "remover inteligência não muda duração já capturada");
                    Advance(3, .25f); break;
                case 3:
                    Require(abilities.StatusEffects.DamageBoostVisible, "aura acompanha bônus mesmo com troca de equipamento");
                    Equip("steel-shield", EquipmentSlot.Shield);
                    Require(abilities.TryUse(2), "Guarda continua disponível com escudo trocado");
                    int expected = CombatScaling.RoundDamage(40d * combat.Stats.DamageTakenMultiplier * .5d);
                    Require(combat.ReceiveDamage(40) == expected, "defesa do escudo combina com Guarda antes do arredondamento");
                    Equip("steel-belt", EquipmentSlot.Belt);
                    int healthBefore = combat.Stats.Health;
                    Require(healthBefore < combat.Stats.MaxHealth, "vida extra não cura automaticamente");
                    character.ResetPosition();
                    Require(gear.GetEquipped(EquipmentSlot.Sword).Id == "steel-sword" &&
                        gear.GetEquipped(EquipmentSlot.Belt).Id == "steel-belt" &&
                        combat.Stats.Health == combat.Stats.MaxHealth,
                        "reinício preserva equipamentos e cura ao máximo equipado");
                    int maxWithBelt = combat.Stats.MaxHealth;
                    Require(gear.TryUnequip(EquipmentSlot.Belt, out _), "cinturão pode sair após cura");
                    Require(combat.Stats.MaxHealth == maxWithBelt - 20 && combat.Stats.Health == combat.Stats.MaxHealth,
                        "remoção limita HP ao novo máximo sem overflow");
                    combat.ReceiveDamage(int.MaxValue);
                    Equip("steel-belt", EquipmentSlot.Belt);
                    Require(!combat.IsAlive && combat.Stats.Health == 0 && !combat.TryAttack(),
                        "troca com vida extra durante derrota não ressuscita");
                    character.ResetPosition();
                    CheckAppearanceReuse();
                    Advance(4, .1f); break;
                case 4:
                    Require(materialCount == Resources.FindObjectsOfTypeAll<Material>().Length,
                        "trocas repetidas não criam materiais por equipamento");
                    Require(appearance.SwordVisible && appearance.ShieldVisible,
                        "espada e escudo reaparecem ao reequipar");
                    InvokePanel("ToggleEquipment");
                    Advance(5, .25f); break;
                case 5:
                    Require(PrototypeHud.IsEquipmentOpen && PrototypeHud.BlocksGameplay, "equipamentos permanecem abertos entre frames");
                    InvokePanel("ToggleAttributes");
                    Advance(6, .25f); break;
                case 6:
                    Require(PrototypeHud.IsAttributesOpen && !PrototypeHud.IsEquipmentOpen, "atributos substituem equipamentos entre frames");
                    InvokePanel("ClosePanels");
                    Require(PrototypeHud.BlocksGameplay, "frame do fechamento continua bloqueado");
                    Advance(7, .1f); break;
                case 7:
                    Require(!PrototypeHud.BlocksGameplay, "controle do mundo volta no frame seguinte ao fechamento");
                    Finish(true, "Inventário, bloqueio sem espada, dano capturado, pesado com bônus, inteligência/duração, Guarda/defesa, vida máxima, derrota/reinício e aparência sem materiais extras.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Initialize()
    {
        combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
        Require(combat != null, "personagem presente");
        // Historical scenarios explicitly restore the pre-economy inventory; new characters keep twelve items.
        typeof(PrototypeCombat).GetField("stats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(combat, EconomyCoreChecks.LegacyFixture());
        character = combat.GetComponent<PrototypeCharacter>();
        abilities = combat.GetComponent<PrototypeAbilities>();
        appearance = combat.GetComponent<PrototypeEquipmentAppearance>();
        Require(appearance != null && appearance.IsConfigured, "aparência de equipamentos configurada");
        CheckInterfaceModals();
        character.ResetPosition();
        character.enabled = false;
        foreach (PrototypeMonster monster in combat.Monsters) monster.enabled = false;
        enemy = combat.Monster;
        foreach (PrototypeMonster monster in combat.Monsters) if (monster != enemy) monster.gameObject.SetActive(false);
        PositionEnemy();
        EquipmentState gear = combat.Stats.Equipment;
        trainingSword = gear.GetEquipped(EquipmentSlot.Sword).Id;
        trainingShield = gear.GetEquipped(EquipmentSlot.Shield).Id;
        Require(gear.TryUnequip(EquipmentSlot.Sword, out _) && gear.TryUnequip(EquipmentSlot.Shield, out _), "arma e escudo podem sair");
        appearance.RefreshAppearance();
        Require(!appearance.SwordVisible && !appearance.ShieldVisible, "remoção esconde arma e escudo no modelo");
        float manaBefore = abilities.Mana;
        Require(!combat.CanAttack && !combat.TryAttack() && !abilities.CanUse(0) && !abilities.TryUse(1),
            "básico e pesado exigem espada equipada");
        Require(abilities.Mana == manaBefore && abilities.GetCooldownRemaining(1) == 0f && !combat.HasPendingStrike,
            "ataques recusados preservam mana, recarga e impacto");
        Require(abilities.TryUse(2), "Guarda é magia e funciona sem escudo equipado");
        abilities.ResetEncounter();
        Equip(trainingSword, EquipmentSlot.Sword);
        Equip(trainingShield, EquipmentSlot.Shield);
        Require(combat.Stats.TryAllocate(HeroAttribute.Attack, 2), "requisito da espada de aço investido");
        Equip("steel-sword", EquipmentSlot.Sword);
        appearance.RefreshAppearance();
        Require(appearance.SwordVisible && appearance.ShieldVisible, "troca mantém equipamento visível");
        capturedDamage = combat.Stats.GetBasicDamage();
        Require(capturedDamage == 24 && combat.TryAttack(), "espada de aço causa24 com ataque2 investido mais2 do item");
        Require(gear.TryUnequip(EquipmentSlot.Sword, out _), "espada pode ser removida durante golpe já emitido");
        Advance(1, .25f);
    }

    private static void CheckAppearanceReuse()
    {
        appearance.RefreshAppearance();
        materialCount = Resources.FindObjectsOfTypeAll<Material>().Length;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            Equip(trainingSword, EquipmentSlot.Sword);
            Equip(trainingShield, EquipmentSlot.Shield);
            appearance.RefreshAppearance();
            Equip("steel-sword", EquipmentSlot.Sword);
            Equip("steel-shield", EquipmentSlot.Shield);
            appearance.RefreshAppearance();
        }
        // Both variants must remain owned exactly once despite repeated replacements.
        Require(combat.Stats.Equipment.Inventory.Count(item => item.Id == trainingSword) == 1 &&
            combat.Stats.Equipment.Inventory.All(item => item.Id != "steel-sword"), "trocas visuais não duplicam inventário");
    }

    private static void Equip(string id, EquipmentSlot slot)
    {
        Require(combat.Stats.Equipment.TryEquip(id, slot, out string reason), "equipar " + id + ": " + reason);
    }
    private static void PositionEnemy()
    {
        CharacterController collider = enemy.GetComponent<CharacterController>();
        collider.enabled = false;
        enemy.transform.position = new Vector3(0f, .06f, 1.9f);
        collider.enabled = true;
        Physics.SyncTransforms();
    }
    private static void CheckInterfaceModals()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo equipment = typeof(PrototypeHud).GetMethod("ToggleEquipment", flags);
        MethodInfo attributes = typeof(PrototypeHud).GetMethod("ToggleAttributes", flags);
        MethodInfo help = typeof(PrototypeHud).GetMethod("ToggleHelp", flags);
        MethodInfo close = typeof(PrototypeHud).GetMethod("ClosePanels", flags);
        Require(equipment != null && attributes != null && help != null && close != null, "fluxos modais da interface disponíveis");
        equipment.Invoke(null, null);
        PropertyInfo scaleProperty = typeof(PrototypeHud).GetProperty("UiScale", flags);
        MethodInfo panelRect = typeof(PrototypeHud).GetMethod("EquipmentRect", flags);
        Require(scaleProperty != null && panelRect != null, "layout e escala do painel disponíveis");
        float scale = (float)scaleProperty.GetValue(null);
        Rect panel = (Rect)panelRect.Invoke(null, new object[] { Screen.width / scale, Screen.height / scale });
        Vector3 panelPoint = new Vector3(panel.center.x * scale, Screen.height - panel.center.y * scale);
        Require(PrototypeHud.IsEquipmentOpen && PrototypeHud.BlocksGameplay &&
            PrototypeHud.ContainsScreenPoint(panelPoint),
            "painel de equipamentos bloqueia comandos e clique no mundo");
        attributes.Invoke(null, null);
        Require(!PrototypeHud.IsEquipmentOpen && PrototypeHud.IsAttributesOpen && PrototypeHud.BlocksGameplay,
            "C substitui painel de equipamentos por atributos");
        help.Invoke(null, null);
        Require(!PrototypeHud.IsEquipmentOpen && !PrototypeHud.IsAttributesOpen && PrototypeHud.BlocksGameplay,
            "ajuda substitui os outros painéis");
        close.Invoke(null, null);
        Require(!PrototypeHud.IsEquipmentOpen && !PrototypeHud.IsAttributesOpen && PrototypeHud.BlocksGameplay,
            "fechar bloqueia o frame para não vazar clique para o mundo");
    }
    private static void InvokePanel(string name)
    {
        MethodInfo method = typeof(PrototypeHud).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Require(method != null, "painel existe: " + name);
        method.Invoke(null, null);
    }
    private static void Advance(int stage, float seconds) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + seconds; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Finish(bool passed, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        Time.timeScale = 1f;
        if (passed) Debug.Log("[EQUIPAMENTOS PLAY PASS] " + message);
        else Debug.LogError("[EQUIPAMENTOS PLAY FAIL] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }
}
