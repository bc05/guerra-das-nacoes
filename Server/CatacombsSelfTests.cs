using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

internal static class CatacombsSelfTests
{
    private const double Now=16000;
    private static int assertions;
    private static void Check(bool value,string message)
    {assertions++;if(!value)throw new InvalidOperationException("CATACOMBS-TEST: "+message);}
    private static string Key()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static CollisionGrid Vale()=>new(){originX=-50,originZ=-20,cellSize=.5f,width=200,height=200,blocked=new int[40000]};

    // Authoring footprint is a fixture only. Production always loads the Unity physics export.
    private static CollisionGrid Dungeon()
    {
        var grid=new CollisionGrid {originX=CatacombsLayout.OriginX,originZ=CatacombsLayout.OriginZ,
            cellSize=CatacombsLayout.CellSize,width=CatacombsLayout.GridWidth,height=CatacombsLayout.GridHeight,
            blocked=new int[CatacombsLayout.GridWidth*CatacombsLayout.GridHeight]};
        for(int z=0;z<grid.height;z++)for(int x=0;x<grid.width;x++)
            grid.blocked[z*grid.width+x]=CatacombsLayout.IsFloor(grid.originX+(x+.5f)*grid.cellSize,grid.originZ+(z+.5f)*grid.cellSize)?0:1;
        return grid;
    }
    private static void Block(CollisionGrid grid,float x,float z,bool blocked=true)
    {grid.blocked[(int)Math.Floor((z-grid.originZ)/grid.cellSize)*grid.width+(int)Math.Floor((x-grid.originX)/grid.cellSize)]=blocked?1:0;}
    private static OnlineCommand Command(string type)=>new(){type=type,commandId=Guid.NewGuid().ToString("D")};
    private static OnlineCommand Portal(string id){var c=Command("portal");c.portalId=id;return c;}
    private static OnlineCommand Attack(int ability=0,string target=null){var c=Command("ability");c.ability=ability;c.targetId=target;return c;}

    public static void Run()
    {
        assertions=0;string folder=Path.Combine(Path.GetTempPath(),"gdn-catacombs-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            Migration(Path.Combine(folder,"migration.db"));
            IncompleteDrafts(folder);
            Portals(Path.Combine(folder,"portals.db"));
            MapIsolation(Path.Combine(folder,"isolation.db"));
            PinnedRespawnAndMapIdentity(Path.Combine(folder,"pinned-respawn.db"));
            Collision(Path.Combine(folder,"collision.db"));
            RewardsAndRecovery(Path.Combine(folder,"reward.db"));
            Console.WriteLine($"PASS: {assertions} verificações das Catacumbas: migração, portais, mapas, colisão, recompensas e recuperação.");
        }
        finally{Directory.Delete(folder,true);}
    }

    private static void IncompleteDrafts(string folder)
    {
        (string name,Action<EconomyConfig> edit)[] cases={
            ("items-null",config=>config.items=null),
            ("items-null-entry",config=>config.items=new EconomyItemConfig[]{null}),
            ("monsters-null",config=>config.monsters=null),
            ("monsters-null-entry",config=>config.monsters=new EconomyMonsterConfig[]{null}),
            ("species-null",config=>config.monsterSpecies=null),
            ("species-null-entry",config=>config.monsterSpecies=new EconomyMonsterSpecies[]{null}),
            ("spawns-null",config=>config.monsterSpawns=null),
            ("spawns-null-entry",config=>config.monsterSpawns=new EconomyMonsterSpawn[]{null}),
            ("loot-null",config=>config.lootTables=null),
            ("loot-null-entry",config=>config.lootTables=new EconomyLootTable[]{null})
        };
        foreach(var test in cases)
        {
            var store=new SaveStore(Path.Combine(folder,"draft-"+test.name+".db"));
            var world=new GameWorld(store,Vale(),Now);
            world.Connect(new(){deviceKey=Key()},Now);
            var admin=(AdminSnapshot)world.AdminState(Now);
            var draft=EconomyRules.Clone(admin.draft);test.edit(draft);
            // Exercise the real API: saving an unfinished draft is allowed independently of publication.
            world.AdminDraft(new(){baseRevision=admin.draftRevision,config=draft,reason="Rascunho incompleto de teste"},Now);
            var before=store.Load();
            string originalDraft=WireJson.Write(before.world.economy.draft);
            string originalProgress=WireJson.Write(before.players.Single().character);
            string originalActive=WireJson.Write(MonsterCatalog.Resolve(before.world.economy.active));
            var candidate=EconomyRules.Clone(before.world.economy.draft);
            bool rejected=false;try{CatacombsContent.Add(candidate);}catch(ArgumentException){rejected=true;}
            Check(rejected&&WireJson.Write(candidate)==originalDraft,"rascunho "+test.name+" é recusado sem mutação parcial");

            _=new GameWorld(store,Vale(),Now,Dungeon());var after=store.Load();
            Check(after.world.economy.worldContentVersion==3&&after.world.monsters.Count(m=>m.mapId==CatacombsLayout.MapId)==7,
                "rascunho "+test.name+" não impede instalar o conteúdo ativo válido");
            Check(WireJson.Write(after.world.economy.draft)==originalDraft&&WireJson.Write(after.players.Single().character)==originalProgress,
                "rascunho "+test.name+" e progresso original são preservados integralmente");
            Check(WireJson.Write(MonsterCatalog.Resolve(after.world.economy.active).Where(m=>m.mapId=="ruins").ToArray())==originalActive,
                "rascunho "+test.name+" não altera os perfis ativos existentes do Vale");
        }
    }

