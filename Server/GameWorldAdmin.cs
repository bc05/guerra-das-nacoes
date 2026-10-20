namespace GuerraDasNacoes.Server;

public sealed partial class GameWorld
{
    public object AdminState(double? at=null)
    {
        lock(gate)
        {
            EnsureRecovered(at??UnixNow);
            return EconomyRules.Clone(new AdminSnapshot
            {
                active=economy.active,draft=economy.draft,draftRevision=economy.draftRevision,
                history=economy.releases.Select(r=>new AdminReleaseSummary {version=r.config.revision,label=r.config.label,reason=r.reason,createdAtUnixMs=r.createdAtUnixMs,activateAtUnixMs=r.activateAtUnixMs,status=r.status}).ToArray(),
                audit=economy.audit,bossWindows=BossWindowSnapshot(at??UnixNow),
                metrics=new AdminMetrics {players=players.Count,coinsInWallets=players.Values.Sum(p=>(decimal)p.stats.Coins),currencyCreated=economy.ledger.Where(l=>l.amount>0).Sum(l=>(decimal)l.amount),currencyConsumed=-economy.ledger.Where(l=>l.amount<0).Sum(l=>(decimal)l.amount),pendingLoot=players.Values.Sum(p=>p.state.pendingLoot.Length),rewardedDeaths=economy.rewardedDeaths.Length,ledgerEntries=economy.ledger.Length}
            });
        }
    }
    public EconomyValidation AdminValidate(EconomyConfig config)
    {lock(gate)return EconomyRules.Validate(EconomyRules.Normalize(config??economy.draft,economy),economy,MapFree);}
    public object AdminSimulate(EconomyConfig config,int kills,double killRate)
    {
        lock(gate)
        {
            config=EconomyRules.Normalize(config??economy.draft,economy);
            var validation=EconomyRules.Validate(config,economy,MapFree);
            if(!validation.valid)throw new GameApiException(400,"config_invalid",string.Join("; ",validation.errors));
            return EconomyRules.Simulate(config,kills,killRate);
        }
    }
    public object AdminDraft(AdminDraftRequest request,double? at=null)
    {
        double now=at??UnixNow;
        lock(gate)
        {
            EnsureRecovered(now);
            if(request==null||request.config==null||request.baseRevision!=economy.draftRevision)throw new GameApiException(409,"draft_conflict","O rascunho mudou. Atualize antes de salvar.");
            string checkpoint=CaptureCheckpoint();
            try
            {
                economy.draft=EconomyRules.Normalize(request.config,economy);economy.draftRevision++;
                AddAudit("draft",request.reason??"Rascunho atualizado",economy.active.revision,request.requestId,now);
                Commit(Array.Empty<Receipt>(),now);return AdminState(now);
            }
            catch{RecoverAfterFailure(checkpoint,now);throw;}
        }
    }
    public object AdminPublish(AdminPublishRequest request,double? at=null)
    {
        double now=at??UnixNow;
        lock(gate)
        {
            EnsureRecovered(now);
            if(request==null||!Guid.TryParse(request.requestId,out _)||string.IsNullOrWhiteSpace(request.reason)||request.reason.Length>500)
                throw new GameApiException(400,"publish_invalid","Informe motivo e requestId UUID.");
            if(economy.releases.Any(r=>r.requestId==request.requestId))return AdminState(now);
            if(request.baseRevision!=economy.draftRevision)throw new GameApiException(409,"draft_conflict","O rascunho mudou. Atualize antes de publicar.");
            var validation=EconomyRules.Validate(economy.draft,economy,MapFree);
            if(!validation.valid)throw new GameApiException(400,"config_invalid",string.Join("; ",validation.errors));
            long scheduled=request.activateAtUnixMs!=0?request.activateAtUnixMs:request.publishAtUnixMs;
            if(scheduled!=0&&(scheduled<(long)(now*1000)||scheduled>(long)((now+86400*30)*1000)))throw new GameApiException(400,"schedule_invalid","Agende entre agora e os próximos 30 dias.");
            string checkpoint=CaptureCheckpoint();
            try
            {
                var config=EconomyRules.Clone(economy.draft);
                config.revision=economy.releases.Max(r=>r.config.revision)+1;
                var release=new EconomyRelease{config=config,requestId=request.requestId,reason=request.reason,createdAtUnixMs=(long)(now*1000),activateAtUnixMs=scheduled,status=scheduled>now*1000?"scheduled":"active"};
                economy.releases=economy.releases.Append(release).ToArray();
                if(release.status=="active")Activate(release,now);
                economy.draft=EconomyRules.Clone(config);economy.draftRevision++;
                AddAudit(release.status=="scheduled"?"schedule":"publish",request.reason,config.revision,request.requestId,now);
                Commit(Array.Empty<Receipt>(),now);return AdminState(now);
            }
            catch{RecoverAfterFailure(checkpoint,now);throw;}
        }
    }
    public object AdminRollback(AdminRollbackRequest request,double? at=null)
    {
        double now=at??UnixNow;
        lock(gate)
        {
            EnsureRecovered(now);
            if(request==null||!Guid.TryParse(request.requestId,out _)||string.IsNullOrWhiteSpace(request.reason)||request.reason.Length>500)throw new GameApiException(400,"rollback_invalid","Informe versão, motivo e requestId UUID.");
            if(economy.audit.Any(a=>a.requestId==request.requestId))return AdminState(now);
            int version=request.version!=0?request.version:request.revision;
            var release=economy.releases.FirstOrDefault(r=>r.config.revision==version);
            if(release==null)throw new GameApiException(404,"release_missing","Versão não encontrada.");
            string checkpoint=CaptureCheckpoint();
            try
            {
                foreach(var pending in economy.releases.Where(r=>r.status=="scheduled"))pending.status="canceled";
                Activate(release,now);
                economy.draft=EconomyRules.Clone(release.config);economy.draftRevision++;
                if(economy.worldContentVersion>=2)MonsterCatalog.Upgrade(economy.draft);
                AddAudit("rollback",request.reason,version,request.requestId,now);
                Commit(Array.Empty<Receipt>(),now);return AdminState(now);
            }
            catch{RecoverAfterFailure(checkpoint,now);throw;}
        }
    }
    private void ActivateScheduled(double now)
    {
        foreach(var release in economy.releases.Where(r=>r.status=="scheduled"&&r.activateAtUnixMs<=now*1000).OrderBy(r=>r.activateAtUnixMs).ThenBy(r=>r.config.revision).ToArray())
        {Activate(release,now);AddAudit("activate-scheduled",release.reason,release.config.revision,release.requestId,now);}
    }
    private void Activate(EconomyRelease release,double now)
    {
        var check=EconomyRules.Validate(release.config,null,MapFree);
        if(!check.valid)throw new InvalidDataException("Publicação persistida inválida.");
        foreach(var item in release.config.items)
            if(!EquipmentCatalog.CanRegisterDefinition(item,out string reason))throw new InvalidDataException(reason);
        foreach(var previous in economy.releases.Where(r=>r.status=="active"))previous.status="historical";
        release.status="active";economy.active=EconomyRules.Clone(release.config);
        if(monsters!=null)monsters=monsters.Concat(MonsterCatalog.Resolve(release.config).Where(p=>!p.isBoss&&!monsters.Any(m=>m.id==p.id)).Select(CreateMonster)).ToArray();
    }
    private void AddAudit(string action,string reason,int revision,string requestId,double now)
    {
        economy.audit=economy.audit.Append(new EconomyAudit{action=action,reason=reason,version=revision,requestId=requestId,atUnixMs=(long)(now*1000)}).ToArray();
    }
}
public sealed class AdminDraftRequest {public int baseRevision;public EconomyConfig config;public string reason,requestId;}
public sealed class AdminPublishRequest {public int baseRevision;public string reason,requestId;public long activateAtUnixMs,publishAtUnixMs;}
public sealed class AdminRollbackRequest {public int version,revision;public string reason,requestId;}
public sealed class AdminValidateRequest {public EconomyConfig config;}
public sealed class AdminSimulateRequest {public EconomyConfig config;public int kills=1000;public double killRate=45;}
public sealed class AdminSnapshot {public EconomyConfig active,draft;public int draftRevision;public AdminReleaseSummary[] history;public EconomyAudit[] audit;public AdminMetrics metrics;public OnlineBossWindow[] bossWindows;}
public sealed class AdminReleaseSummary {public int version;public string label,reason,status;public long createdAtUnixMs,activateAtUnixMs;}
public sealed class AdminMetrics {public int players,pendingLoot,rewardedDeaths,ledgerEntries;public decimal coinsInWallets,currencyCreated,currencyConsumed;}
