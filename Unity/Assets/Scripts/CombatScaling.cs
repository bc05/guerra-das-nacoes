using System;

namespace GuerraDasNacoes
{
    public enum HeroAttribute
    {
        Attack,
        Defense,
        Intelligence
    }

    // Balance lives outside Unity so combat, equipment and previews use the same rules.
    public static class CombatScaling
    {
        public const int PointsPerLevel = 5;
        public const double PhysicalAttackPerPoint = 0.02d;
        public const double SkillAttackPerPoint = 0.01d;
        public const double MagicDamagePerPoint = 0.015d;
        public const double MagicDurationPerPoint = 0.005d;
        public const double DefensePerPoint = 2d;
        public const double DefenseReference = 100d;

        public static double PhysicalAttack(int points) => 1d + Math.Max(0, points) * PhysicalAttackPerPoint;
        public static double SkillAttack(int points) => 1d + Math.Max(0, points) * SkillAttackPerPoint;
        public static double MagicDamage(int points) => 1d + Math.Max(0, points) * MagicDamagePerPoint;
        public static double MagicDuration(int points) => 1d + Math.Max(0, points) * MagicDurationPerPoint;
        public static double DamageTaken(int points) => DefenseReference /
            (DefenseReference + Math.Max(0, points) * DefensePerPoint);

        public static bool IsPositive(double value) => !double.IsNaN(value) && value > 0d;

        public static int RoundDamage(double value)
        {
            if (!IsPositive(value)) return 0;
            if (value >= int.MaxValue) return int.MaxValue;
            // A positive hit always does at least one point. Round only after all multipliers.
            return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
        }

        public static double ScaleDuration(double seconds, double multiplier)
        {
            if (!IsPositive(seconds) || !IsPositive(multiplier)) return 0d;
            double result = seconds * multiplier;
            return double.IsPositiveInfinity(result) ? double.MaxValue : result;
        }
    }
}
