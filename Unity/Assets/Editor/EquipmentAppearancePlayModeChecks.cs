using System;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EquipmentAppearancePlayModeChecks
{
    private const string Key = "GuerraDasNacoes.EquipmentAppearanceChecks.";
    private static PrototypeCombat combat;
    private static PrototypeEquipmentAppearance appearance;
    private static PrototypeRemotePlayer remote;
    private static Material atlasMaterial;
    private static Color atlasTint;
    private static int nextFrame, originalRendererCount, originalMaterialCount;
    private static int removedIndex;
    private static SkinnedMeshRenderer[] originalPieces;
    private static readonly int TintProperty = Shader.PropertyToID("_Color");
    private static readonly EquipmentSlot[] VisibleSlots = {
        EquipmentSlot.Head, EquipmentSlot.Shoulders, EquipmentSlot.Chest,
        EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet,
        EquipmentSlot.Sword, EquipmentSlot.Shield,
        EquipmentSlot.Belt, EquipmentSlot.Necklace, EquipmentSlot.RingLeft, EquipmentSlot.RingRight
    };

    static EquipmentAppearancePlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar aparência dos equipamentos")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie a validação fora do PlayMode.");
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 75f);
        SessionState.SetBool(Key + "Active", true);
        nextFrame = removedIndex = 0;
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
            EquipmentState equipment;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
                    Require(combat != null, "personagem presente");
                    // Historical scenarios explicitly restore the pre-economy inventory; new characters keep twelve items.
                    typeof(PrototypeCombat).GetField("stats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .SetValue(combat, EconomyCoreChecks.LegacyFixture());
                    combat.GetComponent<PrototypeCharacter>().enabled = false;
                    appearance = combat.GetComponent<PrototypeEquipmentAppearance>();
                    Require(appearance != null && appearance.IsConfigured, "aparência configurada");
                    foreach (PrototypeMonster enemy in combat.Monsters) enemy.enabled = false;
                    Require(!appearance.BaseBodyVisible, "roupa base fica coberta pelo conjunto completo sem atravessar a armadura");
                    Require(!appearance.BaseHeadVisible, "elmo equipado cobre a cabeça base");
                    ValidateBaseCoverage();
                    Require(appearance.SwordVisible && appearance.ShieldVisible, "espada e escudo de treino presentes");
                    ValidateSkinnedBody(appearance.BaseBodyRenderer, "corpo");
                    ValidateSkinnedBody(appearance.BaseHeadRenderer, "cabeça");
                    originalPieces = PrototypeEquipmentAppearance.ArmourSlots.Select(appearance.GetArmourPiece).ToArray();
                    Require(originalPieces.All(piece => piece != null && piece.enabled && piece.sharedMesh != null && piece.sharedMesh.triangles.Length > 0),
                        "seis peças de armadura têm geometria separada e visível");
                    Require(PrototypeEquipmentVisual.AccessorySlots.All(slot => appearance.GetAccessory(slot) != null && appearance.IsAccessoryVisible(slot)),
                        "quatro acessórios têm geometria independente e visível");
                    Require(!appearance.ArmourRenderer.enabled, "mesh completo de referência não duplica armadura");
                    atlasMaterial = originalPieces[0].sharedMaterials[0];
                    atlasTint = atlasMaterial.GetColor(TintProperty);
                    foreach (EquipmentSlot slot in PrototypeEquipmentAppearance.ArmourSlots)
                        AssertTint(slot, Color.white, "acabamento inicial preservado");
                    originalRendererCount = combat.GetComponentsInChildren<Renderer>(true).Length;
                    originalMaterialCount = Resources.FindObjectsOfTypeAll<Material>().Length;
                    Require(combat.Stats.Equipment.TryUnequip(VisibleSlots[0], out _), "retirar primeira peça");
                    removedIndex = 1;
                    Advance(1); break;
                case 1:
                    for (int index = 0; index < VisibleSlots.Length; index++)
                        Require(IsVisible(VisibleSlots[index]) == (index >= removedIndex),
                            "retirar uma peça não altera as demais: " + VisibleSlots[index]);
                    Require(appearance.BaseHeadVisible, "retirar elmo revela rosto sem buraco");
                    ValidateBaseCoverage();
                    if (removedIndex < VisibleSlots.Length)
                    {
                        Require(combat.Stats.Equipment.TryUnequip(VisibleSlots[removedIndex], out _), "retirar " + VisibleSlots[removedIndex]);
                        removedIndex++;
                        Advance(1); break;
                    }
                    Require(CountItems(combat.Stats.CaptureSaveData()) == 35, "retirada preserva os 35 itens únicos");
                    CharacterSaveData naked = combat.Stats.CaptureSaveData();
                    combat.ApplyOnlineSnapshot(new OnlineSnapshot { character = naked,
                        self = new OnlinePlayerState { health = naked.health, maxHealth = combat.Stats.MaxHealth },
                        events = Array.Empty<OnlineWorldEvent>() });
                    Advance(2); break;
                case 2:
                    Require(VisibleSlots.All(slot => !IsVisible(slot)), "recarregar save sem peças não restaura armadura padrão");
                    Require(appearance.BaseBodyVisible && appearance.BaseHeadVisible, "save sem armadura mantém corpo e rosto");
                    equipment = combat.Stats.Equipment;
                    foreach (EquipmentSlot slot in VisibleSlots)
                        Require(equipment.TryEquip("training-" + slot.ToString().ToLowerInvariant(), slot, out _), "reequipar " + slot);
                    Advance(3); break;
                case 3:
                    Require(VisibleSlots.All(IsVisible) && !appearance.BaseBodyVisible && !appearance.BaseHeadVisible,
                        "reequipar restaura todas as peças sem roupa atravessar a armadura");
                    ValidateBaseCoverage();
                    Require(combat.Stats.Equipment.TryUnequip(EquipmentSlot.Chest, out _), "retirada parcial de peitoral");
                    appearance.RefreshAppearance();
                    Require(!appearance.IsArmourVisible(EquipmentSlot.Chest) && appearance.IsArmourVisible(EquipmentSlot.Shoulders) &&
                        appearance.IsBaseRegionVisible(EquipmentSlot.Chest) && appearance.IsBaseRegionVisible(EquipmentSlot.Shoulders),
                        "base mantém peito e axilas contínuos com ombreiras ainda equipadas");
                    ValidateBaseCoverage();
                    Require(combat.Stats.Equipment.TryEquip("training-chest", EquipmentSlot.Chest, out _), "restaurar peitoral de treino");
                    appearance.RefreshAppearance();
                    ValidateBaseCoverage();
                    Require(combat.Stats.TryAllocate(HeroAttribute.Attack, 2), "requisito da espada de aço");
                    equipment = combat.Stats.Equipment;
                    Require(equipment.TryEquip("steel-sword", EquipmentSlot.Sword, out _), "equipar espada de aço");
                    Require(equipment.TryEquip("steel-shield", EquipmentSlot.Shield, out _), "equipar escudo de aço");
                    Require(equipment.TryEquip("steel-head", EquipmentSlot.Head, out _), "equipar elmo de aço");
                    Advance(4); break;
                case 4:
                    equipment = combat.Stats.Equipment;
                    AssertTint(EquipmentSlot.Head, PrototypeEquipmentAppearance.GetFinishTint(equipment.GetEquipped(EquipmentSlot.Head)), "elmo recebe acabamento por peça");
                    foreach (EquipmentSlot slot in PrototypeEquipmentAppearance.ArmourSlots.Skip(1))
                        AssertTint(slot, Color.white, "elmo não modifica outras peças");
                    Require(atlasMaterial.GetColor(TintProperty) == atlasTint, "atlas compartilhado não foi modificado");
                    combat.Stats.GrantExperience(250);
                    Require(combat.Stats.TryAllocate(HeroAttribute.Intelligence, 4), "requisitos da armadura rúnica");
                    foreach (EquipmentSlot slot in PrototypeEquipmentAppearance.ArmourSlots)
                        Require(equipment.TryEquip("arcane-" + slot.ToString().ToLowerInvariant(), slot, out _), "troca rúnica: " + slot);
                    Advance(5); break;
                case 5:
                    equipment = combat.Stats.Equipment;
                    foreach (EquipmentSlot slot in PrototypeEquipmentAppearance.ArmourSlots)
                        AssertTint(slot, PrototypeEquipmentAppearance.GetFinishTint(equipment.GetEquipped(slot)), "acabamento rúnico: " + slot);
                    Require(appearance.AppliedRevision == equipment.Revision, "visual acompanha revisão automaticamente");
                    CheckRepeatedChanges();
                    remote = PrototypeRemotePlayer.Create(combat.GetComponent<PrototypeCharacter>(), "appearance-remote-check");
                    remote.ApplyState(new OnlinePlayerState { id = "appearance-remote-check", health = 100, maxHealth = 100,
                        equipped = Array.Empty<EquipmentSlotSave>() }, 0d);
                    Advance(6); break;
                case 6:
                    Require(remote.EquipmentVisual != null && !remote.HasGameplayComponents, "remoto contém apenas apresentação configurada");
                    Require(VisibleSlots.All(slot => !RemoteVisible(slot)),
                        "remoto vazio não herda armaduras, armas ou acessórios do herói clonado");
                    Require(remote.EquipmentVisual.BaseBodyVisible && remote.EquipmentVisual.BaseHeadVisible &&
                        !remote.EquipmentVisual.SwordVisible && !remote.EquipmentVisual.ShieldVisible, "remoto vazio mantém anatomia sem armas");
                    remote.ApplyState(new OnlinePlayerState { id = "appearance-remote-check", health = 100, maxHealth = 100,
                        equipped = null }, 0d);
                    Require(VisibleSlots.All(slot => !RemoteVisible(slot)),
                        "snapshot remoto nulo limpa peças em vez de presumir set de treino");
                    remote.ApplyState(new OnlinePlayerState { id = "appearance-remote-check", health = 100, maxHealth = 100,
                        equipped = combat.Stats.CaptureSaveData().equipped }, 0d);
                    Require(VisibleSlots.All(RemoteVisible) && !remote.EquipmentVisual.BaseBodyVisible && !remote.EquipmentVisual.BaseHeadVisible,
                        "snapshot remoto restaura todas as doze peças confirmadas sem clipping da base");
                    Require(combat.GetComponentsInChildren<Renderer>(true).Length == originalRendererCount, "trocas não criam renderers no herói");
                    Require(atlasMaterial.GetColor(TintProperty) == atlasTint, "trocas preservam material do FBX");
                    Debug.Log("[EQUIPMENT APPEARANCE PASS] Doze peças removidas individualmente (seis armaduras, armas e quatro acessórios); corpo e rosto preservados; save vazio/restauração; acabamentos; reutilização sem renderers/materiais adicionais; remoto vazio/nulo/reequipado.");
                    Finish(0); break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static bool IsVisible(EquipmentSlot slot) => slot == EquipmentSlot.Sword ? appearance.SwordVisible :
        slot == EquipmentSlot.Shield ? appearance.ShieldVisible : PrototypeEquipmentVisual.AccessorySlots.Contains(slot) ?
        appearance.IsAccessoryVisible(slot) : appearance.IsArmourVisible(slot);
    private static bool RemoteVisible(EquipmentSlot slot) => slot == EquipmentSlot.Sword ? remote.EquipmentVisual.SwordVisible :
        slot == EquipmentSlot.Shield ? remote.EquipmentVisual.ShieldVisible : PrototypeEquipmentVisual.AccessorySlots.Contains(slot) ?
        remote.EquipmentVisual.IsAccessoryVisible(slot) : remote.EquipmentVisual.IsArmourVisible(slot);

    private static void ValidateBaseCoverage()
    {
        foreach (EquipmentSlot slot in PrototypeEquipmentAppearance.ArmourSlots)
        {
            SkinnedMeshRenderer region = appearance.GetBaseRegion(slot);
            Require(region != null && region.sharedMesh != null && region.sharedMesh.triangles.Length > 0, "região base tem geometria: " + slot);
            if (combat.Stats.Equipment.GetEquipped(slot) == null)
                Require(appearance.IsBaseRegionVisible(slot), "retirar peça sempre revela a região base correspondente: " + slot);
            // Adjacent base regions may also be exposed to close seams around retained pieces.
            if (PrototypeEquipmentAppearance.ArmourSlots.All(armourSlot => combat.Stats.Equipment.GetEquipped(armourSlot) != null))
                Require(!appearance.IsBaseRegionVisible(slot), "conjunto completo cobre base para evitar clipping: " + slot);
        }
    }

    private static void ValidateSkinnedBody(SkinnedMeshRenderer renderer, string label)
    {
        Require(renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.vertexCount > 100, label + " tem geometria anatômica");
        Require(renderer.bones.Length > 10 && renderer.bones.All(bone => bone != null), label + " acompanha rig sem ossos ausentes");
        Require(renderer.sharedMesh.bindposes.Length == renderer.bones.Length, label + " tem bindposes compatíveis");
        Require(renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(material => material != null), label + " tem materiais válidos");
    }

    private static void CheckRepeatedChanges()
    {
        EquipmentState equipment = combat.Stats.Equipment;
        for (int iteration = 0; iteration < 5; iteration++)
        {
            foreach (EquipmentSlot slot in VisibleSlots) Require(equipment.TryUnequip(slot, out _), "retirada repetida: " + slot);
            appearance.RefreshAppearance();
            Require(VisibleSlots.All(slot => !IsVisible(slot)), "troca repetida retira todas as peças");
            ValidateBaseCoverage();
            foreach (EquipmentSlot slot in VisibleSlots)
                Require(equipment.TryEquip(RepeatedItemId(slot), slot, out _), "troca repetida: " + slot);
            appearance.RefreshAppearance();
            ValidateBaseCoverage();
        }
        Require(originalPieces.Select((piece, index) => ReferenceEquals(piece, appearance.GetArmourPiece(PrototypeEquipmentAppearance.ArmourSlots[index]))).All(value => value),
            "trocas reutilizam os mesmos renderers por peça");
        Require(Resources.FindObjectsOfTypeAll<Material>().Length == originalMaterialCount, "trocas não instanciam materiais");
        Require(CountItems(combat.Stats.CaptureSaveData()) == 35, "trocas preservam 35 itens únicos");
    }

    private static string RepeatedItemId(EquipmentSlot slot) => slot == EquipmentSlot.RingLeft ? "steel-ring" :
        slot == EquipmentSlot.RingRight ? "arcane-ring" :
        (slot == EquipmentSlot.Sword || slot == EquipmentSlot.Shield ? "steel-" : "arcane-") + slot.ToString().ToLowerInvariant();

    private static int CountItems(CharacterSaveData data)
    {
        string[] items = data.inventoryItemIds.Concat(data.equipped.Select(slot => slot.itemId)).ToArray();
        Require(items.Distinct().Count() == items.Length, "propriedade sem itens duplicados");
        return items.Length;
    }

    private static void AssertTint(EquipmentSlot slot, Color expected, string message)
    {
        var block = new MaterialPropertyBlock();
        appearance.GetArmourPiece(slot).GetPropertyBlock(block, 0);
        Require(Vector4.Distance(block.GetColor(TintProperty), expected) < .001f, message);
    }
    private static void Advance(int stage) { SessionState.SetInt(Key + "Stage", stage); nextFrame = Time.frameCount + 2; }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Aparência dos equipamentos: " + message); }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key + "Active", false);
        if (remote != null) UnityEngine.Object.Destroy(remote.gameObject);
        if (Application.isBatchMode) EditorApplication.Exit(code); else EditorApplication.ExitPlaymode();
    }
}
