namespace GuerraDasNacoes.Server;

public sealed partial class GameWorld
{
    private readonly CollisionGrid catacombsGrid;
    private bool HasCatacombs => catacombsGrid!=null && economy.worldContentVersion>=3;

    private void InstallCatacombs(double now)
    {
        if(catacombsGrid==null || economy.worldContentVersion<2 || economy.worldContentVersion>=3)return;
        foreach(var point in CatacombsLayout.SpawnPoints.Append(CatacombsLayout.Entry).Append(CatacombsLayout.ReturnPortal))
            if(!catacombsGrid.Free(point.X,point.Z))throw new InvalidDataException("Nascimento das Catacumbas bloqueado; nenhum progresso foi alterado.");
        if(!grid.Free(CatacombsLayout.ValePortal.X,CatacombsLayout.ValePortal.Z)||!grid.Free(CatacombsLayout.ValeReturn.X,CatacombsLayout.ValeReturn.Z))
            throw new InvalidDataException("A entrada das Catacumbas exige a grade atualizada do Vale.");
        var active=EconomyRules.Clone(economy.active);
        CatacombsContent.Add(active);
        var validation=EconomyRules.Validate(active,economy,MapFree);
        if(!validation.valid)throw new InvalidDataException("Catacumbas incompatíveis: "+string.Join("; ",validation.errors));
        active.revision=economy.releases.Max(r=>r.config.revision)+1;
        foreach(var release in economy.releases.Where(r=>r.status=="active"))release.status="historical";
        economy.active=active;
        economy.releases=economy.releases.Append(new EconomyRelease{config=EconomyRules.Clone(active),reason="Catacumbas: mapa, esqueletos e conjuradores",createdAtUnixMs=(long)(now*1000)}).ToArray();
        var draft=EconomyRules.Clone(economy.draft??active);
        try { CatacombsContent.Add(draft);economy.draft=draft; }
        catch(ArgumentException) { /* An unfinished draft never prevents loading a valid world. */ }
        economy.draftRevision++;
        economy.worldContentVersion=3;
        economy.audit=economy.audit.Append(new EconomyAudit{action="catacombs",reason="Mapa e encontros adicionados, sem substituir progresso ou revisões anteriores",version=active.revision,atUnixMs=(long)(now*1000)}).ToArray();
    }

    private IEnumerable<OnlinePortal> AvailablePortals => Portals.Where(p=>
        (p.mapId!=CatacombsLayout.MapId&&p.destinationMapId!=CatacombsLayout.MapId)||HasCatacombs);

    private static (float x,float z) PortalDestination(string id) => id switch {
        "portal-city" => (0,-5), "portal-ruins" => (0,-1),
        "portal-catacombs" => (CatacombsLayout.Entry.X,CatacombsLayout.Entry.Z),
        "portal-catacombs-exit" => (CatacombsLayout.ValeReturn.X,CatacombsLayout.ValeReturn.Z),
        _=>throw new InvalidDataException("Destino de portal desconhecido.") };
}
