using UnityEngine;

namespace GuerraDasNacoes
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private PrototypeCharacter character;
        [SerializeField] private PrototypeCombat combat;
        private PrototypeAbilities abilities;
        private PrototypeSkillIcons icons;
        private PrototypeEquipmentIcons equipmentIcons;
        private GUIStyle titleStyle, bodyStyle, smallStyle, tinyStyle, centeredStyle, numberStyle, damageStyle, wrappedStyle, wrappedSmallStyle, wrappedTinyStyle;
        private GUIStyle equipmentCaptionStyle, equipmentNameStyle, equipmentButtonStyle;
        private GUIStyle threatNameStyle, threatWorldStyle;
        private static bool showHelp;
        private static bool showAttributes;
        private static bool showEquipment;
        private static bool showAudio;
        private static bool hasTarget;
        private static int modalInputFrame = -1;
        private static readonly Color Panel = new Color(0.025f, 0.030f, 0.035f, 0.94f);
        private static readonly Color Edge = new Color(0.34f, 0.29f, 0.21f);
        private static readonly Color Gold = new Color(0.83f, 0.73f, 0.52f);
        private static readonly Color Muted = new Color(0.65f, 0.68f, 0.70f);
        private static float UiScale => Mathf.Clamp(Mathf.Min(Screen.height / 900f, Screen.width / 1200f), 0.7f, 1.4f);
        private static readonly Color Amber = new Color(0.96f, 0.61f, 0.20f);
        private static readonly EquipmentSlot[] EquipmentSlots = {
            EquipmentSlot.Head, EquipmentSlot.Necklace, EquipmentSlot.Shoulders, EquipmentSlot.Chest,
            EquipmentSlot.Hands, EquipmentSlot.Belt, EquipmentSlot.Sword, EquipmentSlot.Shield,
            EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.RingLeft, EquipmentSlot.RingRight
        };
        private readonly System.Collections.Generic.List<EquipmentItem> equipmentCandidates = new System.Collections.Generic.List<EquipmentItem>();
        private EquipmentSlot selectedEquipmentSlot = EquipmentSlot.Sword;
        private string selectedEquipmentItemId;
        private string equipmentNotice = "Selecione um espaço e escolha uma peça na mochila.";
        private string equipmentHoverText;
        private bool equipmentNoticeSucceeded = true;
        private Vector2 equipmentScroll;
        private Vector2 equipmentComparisonScroll;
        private int equipmentRevision = -1;
        private EquipmentState equipmentState;
        private string displayedEquipmentResultId;
        private static PrototypeOnlineClient Network => PrototypeOnlineClient.Active;
        private static bool UsesServer => Network != null && Network.IsOnlineMode;
        private static bool CanMutate => !UsesServer || (Network.CanPlay && !Network.HasPendingMutation);
        public static bool IsAttributesOpen => showAttributes;
        public static bool IsEquipmentOpen => showEquipment;
        public static bool IsAudioOpen => showAudio;
        public static bool IsAnyPanelOpen => IsPanelOpen;
        public static void CloseForEconomy() => ClosePanels();
        private static bool IsPanelOpen => showAttributes || showHelp || showEquipment || showAudio;
        public static bool BlocksGameplay => PrototypeGameMenu.BlocksGameplay || IsPanelOpen || PrototypeEconomyUi.BlocksGameplay || modalInputFrame == Time.frameCount || (UsesServer && !Network.CanPlay);
        private static Rect PlayerRect => new Rect(18f, 18f, 284f, 204f);
        private static Rect HotbarRect(float width, float height) => new Rect(width / 2f - 288f, height - 156f, 576f, 138f);
        private static Rect TargetRect(float width) => new Rect(width / 2f - 152f, 18f, 304f, 91f);
        private static Rect RegionRect(float width) => new Rect(width - 230f, 18f, 212f, 96f);
        private static Rect AudioRect(float width, float height) => new Rect(width / 2f - 230f, height / 2f - 196f, 460f, 392f);
        private static Rect HelpRect(float width, float height) => new Rect(width / 2f - 325f, height / 2f - 247f, 650f, 494f);
        private static Rect AttributesRect(float width, float height) => new Rect(width / 2f - 322f, height / 2f - 270f, 644f, 514f);
        private static Rect EquipmentRect(float width, float height) => new Rect(width - 400f, 18f, 382f, Mathf.Min(714f, height - 36f));
        private static Rect EquipmentComparisonRect(float width, float height)
        {
            float top = PlayerRect.yMax + 14f;
            return new Rect(18f, top, 342f, Mathf.Max(300f, height - 168f - top));
        }
        private static Rect EquipmentInventoryRect(Rect panel) => new Rect(panel.x + 30f, panel.y + 431f, 340f, 227f);
        private static Rect EquipmentInventoryCellRect(Rect inventory, int index) => new Rect(inventory.x + index % 7 * 46f, inventory.y + index / 7 * 46f, 43f, 43f);
        private static Rect EquipmentSlotRect(Rect panel, EquipmentSlot slot)
        {
            Vector2 position;
            switch (slot)
            {
                case EquipmentSlot.Head: position = new Vector2(166f, 84f); break;
                case EquipmentSlot.Shoulders: position = new Vector2(94f, 112f); break;
                case EquipmentSlot.Necklace: position = new Vector2(238f, 112f); break;
                case EquipmentSlot.Chest: position = new Vector2(166f, 146f); break;
                case EquipmentSlot.Belt: position = new Vector2(166f, 208f); break;
                case EquipmentSlot.Legs: position = new Vector2(166f, 270f); break;
                case EquipmentSlot.Feet: position = new Vector2(166f, 330f); break;
                case EquipmentSlot.Sword: position = new Vector2(22f, 160f); break;
                case EquipmentSlot.Shield: position = new Vector2(310f, 160f); break;
                case EquipmentSlot.Hands: position = new Vector2(22f, 284f); break;
                case EquipmentSlot.RingLeft: position = new Vector2(94f, 284f); break;
                default: position = new Vector2(238f, 284f); break;
            }
            return new Rect(panel.x + position.x, panel.y + position.y, 50f, slot == EquipmentSlot.Sword || slot == EquipmentSlot.Shield ? 104f : 46f);
        }
        private static Rect SlotRect(Rect bar, int slot) => new Rect(bar.x + 23f + slot * 67f, bar.y + 20f, 61f, 61f);

        public void Configure(PrototypeCharacter player)
        {
            character = player;
            combat = player.GetComponent<PrototypeCombat>();
            abilities = player.GetComponent<PrototypeAbilities>();
        }

        private void Awake()
        {
            if (combat == null && character != null) combat = character.GetComponent<PrototypeCombat>();
            if (combat != null) abilities = combat.GetComponent<PrototypeAbilities>();
            if (combat != null && combat.GetComponent<PrototypeTargetIndicator>() == null)
                combat.gameObject.AddComponent<PrototypeTargetIndicator>();
            showHelp = false;
            showAttributes = false;
            showEquipment = false;
            showAudio = false;
            modalInputFrame = -1;
        }

        private void Update()
        {
            if (PrototypeGameMenu.BlocksGameplay) { hasTarget = false; return; }
            if (Input.GetKeyDown(KeyCode.C)) ToggleAttributes();
            if (Input.GetKeyDown(KeyCode.H)) ToggleHelp();
            if (Input.GetKeyDown(KeyCode.I)) ToggleEquipment();
            if (Input.GetKeyDown(KeyCode.O)) ToggleAudio();
            if (Input.GetKeyDown(KeyCode.Escape) && IsPanelOpen) ClosePanels();
            if (showAttributes && combat != null)
            {
                if (Input.GetKeyDown(KeyCode.F6)) TryAllocate(HeroAttribute.Attack);
                if (Input.GetKeyDown(KeyCode.F7)) TryAllocate(HeroAttribute.Defense);
                if (Input.GetKeyDown(KeyCode.F8)) TryAllocate(HeroAttribute.Intelligence);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F12)) StartCoroutine(CapturePreview());
