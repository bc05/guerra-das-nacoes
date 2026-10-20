using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace GuerraDasNacoes.Server;

internal static class MonsterCatalogMigrationSelfTests
{
    private static int assertions;
    private static void Check(bool value,string message)
    { assertions++; if(!value)throw new InvalidOperationException("CATALOG-TEST: "+message); }
    private static CollisionGrid Small()=>new(){originX=-18,originZ=-18,cellSize=.5f,width=72,height=72,blocked=new int[72*72]};
    private static CollisionGrid Large()=>new(){originX=-50,originZ=-20,cellSize=.5f,width=200,height=200,blocked=new int[200*200]};

    public static void Run()
    {
        assertions=0;
        string directory=Path.Combine(Path.GetTempPath(),"gdn-catalog-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestMigration(Path.Combine(directory,"legacy.db"),false);
            TestMigration(Path.Combine(directory,"invalid-draft.db"),true);
            TestSpeciesSharing();
            var fresh=new GameWorld(new SaveStore(Path.Combine(directory,"fresh.db")),Large(),10000);
            var freshAdmin=(AdminSnapshot)fresh.AdminState(10000);
            Check(MonsterCatalog.Resolve(freshAdmin.draft).Single(m=>m.id=="orc-shaman-0").behavior=="ranged","rascunho sem edições acompanha os comportamentos da nova configuração ativa");
            Console.WriteLine($"PASS: {assertions} verificações de catálogo, migração sem reset, histórico e rascunho.");
        }
        finally{Directory.Delete(directory,true);}
    }

