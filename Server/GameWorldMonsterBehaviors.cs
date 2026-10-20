namespace GuerraDasNacoes.Server;

public sealed partial class GameWorld
{
    private void AdvanceSpecialAttack(SavedMonster monster, EconomyMonsterConfig profile, Player target, double now, double dt)
    {
        bool area = profile.behavior == "area";
        if (monster.windupUntil > 0)
        {
            if (now < monster.windupUntil) return;
            monster.windupUntil = 0;
            monster.nextAttackAt = now + Math.Max(.1, profile.attackInterval - profile.attackPreparation);
            float radius = profile.areaRadius;
            var hits = new List<(Player player, int damage)>();
            // The warning's ground position is fixed. Moving out of it or behind cover avoids damage.
            foreach (var victim in players.Values)
            {
                if ((!area && victim != target) || !victim.stats.IsAlive || victim.state.mapId != monster.mapId ||
                    !Online(victim.state.id, now) || Distance(victim.state.x,victim.state.z,profile.x,profile.z) > profile.leashRadius ||
                    Distance(victim.state.x,victim.state.z,monster.attackX,monster.attackZ) > radius ||
                    !MapGrid(monster.mapId).ClearLine(monster.x,monster.z,victim.state.x,victim.state.z)) continue;
                int damage = victim.stats.ReceiveDamage(profile.damage, victim.state.guardUntil > now ? .5 : 1);
                hits.Add((victim, damage));
            }
            int ability = area ? 3 : 2;
            Emit("monsterStrike",monster.id,target.state.id,ability,hits.Sum(hit=>hit.damage));
            foreach (var hit in hits)
            {
                Emit("playerHit",monster.id,hit.player.state.id,ability,hit.damage);
                if (!hit.player.stats.IsAlive)
                {
                    hit.player.state.guardUntil = hit.player.state.furyUntil = hit.player.state.healUntil = 0;
                    hit.player.state.strike = null;
                    Emit("death",monster.id,hit.player.state.id,ability,0);
                }
            }
            return;
        }
        double distance = Distance(monster.x,monster.z,target.state.x,target.state.z);
        float reach = area ? profile.areaRadius : profile.attackRange;
        if (distance > reach)
            MoveMonster(monster,target.state.x,target.state.z,Math.Min(profile.moveSpeed*dt,distance-(reach-.15)));
        else if (now >= monster.nextAttackAt && MapGrid(monster.mapId).ClearLine(monster.x,monster.z,target.state.x,target.state.z))
        {
            monster.attackX = area ? monster.x : target.state.x;
            monster.attackZ = area ? monster.z : target.state.z;
            monster.attackPositionSet = true;
            monster.windupUntil = now + profile.attackPreparation;
        }
    }
}
