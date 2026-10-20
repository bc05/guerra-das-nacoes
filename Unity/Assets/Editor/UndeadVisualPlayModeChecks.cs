using System;
using System.Linq;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class UndeadVisualPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.UndeadVisualChecks.";
    private static PrototypeCombat player;
    private static PrototypeMonster first, second;
    private static GameObject fixtures;
    private static Material sharedBone;
    private static int ownersBefore, checks;
    private static float nextAt;
    private static string progressBefore;
    static UndeadVisualPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar anatomia e reuso dos esqueletos")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Execute fora do PlayMode.");
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetInt(Key + "Stage", 0); SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 60f);
        SessionState.SetBool(Key + "Active", true); nextAt = 0f; checks = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup >= SessionState.GetFloat(Key + "Timeout", 0f)) throw new InvalidOperationException("tempo máximo excedido");
            if (!EditorApplication.isPlaying)
            { if (SessionState.GetBool(Key + "Entered", false)) throw new InvalidOperationException("PlayMode interrompido"); return; }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 4 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0:
                    player = Object.FindFirstObjectByType<PrototypeCombat>();
                    Require(player != null && player.Monsters.Count > 0, "molde original da cena disponível");
                    player.GetComponent<PrototypeOnlineClient>().ConfigureForTests(false);
                    player.GetComponent<PrototypeCharacter>().enabled = false;
                    foreach (PrototypeMonster enemy in player.Monsters) enemy.enabled = false;
                    progressBefore = JsonUtility.ToJson(player.Stats.CaptureSaveData());
                    ownersBefore = PrototypeUndeadVisual.SharedResourceOwners;
                    fixtures = new GameObject("Verificação visual descartável dos esqueletos"); fixtures.SetActive(false);
                    first = Object.Instantiate(player.Monsters[0].gameObject, fixtures.transform).GetComponent<PrototypeMonster>();
                    second = Object.Instantiate(player.Monsters[0].gameObject, fixtures.transform).GetComponent<PrototypeMonster>();
                    first.enabled = false; second.enabled = false; fixtures.SetActive(true);
                    int health = first.CurrentHealth;
                    CharacterController body = first.GetComponent<CharacterController>(); int bodyId = body.GetInstanceID();
                    float oldHeight = body.height, oldRadius = body.radius; Vector3 oldCenter = body.center;
                    Transform source = first.VisualRoot;
                    Show(first, "skeleton"); Show(second, "skeleton");
                    PrototypeUndeadVisual view = first.GetComponent<PrototypeUndeadVisual>();
                    Require(view != null && view.IsApplied && first.VisualRoot == view.ActiveRoot && !source.gameObject.activeSelf,
                        "esqueleto substitui a apresentação do troll e preserva o molde oculto");
                    Require(first.CurrentHealth == health && body.GetInstanceID() == bodyId && body.height > 2f && body.height < 2.25f,
                        "troca visual mantém vida e identidade do collider e usa porte humano");
                    Require(first.VisualRoot.GetComponentsInChildren<Collider>(true).Length == 0, "anatomia e acessórios não criam colliders");
                    foreach (string landmark in new[] { "Crânio", "Mandíbula", "Costela L.0", "Costela R.6", "Vértebra 3", "Úmero", "Rádio", "Ulna", "Fêmur", "Tíbia", "Fíbula", "Lâmina da espada", "Escudo funerário" })
                        Require(Find(first.VisualRoot, landmark) != null, "anatomia autoral contém " + landmark);
                    foreach (string bone in new[] { "Braco.L", "Braco.R", "Perna.L", "Perna.R", "shin.L", "shin.R", "foot.L", "foot.R", "forearm.L", "forearm.R", "hand.L", "hand.R", "Bone.001", "Bone.002", "Bone.003" })
                        Require(Find(first.VisualRoot, bone) != null, "rig articulado contém " + bone);
                    MeshRenderer[] renderers = first.VisualRoot.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
                    Require(renderers.Length < 55 && renderers.Length > 12, "geometria rígida combinada reduz chamadas de desenho preservando articulação");
                    sharedBone = renderers.First(r => r.sharedMaterial.name.Contains("Osso envelhecido")).sharedMaterial;
                    Require(second.VisualRoot.GetComponentsInChildren<MeshRenderer>().Any(r => r.sharedMaterial == sharedBone),
                        "instâncias compartilham o mesmo material ósseo");
                    Mesh firstMesh = renderers.First(r => r.sharedMaterial == sharedBone).GetComponent<MeshFilter>().sharedMesh;
                    Require(second.VisualRoot.GetComponentsInChildren<MeshFilter>().Any(f => f.sharedMesh == firstMesh),
                        "instâncias reutilizam a mesma malha combinada");
                    Transform skeleton = first.VisualRoot; int children = skeleton.GetComponentsInChildren<Transform>(true).Length;
                    for (int index = 0; index < 100; index++) Show(first, "skeleton");
                    Require(first.VisualRoot == skeleton && children == skeleton.GetComponentsInChildren<Transform>(true).Length,
                        "cem snapshots não recriam nem duplicam o modelo");
                    first.gameObject.SetActive(false); first.gameObject.SetActive(true);
                    Require(first.VisualRoot == skeleton && !source.gameObject.activeSelf, "ocultar e reabrir mapa conserva o modelo correto");
                    Show(first, "skeleton-caster");
                    Transform caster = first.VisualRoot;
                    Require(Find(caster, "Capuz aberto do conjurador") != null && Find(caster, "Tira de tecido ritual -1") != null &&
                        Find(caster, "Bastão de ossuário") != null && Find(caster, "Cristal espectral do bastão") != null,
                        "conjurador tem capuz aberto, tecido rasgado, bastão e cristal próprios");
                    Require(Find(caster, "Lâmina da espada") == null && Find(caster, "Escudo funerário") == null,
                        "conjurador possui silhueta distinta do guerreiro");
                    Show(first, "skeleton"); Show(first, "skeleton-caster");
                    Require(first.VisualRoot == caster && !skeleton.gameObject.activeSelf, "trocar variante reutiliza os dois rigs sem sobreposição");
                    first.ApplyOnlineState(new OnlineMonsterState { health = 0, maxHealth = 60, visualKind = "skeleton-caster" }, 100d);
                    Require(!caster.gameObject.activeSelf && !source.gameObject.activeSelf, "morte oculta esqueleto sem reativar troll");
                    first.ApplyOnlineState(new OnlineMonsterState { health = 60, maxHealth = 60, visualKind = "skeleton-caster" }, 101d);
                    Require(caster.gameObject.activeSelf && first.VisualRoot == caster && !source.gameObject.activeSelf,
                        "reaparecimento mostra somente a espécie esquelética");
                    Transform arm = Find(caster, "Braco.R"); Quaternion rest = arm.localRotation;
                    first.ApplyOnlineState(new OnlineMonsterState { health = 60, maxHealth = 60, x = 0f, z = 0f,
                        behavior = "ranged", attackPreparation = 1.1f, attackInterval = 2f, windupUntil = 101.5d }, 101d);
                    typeof(PrototypeMonster).GetMethod("ApplyVisualPose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(first, new object[] { false, .2f });
                    Require(Quaternion.Angle(rest, arm.localRotation) > 1f, "preparação procedural articula o novo braço e seu bastão");
                    Show(first, "troll");
                    Require(first.VisualRoot == source && source.gameObject.activeSelf && !caster.gameObject.activeSelf &&
                        body.height == oldHeight && body.radius == oldRadius && body.center == oldCenter,
                        "retornar ao troll restaura visual e dimensões originais");
                    Require(JsonUtility.ToJson(player.Stats.CaptureSaveData()) == progressBefore, "trocas visuais não alteram progresso do personagem");
                    Object.DestroyImmediate(fixtures); fixtures = null;
                    Next(1, .08f); break;
                case 1:
                    Require(PrototypeUndeadVisual.SharedResourceOwners == ownersBefore,
                        "destruir instâncias devolve todas as referências ao cache compartilhado");
                    if (ownersBefore == 0) Require(sharedBone == null, "última destruição libera materiais autorais");
                    Finish(true, checks + " verificações de anatomia, articulação, identidade, cache, reaparecimento, restauração e descarte."); break;
            }
        }
        catch (Exception error) { Finish(false, "etapa=" + SessionState.GetInt(Key + "Stage", -1) + " " + error); }
    }
    private static void Show(PrototypeMonster target, string kind) => PrototypeWorldExpansion.ConfigureMonsterVisual(target,
        new OnlineMonsterState { visualKind = kind, visualScale = 1f, speciesId = kind });
    private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    private static void Next(int stage, float delay) { SessionState.SetInt(Key + "Stage", stage); nextAt = Time.time + delay; }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks++; }
    private static void Finish(bool success, string text)
    {
        SessionState.SetBool(Key + "Active", false);
        if (fixtures != null) Object.DestroyImmediate(fixtures);
        if (success) Debug.Log("[UNDEAD VISUAL PLAY PASS] " + text); else Debug.LogError("[UNDEAD VISUAL PLAY FAIL] " + text);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
