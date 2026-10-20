using UnityEngine;

namespace GuerraDasNacoes
{
    public enum MonsterDangerBand { Trivial, Easy, Matched, Dangerous, Deadly, Overwhelming }

    public static class MonsterDanger
    {
        public static MonsterDangerBand Compare(int monsterLevel, int heroLevel)
        {
            long difference = (long)Mathf.Max(1, monsterLevel) - Mathf.Max(1, heroLevel);
            return difference <= -6 ? MonsterDangerBand.Trivial : difference <= -3 ? MonsterDangerBand.Easy :
                difference <= 2 ? MonsterDangerBand.Matched : difference <= 5 ? MonsterDangerBand.Dangerous :
                difference <= 9 ? MonsterDangerBand.Deadly : MonsterDangerBand.Overwhelming;
        }

        public static Color Tint(int monsterLevel, int heroLevel)
        {
            switch (Compare(monsterLevel, heroLevel))
            {
                case MonsterDangerBand.Trivial: return new Color(.65f, .67f, .68f);
                case MonsterDangerBand.Easy: return new Color(.42f, .86f, .49f);
                case MonsterDangerBand.Matched: return new Color(1f, .86f, .38f);
                case MonsterDangerBand.Dangerous: return new Color(1f, .58f, .20f);
                case MonsterDangerBand.Deadly: return new Color(1f, .29f, .25f);
                default: return new Color(.87f, .43f, 1f);
            }
        }

        public static string Describe(int monsterLevel, int heroLevel)
        {
            switch (Compare(monsterLevel, heroLevel))
            {
                case MonsterDangerBand.Trivial: return "Muito abaixo do seu nível";
                case MonsterDangerBand.Easy: return "Ameaça baixa";
                case MonsterDangerBand.Matched: return "Nível próximo ao seu";
                case MonsterDangerBand.Dangerous: return "Perigoso";
                case MonsterDangerBand.Deadly: return "Muito perigoso";
                default: return "Ameaça extrema";
            }
        }
    }
}