    private static void Migration(string path)
    {
        var store=new SaveStore(path);var world=new GameWorld(store,Vale(),Now);
        var legacy=world.Connect(new(){deviceKey=Key()},Now);
        Check(legacy.portals.All(portal=>portal.destinationMapId!=CatacombsLayout.MapId),"mundo anterior sem grade não anuncia portal para mapa indisponível");
        var before=store.Load();var player=before.players.Single();var stats=PlayerProgression.FromSaveData(player.character);
        stats.GrantExperience(650);stats.TryAllocate(HeroAttribute.Attack,3);stats.TryCreditCoins(321);stats.ReceiveDamage(7);player.character=stats.CaptureSaveData();
        var economy=before.world.economy;
        economy.draft.label="Ajustes particulares";economy.draft.lootTables[0].coinsMin=2;
        economy.active.npcs[0].name="Vendedor preservado";
        var old=before.world.monsters.First();old.health-=5;old.spawn=3;
        economy.rewardedDeaths=new[]{"ruins:troll-0:1"};
        string progress=WireJson.Write(player.character),oldMonsters=WireJson.Write(before.world.monsters),npcs=WireJson.Write(economy.active.npcs);
        string receipt=Guid.NewGuid().ToString("D");
        store.Save(before.players,before.world,new[]{new Receipt{playerId=player.id,commandId=receipt,requestHash="preserved",result=new(){commandId=receipt,accepted=true}}});
        var badGrid=Dungeon();Block(badGrid,0,0);
        bool rejected=false;try{_=new GameWorld(store,Vale(),Now,badGrid);}catch(InvalidDataException){rejected=true;}
        Check(rejected&&WireJson.Write(store.Load().world)==WireJson.Write(before.world),"grade com entrada bloqueada recusa instalação sem gravar mudanças");
        _=new GameWorld(store,Vale(),Now,Dungeon());var after=store.Load();
        Check(WireJson.Write(after.players.Single().character)==progress,"migração preserva todos os campos do personagem, mochila, equipamento e carteira");
        Check(WireJson.Write(after.world.monsters.Where(m=>m.mapId=="ruins").ToArray())==oldMonsters,"vida, posições e contadores dos encontros antigos são preservados");
        Check(WireJson.Write(after.world.economy.releases.Take(economy.releases.Length).Select(r=>r.config).ToArray())==WireJson.Write(economy.releases.Select(r=>r.config).ToArray()),"configurações históricas permanecem imutáveis");
        Check(WireJson.Write(after.world.economy.active.npcs)==npcs&&after.world.economy.draft.label=="Ajustes particulares"&&after.world.economy.draft.lootTables[0].coinsMin==2,"NPCs e edições administrativas do rascunho são preservados");
        Check(after.world.economy.rewardedDeaths.SequenceEqual(economy.rewardedDeaths)&&store.FindReceipt(player.id,receipt)?.result.accepted==true,"marcas de recompensa e recibos antigos são preservados");
        Check(after.world.economy.worldContentVersion==3&&after.world.monsters.Count(m=>m.mapId==CatacombsLayout.MapId)==7,"instalação adiciona exatamente sete encontros em conteúdo v3");
        int releases=after.world.economy.releases.Length;_=new GameWorld(store,Vale(),Now,Dungeon());
        Check(store.Load().world.economy.releases.Length==releases,"reiniciar não duplica conteúdo ou revisão");
    }

