using System;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Real CharacterController collision outside the authored level; never connects or saves progression.
[InitializeOnLoad]
public static class ApproachBlockPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.ApproachBlockChecks.";
    private static PrototypeCharacter character;
    private static PrototypeCombat combat;
    private static PrototypeMonster enemy;
    private static GameObject floor, wall;
    private static float nextAt;
    private static int originalHealth, swings, checks;
    private static Vector3 stoppedAt;
    private static string originalProgress;
    static ApproachBlockPlayModeChecks() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 35f);
        SessionState.SetBool(Key + "Active", true);
        nextAt = 0f; checks = 0;
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
                Guard(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrado antecipadamente"); return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 4 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    character = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
                    combat = character.GetComponent<PrototypeCombat>();
                    character.GetComponent<PrototypeOnlineClient>().ConfigureForTests(false);
                    foreach (PrototypeMonster monster in combat.Monsters) monster.enabled = false;
                    enemy = combat.Monsters[0]; combat.ResetEncounter();
                    floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.layer = 8;
                    floor.transform.position = new Vector3(500, -.5f, 500);
                    floor.transform.localScale = new Vector3(30, 1, 30);
                    Move(character.transform, new Vector3(500, .04f, 500)); character.transform.rotation = Quaternion.identity;
                    Move(enemy.transform, new Vector3(500, .06f, 507));
                    typeof(PrototypeMonster).GetField("homePosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(enemy, enemy.transform.position);
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = new Vector3(500, 1.25f, 501.4f);
                    wall.transform.localScale = new Vector3(6, 3, .4f); Physics.SyncTransforms();
                    combat.SetTarget(enemy); originalHealth = enemy.CurrentHealth; swings = combat.SwingCount;
                    originalProgress = JsonUtility.ToJson(combat.Stats.CaptureSaveData());
                    Require(combat.RequestAttack(0) && combat.HasQueuedAttack, "alvo distante inicia aproximação real até a parede");
                    SessionState.SetInt(Key + "Stage", 1); break;
                case 1:
                    if (combat.HasQueuedAttack) return;
                    Require(!character.HasFreeAttack && !character.HasDestination,
                        "colisão bloqueada cancela a aproximação até um novo comando explícito");
                    Require(combat.Message != null && combat.Message.Contains("Caminho bloqueado"), "bloqueio explica o próximo passo ao jogador");
                    Require(character.transform.position.z > 500.2f && character.transform.position.z < 501.2f,
                        "cápsula percorre o caminho livre e para diante da parede");
                    Require(combat.SwingCount == swings && enemy.CurrentHealth == originalHealth && combat.CooldownRemaining == 0,
                        "bloqueio não inventa golpe, dano ou recarga");
                    stoppedAt = character.transform.position; Next(2, .35f); break;
                case 2:
                    Require(!combat.HasQueuedAttack && !character.IsMoving && Vector3.Distance(stoppedAt, character.transform.position) < .06f,
                        "personagem permanece parado após cancelar a aproximação bloqueada");
                    Require(character.VisualRoot.GetComponentInChildren<Animator>().GetFloat("LocomotionSpeed") < .2f,
                        "animação deixa de correr contra o obstáculo");
                    Require(JsonUtility.ToJson(combat.Stats.CaptureSaveData()) == originalProgress,
                        "cancelamento preserva atributos, vida, mochila, equipamentos e experiência");
                    UnityEngine.Object.DestroyImmediate(wall); wall = null;
                    Physics.SyncTransforms();
                    Next(3, 2f * combat.AttackInterval + .1f); break;
                case 3:
                    Require(!combat.HasQueuedAttack && combat.SwingCount == swings &&
                        Vector3.Distance(stoppedAt, character.transform.position) < .06f,
                        "liberar o caminho e esperar a recarga não recria um ataque cancelado");
                    Require(combat.RequestAttack(0) && combat.HasQueuedAttack, "nova intenção explícita permite tentar um caminho liberado");
                    float approachDistance = combat.TryGetApproachDestination(out Vector3 destination)
                        ? Vector3.ProjectOnPlane(destination - character.transform.position, Vector3.up).magnitude : 0f;
                    // Include acceleration and the existing strike delay after walking into range.
                    Next(4, approachDistance / PrototypeCharacter.DefaultWalkSpeed + .5f); break;
                case 4:
                    Require(!combat.HasQueuedAttack && combat.SwingCount == swings + 1 && enemy.CurrentHealth < originalHealth,
                        "sem parede a mesma aproximação alcança o alvo e executa um único ataque");
                    Finish(true, checks + " verificações de colisão, parada, repetição bloqueada, progresso e caminho liberado."); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Move(Transform target, Vector3 position)
    {
        CharacterController capsule = target.GetComponent<CharacterController>(); capsule.enabled = false;
        target.position = position; capsule.enabled = true;
    }
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Guard(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Require(bool value, string message) { Guard(value, message); checks++; }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
        if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
        if (success) Debug.Log("[APPROACH BLOCK PLAY PASS] " + message);
        else Debug.LogError("[APPROACH BLOCK PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
