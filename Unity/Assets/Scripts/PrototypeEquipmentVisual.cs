using System.Collections.Generic;
using UnityEngine;

namespace GuerraDasNacoes
{
    /// <summary>Pure equipment presentation shared by the local hero and remote visual clones.</summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeEquipmentVisual : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer armourReference;
        [SerializeField] private SkinnedMeshRenderer[] armourPieces;
        [SerializeField] private SkinnedMeshRenderer baseBody;
        [SerializeField] private SkinnedMeshRenderer baseHead;
        [SerializeField] private SkinnedMeshRenderer[] baseRegions;
        [SerializeField] private SkinnedMeshRenderer sword;
        [SerializeField] private SkinnedMeshRenderer shield;
        [SerializeField] private SkinnedMeshRenderer[] accessories;
        private MaterialPropertyBlock properties;
        private static readonly int TintProperty = Shader.PropertyToID("_Color");
        public static readonly EquipmentSlot[] AccessorySlots =
        {
            EquipmentSlot.Belt, EquipmentSlot.Necklace, EquipmentSlot.RingLeft, EquipmentSlot.RingRight
        };

        public bool IsConfigured => armourReference != null && armourPieces != null &&
            armourPieces.Length == PrototypeEquipmentAppearance.ArmourSlots.Length && baseBody != null &&
            baseHead != null && sword != null && shield != null;
        public bool SwordVisible => sword != null && sword.enabled;
        public bool ShieldVisible => shield != null && shield.enabled;
        public bool BaseBodyVisible
        {
            get
            {
                if (baseRegions != null)
                    foreach (SkinnedMeshRenderer region in baseRegions)
                        if (region != null && region.enabled) return true;
                return false;
            }
        }
        public bool BaseHeadVisible => baseHead != null && baseHead.enabled;
        public SkinnedMeshRenderer ArmourRenderer => armourReference;
        public SkinnedMeshRenderer BaseBodyRenderer => baseBody;
        public SkinnedMeshRenderer BaseHeadRenderer => baseHead;
        public SkinnedMeshRenderer SwordRenderer => sword;
        public SkinnedMeshRenderer ShieldRenderer => shield;

        public void Configure(SkinnedMeshRenderer reference, SkinnedMeshRenderer[] pieces,
            SkinnedMeshRenderer body, SkinnedMeshRenderer head, SkinnedMeshRenderer blade,
            SkinnedMeshRenderer guard, SkinnedMeshRenderer[] accessoryRenderers)
        {
            armourReference = reference; armourPieces = pieces; baseBody = body; baseHead = head;
            sword = blade; shield = guard; accessories = accessoryRenderers;
            // The original partition stays available for validation, never draws over the six pieces.
            armourReference.enabled = false;
        }

        public void ConfigureBaseRegions(SkinnedMeshRenderer[] regions) { baseRegions = regions; }

        public SkinnedMeshRenderer GetBaseRegion(EquipmentSlot slot)
        {
            if (slot == EquipmentSlot.Head) return baseHead;
            if (baseRegions == null) return null;
            for (int i = 0; i < PrototypeEquipmentAppearance.ArmourSlots.Length; i++)
                if (PrototypeEquipmentAppearance.ArmourSlots[i] == slot && i < baseRegions.Length)
                    return baseRegions[i];
            return null;
        }

        public bool IsBaseRegionVisible(EquipmentSlot slot)
        {
            SkinnedMeshRenderer region = GetBaseRegion(slot);
            return region != null && region.enabled;
        }

        public SkinnedMeshRenderer GetArmourPiece(EquipmentSlot slot)
        {
            if (armourPieces == null) return null;
            for (int i = 0; i < PrototypeEquipmentAppearance.ArmourSlots.Length; i++)
                if (PrototypeEquipmentAppearance.ArmourSlots[i] == slot && i < armourPieces.Length)
                    return armourPieces[i];
            return null;
        }

        public SkinnedMeshRenderer GetAccessory(EquipmentSlot slot)
        {
            if (accessories == null) return null;
            for (int i = 0; i < AccessorySlots.Length; i++)
                if (AccessorySlots[i] == slot && i < accessories.Length) return accessories[i];
            return null;
        }

