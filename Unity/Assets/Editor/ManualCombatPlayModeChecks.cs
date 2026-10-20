using System;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Uses the same public selection, attack and cancellation commands as input.
// No input injection, server connection, profile access or persistence is involved.
[InitializeOnLoad]
public static class ManualCombatPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.ManualCombatChecks.";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 PlayerStart = new Vector3(500f, .04f, 500f);
    private static readonly Vector3 FarTarget = new Vector3(500f, .06f, 507.5f);
    private static readonly Vector3 NearTarget = new Vector3(500f, .06f, 502f);
    private static readonly Vector3 OtherTarget = new Vector3(506f, .06f, 502f);
    private enum Cancellation
    {
        Ground, MovementCommand, SameSelection, OtherSelection, Interface, PlayerDeath,
        CharacterDisabled, CombatDisabled, TargetInactive, TargetReturning, WeaponRemoved,
        TransportReset, Obstacle
    }
    private static readonly Cancellation[] QueuedCancellations = (Cancellation[])Enum.GetValues(typeof(Cancellation));
    private static readonly Cancellation[] CommittedCancellations =
    {
        Cancellation.Ground, Cancellation.MovementCommand, Cancellation.SameSelection,
        Cancellation.OtherSelection, Cancellation.Interface, Cancellation.CharacterDisabled
    };
    private static PrototypeCharacter character;
    private static PrototypeCombat combat;
    private static PrototypeAbilities abilities;
    private static PrototypeOnlineClient network;
    private static PrototypeMonster enemy, other;
    private static Animator animator;
    private static GameObject floor, wall;
    private static float nextAt, manaBefore;
    private static int checks, swingsBefore, healthBefore, otherHealthBefore, heavyDamage, cancellationIndex;
    private static string swordId, originalProgress;

    static ManualCombatPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar seleção e combate manual")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 120f);
        SessionState.SetBool(Key + "Active", true);
        checks = 0; nextAt = 0f; cancellationIndex = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Guard(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0f), "timeout");
            if (!EditorApplication.isPlaying)
            {
                Guard(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 4 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    character = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
                    Require(character != null, "personagem da cena presente");
                    combat = character.GetComponent<PrototypeCombat>();
                    abilities = character.GetComponent<PrototypeAbilities>();
                    network = character.GetComponent<PrototypeOnlineClient>();
                    network.ConfigureForTests(false);
                    Require(combat.Monsters.Count >= 2, "dois alvos reais disponíveis para troca de seleção");
                    foreach (PrototypeMonster monster in combat.Monsters) monster.enabled = false;
                    enemy = combat.Monsters[0]; other = combat.Monsters[1];
                    animator = character.VisualRoot.GetComponentInChildren<Animator>();
                    swordId = combat.Stats.Equipment.GetEquipped(EquipmentSlot.Sword).Id;
                    originalProgress = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.name = "Piso temporário do combate manual"; floor.layer = 8;
                    floor.transform.position = new Vector3(500f, -.5f, 500f);
                    floor.transform.localScale = new Vector3(40f, 1f, 40f);
                    Fixture(FarTarget); Capture();
                    Require(character.SelectAttackTarget(enemy) && character.SelectAttackTarget(enemy),
                        "selecionar e reselecionar um alvo distante são aceitos");
                    Require(combat.Target == enemy && !combat.HasQueuedAttack && !character.HasDestination,
                        "seleção distante não registra ataque nem destino de caminhada");
                    Next(1, QuietPeriod); break;
                case 1:
                    RequireQuiet("seleção distante, incluindo clique repetido no mesmo alvo");
                    Require(Vector3.ProjectOnPlane(character.transform.position - PlayerStart, Vector3.up).magnitude < .04f && !character.IsMoving,
                        "personagem permanece parado ao selecionar alvo distante");
                    Fixture(NearTarget); Capture();
                    Require(character.SelectAttackTarget(enemy), "seleção próxima é aceita");
                    Next(2, QuietPeriod); break;
                case 2:
                    RequireQuiet("seleção próxima");
                    Require(Vector3.ProjectOnPlane(character.transform.position - PlayerStart, Vector3.up).magnitude < .04f,
                        "seleção próxima também não move o personagem");
                    StartBasic(0);
                    Next(3, .24f); break;
                case 3:
                    RequireOneBasicHit("primeiro comando manual");
                    Next(4, QuietPeriod); break;
                case 4:
                    RequireOneBasicHit("primeiro comando após mais de duas recargas");
                    Capture(); StartBasic(1);
                    Next(5, .24f); break;
                case 5:
                    RequireOneBasicHit("segundo comando manual");
                    Next(6, QuietPeriod); break;
                case 6:
                    RequireOneBasicHit("segundo comando após mais de duas recargas");
                    Capture(); StartBasic(2);
                    Next(7, .24f); break;
                case 7:
                    RequireOneBasicHit("terceiro comando manual");
                    Capture();
                    Require(character.SelectAttackTarget(other) && combat.Target == other && !combat.HasQueuedAttack,
                        "trocar seleção depois de um golpe não pede o próximo");
                    Next(8, QuietPeriod); break;
                case 8:
                    RequireQuiet("troca de seleção durante recuperação");
                    Fixture(FarTarget); Capture(); combat.SetTarget(enemy);
                    Require(combat.RequestAttack(0) && combat.HasQueuedAttack,
                        "um comando manual distante registra somente um ataque pendente");
                    Require(combat.SwingCount == swingsBefore && enemy.CurrentHealth == healthBefore && abilities.Mana == manaBefore,
                        "pedido distante não antecipa golpe, dano ou mana");
                    Next(9, .4f); break;
                case 9:
                    Require(character.IsMoving && character.transform.position.z > PlayerStart.z + .2f,
                        "pedido manual aproxima o personagem do alvo escolhido");
                    Require(combat.SwingCount == swingsBefore && enemy.CurrentHealth == healthBefore,
                        "aproximação ainda distante não causa contato prematuro");
                    Next(10, RemainingApproachSeconds() + .5f); break;
                case 10:
                    RequireOneBasicHit("pedido manual após alcançar o alvo");
                    Next(11, QuietPeriod); break;
                case 11:
                    RequireOneBasicHit("aproximação concluída após mais de duas recargas");
                    Fixture(FarTarget); Capture(); combat.SetTarget(enemy);
                    heavyDamage = combat.Stats.GetSkillDamage(1.8d, true, abilities.OutgoingDamageMultiplier);
                    Require(abilities.TryUse(1) && combat.HasQueuedAttack,
                        "Corte Pesado distante registra uma intenção manual própria");
                    Require(combat.TryGetApproachDestination(out Vector3 heavyApproach) &&
                        Vector3.ProjectOnPlane(enemy.transform.position - heavyApproach, Vector3.up).magnitude > 2.6f,
                        "aproximação do pesado usa seu alcance maior");
                    Require(abilities.Mana == manaBefore && OwnCooldownRemaining(1) == 0f,
                        "pesado distante aguarda alcance sem mana ou recarga antecipadas");
                    Next(12, RemainingApproachSeconds() + .6f); break;
                case 12:
                    Require(combat.SwingCount == swingsBefore + 1 && enemy.CurrentHealth == healthBefore - heavyDamage &&
                        !combat.HasQueuedAttack && !combat.HasPendingStrike && OwnCooldownRemaining(1) > 0f && abilities.Mana < manaBefore,
                        "pesado aplica uma vez seu dano, mana e recarga, sem enfileirar básico");
                    Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == PrototypeAttackAnimation.HeavyVariant,
                        "comando pesado usa a quarta captura da biblioteca");
                    Next(13, QuietPeriod); break;
                case 13:
                    Require(combat.SwingCount == swingsBefore + 1 && enemy.CurrentHealth == healthBefore - heavyDamage && !combat.HasQueuedAttack,
                        "pesado não se repete nem inicia básicos após a recarga compartilhada");
                    cancellationIndex = 0; BeginQueuedCancellation();
                    break;
                case 14:
                    Require(!combat.HasQueuedAttack && !character.HasFreeAttack,
                        QueuedCancellations[cancellationIndex] + " cancela a intenção ainda não comprometida");
                    if (QueuedCancellations[cancellationIndex] == Cancellation.CharacterDisabled)
                        Require(!character.HasDestination, "desativar Character limpa também destino de movimento");
                    if (QueuedCancellations[cancellationIndex] == Cancellation.PlayerDeath)
                        Require(!combat.IsAlive, "morte do personagem é exercitada na simulação real");
                    EndCancellation(QueuedCancellations[cancellationIndex]);
                    Next(15, QuietPeriod); break;
                case 15:
                    RequireQuiet("cancelamento por " + QueuedCancellations[cancellationIndex]);
                    cancellationIndex++;
                    if (cancellationIndex < QueuedCancellations.Length) BeginQueuedCancellation();
                    else { cancellationIndex = 0; BeginCommittedCancellation(); }
                    break;
                case 16:
                    RequireOneBasicHit("golpe comprometido antes de " + CommittedCancellations[cancellationIndex]);
                    EndCancellation(CommittedCancellations[cancellationIndex]);
                    Next(17, QuietPeriod); break;
                case 17:
                    RequireOneBasicHit("golpe cancelado somente na intenção por " + CommittedCancellations[cancellationIndex]);
                    cancellationIndex++;
                    if (cancellationIndex < CommittedCancellations.Length) BeginCommittedCancellation();
                    else { ValidateLateRejections(); Next(18, .35f); }
                    break;
                case 18:
                    Require(combat.HasQueuedAttack && combat.Target == other && character.IsMoving,
                        "pedido mais novo continua aproximando após recusa tardia de outro envio");
                    string currentId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, currentId);
                    combat.ResolveSubmittedAttack(currentId, false);
                    Require(!combat.HasQueuedAttack && !combat.HasSubmittedAttackPresentation,
                        "recusa da intenção atual ainda cancela sua própria fila");
                    Fixture(FarTarget);
                    Require(JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                        "cenários isolados conservam vida restaurada, atributos, nível, experiência e equipamento originais");
                    Require(Mathf.Abs(combat.AttackInterval - .65f) < .0001f &&
                        PrototypeAttackAnimation.ClipNames.Length == 4 && PrototypeCombat.AttackPresentationDuration == .6f,
                        "controle manual conserva recarga e biblioteca de quatro golpes com recuperação completa");
                    Finish(true, checks + " verificações de seleção, comando único, aproximação, quatro golpes, cancelamentos e rejeições tardias.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, "etapa=" + SessionState.GetInt(Key + "Stage", -1) + " " + error); }
    }

    private static float QuietPeriod => 2f * combat.AttackInterval + .15f;

    private static void StartBasic(int variant)
    {
        Require(combat.RequestAttack(0) && combat.HasPendingStrike && !combat.HasQueuedAttack,
            "um pedido manual próximo compromete exatamente um básico");
        Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == variant,
            "comando manual seleciona " + PrototypeAttackAnimation.ClipNames[variant]);
        Require(Mathf.Abs((float)typeof(PrototypeCombat).GetField("strikeAt", PrivateInstance).GetValue(combat) - Time.time - .13f) < .001f,
            "comando manual conserva contato em 0,13 s");
        Require(!combat.RequestAttack(0), "novo pedido na recarga é recusado sem registrar repetição futura");
    }

    private static void RequireQuiet(string context)
    {
        Require(combat.SwingCount == swingsBefore && enemy.CurrentHealth == healthBefore && other.CurrentHealth == otherHealthBefore &&
            !combat.HasQueuedAttack && !combat.HasPendingStrike, context + " não dispara ataque, dano ou nova fila");
        Require(abilities.Mana == manaBefore, context + " conserva mana sem habilidade executada");
    }

    private static void RequireOneBasicHit(string context)
    {
        Require(combat.SwingCount == swingsBefore + 1 && enemy.CurrentHealth == healthBefore - combat.Stats.GetBasicDamage() &&
            other.CurrentHealth == otherHealthBefore && !combat.HasPendingStrike && !combat.HasQueuedAttack,
            context + " conclui um único básico no alvo original");
        Require(abilities.Mana == manaBefore && OwnCooldownRemaining(1) == 0f,
            context + " conserva mana e recarga própria do pesado");
    }

    private static void BeginQueuedCancellation()
    {
        Fixture(FarTarget); Capture(); combat.SetTarget(enemy);
        Require(combat.RequestAttack(0) && combat.HasQueuedAttack && !combat.HasPendingStrike,
            QueuedCancellations[cancellationIndex] + " começa com pedido manual ainda distante");
        ApplyCancellation(QueuedCancellations[cancellationIndex]);
        Next(14, QueuedCancellations[cancellationIndex] == Cancellation.Obstacle ? 1.4f : .15f);
    }

    private static void BeginCommittedCancellation()
    {
        Fixture(NearTarget); Capture(); combat.SetTarget(enemy);
        Require(combat.RequestAttack(0) && combat.HasPendingStrike,
            CommittedCancellations[cancellationIndex] + " começa depois do compromisso real de contato");
        ApplyCancellation(CommittedCancellations[cancellationIndex]);
        Require(combat.HasPendingStrike && !combat.HasQueuedAttack,
            CommittedCancellations[cancellationIndex] + " limpa somente intenção e preserva o golpe já comprometido");
        Next(16, .24f);
    }

    private static void ApplyCancellation(Cancellation kind)
    {
        switch (kind)
        {
            case Cancellation.Ground: character.MoveTo(PlayerStart + Vector3.left * 6f); break;
            // This is the real cancellation command used by the WASD branch. Hardware input is checked manually.
            case Cancellation.MovementCommand: combat.CancelRequestedAttack(); break;
            case Cancellation.SameSelection: Require(character.SelectAttackTarget(enemy), "reselecionar mesmo alvo é aceito"); break;
            case Cancellation.OtherSelection: Require(character.SelectAttackTarget(other), "nova seleção é aceita sem pedir golpe"); break;
            case Cancellation.Interface: SetAttributesPanel(true); break;
            case Cancellation.PlayerDeath: combat.ReceiveDamage(int.MaxValue); break;
            case Cancellation.CharacterDisabled: character.enabled = false; break;
            case Cancellation.CombatDisabled: combat.enabled = false; break;
            case Cancellation.TargetInactive: enemy.gameObject.SetActive(false); break;
            case Cancellation.TargetReturning: typeof(PrototypeMonster).GetMethod("BeginReturn", PrivateInstance).Invoke(enemy, null); break;
            case Cancellation.WeaponRemoved:
                Require(combat.Stats.Equipment.TryUnequip(EquipmentSlot.Sword, out _), "espada original é retirada durante a fila"); break;
            case Cancellation.TransportReset: network.ConfigureForTests(false); break;
            case Cancellation.Obstacle:
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Parede temporária do combate manual";
                wall.transform.position = new Vector3(500f, 1.2f, 501f);
                wall.transform.localScale = new Vector3(3f, 3f, .2f); Physics.SyncTransforms(); break;
        }
    }

    private static void EndCancellation(Cancellation kind)
    {
        SetAttributesPanel(false);
        character.enabled = true; combat.enabled = true;
        if (kind == Cancellation.WeaponRemoved)
            Require(combat.Stats.Equipment.TryEquip(swordId, EquipmentSlot.Sword, out _), "espada original é reequipada");
        if (wall != null) { UnityEngine.Object.DestroyImmediate(wall); wall = null; Physics.SyncTransforms(); }
    }

    private static void ValidateLateRejections()
    {
        Fixture(FarTarget); Capture(); combat.SetTarget(enemy);
        Require(combat.RequestAttack(0) && combat.HasQueuedAttack, "primeiro pedido distante aguarda alcance");
        string oldId = Guid.NewGuid().ToString("D");
        combat.PresentSubmittedAttack(0, oldId);
        Require(combat.HasSubmittedAttackPresentation && !combat.HasPendingStrike && enemy.CurrentHealth == healthBefore && abilities.Mana == manaBefore,
            "apresentação por UUID permanece cosmética e não prevê dano ou mana");
        Require(combat.RequestAttack(0), "novo pedido manual no mesmo alvo é aceito durante apresentação antiga");
        combat.ResolveSubmittedAttack(oldId, false);
        Require(combat.HasQueuedAttack && combat.Target == enemy && !combat.HasSubmittedAttackPresentation,
            "recusa tardia não apaga pedido manual mais novo no mesmo alvo");
        string previousId = Guid.NewGuid().ToString("D");
        combat.PresentSubmittedAttack(0, previousId);
        Require(character.SelectAttackTarget(other) && !combat.HasQueuedAttack && combat.RequestAttack(0),
            "troca cancela pedido anterior e novo comando manual cria intenção para outro alvo");
        combat.ResolveSubmittedAttack(oldId, false);
        Require(combat.HasQueuedAttack && combat.HasSubmittedAttackPresentation && combat.Target == other,
            "UUID estrangeiro não cancela apresentação nem pedido atuais");
        combat.ResolveSubmittedAttack(previousId, false);
        Require(combat.HasQueuedAttack && combat.Target == other && !combat.HasSubmittedAttackPresentation,
            "recusa do envio anterior preserva novo pedido manual para outro alvo");
    }

    private static void Fixture(Vector3 targetPosition)
    {
        SetAttributesPanel(false);
        character.enabled = true; combat.enabled = true;
        enemy.gameObject.SetActive(true); other.gameObject.SetActive(true);
        combat.ClearSubmittedAttackPresentation(); combat.ResetEncounter();
        SetCharacter("movementVelocity", Vector3.zero); SetCharacter("hasDestination", false);
        SetCharacter("verticalSpeed", 0f); SetCharacter("approachStalledTime", 0f);
        Move(character.transform, PlayerStart); character.transform.rotation = Quaternion.identity;
        PlaceMonster(enemy, targetPosition, "Alvo A da verificação manual");
        PlaceMonster(other, OtherTarget, "Alvo B da verificação manual");
        Physics.SyncTransforms();
        Guard(!combat.HasQueuedAttack && !character.HasFreeAttack, "fixture precisa começar sem intenção de ataque");
    }

    private static void PlaceMonster(PrototypeMonster monster, Vector3 position, string name)
    {
        typeof(PrototypeMonster).GetField("homePosition", PrivateInstance).SetValue(monster, position);
        typeof(PrototypeMonster).GetField("homeRotation", PrivateInstance).SetValue(monster, Quaternion.identity);
        monster.ResetEncounter(); monster.ConfigureStats(name, 100000, 0, 0);
        monster.ConfigureEncounter(.25f, 45f, 1f); monster.enabled = false;
        Move(monster.transform, position);
    }

    private static void Move(Transform target, Vector3 position)
    {
        CharacterController controller = target.GetComponent<CharacterController>();
        controller.enabled = false; target.position = position; controller.enabled = true;
    }
    private static void Capture()
    {
        swingsBefore = combat.SwingCount; healthBefore = enemy.CurrentHealth;
        otherHealthBefore = other.CurrentHealth; manaBefore = abilities.Mana;
    }
    private static float RemainingApproachSeconds() => combat.TryGetApproachDestination(out Vector3 destination)
        ? Vector3.ProjectOnPlane(destination - character.transform.position, Vector3.up).magnitude / PrototypeCharacter.DefaultWalkSpeed : 0f;
    private static float OwnCooldownRemaining(int slot)
    {
        float[] readyAt = (float[])typeof(PrototypeAbilities).GetField("readyAt", PrivateInstance).GetValue(abilities);
        return Mathf.Max(0f, readyAt[slot] - Time.time);
    }
    private static void SetCharacter(string name, object value) => typeof(PrototypeCharacter).GetField(name, PrivateInstance).SetValue(character, value);
    private static void SetAttributesPanel(bool open) => typeof(PrototypeHud).GetField("showAttributes", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, open);
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Guard(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Require(bool condition, string message) { Guard(condition, message); checks++; }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        SetAttributesPanel(false);
        if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
        if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
        if (success) Debug.Log("[MANUAL COMBAT PLAY PASS] " + message);
        else Debug.LogError("[MANUAL COMBAT PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