    private sealed class Fixture
    {
        public SaveStore store;public GameWorld world;public CollisionGrid vale=Vale(),cat=Dungeon();
        public string[] keys={Key(),Key()},tokens;public string catId,ruinsId;
        public void Restart(double now=Now)
        {
            world=new GameWorld(store,vale,now,cat);
            tokens=keys.Select(key=>world.Connect(new(){deviceKey=key,preserveExistingName=true},now).sessionToken).ToArray();
        }
        public OnlineSnapshot Send(int index,double now,params OnlineCommand[] commands)=>world.Commands(tokens[index],new(){commands=commands},now);
        public void Edit(Action<List<SavedPlayer>,SavedWorld> edit,double now=Now)
        {var saved=store.Load();edit(saved.players,saved.world);store.Save(saved.players,saved.world,Array.Empty<Receipt>());Restart(now);}
    }

    private static Fixture Prepare(string path,bool ranged=false,string map="catacombs",float x=0,float z=2.5f)
    {
        var f=new Fixture{store=new SaveStore(path)};f.world=new GameWorld(f.store,f.vale,Now,f.cat);
        for(int i=0;i<f.keys.Length;i++)f.world.Connect(new(){deviceKey=f.keys[i],displayName="Verificador cat "+i},Now);
        var saved=f.store.Load();var profiles=MonsterCatalog.Resolve(saved.world.economy.active);
        f.catId=profiles.First(p=>p.mapId==CatacombsLayout.MapId&&p.behavior==(ranged?"ranged":"melee")).id;
        f.ruinsId=profiles.First(p=>p.mapId=="ruins"&&!p.isBoss).id;
        foreach(var config in saved.world.economy.releases.Select(r=>r.config).Append(saved.world.economy.active))
        {
            foreach(var species in config.monsterSpecies)species.aggroRadius=0;
            foreach(var legacy in config.monsters)legacy.aggroRadius=0;
            foreach(var post in config.monsterSpawns.Where(post=>post.id==f.catId||post.id==f.ruinsId)){post.x=0;post.z=4;}
        }
        foreach(var monster in saved.world.monsters.Where(m=>m.id==f.catId||m.id==f.ruinsId)){monster.x=0;monster.z=4;}
        foreach(var player in saved.players){player.mapId=player.displayName.EndsWith("0")?map:"ruins";player.x=x;player.z=z;player.yaw=0;}
        f.store.Save(saved.players,saved.world,Array.Empty<Receipt>());f.Restart();return f;
    }

    private static void Portals(string path)
    {
        var f=Prepare(path,map:"ruins",z:0);
        var far=f.Send(0,Now+.1,Portal("portal-catacombs"));
        Check(!far.results.Single().accepted&&far.mapId=="ruins","portal distante não altera mapa ou posição");
        f.Edit((players,world)=>{var p=players.Single(p=>p.displayName.EndsWith("0"));p.x=0;p.z=19;});
        Block(f.cat,0,0);var blocked=f.Send(0,Now+.2,Portal("portal-catacombs"));
        Check(!blocked.results.Single().accepted&&blocked.self.z==19&&blocked.mapId=="ruins","destino bloqueado conserva posição e mapa de origem");Block(f.cat,0,0,false);
        var pending=f.Send(0,Now+.21,Attack(),Portal("portal-catacombs"));
        Check(pending.results[0].accepted&&!pending.results[1].accepted&&pending.mapId=="ruins","golpe pendente impede trocar o mapa durante sua resolução");
        f.Edit((players,world)=>{
            players.Single(p=>p.displayName.EndsWith("0")).strike=null;
            var m=world.monsters.Single(m=>m.id==f.ruinsId);m.targetId=null;m.z=19;
            foreach(var config in world.economy.releases.Select(r=>r.config).Append(world.economy.active))
                foreach(var spawn in config.monsterSpawns.Where(s=>s.id==m.id))
                {spawn.z=19;config.monsterSpecies.Single(s=>s.id==spawn.speciesId).aggroRadius=5;}
        });
        var fighting=f.Send(0,Now+.3,Portal("portal-catacombs"));
        Check(!fighting.results.Single().accepted&&fighting.mapId=="ruins","monstro perseguindo bloqueia viagem entre mapas");
        f.Edit((players,world)=>{
            foreach(var m in world.monsters){m.targetId=null;m.windupUntil=0;m.nextAttackAt=0;}
            foreach(var config in world.economy.releases.Select(r=>r.config).Append(world.economy.active))
                foreach(var species in config.monsterSpecies)species.aggroRadius=0;
        });
        var enter=Portal("portal-catacombs");var arrived=f.Send(0,Now+.4,enter);
        Check(arrived.results.Single().accepted&&arrived.mapId==CatacombsLayout.MapId&&arrived.self.x==0&&arrived.self.z==0,"entrada usa destino autoritativo das Catacumbas");
        var replay=f.Send(0,Now+.5,enter);
        Check(replay.results.Single().accepted&&replay.mapId==CatacombsLayout.MapId&&replay.events.Count(e=>e.kind=="travel")==1,"reenvio do comando não executa segunda viagem");
        var back=f.Send(0,Now+.6,Portal("portal-catacombs-exit"));
        Check(back.results.Single().accepted&&back.mapId=="ruins"&&back.self.z==17,"saída próxima devolve ao Vale sem tocar atributos");
    }