        public bool IsArmourVisible(EquipmentSlot slot)
        {
            SkinnedMeshRenderer piece = GetArmourPiece(slot);
            return piece != null && piece.enabled;
        }

        public bool IsAccessoryVisible(EquipmentSlot slot)
        {
            SkinnedMeshRenderer accessory = GetAccessory(slot);
            return accessory != null && accessory.enabled;
        }

        public void ApplyEquipmentState(EquipmentState equipment)
        {
            if (!IsConfigured || equipment == null) return;
            for (int i = 0; i < PrototypeEquipmentAppearance.ArmourSlots.Length; i++)
                ApplyItem(armourPieces[i], equipment.GetEquipped(PrototypeEquipmentAppearance.ArmourSlots[i]));
            ApplyItem(sword, equipment.GetEquipped(EquipmentSlot.Sword));
            ApplyItem(shield, equipment.GetEquipped(EquipmentSlot.Shield));
            for (int i = 0; i < AccessorySlots.Length; i++)
                ApplyItem(GetAccessory(AccessorySlots[i]), equipment.GetEquipped(AccessorySlots[i]));
            UpdateBaseBody();
            baseHead.enabled = equipment.GetEquipped(EquipmentSlot.Head) == null;
            armourReference.enabled = false;
        }

        public void ApplySlots(IReadOnlyList<EquipmentSlot> slots, IReadOnlyList<string> appearanceIds = null)
        {
            if (!IsConfigured) return;
            foreach (EquipmentSlot slot in PrototypeEquipmentAppearance.ArmourSlots)
                ApplySlot(GetArmourPiece(slot), slot, slots, appearanceIds);
            ApplySlot(sword, EquipmentSlot.Sword, slots, appearanceIds);
            ApplySlot(shield, EquipmentSlot.Shield, slots, appearanceIds);
            foreach (EquipmentSlot slot in AccessorySlots)
                ApplySlot(GetAccessory(slot), slot, slots, appearanceIds);
            UpdateBaseBody();
            baseHead.enabled = !IsArmourVisible(EquipmentSlot.Head);
            armourReference.enabled = false;
        }

        private void UpdateBaseBody()
        {
            // Only the uncovered region draws. This keeps the original plate silhouette
            // and avoids body/cloth passing through an equipped piece during animation.
            baseBody.enabled = false;
            if (baseRegions == null) return;
            for (int i = 0; i < baseRegions.Length; i++)
                if (baseRegions[i] != null)
                {
                    EquipmentSlot slot = PrototypeEquipmentAppearance.ArmourSlots[i];
                    // The breastplate normally covers the inner shoulder/axilla seam.
                    // Without it, retain the shirt sleeve beneath equipped pauldrons.
                    baseRegions[i].enabled = !IsArmourVisible(slot) ||
                        ((slot == EquipmentSlot.Shoulders || slot == EquipmentSlot.Legs) && !IsArmourVisible(EquipmentSlot.Chest));
                }
        }

        private void ApplySlot(SkinnedMeshRenderer renderer, EquipmentSlot slot,
            IReadOnlyList<EquipmentSlot> slots, IReadOnlyList<string> appearanceIds)
        {
            if (renderer == null) return;
            int found = -1;
            if (slots != null)
                for (int i = 0; i < slots.Count; i++) if (slots[i] == slot) { found = i; break; }
            renderer.enabled = found >= 0;
            string finish = found >= 0 && appearanceIds != null && found < appearanceIds.Count
                ? appearanceIds[found] : null;
            ApplyTint(renderer, PrototypeEquipmentAppearance.GetFinishTint(finish));
        }

        private void ApplyItem(SkinnedMeshRenderer renderer, EquipmentItem item)
        {
            if (renderer == null) return;
            renderer.enabled = item != null;
            ApplyTint(renderer, PrototypeEquipmentAppearance.GetFinishTint(item));
        }

        private void ApplyTint(Renderer renderer, Color tint)
        {
            if (properties == null) properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties, 0);
            // Keep authored leather/metal colours; the item finish modulates the base.
            Color baseColour = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(TintProperty)
                ? renderer.sharedMaterial.GetColor(TintProperty) : Color.white;
            properties.SetColor(TintProperty, baseColour * tint);
            renderer.SetPropertyBlock(properties, 0);
        }
    }
}
