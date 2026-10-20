using UnityEngine;

namespace GuerraDasNacoes
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PrototypeStatusEffects))]
    public sealed class PrototypeAbilities : MonoBehaviour
    {
        public const int SlotCount = 8;
        private const float GlobalActionDelay = 0.2f;
        private const float BaseGuardDuration = 4f;
        private const float BaseDamageBoostDuration = 8f;

        private PrototypeCombat combat;
        private PrototypeStatusEffects statusEffects;
        private readonly float[] readyAt = new float[SlotCount];
        private float mana = 100f;
        private float guardUntil;
        private float damageBoostUntil;
        private float nextActionAt;
        private static PrototypeOnlineClient Network => PrototypeOnlineClient.Active;
        private static bool UsesServer => Network != null && Network.IsOnlineMode;

        public float Mana => mana;
        public int MaxMana => 100;
        public float RegenerationPerSecond => 8f;
        public bool IsGuarding => combat != null && combat.IsAlive && Time.time < guardUntil;
        public float GuardRemaining => IsGuarding ? Mathf.Max(0f, guardUntil - Time.time) : 0f;
        public bool IsDamageBoosted => combat != null && combat.IsAlive && Time.time < damageBoostUntil;
        public float DamageBoostRemaining => IsDamageBoosted ? Mathf.Max(0f, damageBoostUntil - Time.time) : 0f;
        public float GuardDurationSeconds { get; private set; } = BaseGuardDuration;
        public float DamageBoostDurationSeconds { get; private set; } = BaseDamageBoostDuration;
        public double OutgoingDamageMultiplier => IsDamageBoosted ? 1.25d : 1d;
        public double IncomingDamageMultiplier => IsGuarding ? 0.5d : 1d;
        public PrototypeStatusEffects StatusEffects => statusEffects;
        public float GlobalCooldownRemaining => Mathf.Max(0f, nextActionAt - Time.time);
        public bool IsActionReady => isActiveAndEnabled && combat != null && combat.isActiveAndEnabled && combat.IsAlive &&
            (!UsesServer || (Network.CanPlay && !Network.HasPendingMutation)) && Time.time >= nextActionAt;

        private void Awake()
        {
            combat = GetComponent<PrototypeCombat>();
            statusEffects = GetComponent<PrototypeStatusEffects>();
        }

        private void Update()
        {
            if (UsesServer)
            {
                if (!Network.CanPlay || combat == null || !combat.IsAlive) CancelActiveEffects();
                return;
            }
            if (combat != null && combat.isActiveAndEnabled && combat.IsAlive)
                mana = Mathf.Min(MaxMana, mana + RegenerationPerSecond * Time.deltaTime);
            else
                CancelActiveEffects();
        }

        public bool IsAssigned(int slot)
        {
            return slot >= 0 && slot < 5;
        }

        public string GetName(int slot)
        {
            switch (slot)
            {
                case 0: return "Ataque Básico";
                case 1: return "Corte Pesado";
                case 2: return "Guarda de Ferro";
                case 3: return "Segundo Fôlego";
                case 4: return "Fúria Arcana";
                default: return string.Empty;
            }
        }

        public string GetDescription(int slot)
        {
            switch (slot)
            {
                case 0: return "Um golpe por acionamento; segurar não repete. Com alvo, aproxima até o alcance. Sem alvo, golpeia à frente. Arco frontal de 130° a 2,65 m.";
                case 1: return "Um corte por acionamento: aproxima até o alvo e libera uma onda circular a 3,15 m. Poder 1,8: bônus de Ataque e Inteligência. Também funciona sem alvo.";
                case 2: return $"Reduz o dano recebido em 50% durante {MagicDuration(BaseGuardDuration):0.#} segundos. Inteligência aumenta a duração.";
                case 3: return "Recupera 35% da vida máxima. Requer vida abaixo do máximo.";
                case 4: return $"Adiciona 25% ao dano dos ataques e do Corte Pesado por {MagicDuration(BaseDamageBoostDuration):0.#} segundos. Inteligência aumenta a duração. Não acumula.";
                default: return "Espaço de habilidade vazio.";
            }
        }

        public int GetManaCost(int slot)
        {
            switch (slot)
            {
                case 1: return 16;
                case 2: return 20;
                case 3: return 25;
                case 4: return 25;
                default: return 0;
            }
        }

        public float GetCooldownDuration(int slot)
        {
            switch (slot)
            {
                case 0: return combat != null ? combat.AttackInterval : 0.65f;
                case 1: return 5f;
                case 2: return 12f;
                case 3: return 15f;
                case 4: return 20f;
                default: return 0f;
            }
        }

        public float GetCooldownRemaining(int slot)
        {
            if (!IsAssigned(slot))
                return 0f;
            float ownCooldown = Mathf.Max(0f, readyAt[slot] - Time.time);
            if ((slot == 0 || slot == 1) && combat != null)
                return Mathf.Max(ownCooldown, combat.CooldownRemaining);
            return ownCooldown;
        }

        public bool CanUse(int slot)
        {
            if (PrototypeGameMenu.BlocksGameplay || !IsAssigned(slot) || !IsActionReady || GetCooldownRemaining(slot) > 0f || mana < GetManaCost(slot))
                return false;
            switch (slot)
            {
                case 0:
                case 1:
                    return combat.CanAttack;
                case 2:
                    return !IsGuarding;
                case 3:
                    return combat.Stats.Health < combat.Stats.MaxHealth;
                case 4:
                    return !IsDamageBoosted;
                default:
                    return false;
            }
        }

        public bool TryUse(int slot)
        {
            if (!CanUse(slot))
                return false;
            if (slot == 0 || slot == 1) return combat.RequestAttack(slot);
            if (UsesServer) return Network.TryUseAbility(slot);

            switch (slot)
            {
                case 2:
                    GuardDurationSeconds = MagicDuration(BaseGuardDuration);
                    guardUntil = Time.time + GuardDurationSeconds;
                    combat.Notify($"Guarda de Ferro: -50% de dano recebido por {GuardDurationSeconds:0.#} segundos.");
                    break;
                case 3:
                    int restored = combat.Stats.Heal((int)((long)combat.Stats.MaxHealth * 35 / 100));
                    if (restored <= 0)
                        return false;
                    if (statusEffects != null)
                        statusEffects.PlayHealing();
                    combat.Notify($"Segundo Fôlego: +{restored} de vida.");
                    break;
                case 4:
                    DamageBoostDurationSeconds = MagicDuration(BaseDamageBoostDuration);
                    damageBoostUntil = Time.time + DamageBoostDurationSeconds;
                    combat.Notify($"Fúria Arcana: +25% de dano por {DamageBoostDurationSeconds:0.#} segundos.");
                    break;
            }

            mana = Mathf.Max(0f, mana - GetManaCost(slot));
            readyAt[slot] = Time.time + GetCooldownDuration(slot);
            CommitAction();
            if (slot >= 2)
                PrototypeAudio.Play(slot == 2 ? GameSound.Guard : slot == 3 ? GameSound.Heal : GameSound.Fury, transform.position);
            if (statusEffects != null)
                statusEffects.RefreshActiveEffects();
            return true;
        }

        // Called only after the character reaches the selected target. Presentation is never a local online hit.
        internal bool ExecuteAttackAbility(int slot)
        {
            if ((slot != 0 && slot != 1) || !CanUse(slot) || !combat.PrepareTargetedAttack(slot)) return false;
            if (UsesServer) return Network.TryUseAbility(slot);
            bool started = slot == 0 ? combat.TryAttack() :
                combat.TryAttackDamage(combat.Stats.GetSkillDamage(1.8d, true, OutgoingDamageMultiplier), true);
            if (!started) return false;
            mana = Mathf.Max(0f, mana - GetManaCost(slot));
            readyAt[slot] = Time.time + GetCooldownDuration(slot);
            CommitAction();
            return true;
        }

        internal void CommitAction()
        {
            nextActionAt = Time.time + GlobalActionDelay;
        }

        public void ApplyOnlineState(OnlinePlayerState value, double serverTime)
        {
            mana = Mathf.Clamp(value.mana, 0f, MaxMana);
            guardUntil = LocalDeadline(value.guardUntil, serverTime);
            damageBoostUntil = LocalDeadline(value.furyUntil, serverTime);
            nextActionAt = LocalDeadline(value.nextActionAt, serverTime);
            GuardDurationSeconds = value.guardDuration > 0f ? value.guardDuration : MagicDuration(BaseGuardDuration);
            DamageBoostDurationSeconds = value.furyDuration > 0f ? value.furyDuration : MagicDuration(BaseDamageBoostDuration);
            for (int index = 0; index < SlotCount; index++)
                readyAt[index] = value.cooldownUntil != null && index < value.cooldownUntil.Length
                    ? LocalDeadline(value.cooldownUntil[index], serverTime) : 0f;
            if (statusEffects != null) statusEffects.RefreshActiveEffects();
        }

        private static float LocalDeadline(double deadline, double serverTime)
        {
            return Time.time + (float)System.Math.Max(0d, deadline - serverTime);
        }

        public void CancelActiveEffects()
        {
            guardUntil = 0f;
            damageBoostUntil = 0f;
            if (statusEffects != null)
                statusEffects.Cancel();
        }

        private float MagicDuration(float baseSeconds)
        {
            return combat != null ? (float)combat.Stats.GetMagicDuration(baseSeconds) : baseSeconds;
        }

        private void OnDisable()
        {
            CancelActiveEffects();
        }

        public void ResetEncounter()
        {
            mana = MaxMana;
            CancelActiveEffects();
            nextActionAt = 0f;
            for (int slot = 0; slot < SlotCount; slot++)
                readyAt[slot] = 0f;
        }
    }
}
