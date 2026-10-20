using System;
using System.Linq;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs with an isolated empty economy server at :18092. Never uses the principal profile.
[InitializeOnLoad]
public static class EconomyPlayModeChecks
{
    private const string Key="GuerraDasNacoes.EconomyChecks.";
    private static PrototypeOnlineClient client;
    private static PrototypeCharacter character;
    private static PrototypeCity city;
    private static PrototypeEconomyUi ui;
    private static string rejectedId;
    private static int nextFrame;
    private static int nextCapture;
    static EconomyPlayModeChecks(){EditorApplication.update+=Tick;}
    [MenuItem("Guerra das Nações/Validar economia, cidade e lojas online")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Inicie fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity",OpenSceneMode.Single);
        SessionState.SetBool(Key+"Active",true);SessionState.SetBool(Key+"Entered",false);
        SessionState.SetInt(Key+"Stage",0);SessionState.SetFloat(Key+"Timeout",(float)EditorApplication.timeSinceStartup+110f);
        SessionState.SetString(Key+"Profile","economy-"+Guid.NewGuid().ToString("N").Substring(0,18));
        nextCapture=0;nextFrame=0;
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key+"Active",false))return;
        try
        {
            Require(EditorApplication.timeSinceStartup<SessionState.GetFloat(Key+"Timeout",0),"timeout estágio "+SessionState.GetInt(Key+"Stage",0)+" · "+(client!=null?client.Status:"iniciando"));
            if(!EditorApplication.isPlaying){Require(!SessionState.GetBool(Key+"Entered",false),"PlayMode terminou antes do teste");return;}
            SessionState.SetBool(Key+"Entered",true);
            if(EditorApplication.isCompiling||Time.frameCount<4||Time.frameCount<nextFrame)return;
            switch(SessionState.GetInt(Key+"Stage",0))
            {
                case 0:
                    client=UnityEngine.Object.FindFirstObjectByType<PrototypeOnlineClient>();Require(client!=null,"cliente existe");
                    character=client.GetComponent<PrototypeCharacter>();character.enabled=false;
                    city=client.GetComponent<PrototypeCity>();ui=client.GetComponent<PrototypeEconomyUi>();
                    Require(city!=null&&city.IsBuilt&&ui!=null,"cidade e interface instaladas em runtime");
                    Require(city.NpcCount==6,"seis habitantes com modelos 3D");
                    Require(city.CityRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length>=6,"NPCs usam modelos com malha e esqueleto");
                    Require(city.CityRoot.GetComponentsInChildren<PrototypeCombat>(true).Length==0&&city.CityRoot.GetComponentsInChildren<PrototypeOnlineClient>(true).Length==0,"NPC visual não duplica gameplay ou credenciais");
                    city.ApplyMap("city");Physics.SyncTransforms();
                    Require(city.CityRoot.activeSelf&&PrototypeCity.IsInCity,"cidade ativada");
                    Require(Physics.Raycast(new Vector3(0f,4f,0f),Vector3.down,out RaycastHit hit,8f,1<<8)&&hit.collider.transform.IsChildOf(city.CityRoot.transform),"chão da cidade possui colisão e layer para clique");
                    Require(city.CityRoot.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterial!=null&&r.sharedMaterial.mainTexture!=null),"cidade reutiliza texturas escaneadas");
                    city.ApplyMap("ruins");Require(!city.CityRoot.activeSelf,"retorno restaura cenário das ruínas");
                    client.ConfigureForTests(true,"http://127.0.0.1:18092",SessionState.GetString(Key+"Profile",""),"Economia teste");
                    Advance(1);break;
                case 1:
                    if(!client.CanPlay)return;
                    if(client.MapId=="ruins")
                    {
                        OnlinePortal entry=client.Snapshot.portals.First(p=>p.destinationMapId=="city");
                        if(!WalkTo(new Vector3(entry.x,.04f,entry.z)))return;
                        if(client.HasPendingMutation)return;
                        Require(client.TryTravel(entry.id),"novo personagem viaja à cidade pelo portal próximo");
                        Advance(101);break;
                    }
                    Require(client.Snapshot.protocolVersion==2&&client.MapId=="city","portal coloca o personagem na cidade no protocolo 2");
                    Require(client.Snapshot.character.coins==0&&client.Snapshot.character.healthPotions==0,"carteira e consumíveis não inventam saldo");
                    Require(client.GetComponent<PrototypeCombat>().Stats.Equipment.Inventory.Count==0&&client.Snapshot.character.itemInstances.Length==12,"novo personagem recebe somente o conjunto vinculado");
                    Require(client.Snapshot.npcs.Length==5&&client.Snapshot.npcs.Any(n=>n.role=="merchant"),"cinco comerciantes publicados pelo servidor");
                    Require(client.Snapshot.monsters.Length==0,"monstros das ruínas não aparecem na cidade");
                    Require(client.Snapshot.itemDefinitions.Length>=35,"cliente recebeu definições das revisões instaladas");
                    Advance(2);break;
                case 101:
                    if(client.HasPendingMutation||client.MapId!="city")return;
                    Advance(1);break;
                case 2:
                    OnlineNpc smith=client.Snapshot.npcs.First(n=>n.role=="blacksmith");
                    if(!WalkTo(new Vector3(smith.x,.04f,smith.z)))return;
                    typeof(PrototypeEconomyUi).GetMethod("OpenShop",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,new object[]{smith});
                    Require(ui.IsOpen&&PrototypeHud.BlocksGameplay,"loja bloqueia combate e movimento");
                    Require(smith.offers.Length>0&&smith.offers[0].price>0,"loja recebe ofertas e preços reais");
                    if(client.HasPendingMutation)return;
                    Require(client.TryBuy(smith.id,smith.offers[0]),"pedido de compra enfileirado");
                    Require(client.Snapshot.character.coins==0,"compra não desconta otimisticamente");
                    Advance(3);break;
                case 3:
                    if(client.HasPendingMutation||client.LastEconomyResult==null)return;
                    Require(!client.LastEconomyResult.accepted&&client.Snapshot.character.coins==0,"servidor recusa saldo insuficiente sem criar item");
                    rejectedId=client.LastEconomyResult.commandId;
                    if(nextCapture==0)
                    {
                        CaptureWorld("/private/tmp/GuerraDasNacoes-v12-city-shop.png");
                        nextCapture=Time.frameCount+5;return;
                    }
                    if(Time.frameCount<nextCapture)return;
                    ui.Close();Require(!ui.IsOpen,"loja fecha");
                    Advance(4);break;
                case 4:
                    OnlinePortal exit=client.Snapshot.portals.First(p=>p.destinationMapId=="ruins");
                    if(!WalkTo(new Vector3(exit.x,.04f,exit.z)))return;
                    if(client.HasPendingMutation)return;
                    Require(client.TryTravel(exit.id),"portal enfileirado");Advance(5);break;
                case 5:
                    if(client.HasPendingMutation||client.MapId!="ruins")return;
                    Require(!PrototypeCity.IsInCity&&!city.CityRoot.activeSelf,"confirmação do mapa troca cenário");
                    Require(client.Snapshot.monsters.Length>=3&&client.Snapshot.npcs.Length==0,"snapshot separa monstros e NPCs entre mapas");
                    Require(client.Snapshot.character.coins==0&&client.Snapshot.character.itemInstances.Length==12,"viajar preserva patrimônio");
                    Advance(6);break;
                case 6:
                    OnlinePortal town=client.Snapshot.portals.First(p=>p.destinationMapId=="city");
                    if(!WalkTo(new Vector3(town.x,.04f,town.z)))return;
                    if(client.HasPendingMutation)return;
                    Require(client.TryTravel(town.id),"retorno à cidade enviado");Advance(7);break;
                case 7:
                    if(client.HasPendingMutation||client.MapId!="city")return;
                    typeof(PrototypeEconomyUi).GetMethod("OpenInbox",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,null);
                    Require(ui.IsOpen&&PrototypeHud.BlocksGameplay,"recuperação de saques bloqueia comandos de combate");
                    Require(client.Snapshot.loot.Length==0,"nenhum saque fictício");
                    CaptureWorld("/private/tmp/GuerraDasNacoes-v12-city-loot.png");
                    Advance(8);nextCapture=Time.frameCount+5;break;
                case 8:
                    if(Time.frameCount<nextCapture)return;
                    ui.Close();
                    Debug.Log("[ECONOMIA PLAYMODE] PASS: modelos de 6 NPCs, texturas, chão clicável, carteira autoritativa, loja/saldo, portais e separação de mapas.");
                    Finish(true);break;
            }
        }
        catch(Exception error){Debug.LogError("[ECONOMIA PLAYMODE] FAIL "+error);Finish(false);}
    }
    private static void CaptureWorld(string path)
    {
        Camera camera=Camera.main;if(camera==null)return;
        RenderTexture previousTarget=camera.targetTexture,previousActive=RenderTexture.active;
        RenderTexture target=new RenderTexture(1440,900,24);
        Texture2D image=new Texture2D(1440,900,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();
            System.IO.File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
            UnityEngine.Object.Destroy(image);target.Release();UnityEngine.Object.Destroy(target);
        }
    }
    private static bool WalkTo(Vector3 target)
    {
        Vector3 current=character.transform.position;
        if(Vector2.Distance(new Vector2(current.x,current.z),new Vector2(target.x,target.z))<.35f)return true;
        character.transform.position=Vector3.MoveTowards(current,target,2.4f*Mathf.Max(.001f,Time.deltaTime));
        return false;
    }
    private static void Advance(int stage){SessionState.SetInt(Key+"Stage",stage);nextFrame=Time.frameCount+2;}
    private static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
    private static void Finish(bool success)
    {
        SessionState.SetBool(Key+"Active",false);EditorApplication.ExitPlaymode();
        if(Application.isBatchMode)EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
    }
}
