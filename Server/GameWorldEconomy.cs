using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

public sealed partial class GameWorld
{
    private static readonly OnlinePortal[] Portals =
    {
        new(){id="portal-city",mapId="ruins",destinationMapId="city",x=0,z=-3},
        new(){id="portal-ruins",mapId="city",destinationMapId="ruins",x=0,z=-7},
        new(){id="portal-catacombs",mapId="ruins",destinationMapId=CatacombsLayout.MapId,x=CatacombsLayout.ValePortal.X,z=CatacombsLayout.ValePortal.Z},
        new(){id="portal-catacombs-exit",mapId=CatacombsLayout.MapId,destinationMapId="ruins",x=CatacombsLayout.ReturnPortal.X,z=CatacombsLayout.ReturnPortal.Z}
    };
    private string Travel(Player player,string portalId,double now)
    {
        var portal=AvailablePortals.FirstOrDefault(p=>p.id==portalId&&p.mapId==player.state.mapId);
        if(!player.stats.IsAlive)return "Personagem derrotado.";
        if(portal==null||Distance(player.state.x,player.state.z,portal.x,portal.z)>3)return "Aproxime-se do portal.";
        if(player.state.strike!=null)return "Aguarde o golpe terminar antes de viajar.";
        if(monsters.Any(m=>m.mapId==player.state.mapId&&m.targetId==player.state.id&&m.health>0&&!m.returning))return "Saia de combate antes de viajar.";
        var destination=PortalDestination(portal.id);
        if(!MapFree(portal.destinationMapId,destination.x,destination.z))return "Destino do portal bloqueado; posição preservada.";
        player.state.mapId=portal.destinationMapId;
        player.state.x=destination.x;player.state.z=destination.z;
        player.state.movementCredit=0;player.state.lastMoveAt=now;player.state.yaw=0;
        Emit("travel",player.state.id,player.state.id,0,0);
        return null;
    }
    private EconomyNpc NearbyNpc(Player player,string id)
    {
        if(!player.stats.IsAlive)return null;
        var npc=economy.active.npcs.FirstOrDefault(n=>n.id==id&&n.mapId==player.state.mapId);
        return npc!=null&&Distance(player.state.x,player.state.z,npc.x,npc.z)<=3?npc:null;
    }
    private EconomyItemConfig ItemConfig(string id,int revision) => economy.releases.SelectMany(r=>r.config.items).Concat(economy.active.items).LastOrDefault(i=>i.definitionId==id&&i.definitionRevision==revision);
    private void Ledger(Player player,long delta,string source,string reference,double now)
    {
        economy.ledger=economy.ledger.Append(new EconomyLedger{id=Guid.NewGuid().ToString("D"),playerId=player.state.id,source=source,reference=reference,amount=delta,balance=player.stats.Coins,atUnixMs=(long)(now*1000)}).ToArray();
    }
    private string Buy(Player player,OnlineCommand command,double now)
    {
        var npc=NearbyNpc(player,command.npcId);
        if(npc==null)return "Aproxime-se do vendedor.";
        if(command.quoteVersion!=economy.active.revision)return "O preço mudou. Atualize a loja e confirme a compra novamente.";
        var offer=economy.active.shops.FirstOrDefault(s=>s.id==npc.shopId)?.offers.FirstOrDefault(o=>o.id==command.offerId&&o.enabled);
        if(offer==null||command.amount<1||command.amount>100)return "Oferta ou quantidade inválida.";
        if(offer.potionQuantity==0&&command.amount!=1)return "Compre um equipamento por vez.";
        long cost;
        try{cost=checked(offer.price*command.amount);}catch(OverflowException){return "Valor inválido.";}
        if(player.stats.Coins<cost)return "Coroas insuficientes.";
        if(offer.potionQuantity>0)
        {
            int count=checked(offer.potionQuantity*command.amount);
            if(!player.stats.TryAddHealthPotions(count))return "Limite de poções atingido.";
        }
        else
        {
            var item=economy.active.items.FirstOrDefault(i=>i.definitionId==offer.definitionId&&i.definitionRevision==offer.definitionRevision&&i.enabled);
            if(item==null)return "Equipamento indisponível.";
            if(!player.stats.Equipment.TryAddItem(item.definitionId,Guid.NewGuid().ToString("D"),out string reason,false,"shop",item.definitionRevision))return reason;
        }
        if(!player.stats.TrySpendCoins(cost))throw new InvalidDataException("Saldo mudou durante compra serializada.");
        Ledger(player,-cost,"shop-buy",command.commandId,now);
        Emit("purchase",player.state.id,player.state.id,0,command.amount);
        return null;
    }
    private string Sell(Player player,OnlineCommand command,double now)
    {
        var npc=NearbyNpc(player,command.npcId);
        if(npc==null||npc.role!="merchant")return "Aproxime-se do mercador para vender.";
        if(command.quoteVersion!=economy.active.revision)return "A cotação mudou. Atualize a loja.";
        if(command.itemId=="scrap")
        {
            if(command.amount<1||command.amount>player.stats.Scrap)return "Quantidade de sucata inválida.";
            long amount=checked((long)command.amount*4);
            if(player.stats.Coins>long.MaxValue-amount)return "Limite de moedas atingido.";
            if(!player.stats.TryRemoveScrap(command.amount)||!player.stats.TryCreditCoins(amount))throw new InvalidDataException("Falha ao vender sucata.");
            Ledger(player,amount,"scrap-sell",command.commandId,now);return null;
        }
        var owned=player.stats.Equipment.GetOwned(command.itemId);
        if(owned==null)return "Item não pertence ao personagem.";
        if(owned.Bound)return "Equipamentos gratuitos ou vinculados não podem ser vendidos.";
        var config=ItemConfig(owned.DefinitionId,owned.DefinitionRevision);
        // Valuation uses the active canonical sale price if available; old definitions remain auditable.
        long price=economy.active.items.FirstOrDefault(i=>i.definitionId==owned.DefinitionId)?.sellPrice??config?.sellPrice??0;
        if(price<=0)return "O mercador não compra este item.";
        if(player.stats.Coins>long.MaxValue-price)return "Limite de moedas atingido.";
        if(!player.stats.Equipment.TryRemoveInventoryItem(command.itemId,out string reason))return reason;
        if(!player.stats.TryCreditCoins(price))throw new InvalidDataException("Crédito de venda recusado.");
        Ledger(player,price,"equipment-sell",command.commandId,now);
        Emit("sale",player.state.id,player.state.id,0,1);return null;
    }
    private bool CanRecover(Player player,PendingLoot loot) => (player.state.mapId==loot.mapId&&Distance(player.state.x,player.state.z,loot.x,loot.z)<=3)||
        (player.state.mapId=="city"&&economy.active.npcs.Any(n=>n.role=="merchant"&&NearbyNpc(player,n.id)!=null));
    private string Collect(Player player,string lootId,double now)
    {
        if(!player.stats.IsAlive)return "Personagem derrotado.";
        var loot=player.state.pendingLoot.FirstOrDefault(l=>l.id==lootId);
        if(loot==null)return "Saque já coletado ou indisponível.";
        if(!CanRecover(player,loot))return "Aproxime-se do saque ou do mercador na cidade.";
        if(!player.stats.Equipment.TryAddItem(loot.definitionId,loot.id,out string reason,false,"drop",loot.definitionRevision))return reason;
        player.state.pendingLoot=player.state.pendingLoot.Where(l=>l.id!=loot.id).ToArray();
        Emit("loot",player.state.id,player.state.id,0,1);return null;
    }
    private string DrinkPotion(Player player,double now)
    {
        if(!player.stats.IsAlive||player.stats.Health>=player.stats.MaxHealth)return "Use a poção quando estiver vivo e ferido.";
        if(now<player.state.nextActionAt)return "Aguarde antes de usar outra poção.";
        int before=player.stats.Health;
        if(!player.stats.TryConsumeHealthPotion())return "Sem poções de vida.";
        int healed=player.stats.Health-before;
        player.state.nextActionAt=now+1;
        Emit("heal",player.state.id,player.state.id,3,healed);return null;
    }
    private OnlineNpc[] NpcSnapshot(Player player) => economy.active.npcs.Where(n=>n.mapId==player.state.mapId).Select(n=>new OnlineNpc
    {
        id=n.id,name=n.name,role=n.role,mapId=n.mapId,x=n.x,z=n.z,
        offers=(economy.active.shops.FirstOrDefault(s=>s.id==n.shopId)?.offers??Array.Empty<EconomyShopOffer>()).Where(o=>o.enabled).Select(o=>
        {
            var item=ItemConfig(o.definitionId,o.definitionRevision);
            return new OnlineShopOffer{id=o.id,definitionId=o.definitionId,definitionRevision=o.definitionRevision,name=o.potionQuantity>0?"Poção de Vida":item?.name??o.definitionId,price=o.price,sellPrice=item?.sellPrice??0,quoteVersion=economy.active.revision};
        }).ToArray()
    }).ToArray();
    private OnlineLoot[] LootSnapshot(Player player) => player.state.pendingLoot.Select(l=>new OnlineLoot{id=l.id,definitionId=l.definitionId,definitionRevision=l.definitionRevision,name=ItemConfig(l.definitionId,l.definitionRevision)?.name??l.definitionId,mapId=l.mapId,x=l.x,z=l.z,kind=l.kind,quantity=l.quantity,recoverable=CanRecover(player,l)}).ToArray();
    private void RecordDamage(SavedMonster monster,Player player,int amount,double now)
    {
        var contribution=monster.contributions.FirstOrDefault(c=>c.playerId==player.state.id);
        if(contribution==null){contribution=new DamageContribution{playerId=player.state.id,firstHit=now};monster.contributions=monster.contributions.Append(contribution).ToArray();}
        contribution.damage+=amount;contribution.lastHit=now;
    }
    private void GrantDeath(SavedMonster monster,EconomyMonsterConfig profile,int ability,double now)
    {
        string deathId=monster.mapId+":"+monster.id+":"+monster.spawn;
        if(economy.rewardedDeaths.Contains(deathId))return;
        economy.rewardedDeaths=economy.rewardedDeaths.Append(deathId).ToArray();
        var winner=monster.contributions.Where(c=>now-c.lastHit<=60&&players.TryGetValue(c.playerId,out var p)&&p.stats.IsAlive&&p.state.mapId==monster.mapId&&Online(c.playerId,now)&&Distance(p.state.x,p.state.z,monster.x,monster.z)<=12)
            .OrderByDescending(c=>c.damage).ThenBy(c=>c.firstHit).ThenBy(c=>c.playerId,StringComparer.Ordinal).FirstOrDefault();
        if(winner==null)return;
        var player=players[winner.playerId];
        var table=Config(monster.configVersion).lootTables.First(t=>t.id==profile.lootTableId);
        long coins=EconomyRules.RandomCoins(table.coinsMin,table.coinsMax);
        if(coins>0&&!player.stats.TryCreditCoins(coins))throw new InvalidDataException("Carteira excedeu o limite; recompensa preservada por transação.");
        Ledger(player,coins,"monster-kill",deathId,now);
        player.stats.GrantExperience(profile.experience);
        Emit("experience",player.state.id,player.state.id,ability,profile.experience);
        Emit("coins",player.state.id,player.state.id,0,checked((int)coins));
        if(RandomNumberGenerator.GetInt32(1000000)<table.scrapChancePpm)
        {
            int scrap=RandomNumberGenerator.GetInt32(table.scrapMin,table.scrapMax+1);
            if(scrap>0&&!player.stats.TryAddScrap(scrap))throw new InvalidDataException("Limite de sucata atingido.");
        }
        foreach(var group in table.groups)
        {
            if(RandomNumberGenerator.GetInt32(1000000)>=group.chancePpm)continue;
            int roll=RandomNumberGenerator.GetInt32(group.entries.Sum(e=>e.weight));
            var entry=group.entries.First(e=>(roll-=e.weight)<0);
            var loot=new PendingLoot{id=Guid.NewGuid().ToString("D"),deathId=deathId,definitionId=entry.definitionId,definitionRevision=entry.definitionRevision,mapId=monster.mapId,x=monster.x,z=monster.z};
            player.state.pendingLoot=player.state.pendingLoot.Append(loot).ToArray();
        }
    }
}