    private static void MapIsolation(string path)
    {
        var f=Prepare(path);
        f.Edit((players,world)=>{foreach(var m in world.monsters)m.nextAttackAt=Now+100;});
        var cat=f.Send(0,Now+.01);var ruins=f.Send(1,Now+.02);
        Check(cat.monsters.All(m=>m.mapId==CatacombsLayout.MapId)&&ruins.monsters.All(m=>m.mapId=="ruins")&&cat.players.Length==0&&ruins.players.Length==0,"snapshots isolam encontros e jogadores em coordenadas coincidentes");
        var foreign=f.Send(0,Now+.1,Attack(target:f.ruinsId));
        Check(!foreign.results.Single().accepted&&foreign.self.mana==100,"alvo de outro mapa é recusado antes de consumir recursos");
        int before=f.store.Load().world.monsters.Single(m=>m.id==f.ruinsId).health;
        var cast=f.Send(0,Now+.2,Attack());f.Send(0,Now+.4);
        Check(cast.results.Single().accepted&&f.store.Load().world.monsters.Single(m=>m.id==f.ruinsId).health==before,"ataque livre não atinge monstro de outro mapa nas mesmas coordenadas");
        var heavy=f.Send(0,Now+1,Attack(1));var hit=f.Send(0,Now+1.2);var unseen=f.Send(1,Now+1.21);
        Check(heavy.results.Single().accepted&&f.store.Load().world.monsters.Single(m=>m.id==f.ruinsId).health==before&&hit.events.Any(e=>e.kind=="hit")&&unseen.events.All(e=>e.mapId=="ruins")&&!unseen.events.Any(e=>e.kind=="hit"),"golpe pesado e eventos de dano permanecem no mapa de origem");
        string progress=WireJson.Write(hit.character);f.Restart(Now+1.3);var reconnect=f.Send(0,Now+1.31);
        Check(reconnect.mapId==CatacombsLayout.MapId&&WireJson.Write(reconnect.character)==progress&&reconnect.monsters.All(m=>m.mapId==CatacombsLayout.MapId),"reinício e reconexão preservam personagem e mapa subterrâneo");
        string saved=WireJson.Write(f.store.Load());bool rejected=false;try{_=new GameWorld(f.store,f.vale,Now+1.4);}catch(InvalidDataException){rejected=true;}
        Check(rejected&&WireJson.Write(f.store.Load())==saved,"mundo instalado recusa grade ausente sem reset ou gravação");
    }

