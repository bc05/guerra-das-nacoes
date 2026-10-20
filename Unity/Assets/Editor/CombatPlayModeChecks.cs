using System;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CombatPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.AreaChecks.";
    private static PrototypeCombat player;
    private static PrototypeCharacter character;
    private static PrototypeMonster front, side, rear;
    private static GameObject wall;
    private static float nextAt;
    private static int healthBefore;
    private static int rewardTotal;
    private static PlayerProgression expectedProgression;

    static CombatPlayModeChecks() { EditorApplication.update += Tick; }
    public static void Run()
    {
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 75f);
        SessionState.SetBool(Key + "Active", true);
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "tempo máximo excedido");
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            int stage = SessionState.GetInt(Key + "Stage", 0);
            switch (stage)
            {
                case 0:
                    player = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
                    character = player.GetComponent<PrototypeCharacter>();
                    Require(player.Monsters.Count == 3, "três inimigos configurados para demonstrar área");
                    front = player.Monsters[0]; side = player.Monsters[1]; rear = player.Monsters[2];
                    rewardTotal = 0;
                    int largestHealth = 0;
                    foreach (PrototypeMonster enemy in player.Monsters)
                    {
                        Require(enemy.MaxHealth >= 240 && enemy.AttackDamage >= 10 && enemy.ExperienceReward >= 120,
                            "guardiões possuem perfil de vida, dano e experiência ampliados");
                        Require(!string.IsNullOrWhiteSpace(enemy.DisplayName), "inimigos possuem nomes de interface");
                        Require(enemy.CurrentHealth == enemy.MaxHealth, "Awake inicia vida cheia conforme perfil serializado");
                        largestHealth = Mathf.Max(largestHealth, enemy.MaxHealth);
                        rewardTotal += enemy.ExperienceReward;
                        enemy.enabled = false;
                    }
                    Require(largestHealth >= 600, "encontro inclui elite com pelo menos 600 HP");
                    expectedProgression = new PlayerProgression();
                    expectedProgression.GrantExperience(rewardTotal);
                    CheckProfileConfiguration();
                    Fixture();
                    player.SetTarget(null);
                    Require(player.TryAttack(), "golpe sem seleção funciona");
                    Require(front.CurrentHealth == front.MaxHealth && side.CurrentHealth == side.MaxHealth, "dano aguarda passagem da lâmina");
                    Require(!player.TryAttack(), "recarga impede golpe duplicado");
                    Next(1, .28f); break;
                case 1:
                    Require(front.CurrentHealth == front.MaxHealth - 18 && side.CurrentHealth == side.MaxHealth - 18, "varredura frontal causa18 em dois inimigos");
                    Require(rear.CurrentHealth == rear.MaxHealth, "básico não atinge inimigo atrás");
                    Require(player.LastHitCount == 2, "um acerto por inimigo");
                    Fixture();
                    // An extra collider on one victim must not multiply damage.
                    BoxCollider extra = rear.gameObject.AddComponent<BoxCollider>();
                    extra.center = Vector3.up; extra.size = Vector3.one * .6f;
                    Physics.SyncTransforms();
                    player.SetTarget(rear);
                    Require(player.TryAttack(), "seleção atrás direciona o golpe ao alvo");
                    Require(Vector3.Dot(player.transform.forward, Vector3.back) > .99f, "seleção gira personagem antes do impacto");
                    Next(2, .28f); break;
                case 2:
                    Require(front.CurrentHealth == front.MaxHealth && side.CurrentHealth == side.MaxHealth && rear.CurrentHealth == rear.MaxHealth - 18, "alvo selecionado recebe o arco direcionado e vítimas anteriores ficam fora");
                    Fixture();
                    Move(side.transform, new Vector3(0, .06f, 4f));
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = new Vector3(0, 1.2f, 1.1f);
                    wall.transform.localScale = new Vector3(1.6f, 3f, .18f);
                    Physics.SyncTransforms();
                    Require(player.TryAttack(), "golpe pode ser executado contra obstáculo");
                    Next(3, .28f); break;
                case 3:
                    Require(front.CurrentHealth == front.MaxHealth && side.CurrentHealth == side.MaxHealth, "parede e distância bloqueiam dano");
                    Require(player.LastHitCount == 0, "golpe vazio não produz acerto");
                    wall.SetActive(false); UnityEngine.Object.Destroy(wall); wall = null;
                    Fixture();
                    Require(player.GetComponent<PrototypeAbilities>().TryUse(1), "onda pesada funciona sem selecionar");
                    Next(4, .28f); break;
                case 4:
                    Require(front.CurrentHealth == front.MaxHealth - 32 && side.CurrentHealth == side.MaxHealth - 32 && rear.CurrentHealth == rear.MaxHealth - 32, "onda360 causa32 nos três inimigos");
                    Require(player.LastHitCount == 3, "onda registra três vítimas sem duplicar");
                    Fixture();
                    Move(front.transform, new Vector3(0, .06f, 4.5f));
                    Move(side.transform, new Vector3(4.5f, .06f, 4f));
                    Move(rear.transform, new Vector3(-4.5f, .06f, 4f));
                    Require(player.TryAttack(), "golpe no vazio executa animação e recarga");
                    Require(player.GetComponent<PrototypeAttackEffects>().IsPlaying && player.GetComponent<PrototypeAreaEffects>().IsPlaying,
                        "golpe livre possui lâmina e varredura visível");
                    Next(5, .28f); break;
                case 5:
                    Require(player.LastHitCount == 0 && front.CurrentHealth == front.MaxHealth, "golpe vazio preserva inimigos");
                    Fixture();
                    Require(player.TryAttack(), "fixture de cancelamento por morte");
                    player.ReceiveDamage(int.MaxValue);
                    Require(!player.IsAlive && !player.HasPendingStrike, "morte cancela impacto pendente");
                    Next(6, .28f); break;
                case 6:
                    Require(front.CurrentHealth == front.MaxHealth && side.CurrentHealth == side.MaxHealth, "morto não causa dano atrasado");
                    Fixture();
                    Require(player.TryAttack(), "fixture de cancelamento por reinício");
                    player.ResetEncounter();
                    Next(7, .28f); break;
                case 7:
                    Require(front.CurrentHealth == front.MaxHealth && !player.HasPendingStrike, "reinício cancela dano pendente");
                    Require(!player.GetComponent<PrototypeAttackEffects>().IsPlaying && !player.GetComponent<PrototypeAreaEffects>().IsPlaying,
                        "reinício limpa todos os efeitos");
                    Fixture();
                    // Reduce health directly to isolate the final area strike without waiting through many attacks.
                    foreach (PrototypeMonster enemy in player.Monsters)
                        enemy.TakeDamage(enemy.MaxHealth - 18, player);
                    Require(player.GetComponent<PrototypeAbilities>().TryUse(1), "onda fatal múltipla");
                    Next(8, .28f); break;
                case 8:
                    Require(!front.IsAlive && !side.IsAlive && !rear.IsAlive, "onda derrota três inimigos");
                    Require(player.Stats.Level == expectedProgression.Level && player.Stats.Experience == expectedProgression.Experience,
                        "recompensas individuais são somadas e preservam excedente após evolução");
                    Require(player.LastExperience == rewardTotal && player.Message.Contains("Nível " + expectedProgression.Level),
                        "recompensa agregada mantém mensagem do nível alcançado");
                    front.TakeDamage(int.MaxValue, player);
                    Require(player.Stats.Experience == expectedProgression.Experience, "morto não concede recompensa repetida");
                    front.ConfigureStats(front.DisplayName, front.MaxHealth, front.AttackDamage, front.ExperienceReward);
                    Require(!front.IsAlive && front.CurrentHealth == 0, "reconfigurar perfil não ressuscita um morto");
                    player.ResetEncounter();
                    Require(player.Stats.Level == expectedProgression.Level && player.Stats.Experience == expectedProgression.Experience &&
                        player.Stats.Health == expectedProgression.MaxHealth, "reinício preserva nível/XP e restaura saúde");
                    foreach (PrototypeMonster enemy in player.Monsters) Require(enemy.IsAlive && enemy.CurrentHealth == enemy.MaxHealth, "reinício recupera todos");
                    Fixture();
                    Move(side.transform, new Vector3(5, .06f, 5)); Move(rear.transform, new Vector3(-5, .06f, 5));
                    front.enabled = true;
                    healthBefore = player.Stats.Health;
                    Require(player.TryAttack(), "novo ataque após evolução");
                    Next(9, .28f); break;
                case 9:
                    Require(front.CurrentHealth == front.MaxHealth - player.Stats.AttackDamage, "básico após evolução usa dano atualizado");
                    Next(10, 2f); break;
                case 10:
                    Require(player.Stats.Health < healthBefore && player.IsAlive, "guardião atingido persegue e revida");
                    front.TakeDamage(int.MaxValue, player);
                    Next(11, 6.3f); break;
                case 11:
                    Require(front.IsAlive && front.CurrentHealth == front.MaxHealth, "guardião reaparece com vida cheia");
                    Finish(true, "Perfis de monstros 240/600 HP, ataque livre, impacto sincronizado, arco frontal/onda360, múltiplos inimigos, paredes, distância, colliders duplicados, morte/reinício, XP, evolução, revide e reaparecimento."); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void CheckProfileConfiguration()
    {
        string name = front.DisplayName;
        int health = front.MaxHealth, damage = front.AttackDamage, reward = front.ExperienceReward;
        front.ConfigureStats("  Guardião de teste  ", -1, -1, -1);
        Require(front.DisplayName == "Guardião de teste" && front.MaxHealth == 1 && front.CurrentHealth == 1 &&
            front.AttackDamage == 0 && front.ExperienceReward == 0, "perfil sanitiza nome e valores inválidos");
        front.ConfigureStats(name, health, damage, reward);
        front.TakeDamage(18, player);
        front.ConfigureStats(name, health, damage, reward, false);
        Require(front.CurrentHealth == health - 18, "reconfiguração opcional preserva ferimentos");
        front.ConfigureStats(name, health, damage, reward);
        Require(front.CurrentHealth == health, "reconfiguração de vivo pode restaurar vida cheia");
    }
    private static void Fixture()
    {
        player.ResetEncounter();
        Move(player.transform, new Vector3(0, .04f, 0));
        player.transform.rotation = Quaternion.identity;
        Move(front.transform, new Vector3(0, .06f, 1.9f));
        Move(side.transform, new Vector3(1.1f, .06f, 1.8f));
        Move(rear.transform, new Vector3(0, .06f, -1.8f));
        player.SetTarget(null);
        Physics.SyncTransforms();
    }
    private static void Move(Transform target, Vector3 position)
    {
        CharacterController controller = target.GetComponent<CharacterController>();
        controller.enabled = false; target.position = position; controller.enabled = true;
        Physics.SyncTransforms();
    }
    private static void Next(int stage, float delay)
    { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (wall != null) UnityEngine.Object.Destroy(wall);
        if (success) Debug.Log("[COMBATE PLAY PASS] " + message);
        else Debug.LogError("[COMBATE PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
