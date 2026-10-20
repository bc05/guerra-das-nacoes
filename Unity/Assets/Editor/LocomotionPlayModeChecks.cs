using System;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs without connecting, saving progression or injecting platform keyboard events.
// The real character consumes a click destination and collides with an isolated wall.
[InitializeOnLoad]
public static class LocomotionPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.LocomotionChecks.";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static PrototypeCharacter character;
    private static PrototypeCombat combat;
    private static PrototypeRemotePlayer remote;
    private static Animator animator, remoteAnimator;
    private static GameObject ground, wall;
    private static string originalProgress;
    private static Vector3 startPosition, remoteTarget;
    private static float nextAt, phaseStartedAt, remoteSpeed, lastSnapshotAt, nextSnapshotAt;
    private static int assertions;
    private static float previousGaitPhase;
    private static bool observedContinuousPhase;

    static LocomotionPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar locomoção local e remota")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie a validação fora do PlayMode.");
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 50f);
        SessionState.SetBool(Key + "Active", true);
        assertions = 0; nextAt = 0f; remoteSpeed = 0f;
        previousGaitPhase = -1f; observedContinuousPhase = false;
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
                Guard(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 4) return;
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (animator != null && stage >= 1 && stage <= 4)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                Guard(state.IsName("Idle") && !animator.IsInTransition(0),
                    "arranque, colisão, parada e corrida devem compartilhar a mesma árvore contínua");
                Guard(previousGaitPhase < 0f || state.normalizedTime >= previousGaitPhase-.001f,
                    "a mudança de velocidade reiniciou a fase da passada");
                if (previousGaitPhase >= 0f && state.normalizedTime > previousGaitPhase+.001f)
                    observedContinuousPhase = true;
                previousGaitPhase = state.normalizedTime;
            }
            if (remote != null && remoteSpeed > 0f && Time.time >= nextSnapshotAt)
            {
                remoteTarget.x += remoteSpeed * (Time.time - lastSnapshotAt);
                lastSnapshotAt = Time.time; nextSnapshotAt = Time.time + .1f;
                ApplyRemote();
            }
            if (Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    character = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
                    Require(character != null, "personagem da cena presente");
                    combat = character.GetComponent<PrototypeCombat>();
                    character.GetComponent<PrototypeOnlineClient>().ConfigureForTests(false);
                    foreach (PrototypeMonster monster in combat.Monsters) monster.enabled = false;
                    originalProgress = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    animator = character.VisualRoot.GetComponentInChildren<Animator>();
                    Require(Mathf.Abs(GetCharacterSpeed("walkSpeed") - PrototypeCharacter.DefaultWalkSpeed) < .0001f &&
                        Mathf.Abs(GetCharacterSpeed("runSpeed") - PrototypeCharacter.DefaultRunSpeed) < .0001f,
                        $"cena salva usa caminhada de {PrototypeCharacter.DefaultWalkSpeed:0.##} m/s e corrida de {PrototypeCharacter.DefaultRunSpeed:0.##} m/s");
                    ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ground.name = "Piso temporário da verificação de locomoção";
                    ground.layer = 8;
                    ground.transform.position = new Vector3(500f, -.5f, 500f);
                    ground.transform.localScale = new Vector3(30f, 1f, 30f);
                    CharacterController capsule = character.GetComponent<CharacterController>();
                    capsule.enabled = false;
                    character.transform.SetPositionAndRotation(new Vector3(500f, .04f, 500f), Quaternion.identity);
                    capsule.enabled = true; Physics.SyncTransforms();
                    startPosition = character.transform.position;
                    SetDestination(startPosition + Vector3.forward * 10f);
                    Next(1, .7f); break;
                case 1:
                    Require(character.IsMoving && character.transform.position.z - startPosition.z > .65f,
                        "destino percorre espaço real após a aceleração");
                    Require(Mathf.Abs(animator.GetFloat("LocomotionSpeed") - PrototypeCharacter.DefaultWalkSpeed) < .15f,
                        $"animação recebe os {PrototypeCharacter.DefaultWalkSpeed:0.##} m/s realmente percorridos sem Shift");
                    Require(Mathf.Abs(animator.GetFloat("MoveSpeed") - 1f) < .1f,
                        "velocidade normal usa a mesma referência física do parâmetro legado");
                    CheckLocalWalking("movimento normal sem Shift");
                    Require(Mathf.Abs(animator.speed - 1f) < .001f, "locomoção não acelera o Animator inteiro");
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name = "Parede temporária da verificação de locomoção";
                    wall.transform.position = character.transform.position + Vector3.forward * 1.2f + Vector3.up;
                    wall.transform.localScale = new Vector3(3f, 2f, .2f);
                    Physics.SyncTransforms();
                    SetDestination(character.transform.position + Vector3.forward * 8f);
                    Next(2, .9f); break;
                case 2:
                    Require(!character.IsMoving && !character.HasDestination, "parede interrompe deslocamento e destino");
                    Require(animator.GetFloat("LocomotionSpeed") < .15f, "pés param de caminhar contra a parede");
                    UnityEngine.Object.DestroyImmediate(wall); wall = null;
                    // This instance-only override checks running cadence, not Shift input handling.
                    // Platform keyboard events are deliberately not injected by this test.
                    SetCharacter("walkSpeed", PrototypeCharacter.DefaultRunSpeed);
                    SetDestination(character.transform.position + Vector3.forward * 10f);
                    Next(3, .7f); break;
                case 3:
                    Require(observedContinuousPhase, "fase contínua avança durante aceleração, obstáculo, parada e corrida");
                    Require(Mathf.Abs(animator.GetFloat("LocomotionSpeed") - PrototypeCharacter.DefaultRunSpeed) < .35f,
                        "corrida mantém 5,5 m/s no parâmetro físico de animação");
                    Require(ClipWeight(animator, "Run") > .98f,
                        "velocidade de corrida seleciona o clipe Run");
                    Require(Mathf.Abs(animator.speed - 1f) < .001f, "corrida preserva velocidade global de animação");
                    SetCharacter("walkSpeed", PrototypeCharacter.DefaultWalkSpeed);
                    Next(4, .5f); break;
                case 4:
                    Require(Mathf.Abs(animator.GetFloat("LocomotionSpeed") - PrototypeCharacter.DefaultWalkSpeed) < .15f,
                        "redução da velocidade retorna à caminhada após desacelerar");
                    CheckLocalWalking("retorno à caminhada após a corrida");
                    character.enabled = false;
                    startPosition = character.transform.position;
                    Require(!character.HasDestination && animator.GetFloat("LocomotionSpeed") == 0f,
                        "desativação limpa destino e velocidade visual imediatamente");
                    Next(5, .2f); break;
                case 5:
                    Require(Vector3.Distance(character.transform.position, startPosition) < .001f,
                        "personagem desativado não continua deslizando");
                    remote = PrototypeRemotePlayer.Create(character, "locomotion-check");
                    Require(!remote.HasGameplayComponents, "remoto continua visual sem colisores ou combate local");
                    remoteAnimator = remote.GetComponentInChildren<Animator>();
                    remoteTarget = new Vector3(600f, character.transform.position.y, 500f);
                    ApplyRemote();
                    remoteSpeed = PrototypeCharacter.DefaultWalkSpeed; lastSnapshotAt = Time.time; nextSnapshotAt = Time.time + .1f;
                    startPosition = remote.transform.position;
                    Next(6, 1f); break;
                case 6:
                    CheckRemoteSpeed(PrototypeCharacter.DefaultWalkSpeed, false);
                    remoteSpeed = PrototypeCharacter.DefaultRunSpeed;
                    startPosition = remote.transform.position;
                    Next(7, 1f); break;
                case 7:
                    CheckRemoteSpeed(PrototypeCharacter.DefaultRunSpeed, true);
                    remoteSpeed = 0f; ApplyRemote();
                    Next(8, .4f); break;
                case 8:
                    Require(remoteAnimator.GetFloat("LocomotionSpeed") < .15f,
                        "remoto entra em repouso após o último snapshot parado");
                    Require(combat.TryAttack(), "ataque local executa sem alvo e longe dos monstros");
                    remote.PlayAttack(0);
                    Next(9, .2f); break;
                case 9:
                    Require(combat.IsAttacking && combat.AttackProgress > .25f && combat.AttackProgress < .8f,
                        "recuperação do golpe permanece ativa no meio do clipe");
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack") &&
                        remoteAnimator.GetCurrentAnimatorStateInfo(0).IsName("Attack"),
                        "ataque usa o mesmo estado no herói e no remoto");
                    Require(Mathf.Abs(animator.speed - 1f) < .001f && Mathf.Abs(remoteAnimator.speed - 1f) < .001f,
                        "cadência de corrida não acelera o ataque de nenhum jogador");
                    Next(10, PrototypeCombat.AttackPresentationDuration - .2f + .02f); break;
                case 10:
                    Require(!combat.IsAttacking && !combat.HasPendingStrike && combat.CooldownRemaining > 0f,
                        "recuperação visual termina antes da recarga autoritativa de0.65s");
                    Require(JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                        "movimento, colisão e apresentação remota preservam todo o progresso");
                    Finish(true, assertions + " verificações de deslocamento, colisão, parada, snapshots, corrida e recuperação de ataque.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void SetCharacter(string name, object value) =>
        typeof(PrototypeCharacter).GetField(name, PrivateInstance).SetValue(character, value);

    private static float GetCharacterSpeed(string name) =>
        (float)typeof(PrototypeCharacter).GetField(name, PrivateInstance).GetValue(character);

    private static float ClipWeight(Animator source, string name)
    {
        float weight = 0f;
        foreach (AnimatorClipInfo clip in source.GetCurrentAnimatorClipInfo(0))
            if (clip.clip.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                weight += clip.weight;
        return weight;
    }

    private static void CheckLocalWalking(string context)
    {
        Require(ClipWeight(animator, "Walk") > .98f && ClipWeight(animator, "Run") < .01f,
            context + " seleciona Walk e mantém Run sem peso significativo");
    }

    private static void SetDestination(Vector3 destination)
    {
        SetCharacter("destination", destination); SetCharacter("hasDestination", true);
    }

    private static void ApplyRemote() => remote.ApplyState(new OnlinePlayerState
    {
        id = "locomotion-check", x = remoteTarget.x, z = remoteTarget.z, health = 100, maxHealth = 100,
        equipped = combat.Stats.CaptureSaveData().equipped
    }, 1000d + Time.time);

    private static void CheckRemoteSpeed(float expected, bool running)
    {
        float observed = Vector3.Distance(remote.transform.position, startPosition) / (Time.time - phaseStartedAt);
        Require(Mathf.Abs(observed - expected) < expected * .25f,
            "snapshots interpolam o deslocamento remoto a" + expected + "m/s; observado=" + observed);
        Require(Mathf.Abs(remoteAnimator.GetFloat("LocomotionSpeed") - expected) < expected * .35f,
            "cadência remota acompanha" + expected + "m/s sem pulso fixo de caminhada");
        Require(Mathf.Abs(remoteAnimator.GetFloat("MoveSpeed") -
            remoteAnimator.GetFloat("LocomotionSpeed") / PrototypeCharacter.DefaultWalkSpeed) < .05f,
            "remoto normaliza MoveSpeed pela velocidade de caminhada");
        // Snapshot interpolation can momentarily blend neighboring clips; tolerate this jitter.
        Require(running ? ClipWeight(remoteAnimator, "Run") > .7f :
            ClipWeight(remoteAnimator, "Walk") > .7f && ClipWeight(remoteAnimator, "Run") < .1f,
            running ? "snapshots de corrida usam predominantemente Run" :
                "snapshots de caminhada usam predominantemente Walk");
        Require(Mathf.Abs(remoteAnimator.speed - 1f) < .001f, "remoto mantém Animator global a1x");
    }

    private static void Next(int stage, float delay)
    {
        SessionState.SetInt(Key + "Stage", stage); phaseStartedAt = Time.time; nextAt = Time.time + delay;
    }
    private static void Guard(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Require(bool condition, string message) { Guard(condition, message); assertions++; }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (remote != null) UnityEngine.Object.DestroyImmediate(remote.gameObject);
        if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
        if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
        if (success) Debug.Log("[LOCOMOTION PLAY PASS] " + message);
        else Debug.LogError("[LOCOMOTION PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