    private static void Collision(string path)
    {
        var f=Prepare(path,ranged:true,z:0);
        f.Edit((players,world)=>{
            foreach(var config in world.economy.releases.Select(r=>r.config).Append(world.economy.active))
            {
                var spawn=config.monsterSpawns.FirstOrDefault(s=>s.id==f.catId);
                if(spawn!=null)config.monsterSpecies.Single(s=>s.id==spawn.speciesId).aggroRadius=5;
            }
        });
        var warning=f.Send(0,Now+.1);var caster=warning.monsters.Single(m=>m.id==f.catId);
        Check(caster.behavior=="ranged"&&caster.windupUntil>Now+.1&&caster.targetId==warning.playerId,"conjurador prepara ataque à distância somente contra jogador do seu mapa");
        Block(f.cat,0,2);var cover=f.Send(0,caster.windupUntil+.01);
        Check(cover.character.health==warning.character.health&&cover.events.Any(e=>e.kind=="monsterStrike"&&e.amount==0),"parede das Catacumbas bloqueia disparo preparado mesmo com Vale aberto");
        var other=f.Send(1,caster.windupUntil+.02);
        Check(other.character.health==100&&!other.events.Any(e=>e.kind=="monsterStrike"),"disparo não causa dano ou evento ao jogador no Vale coincidente");
        f.Edit((players,world)=>{var p=players.Single(p=>p.displayName.EndsWith("0"));p.z=1.5f;},Now+3);
        var move=Command("move");move.x=0;move.z=2.5f;move.running=true;
        var stopped=f.Send(0,Now+3.3,move);
        Check(!stopped.results.Single().accepted&&stopped.self.z==1.5f,"movimento não cruza parede local usando a grade aberta do Vale");
        var targeted=f.Send(0,Now+3.4,Attack(target:f.catId));
        Check(!targeted.results.Single().accepted&&targeted.self.mana==stopped.self.mana,"colisão do mapa bloqueia ataque direcionado através da parede");
    }

    private static void PinnedRespawnAndMapIdentity(string path)
    {
        var f=Prepare(path);
        f.Edit((players,world)=>world.monsters.Single(m=>m.id==f.catId).health=1);
        var saved=f.store.Load();var old=saved.world.monsters.Single(m=>m.id==f.catId);
        int oldRevision=old.configVersion,oldSpawn=old.spawn;
        var oldProfile=MonsterCatalog.Resolve(saved.world.economy.releases.Single(r=>r.config.revision==oldRevision).config).Single(m=>m.id==f.catId);
        var admin=(AdminSnapshot)f.world.AdminState(Now);
        var moved=EconomyRules.Clone(admin.active);
        moved.monsterSpawns.Single(spawn=>spawn.id==f.ruinsId).mapId=CatacombsLayout.MapId;
        Check(!f.world.AdminValidate(moved).valid,"identidade de nascimento existente não pode mudar de mapa no rascunho publicado");
        admin=(AdminSnapshot)f.world.AdminDraft(new(){baseRevision=admin.draftRevision,config=moved,reason="Troca de mapa recusada em teste"},Now+.01);
        string activeBefore=WireJson.Write(f.store.Load().world.economy.active);
        string encountersBefore=WireJson.Write(f.store.Load().world.monsters);
        bool rejected=false;
        try{f.world.AdminPublish(new(){baseRevision=admin.draftRevision,reason="Troca de mapa deve falhar",requestId=Guid.NewGuid().ToString("D")},Now+.02);}
        catch(GameApiException error)when(error.Code=="config_invalid"){rejected=true;}
        saved=f.store.Load();
        Check(rejected&&WireJson.Write(saved.world.economy.active)==activeBefore&&WireJson.Write(saved.world.monsters)==encountersBefore,
            "publicação que move identidade entre mapas é recusada sem alterar configuração ativa ou encontros");

        var changed=EconomyRules.Clone(admin.active);
        var spawn=changed.monsterSpawns.Single(post=>post.id==f.catId);
        spawn.speciesId="cat-guardian";spawn.x=0;spawn.z=6;
        Check(f.world.AdminValidate(changed).valid,"espécie e posição podem mudar para o próximo nascimento no mesmo mapa");
        admin=(AdminSnapshot)f.world.AdminDraft(new(){baseRevision=admin.draftRevision,config=changed,reason="Próximo nascimento no mesmo mapa"},Now+.03);
        admin=(AdminSnapshot)f.world.AdminPublish(new(){baseRevision=admin.draftRevision,reason="Novo perfil somente ao renascer",requestId=Guid.NewGuid().ToString("D")},Now+.04);
        var pinned=f.Send(0,Now+.05).monsters.Single(m=>m.id==f.catId);
        saved=f.store.Load();old=saved.world.monsters.Single(m=>m.id==f.catId);
        Check(old.configVersion==oldRevision&&old.mapId==CatacombsLayout.MapId&&old.health==1&&old.z==4&&
            pinned.maxHealth==oldProfile.health&&pinned.level==oldProfile.level,
            "vida atual conserva revisão, mapa, ferimento, posição e espécie após publicação");

        var cast=f.Send(0,Now+.1,Attack(target:f.catId));f.Send(0,Now+.3);
        saved=f.store.Load();var dead=saved.world.monsters.Single(m=>m.id==f.catId);
        Check(cast.results.Single().accepted&&dead.health==0&&dead.mapId==CatacombsLayout.MapId&&dead.configVersion==oldRevision&&
            Math.Abs(dead.respawnAt-(Now+.3+oldProfile.respawnDelay))<.001,
            "morte conserva mapa e prazo de renascimento da revisão original");
        double afterRespawn=dead.respawnAt+.01;
        f.world.Tick(afterRespawn);
        var current=MonsterCatalog.Resolve(admin.active).Single(m=>m.id==f.catId);
        var cat=f.Send(0,afterRespawn+.01);var ruins=f.Send(1,afterRespawn+.02);
        saved=f.store.Load();var reborn=saved.world.monsters.Single(m=>m.id==f.catId);
        Check(reborn.configVersion==admin.active.revision&&reborn.mapId==current.mapId&&reborn.mapId==CatacombsLayout.MapId&&
            reborn.x==current.x&&reborn.z==current.z&&reborn.health==current.health&&reborn.spawn==oldSpawn+1,
            "renascimento adota revisão, mapa, coordenadas e vida do perfil publicado");
        Check(cat.monsters.Any(m=>m.id==f.catId&&m.speciesId=="cat-guardian")&&cat.events.Any(e=>e.kind=="respawn"&&e.sourceId==f.catId&&e.mapId==CatacombsLayout.MapId)&&
            ruins.monsters.All(m=>m.id!=f.catId)&&ruins.events.All(e=>e.sourceId!=f.catId),
            "snapshot e evento de renascimento ficam somente no mapa do encontro");
        string progress=WireJson.Write(cat.character);
        f.Restart(afterRespawn+.1);var restored=f.Send(0,afterRespawn+.11);
        var persisted=restored.monsters.Single(m=>m.id==f.catId);
        Check(persisted.mapId==CatacombsLayout.MapId&&persisted.spawn==reborn.spawn&&persisted.health==current.health&&
            WireJson.Write(restored.character)==progress&&!restored.events.Any(e=>e.kind=="respawn"&&e.sourceId==f.catId),
            "reinício valida o encontro renascido, conserva progresso e não repete seu evento");
    }

