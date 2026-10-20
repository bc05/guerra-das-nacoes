using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;

// Real casts on the first eligible cooldown frame, with the real controller sampled in LateUpdate.
// Run at 30, 60 or 120 simulation frames/s; no transport, damage prediction or persistence is used.
[InitializeOnLoad]
public static class AttackFluidityPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.AttackFluidityChecks.";
    private static readonly int[] ChainVariants = { 0, 1, 3, 2, 0, 1 };
    private static PrototypeCharacter character;
    private static PrototypeCombat combat;
    private static PrototypeAbilities abilities;
    private static PrototypeRemotePlayer remote;
    private static Animator animator, remoteAnimator;
    private static GameObject floor;
    private static PoseSampler localPose, remotePose;
    private static PoseReference terminalGuard;
    private static Report report;
    private static CastReport current;
    private static string originalProgress, pendingId;
    private static int phase = -1;
    private static int chainIndex, frameAtCast, previousFrame, previousUpdateFrame, swingsBefore;
    private static float castAt, rejectedAt, previousCaptureDeltaTime, previousTimeScale;
    private static bool configuredClock, rejected, sampled, confirmed;
    private static PlayerLoopSystem originalLoop;
    private static bool configuredLoop;

    static AttackFluidityPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar fluidez dos golpes na recarga mínima")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 75f);
        SessionState.SetBool(Key + "Active", true);
        report = null; phase = -1; configuredClock = false; configuredLoop = false;
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
            if (EditorApplication.isCompiling || Time.frameCount < 4 || phase != -1) return;
            Initialize();
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Initialize()
    {
        int rate = 120;
        string option = Environment.GetEnvironmentVariable("GN_ATTACK_FLUIDITY_FRAME_RATE");
        if (!string.IsNullOrEmpty(option)) Guard(int.TryParse(option, out rate), "Frame rate inválido.");
        Guard(rate == 30 || rate == 60 || rate == 120, "Use GN_ATTACK_FLUIDITY_FRAME_RATE=30, 60 ou 120.");
        report = new Report { sampleRate = rate, presentationDuration = PrototypeCombat.AttackPresentationDuration };
        previousCaptureDeltaTime = Time.captureDeltaTime;
        previousTimeScale = Time.timeScale;
        Time.captureDeltaTime = 1f / rate;
        Time.timeScale = 1f;
        configuredClock = true;
        character = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
        Guard(character != null, "Personagem ausente.");
        combat = character.GetComponent<PrototypeCombat>();
        abilities = character.GetComponent<PrototypeAbilities>();
        character.GetComponent<PrototypeOnlineClient>().ConfigureForTests(false);
        foreach (PrototypeMonster enemy in combat.Monsters) enemy.enabled = false;
        character.enabled = false;
        floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Piso temporário de fluidez de combate";
        floor.layer = 8;
        floor.transform.position = new Vector3(500f, -.5f, 500f);
        floor.transform.localScale = new Vector3(20f, 1f, 20f);
        CharacterController capsule = character.GetComponent<CharacterController>();
        capsule.enabled = false;
        character.transform.SetPositionAndRotation(new Vector3(500f, .04f, 500f), Quaternion.identity);
        capsule.enabled = true;
        combat.ResetEncounter();
        originalProgress = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
        animator = character.VisualRoot.GetComponentInChildren<Animator>();
        remote = PrototypeRemotePlayer.Create(character, "attack-fluidity-remote");
        remote.ApplyState(new OnlinePlayerState {
            id = "attack-fluidity-remote", x = 502f, z = 500f, health = 100, maxHealth = 100,
            equipped = combat.Stats.CaptureSaveData().equipped
        }, 1000d + Time.time);
        remoteAnimator = remote.GetComponentInChildren<Animator>();
        localPose = new PoseSampler(animator);
        remotePose = new PoseSampler(remoteAnimator);
        terminalGuard = CaptureTerminalGuard();
        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        Guard(controller != null, "Controller real ausente.");
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState attack = machine.states.Single(value => value.state.name == "Attack").state;
        AnimatorStateTransition entry = machine.anyStateTransitions.Single(value => value.destinationState == attack);
        AnimatorStateTransition exit = attack.transitions.Single(value => value.destinationState.name == "Idle");
        report.entryBlendSeconds = entry.duration;
        report.exitNormalizedTime = exit.exitTime;
        report.exitBlendSeconds = exit.duration;
        report.cooldownSeconds = combat.AttackInterval;
        Require(!entry.canTransitionToSelf && entry.hasFixedDuration && exit.hasFixedDuration,
            "controller preserva transições em segundos e impede auto transição de Attack");
        Require(Mathf.Abs(combat.AttackInterval - .65f) < .0001f &&
            Mathf.Abs(PrototypeCombat.AttackPresentationDuration - .6f) < .0001f,
            "recarga de 0,65 s contém a apresentação de 0,60 s");
        Physics.SyncTransforms();
        chainIndex = 0; previousFrame = Time.frameCount; previousUpdateFrame = Time.frameCount;
        phase = 0;
        originalLoop = PlayerLoop.GetCurrentPlayerLoop();
        PlayerLoopSystem temporaryLoop = originalLoop;
        Guard(AppendProbe(ref temporaryLoop, typeof(UnityEngine.PlayerLoop.Update), ActBeforeAnimation),
            "A fase Update está ausente do PlayerLoop.");
        Guard(AppendProbe(ref temporaryLoop, typeof(UnityEngine.PlayerLoop.PreLateUpdate), SampleFrame),
            "A fase PreLateUpdate está ausente do PlayerLoop.");
        PlayerLoop.SetPlayerLoop(temporaryLoop);
        configuredLoop = true;
    }

    private static bool AppendProbe(ref PlayerLoopSystem loop, Type phaseType, PlayerLoopSystem.UpdateFunction callback)
    {
        if (loop.type == phaseType)
        {
            PlayerLoopSystem[] previous = loop.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            var children = new PlayerLoopSystem[previous.Length + 1];
            Array.Copy(previous, children, previous.Length);
            children[previous.Length] = new PlayerLoopSystem {
                type = typeof(AttackFluidityPlayModeChecks), updateDelegate = callback
            };
            loop.subSystemList = children;
            return true;
        }
        if (loop.subSystemList == null) return false;
        // Copy each ancestor array so the stored original loop remains intact for restoration.
        PlayerLoopSystem[] descendants = (PlayerLoopSystem[])loop.subSystemList.Clone();
        for (int i = 0; i < descendants.Length; i++)
            if (AppendProbe(ref descendants[i], phaseType, callback)) { loop.subSystemList = descendants; return true; }
        return false;
    }

    // The real game starts casts in MonoBehaviour.Update, before this frame's Animator evaluation.
    // Never wait for a post-animation guard state before trying the first eligible cooldown frame.
    private static void ActBeforeAnimation()
    {
        if (!SessionState.GetBool(Key + "Active", false) || phase < 0 || Time.frameCount == previousUpdateFrame) return;
        try
        {
            previousUpdateFrame = Time.frameCount;
            if (phase == 0)
            {
                Require(IsGuard(animator) && IsGuard(remoteAnimator), "primeiro golpe parte da guarda real");
                StartChainCast(); phase = 1; return;
            }
            float age = Time.time - castAt;
            if (phase == 1 && combat.CooldownRemaining <= 0f)
            {
                // Try the next API call even if the Animator is still recovering; the next
                // post-animation samples will record that pre-cast state and any trigger delay.
                CompleteObservation(true, chainIndex + 1 < ChainVariants.Length);
                Require(!combat.IsAttacking && !combat.HasPendingStrike, "recuperação e contato terminam antes do próximo cast");
                chainIndex++;
                if (chainIndex < ChainVariants.Length) { StartChainCast(); return; }
                Require(!remote.HasGameplayComponents && JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                    "cadeia e remoto não alteram vida, atributos, equipamentos ou experiência");
                phase = 2;
                StartRejectedPrediction(false);
            }
            else if (phase == 2 || phase == 3) ObserveRejection(age);
            else if (phase == 4) ObserveConfirmation(age);
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    // Appended after all PreLateUpdate systems: animation, LateUpdate effects and constraints have finished.
    // The callback runs once per actual game frame, independent of editor polling.
    private static void SampleFrame()
    {
        if (!SessionState.GetBool(Key + "Active", false) || phase < 0 || Time.frameCount == previousFrame) return;
        try
        {
            previousFrame = Time.frameCount;
            if (phase == 0 || current == null) return;
            float age = Time.time - castAt;
            Observe(animator, localPose, current.local, age);
            Observe(remoteAnimator, remotePose, current.remote, age);
            report.frames.Add(new FrameReport {
                frame = Time.frameCount, timeSeconds = Time.time, castIndex = report.casts.Count - 1,
                elapsedSeconds = age, phase = current.kind, variant = current.variant,
                localState = State(animator), localInTransition = animator.IsInTransition(0),
                localNormalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
                localExpectedClipWeight = ClipWeight(animator, current.variant),
                remoteState = State(remoteAnimator), remoteInTransition = remoteAnimator.IsInTransition(0)
            });
            if (phase == 1) ObserveChain(age);
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void StartChainCast()
    {
        int variant = ChainVariants[chainIndex];
        float gap = current == null ? 0f : Time.time - castAt;
        int frames = current == null ? 0 : Time.frameCount - frameAtCast;
        PreCastState before = CapturePreCastState();
        int swings = combat.SwingCount;
        bool started = variant == PrototypeAttackAnimation.HeavyVariant ? abilities.TryUse(1) : combat.TryAttack();
        Require(started && combat.SwingCount == swings + 1, "API real aceita um único golpe no primeiro frame elegível");
        remote.PlayAttack(variant == PrototypeAttackAnimation.HeavyVariant ? 1 : 0);
        StartObservation("minimumCooldownChain", variant, before);
        current.intervalFromPreviousSeconds = gap;
        current.framesFromPrevious = frames;
        if (chainIndex > 0)
        {
            Require(gap >= combat.AttackInterval - .0001f && gap <= combat.AttackInterval + 1f / report.sampleRate + .0001f,
                "cast seguinte ocorre na recarga mínima, com no máximo um frame de quantização; intervalo=" + gap);
            Require(frames >= Mathf.CeilToInt(combat.AttackInterval * report.sampleRate - .001f) &&
                frames <= Mathf.CeilToInt(combat.AttackInterval * report.sampleRate - .001f) + 1,
                "nenhum frame elegível é adiado para esperar o Animator");
        }
        Require(!combat.TryAttack(), "solicitação duplicada no mesmo frame permanece em recarga");
    }

    private static void StartObservation(string kind, int variant, PreCastState before)
    {
        castAt = Time.time; frameAtCast = Time.frameCount;
        current = new CastReport { kind = kind, variant = variant, castAtSeconds = castAt,
            castFrame = frameAtCast, beforeCast = before };
        report.casts.Add(current);
        localPose.Reset(); remotePose.Reset();
        sampled = false; rejected = false; confirmed = false;
        Guard(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == variant &&
            remoteAnimator.GetFloat(PrototypeAttackAnimation.VariantParameter) == variant,
            "seleção do herói/remoto difere da captura solicitada");
    }

    private static void ObserveChain(float age)
    {
        if (age >= .1f && age <= .40f)
        {
            Guard(ClipWeight(animator, current.variant) > .98f && ClipWeight(remoteAnimator, current.variant) > .98f,
                "gatilho perdido ou variante trocada durante o golpe " + current.variant + " aos " + age + " s");
            Guard(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack") &&
                remoteAnimator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), "golpe encerrou antes da recuperação");
            sampled = true;
        }
        Guard(animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == current.variant &&
            remoteAnimator.GetFloat(PrototypeAttackAnimation.VariantParameter) == current.variant,
            "AttackVariant alterou a captura anterior durante sua recuperação");
        if (age >= .10f)
        {
            Guard(SafeGuardBeforeCast(current.beforeCast, false) && SafeGuardBeforeCast(current.beforeCast, true),
                "pose pré-cast em Update ainda não é a guarda: herói=" + current.beforeCast.localState +
                ", transição=" + current.beforeCast.localInTransition + ", ponta=" + current.beforeCast.localTipErrorMetres +
                " m; remoto=" + current.beforeCast.remoteState + ", ponta=" + current.beforeCast.remoteTipErrorMetres + " m");
            ActorReport firstLocal = report.casts[0].local;
            ActorReport firstRemote = report.casts[0].remote;
            float oneFrame = 1f / report.sampleRate;
            Guard(firstLocal.entryStartedAtSeconds >= 0f && firstRemote.entryStartedAtSeconds >= 0f &&
                firstLocal.entryStartedAtSeconds <= oneFrame + .0001f && firstRemote.entryStartedAtSeconds <= oneFrame + .0001f,
                "entrada a partir de Idle excede um frame de avaliação: local=" + firstLocal.entryStartedAtSeconds +
                " s; remota=" + firstRemote.entryStartedAtSeconds + " s");
            Guard(current.local.entryStartedAtSeconds >= 0f && current.remote.entryStartedAtSeconds >= 0f &&
                current.local.entryStartedAtSeconds <= firstLocal.entryStartedAtSeconds + .0001f &&
                current.remote.entryStartedAtSeconds <= firstRemote.entryStartedAtSeconds + .0001f,
                "combo acrescentou latência em relação ao primeiro cast Idle: local=" + current.local.entryStartedAtSeconds +
                " s (base=" + firstLocal.entryStartedAtSeconds + "); remota=" + current.remote.entryStartedAtSeconds +
                " s (base=" + firstRemote.entryStartedAtSeconds + ")");
        }
    }

    private static void CompleteObservation(bool continuousCadence = true, bool allowIncompleteGuard = false)
    {
        Require(sampled, "captura recebeu amostras de Attack com peso integral");
        Require(current.local.entryFinishedAtSeconds >= 0f && current.remote.entryFinishedAtSeconds >= 0f,
            "herói e remoto atravessam a entrada do golpe");
        if (!allowIncompleteGuard)
        {
            PreCastState before = CapturePreCastState();
            Require(SafeGuardBeforeCast(before, false) && SafeGuardBeforeCast(before, true),
                "primeiro frame liberado pela recarga já alcançou a guarda visual comprovada");
        }
        float localGuardAt = current.local.guardAtSeconds >= 0f ? current.local.guardAtSeconds : current.local.visualGuardAtSeconds;
        float remoteGuardAt = current.remote.guardAtSeconds >= 0f ? current.remote.guardAtSeconds : current.remote.visualGuardAtSeconds;
        current.local.guardPauseBeforeNextSeconds = localGuardAt < 0f ? -1f : Time.time - castAt - localGuardAt;
        current.remote.guardPauseBeforeNextSeconds = remoteGuardAt < 0f ? -1f : Time.time - castAt - remoteGuardAt;
        if (continuousCadence && localGuardAt > 0f)
            Require(current.local.guardPauseBeforeNextSeconds >= -0.0001f && current.local.guardPauseBeforeNextSeconds < .15f,
                "guarda entre golpes não mantém a antiga pausa de 0,25 s");
    }

    private static void StartRejectedPrediction(bool heavy)
    {
        pendingId = Guid.NewGuid().ToString("D");
        swingsBefore = combat.SwingCount;
        int variant = heavy ? 3 : 2;
        PreCastState before = CapturePreCastState();
        combat.PresentSubmittedAttack(heavy ? 1 : 0, pendingId);
        remote.PlayAttack(heavy ? 1 : 0);
        StartObservation(heavy ? "rejectedHeavy" : "rejectedBasic", variant, before);
        Require(combat.HasSubmittedAttackPresentation && combat.SwingCount == swingsBefore + 1 && !combat.HasPendingStrike,
            "previsão usa apenas apresentação e mantém o UUID pendente");
    }

    private static void ObserveRejection(float age)
    {
        if (!rejected && age >= .25f)
        {
            Guard(ClipWeight(animator, current.variant) > .98f, "previsão não chegou ao clipe solicitado");
            sampled = true;
            combat.ResolveSubmittedAttack(Guid.NewGuid().ToString("D"), false);
            Require(combat.HasSubmittedAttackPresentation && animator.GetFloat(PrototypeAttackAnimation.VariantParameter) == current.variant,
                "recusa estrangeira conserva o golpe atual");
            combat.ResolveSubmittedAttack(pendingId, false);
            rejectedAt = Time.time;
            current.rejectedAtSeconds = age;
            rejected = true;
            Require(!combat.IsAttacking && !combat.HasSubmittedAttackPresentation &&
                !combat.GetComponent<PrototypeAttackEffects>().IsPlaying,
                "recusa correta encerra a previsão e seus efeitos");
        }
        if (!rejected) return;
        if (Time.time - rejectedAt >= .15f)
            Guard(IsGuard(animator), "recusa não retornou à guarda ou deixou um gatilho residual");
        if (age < combat.AttackInterval) return;
        CompleteObservation(false);
        current.local.guardAfterRejectionSeconds = current.local.guardAtSeconds - current.rejectedAtSeconds;
        Require(combat.SwingCount == swingsBefore + 1, "cancelamento não reproduz um novo golpe após voltar à guarda");
        if (phase == 2) { phase = 3; StartRejectedPrediction(true); return; }
        phase = 4;
        pendingId = Guid.NewGuid().ToString("D");
        swingsBefore = combat.SwingCount;
        PreCastState before = CapturePreCastState();
        combat.PresentSubmittedAttack(0, pendingId);
        // The remote's visual sequence is independent of the local rejection rollback.
        new PrototypeAttackAnimation(remoteAnimator).PlayVariant(2);
        StartObservation("lateConfirmedBasic", 2, before);
        Require(!combat.HasPendingStrike, "nova previsão após recusas continua sem dano local");
    }

    private static void ObserveConfirmation(float age)
    {
        if (age >= .1f && age <= .4f)
        {
            Guard(ClipWeight(animator, 2) > .98f && ClipWeight(remoteAnimator, 2) > .98f,
                "recusa não devolveu a terceira captura à sequência");
            sampled = true;
        }
        if (age < PrototypeCombat.AttackPresentationDuration + .05f) return;
        CompleteObservation();
        if (!confirmed)
        {
            Acknowledge(1200);
            Acknowledge(1200);
            combat.ResolveSubmittedAttack(pendingId, true);
            confirmed = true;
            Require(combat.SwingCount == swingsBefore + 1 && !combat.IsAttacking &&
                !combat.HasSubmittedAttackPresentation && IsGuard(animator),
                "ACK tardio repetido mantém a guarda e não reapresenta o clipe");
            Require(!combat.HasPendingStrike && JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                "recusas e confirmações não fabricam dano ou progresso");
            Finish(true, report.checks + " verificações em " + report.sampleRate + " fps; seis casts no primeiro frame de recarga, guarda, variantes, pesado e recusa.");
        }
    }

    private static void Observe(Animator source, PoseSampler pose, ActorReport actor, float age)
    {
        AnimatorStateInfo state = source.GetCurrentAnimatorStateInfo(0);
        bool transition = source.IsInTransition(0);
        if (transition && source.GetNextAnimatorStateInfo(0).IsName("Attack") &&
            source.GetNextAnimatorStateInfo(0).normalizedTime < .25f && actor.entryStartedAtSeconds < 0f)
            actor.entryStartedAtSeconds = age;
        if (state.IsName("Attack") && state.normalizedTime < .9f && !transition && actor.entryFinishedAtSeconds < 0f)
            actor.entryFinishedAtSeconds = age;
        if (state.IsName("Attack") && transition && source.GetNextAnimatorStateInfo(0).IsName("Idle") && actor.exitStartedAtSeconds < 0f)
            actor.exitStartedAtSeconds = age;
        if (age > .15f && IsGuard(source) && actor.guardAtSeconds < 0f) actor.guardAtSeconds = age;
        if (age > .4f && actor.visualGuardAtSeconds < 0f)
        {
            PoseError error = pose.GuardError(terminalGuard);
            bool terminalPose = state.IsName("Attack") && state.normalizedTime >= 1f - .0001f &&
                error.jointAngle < 1f && error.jointPosition < .005f && error.tip < .01f;
            if (IsGuard(source) || terminalPose) actor.visualGuardAtSeconds = age;
        }
        PoseDelta delta = pose.Sample();
        JumpReport window = age <= .10f ? actor.entrySteps : age <= .30f ? actor.contactSteps : age <= .60f ? actor.recoverySteps : actor.guardSteps;
        window.maximumSwordTipStepMetres = Mathf.Max(window.maximumSwordTipStepMetres, delta.tipStep);
        window.maximumJointStepDegrees = Mathf.Max(window.maximumJointStepDegrees, delta.jointStep);
        if (delta.jointStep >= window.maximumJointStepDegrees) window.maximumJointName = delta.jointName;
        window.samples++;
    }

    private static PreCastState CapturePreCastState()
    {
        PoseError localError = localPose.GuardError(terminalGuard);
        PoseError remoteError = remotePose.GuardError(terminalGuard);
        return new PreCastState {
            frame = Time.frameCount,
            localGuard = IsGuard(animator), remoteGuard = IsGuard(remoteAnimator),
            localState = State(animator), remoteState = State(remoteAnimator),
            localInTransition = animator.IsInTransition(0), remoteInTransition = remoteAnimator.IsInTransition(0),
            localNormalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
            remoteNormalizedTime = remoteAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime,
            localVariant = animator.GetFloat(PrototypeAttackAnimation.VariantParameter),
            remoteVariant = remoteAnimator.GetFloat(PrototypeAttackAnimation.VariantParameter),
            localJointErrorDegrees = localError.jointAngle, localJointErrorMetres = localError.jointPosition,
            localTipErrorMetres = localError.tip, remoteJointErrorDegrees = remoteError.jointAngle,
            remoteJointErrorMetres = remoteError.jointPosition, remoteTipErrorMetres = remoteError.tip
        };
    }

    private static bool SafeGuardBeforeCast(PreCastState state, bool isRemote)
    {
        if (isRemote ? state.remoteGuard : state.localGuard) return true;
        // A transition may still report Attack after its common terminal guard has been reached.
        // Permit that state only when measured joints/tip agree with the imported terminal guard;
        // Entry latency is checked separately against the first cast from Idle (at most one frame).
        return (isRemote ? state.remoteState : state.localState) == "Attack" &&
            (isRemote ? state.remoteNormalizedTime : state.localNormalizedTime) >= 1f - .0001f &&
            (isRemote ? state.remoteJointErrorDegrees : state.localJointErrorDegrees) < 1f &&
            (isRemote ? state.remoteJointErrorMetres : state.localJointErrorMetres) < .005f &&
            (isRemote ? state.remoteTipErrorMetres : state.localTipErrorMetres) < .01f;
    }

    private static PoseReference CaptureTerminalGuard()
    {
        GameObject reference = UnityEngine.Object.Instantiate(animator.gameObject);
        try
        {
            reference.name = "Referência temporária da guarda terminal Mixamo";
            reference.hideFlags = HideFlags.HideAndDontSave;
            foreach (MonoBehaviour behaviour in reference.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (Renderer renderer in reference.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in reference.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            Animator referenceAnimator = reference.GetComponent<Animator>();
            referenceAnimator.enabled = false;
            var sampler = new PoseSampler(referenceAnimator);
            PoseReference result = null;
            foreach (string name in PrototypeAttackAnimation.ClipNames)
            {
                AnimationClip clip = animator.runtimeAnimatorController.animationClips.Single(value =>
                    value.name.Substring(value.name.LastIndexOf('|') + 1) == name);
                clip.SampleAnimation(reference, clip.length);
                if (result == null) result = sampler.CaptureReference();
                PoseError error = sampler.GuardError(result);
                report.maximumTerminalJointMismatchDegrees = Mathf.Max(report.maximumTerminalJointMismatchDegrees, error.jointAngle);
                report.maximumTerminalJointMismatchMetres = Mathf.Max(report.maximumTerminalJointMismatchMetres, error.jointPosition);
                report.maximumTerminalTipMismatchMetres = Mathf.Max(report.maximumTerminalTipMismatchMetres, error.tip);
            }
            Require(report.maximumTerminalJointMismatchDegrees < .25f &&
                report.maximumTerminalJointMismatchMetres < .001f && report.maximumTerminalTipMismatchMetres < .002f,
                "quatro clipes importados terminam na mesma guarda, antes de avaliar troca de variante na transição");
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(reference); }
    }

    private static void Acknowledge(long eventId)
    {
        combat.ApplyOnlineSnapshot(new OnlineSnapshot {
            playerId = "attack-fluidity-check", mapId = "ruins", serverTime = 1000d + Time.time,
            latestEventId = eventId, character = combat.Stats.CaptureSaveData(),
            self = new OnlinePlayerState { id = "attack-fluidity-check", health = combat.Stats.Health,
                maxHealth = combat.Stats.MaxHealth, mana = abilities.Mana },
            monsters = Array.Empty<OnlineMonsterState>(),
            events = new[] { new OnlineWorldEvent { eventId = eventId, kind = "attack", sourceId = "attack-fluidity-check", ability = 0 } }
        });
    }

    private static string State(Animator source) => source.GetCurrentAnimatorStateInfo(0).IsName("Attack") ? "Attack" :
        source.GetCurrentAnimatorStateInfo(0).IsName("Idle") ? "Idle" : "other";
    private static bool IsGuard(Animator source) => source.GetCurrentAnimatorStateInfo(0).IsName("Idle") && !source.IsInTransition(0);
    private static float ClipWeight(Animator source, int variant) => source.GetCurrentAnimatorClipInfo(0)
        .Where(value => value.clip.name.Substring(value.clip.name.LastIndexOf('|') + 1) == PrototypeAttackAnimation.ClipNames[variant])
        .Sum(value => value.weight);
    private static void Guard(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Require(bool value, string message) { Guard(value, message); report.checks++; }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        try
        {
            if (report == null) report = new Report();
            report.success = success; report.message = message;
        }
        finally
        {
            if (configuredLoop)
            {
                PlayerLoop.SetPlayerLoop(originalLoop);
                configuredLoop = false;
                report.playerLoopRestored = true;
            }
            if (configuredClock)
            {
                Time.captureDeltaTime = previousCaptureDeltaTime;
                Time.timeScale = previousTimeScale;
                report.clockRestored = Mathf.Abs(Time.captureDeltaTime - previousCaptureDeltaTime) < .00001f &&
                    Mathf.Abs(Time.timeScale - previousTimeScale) < .00001f;
            }
            if (remote != null) UnityEngine.Object.DestroyImmediate(remote.gameObject);
            if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
        }
        try
        {
            string path = Environment.GetEnvironmentVariable("GN_ATTACK_FLUIDITY_REPORT_PATH");
            if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(Application.dataPath, "../../work/attack-fluidity-report.json");
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (success) Debug.Log("[ATTACK FLUIDITY PLAY PASS] " + message + " Relatório: " + path);
            else Debug.LogError("[ATTACK FLUIDITY PLAY FAIL] " + message + " Relatório: " + path);
        }
        catch (Exception error) { success = false; Debug.LogError("[ATTACK FLUIDITY REPORT FAIL] " + error); }
        EditorApplication.Exit(success ? 0 : 1);
    }

    private sealed class PoseSampler
    {
        private static readonly string[] BoneNames = { "Root", "Pelvis", "Spine", "Chest", "Head",
            "UpperArm.R", "Forearm.R", "Hand.R", "UpperArm.L", "Forearm.L", "Hand.L",
            "Thigh.R", "Shin.R", "Foot.R", "Thigh.L", "Shin.L", "Foot.L" };
        private readonly Animator source;
        private readonly Transform[] bones;
        private readonly Quaternion[] previous;
        private readonly Transform tip;
        private Vector3 previousTip;
        public PoseSampler(Animator animatorSource)
        {
            source = animatorSource;
            Transform[] nodes = source.GetComponentsInChildren<Transform>(true);
            bones = BoneNames.Select(name => nodes.Single(node => node.name == name)).ToArray();
            previous = new Quaternion[bones.Length];
            tip = nodes.First(node => node.name == "SwordTip" && node.gameObject.activeInHierarchy);
            Reset();
        }
        public void Reset()
        {
            for (int i = 0; i < bones.Length; i++) previous[i] = bones[i].localRotation;
            previousTip = source.transform.InverseTransformPoint(tip.position);
        }
        public PoseDelta Sample()
        {
            var result = new PoseDelta();
            for (int i = 0; i < bones.Length; i++)
            {
                float delta = Quaternion.Angle(previous[i], bones[i].localRotation);
                if (delta > result.jointStep) { result.jointStep = delta; result.jointName = bones[i].name; }
                previous[i] = bones[i].localRotation;
            }
            Vector3 currentTip = source.transform.InverseTransformPoint(tip.position);
            result.tipStep = Vector3.Distance(previousTip, currentTip); previousTip = currentTip;
            return result;
        }
        public PoseReference CaptureReference() => new PoseReference {
            rotations = bones.Select(bone => bone.localRotation).ToArray(),
            positions = bones.Select(bone => bone.localPosition).ToArray(),
            tip = source.transform.InverseTransformPoint(tip.position)
        };
        public PoseError GuardError(PoseReference reference)
        {
            var result = new PoseError { tip = Vector3.Distance(source.transform.InverseTransformPoint(tip.position), reference.tip) };
            for (int i = 0; i < bones.Length; i++)
            {
                result.jointAngle = Mathf.Max(result.jointAngle, Quaternion.Angle(bones[i].localRotation, reference.rotations[i]));
                result.jointPosition = Mathf.Max(result.jointPosition, Vector3.Distance(bones[i].localPosition, reference.positions[i]));
            }
            return result;
        }
    }

    private struct PoseDelta { public float tipStep, jointStep; public string jointName; }
    private struct PoseError { public float jointAngle, jointPosition, tip; }
    private sealed class PoseReference { public Quaternion[] rotations; public Vector3[] positions; public Vector3 tip; }
    [Serializable] private sealed class Report
    {
        public bool success, clockRestored, playerLoopRestored;
        public string message;
        public int sampleRate, checks;
        public float presentationDuration, cooldownSeconds, entryBlendSeconds, exitNormalizedTime, exitBlendSeconds;
        public float maximumTerminalJointMismatchDegrees, maximumTerminalJointMismatchMetres, maximumTerminalTipMismatchMetres;
        public string stepMeasurement = "Consecutive rendered poses, actor-local sword tip and 17 local joint rotations; finite frame steps, not mathematical discontinuities.";
        public string castPhase = "PlayerLoop.Update, after MonoBehaviour.Update and before Animator evaluation; poses sampled after all PreLateUpdate systems.";
        public List<CastReport> casts = new List<CastReport>();
        public List<FrameReport> frames = new List<FrameReport>();
    }
    [Serializable] private sealed class CastReport
    {
        public string kind;
        public int variant, castFrame, framesFromPrevious;
        public float castAtSeconds, intervalFromPreviousSeconds, rejectedAtSeconds;
        public PreCastState beforeCast;
        public ActorReport local = new ActorReport(), remote = new ActorReport();
    }
    [Serializable] private sealed class PreCastState
    {
        public int frame;
        public bool localGuard, remoteGuard, localInTransition, remoteInTransition;
        public string localState, remoteState;
        public float localNormalizedTime, remoteNormalizedTime, localVariant, remoteVariant;
        public float localJointErrorDegrees, localJointErrorMetres, localTipErrorMetres;
        public float remoteJointErrorDegrees, remoteJointErrorMetres, remoteTipErrorMetres;
    }
    [Serializable] private sealed class ActorReport
    {
        public float entryStartedAtSeconds = -1f, entryFinishedAtSeconds = -1f, exitStartedAtSeconds = -1f, guardAtSeconds = -1f;
        public float visualGuardAtSeconds = -1f;
        public float guardPauseBeforeNextSeconds, guardAfterRejectionSeconds;
        public JumpReport entrySteps = new JumpReport(), contactSteps = new JumpReport(), recoverySteps = new JumpReport(), guardSteps = new JumpReport();
    }
    [Serializable] private sealed class JumpReport
    {
        public int samples;
        public float maximumSwordTipStepMetres, maximumJointStepDegrees;
        public string maximumJointName;
    }
    [Serializable] private sealed class FrameReport
    {
        public int frame, castIndex, variant;
        public string phase, localState, remoteState;
        public float timeSeconds, elapsedSeconds, localNormalizedTime, localExpectedClipWeight;
        public bool localInTransition, remoteInTransition;
    }
}
