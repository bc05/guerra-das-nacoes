using UnityEngine;

namespace GuerraDasNacoes
{
    /// <summary>Observes the hero's equipment revision and updates the shared modular visual.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PrototypeCombat))]
    public sealed class PrototypeEquipmentAppearance : MonoBehaviour
    {
        public static readonly EquipmentSlot[] ArmourSlots =
        {
            EquipmentSlot.Head, EquipmentSlot.Shoulders, EquipmentSlot.Chest,
            EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet
        };
        private static readonly Color SteelTint = new Color(.83f, .78f, .67f, 1f);
        private static readonly Color ArcaneTint = new Color(.60f, .73f, .88f, 1f);
        [SerializeField] private PrototypeEquipmentVisual visual;
        private PrototypeCombat combat;
        private int appliedRevision = -1;
        private EquipmentState appliedState;

        public bool IsConfigured => visual != null && visual.IsConfigured;
        public bool SwordVisible => visual != null && visual.SwordVisible;
        public bool ShieldVisible => visual != null && visual.ShieldVisible;
        public bool BaseBodyVisible => visual != null && visual.BaseBodyVisible;
        public bool BaseHeadVisible => visual != null && visual.BaseHeadVisible;
        public int AppliedRevision => appliedRevision;
        public PrototypeEquipmentVisual Visual => visual;
        public SkinnedMeshRenderer ArmourRenderer => visual != null ? visual.ArmourRenderer : null;
        public SkinnedMeshRenderer BaseBodyRenderer => visual != null ? visual.BaseBodyRenderer : null;
        public SkinnedMeshRenderer BaseHeadRenderer => visual != null ? visual.BaseHeadRenderer : null;
        public SkinnedMeshRenderer SwordRenderer => visual != null ? visual.SwordRenderer : null;
        public SkinnedMeshRenderer ShieldRenderer => visual != null ? visual.ShieldRenderer : null;
        public SkinnedMeshRenderer GetArmourPiece(EquipmentSlot slot) => visual != null ? visual.GetArmourPiece(slot) : null;
        public SkinnedMeshRenderer GetAccessory(EquipmentSlot slot) => visual != null ? visual.GetAccessory(slot) : null;
        public bool IsArmourVisible(EquipmentSlot slot) => visual != null && visual.IsArmourVisible(slot);
        public SkinnedMeshRenderer GetBaseRegion(EquipmentSlot slot) => visual != null ? visual.GetBaseRegion(slot) : null;
        public bool IsBaseRegionVisible(EquipmentSlot slot) => visual != null && visual.IsBaseRegionVisible(slot);
        public bool IsAccessoryVisible(EquipmentSlot slot) => visual != null && visual.IsAccessoryVisible(slot);

        public void Configure(PrototypeEquipmentVisual equipmentVisual)
        {
            visual = equipmentVisual; appliedRevision = -1;
            if (Application.isPlaying) RefreshAppearance();
        }

        private void Awake() { combat = GetComponent<PrototypeCombat>(); }
        private void OnEnable()
        {
            appliedRevision = -1;
            if (Application.isPlaying) RefreshAppearance();
        }
        private void LateUpdate()
        {
            if (combat != null && (!ReferenceEquals(appliedState, combat.Stats.Equipment) ||
                appliedRevision != combat.Stats.Equipment.Revision)) RefreshAppearance();
        }
        public void RefreshAppearance()
        {
            if (!IsConfigured) return;
            if (combat == null) combat = GetComponent<PrototypeCombat>();
            EquipmentState equipment = combat.Stats.Equipment;
            visual.ApplyEquipmentState(equipment);
            appliedRevision = equipment.Revision; appliedState = equipment;
        }
        public static Color GetFinishTint(EquipmentItem item) => GetFinishTint(item != null ? item.AppearanceId : null);
        public static Color GetFinishTint(string appearanceId)
        {
            switch (appearanceId)
            {
                case "steel": return SteelTint;
                case "arcane": return ArcaneTint;
                default: return Color.white;
            }
        }
    }
}
