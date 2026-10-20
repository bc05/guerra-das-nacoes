using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

internal static class EconomySelfTests
{
    private static int assertions;
    private static void Check(bool ok,string text){assertions++;if(!ok)throw new InvalidOperationException("ECONOMY-TEST: "+text);}
    private static CollisionGrid Grid()=>new(){originX=-18,originZ=-18,cellSize=.25f,width=144,height=144,blocked=new int[144*144]};
    private static string Key()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static OnlineCommand Cmd(string type)=>new(){commandId=Guid.NewGuid().ToString("D"),type=type};
    private static OnlineSnapshot Send(GameWorld world,string token,double at,params OnlineCommand[] commands)=>world.Commands(token,new(){commands=commands},at);
    private static void Expect<T>(Action action,string text)where T:Exception{try{action();}catch(T){Check(true,text);return;}Check(false,text);}
    public static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gdn-economy-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{TestCommerce(Path.Combine(directory,"commerce.db"));TestDrop(Path.Combine(directory,"drop.db"));TestAdmin(Path.Combine(directory,"admin.db"));Console.WriteLine($"PASS: {assertions} verificações de economia, drops, mapas, comércio, versões e recuperação.");}
        finally{Directory.Delete(directory,true);}
    }
    private static void TestCommerce(string path)
    {
        var store=new SaveStore(path);var world=new GameWorld(store,Grid(),1000);string key=Key();var connected=world.Connect(new(){deviceKey=key},1000);
        Check(connected.mapId=="ruins"&&connected.character.coins==0&&connected.character.itemInstances.Length==12,"novo personagem inicia ruínas sem riqueza gratuita");
        var load=store.Load();var state=load.players.Single();var stats=PlayerProgression.FromSaveData(state.character);stats.TryCreditCoins(1000);stats.ReceiveDamage(40);state.character=stats.CaptureSaveData();state.mapId="city";state.x=-5;state.z=3;store.Save(load.players,load.world,Array.Empty<Receipt>());
        world=new GameWorld(store,Grid(),1001);connected=world.Connect(new(){deviceKey=key},1001);string token=connected.sessionToken;
        var buy=Cmd("buy");buy.npcId="npc-ferreiro";buy.offerId="offer-steel-sword";buy.quoteVersion=1;
        var bought=Send(world,token,1001.1,buy);Check(bought.results[0].accepted&&bought.character.coins==640,"compra debita entrega atomicamente");
        var repeated=Send(world,token,1001.2,buy);Check(repeated.character.coins==640&&repeated.character.inventoryItemIds.Length==1,"retry compra não duplica");
        buy=Cmd("buy");buy.npcId="npc-ferreiro";buy.offerId="offer-steel-sword";buy.quoteVersion=0;Check(!Send(world,token,1001.3,buy).results[0].accepted,"cotação antiga recusada");
        var potion=Cmd("potion");Check(!Send(world,token,1001.4,potion).results[0].accepted,"poção inexistente recusada");
        var portal=Cmd("portal");portal.portalId="portal-ruins";Check(!Send(world,token,1001.5,portal).results[0].accepted,"portal longe recusado");
        var ability=Cmd("ability");Check(!Send(world,token,1001.6,ability).results[0].accepted,"cidade segura sem ataque");
        load=store.Load();state=load.players.Single();state.x=3;state.z=-4;store.Save(load.players,load.world,Array.Empty<Receipt>());
        world=new GameWorld(store,Grid(),1002);connected=world.Connect(new(){deviceKey=key},1002);token=connected.sessionToken;
        string sword=connected.character.inventoryItemIds.Single();var sell=Cmd("sell");sell.npcId="npc-mercador";sell.itemId=sword;sell.quoteVersion=1;
        store.FailAfterCommitForTests=true;Expect<IOException>(()=>Send(world,token,1002.1,sell),"commit ambíguo venda");
        var sold=Send(world,token,1002.2,sell);Check(sold.results[0].accepted&&sold.character.coins==676&&sold.character.inventoryItemIds.Length==0,"retry venda após commit não duplica moedas");
        var unequip=Cmd("unequip");unequip.slot=0;var removed=Send(world,token,1002.3,unequip);string training=removed.character.inventoryItemIds.Single();
        sell=Cmd("sell");sell.npcId="npc-mercador";sell.itemId=training;sell.quoteVersion=1;Check(!Send(world,token,1002.4,sell).results[0].accepted,"starter vinculado não gera moedas");
        load=store.Load();state=load.players.Single();state.x=6;state.z=2;store.Save(load.players,load.world,Array.Empty<Receipt>());
        world=new GameWorld(store,Grid(),1003);connected=world.Connect(new(){deviceKey=key},1003);token=connected.sessionToken;
        var purchasePotion=Cmd("buy");purchasePotion.npcId="npc-alquimista";purchasePotion.offerId="potion-health";purchasePotion.quoteVersion=1;
        var withPotion=Send(world,token,1003.1,purchasePotion);Check(withPotion.results[0].accepted&&withPotion.character.healthPotions==1&&withPotion.character.coins==656,"compra poção é sumidouro");
        var healed=Send(world,token,1003.2,Cmd("potion"));Check(healed.results[0].accepted&&healed.character.healthPotions==0&&healed.character.health>withPotion.character.health,"poção cura e consome unidade");
        load=store.Load();state=load.players.Single();state.x=0;state.z=-7;store.Save(load.players,load.world,Array.Empty<Receipt>());
        world=new GameWorld(store,Grid(),1004);connected=world.Connect(new(){deviceKey=key},1004);token=connected.sessionToken;portal=Cmd("portal");portal.portalId="portal-ruins";
        var ruins=Send(world,token,1004.1,portal);Check(ruins.results[0].accepted&&ruins.mapId=="ruins"&&ruins.monsters.Length==4&&ruins.npcs.Length==0,"portal autoritativo muda mapa e visibilidade");
        Check(ruins.character.coins==656,"transição preserva carteira");
        Expect<GameApiException>(()=>world.Connect(new(){deviceKey=key,protocolVersion=1},1005),"cliente protocolo1 recusado");
    }
    private static void TestDrop(string path)
    {
        var store=new SaveStore(path);var world=new GameWorld(store,Grid(),2000);string keyA=Key(),keyB=Key();var a=world.Connect(new(){deviceKey=keyA,displayName="Economia A"},2000);var b=world.Connect(new(){deviceKey=keyB,displayName="Economia B"},2000);
        var load=store.Load();foreach(var p in load.players){p.mapId="ruins";p.x=-2.6f;p.z=1.8f;p.yaw=0;}
        var monster=load.world.monsters.First(m=>m.id=="troll-0");monster.health=1;monster.contributions=new[]{new DamageContribution{playerId=a.playerId,damage=100,firstHit=1999,lastHit=2000}};
        foreach(var config in new[]{load.world.economy.active,load.world.economy.releases[0].config})
        {var t=config.lootTables.First(t=>t.id=="loot-saqueador");t.coinsMin=t.coinsMax=7;t.groups=new[]{new EconomyLootGroup{id="guaranteed-test",chancePpm=1000000,entries=new[]{new EconomyLootEntry{definitionId="steel-sword",weight=1}}}};}
        store.Save(load.players,load.world,Array.Empty<Receipt>());world=new GameWorld(store,Grid(),2001);a=world.Connect(new(){deviceKey=keyA,displayName="Economia A"},2001);b=world.Connect(new(){deviceKey=keyB,displayName="Economia B"},2001);
        var hit=Cmd("ability");hit.ability=0;Check(Send(world,b.sessionToken,2001.1,hit).results[0].accepted,"golpe final válido");
        store.FailAfterCommitForTests=true;
        Expect<IOException>(()=>Send(world,b.sessionToken,2001.3),"commit ambíguo de morte e saque");
        var reward=Send(world,a.sessionToken,2001.4);Check(reward.character.coins==7&&reward.loot.Length==1,"maior contribuição ganha moedas e saque");
        var last=Send(world,b.sessionToken,2001.5);Check(last.character.coins==0&&last.loot.Length==0,"último golpe não rouba recompensa");
        var collect=Cmd("collect");collect.lootId=reward.loot[0].id;var collected=Send(world,a.sessionToken,2001.6,collect);Check(collected.results[0].accepted&&collected.loot.Length==0&&collected.character.inventoryItemIds.Length==1,"coleta transforma saque em cópia");
        var retry=Send(world,a.sessionToken,2001.7,collect);Check(retry.character.inventoryItemIds.Length==1&&retry.character.coins==7,"coleta idempotente");
        world=new GameWorld(store,Grid(),2002);var restored=world.Connect(new(){deviceKey=keyA,displayName="Economia A"},2002);Check(restored.character.coins==7&&restored.character.inventoryItemIds.Length==1,"reabertura preserva carteira e cópia");
        Check(store.Load().world.economy.rewardedDeaths.Length==1,"morte possui recibo durável único");
        load=store.Load();var owner=load.players.First(p=>p.id==a.playerId);var stats=PlayerProgression.FromSaveData(owner.character);
        while(stats.Equipment.Inventory.Count<EquipmentState.InventoryCapacity)
            if(!stats.Equipment.TryAddItem("steel-head",Guid.NewGuid().ToString("D"),out _,false,"test"))throw new InvalidOperationException("Fixture de mochila cheia inválida.");
        owner.character=stats.CaptureSaveData();owner.mapId="city";owner.x=3;owner.z=-4;
        string pendingId=Guid.NewGuid().ToString("D");owner.pendingLoot=new[]{new PendingLoot{id=pendingId,deathId="test-recovery",definitionId="steel-shield",mapId="ruins",x=10,z=10}};
        store.Save(load.players,load.world,Array.Empty<Receipt>());world=new GameWorld(store,Grid(),2003);restored=world.Connect(new(){deviceKey=keyA,displayName="Economia A"},2003);
        collect=Cmd("collect");collect.lootId=pendingId;var full=Send(world,restored.sessionToken,2003.1,collect);
        Check(!full.results[0].accepted&&full.loot.Length==1&&full.loot[0].recoverable,"mochila cheia preserva saque recuperável no mercador");
        var sell=Cmd("sell");sell.npcId="npc-mercador";sell.itemId=full.character.inventoryItemIds[0];sell.quoteVersion=1;
        Check(Send(world,restored.sessionToken,2003.2,sell).results[0].accepted,"vende excedente para liberar espaço");
        collect=Cmd("collect");collect.lootId=pendingId;var recovered=Send(world,restored.sessionToken,2003.3,collect);
        Check(recovered.results[0].accepted&&recovered.loot.Length==0&&recovered.character.inventoryItemIds.Length==EquipmentState.InventoryCapacity,"recupera saque remoto sem perda nem excesso de capacidade");

    }
    private static void TestAdmin(string path)
    {
        var store=new SaveStore(path);var world=new GameWorld(store,Grid(),3000);var state=(AdminSnapshot)world.AdminState(3000);var config=EconomyRules.Clone(state.draft);
        config.lootTables[0].coinsMin=8;config.lootTables[0].coinsMax=10;
        Check(world.AdminValidate(config).valid,"rascunho econômico válido");
        world.AdminDraft(new(){baseRevision=state.draftRevision,config=config,reason="Teste",requestId=Guid.NewGuid().ToString("D")},3000);
        Expect<GameApiException>(()=>world.AdminDraft(new(){baseRevision=state.draftRevision,config=config},3000),"conflito de edição recusado");
        state=(AdminSnapshot)world.AdminState(3000);string request=Guid.NewGuid().ToString("D");var publish=new AdminPublishRequest{baseRevision=state.draftRevision,reason="Taxas teste",requestId=request};
        state=(AdminSnapshot)world.AdminPublish(publish,3000);Check(state.active.revision==2,"publicação cria versão");
        state=(AdminSnapshot)world.AdminPublish(publish,3000.1);Check(state.history.Length==2,"retry publicação não duplica versão");
        var invalid=EconomyRules.Clone(state.draft);invalid.items.First(i=>i.definitionId=="steel-sword").damage++;Check(world.AdminValidate(invalid).valid,"atributo editado recebe nova revisão automaticamente");
        invalid=EconomyRules.Clone(state.draft);invalid.shops.First(s=>s.id=="shop-ferreiro").offers[0].price=1;Check(!world.AdminValidate(invalid).valid,"arbitragem compra venda recusada");
        var scheduled=new AdminPublishRequest{baseRevision=state.draftRevision,reason="Agendamento teste",requestId=Guid.NewGuid().ToString("D"),activateAtUnixMs=3005000};
        state=(AdminSnapshot)world.AdminPublish(scheduled,3001);Check(state.active.revision==2&&state.history.Any(r=>r.status=="scheduled"),"agendamento não ativa cedo");world.Tick(3006);state=(AdminSnapshot)world.AdminState(3006);Check(state.active.revision==3,"agendamento ativa sem jogadores online");
        world.AdminRollback(new(){version=1,reason="Retorno teste",requestId=Guid.NewGuid().ToString("D")},3007);state=(AdminSnapshot)world.AdminState(3007);Check(state.active.revision==1&&state.audit.Length>=4,"rollback auditado preserva histórico");
        world=new GameWorld(store,Grid(),3008);state=(AdminSnapshot)world.AdminState(3008);Check(state.active.revision==1&&state.history.Length==3,"versões sobrevivem reinício");
        config=EconomyRules.Clone(state.draft);var head=config.items.First(i=>i.definitionId=="steel-head");head.defense++;
        state=(AdminSnapshot)world.AdminDraft(new(){baseRevision=state.draftRevision,config=config,reason="Nova revisão"},3009);
        Check(state.draft.items.First(i=>i.definitionId=="steel-head").definitionRevision==2,"rascunho atribui revisão de atributos automaticamente");
        Check(state.draft.shops.SelectMany(s=>s.offers).First(o=>o.definitionId=="steel-head").definitionRevision==2,"ofertas acompanham nova revisão");
        var publishItem=new AdminPublishRequest{baseRevision=state.draftRevision,reason="Elmo ajustado",requestId=Guid.NewGuid().ToString("D")};
        store.FailNextSaveForTests=true;Expect<IOException>(()=>world.AdminPublish(publishItem,3010),"publicação falha antes de commit");
        Check(((AdminSnapshot)world.AdminState(3010)).active.revision==1,"publicação sem commit não fica ativa");
        state=(AdminSnapshot)world.AdminPublish(publishItem,3010.1);Check(state.active.items.First(i=>i.definitionId=="steel-head").definitionRevision==2,"retry publica revisão nova após recuperação");
        Check(EquipmentCatalog.GetDefinition("steel-head",1).Bonuses.Defense==1&&EquipmentCatalog.GetDefinition("steel-head",2).Bonuses.Defense==2,"revisão nova preserva atributos antigos");
        config=EconomyRules.Clone(state.draft);var extra=EconomyRules.Clone(config.monsters[0]);extra.id="extra-test";extra.x=10;extra.z=10;config.monsters=config.monsters.Append(extra).ToArray();
        state=(AdminSnapshot)world.AdminDraft(new(){baseRevision=state.draftRevision,config=config,reason="Spawn extra"},3011);
        state=(AdminSnapshot)world.AdminPublish(new(){baseRevision=state.draftRevision,reason="Novo encontro",requestId=Guid.NewGuid().ToString("D")},3011.1);
        world.AdminRollback(new(){version=1,reason="Retira encontro futuro",requestId=Guid.NewGuid().ToString("D")},3012);
        var loaded=store.Load();var retired=loaded.world.monsters.Single(m=>m.id=="extra-test");retired.health=0;retired.respawnAt=3013;store.Save(loaded.players,loaded.world,Array.Empty<Receipt>());
        world=new GameWorld(store,Grid(),3014);var connected=world.Connect(new(){deviceKey=Key()},3014);
        Check(!connected.monsters.Any(m=>m.id=="extra-test"),"rollback retira spawn novo após sua vida sem travar respawn");

    }
}
