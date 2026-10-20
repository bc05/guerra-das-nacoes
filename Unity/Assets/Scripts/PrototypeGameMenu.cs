using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuerraDasNacoes
{
    // The front end owns presentation only. Loading credentials and changing sessions stay
    // inside the online client; previews never construct a second gameplay character.
    [DefaultExecutionOrder(-400), DisallowMultipleComponent]
    public sealed class PrototypeGameMenu : MonoBehaviour
    {
        private enum Page { Characters, Pause, Graphics }
        private static PrototypeGameMenu active;
        private PrototypeOnlineClient network;
        private PrototypeCharacter character;
        private PrototypeGraphicsSettings graphics;
        private CharacterRoster roster;
        private CharacterRosterEntry selected;
        private OnlineCharacterPreview preview;
        private MenuHeroPreview heroPreview;
        private GraphicsSettingsData draft;
        private string rosterDirectory, profileRoot, endpoint, activeEndpoint, message, previewStatus, newName = "", rosterError;
        private readonly HashSet<string> verifiedNicknames = new HashSet<string>(StringComparer.Ordinal);
        private bool checkingNickname, nicknameForSelected, focusNickname;
        private bool nicknameTestAvailable = true, inputDiagnostics;
        private string nicknameTestNotice;
        private string nicknameStatus;
        private int nicknameRequest;
        private bool frontEnd, open, joining, leaving, creating, advanced, confirmingQuit, testsSkipPreview, wasDisplayPending;
        private int previewRequest, inputFrame = -1;
        private Page page;
        private Vector2 rosterScroll, settingsScroll, detailsScroll;
        private GUIStyle title, subtitle, heading, body, muted, small, button, primary, field, card, center;
        private static readonly Color Gold = new Color(.84f, .73f, .51f);
        private static readonly Color Pale = new Color(.88f, .9f, .91f);
        private static readonly Color Muted = new Color(.57f, .63f, .67f);

        public static bool BlocksGameplay => active != null && (active.IsOpen || active.inputFrame == Time.frameCount);
        public static bool HidesHud => active != null && active.IsOpen;
        public bool IsFrontEnd => frontEnd;
        public bool IsOpen => frontEnd || open;
        public bool IsGraphicsOpen => IsOpen && page == Page.Graphics;
        public string SelectedProfile => selected == null ? null : selected.profile;
        public bool PreviewHasGameplayComponents => heroPreview != null && heroPreview.HasGameplayComponents;
        public bool HasPreview => heroPreview != null;

        private void Awake()
        {
            if (active != null && active != this) { enabled = false; return; }
            active = this;
            network = GetComponent<PrototypeOnlineClient>();
            character = GetComponent<PrototypeCharacter>();
            graphics = PrototypeGraphicsSettings.GetOrCreate();
            string[] args = Environment.GetCommandLineArgs();
            bool auto = Array.IndexOf(args, "--auto-connect") >= 0 || Array.IndexOf(args, "-auto-connect") >= 0;
            inputDiagnostics = Array.IndexOf(args, "--menu-input-check") >= 0;
            frontEnd = network != null && network.IsOnlineMode && !auto;
            endpoint = network != null ? network.ServerUrl : "http://127.0.0.1:8088";
            rosterDirectory = Application.persistentDataPath;
            if (frontEnd) InitializeFrontEnd();
        }

        private void Start()
        {
            if (frontEnd) EnsurePreview();
        }

        public void ConfigureForTests(bool enableMenu, string temporaryRosterDirectory = null)
        {
            testsSkipPreview = !string.IsNullOrEmpty(temporaryRosterDirectory);
            rosterDirectory = temporaryRosterDirectory ?? Application.persistentDataPath;
            frontEnd = enableMenu; open = false; joining = false; leaving = false;
            if (enableMenu) InitializeFrontEnd();
            else if (heroPreview != null) heroPreview.SetVisible(false);
        }

        public void ShowGraphicsForTests() { OpenGraphics(); }

        public void ConfigureNicknameCheckForTests(bool available, string notice = null)
        {
            if (!testsSkipPreview) throw new InvalidOperationException("A disponibilidade de teste exige uma pasta de perfis isolada.");
            nicknameTestAvailable = available; nicknameTestNotice = notice;
        }

        public void SetPreviewForTests(OnlineCharacterPreview value)
        {
            if (!testsSkipPreview) throw new InvalidOperationException("A prévia de teste exige uma pasta de perfis isolada.");
            preview = value; EnsurePreview(); ApplyPreviewAppearance();
        }

        public static Rect MainPanelRect(float width, float height) =>
            new Rect(Mathf.Max(24f, (width - 1280f) * .5f), 142f, Mathf.Min(1280f, width - 48f), Mathf.Min(690f, height - 200f));

        public static Rect GraphicsPanelRect(float width, float height) =>
            new Rect((width - Mathf.Min(950f, width - 48f)) * .5f, 122f,
                Mathf.Min(950f, width - 48f), Mathf.Min(724f, height - 162f));

        public static Rect ConnectionInfoRect(Rect details) =>
            new Rect(details.x+18,details.y+17,details.width-36,Mathf.Max(120,details.height-199));

        public static Rect ConnectActionRect(Rect details) =>
            new Rect(details.x+24,details.yMax-166,details.width-48,54);

        private void InitializeFrontEnd()
        {
            page = Page.Characters; creating = false; checkingNickname = false; nicknameRequest++;
            confirmingQuit = false; message = null; rosterError = null;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                endpoint = PrototypeOnlineClient.ValidateServer(endpoint, Array.IndexOf(args, "--allow-insecure-lan") >= 0);
                activeEndpoint = endpoint;
                profileRoot = testsSkipPreview ? rosterDirectory : OnlineIdentityStore.GetIdentityRoot(rosterDirectory, endpoint);
                roster = CharacterRosterStore.Load(profileRoot);
                if (network != null && network.Profile == "segundo")
                    CharacterRosterStore.EnsureSecondLauncherCharacter(profileRoot, roster, network.DisplayName);
                selected = null;
                if (roster.entries != null && roster.entries.Length > 0)
                {
                    foreach (CharacterRosterEntry entry in roster.entries)
                        if (network != null && entry.profile == network.Profile) selected = entry;
                    if (selected == null) selected = roster.entries[0];
                }
                SelectCharacter(selected);
            }
            catch (Exception error) { selected = null; roster = null; rosterError = error.Message; message = "Não foi possível abrir a lista de personagens. " + error.Message; }
            if (Application.isPlaying) EnsurePreview();
        }

        private void EnsurePreview()
        {
            if (heroPreview == null && character != null && character.VisualRoot != null)
            {
                heroPreview = new MenuHeroPreview(character);
                ApplyPreviewAppearance();
            }
            if (heroPreview != null) heroPreview.SetVisible(frontEnd && page != Page.Graphics);
        }

        private void SelectCharacter(CharacterRosterEntry entry)
        {
            if (joining || leaving) return;
            selected = entry; preview = null; previewRequest++; message = null; detailsScroll = Vector2.zero;
            if (entry == null) return;
            previewStatus = testsSkipPreview ? "Prévia local" : "Consultando personagem…";
            ApplyPreviewAppearance();
            if (testsSkipPreview || network == null) return;
            int request = previewRequest;
            network.RequestCharacterPreview(entry.profile, activeEndpoint, (value, error) =>
            {
                if (this == null || request != previewRequest || !frontEnd) return;
                preview = value;
                previewStatus = !string.IsNullOrEmpty(error) ? error : value != null && value.exists
                    ? "Progresso consultado no servidor" : "Novo personagem · será criado ao conectar";
                ApplyPreviewAppearance();
            });
        }

        private void ApplyPreviewAppearance()
        {
            if (heroPreview == null) return;
            heroPreview.ApplyEquipment(preview != null && preview.exists ? preview.equipped : null,
                preview == null || !preview.exists);
        }

        private void Update()
        {
            if (inputDiagnostics && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0)))
                Debug.Log("[MENU INPUT] hardware=" + (Input.GetMouseButtonDown(0) ? "down" : "up") +
                    " pos=" + Input.mousePosition + " focused=" + Application.isFocused + " menu=" + IsOpen +
                    " screen=" + Screen.width + "x" + Screen.height);
            if (wasDisplayPending && !graphics.HasPendingConfirmation && page == Page.Graphics) draft = graphics.Current;
            wasDisplayPending = graphics.HasPendingConfirmation;
            if (heroPreview != null) heroPreview.SetVisible(frontEnd && page != Page.Graphics);
            if (joining && network != null)
            {
                if (network.IsConnected && network.CanPlay)
                {
                    joining = false; frontEnd = false; open = false; inputFrame = Time.frameCount;
                    if (heroPreview != null) heroPreview.SetVisible(false);
                    return;
                }
                if (!network.IsConnecting && !network.IsConnected)
                {
                    joining = false; message = network.Status;
                    if (IsNicknameError && selected != null)
                    {
                        verifiedNicknames.Remove(NicknameKey(selected.profile));
                        string reason = network.Status;
                        OpenNickname(true); nicknameStatus = reason;
                    }
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
            if (IsOpen && Input.GetKeyDown(KeyCode.Return) && !creating && !confirmingQuit && page == Page.Characters)
                ConnectSelected();
        }

        private void HandleEscape()
        {
            if (leaving) return;
            if (joining) { CancelConnection(); return; }
            if (confirmingQuit) { confirmingQuit = false; inputFrame = Time.frameCount; return; }
            if (creating) { CloseNickname(); inputFrame = Time.frameCount; return; }
            if (page == Page.Graphics && IsOpen)
            {
                if (graphics.HasPendingConfirmation) graphics.Revert();
                CloseGraphics(); inputFrame = Time.frameCount; return;
            }
            if (frontEnd) return;
            // Let the existing HUD/economy consume Escape before opening the pause menu.
            if (PrototypeHud.IsAnyPanelOpen) { PrototypeHud.CloseForEconomy(); inputFrame = Time.frameCount; return; }
            if (PrototypeEconomyUi.BlocksGameplay) return;
            open = !open; page = Page.Pause; message = null; inputFrame = Time.frameCount;
        }

        private void ConnectSelected()
        {
            if (selected == null || joining || leaving || network == null || !string.IsNullOrEmpty(rosterError) || endpoint != activeEndpoint) return;
            if (NeedsNickname)
            { OpenNickname(true); return; }
            StartSelectedCharacter();
        }

        private bool IsNicknameError => network != null &&
            (network.LastConnectionErrorCode == "nickname_taken" || network.LastConnectionErrorCode == "nickname_invalid");
        private bool SelectedHasNicknameError => IsNicknameError && selected != null &&
            network.Profile == selected.profile && (preview == null || !preview.exists);
        private string NicknameKey(string profile) => activeEndpoint + "|" + profile;
        private bool NeedsNickname => selected != null && preview != null && !preview.exists &&
            !verifiedNicknames.Contains(NicknameKey(selected.profile));

        private void StartSelectedCharacter()
        {
            if (selected == null || joining || leaving || network == null || endpoint != activeEndpoint) return;
            string reason;
            string name = preview != null && preview.exists && !string.IsNullOrWhiteSpace(preview.displayName)
                ? preview.displayName : selected.displayName;
            if (network.StartCharacter(selected.profile, name, endpoint, out reason))
            { joining = true; message = null; previewRequest++; }
            else message = reason;
        }

        private void OpenNickname(bool forSelected)
        {
            if (joining || leaving || (forSelected && selected == null)) return;
            creating = true; checkingNickname = false; nicknameForSelected = forSelected;
            newName = forSelected ? selected.displayName : "";
            nicknameStatus = null; message = null; nicknameRequest++; focusNickname = true;
        }

        private void CloseNickname()
        {
            creating = false; checkingNickname = false; nicknameRequest++; nicknameStatus = null;
        }

        private void CheckNickname()
        {
            if (!creating || checkingNickname || network == null) return;
            string normalized;
            try { normalized = CharacterNickname.Normalize(newName); }
            catch (ArgumentException error) { nicknameStatus = error.Message; return; }
            newName = normalized; checkingNickname = true; nicknameStatus = "Verificando disponibilidade no servidor…";
            int request = ++nicknameRequest;
            if (testsSkipPreview)
            {
                CompleteNicknameCheck(request, new OnlineNicknameAvailability { available = nicknameTestAvailable,
                    nickname = normalized, message = nicknameTestNotice ?? "Este nick já está em uso." }, null);
                return;
            }
            // A new card has no identity. A pending existing card can reuse its credential
            // after a refused nickname without changing or replacing that credential.
            network.RequestNicknameAvailability(nicknameForSelected ? selected.profile : null,
                activeEndpoint, normalized, (result, error) => CompleteNicknameCheck(request, result, error));
        }

        private void CompleteNicknameCheck(int request, OnlineNicknameAvailability result, string error)
        {
            if (this == null || request != nicknameRequest || !creating) return;
            checkingNickname = false;
            if (!string.IsNullOrEmpty(error)) { nicknameStatus = error; return; }
            if (result == null || !result.available)
            { nicknameStatus = result == null ? "Não foi possível consultar este nick." : result.message ?? "Este nick já está em uso."; return; }
            try
            {
                newName = CharacterNickname.Normalize(result.nickname);
                if (!nicknameForSelected) { CreateCharacter(); return; }
                selected = CharacterRosterStore.UpdatePendingNickname(profileRoot, roster, selected.profile, newName);
                verifiedNicknames.Add(NicknameKey(selected.profile));
                creating = false; nicknameStatus = null;
                StartSelectedCharacter();
            }
            catch (Exception failure) { nicknameStatus = failure.Message; }
        }

        private void LeaveToCharacters()
        {
            if (network == null || leaving || joining) return;
            leaving = true; message = "Encerrando a sessão com segurança…";
            network.TryLeaveToMenu((accepted, reason) =>
            {
                if (this == null) return;
                leaving = false; message = reason;
                if (!accepted) return;
                frontEnd = true; open = false; inputFrame = Time.frameCount; InitializeFrontEnd();
            });
        }

        private void CancelConnection()
        {
            if (!joining || leaving || network == null) return;
            leaving = true;
            network.TryLeaveToMenu((accepted, reason) =>
            {
                if (this == null) return;
                leaving = false;
                if (accepted) { joining = false; message = "Conexão cancelada. Seu progresso foi preservado."; }
                else message = reason;
            });
        }

        private void Quit()
        {
            if (joining || leaving) return;
            if (frontEnd || network == null || !network.IsOnlineMode) { Application.Quit(); return; }
            leaving = true; message = "Salvando e encerrando a sessão…";
            network.TryLeaveToMenu((accepted, reason) =>
            {
                if (this == null) return;
                leaving = false; message = reason;
                if (accepted) Application.Quit();
            });
        }

        private void OpenGraphics()
        {
            if (joining || leaving) return;
            draft = graphics.Current; settingsScroll = Vector2.zero; page = Page.Graphics;
            if (!frontEnd) open = true;
            message = null; creating = false; confirmingQuit = false;
        }

        private void CloseGraphics() { draft = null; page = frontEnd ? Page.Characters : Page.Pause; }

        private void CreateCharacter()
        {
            if (roster == null || joining) return;
            try
            {
                CharacterRosterEntry entry = CharacterRosterStore.AddCharacter(profileRoot, roster, newName.Trim());
                // Reload the atomically persisted roster rather than keeping an uncertain in-memory mutation.
                roster = CharacterRosterStore.Load(profileRoot); creating = false; newName = "";
                verifiedNicknames.Add(NicknameKey(entry.profile));
                SelectCharacter(entry);
            }
            catch (Exception error) { nicknameStatus = error.Message; }
        }

        private void EnsureStyles()
        {
            if (title != null) return;
            title = Label(38, Gold, FontStyle.Bold);
            subtitle = Label(13, Muted);
            heading = Label(22, Pale, FontStyle.Bold); heading.wordWrap = true;
            body = Label(16, Pale); body.wordWrap = true;
            muted = Label(14, Muted); muted.wordWrap = true;
            small = Label(12, Muted); small.wordWrap = true;
            center = Label(13, Muted); center.alignment = TextAnchor.MiddleCenter;
            button = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Normal,
                padding = new RectOffset(12, 12, 8, 8), richText = false };
            button.normal.textColor = Pale;
            primary = new GUIStyle(button) { fontStyle = FontStyle.Bold, fontSize = 18 };
            primary.normal.textColor = Gold;
            field = new GUIStyle(GUI.skin.textField) { fontSize = 16, padding = new RectOffset(12,12,10,8), richText = false };
            card = new GUIStyle(button) { alignment = TextAnchor.UpperLeft, fontSize = 17, padding = new RectOffset(19,14,15,12) };
        }

        private static GUIStyle Label(int size, Color color, FontStyle style = FontStyle.Normal)
        {
            GUIStyle result = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, richText = false };
            result.normal.textColor = color; return result;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();
            bool mouseEvent = inputDiagnostics && (Event.current.rawType == EventType.MouseDown || Event.current.rawType == EventType.MouseUp);
            if (mouseEvent) Debug.Log("[MENU GUI INPUT] phase=before type=" + Event.current.type +
                " raw=" + Event.current.rawType + " pos=" + Event.current.mousePosition + " enabled=" + GUI.enabled + " focused=" + Application.isFocused);
            Matrix4x4 previous = GUI.matrix; Color tint = GUI.color; int depth = GUI.depth;
            GUI.depth = -120;
            float scale = Mathf.Max(.45f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
            float width = Screen.width / scale, height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            if (mouseEvent) Debug.Log("[MENU GUI INPUT] phase=scaled type=" + Event.current.type +
                " pos=" + Event.current.mousePosition + " enabled=" + GUI.enabled + " scale=" + scale +
                " hotControl=" + GUIUtility.hotControl);
            try
            {
                Fill(new Rect(0, 0, width, height), new Color(.012f,.02f,.028f,frontEnd ? .94f : .80f));
                Fill(new Rect(0, 0, width, 128), new Color(.014f,.022f,.03f,.94f));
                GUI.Label(new Rect(54, 26, 650, 53), "GUERRA DAS NAÇÕES", title);
                GUI.Label(new Rect(58, 84, 900, 25), frontEnd ? "1  ESCOLHA O PERSONAGEM     2  DEFINA SEU NICK     3  CONECTE AO MUNDO" : "MENU DO JOGO", subtitle);
                if (page == Page.Graphics) DrawGraphics(GraphicsPanelRect(width, height));
                else if (frontEnd) DrawCharacters(MainPanelRect(width, height));
                else DrawPause(width, height);
                GUI.Label(new Rect(45,height-43,width-90,25), frontEnd
                    ? "Seu progresso é carregado do servidor ao conectar.  •  Enter para conectar"
                    : "O mundo online continua em movimento enquanto o menu está aberto.", center);
                if (creating) DrawCreate(width,height);
                if (confirmingQuit) DrawQuit(width,height);
                if (graphics.HasPendingConfirmation) DrawDisplayConfirmation(width,height);
            }
            finally { GUI.matrix = previous; GUI.color = tint; GUI.depth = depth; }
        }

        private void DrawCharacters(Rect panel)
        {
            float left = 304f, right = 330f, gap = 18f;
            Rect rosterPanel = new Rect(panel.x,panel.y,left,panel.height);
            Rect details = new Rect(panel.xMax-right,panel.y,right,panel.height);
            Rect stage = new Rect(rosterPanel.xMax+gap,panel.y, panel.width-left-right-gap*2,panel.height);
            Panel(rosterPanel); Panel(details);
            GUI.Label(new Rect(rosterPanel.x+22,rosterPanel.y+20,260,32), "Personagens", heading);
            GUI.Label(new Rect(rosterPanel.x+22,rosterPanel.y+58,260,25), "Neste computador e servidor", small);
            Rect scroll = new Rect(rosterPanel.x+12,rosterPanel.y+97,left-24,rosterPanel.height-182);
            int count = roster != null && roster.entries != null ? roster.entries.Length : 0;
            rosterScroll = GUI.BeginScrollView(scroll,rosterScroll,new Rect(0,0,scroll.width-20,Mathf.Max(scroll.height,count*90f)));
            for (int i=0;i<count;i++)
            {
                CharacterRosterEntry entry = roster.entries[i];
                Rect cell = new Rect(3,i*90+2,scroll.width-27,78);
                bool current = selected != null && selected.profile == entry.profile;
                Color old = GUI.backgroundColor; GUI.backgroundColor = current ? new Color(.62f,.51f,.31f) : new Color(.22f,.28f,.32f);
                bool allowed = GUI.enabled; GUI.enabled = !joining && !leaving && !creating && !confirmingQuit;
                if (GUI.Button(cell, entry.displayName, card)) SelectCharacter(entry);
                GUI.enabled = allowed; GUI.backgroundColor = old;
                GUI.Label(new Rect(cell.x+19,cell.y+43,cell.width-30,22), current ? "SELECIONADO" : "Selecionar personagem", small);
                if (current) Fill(new Rect(cell.x,cell.y+7,3,cell.height-14),Gold);
            }
            GUI.EndScrollView();
            bool enabled = GUI.enabled; GUI.enabled = !joining && !leaving && string.IsNullOrEmpty(rosterError) && endpoint == activeEndpoint && !creating && !confirmingQuit;
            if (GUI.Button(new Rect(rosterPanel.x+20,rosterPanel.yMax-65,left-40,44), "+  Novo personagem",button))
                OpenNickname(false);
            GUI.enabled = enabled;

            if (heroPreview != null && heroPreview.Texture != null)
                GUI.DrawTexture(new Rect(stage.x,stage.y+3,stage.width,stage.height-59),heroPreview.Texture,ScaleMode.ScaleToFit,true);
            GUI.Label(new Rect(stage.x,stage.yMax-53,stage.width,27), preview != null && preview.exists ? "PRÉVIA DO PERSONAGEM" : "VISUAL DE REFERÊNCIA",center);
            GUI.Label(new Rect(stage.x,stage.yMax-28,stage.width,22), "Arraste para girar",center);
            if (heroPreview != null && Event.current.type==EventType.MouseDrag && stage.Contains(Event.current.mousePosition))
            { heroPreview.Rotate(Event.current.delta.x); Event.current.Use(); }

            // Long names, connection errors, and advanced fields scroll independently.
            // The connect/settings/exit actions always stay outside this clipped viewport.
            Rect information = ConnectionInfoRect(details);
            bool canDefineNickname = selected != null && ((preview != null && !preview.exists) || SelectedHasNicknameError);
            detailsScroll = GUI.BeginScrollView(information, detailsScroll,
                new Rect(0,0,information.width-20,(advanced?495:380)+(canDefineNickname?44:0)));
            float x=6, w=information.width-30, y=6;
            GUI.Label(new Rect(x,y,w,25),"PERSONAGEM SELECIONADO",small); y+=35;
            string name = preview != null && preview.exists && !string.IsNullOrEmpty(preview.displayName) ? preview.displayName : selected == null ? "Nenhum personagem" : selected.displayName;
            GUI.Label(new Rect(x,y,w,64),name,heading); y+=74;
            string level = preview != null && preview.exists ? "Nível "+preview.level : "Progresso disponível ao conectar";
            GUI.Label(new Rect(x,y,w,32),level,body); y+=39;
            GUI.Label(new Rect(x,y,w,44),preview != null && preview.exists ? MapName(preview.mapId) : "O mapa será confirmado pelo servidor",muted); y+=59;
            Fill(new Rect(x,y,w,1),new Color(.22f,.27f,.30f)); y+=20;
            GUI.Label(new Rect(x,y,w,22),"SERVIDOR",small); y+=26;
            GUI.Label(new Rect(x,y,w,54), joining && network != null ? network.Status : message ?? previewStatus ?? "Selecione um personagem",muted); y+=58;
            bool prior = GUI.enabled; GUI.enabled = !joining && !leaving && !creating && !confirmingQuit;
            if (canDefineNickname)
            {
                if (GUI.Button(new Rect(x,y,w,34), "Definir ou alterar nick", button)) OpenNickname(true);
                y+=44;
            }
            if (GUI.Button(new Rect(x,y,w,32),advanced ? "− Configuração de conexão" : "+ Configuração de conexão",button)) advanced=!advanced;
            y+=42;
            if (advanced)
            {
                GUI.Label(new Rect(x,y,w,22),"Endereço do servidor",small); y+=25;
                GUI.SetNextControlName("server-address"); string next=GUI.TextField(new Rect(x,y,w,42),endpoint ?? "",256,field); y+=48;
                if(next!=endpoint){endpoint=next;previewRequest++;preview=null;previewStatus="Clique em Atualizar após alterar o endereço.";}
                if(GUI.Button(new Rect(x,y,w,32),"Usar servidor e atualizar",button)) InitializeFrontEnd();
                y+=42;
            }
            GUI.enabled=prior;
            GUI.EndScrollView();
            Rect connect=ConnectActionRect(details);
            x=connect.x;w=connect.width;float bottom=connect.y;
            GUI.enabled=!leaving&&selected!=null&&string.IsNullOrEmpty(rosterError)&&endpoint==activeEndpoint&&!creating&&!confirmingQuit;
            if(MenuButton(connect,joining?"Cancelar conexão":NeedsNickname?"Definir nick e conectar":"Conectar",primary,"connect"))
            { if(joining)CancelConnection();else ConnectSelected(); }
            GUI.enabled=!joining&&!leaving&&!creating&&!confirmingQuit;
            if(MenuButton(new Rect(x,bottom+66,w,40),"Gráficos",button,"frontend-graphics")) OpenGraphics();
            if(MenuButton(new Rect(x,bottom+116,w,32),"Sair do jogo",button,"frontend-quit")) confirmingQuit=true;
            GUI.enabled=prior;
        }

        private void DrawPause(float width,float height)
        {
            Rect r=new Rect(width*.5f-238,Mathf.Max(145,height*.5f-226),476,452);Panel(r);
            GUI.Label(new Rect(r.x+30,r.y+24,r.width-60,35),"Menu do jogo",heading);
            GUI.Label(new Rect(r.x+30,r.y+67,r.width-60,43),"O personagem permanece no mundo online.",muted);
            bool prior=GUI.enabled;GUI.enabled=!leaving&&!confirmingQuit;
            float y=r.y+126;
            if(MenuButton(new Rect(r.x+30,y,r.width-60,48),"Continuar",primary,"continue")){open=false;inputFrame=Time.frameCount;}y+=61;
            if(MenuButton(new Rect(r.x+30,y,r.width-60,44),"Gráficos",button,"pause-graphics"))OpenGraphics();y+=57;
            if(MenuButton(new Rect(r.x+30,y,r.width-60,44),"Selecionar personagem",button,"pause-characters"))LeaveToCharacters();y+=57;
            if(MenuButton(new Rect(r.x+30,y,r.width-60,40),"Sair do jogo",button,"pause-quit"))confirmingQuit=true;
            GUI.enabled=prior;
            GUI.Label(new Rect(r.x+30,r.yMax-59,r.width-60,48),message??"Troque de personagem fora de combate.",small);
        }

        private void DrawGraphics(Rect r)
        {
            Panel(r);float x=r.x+30,w=r.width-60;
            GUI.Label(new Rect(x,r.y+20,w,35),"Configurações de gráficos",heading);
            GUI.Label(new Rect(x,r.y+62,w,34),"Escolha um perfil ou ajuste cada opção para o seu computador.",muted);
            if(draft==null)draft=graphics.Current;
            bool enabled=GUI.enabled;GUI.enabled=!graphics.HasPendingConfirmation;
            float gap=10,presetWidth=(w-3*gap)/4;
            string[] names={"Baixo","Médio","Alto","Ultra"};
            for(int i=0;i<4;i++)
            {
                Color previous=GUI.backgroundColor;GUI.backgroundColor=(int)draft.preset==i?new Color(.66f,.55f,.34f):new Color(.25f,.30f,.34f);
                if(GUI.Button(new Rect(x+i*(presetWidth+gap),r.y+108,presetWidth,42),names[i],button))
                    draft=PrototypeGraphicsSettings.CreatePreset((GraphicsPreset)i,draft);
                GUI.backgroundColor=previous;
            }
            Rect viewport=new Rect(x,r.y+167,w,r.height-280);
            settingsScroll=GUI.BeginScrollView(viewport,settingsScroll,new Rect(0,0,w-22,475));
            float row=0;string before=JsonUtility.ToJson(draft);
            GraphicsResolution[] resolutions=graphics.SupportedResolutions;
            string resolution=draft.width+" × "+draft.height;
            SettingRow(row,"Resolução",resolution,() => CycleResolution(-1,resolutions),() => CycleResolution(1,resolutions),w-24);row+=51;
            string[] modes={"Janela","Janela sem bordas",Application.platform==RuntimePlatform.OSXPlayer||Application.platform==RuntimePlatform.OSXEditor?"Maximizada":"Tela cheia"};
            SettingRow(row,"Modo de exibição",modes[Mathf.Clamp((int)draft.windowMode,0,2)],()=>draft.windowMode=(GraphicsWindowMode)(((int)draft.windowMode+2)%3),()=>draft.windowMode=(GraphicsWindowMode)(((int)draft.windowMode+1)%3),w-24);row+=51;
            string[] quality={"Desligadas","Baixas","Altas"};
            SettingRow(row,"Sombras",quality[Mathf.Clamp((int)draft.shadows,0,2)],()=>draft.shadows=(GraphicsShadowQuality)(((int)draft.shadows+2)%3),()=>draft.shadows=(GraphicsShadowQuality)(((int)draft.shadows+1)%3),w-24);row+=51;
            SettingRow(row,"Texturas",new[]{"Completa","Metade","Quarto"}[Mathf.Clamp(draft.textureQuality,0,2)],()=>draft.textureQuality=(draft.textureQuality+2)%3,()=>draft.textureQuality=(draft.textureQuality+1)%3,w-24);row+=51;
            SettingRow(row,"Suavização de bordas",draft.antiAliasing==0?"Desligada":draft.antiAliasing+"×",()=>draft.antiAliasing=Cycle(new[]{0,2,4,8},draft.antiAliasing,-1),()=>draft.antiAliasing=Cycle(new[]{0,2,4,8},draft.antiAliasing,1),w-24);row+=51;
            SettingRow(row,"Distância de visão",Mathf.RoundToInt(draft.viewDistance)+" m",()=>draft.viewDistance=Mathf.Max(60,draft.viewDistance-20),()=>draft.viewDistance=Mathf.Min(220,draft.viewDistance+20),w-24);row+=51;
            ToggleRow(row,"Efeitos de iluminação",ref draft.postProcessing,w-24);row+=44;
            ToggleRow(row,"Sincronização vertical",ref draft.vSync,w-24);row+=44;
            SettingRow(row,"Limite de quadros",draft.frameRateLimit<0?"Sem limite":draft.frameRateLimit+" FPS",()=>draft.frameRateLimit=Cycle(new[]{30,60,120,-1},draft.frameRateLimit,-1),()=>draft.frameRateLimit=Cycle(new[]{30,60,120,-1},draft.frameRateLimit,1),w-24);
            GUI.Label(new Rect(0,row+42,w-30,35),"Com sincronização ativada, a tela determina o limite de quadros.",small);
            if(before!=JsonUtility.ToJson(draft))draft.preset=GraphicsPreset.Custom;
            GUI.EndScrollView();
            bool changed=JsonUtility.ToJson(draft)!=JsonUtility.ToJson(graphics.Current);
            GUI.Label(new Rect(x,r.yMax-102,w,29),changed?"Alterações ainda não aplicadas":graphics.Status??"Configuração atual",small);
            float bw=(w-24)/3;
            if(GUI.Button(new Rect(x,r.yMax-61,bw,40),"Voltar",button))CloseGraphics();
            GUI.enabled=enabled&&!graphics.HasPendingConfirmation&&changed;
            if(GUI.Button(new Rect(x+bw+12,r.yMax-61,bw,40),"Descartar",button))draft=graphics.Current;
            if(GUI.Button(new Rect(x+2*(bw+12),r.yMax-61,bw,40),"Aplicar",primary))
            { graphics.Apply(draft);draft=graphics.Current; }
            GUI.enabled=enabled;
        }

        private void CycleResolution(int direction,GraphicsResolution[] values)
        {
            if(values==null||values.Length==0)return;
            int index=0;for(int i=0;i<values.Length;i++)if(values[i].width==draft.width&&values[i].height==draft.height)index=i;
            index=(index+direction+values.Length)%values.Length;draft.width=values[index].width;draft.height=values[index].height;
        }
        private static int Cycle(int[] values,int current,int direction)
        {int i=Array.IndexOf(values,current);if(i<0)i=0;return values[(i+direction+values.Length)%values.Length];}
        private void SettingRow(float y,string caption,string value,Action previous,Action next,float width)
        {
            GUI.Label(new Rect(0,y+7,width*.45f,32),caption,body);float right=width*.48f;
            if(GUI.Button(new Rect(right,y,37,36),"‹",button))previous();
            GUI.Label(new Rect(right+44,y+4,width-right-88,32),value,center);
            if(GUI.Button(new Rect(width-37,y,37,36),"›",button))next();
        }
        private void ToggleRow(float y,string caption,ref bool value,float width)
        {
            GUI.Label(new Rect(0,y+4,width*.6f,32),caption,body);
            if(GUI.Button(new Rect(width-195,y,195,34),value?"Ativado":"Desativado",button))value=!value;
        }

        private void DrawCreate(float width,float height)
        {
            Fill(new Rect(0,0,width,height),new Color(0,0,0,.76f));Rect r=new Rect(width*.5f-280,height*.5f-207,560,414);Panel(r);
            GUI.Label(new Rect(r.x+28,r.y+22,504,34),nicknameForSelected?"Defina seu nick":"Novo personagem",heading);
            GUI.Label(new Rect(r.x+28,r.y+69,504,57),"Seu nick identifica o personagem e precisa estar disponível no servidor.",muted);
            GUI.Label(new Rect(r.x+28,r.y+131,504,22),"NICK DO PERSONAGEM",small);
            bool prior=GUI.enabled;GUI.enabled=!checkingNickname;
            GUI.SetNextControlName("new-character-name");newName=GUI.TextField(new Rect(r.x+28,r.y+160,504,46),newName,24,field);
            if(focusNickname){GUI.FocusControl("new-character-name");focusNickname=false;}
            GUI.enabled=prior;
            GUI.Label(new Rect(r.x+28,r.y+217,504,41),"2 a 24 caracteres: letras, números, espaços, hífen e sublinhado.",small);
            GUI.Label(new Rect(r.x+28,r.y+266,504,57),nicknameStatus??"A disponibilidade será conferida antes de continuar.",muted);
            if(GUI.Button(new Rect(r.x+28,r.yMax-69,241,44),"Cancelar",button)){CloseNickname();message=null;}
            GUI.enabled=prior&&!checkingNickname&&!string.IsNullOrWhiteSpace(newName);
            string action=checkingNickname?"Verificando…":nicknameForSelected?"Verificar e conectar":"Verificar e criar";
            if(GUI.Button(new Rect(r.x+291,r.yMax-69,241,44),action,primary))CheckNickname();
            GUI.enabled=prior;
            if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Return&&!checkingNickname&&!string.IsNullOrWhiteSpace(newName))
            {CheckNickname();Event.current.Use();}
        }

        private void DrawQuit(float width,float height)
        {
            Fill(new Rect(0,0,width,height),new Color(0,0,0,.76f));Rect r=new Rect(width*.5f-250,height*.5f-128,500,256);Panel(r);
            GUI.Label(new Rect(r.x+28,r.y+24,444,36),"Sair do jogo?",heading);
            GUI.Label(new Rect(r.x+28,r.y+80,444,58),"O progresso confirmado permanece salvo no servidor.",muted);
            if(GUI.Button(new Rect(r.x+28,r.yMax-72,214,44),"Continuar aqui",button))confirmingQuit=false;
            if(GUI.Button(new Rect(r.x+258,r.yMax-72,214,44),"Sair",primary)){confirmingQuit=false;Quit();}
        }

        private void DrawDisplayConfirmation(float width,float height)
        {
            Fill(new Rect(0,0,width,height),new Color(0,0,0,.8f));Rect r=new Rect(width*.5f-277,height*.5f-152,554,304);Panel(r);
            GUI.Label(new Rect(r.x+28,r.y+24,498,38),"Manter esta configuração?",heading);
            GUI.Label(new Rect(r.x+28,r.y+85,498,75),"As opções de exibição serão restauradas em "+Mathf.CeilToInt(graphics.PendingRemaining)+" segundos se você não confirmar.",body);
            if(GUI.Button(new Rect(r.x+28,r.yMax-77,239,47),"Restaurar",button)){graphics.Revert();draft=graphics.Current;}
            if(GUI.Button(new Rect(r.x+285,r.yMax-77,239,47),"Manter",primary)){graphics.Confirm();draft=graphics.Current;}
        }

        private static string MapName(string value) => PrototypeCity.MapName(value);
        private static void Panel(Rect value)
        {Fill(value,new Color(.045f,.059f,.070f,.96f));Fill(new Rect(value.x,value.y,value.width,2),new Color(.46f,.39f,.27f));Fill(new Rect(value.x,value.yMax-1,value.width,1),new Color(.19f,.23f,.26f));}
        private static void Fill(Rect value,Color color)
        {Color old=GUI.color;GUI.color=color;GUI.DrawTexture(value,Texture2D.whiteTexture);GUI.color=old;}

        private bool MenuButton(Rect rectangle, string caption, GUIStyle style, string target)
        {
            EventType type = Event.current.type;
            bool trace = inputDiagnostics && (Event.current.rawType == EventType.MouseDown || Event.current.rawType == EventType.MouseUp);
            bool inside = trace && rectangle.Contains(Event.current.mousePosition);
            bool enabled = GUI.enabled;
            bool clicked = GUI.Button(rectangle, caption, style);
            if (trace || (inputDiagnostics && clicked))
                Debug.Log("[MENU GUI HIT] target=" + target + " type=" + type + " inside=" + inside +
                    " enabled=" + enabled + " clicked=" + clicked + " hotControl=" + GUIUtility.hotControl);
            return clicked;
        }

        private void OnDestroy()
        {previewRequest++;if(heroPreview!=null)heroPreview.Dispose();if(active==this)active=null;}
    }

    internal sealed class MenuHeroPreview : IDisposable
    {
        private const int PreviewLayer=11;
        private readonly GameObject root;
        private readonly Transform model;
        private readonly Camera camera;
        private readonly PrototypeEquipmentVisual equipment;
        private readonly List<Material> materials=new List<Material>();
        private readonly List<EquipmentSlot> slots=new List<EquipmentSlot>();
        private readonly List<string> finishes=new List<string>();
        public RenderTexture Texture {get;private set;}
        public bool HasGameplayComponents => root.GetComponentInChildren<PrototypeCombat>(true)!=null||
            root.GetComponentInChildren<PrototypeCharacter>(true)!=null||root.GetComponentInChildren<PrototypeOnlineClient>(true)!=null||
            root.GetComponentInChildren<Collider>(true)!=null;

        public MenuHeroPreview(PrototypeCharacter source)
        {
            root=new GameObject("Prévia visual do menu");root.SetActive(false);
            root.transform.position=new Vector3(-4000,200,-4000);
            GameObject clone=UnityEngine.Object.Instantiate(source.VisualRoot.gameObject,root.transform,false);
            clone.name="Modelo visual sem lógica de jogo";model=clone.transform;
            model.localPosition=Vector3.zero;model.localRotation=Quaternion.Euler(0,17,0);
            foreach(MonoBehaviour component in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if(!(component is PrototypeEquipmentVisual)){component.enabled=false;UnityEngine.Object.DestroyImmediate(component);}
            foreach(Collider collider in clone.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
            foreach(Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true))UnityEngine.Object.DestroyImmediate(body);
            foreach(AudioSource audio in clone.GetComponentsInChildren<AudioSource>(true))UnityEngine.Object.DestroyImmediate(audio);
            equipment=clone.GetComponentInChildren<PrototypeEquipmentVisual>(true);
            Animator animator=clone.GetComponentInChildren<Animator>(true);
            if(animator!=null){animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
            CreatePodium();
            Light key=CreateLight("Luz quente da prévia",new Color(1,.89f,.72f),1.6f,new Vector3(35,-28,0));
            CreateLight("Recorte frio da prévia",new Color(.48f,.69f,1),1.4f,new Vector3(18,150,0));
            CreateLight("Preenchimento da prévia",new Color(.68f,.79f,.87f),.65f,new Vector3(18,48,0));
            GameObject cameraRoot=new GameObject("Câmera exclusiva do menu");cameraRoot.transform.SetParent(root.transform,false);
            camera=cameraRoot.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.016f,.025f,.034f,1);
            camera.cullingMask=1<<PreviewLayer;camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.fieldOfView=32;
            camera.transform.localPosition=new Vector3(0,1.12f,4.3f);camera.transform.LookAt(root.transform.position+Vector3.up*.97f);
            camera.allowHDR=false;camera.allowMSAA=true;camera.depth=-20;
            Texture=new RenderTexture(600,850,24,RenderTextureFormat.ARGB32){name="Prévia do personagem",antiAliasing=2};Texture.Create();camera.targetTexture=Texture;
            foreach(Transform child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=PreviewLayer;
            foreach(Camera worldCamera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if(worldCamera!=camera)worldCamera.cullingMask&=~(1<<PreviewLayer);
            root.SetActive(true);
            if(animator!=null){animator.Play("Idle",0,0);animator.Update(0);}
        }

        private Light CreateLight(string name,Color color,float intensity,Vector3 rotation)
        {
            GameObject rig=new GameObject(name);rig.transform.SetParent(root.transform,false);rig.transform.localRotation=Quaternion.Euler(rotation);
            Light light=rig.AddComponent<Light>();light.type=LightType.Directional;light.color=color;light.intensity=intensity;light.cullingMask=1<<PreviewLayer;light.shadows=LightShadows.None;return light;
        }
        private void CreatePodium()
        {
            GameObject podium=GameObject.CreatePrimitive(PrimitiveType.Cylinder);podium.name="Pedestal do personagem";podium.transform.SetParent(root.transform,false);
            podium.transform.localPosition=new Vector3(0,-.09f,0);podium.transform.localScale=new Vector3(1.75f,.08f,1.75f);
            UnityEngine.Object.DestroyImmediate(podium.GetComponent<Collider>());
            Material stone=new Material(Shader.Find("Standard")){name="Pedra do pedestal"};stone.color=new Color(.13f,.16f,.18f);stone.SetFloat("_Metallic",.35f);stone.SetFloat("_Glossiness",.28f);materials.Add(stone);podium.GetComponent<Renderer>().sharedMaterial=stone;
        }
        public void ApplyEquipment(EquipmentSlotSave[] equipped,bool starter)
        {
            if(equipment==null)return;slots.Clear();finishes.Clear();
            if(starter)
                foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot))){slots.Add(slot);finishes.Add("training");}
            else if(equipped!=null)
                foreach(EquipmentSlotSave item in equipped)
                {
                    if(item==null)continue;string definition=string.IsNullOrEmpty(item.definitionId)?item.itemId:item.definitionId;
                    EquipmentItem value=EquipmentCatalog.GetDefinition(definition,item.definitionRevision>0?item.definitionRevision:1);
                    if(value==null)continue;slots.Add((EquipmentSlot)item.slot);finishes.Add(value.AppearanceId);
                }
            equipment.ApplySlots(slots,finishes);
        }
        public void SetVisible(bool value){if(root!=null&&root.activeSelf!=value)root.SetActive(value);}
        public void Rotate(float amount){if(model!=null)model.Rotate(0,-amount*.6f,0,Space.Self);}
        public void Dispose()
        {
            if(camera!=null)camera.targetTexture=null;
            if(root!=null)UnityEngine.Object.Destroy(root);
            if(Texture!=null){Texture.Release();UnityEngine.Object.Destroy(Texture);Texture=null;}
            foreach(Material material in materials)UnityEngine.Object.Destroy(material);
        }
    }
}
