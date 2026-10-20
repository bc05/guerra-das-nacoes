using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PrototypeSetup
{
    private const string ScenePath = "Assets/Scenes/Patio.unity";
    private const string CharacterPath = "Assets/Art/Cavaleiro.fbx";
    private const string MotionProfilePath = "Assets/Art/CavaleiroMotionProfile.json";
    private const string EnvironmentPath = "Assets/Art/Cenario.fbx";
    private const string MonsterPath = "Assets/Art/Troll.fbx";
    private const string ControllerPath = "Assets/Animations/Personagem.controller";
    private const int GroundLayer = 8;
    private static readonly Dictionary<string, Color> MaterialColors = new Dictionary<string, Color>
    {
        { "MAT_ArmaduraAzul", new Color(0.43f, 0.48f, 0.53f) },
        { "MAT_AzulClaro", new Color(0.53f, 0.58f, 0.62f) },
        { "MAT_Cobre", new Color(0.46f, 0.36f, 0.25f) },
        { "MAT_TecidoEscuro", new Color(0.14f, 0.16f, 0.19f) },
        { "MAT_Couro", new Color(0.32f, 0.21f, 0.14f) },
        { "MAT_Pele", new Color(0.49f, 0.34f, 0.24f) },
        { "MAT_CharacterOlho", new Color(0.68f, 0.66f, 0.58f) },
        { "MAT_CharacterMalha", new Color(0.38f, 0.42f, 0.46f) },
        { "MAT_Aco", new Color(0.65f, 0.68f, 0.7f) },
        { "MAT_Visor", new Color(0.12f, 0.48f, 0.62f) },
        { "MAT_Chao", new Color(0.70f, 0.70f, 0.70f) },
        { "MAT_Pedra", new Color(0.66f, 0.69f, 0.72f) },
        { "MAT_PedraClara", new Color(0.75f, 0.76f, 0.77f) },
        { "MAT_PedraEscura", new Color(0.43f, 0.45f, 0.48f) },
        { "MAT_Caminho", new Color(0.58f, 0.61f, 0.65f) },
        { "MAT_Musgo", new Color(0.19f, 0.23f, 0.14f) },
        { "MAT_Tronco", new Color(0.3f, 0.23f, 0.16f) },
        { "MAT_Pinheiro", new Color(0.12f, 0.18f, 0.12f) },
        { "MAT_PinheiroClaro", new Color(0.18f, 0.24f, 0.15f) },
        { "MAT_MetalEstandarte", new Color(0.3f, 0.24f, 0.16f) },
        { "MAT_NacaoAzul", new Color(0.055f, 0.11f, 0.18f) },
        { "MAT_NacaoVermelha", new Color(0.23f, 0.045f, 0.055f) },
        { "MAT_GolemPedra", new Color(0.24f, 0.27f, 0.26f) },
        { "MAT_GolemPedraClara", new Color(0.38f, 0.40f, 0.38f) },
        { "MAT_GolemCristal", new Color(0.72f, 0.3f, 0.065f) },
        { "MAT_GolemEscuro", new Color(0.05f, 0.07f, 0.065f) },
        { "MAT_GolemMusgo", new Color(0.15f, 0.22f, 0.08f) }
    };

    [MenuItem("Guerra das Nações/Preparar protótipo")]
    public static void PrepareAndValidate()
    {
        ConfigureProject();
        EnsureDirectory("Assets/Scenes");
        EnsureDirectory("Assets/Animations");
        EnsureDirectory("Assets/Materials");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureSurfaceTextures();

        ConfigureModel(CharacterPath, true);
        ConfigureModel(EnvironmentPath, false);
        ConfigureModel(MonsterPath, false, true);
        RemapMaterials(CharacterPath);
        RemapMaterials(EnvironmentPath);
        RemapMaterials(MonsterPath);
        ThirdPartyCharacterMaterials.Configure();
        ThirdPartyEnvironmentSetup.ConfigureMaterialsAndAssets();
        GameObject characterModel = RequireModel(CharacterPath);
        GameObject environmentModel = RequireModel(EnvironmentPath);
        GameObject monsterModel = RequireModel(MonsterPath);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject environment = (GameObject)PrefabUtility.InstantiatePrefab(environmentModel, scene);
        environment.name = "Cenario";
        environment.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
        environment.isStatic = true;
        int groundObjects = 0;
        foreach (MeshFilter mesh in environment.GetComponentsInChildren<MeshFilter>(true))
        {
            mesh.gameObject.isStatic = true;
            MeshCollider collider = mesh.GetComponent<MeshCollider>();
            if (mesh.name.StartsWith("Deco_", StringComparison.Ordinal)) continue;
            if (collider == null)
                collider = mesh.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh.sharedMesh;
            if (mesh.name.StartsWith("Ground_", StringComparison.OrdinalIgnoreCase))
            {
                mesh.gameObject.layer = GroundLayer;
                groundObjects++;
            }
        }
        if (groundObjects == 0)
            throw new BuildFailedException("Cenario.fbx deve conter pelo menos uma malha com prefixo Ground_ para permitir clique no chão.");
        ThirdPartyEnvironmentSetup.Populate(scene);

        GameObject player = new GameObject("Jogador");
        player.layer = 10;
        player.transform.position = new Vector3(0f, 0.04f, 0f);
        CharacterController characterController = player.AddComponent<CharacterController>();
        characterController.height = 1.8f;
        characterController.radius = 0.3f;
        characterController.center = new Vector3(0f, 0.9f, 0f);
        characterController.stepOffset = 0.3f;
        characterController.slopeLimit = 45f;
        characterController.skinWidth = 0.04f;
        characterController.minMoveDistance = 0f;

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(player.transform, false);
        GameObject hero = (GameObject)PrefabUtility.InstantiatePrefab(characterModel, scene);
        hero.name = "Personagem";
        hero.transform.SetParent(visual.transform, false);
        SetupAnimator(hero);

        PrototypeCharacter movement = player.AddComponent<PrototypeCharacter>();
        movement.Configure(visual.transform);
        PrototypeEquipmentAppearanceEditor.Configure(player);
        Material slashMaterial = LoadOrCreateMaterial("Assets/Materials/CorteArcano.mat", "GuerraDasNacoes/AttackGlow", Color.white);
        Material impactMaterial = LoadOrCreateMaterial("Assets/Materials/ImpactoArcano.mat", "GuerraDasNacoes/AttackGlow", Color.white);
        player.GetComponent<PrototypeAttackEffects>().Configure(slashMaterial, impactMaterial);
        player.GetComponent<PrototypeStatusEffects>().Configure(slashMaterial);

        PrototypeCombat combat = player.GetComponent<PrototypeCombat>();
        player.AddComponent<PrototypeOnlineClient>();
        PrototypeMonster[] monsters = new PrototypeMonster[3];
        Vector3[] posts = { new Vector3(-2.6f, 0.06f, 3.8f), new Vector3(0f, 0.06f, 5.8f), new Vector3(2.6f, 0.06f, 3.8f) };
        for (int index = 0; index < monsters.Length; index++)
        {
            bool elite = index == 1;
            float height = elite ? 3.9f : 3.2f;
            GameObject monsterObject = new GameObject(elite ? "Troll Ancião — Elite" : "Troll Saqueador " + (index + 1));
            monsterObject.layer = 9;
            monsterObject.transform.SetPositionAndRotation(posts[index], Quaternion.Euler(0f, 180f, 0f));
            CharacterController monsterCollider = monsterObject.AddComponent<CharacterController>();
            monsterCollider.height = height;
            monsterCollider.radius = elite ? 1.0f : 0.8f;
            monsterCollider.center = new Vector3(0f, height * 0.5f, 0f);
            monsterCollider.stepOffset = 0.3f;
            monsterCollider.skinWidth = 0.04f;
            monsterCollider.minMoveDistance = 0f;
            GameObject monsterVisual = (GameObject)PrefabUtility.InstantiatePrefab(monsterModel, scene);
            monsterVisual.name = "Visual do troll";
            monsterVisual.transform.SetParent(monsterObject.transform, false);
            monsterVisual.transform.localScale = Vector3.one * (height / 3.2f);
            if (elite) ThirdPartyCharacterMaterials.ApplyElite(monsterVisual);
            monsters[index] = monsterObject.AddComponent<PrototypeMonster>();
            monsters[index].ConfigureStats(elite ? "Troll Ancião · Elite" : "Troll Saqueador", elite ? 600 : 240, elite ? 16 : 10, elite ? 240 : 120);
            monsters[index].Configure(combat, monsterVisual.transform);
        }
        combat.Configure(monsters[0], monsters);
        player.GetComponent<PrototypeAreaEffects>().Configure(slashMaterial);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.15f, 0.20f, 0.25f);
        camera.fieldOfView = 50f;
        camera.allowHDR = true;
        cameraObject.AddComponent<PrototypePresentation>().Configure(Shader.Find("GuerraDasNacoes/RuinsPresentation"));
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 140f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<PrototypeAudio>();
        cameraObject.AddComponent<PrototypeCamera>().Configure(player.transform);

        GameObject lightObject = new GameObject("Sol do fim da tarde");
        Light sunlight = lightObject.AddComponent<Light>();
        sunlight.type = LightType.Directional;
        sunlight.color = new Color(1.0f, 0.95f, 0.86f);
        sunlight.intensity = 1.25f;
        sunlight.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
        sunlight.shadowBias = 0.025f;
        sunlight.shadowNormalBias = 0.20f;
        sunlight.shadows = LightShadows.Soft;
        sunlight.shadowStrength = 0.85f;
        lightObject.transform.rotation = Quaternion.Euler(37f, -38f, 0f);

        GameObject fillObject = new GameObject("Luz difusa das ruínas");
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.72f, 0.80f, 0.91f);
        fill.intensity = 0.20f;
        fill.shadows = LightShadows.None;
        fillObject.transform.rotation = Quaternion.Euler(35f, 145f, 0f);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.29f, 0.37f, 0.48f);
        RenderSettings.ambientEquatorColor = new Color(0.19f, 0.23f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.10f, 0.12f, 0.14f);
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflection = CreateAmbientReflection();
        RenderSettings.reflectionIntensity = 1.1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = camera.backgroundColor;
        RenderSettings.fogStartDistance = 18f;
        RenderSettings.fogEndDistance = 52f;
        ThirdPartyEnvironmentSetup.ConfigureLighting();

        new GameObject("Interface de instruções").AddComponent<PrototypeHud>().Configure(movement);
        EditorSceneManager.SaveScene(scene, ScenePath);
        ServerWorldExporter.Export();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = player;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(Vector3.up, Quaternion.Euler(45f, 0f, 0f), 18f);

        Validate();
        Debug.Log("[PROTOTIPO PASS] Cena pronta em Assets/Scenes/Patio.unity. Abra a aba Game e pressione Play.");
    }

    [MenuItem("Guerra das Nações/Validar protótipo")]
    public static void Validate()
    {
        UnityEngine.Object settingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (settingsAsset == null || new SerializedObject(settingsAsset).FindProperty("activeInputHandler")?.intValue != 0)
            Fail("A API de entrada antiga deve estar habilitada (activeInputHandler=0).");
        if (LayerMask.LayerToName(GroundLayer) != "Terreno")
            Fail("Camada8 Terreno não configurada.");
        if (LayerMask.LayerToName(9) != "Inimigos" || LayerMask.LayerToName(10) != "Jogador")
            Fail("Camadas de inimigos/jogador ausentes.");
        if (!File.Exists(ScenePath))
            Fail("Cena Assets/Scenes/Patio.unity ausente. Execute Preparar protótipo.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject[] roots = scene.GetRootGameObjects();
        PrototypeCharacter player = roots.SelectMany(root => root.GetComponentsInChildren<PrototypeCharacter>(true)).SingleOrDefault();
        PrototypeCamera camera = roots.SelectMany(root => root.GetComponentsInChildren<PrototypeCamera>(true)).SingleOrDefault();
        if (player == null || player.GetComponent<CharacterController>() == null || player.VisualRoot == null)
            Fail("Jogador, CharacterController ou visual ausente.");
        if (camera == null || camera.Target != player.transform || camera.GetComponent<Camera>().tag != "MainCamera")
            Fail("Câmera principal sem vínculo com o jogador.");
        if (!roots.Any(root => root.GetComponent<PrototypeHud>() != null))
            Fail("Interface com instruções ausente.");
        PrototypeCombat combat = player.GetComponent<PrototypeCombat>();
        PrototypeMonster monster = roots.SelectMany(root => root.GetComponentsInChildren<PrototypeMonster>(true)).FirstOrDefault();
        if (combat == null || monster == null || combat.Monster != monster)
            Fail("Combate ou monstro não configurado.");
        if (player.GetComponent<PrototypeAbilities>() == null)
            Fail("O personagem está sem habilidades e recurso de mana.");
        if (player.GetComponent<PrototypeOnlineClient>() == null)
            Fail("O personagem está sem cliente online.");
        if (player.GetComponent<PrototypeStatusEffects>() == null || !player.GetComponent<PrototypeStatusEffects>().IsConfigured)
            Fail("O personagem está sem efeitos das habilidades ativas.");
        PrototypeEquipmentAppearanceEditor.Validate(player.gameObject);
        if (combat.Monsters.Count != 3 || combat.Monsters.Any(enemy => enemy == null))
            Fail("O encontro precisa de três guardiões configurados para o combate em área.");
        foreach (PrototypeMonster enemy in combat.Monsters)
        {
            if (enemy.gameObject.layer != 9) Fail("Guardião fora da camada Inimigos.");
            Transform[] nodes = enemy.GetComponentsInChildren<Transform>(true);
            foreach (string pivot in new[] { "Braco.L", "Braco.R", "Perna.L", "Perna.R" })
                if (!nodes.Any(node => node.name == pivot)) Fail("Pivô de animação ausente: " + pivot);
            SkinnedMeshRenderer[] skins = enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0 || skins.All(skin => skin.bones.Length == 0 || skin.sharedMesh.bindposes.Length == 0))
                Fail("O troll precisa de malha com pesos vinculados ao esqueleto para apresentar os golpes.");
        }
        Shader presentation = Shader.Find("GuerraDasNacoes/RuinsPresentation");
        if (camera.GetComponent<PrototypePresentation>() == null || presentation == null || ShaderUtil.ShaderHasError(presentation))
            Fail("Apresentação da câmera ausente ou shader com erro.");
        if (player.GetComponent<PrototypeAreaEffects>() == null)
            Fail("Varredura de energia em área ausente.");
        if (player.GetComponent<PrototypeAttackEffects>() == null || !player.GetComponent<PrototypeAttackEffects>().IsConfigured)
            Fail("Efeito de ataque não configurado.");
        if (monster.GetComponent<CharacterController>() == null || monster.GetComponentsInChildren<Renderer>(true).Length == 0)
            Fail("O golem está sem colisão ou visual.");
        if (monster.GetComponentsInChildren<Transform>(true).All(child => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) != MonsterPath))
            Fail("O troll não está vinculado ao modelo Troll.fbx.");
        CombatRulesChecks.Run();
        EquipmentRulesChecks.Run();
        ProgressionPersistenceChecks.Run();
        EconomyCoreChecks.Run();
        MenuRosterChecks.Run();

        GameObject environment = roots.SingleOrDefault(root => root.name == "Cenario");
        if (environment == null || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(environment) != EnvironmentPath)
            Fail("O cenário não está vinculado ao FBX esperado.");
        GameObject model = player.VisualRoot.GetChild(0).gameObject;
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model) != CharacterPath)
            Fail("O personagem não está vinculado ao FBX esperado.");
        Transform[] characterNodes = model.GetComponentsInChildren<Transform>(true);
        if (!characterNodes.Any(node => node.name == "SwordBase") || !characterNodes.Any(node => node.name == "SwordTip"))
            Fail("A espada precisa dos pontos SwordBase e SwordTip para o rastro de energia.");
        float bladeLength = Vector3.Distance(characterNodes.First(node => node.name == "SwordBase").position,
            characterNodes.First(node => node.name == "SwordTip").position);
        if (bladeLength < 1f || bladeLength > 1.05f)
            Fail("Os pontos do rastro precisam acompanhar a lâmina de 1,025 m.");
        int environmentMeshes = environment.GetComponentsInChildren<MeshFilter>(true).Length;
        int characterMeshes = model.GetComponentsInChildren<MeshFilter>(true).Length + model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
        if (environmentMeshes == 0 || characterMeshes == 0)
            Fail("FBX importado sem malhas renderizáveis.");
        Animator animator = model.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null ||
            !animator.parameters.Any(parameter => parameter.name == "MoveSpeed" && parameter.type == AnimatorControllerParameterType.Float))
            Fail("O personagem está sem Animator ou parâmetro MoveSpeed.");
        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (!clips.Any(clip => clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0) ||
            !clips.Any(clip => clip.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0) ||
            !clips.Any(clip => clip.name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0) ||
            PrototypeAttackAnimation.ClipNames.Any(name => !clips.Any(clip =>
                clip.name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                clip.name.EndsWith("|" + name, StringComparison.OrdinalIgnoreCase))))
            Fail("As animações de locomoção e os quatro golpes não foram vinculados ao personagem.");
        if (!animator.parameters.Any(parameter => parameter.name == "Attack" && parameter.type == AnimatorControllerParameterType.Trigger) ||
            !animator.parameters.Any(parameter => parameter.name == PrototypeAttackAnimation.VariantParameter &&
                parameter.type == AnimatorControllerParameterType.Float))
            Fail("O personagem está sem o gatilho ou seletor da animação de ataque.");
        if (clips.Any(clip => clip.name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0 && clip.isLooping))
            Fail("A animação Attack deve terminar sem repetição automática.");
        foreach (string materialName in new[] { "MAT_Knight" })
        {
            Material surface = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + materialName + ".mat");
            if (surface == null || surface.GetTexture("_MainTex") == null || surface.GetTexture("_BumpMap") == null ||
                surface.GetTexture("_MetallicGlossMap") == null)
                Fail("Material refinado sem os três mapas de superfície: " + materialName);
        }
        if (RenderSettings.customReflection == null)
            Fail("Reflexo de ambiente ausente para os materiais metálicos.");
        foreach (PrototypeMonster enemy in combat.Monsters)
            if (enemy.MaxHealth < 240 || enemy.GetComponent<CharacterController>().height < 3.1f)
                Fail("Os trolls precisam de porte e vida ampliados.");
        if (!combat.Monsters.Any(enemy => enemy.MaxHealth >= 600 && enemy.GetComponent<CharacterController>().height >= 3.8f))
            Fail("O encontro precisa do troll elite de 600 HP.");
        if (!environment.GetComponentsInChildren<MeshCollider>(true).Any(collider => collider.gameObject.layer == GroundLayer && collider.sharedMesh != null))
            Fail("O piso não tem colisão na camada Terreno.");

        Physics.SyncTransforms();
        if (!Physics.Raycast(new Vector3(0f, 4f, 0f), Vector3.down, out RaycastHit hit, 8f, 1 << GroundLayer))
            Fail("Nenhum piso foi encontrado abaixo do ponto inicial.");
        if (Mathf.Abs(hit.point.y) > 0.15f)
            Fail("O piso central precisa ficar na altura y=0.");
        CharacterController capsule = player.GetComponent<CharacterController>();
        Vector3 lower = player.transform.position + Vector3.up * (capsule.radius + 0.06f);
        Vector3 upper = player.transform.position + Vector3.up * (capsule.height - capsule.radius);
        Collider[] blockers = Physics.OverlapCapsule(lower, upper, capsule.radius * 0.9f, ~(1 << GroundLayer), QueryTriggerInteraction.Ignore);
        if (blockers.Any(collider => !collider.transform.IsChildOf(player.transform)))
            Fail("O ponto inicial do personagem está bloqueado por um objeto do cenário.");

        Debug.Log($"[PROTOTIPO PASS] Cena validada: {environmentMeshes} malhas de cenário, {characterMeshes} malhas de personagem; Idle/Walk/Attack, espada, câmera, entrada, piso, colisões e spawn configurados.");
    }

    [MenuItem("Guerra das Nações/Gerar aplicativo macOS")]
    public static void BuildMacOS()
    {
        PlayerSettings.runInBackground = true;
        Validate();
        PrototypeBranding.Configure();
        string projectDirectory = Directory.GetParent(Application.dataPath).FullName;
        string notices = Path.GetFullPath(Path.Combine(projectDirectory, "../ThirdPartyNotices.md"));
        if (!File.Exists(notices)) Fail("Créditos dos assets externos ausentes.");
        EnsureDirectory("Assets/StreamingAssets");
        File.Copy(notices, "Assets/StreamingAssets/ThirdPartyNotices.md", true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string outputPath = Path.Combine(projectDirectory, "Builds/macOS/Guerra das Nações.app");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            Fail("Build macOS falhou: " + report.summary.result);
        PrototypeBranding.ConfigureMacOSBundle(outputPath);
        Debug.Log("[PROTOTIPO PASS] Aplicativo macOS gerado em " + outputPath);
    }

    [MenuItem("Guerra das Nações/Renderizar prévia")]
    public static void RenderPreview()
    {
        Validate();
        Camera camera = Camera.main;
        if (camera == null)
            Fail("Câmera principal ausente para renderizar prévia.");
        string projectDirectory = Directory.GetParent(Application.dataPath).FullName;
        string previewPath = Path.GetFullPath(Path.Combine(projectDirectory, "../PreviewUnity.png"));
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture texture = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(previewPath, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(texture);
            UnityEngine.Object.DestroyImmediate(image);
        }
        Debug.Log("[PROTOTIPO PASS] Prévia da câmera exportada em " + previewPath);
    }

    private static void ConfigureProject()
    {
        PlayerSettings.companyName = "Guerra das Nações";
        PlayerSettings.productName = "Guerra das Nações";
        PlayerSettings.bundleVersion = "0.16.0";
        PlayerSettings.runInBackground = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.guerradasnacoes.prototipo");
        PlayerSettings.defaultScreenWidth = 1440;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        QualitySettings.shadowDistance = 45f;
        QualitySettings.antiAliasing = 4;
        QualitySettings.vSyncCount = 1;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowCascades = 4;
        QualitySettings.pixelLightCount = 4;

        UnityEngine.Object settingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (settingsAsset == null)
            throw new BuildFailedException("Não foi possível localizar PlayerSettings para configurar a entrada por teclado.");
        SerializedObject settings = new SerializedObject(settingsAsset);
        SerializedProperty activeInput = settings.FindProperty("activeInputHandler");
        if (activeInput == null)
            throw new BuildFailedException("A versão do editor não expõe activeInputHandler em PlayerSettings.");
        activeInput.intValue = 0;
        settings.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.Object tagsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (tagsAsset == null)
            throw new BuildFailedException("TagManager não encontrado.");
        SerializedObject tags = new SerializedObject(tagsAsset);
        tags.FindProperty("layers").GetArrayElementAtIndex(GroundLayer).stringValue = "Terreno";
        tags.FindProperty("layers").GetArrayElementAtIndex(9).stringValue = "Inimigos";
        tags.FindProperty("layers").GetArrayElementAtIndex(10).stringValue = "Jogador";
        tags.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureModel(string path, bool animated, bool rigged = false)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
            throw new BuildFailedException("Modelo não encontrado: " + path);
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = animated;
        importer.isReadable = !animated;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.animationType = animated || rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        if (animated || rigged)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
        }
        if (animated)
        {
            // Keep the densely authored joint curves: reducing keys introduces
            // small pauses and foot drift in this short, tightly planted gait.
            if (path == CharacterPath)
            {
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.resampleCurves = false;
            }
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                bool attack = clip.name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0;
                clip.loopTime = !attack;
                clip.loopPose = !attack;
            }
            importer.clipAnimations = clips;
        }
        importer.SaveAndReimport();
    }

    private static void RemapMaterials(string path)
    {
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
        GameObject model = RequireModel(path);
        string[] names = model.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
            .Select(material => material.name).Distinct().ToArray();
        foreach (string materialName in names)
        {
            if (!MaterialColors.TryGetValue(materialName, out Color color))
                continue;
            Material material = LoadOrCreateMaterial("Assets/Materials/" + materialName + ".mat", "Standard", color);
            ConfigureSurfaceMaterial(material, materialName);
            if (materialName == "MAT_Visor" || materialName == "MAT_GolemCristal")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * (materialName == "MAT_Visor" ? 2.5f : 4f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), materialName), material);
        }
        importer.SaveAndReimport();
    }

    private static void ConfigureSurfaceTextures()
    {
        const string folder = "Assets/Art/Textures";
        foreach (string path in Directory.GetFiles(folder, "*.png"))
        {
            string channel = Path.GetFileNameWithoutExtension(path).Split('_').Last();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) Fail("Não foi possível importar a textura " + path);
            importer.textureType = channel == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = channel == "Albedo";
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = path.Contains("CharacterArmor") ? 2048 : 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureSurfaceMaterial(Material material, string materialName)
    {
        string surface;
        if (materialName == "MAT_Visor" || materialName == "MAT_GolemCristal" || materialName == "MAT_Pele" || materialName == "MAT_CharacterOlho")
            surface = null;
        else if (materialName == "MAT_ArmaduraAzul" || materialName == "MAT_AzulClaro" || materialName == "MAT_Aco" ||
                 materialName == "MAT_Cobre" || materialName == "MAT_MetalEstandarte")
            surface = materialName == "MAT_MetalEstandarte" ? "Metal" : "CharacterArmor";
        else if (materialName == "MAT_CharacterMalha") surface = "CharacterMail";
        else if (materialName == "MAT_Couro") surface = "Leather";
        else if (materialName == "MAT_TecidoEscuro" || materialName.StartsWith("MAT_Nacao", StringComparison.Ordinal)) surface = "Cloth";
        else if (materialName == "MAT_Tronco") surface = "Wood";
        else if (materialName == "MAT_Caminho") surface = "EnvironmentPaving";
        else if (materialName == "MAT_Chao") surface = "EnvironmentEarth";
        else if (materialName.StartsWith("MAT_Golem", StringComparison.Ordinal)) surface = "Monster";
        else if (materialName == "MAT_Musgo" || materialName.StartsWith("MAT_Pinheiro", StringComparison.Ordinal)) surface = "Dirt";
        else surface = "EnvironmentStone";

        bool metal = surface == "Metal" || surface == "CharacterArmor" || surface == "CharacterMail";
        material.SetFloat("_Metallic", metal ? 0.8f : 0f);
        material.SetFloat("_Glossiness", metal ? 0.42f : 0.2f);
        if (surface == null)
        {
            material.SetTexture("_MainTex", null);
            material.SetTexture("_BumpMap", null);
            material.SetTexture("_MetallicGlossMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
            return;
        }
        string prefix = "Assets/Art/Textures/" + surface;
        foreach (string channel in new[] { "Albedo", "Normal", "MetallicSmoothness" })
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_" + channel + ".png") == null)
                Fail("Mapa de superfície ausente: " + prefix + "_" + channel + ".png");
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Albedo.png"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Normal.png"));
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_MetallicSmoothness.png"));
        float repeat = surface == "CharacterMail" ? 8f : metal ? 4f : surface == "Cloth" ? 6f : surface == "Leather" ? 5f : surface == "Wood" ? 1.8f : surface == "EnvironmentEarth" ? 0.6f : 1f;
        material.SetTextureScale("_MainTex", Vector2.one * repeat);
        material.SetTextureScale("_BumpMap", Vector2.one * repeat);
        material.SetTextureScale("_MetallicGlossMap", Vector2.one * repeat);
        material.SetFloat("_BumpScale", surface == "CharacterArmor" ? 0.10f : surface == "CharacterMail" ? 0.35f : metal ? 0.5f : 0.85f);
        material.SetFloat("_GlossMapScale", metal ? 0.85f : 0.9f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.EnableKeyword("_NORMALMAP");
        material.EnableKeyword("_METALLICGLOSSMAP");
    }

    private static Cubemap CreateAmbientReflection()
    {
        const string path = "Assets/Materials/ReflexoAmbiente.asset";
        const int size = 32;
        Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if (cube == null)
        {
            cube = new Cubemap(size, TextureFormat.RGBA32, true) { name = "Céu difuso das ruínas" };
            AssetDatabase.CreateAsset(cube, path);
        }
        Vector3 key = new Vector3(0.4f, 0.7f, -0.5f).normalized;
        for (int face = 0; face < 6; face++)
        {
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                Vector3 direction;
                switch ((CubemapFace)face)
                {
                    case CubemapFace.PositiveX: direction = new Vector3(1f, -v, -u); break;
                    case CubemapFace.NegativeX: direction = new Vector3(-1f, -v, u); break;
                    case CubemapFace.PositiveY: direction = new Vector3(u, 1f, v); break;
                    case CubemapFace.NegativeY: direction = new Vector3(u, -1f, -v); break;
                    case CubemapFace.PositiveZ: direction = new Vector3(u, -v, 1f); break;
                    default: direction = new Vector3(-u, -v, -1f); break;
                }
                direction.Normalize();
                float sky = Mathf.Pow(direction.y * 0.5f + 0.5f, 0.8f);
                Color color = Color.Lerp(new Color(0.10f, 0.11f, 0.12f), new Color(0.43f, 0.47f, 0.53f), sky);
                color += new Color(0.32f, 0.30f, 0.25f) * Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, key)), 18f);
                pixels[y * size + x] = color;
            }
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(true, false);
        EditorUtility.SetDirty(cube);
        return cube;
    }

    private static GameObject RequireModel(string path)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
            throw new BuildFailedException("Não foi possível importar " + path);
        return model;
    }

    [MenuItem("Guerra das Nações/Atualizar animações do personagem")]
    public static void RefreshAnimatorInScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            Fail("Pare o Play antes de atualizar as animações do personagem.");
        Scene scene = SceneManager.GetActiveScene();
        PrototypeCharacter character = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PrototypeCharacter>(true)).FirstOrDefault();
        if (character == null || character.VisualRoot == null)
            Fail("Abra a cena do jogo com o personagem antes de atualizar suas animações.");
        // Existing FBX metadata may list only the older three takes. Refresh the
        // imported takes before rebuilding the controller so Run is included.
        ConfigureModel(CharacterPath, true);
        Animator animator = character.VisualRoot.GetComponentInChildren<Animator>(true);
        if (animator == null)
            Fail("O modelo do personagem está sem Animator.");
        SetupAnimator(animator.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ANIMATION PASS] Animações atualizadas sem recriar a cena. Salve a cena para manter a alteração.");
    }

    private static void SetupAnimator(GameObject hero)
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)).ToArray();
        AnimationClip idle = clips.FirstOrDefault(clip => clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);
        AnimationClip walk = clips.FirstOrDefault(clip => clip.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0);
        AnimationClip run = clips.FirstOrDefault(clip => clip.name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0);
        AnimationClip[] attacks = PrototypeAttackAnimation.ClipNames.Select(name =>
            clips.FirstOrDefault(clip => clip.name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                clip.name.EndsWith("|" + name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (idle == null || walk == null || run == null || attacks.Any(clip => clip == null))
            Fail("Clipes Idle, Walk, Run, Attack, Attack2, Attack3 e HeavyAttack não encontrados em Cavaleiro.fbx.");

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        // Keep the controller GUID so existing scenes/prefabs retain their reference.
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
            machine.RemoveAnyStateTransition(transition);
        foreach (ChildAnimatorState child in machine.states)
        {
            BlendTree oldTree = child.state.motion as BlendTree;
            machine.RemoveState(child.state);
            if (oldTree != null && AssetDatabase.GetAssetPath(oldTree) == ControllerPath)
                UnityEngine.Object.DestroyImmediate(oldTree, true);
        }
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("LocomotionSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PrototypeAttackAnimation.VariantParameter, AnimatorControllerParameterType.Float);
        AnimatorState idleState = machine.AddState("Idle");
        machine.defaultState = idleState;
        BlendTree locomotion = new BlendTree
        {
            name = "Locomoção por velocidade real",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "LocomotionSpeed",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(locomotion, controller);
        // Match the source stride after retargeting to this character without
        // changing the capsule's speed or the attack/idle playback.
        TextAsset motionProfileAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(MotionProfilePath);
        if (motionProfileAsset == null)
            Fail("Perfil das animações ausente: " + MotionProfilePath);
        KnightMotionProfile motionProfile = JsonUtility.FromJson<KnightMotionProfile>(motionProfileAsset.text);
        if (motionProfile == null || motionProfile.origin != "Mixamo" ||
            !ValidCycleDistance(motionProfile.walk_cycle_m) || !ValidCycleDistance(motionProfile.run_cycle_m))
            Fail("Perfil Mixamo inválido; regenere o cavaleiro com seus arquivos fonte.");
        float walkThreshold = PrototypeCharacter.DefaultWalkSpeed;
        locomotion.children = new[]
        {
            new ChildMotion { motion = idle, threshold = 0f, timeScale = 1f },
            new ChildMotion { motion = walk, threshold = walkThreshold, timeScale = walkThreshold * walk.length / motionProfile.walk_cycle_m },
            new ChildMotion { motion = run, threshold = PrototypeCharacter.DefaultRunSpeed,
                timeScale = PrototypeCharacter.DefaultRunSpeed * run.length / motionProfile.run_cycle_m }
        };
        // One continuous tree preserves gait phase through starts, stops and
        // walk/run blends. Keep the legacy Idle name used by combat/menu resets.
        idleState.motion = locomotion;
        AnimatorState attackState = machine.AddState("Attack");
        // The selector takes discrete values, so each swing uses one captured
        // clip while retaining the existing Attack state and cancellation path.
        BlendTree strikes = new BlendTree
        {
            name = "Golpes de espada e escudo",
            blendType = BlendTreeType.Simple1D,
            blendParameter = PrototypeAttackAnimation.VariantParameter,
            useAutomaticThresholds = false,
            children = attacks.Select((clip, index) => new ChildMotion
            {
                motion = clip, threshold = index,
                timeScale = clip.length / PrototypeCombat.AttackPresentationDuration
            }).ToArray()
        };
        AssetDatabase.AddObjectToAsset(strikes, controller);
        attackState.motion = strikes;
        attackState.speed = 1f;
        AnimatorStateTransition startAttack = machine.AddAnyStateTransition(attackState);
        startAttack.hasExitTime = false;
        startAttack.canTransitionToSelf = false;
        startAttack.hasFixedDuration = true;
        startAttack.duration = 0.05f;
        startAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        AnimatorStateTransition finishIdle = attackState.AddTransition(idleState);
        finishIdle.hasExitTime = true;
        // Let the authored recovery reach the guard before blending out.
        // The short blend then fits before the next 0.65 s basic attack.
        finishIdle.exitTime = 1f;
        finishIdle.hasFixedDuration = true;
        finishIdle.duration = 0.025f;
        Animator animator = hero.GetComponent<Animator>();
        if (animator == null)
            animator = hero.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(controller);
    }

    [Serializable]
    private sealed class KnightMotionProfile
    {
        public string origin;
        public float walk_cycle_m;
        public float run_cycle_m;
    }

    private static bool ValidCycleDistance(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= .25f && value <= 5f;

    private static Material LoadOrCreateMaterial(string path, string shaderName, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new BuildFailedException("Shader interno não encontrado: " + shaderName);
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    private static void Fail(string message)
    {
        Debug.LogError("[PROTOTIPO FAIL] " + message);
        throw new BuildFailedException(message);
    }
}
