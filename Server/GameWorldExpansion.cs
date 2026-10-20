namespace GuerraDasNacoes.Server;

public sealed partial class GameWorld
{
    private void InstallMonsterCatalog(double now)
    {
        if(economy.worldContentVersion!=1) return;
        var active=EconomyRules.Clone(economy.active);
        var comparison=EconomyRules.Clone(active);
        if(economy.draft!=null)comparison.revision=economy.draft.revision;
        bool unchangedDraft=economy.draft!=null&&WireJson.Write(comparison)==WireJson.Write(economy.draft);
        MonsterCatalog.Upgrade(active, true);
        var validation=EconomyRules.Validate(active,economy,MapFree);
        if(!validation.valid)throw new InvalidDataException("Catálogo incompatível: "+string.Join("; ",validation.errors));
        active.revision=economy.releases.Max(r=>r.config.revision)+1;
        foreach(var release in economy.releases.Where(r=>r.status=="active"))release.status="historical";
        economy.active=active;
        economy.releases=economy.releases.Append(new EconomyRelease{config=EconomyRules.Clone(active),reason="Espécies e nascimentos separados; comportamentos do Vale dos Clãs",createdAtUnixMs=(long)(now*1000)}).ToArray();
        economy.draft ??= EconomyRules.Clone(active);
        var draft=EconomyRules.Clone(economy.draft);
        try { MonsterCatalog.Upgrade(draft, unchangedDraft); economy.draft=draft; }
        catch(ArgumentException) { /* An unfinished invalid draft must not prevent loading a valid world. */ }
        economy.draftRevision++;
        economy.worldContentVersion=2;
        economy.audit=economy.audit.Append(new EconomyAudit{action="monster-catalog",reason="Identidades, progresso, encontros vivos e rascunho preservados",version=active.revision,atUnixMs=(long)(now*1000)}).ToArray();
    }
    private void InstallWorldExpansion(double now)
    {
        // Existing administrator edits and item instances are retained. Install once only when
        // the matching expanded collision asset is present; historical releases remain immutable.
        if(economy.worldContentVersion>=1 || grid.width*grid.cellSize<95 || grid.height*grid.cellSize<95) return;
        var active=EconomyRules.Clone(economy.active);
        EconomyDefaults.AddFrontierContent(active);
        var validation=EconomyRules.Validate(active,economy,MapFree);
        if(!validation.valid)throw new InvalidDataException("Expansão incompatível com a grade: "+string.Join("; ",validation.errors));
        active.revision=economy.releases.Max(r=>r.config.revision)+1;
        foreach(var release in economy.releases.Where(r=>r.status=="active"))release.status="historical";
        economy.active=active;
        economy.releases=economy.releases.Append(new EconomyRelease{config=EconomyRules.Clone(active),reason="Expansão aditiva: Vale dos Clãs e agenda do chefão",createdAtUnixMs=(long)(now*1000)}).ToArray();
        economy.draft ??= EconomyRules.Clone(active);
        EconomyDefaults.AddFrontierContent(economy.draft);
        economy.draftRevision++;
        economy.worldContentVersion=1;
        economy.audit=economy.audit.Append(new EconomyAudit{action="world-expansion",reason="Perfis e itens adicionados sem substituir configurações existentes",version=active.revision,atUnixMs=(long)(now*1000)}).ToArray();
    }
    private bool BossScheduleNeedsTick(double now) => monsters.Any(m=>m.expiresAt>0&&m.expiresAt<=now&&m.health>0) ||
        (economy.active.bossSchedules??Array.Empty<EconomyBossSchedule>()).Any(s=>BossScheduleRules.Occurrences(s,now,false).Any(o=>o.StartsAt<=now&&now<o.EndsAt&&!economy.bossSpawnWindows.Any(w=>w.id==o.Id)));
    private void AdvanceBossSchedules(double now)
    {
        foreach(var monster in monsters.Where(m=>m.expiresAt>0&&m.expiresAt<=now&&m.health>0))
        {
            monster.health=0;monster.targetId=null;monster.windupUntil=0;monster.respawnAt=monster.expiresAt;
            var receipt=economy.bossSpawnWindows.FirstOrDefault(w=>w.id==monster.spawnWindowId);
            if(receipt!=null)receipt.outcome="expired";
            Emit("bossExpired",monster.id,monster.id,0,0);
        }
        foreach(var schedule in economy.active.bossSchedules??Array.Empty<EconomyBossSchedule>())
        {
            var occurrence=BossScheduleRules.Occurrences(schedule,now,false).FirstOrDefault(o=>o.StartsAt<=now&&now<o.EndsAt);
            if(occurrence.Id==null || economy.bossSpawnWindows.Any(w=>w.id==occurrence.Id))continue;
            var profile=MonsterCatalog.Resolve(economy.active).First(m=>m.id==schedule.monsterId);
            // One profile cannot overlap itself. Receipt is still consumed, so a later kill
            // or configuration edit cannot recreate the same calendar occurrence.
            var existing=monsters.FirstOrDefault(m=>m.id==profile.id);
            bool overlapping=existing!=null&&existing.health>0;
            economy.bossSpawnWindows=economy.bossSpawnWindows.Append(new BossSpawnReceipt{id=occurrence.Id,monsterId=profile.id,startsAt=occurrence.StartsAt,endsAt=occurrence.EndsAt,outcome=overlapping?"overlap-skipped":"active"}).ToArray();
            if(overlapping)continue;
            var spawned=CreateMonster(profile);
            if(existing!=null)spawned.spawn=Math.Max(spawned.spawn,existing.spawn+1);
            spawned.spawnWindowId=occurrence.Id;spawned.expiresAt=occurrence.EndsAt;
            monsters=monsters.Where(m=>m.id!=profile.id).Append(spawned).ToArray();
            Emit("bossSpawn",spawned.id,spawned.id,0,0);
        }
    }
    private OnlineBossWindow[] BossWindowSnapshot(double now) => (economy.active.bossSchedules??Array.Empty<EconomyBossSchedule>()).Select(s=>
    {
        var monster=monsters.FirstOrDefault(m=>m.id==s.monsterId&&m.health>0&&m.expiresAt>now);
        var upcoming=BossScheduleRules.Occurrences(s,now,true).Where(o=>o.StartsAt>now).OrderBy(o=>o.StartsAt).FirstOrDefault();
        return new OnlineBossWindow{id=s.id,monsterId=s.monsterId,name=MonsterCatalog.Resolve(economy.active).First(m=>m.id==s.monsterId).name,timeZone=s.timeZone,active=monster!=null,endsAt=monster?.expiresAt??0,nextSpawnAt=upcoming.StartsAt};
    }).Where(w=>w.active||w.nextSpawnAt>0).ToArray();
}
