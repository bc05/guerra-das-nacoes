using System.Collections.Generic;
using UnityEngine;

namespace GuerraDasNacoes
{
    // Remote entities contain rendering and animation only. They never execute local combat,
    // grant experience, apply damage or hold a CharacterController.
    [DisallowMultipleComponent]
    public sealed class PrototypeRemotePlayer : MonoBehaviour
    {
        private readonly Dictionary<EquipmentSlot, EquipmentItem> equipped = new Dictionary<EquipmentSlot, EquipmentItem>();
        private readonly List<EquipmentSlot> visibleSlots = new List<EquipmentSlot>(12);
        private readonly List<string> appearanceIds = new List<string>(12);
        private Transform visual;
        private Quaternion originalVisualRotation;
        private Animator animator;
        private PrototypeEquipmentVisual equipmentVisual;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Vector3 interpolationPosition;
        private Quaternion interpolationRotation;
        private float interpolationStartedAt;
        private float interpolationDuration = .1f;
        private double lastServerTime;
        private bool receivedState;
        private bool hasMoveParameter, hasLocomotionParameter;
        private PrototypeAttackAnimation attackAnimation;
        private int health, maxHealth;
        private string displayName;
        private float damageUntil;
        private int damageAmount;
        private PrototypeAttackEffects attackEffects;
        private PrototypeAreaEffects areaEffects;
        private Material ownedEffectMaterial;
        private GUIStyle nameStyle;
        private GUIStyle healthStyle;

        public string PlayerId { get; private set; }
        public int Health => health;
        public int MaxHealth => maxHealth;
        public PrototypeEquipmentVisual EquipmentVisual => equipmentVisual;
        public bool HasGameplayComponents => GetComponentInChildren<PrototypeCombat>() != null ||
            GetComponentInChildren<PrototypeCharacter>() != null || GetComponentInChildren<PrototypeOnlineClient>() != null ||
            GetComponentInChildren<Collider>() != null;

