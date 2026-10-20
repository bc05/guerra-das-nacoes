using System;
using System.IO;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Local roster and pure model checks. No call to connect, preview HTTP, or the main server.
[InitializeOnLoad]
public static class MenuPlayModeChecks
{
    private const string Key="GuerraDasNacoes.MenuChecks.";
    private const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
    private static PrototypeGameMenu menu;
    private static PrototypeOnlineClient client;
    private static PrototypeCombat combat;
    private static string folder,originalProgress;
    private static int assertions,nextFrame;

    static MenuPlayModeChecks(){EditorApplication.update+=Tick;}

    [MenuItem("Guerra das Nações/Validar menu de personagens e gráficos")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Inicie a validação fora do PlayMode.");
        SessionState.SetBool(Key+"Active",false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity",OpenSceneMode.Single);
        SessionState.SetString(Key+"Directory",Path.Combine(Path.GetTempPath(),"guerra-menu-"+Guid.NewGuid().ToString("N")));
        SessionState.SetBool(Key+"Entered",false);SessionState.SetInt(Key+"Stage",0);
        SessionState.SetFloat(Key+"Timeout",(float)EditorApplication.timeSinceStartup+80f);
        SessionState.SetBool(Key+"Active",true);EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if(!SessionState.GetBool(Key+"Active",false))return;
        try
        {
            Guard(EditorApplication.timeSinceStartup<SessionState.GetFloat(Key+"Timeout",0),"tempo máximo excedido");
            if(!EditorApplication.isPlaying)
            {Guard(!SessionState.GetBool(Key+"Entered",false),"PlayMode terminou antes da validação");return;}
            SessionState.SetBool(Key+"Entered",true);
            if(EditorApplication.isCompiling||Time.frameCount<4||Time.frameCount<nextFrame)return;
            switch(SessionState.GetInt(Key+"Stage",0))
            {
                case 0:
                    folder=SessionState.GetString(Key+"Directory","");Directory.CreateDirectory(folder);
                    client=UnityEngine.Object.FindFirstObjectByType<PrototypeOnlineClient>();
                    Require(client!=null,"cliente da cena existe");
                    client.ConfigureForTests(false);
                    menu=client.GetComponent<PrototypeGameMenu>();
                    Require(menu!=null,"menu instalado no personagem sem duplicar cliente");
                    // The scene preference component has already executed Start. Replace only
                    // that transient component, then inject an isolated path before its Start.
                    UnityEngine.Object.DestroyImmediate(PrototypeGraphicsSettings.Instance);
                    PrototypeGraphicsSettings isolated=new GameObject("Preferências isoladas do menu").AddComponent<PrototypeGraphicsSettings>();
                    isolated.ConfigureForChecks(Path.Combine(folder,"graphics.json"));Set("graphics",isolated);
                    combat=client.GetComponent<PrototypeCombat>();
                    originalProgress=JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    foreach(PrototypeMonster monster in UnityEngine.Object.FindObjectsByType<PrototypeMonster>(FindObjectsSortMode.None))monster.enabled=false;
                    menu.ConfigureForTests(true,folder);
                    Require(menu.IsFrontEnd&&menu.IsOpen&&PrototypeGameMenu.BlocksGameplay&&PrototypeGameMenu.HidesHud,"seleção bloqueia controles e oculta HUD");
                    Require(!client.IsConnected&&client.Snapshot==null,"menu não conecta nem cria sessão automaticamente");
                    Require(!File.Exists(OnlineIdentityStore.GetIdentityPath(folder,"principal")),"lista inicial não cria credencial");
                    CharacterRoster roster=CharacterRosterStore.Load(folder);
                    Require(roster.entries.Length==1&&roster.entries[0].profile=="principal","principal preservado na lista inicial");
                    typeof(PrototypeOnlineClient).GetField("profile",Instance).SetValue(client,"segundo");
                    menu.ConfigureForTests(true,Path.Combine(folder,"second-launcher"));
                    Require(menu.SelectedProfile=="segundo","segundo atalho seleciona seu próprio card sem credencial anterior");
                    Require(!File.Exists(OnlineIdentityStore.GetIdentityPath(Path.Combine(folder,"second-launcher"),"segundo")),"seleção do segundo atalho não cria identidade");
                    typeof(PrototypeOnlineClient).GetField("profile",Instance).SetValue(client,"principal");
                    menu.ConfigureForTests(true,folder);
                    menu.SetPreviewForTests(new OnlineCharacterPreview{exists=false});Call("ConnectSelected");
                    Require((bool)typeof(PrototypeGameMenu).GetField("creating",Instance).GetValue(menu)&&!client.IsConnected,"principal novo pede nick antes de conectar");
                    Call("CloseNickname");
                    Call("OpenNickname",false);Set("newName","Lysara");
                    menu.ConfigureNicknameCheckForTests(false,"Este nick já está em uso.");Call("CheckNickname");
                    Require(CharacterRosterStore.Load(folder).entries.Length==1&&menu.IsOpen,"nick ocupado mantém formulário e não cria card");
                    Require(!File.Exists(OnlineIdentityStore.GetIdentityPath(folder,"principal")),"nick recusado não cria nem substitui identidade");
                    menu.ConfigureNicknameCheckForTests(true);Call("CheckNickname");
                    roster=CharacterRosterStore.Load(folder);
                    Require(roster.entries.Length==2&&roster.entries[1].displayName=="Lysara","formulário persiste segundo personagem");
                    Require(menu.SelectedProfile==roster.entries[1].profile&&roster.entries[1].profile!="principal","novo perfil possui identidade lógica distinta");
                    Require(!File.Exists(OnlineIdentityStore.GetIdentityPath(folder,menu.SelectedProfile)),"criação de card não cria personagem no servidor");
                    bool rejected=false;try{CharacterRosterStore.AddCharacter(folder,roster,"<nome>");}catch(ArgumentException){rejected=true;}
                    Require(rejected&&CharacterRosterStore.Load(folder).entries.Length==2,"nome inválido não altera lista");
                    Call("OpenNickname",false);Set("newName","Outro Herói");
                    int oldRequest=(int)typeof(PrototypeGameMenu).GetField("nicknameRequest",Instance).GetValue(menu);
                    Call("CloseNickname");
                    Call("CompleteNicknameCheck",oldRequest,new OnlineNicknameAvailability{available=true,nickname="Outro Herói"},null);
                    Require(CharacterRosterStore.Load(folder).entries.Length==2,"resposta de nick após cancelamento é ignorada");
                    foreach(Vector2 size in new[]{new Vector2(1440,900),new Vector2(1024,768),new Vector2(1280,720)})CheckLayout(size);
                    Require(menu.HasPreview&&!menu.PreviewHasGameplayComponents,"prévia contém somente visual, sem controle/combate/credenciais/colliders");
                    Require((Camera.main.cullingMask&(1<<11))==0,"câmera do jogo não enxerga prévia fora do mapa");
                    Advance(1);break;
                case 1:
                    menu.SetPreviewForTests(new OnlineCharacterPreview{exists=true,displayName="Lysara",level=8,mapId="city",
                        equipped=new[]{new EquipmentSlotSave{slot=(int)EquipmentSlot.Sword,definitionId="steel-sword",definitionRevision=1}}});
                    PrototypeEquipmentVisual visual=PreviewVisual();
                    Require(visual!=null&&visual.SwordVisible&&!visual.ShieldVisible,"prévia usa exatamente os slots equipados consultados");
                    Require(visual.BaseBodyVisible&&visual.BaseHeadVisible,"prévia mantém roupa base onde não há armadura");
                    Call("ConnectSelected");
                    Require(!(bool)typeof(PrototypeGameMenu).GetField("creating",Instance).GetValue(menu),"personagem existente não recebe formulário de renomeação");
                    Require(JsonUtility.ToJson(combat.Stats.CaptureSaveData())==originalProgress,"selecionar e visualizar não altera progresso do herói");
                    menu.ShowGraphicsForTests();
                    Require(menu.IsGraphicsOpen&&PrototypeGameMenu.BlocksGameplay,"gráficos abrem a partir da seleção");
                    Call("HandleEscape");Require(menu.IsFrontEnd&&menu.IsOpen&&!menu.IsGraphicsOpen,"Escape de gráficos volta à seleção");
                    Set("joining",true);Call("ConnectSelected");
                    Require(!client.IsConnected&&client.Snapshot==null,"duplo comando de conexão é ignorado durante espera");Set("joining",false);
                    menu.ConfigureForTests(false,folder);Advance(2);break;
                case 2:
                    Require(!menu.IsOpen&&!PrototypeGameMenu.BlocksGameplay,"fechar seleção libera controles");
                    PrototypeHud.CloseForEconomy();Call("HandleEscape");
                    Require(menu.IsOpen&&!menu.IsFrontEnd&&PrototypeGameMenu.BlocksGameplay,"Escape abre menu durante jogo");
                    Require(Mathf.Approximately(Time.timeScale,1),"menu não pausa o relógio do mundo online");
                    menu.ShowGraphicsForTests();Call("HandleEscape");
                    Require(menu.IsOpen&&!menu.IsGraphicsOpen&&!menu.IsFrontEnd,"Escape de gráficos retorna ao menu durante jogo");
                    Call("HandleEscape");Require(!menu.IsOpen,"Escape fecha menu durante jogo");
                    Require(!File.Exists(OnlineIdentityStore.GetIdentityPath(folder,"principal")),"nenhuma credencial foi criada por testes de UI");
                    Finish(true,assertions+" verificações: seleção, cadastro local, layout 1440/1024/720, preview modular isolado, gráficos e Escape sem alterar progresso ou conectar servidor.");break;
            }
        }
        catch(Exception error){Finish(false,error.ToString());}
    }

    private static PrototypeEquipmentVisual PreviewVisual()
    {
        object preview=typeof(PrototypeGameMenu).GetField("heroPreview",Instance).GetValue(menu);
        return preview.GetType().GetField("equipment",Instance).GetValue(preview) as PrototypeEquipmentVisual;
    }
    private static void CheckLayout(Vector2 size)
    {
        float scale=Mathf.Max(.45f,Mathf.Min(size.x/1440f,size.y/900f));float w=size.x/scale,h=size.y/scale;
        Rect main=PrototypeGameMenu.MainPanelRect(w,h),graphics=PrototypeGameMenu.GraphicsPanelRect(w,h);
        Require(Inside(w,h,main)&&Inside(w,h,graphics),"painéis cabem na tela "+size);
        Require(main.width-304-330-36>=440,"prévia e colunas têm espaço sem sobreposição "+size);
        Require(graphics.height-280>=400,"área de configurações permite rolagem "+size);
        Rect details=new Rect(main.xMax-330,main.y,330,main.height);
        Rect information=PrototypeGameMenu.ConnectionInfoRect(details),connect=PrototypeGameMenu.ConnectActionRect(details);
        Require(!information.Overlaps(connect)&&information.yMax<=connect.y-15,"conexão avançada fica recortada acima das ações fixas "+size);
        Require(Inside(w,h,information)&&Inside(w,h,connect),"conexão e ações permanecem dentro da tela "+size);
    }
    private static bool Inside(float w,float h,Rect r)=>r.x>=0&&r.y>=0&&r.xMax<=w&&r.yMax<=h-35;
    private static object Call(string name,params object[] arguments)=>typeof(PrototypeGameMenu).GetMethod(name,Instance).Invoke(menu,arguments);
    private static void Set(string name,object value)=>typeof(PrototypeGameMenu).GetField(name,Instance).SetValue(menu,value);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);assertions++;}
    private static void Guard(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Advance(int stage){SessionState.SetInt(Key+"Stage",stage);nextFrame=Time.frameCount+2;}
    private static void Finish(bool accepted,string message)
    {
        SessionState.SetBool(Key+"Active",false);
        if(!string.IsNullOrEmpty(folder)&&Directory.Exists(folder))Directory.Delete(folder,true);
        if(accepted)Debug.Log("[MENU PLAY PASS] "+message);else Debug.LogError("[MENU PLAY FAIL] "+message);
        if(Application.isBatchMode)EditorApplication.Exit(accepted?0:1);else EditorApplication.ExitPlaymode();
    }
}
