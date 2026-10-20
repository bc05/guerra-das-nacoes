using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

internal static class WorldExpansionSelfTests
{
    private static int assertions;
    private static void Check(bool ok,string message){assertions++;if(!ok)throw new InvalidOperationException("FRONTIER-TEST: "+message);}
    private static CollisionGrid Small()=>new(){originX=-18,originZ=-18,cellSize=.5f,width=72,height=72,blocked=new int[72*72]};
    private static CollisionGrid Large()=>new(){originX=-50,originZ=-20,cellSize=.5f,width=200,height=200,blocked=new int[200*200]};
    private static string Key()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static OnlineCommand Ability(string target=null,int skill=0)=>new(){commandId=Guid.NewGuid().ToString("D"),type="ability",targetId=target,ability=skill};
    private static OnlineSnapshot Send(GameWorld world,string token,double now,params OnlineCommand[] commands)=>world.Commands(token,new(){commands=commands},now);
    public static void Run()
    {
        assertions=0;
        string directory=Path.Combine(Path.GetTempPath(),"gdn-frontier-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            TestAdditive(Path.Combine(directory,"upgrade.db"));
            TestTargetAndAggro(Path.Combine(directory,"target.db"));
            TestScheduleAndRewards(Path.Combine(directory,"boss.db"));
            TestValidation(Path.Combine(directory,"validate.db"));
            TestDisabledSchedule(Path.Combine(directory,"disabled.db"));
            Console.WriteLine($"PASS: {assertions} verificações de expansão, alvo, agressão, calendário de chefão e recompensa transacional.");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static void TestAdditive(string path)
    {
        const double now=10000;
        var store=new SaveStore(path);var world=new GameWorld(store,Small(),now);string key=Key();world.Connect(new(){deviceKey=key},now);
        var loaded=store.Load();var player=loaded.players.Single();var stats=PlayerProgression.FromSaveData(player.character);
        stats.GrantExperience(200);stats.TryAllocate(HeroAttribute.Attack,3);stats.TryCreditCoins(123);player.character=stats.CaptureSaveData();
        string progress=WireJson.Write(player.character);
        loaded.world.economy.active.monsters[0].damage=13;loaded.world.economy.releases[0].config.monsters[0].damage=13;
        loaded.world.economy.draft.label="Rascunho personalizado preservado";loaded.world.economy.draft.lootTables[0].coinsMin=3;
        store.Save(loaded.players,loaded.world,Array.Empty<Receipt>());
        world=new GameWorld(store,Large(),now+1);var result=world.Connect(new(){deviceKey=key},now+1);
        var admin=(AdminSnapshot)world.AdminState(now+1);loaded=store.Load();
        Check(WireJson.Write(result.character)==progress,"expansão preserva nível XP pontos moedas mochila e equipamentos");
        Check(MonsterCatalog.Resolve(admin.active)[0].damage==13&&admin.draft.label=="Rascunho personalizado preservado"&&admin.draft.lootTables[0].coinsMin==3,"publicação aditiva preserva configurações e rascunho personalizados");
        Check(MonsterCatalog.Resolve(admin.active).Length==10&&admin.active.bossSchedules.Length==1&&admin.history.Length==3,"expansão instala seis perfis em nova versão");
        Check(result.monsters.Length==9&&!result.monsters.Any(m=>m.isBoss),"chefão não nasce fora de sua agenda");
        Check(admin.active.items.Count(i=>i.rarity=="legendary"&&i.definitionId.StartsWith("sovereign-"))==3,"três relíquias lendárias revisionadas sem alterar itens antigos");
        Check(Large().Free(29,40)&&!Large().Free(51,40),"grade usa limites reais do mapa ampliado");
        world=new GameWorld(store,Large(),now+2);admin=(AdminSnapshot)world.AdminState(now+2);
        Check(admin.history.Length==3&&MonsterCatalog.Resolve(admin.active).Length==10,"reiniciar não duplica expansão ou perfis");
    }
    private static void TestTargetAndAggro(string path)
    {
        var store=new SaveStore(path);var grid=Small();var world=new GameWorld(store,grid,20000);string key=Key();world.Connect(new(){deviceKey=key},20000);
        var loaded=store.Load();var player=loaded.players.Single();player.x=-2.6f;player.z=1.8f;player.yaw=180;
        foreach(var config in new[]{loaded.world.economy.active,loaded.world.economy.releases[0].config})foreach(var profile in config.monsters)profile.aggroRadius=0;
        store.Save(loaded.players,loaded.world,Array.Empty<Receipt>());world=new GameWorld(store,grid,20001);var current=world.Connect(new(){deviceKey=key},20001);string token=current.sessionToken;
        var target=current.monsters.Single(m=>m.id=="troll-0");int hp=target.health;
        var cast=Send(world,token,20001.1,Ability(target.id));
        Check(cast.results.Single().accepted&&Math.Abs(cast.self.yaw)<.01,"alvo válido gira o herói autoritativamente sem teleporte");
        current=Send(world,token,20001.3);
        Check(current.monsters.Single(m=>m.id==target.id).health<hp,"ataque direcionado causa dano ao alvo");
        var invalid=Send(world,token,20002,Ability("forged-monster"));
        Check(!invalid.results.Single().accepted&&invalid.self.mana==100,"alvo forjado recusado sem mana ou recarga");
        int wallColumn=(int)Math.Floor((-2.6-grid.originX)/grid.cellSize),wallRow=(int)Math.Floor((2.5-grid.originZ)/grid.cellSize);
        grid.blocked[wallRow*grid.width+wallColumn]=1;
        var blocked=Send(world,token,20002.05,Ability(target.id));
        Check(!blocked.results.Single().accepted,"alvo próximo atrás de obstáculo é recusado");
        grid.blocked[wallRow*grid.width+wallColumn]=0;
        var distant=Send(world,token,20002.1,Ability("sentinel-0",1));
        Check(!distant.results.Single().accepted&&distant.self.mana==100&&distant.self.cooldownUntil[1]==0,"alvo distante não consome habilidade");
        loaded=store.Load();loaded.players.Single().mapId="city";loaded.players.Single().x=0;loaded.players.Single().z=0;
        store.Save(loaded.players,loaded.world,Array.Empty<Receipt>());world=new GameWorld(store,grid,20003);current=world.Connect(new(){deviceKey=key},20003);token=current.sessionToken;
        Check(!Send(world,token,20003.1,Ability(target.id)).results.Single().accepted,"alvo de outro mapa é recusado");
        // Fresh position acquires a live nearby hero without a preliminary player hit.
        loaded=store.Load();player=loaded.players.Single();player.mapId="ruins";player.x=-2.6f;player.z=1.8f;
        foreach(var config in new[]{loaded.world.economy.active,loaded.world.economy.releases[0].config})config.monsters[0].aggroRadius=4;
        var monster=loaded.world.monsters.Single(m=>m.id==target.id);monster.x=-2.6f;monster.z=3.8f;monster.targetId=null;monster.returning=false;monster.windupUntil=0;
        store.Save(loaded.players,loaded.world,Array.Empty<Receipt>());world=new GameWorld(store,grid,20004);current=world.Connect(new(){deviceKey=key},20004);token=current.sessionToken;current=Send(world,token,20004.1);
        Check(current.monsters.Single(m=>m.id==target.id).targetId==current.playerId,"proximidade sozinha adquire alvo e inicia perseguição");
        loaded=store.Load();player=loaded.players.Single();player.x=-2.6f;player.z=-8;
        store.Save(loaded.players,loaded.world,Array.Empty<Receipt>());world=new GameWorld(store,grid,20005);current=world.Connect(new(){deviceKey=key},20005);token=current.sessionToken;
        Check(current.monsters.Single(m=>m.id==target.id).returning,"sair do território encerra perseguição e devolve monstro ao nascimento");
    }
    private static void TestScheduleAndRewards(string path)
    {
        double start=DateTimeOffset.Parse("2026-10-19T23:00:00Z").ToUnixTimeSeconds(); // Monday, 20:00 São Paulo.
        var store=new SaveStore(path);var world=new GameWorld(store,Large(),start-60);
        Check(store.Load().world.monsters.All(m=>m.id!="boss-grond"),"antes das 20h locais não existe chefão");
        store.FailNextSaveForTests=true;
        try{world.Tick(start);}catch(IOException){}
        Check(store.Load().world.economy.bossSpawnWindows.Length==0,"falha antes de commit não consome janela de nascimento");
        world.Tick(start+.1);var saved=store.Load();var boss=saved.world.monsters.Single(m=>m.id=="boss-grond");
        Check(boss.health==12000&&boss.expiresAt==start+3600&&saved.world.economy.bossSpawnWindows.Length==1,"tick sem jogadores cria um chefão no fuso e prazo configurados");
        boss.health=8000;store.Save(saved.players,saved.world,Array.Empty<Receipt>());
        world=new GameWorld(store,Large(),start+1);saved=store.Load();boss=saved.world.monsters.Single(m=>m.id=="boss-grond");
        Check(boss.health==8000&&saved.world.economy.bossSpawnWindows.Length==1,"reinício preserva ferimentos e não duplica ocorrência");
        string key=Key();world.Connect(new(){deviceKey=key},start+1);saved=store.Load();var player=saved.players.Single();player.x=0;player.z=59;player.yaw=180;
        boss=saved.world.monsters.Single(m=>m.id=="boss-grond");boss.health=1;
        foreach(var config in new[]{saved.world.economy.active,saved.world.economy.releases.Single(r=>r.config.revision==boss.configVersion).config})
        {
            var table=config.lootTables.Single(t=>t.id=="loot-grond");table.coinsMin=table.coinsMax=500;table.scrapChancePpm=0;
            table.groups=new[]{new EconomyLootGroup{id="guaranteed-fixture",chancePpm=1000000,entries=new[]{new EconomyLootEntry{definitionId="sovereign-sword",weight=1}}}};
        }
        store.Save(saved.players,saved.world,Array.Empty<Receipt>());world=new GameWorld(store,Large(),start+2);var connected=world.Connect(new(){deviceKey=key},start+2);
        Check(connected.bossWindows.Single().active&&connected.bossWindows.Single().endsAt==start+3600,"snapshot informa janela ativa e encerramento");
        var bossView=connected.monsters.Single(m=>m.id=="boss-grond");
        Check(bossView.attackRange==2.5f&&bossView.moveSpeed==2.5f&&bossView.aggroRadius==12&&bossView.leashRadius==26,
            "snapshot do chefão transmite alcance movimento agressão e território autoritativos para animação e efeitos");
        var normalView=connected.monsters.Single(m=>m.id=="troll-0");
        Check(normalView.attackRange==1.65f&&normalView.moveSpeed==2.25f&&normalView.aggroRadius==4&&normalView.leashRadius==8,
            "snapshot normal preserva alcance e parâmetros legados de apresentação");
        var attack=Ability("boss-grond");Check(Send(world,connected.sessionToken,start+2.1,attack).results.Single().accepted,"chefão pode ser alvo de ataque normal");
        store.FailAfterCommitForTests=true;try{Send(world,connected.sessionToken,start+2.3);}catch(IOException){}
        var reward=Send(world,connected.sessionToken,start+2.4);
        Check(reward.character.coins==500&&reward.loot.Length==1&&reward.loot[0].definitionId=="sovereign-sword","morte e moeda XP item raro são atômicos mesmo após ACK ambíguo");
        Check(store.Load().world.economy.rewardedDeaths.Length==1&&store.Load().world.economy.bossSpawnWindows.Single().outcome=="defeated","morte e ocorrência têm recibos duráveis");
        Send(world,connected.sessionToken,start+3,attack);
        var admin=(AdminSnapshot)world.AdminState(start+4);
        int expansionRevision=admin.active.revision;
        world.AdminRollback(new(){version=1,reason="Teste de retirada de agenda",requestId=Guid.NewGuid().ToString("D")},start+4);
        world.AdminRollback(new(){version=expansionRevision,reason="Teste de reativação de agenda",requestId=Guid.NewGuid().ToString("D")},start+5);
        world.Tick(start+6);
        Check(store.Load().world.monsters.Single(m=>m.id=="boss-grond").health==0&&store.Load().world.economy.bossSpawnWindows.Length==1,"reativar publicação na mesma janela preserva recibo e não recria chefão");
        world=new GameWorld(store,Large(),start+10);world.Tick(start+20);
        Check(store.Load().world.monsters.Single(m=>m.id=="boss-grond").health==0&&store.Load().world.economy.rewardedDeaths.Length==1,"replay e reinício na mesma janela não ressuscitam nem recompensam duas vezes");
        world.Tick(start+86400);saved=store.Load();boss=saved.world.monsters.Single(m=>m.id=="boss-grond");
        Check(boss.health==12000&&boss.spawn==2&&saved.world.economy.bossSpawnWindows.Length==2,"próximo dia permite somente um novo encontro com identidade nova");
        world.Tick(start+86400+3600);saved=store.Load();
        Check(saved.world.monsters.Single(m=>m.id=="boss-grond").health==0&&saved.world.economy.bossSpawnWindows.Last().outcome=="expired"&&saved.world.economy.rewardedDeaths.Length==1,"fim de janela remove chefão sem criar morte recompensada");
        world=new GameWorld(store,Large(),start+86400+3601);
        Check(store.Load().world.monsters.Single(m=>m.id=="boss-grond").health==0,"reinício depois do prazo não recria chefão expirado");
    }
    private static void TestDisabledSchedule(string path)
    {
        double start=DateTimeOffset.Parse("2026-10-19T23:00:00Z").ToUnixTimeSeconds();
        var store=new SaveStore(path);var world=new GameWorld(store,Large(),start+10);
        var state=(AdminSnapshot)world.AdminState(start+10);var draft=EconomyRules.Clone(state.draft);
        draft.bossSchedules.Single().enabled=false;
        state=(AdminSnapshot)world.AdminDraft(new(){baseRevision=state.draftRevision,config=draft,reason="Desativar próximos nascimentos"},start+11);
        world.AdminPublish(new(){baseRevision=state.draftRevision,reason="Agenda desativada por teste",requestId=Guid.NewGuid().ToString("D")},start+12);
        state=(AdminSnapshot)world.AdminState(start+12);
        Check(state.bossWindows.Length==1&&state.bossWindows[0].active&&state.bossWindows[0].nextSpawnAt==0&&state.bossWindows[0].endsAt==start+3600,
            "desativar agenda mantém encontro ativo e prazo original sem anunciar novo nascimento");
        world.Tick(start+3600);
        state=(AdminSnapshot)world.AdminState(start+3600);
        Check(state.bossWindows.Length==0,"agenda desativada sem chefão vivo não anuncia nascimento em zero minutos");
        world=new GameWorld(store,Large(),start+3601);
        var connected=world.Connect(new(){deviceKey=Key(),displayName="Agenda desativada"},start+3601);
        Check(connected.bossWindows.Length==0,"snapshot do jogador e reinício omitem anúncio de agenda desativada");
    }

    private static void TestValidation(string path)
    {
        var world=new GameWorld(new SaveStore(path),Large(),30000);var admin=(AdminSnapshot)world.AdminState(30000);var config=EconomyRules.Clone(admin.draft);
        config.bossSchedules[0].timeZone="Invalid/Zone";Check(!world.AdminValidate(config).valid,"fuso desconhecido é recusado");
        config=EconomyRules.Clone(admin.draft);config.bossSchedules[0].startTime="25:99";Check(!world.AdminValidate(config).valid,"horário inválido recusado");
        config=EconomyRules.Clone(admin.draft);config.bossSchedules[0].daysOfWeek=new[]{1,1};Check(!world.AdminValidate(config).valid,"dias duplicados recusados");
        config=EconomyRules.Clone(admin.draft);config.bossSchedules[0].durationMinutes=1440;Check(!world.AdminValidate(config).valid,"janela ilimitada é recusada");
        config=EconomyRules.Clone(admin.draft);config.bossSchedules[0].enabled=false;Check(world.AdminValidate(config).valid,"administrador pode desativar a agenda sem remover perfil");
        config=EconomyRules.Clone(admin.draft);config.bossSchedules[0].daysOfWeek=new[]{2};
        double monday=DateTimeOffset.Parse("2026-10-19T23:00:00Z").ToUnixTimeSeconds();
        Check(!BossScheduleRules.Occurrences(config.bossSchedules[0],monday,false).Any(o=>o.StartsAt<=monday&&monday<o.EndsAt),"dia fora da agenda não abre ocorrência");
    }
}