        public static PrototypeRemotePlayer Create(PrototypeCharacter source, string playerId)
        {
            GameObject root = new GameObject("Jogador remoto");
            root.transform.position = source.transform.position;
            PrototypeRemotePlayer remote = root.AddComponent<PrototypeRemotePlayer>();
            remote.PlayerId = playerId;
            GameObject clone = Instantiate(source.VisualRoot.gameObject, root.transform, false);
            clone.name = "Visual remoto";
            remote.visual = clone.transform;
            // Local death/idle presentation may currently have tilted the source model.
            remote.originalVisualRotation = Quaternion.identity;
            remote.visual.localRotation = remote.originalVisualRotation;
            // The source is the model subtree, not the player GameObject; strip any future
            // gameplay components/colliders defensively before a remote entity can update.
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // This component only controls renderers. Keep the same piece/body bindings
                // on local and remote avatars; progression and combat remain on the local root.
                if (behaviour is PrototypeEquipmentVisual) continue;
                behaviour.enabled = false;
                Destroy(behaviour);
            }
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }
            foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            remote.animator = clone.GetComponentInChildren<Animator>();
            if (remote.animator != null)
            {
                remote.animator.applyRootMotion = false;
                foreach (AnimatorControllerParameter parameter in remote.animator.parameters)
                {
                    if (parameter.name == "MoveSpeed" && parameter.type == AnimatorControllerParameterType.Float)
                        remote.hasMoveParameter = true;
                    if (parameter.name == "LocomotionSpeed" && parameter.type == AnimatorControllerParameterType.Float)
                        remote.hasLocomotionParameter = true;
                }
                remote.animator.Play("Idle", 0, 0f);
            }
            remote.attackAnimation = new PrototypeAttackAnimation(remote.animator);
            remote.equipmentVisual = clone.GetComponentInChildren<PrototypeEquipmentVisual>(true);
            Shader glow = Shader.Find("GuerraDasNacoes/AttackGlow");
            if (glow != null)
            {
                remote.ownedEffectMaterial = new Material(glow) { name = "Energia de jogador remoto" };
                remote.ownedEffectMaterial.SetColor("_Color", Color.white);
                remote.attackEffects = root.AddComponent<PrototypeAttackEffects>();
                remote.attackEffects.Configure(remote.ownedEffectMaterial, remote.ownedEffectMaterial);
                remote.areaEffects = root.AddComponent<PrototypeAreaEffects>();
                remote.areaEffects.Configure(remote.ownedEffectMaterial);
            }
            return remote;
        }

        public void ApplyState(OnlinePlayerState state, double serverNow)
        {
            Vector3 nextPosition = new Vector3(state.x, transform.position.y, state.z);
            // Interpolate over the measured snapshot interval. A fixed "moving for .18 s"
            // pulse made remote feet stop between polls and hid running speed entirely.
            if (receivedState)
                interpolationDuration = Mathf.Clamp((float)(serverNow - lastServerTime), 1f / 60f, .35f);
            interpolationPosition = transform.position;
            interpolationRotation = transform.rotation;
            interpolationStartedAt = Time.time;
            targetPosition = nextPosition;
            targetRotation = Quaternion.Euler(0f, state.yaw, 0f);
            if (!receivedState || (transform.position - nextPosition).sqrMagnitude > 64f)
            {
                transform.SetPositionAndRotation(nextPosition, targetRotation);
                interpolationPosition = nextPosition;
                interpolationRotation = targetRotation;
                if (hasMoveParameter) animator.SetFloat("MoveSpeed", 0f);
                if (hasLocomotionParameter) animator.SetFloat("LocomotionSpeed", 0f);
            }
            lastServerTime = serverNow;
            receivedState = true;
            displayName = string.IsNullOrWhiteSpace(state.displayName) ? "Guerreiro" : state.displayName;
            if (health > state.health && maxHealth > 0)
            {
                damageAmount = health - state.health;
                damageUntil = Time.time + .8f;
            }
            health = state.health;
            maxHealth = state.maxHealth;
            ApplyEquipment(state.equipped);
            if (health <= 0)
            {
                if (attackEffects != null) attackEffects.Cancel();
                if (areaEffects != null) areaEffects.Cancel();
            }
        }

        private void Update()
        {
            if (!receivedState) return;
            Vector3 previousPosition = transform.position;
            float progress = Mathf.Clamp01((Time.time - interpolationStartedAt) / interpolationDuration);
            transform.position = Vector3.Lerp(interpolationPosition, targetPosition, progress);
            transform.rotation = Quaternion.Slerp(interpolationRotation, targetRotation, progress);
            float speed = health > 0
                ? Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up).magnitude / Mathf.Max(Time.deltaTime, .0001f)
                : 0f;
            if (hasMoveParameter) animator.SetFloat("MoveSpeed", speed / PrototypeCharacter.DefaultWalkSpeed, .06f, Time.deltaTime);
            if (hasLocomotionParameter) animator.SetFloat("LocomotionSpeed", speed, .06f, Time.deltaTime);
            if (visual != null)
                visual.localRotation = health <= 0 ? originalVisualRotation * Quaternion.Euler(0f, 0f, 75f) : originalVisualRotation;
        }

        public void PlayAttack(int ability)
        {
            if (health <= 0 || ability < 0 || ability > 1) return;
            attackAnimation.PlayNext(ability == 1);
            if (attackEffects != null) attackEffects.PlaySwing();
            if (areaEffects != null) areaEffects.Play(transform.position, transform.forward,
                ability == 1 ? 3.15f : 2.65f, ability == 1 ? 360f : 130f, ability == 1);
            PrototypeAudio.Play(ability == 1 ? GameSound.HeavySwing : GameSound.SwordSwing, transform.position, .8f);
        }

        private void ApplyEquipment(EquipmentSlotSave[] slots)
        {
            equipped.Clear();
            if (slots != null)
                foreach (EquipmentSlotSave item in slots)
                    if (item != null)
                    {
                        string definitionId = string.IsNullOrEmpty(item.definitionId) ? item.itemId : item.definitionId;
                        EquipmentItem definition = EquipmentCatalog.GetDefinition(definitionId, item.definitionRevision > 0 ? item.definitionRevision : 1);
                        if (definition != null) equipped[(EquipmentSlot)item.slot] = definition;
                    }
            visibleSlots.Clear();
            appearanceIds.Clear();
            foreach (KeyValuePair<EquipmentSlot, EquipmentItem> entry in equipped)
            {
                visibleSlots.Add(entry.Key);
                appearanceIds.Add(entry.Value.AppearanceId);
            }
            if (equipmentVisual != null) equipmentVisual.ApplySlots(visibleSlots, appearanceIds);
        }

        private void OnGUI()
        {
            Camera camera = Camera.main;
            if (!receivedState || camera == null) return;
            Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * 2.08f);
            if (screen.z <= 0f) return;
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13,
                    fontStyle = FontStyle.Bold, richText = false };
                nameStyle.normal.textColor = new Color(.7f, .88f, 1f);
                healthStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10, richText = false };
            }
            float x = screen.x - 75f, y = Screen.height - screen.y;
            GUI.Label(new Rect(x - 35f, y - 24f, 220f, 22f), displayName, nameStyle);
            Color previous = GUI.color;
            GUI.color = new Color(.04f, .04f, .06f, .9f);
            GUI.DrawTexture(new Rect(x, y, 150, 12), Texture2D.whiteTexture);
            GUI.color = new Color(.2f, .63f, .87f, .95f);
            GUI.DrawTexture(new Rect(x + 1, y + 1, 148f * Mathf.Clamp01(maxHealth > 0 ? health / (float)maxHealth : 0f), 10), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(x, y - 2f, 150, 16), health + " / " + maxHealth, healthStyle);
            if (Time.time < damageUntil)
                GUI.Label(new Rect(x, y - 55f - (1f - (damageUntil - Time.time) / .8f) * 18f, 150, 24), "−" + damageAmount, nameStyle);
        }

        private void OnDestroy()
        {
            if (ownedEffectMaterial != null) Destroy(ownedEffectMaterial);
        }
    }
}