    private static void TestMigration(string path,bool invalidDraft)
    {
        const double now=10000;
        var store=new SaveStore(path);
        var original=new GameWorld(store,Small(),now);
        var session=original.Connect(new(){deviceKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))},now);
        var loaded=store.Load();
        var player=loaded.players.Single();
        var stats=PlayerProgression.FromSaveData(player.character);
        stats.GrantExperience(600);stats.TryCreditCoins(123);stats.TryAllocate(HeroAttribute.Attack,3);
        player.character=stats.CaptureSaveData();
        string progress=WireJson.Write(player.character);
        var economy=loaded.world.economy;
        var frontier=EconomyRules.Clone(economy.active);
        EconomyDefaults.AddFrontierContent(frontier);
        frontier.revision=2;frontier.monsters[0].damage=13;
        economy.releases[0].status="historical";
        economy.releases=economy.releases.Append(new(){config=EconomyRules.Clone(frontier),reason="Fronteira legada"}).ToArray();
        economy.active=frontier;economy.draft=EconomyRules.Clone(frontier);
        economy.draft.label="Rascunho em edição";economy.draft.lootTables[0].coinsMin=3;
        if(invalidDraft)economy.draft.monsters=null;
        string originalDraft=WireJson.Write(economy.draft);
        economy.worldContentVersion=1;
        economy.rewardedDeaths=new[]{"ruins:troll-0:1"};
        loaded.world.monsters=frontier.monsters.Where(p=>!p.isBoss).Select(p=>new SavedMonster {
            id=p.id,definitionId=p.id,configVersion=2,mapId=p.mapId,x=p.x,z=p.z,health=p.health,spawn=2
        }).ToArray();
        var alive=loaded.world.monsters.Single(m=>m.id=="orc-shaman-0");
        alive.health-=17;alive.windupUntil=now+3;alive.nextAttackAt=now+4;alive.targetId=player.id;
        var dead=loaded.world.monsters.Single(m=>m.id=="troll-0");
        dead.health=0;dead.respawnAt=now+6;
        string encounters=WireJson.Write(loaded.world.monsters);
        string receiptId=Guid.NewGuid().ToString("D");
        store.Save(loaded.players,loaded.world,new[]{new Receipt{playerId=player.id,commandId=receiptId,requestHash="migration-test",result=new(){commandId=receiptId,accepted=true}}});
        var migrated=new GameWorld(store,Large(),now);
        var after=store.Load();
        Check(WireJson.Write(after.players.Single().character)==progress,"nível XP moedas pontos mochila e equipamentos preservados");
        Check(WireJson.Write(after.world.monsters)==encounters,"vida morte contador posição e preparo existentes preservados");
        Check(WireJson.Write(after.world.economy.releases.Take(2).Select(r=>r.config).ToArray())==
            WireJson.Write(economy.releases.Select(r=>r.config).ToArray()),"revisões anteriores imutáveis");
        Check(after.world.economy.rewardedDeaths.SequenceEqual(economy.rewardedDeaths)&&store.FindReceipt(player.id,receiptId)?.result.accepted==true,"mortes recompensadas e recibos de comando preservados");
        Check(after.world.economy.worldContentVersion==2&&after.world.economy.active.monsters.Length==0&&after.world.economy.active.monsterSpawns.Length==10,"catálogo separado instalado uma vez");
        Check(MonsterCatalog.Resolve(after.world.economy.active).Single(m=>m.id=="orc-shaman-0").behavior=="ranged","novo nascimento do xamã terá ataque à distância");
        Check(MonsterCatalog.Resolve(after.world.economy.releases.Single(r=>r.config.revision==alive.configVersion).config).Single(m=>m.id==alive.id).behavior=="melee","encontro vivo mantém seu comportamento revisionado");
        if(invalidDraft)Check(WireJson.Write(after.world.economy.draft)==originalDraft,"rascunho incompleto preservado sem bloquear inicialização");
        else Check(after.world.economy.draft.label=="Rascunho em edição"&&after.world.economy.draft.lootTables[0].coinsMin==3&&MonsterCatalog.Resolve(after.world.economy.draft)[0].damage==13,"edições administrativas do rascunho preservadas");
        int count=after.world.economy.releases.Length;
        _=new GameWorld(store,Large(),now);
        Check(store.Load().world.economy.releases.Length==count,"reinício não repete instalação");
        var waiting=store.Load();
        var nextShaman=waiting.world.monsters.Single(m=>m.id=="orc-shaman-0");
        int previousSpawn=nextShaman.spawn;
        nextShaman.health=0;nextShaman.respawnAt=now+1;nextShaman.windupUntil=0;nextShaman.targetId=null;
        store.Save(waiting.players,waiting.world,Array.Empty<Receipt>());
        var respawning=new GameWorld(store,Large(),now);
        var next=respawning.Connect(new(){deviceKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),displayName="Teste renascimento"},now+2);
        var born=next.monsters.Single(m=>m.id=="orc-shaman-0");
        Check(born.behavior=="ranged"&&born.spawn==previousSpawn+1&&born.health==born.maxHealth,"renascimento adota nova espécie/comportamento sem reutilizar a vida anterior");
    }

    private static void TestSpeciesSharing()
    {
        var config=EconomyDefaults.Create();
        MonsterCatalog.Upgrade(config);
        Check(config.monsterSpawns.Single(s=>s.id=="troll-0").speciesId==config.monsterSpawns.Single(s=>s.id=="troll-2").speciesId,"dois pontos reutilizam uma espécie idêntica");
        var species=config.monsterSpecies.Single(s=>s.id==config.monsterSpawns[0].speciesId);species.damage=77;
        Check(MonsterCatalog.Resolve(config).Where(m=>m.id is "troll-0" or "troll-2").All(m=>m.damage==77),"alteração de espécie atualiza ambos os pontos resolvidos");
        var mixed=EconomyRules.Clone(config);mixed.monsters=EconomyDefaults.Create().monsters;
        Check(!EconomyRules.Validate(mixed,null,(_,_,_)=>true).valid,"duas fontes de configuração são recusadas");
        var missing=EconomyRules.Clone(config);missing.monsterSpawns[0].speciesId="inexistente";
        Check(!EconomyRules.Validate(missing,null,(_,_,_)=>true).valid,"espécie inexistente é recusada");
    }

    // Explicit opt-in: SQLite backup API reads the existing database; only the copy is migrated.
    public static void VerifyExistingCopy(string sourcePath,string destinationPath,string collisionPath)
    {
        CopyDatabase(sourcePath,destinationPath);
        string destination=Path.GetFullPath(destinationPath);
        var store=new SaveStore(destination);
        var before=store.Load();
        string progress=WireJson.Write(before.players.Select(p=>p.character).ToArray());
        string history=WireJson.Write(before.world.economy.releases.Select(r=>r.config).ToArray());
        string deaths=WireJson.Write(before.world.economy.rewardedDeaths);
        int historyCount=before.world.economy.releases.Length;
        string catPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(collisionPath)),"catacombs-collision.json");
        _=new GameWorld(store,CollisionGrid.Load(collisionPath),catacombsGrid:File.Exists(catPath)?CollisionGrid.Load(catPath):null);
        var after=store.Load();
        if(progress!=WireJson.Write(after.players.Select(p=>p.character).ToArray()) || deaths!=WireJson.Write(after.world.economy.rewardedDeaths) ||
            history!=WireJson.Write(after.world.economy.releases.Take(historyCount).Select(r=>r.config).ToArray()))
            throw new InvalidOperationException("Migração da cópia alterou progresso, mortes recompensadas ou configurações históricas.");
        Console.WriteLine($"PASS: cópia SQLite consistente de {after.players.Count} personagens; progresso, recompensas e revisões preservados; conteúdo {after.world.economy.worldContentVersion}.");
    }
    public static void CopyDatabase(string sourcePath,string destinationPath)
    {
        string source=Path.GetFullPath(sourcePath),destination=Path.GetFullPath(destinationPath);
        if(source==destination||File.Exists(destination))throw new InvalidOperationException("Informe um destino novo, separado do banco existente.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        using(var from=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        using(var to=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination,Pooling=false}.ToString()))
        {from.Open();to.Open();from.BackupDatabase(to);}
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(destination,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }

}
