using System;
using System.Linq;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Exercises the real controller, offline ability pipeline and existing online presentation APIs.
// It never connects, sends commands or persists character data.
[InitializeOnLoad]
public static class AttackLibraryPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.AttackLibraryChecks.";
    private static PrototypeCharacter character;
    private static PrototypeCombat combat;
    private static PrototypeAbilities abilities;
    private static Animator animator, remoteAnimator;
    private static PrototypeRemotePlayer remote, otherRemote;
    private static GameObject floor;
    private static float nextAt;
    private static int checks, swingsBefore;
    private static string originalProgress, predictedId;

    static AttackLibraryPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar golpes da biblioteca Mixamo")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 45f);
        SessionState.SetBool(Key + "Active", true);
        checks = 0; nextAt = 0f;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Guard(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "timeout");
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
                    combat = character.GetComponent<PrototypeCombat>();
                    abilities = character.GetComponent<PrototypeAbilities>();
                    character.GetComponent<PrototypeOnlineClient>().ConfigureForTests(false);
                    foreach (PrototypeMonster enemy in combat.Monsters) enemy.enabled = false;
                    character.enabled = false;
                    floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.layer = 8; floor.name = "Piso temporário dos golpes Mixamo";
                    floor.transform.position = new Vector3(500f, -.5f, 500f);
                    floor.transform.localScale = new Vector3(25f, 1f, 25f);
                    CharacterController capsule = character.GetComponent<CharacterController>();
                    capsule.enabled = false;
                    character.transform.SetPositionAndRotation(new Vector3(500f, .04f, 500f), Quaternion.identity);
                    capsule.enabled = true; Physics.SyncTransforms();
                    combat.ResetEncounter();
                    animator = character.VisualRoot.GetComponentInChildren<Animator>();
                    originalProgress = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    ValidateController();
                    ValidateStaleReservation();
                    remote = CreateRemote("attack-library-remote", 502f);
                    otherRemote = CreateRemote("attack-library-other", 504f);
                    remoteAnimator = remote.GetComponentInChildren<Animator>();
                    StartBasic(0);
                    remote.PlayAttack(0);
                    Next(1, .11f); break;
                case 1:
                    CheckActiveClip(animator, 0); CheckActiveClip(remoteAnimator, 0);
                    Next(2, .59f); break;
                case 2:
                    StartBasic(1); remote.PlayAttack(0);
                    Next(3, .11f); break;
                case 3:
                    CheckActiveClip(animator, 1); CheckActiveClip(remoteAnimator, 1);
                    Next(4, .59f); break;
                case 4:
                    Require(abilities.TryUse(1), "Corte Pesado usa o fluxo real da habilidade");
                    Require(Mathf.Abs(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) - 3f) < .0001f,
                        "Corte Pesado seleciona sua captura própria");
                    remote.PlayAttack(1);
                    Next(5, .11f); break;
                case 5:
                    CheckActiveClip(animator, 3); CheckActiveClip(remoteAnimator, 3);
                    Next(6, .59f); break;
                case 6:
                    StartBasic(2); remote.PlayAttack(0); otherRemote.PlayAttack(0);
                    Next(7, .11f); break;
                case 7:
                    CheckActiveClip(animator, 2); CheckActiveClip(remoteAnimator, 2);
                    CheckActiveClip(otherRemote.GetComponentInChildren<Animator>(), 0);
                    Require(!remote.HasGameplayComponents && !otherRemote.HasGameplayComponents,
                        "remotos mantêm sequências independentes sem componentes de combate ou colisão");
                    Next(8, .59f); break;
                case 8:
                    StartBasic(0); remote.PlayAttack(0);
                    Next(9, .11f); break;
                case 9:
                    CheckActiveClip(animator, 0); CheckActiveClip(remoteAnimator, 0);
                    Next(10, .59f); break;
                case 10:
                    Require(JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                        "golpes no vazio e apresentações remotas preservam vida, atributos, equipamento e experiência");
                    combat.ResetEncounter();
                    swingsBefore = combat.SwingCount;
                    float manaBefore = abilities.Mana;
                    predictedId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, predictedId);
                    combat.PresentSubmittedAttack(0, predictedId);
                    Require(combat.SwingCount == swingsBefore + 1 && combat.HasSubmittedAttackPresentation &&
                        animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == 0f,
                        "UUID previsto repetido reserva apenas o primeiro básico");
                    Require(!combat.HasPendingStrike && combat.CooldownRemaining == 0f && abilities.Mana == manaBefore &&
                        JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                        "seleção cosmética prevista não cria dano, recarga, gasto de mana ou progresso");
                    combat.ResolveSubmittedAttack(Guid.NewGuid().ToString("D"), false);
                    Require(combat.HasSubmittedAttackPresentation, "recusa estrangeira preserva a reserva atual");
                    Next(11, PrototypeCombat.AttackPresentationDuration + .05f); break;
                case 11:
                    Acknowledge(200, 0);
                    Acknowledge(200, 0);
                    Require(combat.SwingCount == swingsBefore + 1 && !combat.IsAttacking && !combat.HasSubmittedAttackPresentation,
                        "ACK tardio e evento repetido não repetem o primeiro golpe nem avançam a sequência");
                    string secondId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, secondId);
                    Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == 1f,
                        "básico seguinte ao ACK usa a segunda captura");
                    combat.ResolveSubmittedAttack(predictedId, false);
                    Require(combat.HasSubmittedAttackPresentation, "recusa de UUID antigo não desfaz o próximo golpe");
                    combat.ResolveSubmittedAttack(secondId, false);
                    Require(!combat.HasSubmittedAttackPresentation && !combat.IsAttacking &&
                        !combat.GetComponent<PrototypeAttackEffects>().IsPlaying,
                        "recusa atual limpa apresentação e rastro");
                    string retryId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, retryId);
                    Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == 1f,
                        "básico recusado devolve sua posição à sequência");
                    Acknowledge(201, 0);
                    combat.ResolveSubmittedAttack(retryId, true);
                    string heavyId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(1, heavyId);
                    Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == 3f,
                        "previsão da habilidade pesada usa o clipe exclusivo");
                    combat.ResolveSubmittedAttack(heavyId, false);
                    string thirdId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, thirdId);
                    Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == 2f,
                        "pesado recusado não altera o próximo básico");
                    Acknowledge(202, 0);
                    combat.ResolveSubmittedAttack(thirdId, true);
                    int afterPrediction = combat.SwingCount;
                    Acknowledge(203, 1);
                    Require(combat.SwingCount == afterPrediction + 1 && animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == 3f,
                        "evento autoritativo sem previsão apresenta o golpe pesado uma vez");
                    Acknowledge(203, 1);
                    Require(combat.SwingCount == afterPrediction + 1, "evento pesado repetido não reapresenta o golpe");
                    Acknowledge(204, 0);
                    Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == 0f,
                        "pesado confirmado preserva a volta ao início dos básicos");
                    Require(!combat.HasPendingStrike && JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                        "eventos de apresentação não executam a simulação de dano offline");
                    string pendingId = Guid.NewGuid().ToString("D");
                    combat.PresentSubmittedAttack(0, pendingId);
                    combat.ClearSubmittedAttackPresentation();
                    Require(!combat.IsAttacking && !combat.HasSubmittedAttackPresentation,
                        "desconexão limpa a previsão e sua reserva de animação");
                    Finish(true, checks + " verificações de quatro clipes, ciclo básico, pesado, remotos, autoridade, ACK, recusa e deduplicação.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, "etapa=" + SessionState.GetInt(Key + "Stage", -1) + " " + error); }
    }

    private static void StartBasic(int variant)
    {
        Require(combat.TryAttack(), "básico " + (variant + 1) + " executa pela API real");
        Require(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == variant,
            "básico seleciona a captura " + PrototypeAttackAnimation.ClipNames[variant]);
        Require(Mathf.Abs(combat.AttackInterval - .65f) < .0001f && combat.HasPendingStrike &&
            Mathf.Abs((float)typeof(PrototypeCombat).GetField("strikeAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(combat) - Time.time - .13f) < .001f,
            "recarga de 0,65 s e contato de 0,13 s permanecem na simulação");
        Require(!combat.TryAttack(), "segunda solicitação durante a recarga é recusada");
    }

    private static void CheckActiveClip(Animator source, int variant)
    {
        AnimatorStateInfo state = source.GetCurrentAnimatorStateInfo(0);
        Require(state.IsName("Attack") && !source.IsInTransition(0), "quatro golpes preservam o estado Attack compartilhado");
        Require(Mathf.Abs(state.length - PrototypeCombat.AttackPresentationDuration) < .015f && Mathf.Abs(source.speed - 1f) < .0001f,
            "clipe selecionado preserva a duração de apresentação sem alterar a velocidade global do Animator");
        AnimatorClipInfo[] clips = source.GetCurrentAnimatorClipInfo(0);
        float expected = clips.Where(value => ClipName(value.clip) == PrototypeAttackAnimation.ClipNames[variant]).Sum(value => value.weight);
        float other = clips.Where(value => ClipName(value.clip) != PrototypeAttackAnimation.ClipNames[variant]).Sum(value => value.weight);
        Require(expected > .98f && other < .01f,
            "seleção discreta aplica somente " + PrototypeAttackAnimation.ClipNames[variant] + "; peso=" + expected);
    }

    private static void ValidateController()
    {
        Require(animator.parameters.Any(parameter => parameter.name == PrototypeAttackAnimation.VariantParameter &&
            parameter.type == AnimatorControllerParameterType.Float), "controller fornece AttackVariant float");
        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        Require(controller != null, "controller real está disponível para inspeção");
        AnimatorState attack = controller.layers[0].stateMachine.states.Single(value => value.state.name == "Attack").state;
        BlendTree tree = attack.motion as BlendTree;
        Require(tree != null && tree.blendType == BlendTreeType.Simple1D &&
            tree.blendParameter == PrototypeAttackAnimation.VariantParameter && !tree.useAutomaticThresholds &&
            tree.children.Length == PrototypeAttackAnimation.ClipNames.Length,
            "Attack usa uma árvore de quatro capturas com limiares explícitos");
        for (int variant = 0; variant < PrototypeAttackAnimation.ClipNames.Length; variant++)
        {
            ChildMotion motion = tree.children[variant];
            AnimationClip clip = motion.motion as AnimationClip;
            Require(clip != null && ClipName(clip) == PrototypeAttackAnimation.ClipNames[variant] &&
                !clip.isLooping && Mathf.Abs(motion.threshold - variant) < .0001f && motion.timeScale > 0f,
                "captura importada sem loop ocupa o limiar " + variant);
        }
        Require(!animator.applyRootMotion, "golpes não deslocam a cápsula por root motion");
    }

    private static void ValidateStaleReservation()
    {
        var sequence = new PrototypeAttackAnimation(null);
        sequence.PlayNext(false);
        int oldVersion = sequence.BasicSequenceVersion;
        sequence.PlayNext(false);
        sequence.RestoreBasicSequence(oldVersion, 0);
        Require(sequence.PlayNext(false) == 2, "reserva antiga não rebobina um básico mais recente");
        sequence.PlayNext(true);
        Require(sequence.PlayNext(false) == 0, "pesado não consome uma posição do ciclo básico");
    }

    private static PrototypeRemotePlayer CreateRemote(string id, float x)
    {
        PrototypeRemotePlayer result = PrototypeRemotePlayer.Create(character, id);
        result.ApplyState(new OnlinePlayerState {
            id = id, x = x, z = 500f, health = 100, maxHealth = 100,
            equipped = combat.Stats.CaptureSaveData().equipped
        }, 1000d + Time.time);
        return result;
    }

    private static void Acknowledge(long eventId, int ability)
    {
        combat.ApplyOnlineSnapshot(new OnlineSnapshot {
            playerId = "attack-library-check", mapId = "ruins", serverTime = 1000d + Time.time,
            latestEventId = eventId, character = combat.Stats.CaptureSaveData(),
            self = new OnlinePlayerState { id = "attack-library-check", health = combat.Stats.Health,
                maxHealth = combat.Stats.MaxHealth, mana = abilities.Mana },
            monsters = Array.Empty<OnlineMonsterState>(),
            events = new[] { new OnlineWorldEvent {
                eventId = eventId, kind = "attack", sourceId = "attack-library-check", ability = ability
            } }
        });
    }

    private static string ClipName(AnimationClip clip) => clip.name.Substring(clip.name.LastIndexOf('|') + 1);
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Guard(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Require(bool value, string message) { Guard(value, message); checks++; }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (remote != null) UnityEngine.Object.DestroyImmediate(remote.gameObject);
        if (otherRemote != null) UnityEngine.Object.DestroyImmediate(otherRemote.gameObject);
        if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
        if (success) Debug.Log("[ATTACK LIBRARY PLAY PASS] " + message);
        else Debug.LogError("[ATTACK LIBRARY PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
