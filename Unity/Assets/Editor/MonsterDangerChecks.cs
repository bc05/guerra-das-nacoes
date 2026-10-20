using System;
using GuerraDasNacoes;
using UnityEngine;

public static class MonsterDangerChecks
{
    public static void Run()
    {
        int checks = 0;
        Action<bool> require = condition => { if (!condition) throw new InvalidOperationException("Faixa de perigo por nível inconsistente."); checks++; };
        require(MonsterDanger.Compare(4, 10) == MonsterDangerBand.Trivial);
        require(MonsterDanger.Compare(5, 10) == MonsterDangerBand.Easy);
        require(MonsterDanger.Compare(7, 10) == MonsterDangerBand.Easy);
        require(MonsterDanger.Compare(8, 10) == MonsterDangerBand.Matched);
        require(MonsterDanger.Compare(12, 10) == MonsterDangerBand.Matched);
        require(MonsterDanger.Compare(13, 10) == MonsterDangerBand.Dangerous);
        require(MonsterDanger.Compare(15, 10) == MonsterDangerBand.Dangerous);
        require(MonsterDanger.Compare(16, 10) == MonsterDangerBand.Deadly);
        require(MonsterDanger.Compare(19, 10) == MonsterDangerBand.Deadly);
        require(MonsterDanger.Compare(20, 10) == MonsterDangerBand.Overwhelming);
        require(MonsterDanger.Compare(int.MaxValue, 1) == MonsterDangerBand.Overwhelming);
        require(MonsterDanger.Compare(1, int.MaxValue) == MonsterDangerBand.Trivial);
        require(MonsterDanger.Compare(5, 1) != MonsterDanger.Compare(5, 10));
        require(MonsterDanger.Tint(25, 1) != MonsterDanger.Tint(1, 25));
        require(!string.IsNullOrEmpty(MonsterDanger.Describe(25, 1)));
        Debug.Log("[MONSTER DANGER PASS] " + checks + " limites, mudança de nível, cor e descrição de perigo.");
    }
}
