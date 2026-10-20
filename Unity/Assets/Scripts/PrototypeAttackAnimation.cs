using UnityEngine;

namespace GuerraDasNacoes
{
    // Presentation only: choosing a captured motion never changes damage, timing or ability rules.
    public sealed class PrototypeAttackAnimation
    {
        public const string VariantParameter = "AttackVariant";
        public const int BasicVariantCount = 3;
        public const int HeavyVariant = 3;
        public static readonly string[] ClipNames = { "Attack", "Attack2", "Attack3", "HeavyAttack" };

        private readonly Animator animator;
        private readonly bool hasVariant;
        public bool HasTrigger { get; }
        public int NextBasicVariant { get; private set; }
        public int BasicSequenceVersion { get; private set; }
        public int LastVariant { get; private set; }

        public PrototypeAttackAnimation(Animator source)
        {
            animator = source;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name == "Attack" && parameter.type == AnimatorControllerParameterType.Trigger)
                    HasTrigger = true;
                if (parameter.name == VariantParameter && parameter.type == AnimatorControllerParameterType.Float)
                    hasVariant = true;
            }
        }

        public int PlayNext(bool heavy)
        {
            int variant = heavy ? HeavyVariant : NextBasicVariant;
            if (!heavy)
            {
                NextBasicVariant = (NextBasicVariant + 1) % BasicVariantCount;
                BasicSequenceVersion++;
            }
            PlayVariant(variant);
            return variant;
        }

        // Direct selection is useful for previews; the blend parameter is always a discrete value.
        // Legacy controllers with only Attack retain their original motion without parameter warnings.
        public void PlayVariant(int variant)
        {
            LastVariant = Mathf.Clamp(variant, 0, HeavyVariant);
            if (animator == null) return;
            if (hasVariant) animator.SetFloat(VariantParameter, LastVariant);
            if (HasTrigger) animator.SetTrigger("Attack");
        }

        public void RestoreBasicSequence(int expectedVersion, int nextVariant)
        {
            // An old rejection cannot rewind a more recent authoritative presentation.
            if (BasicSequenceVersion != expectedVersion || nextVariant < 0 || nextVariant >= BasicVariantCount) return;
            NextBasicVariant = nextVariant;
            BasicSequenceVersion++;
        }

        public void ResetSequence()
        {
            NextBasicVariant = 0;
            LastVariant = 0;
            BasicSequenceVersion++;
            if (animator != null && hasVariant) animator.SetFloat(VariantParameter, 0f);
        }
    }
}
