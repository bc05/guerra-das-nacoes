using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GuerraDasNacoes
{
    [DefaultExecutionOrder(-410)]
    public sealed class PrototypeEconomyUi : MonoBehaviour
    {
        private static PrototypeEconomyUi active;
        private static int interactionFrame = -1;
        private string npcId, selectedOfferId, selectedInstanceId;
        private bool inboxOpen, selling, confirming;
        private int confirmationVersion;
        private long confirmationPrice;
        private string confirmationName, confirmationId;
        private Vector2 listScroll;
        private string notice;
        private string lastResultId;
        private bool hasWalletSample;
        private long sampledCoins;
        private string rewardMessage;
        private float rewardMessageUntil;
        private OnlineNpc nearbyNpc;
        private OnlinePortal nearbyPortal;
        private OnlineLoot nearbyLoot;
        private GUIStyle title, body, small, centered, button;
        private PrototypeEquipmentIcons icons;
        private static readonly Color Edge = new Color(.39f,.33f,.23f);
        private static readonly Color Gold = new Color(.88f,.77f,.52f);
        private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
        private PrototypeOnlineClient Network => GetComponent<PrototypeOnlineClient>();
        public static bool BlocksGameplay => active != null && (active.IsOpen || interactionFrame == Time.frameCount);
        public bool IsOpen => inboxOpen || !string.IsNullOrEmpty(npcId);
        private static float Scale => Mathf.Clamp(Mathf.Min(Screen.height/900f,Screen.width/1200f),.7f,1.4f);
        private static Rect WalletRect(float width) => new Rect(width-230f,128f,212f,128f);
        private static Rect PanelRect(float width,float height) => new Rect(width/2f-350f,Mathf.Max(22f,height/2f-300f),700f,Mathf.Min(600f,height-44f));
        private static Rect InteractionRect(float width,float height) => new Rect(width/2f-240f,height-253f,480f,37f);
        private static string Money(long coins) => coins.ToString("N0",Portuguese)+" Coroas";
        private bool Ready => Network!=null&&Network.IsOnlineMode&&Network.CanPlay&&Network.Snapshot!=null;
        private bool CanSend => Ready&&!Network.HasPendingMutation;

        private void Awake(){active=this;icons=new PrototypeEquipmentIcons();}
        private void Update()
        {
            if (PrototypeGameMenu.BlocksGameplay) { if (IsOpen) Close(); return; }
            nearbyNpc=null;nearbyPortal=null;nearbyLoot=null;
            if(!Ready){hasWalletSample=false;return;}
            OnlineSnapshot snapshot=Network.Snapshot;
            if(hasWalletSample&&snapshot.character.coins>sampledCoins)
            {
                rewardMessage="+"+Money(snapshot.character.coins-sampledCoins)+" · saldo confirmado";
                rewardMessageUntil=Time.unscaledTime+3f;
            }
            sampledCoins=snapshot.character.coins;hasWalletSample=true;
            if(snapshot.npcs!=null)
                foreach(OnlineNpc npc in snapshot.npcs)
                    if(npc!=null&&npc.mapId==snapshot.mapId&&Distance(npc.x,npc.z)<=3f&&
                        (nearbyNpc==null||Distance(npc.x,npc.z)<Distance(nearbyNpc.x,nearbyNpc.z)))nearbyNpc=npc;
            if(snapshot.portals!=null)
                foreach(OnlinePortal portal in snapshot.portals)
                    if(portal!=null&&portal.mapId==snapshot.mapId&&Distance(portal.x,portal.z)<=3f)nearbyPortal=portal;
            if(snapshot.loot!=null)
                foreach(OnlineLoot loot in snapshot.loot)
                    if(loot!=null&&loot.mapId==snapshot.mapId&&Distance(loot.x,loot.z)<=3f&&
                        (nearbyLoot==null||Distance(loot.x,loot.z)<Distance(nearbyLoot.x,nearbyLoot.z)))nearbyLoot=loot;
            if(!string.IsNullOrEmpty(npcId))
            {
                OnlineNpc current=CurrentNpc();
                if(current==null||current.mapId!=snapshot.mapId||Distance(current.x,current.z)>3f)Close();
            }
            if(Input.GetKeyDown(KeyCode.I)||Input.GetKeyDown(KeyCode.C)||Input.GetKeyDown(KeyCode.H)||Input.GetKeyDown(KeyCode.O))Close();
            if(Input.GetKeyDown(KeyCode.Escape)&&IsOpen){interactionFrame=Time.frameCount;Close();}
            if(Input.GetKeyDown(KeyCode.B)){if(IsOpen)Close();else OpenInbox();interactionFrame=Time.frameCount;}
            if(!IsOpen&&!PrototypeHud.IsAnyPanelOpen&&Input.GetKeyDown(KeyCode.E))Interact();
            if(!IsOpen&&!PrototypeHud.IsAnyPanelOpen&&Input.GetKeyDown(KeyCode.F)&&snapshot.character.healthPotions>0)
            {Network.TryUsePotion();interactionFrame=Time.frameCount;}
            OnlineCommandResult result=Network.LastEconomyResult;
            if(result!=null&&result.commandId!=lastResultId)
            {
                lastResultId=result.commandId;
                notice=string.IsNullOrEmpty(result.message)?(result.accepted?"Operação confirmada e salva.":"Operação recusada."):result.message;
                confirming=false;
            }
            if(confirming&&confirmationVersion!=snapshot.economyVersion)
            {confirming=false;notice="A loja foi atualizada. Confira o preço e confirme novamente.";}
        }
        private float Distance(float x,float z)=>Vector2.Distance(new Vector2(transform.position.x,transform.position.z),new Vector2(x,z));
        private OnlineNpc CurrentNpc()
        {
            if(Network?.Snapshot?.npcs==null)return null;
            foreach(OnlineNpc npc in Network.Snapshot.npcs)if(npc!=null&&npc.id==npcId)return npc;
            return null;
        }
        private void Interact()
        {
            if(!CanSend)return;
            interactionFrame=Time.frameCount;
            if(nearbyLoot!=null){Network.TryCollect(nearbyLoot.id);return;}
            if(nearbyNpc!=null){OpenShop(nearbyNpc);return;}
            if(nearbyPortal!=null)Network.TryTravel(nearbyPortal.id);
        }
        private void OpenShop(OnlineNpc npc)
        {
            PrototypeHud.CloseForEconomy();inboxOpen=false;npcId=npc.id;selling=npc.role=="merchant";
            selectedOfferId=null;selectedInstanceId=null;confirming=false;listScroll=Vector2.zero;
            notice=selling?"Selecione um item da mochila. Equipamentos vinculados não podem ser vendidos.":"Selecione uma oferta para consultar atributos e requisitos.";
        }
        private void OpenInbox(){PrototypeHud.CloseForEconomy();npcId=null;inboxOpen=true;confirming=false;listScroll=Vector2.zero;}
        public void Close(){npcId=null;inboxOpen=false;confirming=false;}
        public static bool ContainsScreenPoint(Vector3 point)
        {
            if(active==null||!active.Ready)return false;
            float scale=Scale,width=Screen.width/scale,height=Screen.height/scale;
            Vector2 cursor=new Vector2(point.x/scale,(Screen.height-point.y)/scale);
            return WalletRect(width).Contains(cursor)||(active.IsOpen&&PanelRect(width,height).Contains(cursor))||
                (!active.IsOpen&&active.HasInteraction&&InteractionRect(width,height).Contains(cursor));
        }
        private bool HasInteraction=>nearbyLoot!=null||nearbyNpc!=null||nearbyPortal!=null;
        private void EnsureStyles()
        {
            if(title!=null)return;
            body=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true,richText=false};body.normal.textColor=new Color(.84f,.85f,.82f);
            title=new GUIStyle(body){fontSize=19,fontStyle=FontStyle.Bold};title.normal.textColor=Gold;
            small=new GUIStyle(body){fontSize=12};
            centered=new GUIStyle(body){alignment=TextAnchor.MiddleCenter};
            button=new GUIStyle(GUI.skin.button){fontSize=13,wordWrap=true,richText=false};
        }
        private void OnGUI()
        {
            if(!Ready || PrototypeGameMenu.HidesHud)return;
            EnsureStyles();float scale=Scale,width=Screen.width/scale,height=Screen.height/scale;
            Matrix4x4 previous=GUI.matrix;Color oldColor=GUI.color;bool oldEnabled=GUI.enabled;int oldDepth=GUI.depth;
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1f));GUI.color=Color.white;GUI.depth=-20;
            try
            {
                DrawWallet(WalletRect(width));
                if(!IsOpen&&Time.unscaledTime<rewardMessageUntil)
                {
                    Rect reward=new Rect(width/2f-225f,height-298f,450f,31f);
                    Frame(reward);GUI.Label(reward,rewardMessage,centered);
                }
                if(!IsOpen&&!PrototypeHud.IsAnyPanelOpen&&HasInteraction)
                {
                    string action=nearbyLoot!=null?"Coletar "+nearbyLoot.name:nearbyNpc!=null?"Conversar com "+nearbyNpc.name:
                        "Viajar · "+PrototypeCity.MapName(nearbyPortal.destinationMapId);
                    GUI.enabled=CanSend;
                    if(GUI.Button(InteractionRect(width,height),"E · "+action,button))Interact();GUI.enabled=oldEnabled;
                }

                if(IsOpen)DrawPanel(PanelRect(width,height));
            }
            finally{GUI.matrix=previous;GUI.color=oldColor;GUI.enabled=oldEnabled;GUI.depth=oldDepth;}
        }
        private void DrawWallet(Rect panel)
        {
            Frame(panel);CharacterSaveData data=Network.Snapshot.character;
            GUI.Label(new Rect(panel.x+12,panel.y+8,panel.width-24,25),Money(data.coins),title);
            GUI.Label(new Rect(panel.x+12,panel.y+37,panel.width-24,21),"Sucata: "+data.scrap,small);
            bool prior=GUI.enabled;GUI.enabled=CanSend&&data.healthPotions>0&&Network.Snapshot.self.health>0&&Network.Snapshot.self.health<Network.Snapshot.self.maxHealth;
            if(GUI.Button(new Rect(panel.x+12,panel.y+61,panel.width-24,25),"F · Poção de vida ("+data.healthPotions+")",button)){Network.TryUsePotion();interactionFrame=Time.frameCount;}
            GUI.enabled=Ready;
            int pending=Network.Snapshot.loot?.Length??0;
            if(GUI.Button(new Rect(panel.x+12,panel.y+93,panel.width-24,24),"B · Saques pendentes ("+pending+")",button))
            {if(IsOpen)Close();else OpenInbox();interactionFrame=Time.frameCount;}
            GUI.enabled=prior;
        }
        private void DrawPanel(Rect panel)
        {
            Frame(panel);GUI.Label(new Rect(panel.x+20,panel.y+13,panel.width-65,28),inboxOpen?"SAQUES PENDENTES":CurrentNpc()?.name??"COMÉRCIO",title);
            if(GUI.Button(new Rect(panel.xMax-43,panel.y+11,28,27),"×",button)){Close();interactionFrame=Time.frameCount;return;}
            GUI.Label(new Rect(panel.x+20,panel.y+47,panel.width-40,38),inboxOpen?
                "O saque pertence a você. Colete no local ou recupere perto do mercador da cidade.":
                selling?"Venda equipamentos que estão na mochila. Remova a peça do corpo antes de vendê-la.":"Ofertas confirmadas pelo servidor · "+Money(Network.Snapshot.character.coins),small);
            if(inboxOpen){DrawInbox(panel);return;}
            if(selling)DrawSelling(panel);else DrawBuying(panel);
            if(!string.IsNullOrEmpty(notice))GUI.Label(new Rect(panel.x+20,panel.yMax-85,panel.width-40,64),notice,small);
        }
        private void DrawBuying(Rect panel)
        {
            OnlineNpc npc=CurrentNpc();if(npc==null)return;
            OnlineShopOffer[] offers=npc.offers??Array.Empty<OnlineShopOffer>();
            Rect list=new Rect(panel.x+20,panel.y+99,282,panel.height-205);
            listScroll=GUI.BeginScrollView(list,listScroll,new Rect(0f,0f,262f,Mathf.Max(list.height,offers.Length*66f)));
            OnlineShopOffer selected=null;
            foreach(OnlineShopOffer offer in offers)
            {
                if(offer==null)continue;int index=Array.IndexOf(offers,offer);
                GUI.color=offer.id==selectedOfferId?Gold:Color.white;
                if(GUI.Button(new Rect(0f,index*66f,260f,59f),offer.name+"\n"+Money(offer.price),button))
                {selectedOfferId=offer.id;confirming=false;}
                GUI.color=Color.white;if(offer.id==selectedOfferId)selected=offer;
            }
            GUI.EndScrollView();
            Rect detail=new Rect(panel.x+322,panel.y+100,panel.width-342,panel.height-210);
            if(selected==null){GUI.Label(detail,"Escolha uma oferta na lista.",body);return;}
            EquipmentItem definition=EquipmentCatalog.GetDefinition(selected.definitionId,selected.definitionRevision>0?selected.definitionRevision:1);
            DrawDefinition(detail,definition,selected.name);
            Rect buy=new Rect(detail.x,panel.yMax-144,detail.width,39);
            bool allowed=CanSend&&Network.Snapshot.character.coins>=selected.price;
            GUI.enabled=allowed;
            if(GUI.Button(buy,confirming?"Confirmar compra · "+Money(confirmationPrice):"Comprar · "+Money(selected.price),button))
            {
                if(confirming&&confirmationId==selected.id&&confirmationPrice==selected.price&&confirmationVersion==Network.Snapshot.economyVersion)
                {Network.TryBuy(npc.id,selected);confirming=false;notice="Compra enviada. Aguarde a confirmação.";}
                else BeginConfirmation(selected.id,selected.name,selected.price);
            }
            GUI.enabled=true;
            if(!allowed&&Network.Snapshot.character.coins<selected.price)GUI.Label(new Rect(detail.x,panel.yMax-104,detail.width,20),"Coroas insuficientes.",small);
        }
        private void DrawSelling(Rect panel)
        {
            PrototypeCombat combat=GetComponent<PrototypeCombat>();if(combat==null)return;
            IReadOnlyList<EquipmentItem> inventory=combat.Stats.Equipment.Inventory;
            Rect list=new Rect(panel.x+20,panel.y+99,282,panel.height-205);
            int count=inventory.Count+(Network.Snapshot.character.scrap>0?1:0);
            listScroll=GUI.BeginScrollView(list,listScroll,new Rect(0f,0f,262f,Mathf.Max(list.height,count*66f)));
            EquipmentItem selected=null;
            for(int i=0;i<inventory.Count;i++)
            {
                EquipmentItem item=inventory[i];long price=SellPrice(item);
                GUI.color=item.Id==selectedInstanceId?Gold:item.Bound?new Color(.55f,.55f,.55f):Color.white;
                if(GUI.Button(new Rect(0f,i*66f,260f,59f),item.Name+"\n"+(item.Bound?"Vinculado · não vendável":Money(price)),button))
                {selectedInstanceId=item.Id;confirming=false;}
                GUI.color=Color.white;if(item.Id==selectedInstanceId)selected=item;
            }
            if(Network.Snapshot.character.scrap>0&&GUI.Button(new Rect(0f,inventory.Count*66f,260f,59f),"Sucata × "+Network.Snapshot.character.scrap+"\n4 Coroas por unidade",button))
            {selectedInstanceId="scrap";confirming=false;}
            GUI.EndScrollView();Rect detail=new Rect(panel.x+322,panel.y+100,panel.width-342,panel.height-210);
            bool scrap=selectedInstanceId=="scrap"&&Network.Snapshot.character.scrap>0;
            if(selected==null&&!scrap){GUI.Label(detail,"Escolha um item da mochila.",body);return;}
            DrawDefinition(detail,selected,scrap?"Sucata × "+Network.Snapshot.character.scrap:null);
            long value=scrap?Network.Snapshot.character.scrap*4L:SellPrice(selected);
            GUI.enabled=CanSend&&value>0&&(scrap||!selected.Bound);
            if(GUI.Button(new Rect(detail.x,panel.yMax-144,detail.width,39),confirming?"Confirmar venda · "+Money(confirmationPrice):"Vender"+(scrap?" toda a sucata":" esta cópia")+" · "+Money(value),button))
            {
                if(confirming&&confirmationId==selectedInstanceId&&confirmationPrice==value&&confirmationVersion==Network.Snapshot.economyVersion)
                {Network.TrySell(npcId,selectedInstanceId,scrap?Network.Snapshot.character.scrap:1);confirming=false;notice="Venda enviada. Aguarde a confirmação.";}
                else BeginConfirmation(selectedInstanceId,scrap?"Toda a sucata":selected.Name,value);
            }
            GUI.enabled=true;
            if(selected!=null&&selected.Bound)GUI.Label(new Rect(detail.x,panel.yMax-104,detail.width,21),"Equipamento vinculado: venda bloqueada.",small);
        }
        private void DrawDefinition(Rect detail,EquipmentItem item,string fallback)
        {
            GUI.Label(new Rect(detail.x,detail.y,detail.width,56),item?.Name??fallback??"Item",title);
            if(item==null)
            {GUI.Label(new Rect(detail.x,detail.y+72,detail.width,145),selectedInstanceId=="scrap"?"Material obtido nas caçadas. Venda ao mercador por Coroas.":"Consumível de vida. F ou botão da carteira para usar. A cura e o consumo são confirmados pelo servidor.",body);return;}
            GUI.DrawTexture(new Rect(detail.x,detail.y+58,64,64),icons.Get(item.Slot),ScaleMode.ScaleToFit);
            GUI.Label(new Rect(detail.x,detail.y+133,detail.width,90),item.Bonuses.Describe(),body);
            GUI.Label(new Rect(detail.x,detail.y+230,detail.width,100),"Requisitos\n"+item.Requirements.Describe(),small);
        }
        private long SellPrice(EquipmentItem item)
        {
            if(item==null||item.Bound)return 0;
            if(Network.Snapshot.itemDefinitions!=null)
                foreach(EconomyItemConfig definition in Network.Snapshot.itemDefinitions)
                    if(definition.definitionId==item.DefinitionId&&definition.definitionRevision==item.DefinitionRevision)return definition.sellPrice;
            return 0;
        }
        private void BeginConfirmation(string id,string name,long price)
        {confirming=true;confirmationId=id;confirmationName=name;confirmationPrice=price;confirmationVersion=Network.Snapshot.economyVersion;notice="Confira a operação: "+name+" · "+Money(price)+". Clique novamente para confirmar.";}
        private void DrawInbox(Rect panel)
        {
            OnlineLoot[] loot=Network.Snapshot.loot??Array.Empty<OnlineLoot>();
            if(loot.Length==0){GUI.Label(new Rect(panel.x+20,panel.y+110,panel.width-40,70),"Nenhum saque pendente. Moedas são creditadas diretamente na carteira ao derrotar monstros.",body);return;}
            Rect area=new Rect(panel.x+20,panel.y+101,panel.width-40,panel.height-127);
            listScroll=GUI.BeginScrollView(area,listScroll,new Rect(0,0,area.width-20,Mathf.Max(area.height,loot.Length*78f)));
            for(int i=0;i<loot.Length;i++)
            {
                OnlineLoot item=loot[i];if(item==null)continue;
                float y=i*78f;GUI.Label(new Rect(10,y+5,area.width-180,26),item.name+(item.quantity>1?" × "+item.quantity:""),body);
                string where=PrototypeCity.MapName(item.mapId);
                GUI.Label(new Rect(10,y+32,area.width-180,30),where+" · "+(item.mapId==Network.MapId?Mathf.CeilToInt(Distance(item.x,item.z))+" m":"outro mapa"),small);
                GUI.enabled=CanSend&&item.recoverable;
                if(GUI.Button(new Rect(area.width-160,y+12,132,37),PrototypeCity.IsInCity?"Recuperar":"Coletar",button))Network.TryCollect(item.id);
                GUI.enabled=true;
            }
            GUI.EndScrollView();
        }
        private static void Frame(Rect rect)
        {
            Color previous=GUI.color;GUI.color=Edge;GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color=new Color(.022f,.028f,.032f,.97f);GUI.DrawTexture(new Rect(rect.x+2,rect.y+2,rect.width-4,rect.height-4),Texture2D.whiteTexture);GUI.color=previous;
        }
        private void OnDestroy(){icons?.Dispose();if(active==this)active=null;}
    }
}