#endif
            hasTarget = combat != null && combat.Target != null;
            if (UsesServer && Network.LastEquipmentResult != null &&
                Network.LastEquipmentResult.commandId != displayedEquipmentResultId)
            {
                OnlineCommandResult result = Network.LastEquipmentResult;
                displayedEquipmentResultId = result.commandId;
                equipmentNoticeSucceeded = result.accepted;
                equipmentNotice = result.accepted ? "Equipamento confirmado e salvo no servidor." : result.message;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private System.Collections.IEnumerator CapturePreview()
        {
            yield return new WaitForEndOfFrame();
            string folder = System.IO.Path.Combine(Application.persistentDataPath, "Capturas");
            System.IO.Directory.CreateDirectory(folder);
            string path = System.IO.Path.Combine(folder, "Previa.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[PROTOTIPO CAPTURA] " + path);
        }
#endif

        public static bool ContainsScreenPoint(Vector3 point)
        {
            if (PrototypeGameMenu.BlocksGameplay) return true;
            float scale = UiScale, width = Screen.width / scale, height = Screen.height / scale;
            Vector2 position = new Vector2(point.x / scale, (Screen.height - point.y) / scale);
            Rect bar = HotbarRect(width, height);
            return PrototypeEconomyUi.ContainsScreenPoint(point) || PlayerRect.Contains(position) || RegionRect(width).Contains(position) || bar.Contains(position) ||
                (UsesServer && Network.MapId == "ruins" && Network.Snapshot?.bossWindows?.Length > 0 &&
                 new Rect(width - 250f, 126f, 232f, 83f).Contains(position)) ||
                (hasTarget && TargetRect(width).Contains(position)) ||
                (showHelp && HelpRect(width, height).Contains(position)) ||
                (showAttributes && AttributesRect(width, height).Contains(position)) ||
                (showEquipment && (EquipmentRect(width, height).Contains(position) || EquipmentComparisonRect(width, height).Contains(position))) ||
                (showAudio && AudioRect(width, height).Contains(position));
        }

        private void OnGUI()
        {
            if (PrototypeGameMenu.HidesHud) return;
            if (combat == null || abilities == null) return;
            EnsureStyles();
            float scale = UiScale, width = Screen.width / scale, height = Screen.height / scale;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.color = Color.white;
            try
            {
                DrawPlayer();
                DrawRegion(width);
                DrawTarget(width);
                DrawWorldTarget(scale);
                if (!IsPanelOpen) DrawBossWindow(width);
                if (combat.HasMessage)
                {
                    Rect message = new Rect(width / 2f - 290f, height - 201f, 580f, 40f);
                    Fill(message, new Color(0.015f, 0.02f, 0.024f, 0.86f));
                    GUI.Label(message, combat.Message, centeredStyle);
                }
                DrawHotbar(width, height);
                DrawFloatingFeedback(scale);
                if (!combat.IsAlive && !IsPanelOpen)
                {
                    Rect death = new Rect(width / 2f - 230f, height / 2f - 58f, 460f, 116f);
                    Frame(death, Edge);
                    GUI.Label(new Rect(death.x, death.y + 17f, death.width, 26f), "VOCÊ FOI DERROTADO", numberStyle);
                    GUI.Label(new Rect(death.x + 18f, death.y + 57f, death.width - 36f, 35f), "R para renascer · nível e XP mantidos", centeredStyle);
                }
                if (showHelp) DrawHelp(width, height);
                if (showAttributes) DrawAttributes(width, height);
                if (showEquipment) DrawEquipment(width, height);
                if (showAudio) DrawAudio(width, height);
                GUI.Label(new Rect(18f, height - 35f, 340f, 21f), UsesServer ? "Protótipo online · progresso no servidor" : "Modo de teste offline · sem salvamento", tinyStyle);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.enabled = oldEnabled;
            }
        }

        private void DrawPlayer()
        {
            Rect rect = PlayerRect;
            PlayerProgression stats = combat.Stats;
            Frame(rect, Edge);
            GUI.Label(new Rect(rect.x + 13f, rect.y + 8f, 205f, 25f), "GUERREIRO", titleStyle);
            GUI.Label(new Rect(rect.x + 215f, rect.y + 10f, 56f, 22f), "Nv. " + stats.Level, smallStyle);
            DrawBar(new Rect(rect.x + 13f, rect.y + 39f, 258f, 22f), stats.Health / (float)stats.MaxHealth,
                new Color(0.50f, 0.13f, 0.12f), stats.Health + " / " + stats.MaxHealth);
            DrawBar(new Rect(rect.x + 13f, rect.y + 67f, 258f, 17f), abilities.Mana / abilities.MaxMana,
                new Color(0.13f, 0.28f, 0.48f), "MP " + Mathf.FloorToInt(abilities.Mana) + " / " + abilities.MaxMana);
            GUI.Label(new Rect(rect.x + 13f, rect.y + 91f, 258f, 21f), "Dano " + stats.GetBasicDamage(abilities.OutgoingDamageMultiplier) + "   ·   8 MP/s", smallStyle);
            string buffs = abilities.IsGuarding ? "Guarda " + abilities.GuardRemaining.ToString("0.0") + " s" : "";
            if (abilities.IsDamageBoosted)
                buffs += (buffs.Length > 0 ? "   ·   " : "") + "Fúria " + abilities.DamageBoostRemaining.ToString("0.0") + " s";
            GUI.Label(new Rect(rect.x + 13f, rect.y + 112f, 258f, 20f), buffs.Length > 0 ? buffs : "Sem efeitos ativos", smallStyle);
            string points = stats.UnspentPoints > 0 ? " · " + stats.UnspentPoints + (stats.UnspentPoints == 1 ? " ponto livre" : " pontos livres") : "";
            bool previous = GUI.enabled;
            GUI.enabled = previous && !IsPanelOpen;
            if (GUI.Button(new Rect(rect.x + 13f, rect.y + 137f, 258f, 24f), "C · Atributos" + points)) ToggleAttributes();
            if (GUI.Button(new Rect(rect.x + 13f, rect.y + 168f, 258f, 24f), "I · Equipamentos e mochila")) ToggleEquipment();
            GUI.enabled = previous;
        }

        private void DrawRegion(float width)
        {
            Rect rect = RegionRect(width);
            Frame(rect, Edge);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, 190f, 21f), PrototypeCity.MapName(UsesServer ? Network.MapId : PrototypeCity.CurrentMapId).ToUpperInvariant(), smallStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 32f, 157f, 19f), PrototypeCity.IsInCity ? "Área segura · E para interagir" : PrototypeCity.CurrentMapId == "catacombs" ? "Catacumbas · saída ao sul" : "Área de caça · portal ao sul", tinyStyle);
            bool previous = GUI.enabled;
            GUI.enabled = previous && !IsPanelOpen;
            if (GUI.Button(new Rect(rect.xMax - 39f, rect.y + 29f, 27f, 23f), new GUIContent("?", "H · Controles"))) ToggleHelp();
            if (GUI.Button(new Rect(rect.x + 12f, rect.y + 62f, rect.width - 24f, 24f), "O · Som")) ToggleAudio();
            GUI.enabled = previous;
        }

        private void DrawTarget(float width)
        {
            PrototypeMonster target = combat.Target;
            if (target == null) return;
            Rect rect = TargetRect(width);
            Frame(rect, new Color(0.48f, 0.35f, 0.20f));
            threatNameStyle.normal.textColor = MonsterDanger.Tint(target.Level, combat.Stats.Level);
            GUI.Label(new Rect(rect.x + 13f, rect.y + 7f, 277f, 24f), target.LevelName, threatNameStyle);
            DrawBar(new Rect(rect.x + 13f, rect.y + 36f, 278f, 20f), target.CurrentHealth / (float)target.MaxHealth,
                new Color(0.46f, 0.14f, 0.12f), target.CurrentHealth + " / " + target.MaxHealth);
            string state = !target.IsAlive ? "Reaparece em " + Mathf.CeilToInt(target.RespawnRemaining) + " s"
                : target.IsReturning ? "Retornando ao posto"
                : target.IsPreparingAttack ? "Preparando golpe — afaste-se"
                : (combat.HasQueuedAttack ? "Aproximando · " : target.IsEngaged ? "Em combate · " : "") +
                    MonsterDanger.Describe(target.Level, combat.Stats.Level);
            GUI.Label(new Rect(rect.x + 13f, rect.y + 63f, 278f, 20f), state, tinyStyle);
        }

        private void DrawWorldTarget(float scale)
        {
            if (Camera.main == null) return;
            foreach (PrototypeMonster monster in combat.Monsters)
            {
                if (monster == null || !monster.IsAvailableForTargeting) continue;
                Vector3 point = Camera.main.WorldToScreenPoint(monster.HealthAnchor);
                float distance = Vector3.Distance(character.transform.position, monster.transform.position);
                if (point.z <= 0f || distance > 55f || point.x < 0f || point.x > Screen.width || point.y < 0f || point.y > Screen.height) continue;
                float x = point.x / scale, y = (Screen.height - point.y) / scale;
                threatWorldStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(x - 159f, y - 24f, 320f, 22f), monster.LevelName, threatWorldStyle);
                threatWorldStyle.normal.textColor = MonsterDanger.Tint(monster.Level, combat.Stats.Level);
                GUI.Label(new Rect(x - 160f, y - 25f, 320f, 22f), monster.LevelName, threatWorldStyle);
                if (distance > 24f && combat.Target != monster && !monster.IsBoss) continue;
                Rect bar = new Rect(x - 62f, y, 124f, 8f);
                if (combat.Target == monster) Fill(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), Gold);
                DrawBar(bar, monster.CurrentHealth / (float)monster.MaxHealth, new Color(0.53f, 0.16f, 0.13f), "");
            }
        }

        private void DrawBossWindow(float width)
        {
            if (!UsesServer || Network.MapId != "ruins" || Network.Snapshot?.bossWindows == null) return;
            OnlineBossWindow next = null;
            foreach (OnlineBossWindow window in Network.Snapshot.bossWindows)
                if (window != null && (next == null || window.active && !next.active ||
                    window.active == next.active && window.nextSpawnAt < next.nextSpawnAt)) next = window;
            if (next == null) return;
            Rect panel = new Rect(width - 250f, 126f, 232f, 83f);
            Frame(panel, next.active ? Amber : Edge);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 5f, panel.width - 20f, 24f), next.active ? "CHEFÃO PRESENTE" : "PRÓXIMO CHEFÃO", smallStyle);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 28f, panel.width - 20f, 21f), next.name, tinyStyle);
            double remaining = System.Math.Max(0, (next.active ? next.endsAt : next.nextSpawnAt) - Network.Snapshot.serverTime);
            int minutes = (int)System.Math.Min(100000, System.Math.Ceiling(remaining / 60d));
            string clock = minutes >= 60 ? minutes / 60 + " h " + minutes % 60 + " min" : minutes + " min";
            GUI.Label(new Rect(panel.x + 10f, panel.y + 51f, panel.width - 20f, 21f),
                next.active ? "Vale do norte · termina em " + clock : "Nasce em " + clock, tinyStyle);
        }

        private void DrawHotbar(float width, float height)
        {
            Rect bar = HotbarRect(width, height);
            Frame(bar, Edge);
            GUI.Label(new Rect(bar.x + 23f, bar.y + 1f, 270f, 18f), "HABILIDADES", tinyStyle);
            GUI.Label(new Rect(bar.xMax - 238f, bar.y + 1f, 215f, 18f), "Um clique por golpe · H controles", tinyStyle);
            int hovered = -1;
            Vector2 mouse = Event.current.mousePosition;
            for (int slot = 0; slot < PrototypeAbilities.SlotCount; slot++)
            {
                Rect rect = SlotRect(bar, slot);
                bool assigned = abilities.IsAssigned(slot);
                bool hover = rect.Contains(mouse);
                bool active = (slot == 2 && abilities.IsGuarding) || (slot == 4 && abilities.IsDamageBoosted);
                float cooldown = abilities.GetCooldownRemaining(slot);
                bool ready = assigned && combat.IsAlive && abilities.CanUse(slot);
                Fill(rect, active ? slot == 4 ? Amber : new Color(0.38f, 0.60f, 0.73f) : hover && assigned ? Gold : Edge);
                Rect inner = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
                Texture2D icon = icons.Get(slot);
                if (assigned && icon != null)
                {
                    Color old = GUI.color;
                    GUI.color = !combat.IsAlive ? new Color(0.4f, 0.4f, 0.4f) : Color.white;
                    GUI.DrawTexture(inner, icon, ScaleMode.StretchToFill);
                    GUI.color = old;
                }
                else
                {
                    Fill(inner, new Color(0.045f, 0.049f, 0.052f));
                    GUI.Label(inner, "·", numberStyle);
                }
                if (assigned && cooldown > 0f)
                {
                    Texture2D mask = icons.Cooldown(cooldown / Mathf.Max(0.01f, abilities.GetCooldownDuration(slot)));
                    if (mask != null) GUI.DrawTexture(inner, mask);
                    GUI.Label(inner, cooldown >= 1f ? Mathf.CeilToInt(cooldown).ToString() : cooldown.ToString("0.0"), numberStyle);
                }
                else if (assigned && abilities.Mana + 0.01f < abilities.GetManaCost(slot))
                {
                    Fill(inner, new Color(0.05f, 0.08f, 0.14f, 0.65f));
                    GUI.Label(inner, "MP", numberStyle);
                }
                Fill(new Rect(rect.x + 3f, rect.y + 3f, 17f, 17f), new Color(0.015f, 0.02f, 0.025f, 0.9f));
                GUI.Label(new Rect(rect.x + 7f, rect.y + 1f, 20f, 20f), (slot + 1).ToString(), smallStyle);
                if (assigned && abilities.GetManaCost(slot) > 0)
                    GUI.Label(new Rect(rect.x + 31f, rect.y + 41f, 27f, 18f), abilities.GetManaCost(slot).ToString(), tinyStyle);
                bool oldEnabled = GUI.enabled;
                GUI.enabled = assigned && combat.IsAlive && abilities.CanUse(slot) && !BlocksGameplay;
                if (GUI.Button(rect, new GUIContent("", abilities.GetName(slot)), GUIStyle.none))
                {
                    if (slot == 0) combat.TryAttack();
                    else abilities.TryUse(slot);
                }
                GUI.enabled = oldEnabled;
                GUI.color = ready || active ? Color.white : Muted;
                string caption = slot == 2 && abilities.IsGuarding ? abilities.GuardRemaining.ToString("0.0") + " s"
                    : slot == 4 && abilities.IsDamageBoosted ? abilities.DamageBoostRemaining.ToString("0.0") + " s" : ShortName(slot);
                GUI.Label(new Rect(rect.x - 2f, rect.y + 64f, rect.width + 4f, 18f), caption, tinyStyle);
                GUI.color = Color.white;
                if (hover) hovered = slot;
            }
            PlayerProgression stats = combat.Stats;
            Rect xp = new Rect(bar.x + 23f, bar.y + 106f, 530f, 5f);
            DrawBar(xp, stats.IsMaxLevel ? 1f : stats.Experience / (float)stats.ExperienceToNextLevel, Gold, "");
            GUI.Label(new Rect(bar.x + 23f, bar.y + 112f, 530f, 20f), stats.IsMaxLevel ? "Nível máximo" : "Nv. " + stats.Level + "    " + stats.Experience + " / " + stats.ExperienceToNextLevel + " XP", tinyStyle);
            if (hovered >= 0 && !IsPanelOpen) DrawTooltip(bar, hovered, width);
        }

        private static string ShortName(int slot)
        {
            switch (slot)
            {
                case 0: return "Ataque";
                case 1: return "Pesado";
                case 2: return "Guarda";
                case 3: return "Fôlego";
                case 4: return "Fúria";
                default: return "—";
            }
        }

        private void DrawTooltip(Rect bar, int slot, float width)
        {
            Rect anchor = SlotRect(bar, slot);
            Rect rect = new Rect(Mathf.Clamp(anchor.center.x - 180f, 18f, width - 378f), bar.y - 184f, 360f, 174f);
            Frame(rect, Edge);
            GUI.Label(new Rect(rect.x + 13f, rect.y + 8f, 334f, 25f), abilities.IsAssigned(slot) ? (slot + 1) + " · " + abilities.GetName(slot) : "Espaço livre", titleStyle);
            GUI.Label(new Rect(rect.x + 13f, rect.y + 38f, 334f, 77f), abilities.IsAssigned(slot) ? abilities.GetDescription(slot) : "Nenhuma habilidade atribuída a este espaço.", wrappedStyle);
            if (!abilities.IsAssigned(slot)) return;
            GUI.Label(new Rect(rect.x + 13f, rect.y + 118f, 334f, 20f), abilities.GetManaCost(slot) + " MP   ·   Recarga " + abilities.GetCooldownDuration(slot).ToString("0.#") + " s", smallStyle);
            string status = !combat.IsAlive ? "Indisponível enquanto derrotado"
                : (slot == 0 || slot == 1) && combat.Stats.EquippedWeapon == null ? "Equipe uma espada no painel I para atacar"
                : slot == 2 && abilities.IsGuarding ? "Ativa · " + abilities.GuardRemaining.ToString("0.0") + " s restantes"
                : slot == 4 && abilities.IsDamageBoosted ? "Ativa · " + abilities.DamageBoostRemaining.ToString("0.0") + " s restantes"
                : abilities.GetCooldownRemaining(slot) > 0f ? "Recarregando"
                : abilities.GlobalCooldownRemaining > 0f ? "Aguarde o intervalo entre habilidades"
                : abilities.Mana < abilities.GetManaCost(slot) ? "Mana insuficiente" : slot == 3 && combat.Stats.Health >= combat.Stats.MaxHealth ? "Sua vida já está completa"
                : abilities.CanUse(slot) ? "Pronto · pressione " + (slot + 1)
                : "Indisponível neste momento";
            GUI.Label(new Rect(rect.x + 13f, rect.y + 142f, 334f, 20f), status, tinyStyle);
        }

        private void DrawAttributes(float width, float height)
        {
            PlayerProgression stats = combat.Stats;
            Rect rect = AttributesRect(width, height);
            Frame(rect, Gold);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 14f, 355f, 29f), "ATRIBUTOS DO HERÓI", titleStyle);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 43f, 380f, 23f), "Nv. " + stats.Level + " · 5 pontos iniciais + 5 por nível", smallStyle);
            GUI.Label(new Rect(rect.x + 420f, rect.y + 42f, 195f, 24f), stats.UnspentPoints + (stats.UnspentPoints == 1 ? " PONTO LIVRE" : " PONTOS LIVRES"), centeredStyle);
            if (GUI.Button(new Rect(rect.xMax - 42f, rect.y + 12f, 28f, 28f), "×")) ClosePanels();
            Fill(new Rect(rect.x + 20f, rect.y + 73f, rect.width - 40f, 1f), Edge);

            DrawAttributeRow(rect, 82f, "ATAQUE", "Reforça ataques básicos e dano das habilidades.", stats.AttackPoints, stats.EquipmentBonuses.Attack, stats.EffectiveAttackPoints, HeroAttribute.Attack, "F6");
            DrawAttributeRow(rect, 140f, "DEFESA", "Reduz o dano recebido; combina com a Guarda de Ferro.", stats.DefensePoints, stats.EquipmentBonuses.Defense, stats.EffectiveDefensePoints, HeroAttribute.Defense, "F7");
            DrawAttributeRow(rect, 198f, "INTELIGÊNCIA", "Amplia dano e duração das habilidades mágicas.", stats.IntelligencePoints, stats.EquipmentBonuses.Intelligence, stats.EffectiveIntelligencePoints, HeroAttribute.Intelligence, "F8");

            Fill(new Rect(rect.x + 20f, rect.y + 266f, rect.width - 40f, 1f), Edge);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 274f, 600f, 22f), "RESULTADO ATUAL · inclui encantamentos ativos", smallStyle);
            int basicDamage = stats.GetBasicDamage(abilities.OutgoingDamageMultiplier);
            int magicDamage = stats.GetSkillDamage(1.8d, true, abilities.OutgoingDamageMultiplier);
            DrawAttributeResult(rect, 302f, "Dano básico  " + basicDamage, "Corte circular  " + magicDamage);
            DrawAttributeResult(rect, 324f, "Ataque básico  " + Bonus(stats.PhysicalAttackMultiplier), "Poder das habilidades  " + Bonus(stats.SkillAttackMultiplier));
            DrawAttributeResult(rect, 346f, "Dano mágico  " + Bonus(stats.MagicDamageMultiplier), "Duração mágica  " + Bonus(stats.MagicDurationMultiplier));
            DrawAttributeResult(rect, 368f, "Mitigação por defesa  " + ((1d - stats.DamageTakenMultiplier) * 100d).ToString("0.#") + "%", abilities.IsDamageBoosted ? "Fúria ativa  " + Bonus(abilities.OutgoingDamageMultiplier) : "Fúria inativa");

            Fill(new Rect(rect.x + 20f, rect.y + 397f, rect.width - 40f, 1f), Edge);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 406f, 600f, 24f), "EQUIPAMENTOS · ARMADURA E ACESSÓRIOS", titleStyle);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 435f, 390f, 23f), "Espada: " + (stats.EquippedWeapon != null ? stats.EquippedWeapon.Name : "nenhuma equipada"), smallStyle);
            if (GUI.Button(new Rect(rect.xMax - 225f, rect.y + 434f, 205f, 28f), "I · Trocar equipamentos")) ToggleEquipment();
            GUI.Label(new Rect(rect.x + 20f, rect.y + 465f, 600f, 20f), "Proficiência em espada: " + stats.SwordProficiencyLevel + " · Valores = distribuídos + equipamentos", smallStyle);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 489f, 604f, 17f), "Clique em + para distribuir 1 ponto · F6 / F7 / F8 também funcionam · C / Esc: fechar", tinyStyle);
        }

        private void DrawAttributeRow(Rect panel, float y, string name, string description, int allocated, int equipment, int effective, HeroAttribute attribute, string shortcut)
        {
            Rect row = new Rect(panel.x + 14f, panel.y + y, panel.width - 28f, 53f);
            Fill(row, new Color(0.085f, 0.09f, 0.095f, 0.88f));
            GUI.Label(new Rect(row.x + 9f, row.y + 3f, 410f, 24f), name, titleStyle);
            GUI.Label(new Rect(row.x + 9f, row.y + 27f, 397f, 23f), description, smallStyle);
            GUI.Label(new Rect(row.x + 411f, row.y + 2f, 101f, 29f), effective.ToString(), numberStyle);
            GUI.Label(new Rect(row.x + 411f, row.y + 30f, 101f, 18f), allocated + " + " + equipment + " equip.", tinyStyle);
            bool previous = GUI.enabled;
            GUI.enabled = previous && CanMutate && combat.Stats.UnspentPoints > 0;
            if (GUI.Button(new Rect(row.xMax - 82f, row.y + 10f, 71f, 32f),
                new GUIContent("+1", "Investir 1 ponto em " + name + " · " + shortcut))) TryAllocate(attribute);
            GUI.enabled = previous;
        }

        private void DrawAttributeResult(Rect rect, float y, string left, string right)
        {
            GUI.Label(new Rect(rect.x + 20f, rect.y + y, 292f, 21f), left, bodyStyle);
            GUI.Label(new Rect(rect.x + 330f, rect.y + y, 294f, 21f), right, bodyStyle);
        }

        private void DrawEquipment(float width, float height)
        {
            PlayerProgression stats = combat.Stats;
            equipmentHoverText = null;
            RefreshEquipmentCandidates();
            Rect panel = EquipmentRect(width, height);
            DrawEquipmentFrame(panel);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 8f, 285f, 26f), "EQUIPAMENTOS", titleStyle);
            if (GUI.Button(new Rect(panel.xMax - 38f, panel.y + 8f, 26f, 26f), new GUIContent("×", "I ou Esc · fechar"), equipmentButtonStyle)) ClosePanels();
            GUI.Label(new Rect(panel.x + 18f, panel.y + 42f, 250f, 20f), "GUERREIRO  ·  NÍVEL " + stats.Level, smallStyle);
            if (GUI.Button(new Rect(panel.xMax - 113f, panel.y + 40f, 95f, 23f), "C · Atributos", equipmentButtonStyle)) ToggleAttributes();
            Fill(new Rect(panel.x + 18f, panel.y + 71f, panel.width - 36f, 1f), Edge);
            Rect paperDoll = new Rect(panel.x + 84f, panel.y + 88f, 214f, 302f);
            GUI.DrawTexture(paperDoll, equipmentIcons.Silhouette, ScaleMode.StretchToFill);
            // A paper doll mirrors the body's attachment points; the live hero uses the actual equipped meshes.
            foreach (EquipmentSlot slot in EquipmentSlots)
            {
                EquipmentItem equipped = stats.Equipment.GetEquipped(slot);
                Rect cell = EquipmentSlotRect(panel, slot);
                DrawEquipmentCell(cell, slot, equipped, slot == selectedEquipmentSlot, false, true);
                GUI.Label(new Rect(cell.x - 7f, cell.yMax + 1f, cell.width + 14f, 13f), ShortSlotName(slot), equipmentCaptionStyle);
                if (cell.Contains(Event.current.mousePosition))
                    equipmentHoverText = equipped == null ? EquipmentCatalog.GetSlotName(slot) + "\nEspaço vazio · clique para selecionar" : EquipmentTooltip(equipped, slot, true);
                if (GUI.Button(cell, new GUIContent("", EquipmentCatalog.GetSlotName(slot)), GUIStyle.none)) SelectEquipmentSlot(slot);
            }

            Fill(new Rect(panel.x + 18f, panel.y + 397f, panel.width - 36f, 1f), Edge);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 403f, 220f, 23f), "MOCHILA", titleStyle);
            GUI.Label(new Rect(panel.xMax - 145f, panel.y + 406f, 127f, 19f), stats.Equipment.Inventory.Count + " / 128 espaços", tinyStyle);
            Rect inventory = EquipmentInventoryRect(panel);
            int itemCount = stats.Equipment.Inventory.Count;
            int rows = Mathf.Max(5, Mathf.CeilToInt(itemCount / 7f));
            Rect content = new Rect(0f, 0f, inventory.width - (rows > 5 ? 18f : 0f), rows * 46f - 3f);
            bool scroll = rows > 5;
            Rect localInventory = new Rect(0f, 0f, inventory.width, inventory.height);
            if (scroll) equipmentScroll = GUI.BeginScrollView(inventory, equipmentScroll, content);
            else GUI.BeginGroup(inventory);
            int cells = Mathf.Max(35, itemCount);
            for (int index = 0; index < cells; index++)
            {
                Rect cell = EquipmentInventoryCellRect(localInventory, index);
                EquipmentItem item = index < itemCount ? stats.Equipment.Inventory[index] : null;
                bool selected = item != null && item.Id == selectedEquipmentItemId;
                bool compatible = item != null && item.FitsSlot(selectedEquipmentSlot);
                bool canEquip = item != null && stats.Equipment.CanEquip(item.Id, compatible ? selectedEquipmentSlot : item.Slot, out _);
                DrawEquipmentCell(cell, item != null ? item.Slot : EquipmentSlot.Chest, item, selected, item != null && !canEquip, false);
                if (item == null) continue;
                if (compatible && !selected) Fill(new Rect(cell.x + 6f, cell.yMax - 3f, cell.width - 12f, 1f), new Color(.52f, .58f, .60f));
                if (cell.Contains(Event.current.mousePosition)) equipmentHoverText = EquipmentTooltip(item, compatible ? selectedEquipmentSlot : item.Slot, false);
                if (GUI.Button(cell, new GUIContent("", item.Name), GUIStyle.none)) SelectInventoryItem(item);
            }
            if (scroll) GUI.EndScrollView(); else GUI.EndGroup();
            GUI.Label(new Rect(panel.x + 18f, panel.y + 666f, panel.width - 36f, 18f), "Clique na peça para comparar · I / Esc para fechar", tinyStyle);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 688f, panel.width - 36f, 18f), "Selecionado: " + EquipmentCatalog.GetSlotName(selectedEquipmentSlot), smallStyle);
            DrawEquipmentComparison(EquipmentComparisonRect(width, height));
            DrawEquipmentHover(width, height);
        }

        private void DrawEquipmentFrame(Rect rect)
        {
            Frame(rect, Edge);
            Fill(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, 32f), new Color(.075f, .082f, .082f));
            Fill(new Rect(rect.x + 4f, rect.y + 35f, rect.width - 8f, 1f), new Color(.45f, .38f, .25f));
            // Small metal corner inlays give the panel depth without borrowing a game's UI artwork.
            foreach (Vector2 corner in new[] { new Vector2(rect.x, rect.y), new Vector2(rect.xMax - 10f, rect.y), new Vector2(rect.x, rect.yMax - 10f), new Vector2(rect.xMax - 10f, rect.yMax - 10f) })
            {
                Fill(new Rect(corner.x + 2f, corner.y + 2f, 6f, 6f), new Color(.46f, .40f, .29f));
                Fill(new Rect(corner.x + 4f, corner.y + 4f, 2f, 2f), Gold);
            }
        }

        private void DrawEquipmentCell(Rect rect, EquipmentSlot slot, EquipmentItem item, bool selected, bool locked, bool equipmentSlot)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Color edge = selected ? Gold : hover ? new Color(.58f, .53f, .40f) : new Color(.26f, .29f, .29f);
            Fill(rect, edge);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), selected ? new Color(.12f, .13f, .12f) : new Color(.035f, .043f, .046f));
            Fill(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f), new Color(.064f, .072f, .073f));
            Fill(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, 1f), new Color(.36f, .37f, .32f, .4f));
            if (item != null || equipmentSlot)
            {
                Color previous = GUI.color;
                GUI.color = item == null ? new Color(.40f, .44f, .43f, .38f) : Color.white;
                float iconSize = Mathf.Min(rect.width - 7f, rect.height - 7f);
                Rect iconRect = new Rect(rect.center.x - iconSize * .5f, rect.center.y - iconSize * .5f, iconSize, iconSize);
                if (slot == EquipmentSlot.Sword && rect.height > rect.width) iconRect = new Rect(rect.x + 4f, rect.y + 8f, rect.width - 8f, rect.height - 16f);
                GUI.DrawTexture(iconRect, equipmentIcons.Get(slot), ScaleMode.StretchToFill);
                GUI.color = previous;
            }
            if (locked)
            {
                Fill(new Rect(rect.xMax - 12f, rect.yMax - 14f, 11f, 13f), new Color(.25f, .13f, .07f, .95f));
                GUI.Label(new Rect(rect.xMax - 12f, rect.yMax - 15f, 11f, 15f), "!", equipmentCaptionStyle);
            }
            if (selected)
            {
                Fill(new Rect(rect.x + 1f, rect.y + 1f, 6f, 2f), new Color(1f, .89f, .60f));
                Fill(new Rect(rect.x + 1f, rect.y + 1f, 2f, 6f), new Color(1f, .89f, .60f));
                Fill(new Rect(rect.xMax - 7f, rect.yMax - 3f, 6f, 2f), Gold);
                Fill(new Rect(rect.xMax - 3f, rect.yMax - 7f, 2f, 6f), Gold);
            }
        }

        private static string ShortSlotName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Head: return "Elmo";
                case EquipmentSlot.Shoulders: return "Ombreiras";
                case EquipmentSlot.Chest: return "Peitoral";
                case EquipmentSlot.Hands: return "Luvas";
                case EquipmentSlot.Legs: return "Calças";
                case EquipmentSlot.Feet: return "Botas";
                case EquipmentSlot.RingLeft: return "Anel 1";
                case EquipmentSlot.RingRight: return "Anel 2";
                default: return EquipmentCatalog.GetSlotName(slot);
            }
        }

        private string EquipmentTooltip(EquipmentItem item, EquipmentSlot slot, bool equipped)
        {
            bool allowed = equipped || combat.Stats.Equipment.CanEquip(item.Id, slot, out _);
            return item.Name + (item.Bound ? " · Vinculado" : "") + "\n" + item.Bonuses.Describe() + "\n" + item.Requirements.Describe() + "\n" +
                (equipped ? "Em uso · clique no espaço para remover ou substituir" : allowed ? "Requisitos atendidos · clique para comparar" : "Requisitos pendentes · clique para consultar");
        }

        private void DrawEquipmentHover(float width, float height)
        {
            if (string.IsNullOrEmpty(equipmentHoverText)) return;
            float tooltipHeight = wrappedSmallStyle.CalcHeight(new GUIContent(equipmentHoverText), 304f) + 22f;
            Vector2 mouse = Event.current.mousePosition;
            Rect tooltip = new Rect(Mathf.Clamp(mouse.x - 343f, 18f, width - 342f), Mathf.Clamp(mouse.y + 19f, 18f, height - tooltipHeight - 18f), 326f, tooltipHeight);
            Frame(tooltip, Gold);
            GUI.Label(new Rect(tooltip.x + 11f, tooltip.y + 10f, tooltip.width - 22f, tooltip.height - 20f), equipmentHoverText, wrappedSmallStyle);
        }

        private void DrawEquipmentComparison(Rect rect)
        {
            PlayerProgression stats = combat.Stats;
            EquipmentItem current = stats.Equipment.GetEquipped(selectedEquipmentSlot);
            EquipmentItem candidate = FindEquipmentCandidate();
            EquipmentBonuses projectedBonus = stats.Equipment.PreviewBonuses(selectedEquipmentSlot, candidate ?? current);
            bool canEquip = false;
            string reason = "Escolha uma peça na mochila para comparar.";
            if (candidate != null) canEquip = stats.Equipment.CanEquip(candidate.Id, selectedEquipmentSlot, out reason);
            DrawEquipmentFrame(rect);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 8f, rect.width - 32f, 25f), EquipmentCatalog.GetSlotName(selectedEquipmentSlot).ToUpperInvariant(), titleStyle);
            float scrollHeight = rect.height - 143f;
            float contentWidth = rect.width - 52f;
            string requirementsText = candidate == null ? "Os requisitos aparecerão ao selecionar uma peça." : candidate.Requirements.Describe();
            string statusText = !CanMutate ? UsesServer && Network.HasPendingMutation ? "Aguardando confirmação do servidor…" : "Aguarde a conexão com o servidor." : canEquip ? "Requisitos atendidos. A peça atual volta para a mochila." : reason;
            float requirementsHeight = Mathf.Max(34f, wrappedSmallStyle.CalcHeight(new GUIContent(requirementsText), contentWidth));
            float statusY = 411f + requirementsHeight + 9f;
            float statusHeight = Mathf.Max(35f, wrappedSmallStyle.CalcHeight(new GUIContent(statusText), contentWidth));
            Rect content = new Rect(0f, 0f, contentWidth, Mathf.Max(scrollHeight - 4f, statusY + statusHeight + 6f));
            equipmentComparisonScroll = GUI.BeginScrollView(new Rect(rect.x + 16f, rect.y + 47f, rect.width - 32f, scrollHeight), equipmentComparisonScroll, content);
            GUI.Label(new Rect(0f, 0f, contentWidth, 19f), "EM USO", tinyStyle);
            DrawEquipmentDetailCard(new Rect(0f, 23f, contentWidth, 80f), current, "Espaço vazio", "Sem bônus neste espaço.");
            GUI.Label(new Rect(0f, 114f, contentWidth, 19f), "SELECIONADO NA MOCHILA", tinyStyle);
            DrawEquipmentDetailCard(new Rect(0f, 137f, contentWidth, 91f), candidate, "Selecione uma peça", "Clique em um ícone da mochila.");
            GUI.Label(new Rect(0f, 239f, contentWidth, 20f), "ATRIBUTOS  ·  ATUAL → APÓS TROCA", tinyStyle);
            DrawEquipmentComparisonRow(content, 268f, "Ataque", stats.EffectiveAttackPoints, EquipmentBonuses.SaturatingAdd(stats.AttackPoints, projectedBonus.Attack));
            DrawEquipmentComparisonRow(content, 292f, "Defesa", stats.EffectiveDefensePoints, EquipmentBonuses.SaturatingAdd(stats.DefensePoints, projectedBonus.Defense));
            DrawEquipmentComparisonRow(content, 316f, "Inteligência", stats.EffectiveIntelligencePoints, EquipmentBonuses.SaturatingAdd(stats.IntelligencePoints, projectedBonus.Intelligence));
            DrawEquipmentComparisonRow(content, 340f, "Dano base", stats.EquippedBaseDamage, EquipmentBonuses.SaturatingAdd(stats.BaseAttackDamage, projectedBonus.Damage));
            DrawEquipmentComparisonRow(content, 364f, "Vida máxima", stats.MaxHealth, EquipmentBonuses.SaturatingAdd(stats.BaseMaxHealth, projectedBonus.Health));
            GUI.Label(new Rect(0f, 390f, contentWidth, 19f), "REQUISITOS · PONTOS DISTRIBUÍDOS", tinyStyle);
            GUI.Label(new Rect(0f, 411f, contentWidth, requirementsHeight), requirementsText, wrappedSmallStyle);
            Color previousColor = GUI.color;
            GUI.color = canEquip && CanMutate ? new Color(.60f, .79f, .64f) : Amber;
            GUI.Label(new Rect(0f, statusY, contentWidth, statusHeight), statusText, wrappedSmallStyle);
            GUI.color = previousColor;
            GUI.EndScrollView();
            float actionsY = rect.yMax - 82f;
            Fill(new Rect(rect.x + 16f, actionsY - 11f, rect.width - 32f, 1f), Edge);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && canEquip && CanMutate;
            if (GUI.Button(new Rect(rect.x + 16f, actionsY, 178f, 32f), current == null ? "Equipar peça" : "Substituir peça", equipmentButtonStyle)) EquipSelectedItem();
            GUI.enabled = previousEnabled && current != null && CanMutate;
            if (GUI.Button(new Rect(rect.x + 202f, actionsY, rect.width - 218f, 32f), "Remover", equipmentButtonStyle)) UnequipSelectedItem();
            GUI.enabled = previousEnabled;
            GUI.color = equipmentNoticeSucceeded ? Muted : Amber;
            string notice = UsesServer && combat.HasMessage ? combat.Message : equipmentNotice;
            GUI.Label(new Rect(rect.x + 16f, actionsY + 39f, rect.width - 32f, 36f), notice, wrappedTinyStyle);
            GUI.color = previousColor;
        }

        private void DrawEquipmentDetailCard(Rect rect, EquipmentItem item, string emptyName, string emptyDescription)
        {
            Fill(rect, new Color(.055f, .065f, .068f));
            if (item != null) GUI.DrawTexture(new Rect(rect.x + 4f, rect.y + 9f, 45f, 45f), equipmentIcons.Get(item.Slot));
            GUI.Label(new Rect(rect.x + 55f, rect.y + 3f, rect.width - 61f, 42f), item == null ? emptyName : item.Name, equipmentNameStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 49f, rect.width - 16f, rect.height - 50f), item == null ? emptyDescription : item.Bonuses.Describe(), wrappedTinyStyle);
        }

        private void EquipSelectedItem()
        {
            if (!CanMutate || combat == null) return;
            EquipmentItem candidate = FindEquipmentCandidate();
            if (candidate == null || !combat.Stats.Equipment.CanEquip(candidate.Id, selectedEquipmentSlot, out _)) return;
            EquipmentItem current = combat.Stats.Equipment.GetEquipped(selectedEquipmentSlot);
            string previousId = current != null ? current.Id : null;
            equipmentNoticeSucceeded = UsesServer ? Network.TryEquip(candidate.Id, selectedEquipmentSlot, out equipmentNotice) :
                combat.Stats.Equipment.TryEquip(candidate.Id, selectedEquipmentSlot, out equipmentNotice);
            if (equipmentNoticeSucceeded)
            {
                equipmentNotice = UsesServer ? "Troca enviada. Aguardando confirmação do servidor." : candidate.Name + " equipado. Bônus recalculados.";
                selectedEquipmentItemId = previousId;
                equipmentRevision = -1;
            }
            modalInputFrame = Time.frameCount;
        }

        private void UnequipSelectedItem()
        {
            if (!CanMutate || combat == null) return;
            EquipmentItem current = combat.Stats.Equipment.GetEquipped(selectedEquipmentSlot);
            if (current == null) return;
            equipmentNoticeSucceeded = UsesServer ? Network.TryUnequip(selectedEquipmentSlot, out equipmentNotice) :
                combat.Stats.Equipment.TryUnequip(selectedEquipmentSlot, out equipmentNotice);
            if (equipmentNoticeSucceeded)
            {
                equipmentNotice = UsesServer ? "Remoção enviada. Aguardando confirmação do servidor." : current.Name + " guardado na mochila.";
                selectedEquipmentItemId = current.Id;
                equipmentRevision = -1;
            }
            modalInputFrame = Time.frameCount;
        }

        private void SelectInventoryItem(EquipmentItem item)
        {
            if (item == null) return;
            // Rings may occupy either hand: preserve the explicitly selected ring slot.
            if (!item.FitsSlot(selectedEquipmentSlot)) SelectEquipmentSlot(item.Slot);
            selectedEquipmentItemId = item.Id;
            equipmentNotice = "Compare os valores e confirme a troca.";
            equipmentNoticeSucceeded = true;
            equipmentComparisonScroll = Vector2.zero;
            RefreshEquipmentCandidates();
        }

        private void DrawEquipmentComparisonRow(Rect panel, float y, string label, int value, int projected)
        {
            long change = (long)projected - value;
            Fill(new Rect(panel.x, panel.y + y, panel.width, 22f), new Color(.065f, .075f, .078f));
            GUI.Label(new Rect(panel.x + 5f, panel.y + y, 104f, 22f), label, smallStyle);
            GUI.Label(new Rect(panel.x + 110f, panel.y + y, 126f, 22f), value + "  →  " + projected, bodyStyle);
            Color previous = GUI.color;
            GUI.color = change > 0 ? new Color(.60f, .79f, .64f) : change < 0 ? new Color(.93f, .47f, .38f) : Muted;
            GUI.Label(new Rect(panel.x + 239f, panel.y + y, panel.width - 239f, 22f), change > 0 ? "+" + change : change.ToString(), smallStyle);
            GUI.color = previous;
        }

        private void SelectEquipmentSlot(EquipmentSlot slot)
        {
            if (selectedEquipmentSlot == slot) return;
            selectedEquipmentSlot = slot;
            selectedEquipmentItemId = null;
            equipmentRevision = -1;
            equipmentScroll = Vector2.zero;
            equipmentComparisonScroll = Vector2.zero;
            equipmentNotice = "Escolha uma peça na mochila e compare os atributos.";
            equipmentNoticeSucceeded = true;
        }

        private void RefreshEquipmentCandidates()
        {
            if (ReferenceEquals(equipmentState, combat.Stats.Equipment) && equipmentRevision == combat.Stats.Equipment.Revision) return;
            equipmentState = combat.Stats.Equipment;
            equipmentCandidates.Clear();
            foreach (EquipmentItem item in combat.Stats.Equipment.Inventory)
                if (item.FitsSlot(selectedEquipmentSlot)) equipmentCandidates.Add(item);
            if (FindEquipmentCandidate() == null)
                selectedEquipmentItemId = null;
            equipmentRevision = combat.Stats.Equipment.Revision;
        }

        private void TryAllocate(HeroAttribute attribute)
        {
            if (!CanMutate) return;
            if (UsesServer) Network.TryAllocate(attribute);
            else combat.Stats.TryAllocate(attribute);
        }

        private EquipmentItem FindEquipmentCandidate()
        {
            foreach (EquipmentItem item in equipmentCandidates)
                if (item.Id == selectedEquipmentItemId) return item;
            return null;
        }

        private static string Bonus(double multiplier)
        {
            double percent = (multiplier - 1d) * 100d;
            return (percent >= 0d ? "+" : "") + percent.ToString("0.#") + "%";
        }

        private static void ToggleAttributes()
        {
            showAttributes = !showAttributes;
            showHelp = false;
            showEquipment = false;
            showAudio = false;
            modalInputFrame = Time.frameCount;
        }

        private static void ToggleHelp()
        {
            showHelp = !showHelp;
            showAttributes = false;
            showEquipment = false;
            showAudio = false;
            modalInputFrame = Time.frameCount;
        }

        private static void ToggleEquipment()
        {
            showEquipment = !showEquipment;
            showAttributes = false;
            showHelp = false;
            showAudio = false;
            modalInputFrame = Time.frameCount;
        }

        private static void ToggleAudio()
        {
            showAudio = !showAudio;
            showAttributes = false;
            showHelp = false;
            showEquipment = false;
            modalInputFrame = Time.frameCount;
        }

        private static void ClosePanels()
        {
            showAttributes = false;
            showHelp = false;
            showEquipment = false;
            showAudio = false;
            modalInputFrame = Time.frameCount;
        }

        private void DrawHelp(float width, float height)
        {
            Rect rect = HelpRect(width, height);
            Frame(rect, Edge);
            GUI.Label(new Rect(rect.x + 22f, rect.y + 17f, 530f, 29f), "CONTROLES", titleStyle);
            string[] keys = { "WASD / setas", "Clique no chão", "Shift / scroll", "Q / E", "Botão direito", "1 / Espaço", "2", "3 · 4 · 5", "C / botão Atributos", "+ / F6 · F7 · F8", "I / botão Equipamentos", "Tab / clique no inimigo", "Esc", "R", "O / botão Som", "H" };
            string[] descriptions = { "Mover e cancelar golpe pendente", "Caminhar e cancelar golpe pendente", "Correr / aproximar câmera", "Girar a câmera", "Um golpe por clique; sem alvo, mirar no chão", "Um golpe por toque; segurar não repete", "Um corte circular; aproxima até o alvo", "Guarda · recuperar vida · aumentar dano", "Distribuir pontos com o mouse ou teclado", "+1 Ataque / Defesa / Inteligência no painel", "Escolher, comparar e trocar peças pelo mouse", "Selecionar alvo; sem mover ou atacar", "Fechar painel ou cancelar alvo/ataque", "Renascer após derrota no modo online", "Ajustar música, efeitos ou silenciar", "Abrir ou fechar esta ajuda" };
            for (int row = 0; row < keys.Length; row++)
            {
                GUI.Label(new Rect(rect.x + 22f, rect.y + 59f + row * 26f, 175f, 24f), keys[row], smallStyle);
                GUI.Label(new Rect(rect.x + 202f, rect.y + 59f + row * 26f, 425f, 24f), descriptions[row], bodyStyle);
            }
            GUI.Label(new Rect(rect.x + 22f, rect.y + 474f, 606f, 18f),
                "Perigo: cinza mínimo · verde baixo · amarelo próximo · laranja alto · vermelho mortal · roxo extremo", tinyStyle);
            if (GUI.Button(new Rect(rect.xMax - 42f, rect.y + 12f, 28f, 28f), "×")) ClosePanels();
        }

        private void DrawAudio(float width, float height)
        {
            Rect rect = AudioRect(width, height);
            Frame(rect, Edge);
            GUI.Label(new Rect(rect.x + 22f, rect.y + 17f, 360f, 28f), "SOM", titleStyle);
            if (GUI.Button(new Rect(rect.xMax - 42f, rect.y + 12f, 28f, 28f), "×")) ClosePanels();
            bool mute = GUI.Toggle(new Rect(rect.x + 22f, rect.y + 56f, 360f, 24f), PrototypeAudio.Muted, "  Silenciar todo o som");
            if (mute != PrototypeAudio.Muted) PrototypeAudio.Muted = mute;
            float master = DrawVolume(rect, 96f, "Volume geral", PrototypeAudio.MasterVolume);
            float music = DrawVolume(rect, 148f, "Música ambiente", PrototypeAudio.MusicVolume);
            float effects = DrawVolume(rect, 200f, "Efeitos de combate", PrototypeAudio.EffectsVolume);
            if (!Mathf.Approximately(master, PrototypeAudio.MasterVolume)) PrototypeAudio.MasterVolume = master;
            if (!Mathf.Approximately(music, PrototypeAudio.MusicVolume)) PrototypeAudio.MusicVolume = music;
            if (!Mathf.Approximately(effects, PrototypeAudio.EffectsVolume)) PrototypeAudio.EffectsVolume = effects;
            GUI.Label(new Rect(rect.x + 22f, rect.y + 252f, rect.width - 44f, 41f),
                "Música: A Kind Of Hope — Scott Buckley · CC BY 4.0\nwww.scottbuckley.com.au", wrappedSmallStyle);
            if (GUI.Button(new Rect(rect.x + 22f, rect.y + 301f, 192f, 26f), "Créditos da música"))
                Application.OpenURL("https://www.scottbuckley.com.au/library/a-kind-of-hope/");
            if (GUI.Button(new Rect(rect.x + 226f, rect.y + 301f, 212f, 26f), "Licença CC BY 4.0"))
                Application.OpenURL("https://creativecommons.org/licenses/by/4.0/");
            if (GUI.Button(new Rect(rect.x + 22f, rect.y + 342f, 174f, 27f), "Restaurar padrão"))
            {
                PrototypeAudio.MasterVolume = 1f;
                PrototypeAudio.MusicVolume = .08f;
                PrototypeAudio.EffectsVolume = .60f;
                PrototypeAudio.Muted = false;
            }
            GUI.Label(new Rect(rect.x + 210f, rect.y + 339f, 228f, 35f), "Ajustes salvos neste computador.\nO ou Esc para fechar.", wrappedTinyStyle);
        }

        private float DrawVolume(Rect panel, float y, string label, float value)
        {
            GUI.Label(new Rect(panel.x + 22f, panel.y + y - 7f, 330f, 21f), label, bodyStyle);
            GUI.Label(new Rect(panel.xMax - 63f, panel.y + y - 7f, 43f, 21f), Mathf.RoundToInt(value * 100f) + "%", smallStyle);
            return GUI.HorizontalSlider(new Rect(panel.x + 22f, panel.y + y + 19f, panel.width - 44f, 18f), value, 0f, 1f);
        }

        private void DrawFloatingFeedback(float scale)
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            foreach (PrototypeMonster monster in combat.Monsters)
                if (monster != null) FloatingText(camera, monster.HealthAnchor, monster.LastDamageTime, "−" + monster.LastDamage, Gold, scale);
            FloatingText(camera, combat.transform.position + Vector3.up * 2.1f, combat.LastDamageTime, "−" + combat.LastDamage, new Color(0.95f, 0.33f, 0.28f), scale);
            FloatingText(camera, combat.transform.position + Vector3.up * 2.7f, combat.LastExperienceTime, "+" + combat.LastExperience + " XP", new Color(0.64f, 0.78f, 0.92f), scale);
        }

        private void FloatingText(Camera camera, Vector3 anchor, float time, string text, Color color, float scale)
        {
            float elapsed = Time.time - time;
            if (elapsed < 0f || elapsed > 1.2f) return;
            Vector3 screen = camera.WorldToScreenPoint(anchor + Vector3.up * elapsed * 0.65f);
            if (screen.z <= 0f) return;
            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 1f - elapsed / 1.2f);
            GUI.Label(new Rect(screen.x / scale - 70f, (Screen.height - screen.y) / scale - 18f, 140f, 32f), text, damageStyle);
            GUI.color = previous;
        }

        private void DrawBar(Rect rect, float ratio, Color color, string text)
        {
            Fill(rect, new Color(0.012f, 0.015f, 0.018f));
            Fill(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * Mathf.Clamp01(ratio), rect.height - 2f), color);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * Mathf.Clamp01(ratio), 1f), new Color(1f, 1f, 1f, 0.16f));
            if (!string.IsNullOrEmpty(text)) GUI.Label(rect, text, centeredStyle);
        }

        private static void Frame(Rect rect, Color border)
        {
            Fill(new Rect(rect.x - 2f, rect.y + 3f, rect.width + 4f, rect.height + 3f), new Color(0f, 0f, 0f, 0.28f));
            Fill(rect, border);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), Panel);
            Fill(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, 1f), new Color(0.8f, 0.76f, 0.65f, 0.1f));
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Gold;
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            bodyStyle.normal.textColor = new Color(0.88f, 0.89f, 0.88f);
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 12 };
            smallStyle.normal.textColor = new Color(0.76f, 0.78f, 0.78f);
            tinyStyle = new GUIStyle(smallStyle) { fontSize = 11 };
            tinyStyle.normal.textColor = new Color(0.71f, 0.72f, 0.70f);
            centeredStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            centeredStyle.normal.textColor = Color.white;
            threatNameStyle = new GUIStyle(titleStyle) { fontSize = 13, clipping = TextClipping.Clip };
            threatWorldStyle = new GUIStyle(centeredStyle) { fontSize = 12, fontStyle = FontStyle.Bold };
            numberStyle = new GUIStyle(centeredStyle) { fontSize = 21, fontStyle = FontStyle.Bold };
            damageStyle = new GUIStyle(numberStyle) { fontSize = 22 };
            wrappedStyle = new GUIStyle(bodyStyle) { wordWrap = true };
            wrappedSmallStyle = new GUIStyle(smallStyle) { wordWrap = true };
            wrappedTinyStyle = new GUIStyle(tinyStyle) { wordWrap = true };
            icons = new PrototypeSkillIcons();
            equipmentIcons = new PrototypeEquipmentIcons();
            equipmentCaptionStyle = new GUIStyle(tinyStyle) { fontSize = 11, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            equipmentCaptionStyle.normal.textColor = new Color(.76f, .77f, .71f);
            equipmentNameStyle = new GUIStyle(wrappedStyle) { fontSize = 13, fontStyle = FontStyle.Bold };
            equipmentNameStyle.normal.textColor = new Color(.89f, .86f, .73f);
            equipmentButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            equipmentButtonStyle.normal.textColor = new Color(.90f, .87f, .76f);
        }

        private void OnDestroy()
        {
            if (icons != null) icons.Dispose();
            if (equipmentIcons != null) equipmentIcons.Dispose();
            showHelp = false;
            showAttributes = false;
            showEquipment = false;
            showAudio = false;
            hasTarget = false;
        }
    }
}