    private static void RewardsAndRecovery(string path)
    {
        var f=Prepare(path);
        f.Edit((players,world)=>world.monsters.Single(m=>m.id==f.catId).health=1);
        var attack=Attack(target:f.catId);var cast=f.Send(0,Now+.1,attack);var killed=f.Send(0,Now+.3);
        var saved=f.store.Load();var dead=saved.world.monsters.Single(m=>m.id==f.catId);
        string death=CatacombsLayout.MapId+":"+f.catId+":"+dead.spawn;
        Check(cast.results.Single().accepted&&dead.health==0&&saved.world.economy.rewardedDeaths.Count(id=>id==death)==1&&saved.world.economy.ledger.Count(l=>l.reference==death)==1,"morte subterrânea concede uma recompensa com marca durável");
        string progress=WireJson.Write(killed.character);f.Send(0,Now+.4,attack);f.Restart(Now+.5);
        Check(WireJson.Write(f.Send(0,Now+.51).character)==progress&&f.store.Load().world.economy.ledger.Count(l=>l.reference==death)==1,"replay e reinício não repetem XP moedas ou saque da morte");
        f.Edit((players,world)=>{var p=players.Single(p=>p.displayName.EndsWith("0"));p.character.health=0;},Now+1);
        var before=f.store.Load().players.Single(p=>p.displayName.EndsWith("0")).character;
        var respawn=f.Send(0,Now+1.1,Command("respawn"));
        var expected=WireJson.Read<CharacterSaveData>(WireJson.Write(before));expected.health=respawn.character.health;
        Check(respawn.results.Single().accepted&&respawn.mapId=="city"&&respawn.self.x==0&&respawn.self.z==0&&respawn.character.health==respawn.self.maxHealth&&WireJson.Write(respawn.character)==WireJson.Write(expected),"derrota nas Catacumbas renasce na cidade preservando todos os demais campos do progresso");
    }
}
