using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Exercises the same mouse selection/action handlers used by OnGUI. Actual pointer
// appearance is inspected in the compiled application; physical pointer operation is a manual check.
[InitializeOnLoad]
public static class EquipmentPanelPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.EquipmentPanelChecks.";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static PrototypeCombat combat;
    private static PrototypeHud hud;
    private static PrototypeEquipmentAppearance appearance;
    private static int nextFrame;

    static EquipmentPanelPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar painel clássico de equipamentos")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie a validação fora do PlayMode.");
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 55f);
        SessionState.SetBool(Key + "Active", true);
        nextFrame = 0;
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
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antes da validação");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.frameCount < nextFrame) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
                    hud = UnityEngine.Object.FindFirstObjectByType<PrototypeHud>();
                    Require(combat != null && hud != null, "HUD e personagem existem");
                    // Historical scenarios explicitly restore the pre-economy inventory; new characters keep twelve items.
                    typeof(PrototypeCombat).GetField("stats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .SetValue(combat, EconomyCoreChecks.LegacyFixture());
                    combat.GetComponent<PrototypeCharacter>().enabled = false;
                    foreach (PrototypeMonster monster in combat.Monsters) monster.enabled = false;
                    appearance = combat.GetComponent<PrototypeEquipmentAppearance>();
                    CheckLayout(1440f, 900f);
                    CheckLayout(1024f, 768f);
                    CallStatic("ToggleEquipment");
                    Require(PrototypeHud.IsEquipmentOpen && PrototypeHud.BlocksGameplay, "janela de equipamentos bloqueia ações do mundo");
                    CheckPointerRegions();
                    Refresh();
                    Require(combat.Stats.Equipment.Inventory.Count == 23, "23 itens guardados na mochila legada de teste");
                    foreach (EquipmentItem item in combat.Stats.Equipment.Inventory.ToArray())
                    {
                        Call("SelectInventoryItem", item);
                        Require(SelectedId() == item.Id && item.FitsSlot(SelectedSlot()), "todo item da grade pode ser selecionado: " + item.Id);
                    }
                    Require(ItemCount() == 35, "comparar todos os tipos não modifica patrimônio");
                    EquipmentItem shield = combat.Stats.Equipment.Inventory.Single(item => item.Id == "steel-shield");
                    Call("SelectInventoryItem", shield);
                    Require(SelectedSlot() == EquipmentSlot.Shield && SelectedId() == shield.Id,
                        "seleção de item da mochila escolhe automaticamente seu espaço");
                    Require(combat.Stats.Equipment.GetEquipped(EquipmentSlot.Shield).Id == "training-shield", "seleção só compara e não equipa sozinha");
                    Call("EquipSelectedItem");
                    Require(combat.Stats.Equipment.GetEquipped(EquipmentSlot.Shield).Id == "steel-shield", "botão de equipar troca a peça selecionada");
                    Require(ItemCount() == 35, "troca pelo painel preserva patrimônio");
                    Advance(1); break;
                case 1:
                    Require(appearance.ShieldVisible, "troca pelo painel atualiza escudo no modelo");
                    Refresh();
                    EquipmentItem locked = combat.Stats.Equipment.Inventory.Single(item => item.Id == "veteran-sword");
                    Call("SelectInventoryItem", locked);
                    string priorSword = combat.Stats.Equipment.GetEquipped(EquipmentSlot.Sword).Id;
                    int revision = combat.Stats.Equipment.Revision;
                    Call("EquipSelectedItem");
                    Require(combat.Stats.Equipment.GetEquipped(EquipmentSlot.Sword).Id == priorSword && combat.Stats.Equipment.Revision == revision,
                        "ação recusada por requisitos não altera equipamento ou revisão");
                    Call("SelectEquipmentSlot", EquipmentSlot.RingRight);
                    EquipmentItem ring = combat.Stats.Equipment.Inventory.Single(item => item.Id == "steel-ring");
                    Call("SelectInventoryItem", ring);
                    Require(SelectedSlot() == EquipmentSlot.RingRight, "anel compatível preserva mão direita selecionada");
                    Call("EquipSelectedItem");
                    Require(combat.Stats.Equipment.GetEquipped(EquipmentSlot.RingRight).Id == "steel-ring" &&
                        combat.Stats.Equipment.GetEquipped(EquipmentSlot.RingLeft).Id == "training-ringleft", "anel não modifica o outro espaço");
                    Call("SelectEquipmentSlot", EquipmentSlot.Shield);
                    Call("UnequipSelectedItem");
                    Require(combat.Stats.Equipment.GetEquipped(EquipmentSlot.Shield) == null &&
                        combat.Stats.Equipment.Inventory.Any(item => item.Id == "steel-shield"), "remover pelo painel guarda escudo na mochila");
                    Refresh();
                    Require(combat.Stats.Equipment.Inventory.Count == 24 && Candidates().Any(item => item.Id == "steel-shield"),
                        "peça removida volta à mochila e pode ser selecionada para comparação");
                    Advance(2); break;
                case 2:
                    Require(!appearance.ShieldVisible && appearance.SwordVisible && !appearance.BaseHeadVisible,
                        "remoção esconde só o escudo selecionado preservando armadura e elmo");
                    Call("SelectInventoryItem", combat.Stats.Equipment.Inventory.Single(item => item.Id == "steel-shield"));
                    Call("EquipSelectedItem");
                    Require(ItemCount() == 35, "reequipar pelo painel não duplica itens");
                    CallStatic("ClosePanels");
                    Require(!PrototypeHud.IsEquipmentOpen && PrototypeHud.BlocksGameplay, "fechar continua bloqueando o clique do mesmo quadro");
                    Advance(3); break;
                case 3:
                    Require(!PrototypeHud.BlocksGameplay && appearance.ShieldVisible, "controle retorna no próximo quadro e escudo reequipado reaparece");
                    Finish(true, "Layout 1440×900 e 1024×768; doze espaços e grade para 35 itens sem sobreposição; mochila completa; seleção, comparação, requisitos, anéis, remoção/reequipar e bloqueio modal pelos handlers reais do mouse.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void CheckLayout(float width, float height)
    {
        Rect screen = new Rect(0f, 0f, width, height);
        Rect panel = (Rect)CallStatic("EquipmentRect", width, height);
        Rect comparison = (Rect)CallStatic("EquipmentComparisonRect", width, height);
        Require(Inside(screen, panel) && Inside(screen, comparison), "janelas cabem em " + width + "×" + height);
        Require(panel.x > width * .5f && panel.width < width * .4f && !panel.Overlaps(comparison), "mochila lateral e comparação separada deixam área do mundo livre");
        var slots = new List<Rect>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            Rect cell = (Rect)CallStatic("EquipmentSlotRect", panel, slot);
            Require(cell.width >= 34f && cell.height >= 34f && Inside(panel, cell), "espaço clicável dentro da janela: " + slot);
            Require(!slots.Any(previous => previous.Overlaps(cell)), "espaços não se sobrepõem: " + slot);
            slots.Add(cell);
        }
        Require(slots.Count == 12, "doze espaços de equipamento");
        Rect inventory = (Rect)CallStatic("EquipmentInventoryRect", panel);
        Require(Inside(panel, inventory) && !slots.Any(cell => cell.Overlaps(inventory)), "mochila fica abaixo dos equipamentos");
        var cells = new List<Rect>();
        for (int index = 0; index < 35; index++)
        {
            Rect cell = (Rect)CallStatic("EquipmentInventoryCellRect", inventory, index);
            Require(cell.width >= 36f && cell.height >= 36f && Inside(inventory, cell), "grade comporta item " + index);
            Require(!cells.Any(previous => previous.Overlaps(cell)), "células da mochila não se sobrepõem");
            cells.Add(cell);
        }
    }

    private static void CheckPointerRegions()
    {
        PropertyInfo scaleProperty = typeof(PrototypeHud).GetProperty("UiScale", Static);
        Require(scaleProperty != null, "escala visual disponível");
        float scale = (float)scaleProperty.GetValue(null);
        float width = Screen.width / scale, height = Screen.height / scale;
        foreach (string name in new[] { "EquipmentRect", "EquipmentComparisonRect" })
        {
            Rect rect = (Rect)CallStatic(name, width, height);
            Vector3 point = new Vector3(rect.center.x * scale, Screen.height - rect.center.y * scale);
            Require(PrototypeHud.ContainsScreenPoint(point), "clique no painel não atravessa para o chão: " + name);
        }
        Require(!PrototypeHud.ContainsScreenPoint(new Vector3(Screen.width * .5f, Screen.height * .5f)),
            "centro do mundo permanece visível entre as janelas laterais");
    }

    private static bool Inside(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .01f && inner.yMin >= outer.yMin - .01f &&
        inner.xMax <= outer.xMax + .01f && inner.yMax <= outer.yMax + .01f;
    private static int ItemCount()
    {
        CharacterSaveData save = combat.Stats.CaptureSaveData();
        string[] items = save.inventoryItemIds.Concat(save.equipped.Select(piece => piece.itemId)).ToArray();
        Require(items.Distinct().Count() == items.Length, "itens não duplicados");
        return items.Length;
    }
    private static void Refresh() => Call("RefreshEquipmentCandidates");
    private static List<EquipmentItem> Candidates() => (List<EquipmentItem>)typeof(PrototypeHud).GetField("equipmentCandidates", Instance).GetValue(hud);
    private static EquipmentSlot SelectedSlot() => (EquipmentSlot)typeof(PrototypeHud).GetField("selectedEquipmentSlot", Instance).GetValue(hud);
    private static string SelectedId() => (string)typeof(PrototypeHud).GetField("selectedEquipmentItemId", Instance).GetValue(hud);
    private static object Call(string name, params object[] arguments)
    {
        MethodInfo method = typeof(PrototypeHud).GetMethod(name, Instance);
        Require(method != null, "handler disponível: " + name);
        return method.Invoke(hud, arguments);
    }
    private static object CallStatic(string name, params object[] arguments)
    {
        MethodInfo method = typeof(PrototypeHud).GetMethod(name, Static);
        Require(method != null, "layout/ação disponível: " + name);
        return method.Invoke(null, arguments);
    }
    private static void Advance(int stage) { SessionState.SetInt(Key + "Stage", stage); nextFrame = Time.frameCount + 2; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (success) Debug.Log("[EQUIPMENT PANEL PLAY PASS] " + message); else Debug.LogError("[EQUIPMENT PANEL PLAY FAIL] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1); else EditorApplication.ExitPlaymode();
    }
}
