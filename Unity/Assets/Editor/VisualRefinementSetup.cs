using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Updates visual assets in the saved level without rebuilding its layout or gameplay data.</summary>
public static class VisualRefinementSetup
{
    private const string ScenePath = "Assets/Scenes/Patio.unity";
    private const string ControllerPath = "Assets/Animations/Personagem.controller";
    private const string KnightPath = "Assets/Art/Cavaleiro.fbx";
    private const string BasePath = "Assets/Art/Equipment/BaseHero.fbx";
    private const string TextureRoot = "Assets/Art/ThirdParty/";
    private static int checks;

    [MenuItem("Guerra das Nações/Aplicar refinamento visual sem recriar cenário")]
    public static void Apply()
    {
        EnsureEditing();
        // Never discard work in an already-open scene. Batch execution uses its saved file.
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new BuildFailedException("Salve as cenas abertas antes de aplicar o refinamento visual.");
        string controllerGuid = AssetDatabase.AssetPathToGUID(ControllerPath);
        if (string.IsNullOrEmpty(controllerGuid))
            throw new BuildFailedException("Controller existente não encontrado: " + ControllerPath);

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ThirdPartyCharacterMaterials.Configure();
        ThirdPartyEnvironmentSetup.ConfigureMaterialsAndAssets();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var layout = scene.GetRootGameObjects().ToDictionary(root => root.GetInstanceID(),
            root => new RootPose(root.transform));
        PrototypeCharacter player = FindPlayer(scene);
        PrototypeSetup.RefreshAnimatorInScene();
        PrototypeEquipmentAppearanceEditor.Configure(player.gameObject);
        ThirdPartyEnvironmentSetup.ConfigureLighting();

        checks = 0;
        Check(AssetDatabase.AssetPathToGUID(ControllerPath) == controllerGuid,
            "A atualização trocou o GUID do controller existente.");
        Check(scene.GetRootGameObjects().Length == layout.Count && scene.GetRootGameObjects().All(root =>
                layout.TryGetValue(root.GetInstanceID(), out RootPose pose) && pose.Matches(root.transform)),
            "A atualização alterou a posição, orientação, escala ou os objetos raiz do cenário.");
        ValidateInScene(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new BuildFailedException("Não foi possível salvar a cena refinada.");
        LocomotionContinuityChecks.Run();
        Debug.Log($"[VISUAL REFINEMENT PASS] {checks} verificações; materiais, rig e animações atualizados; layout e GUID do controller preservados.");
    }

    [MenuItem("Guerra das Nações/Validar refinamento visual")]
    public static void ValidateAssets()
    {
        EnsureEditing();
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new BuildFailedException("Abra a cena salva Patio.unity para validar seu personagem.");
        checks = 0;
        ValidateInScene(scene);
        LocomotionContinuityChecks.Run();
        Debug.Log($"[VISUAL REFINEMENT VALIDATION PASS] {checks} verificações; mão direita, 91 poses, quatro golpes, corpo modular, clipes e texturas PBR.");
    }

    private static void ValidateInScene(Scene scene)
    {
        PrototypeCharacter player = FindPlayer(scene);
        Animator animator = player.VisualRoot.GetComponentInChildren<Animator>(true);
        Check(animator != null && animator.runtimeAnimatorController != null, "Animator/controller do herói ausente.");
        Check(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) == ControllerPath &&
              !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ControllerPath)), "O herói perdeu seu controller persistente.");
        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        foreach (string name in new[] { "Idle", "Walk", "Run" }.Concat(PrototypeAttackAnimation.ClipNames))
            Check(clips.Any(clip => (clip.name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                clip.name.EndsWith("|" + name, StringComparison.OrdinalIgnoreCase)) && clip.length > 0f &&
                AssetDatabase.GetAssetPath(clip) == KnightPath), "Clipe importado ausente no controller: " + name);
        Check(!animator.applyRootMotion, "A animação não deve mover o capsule/controller do herói.");
        Check(animator.parameters.Any(parameter => parameter.name == "MoveSpeed" && parameter.type == AnimatorControllerParameterType.Float) &&
              animator.parameters.Any(parameter => parameter.name == "LocomotionSpeed" && parameter.type == AnimatorControllerParameterType.Float) &&
              animator.parameters.Any(parameter => parameter.name == PrototypeAttackAnimation.VariantParameter &&
                  parameter.type == AnimatorControllerParameterType.Float) &&
              animator.parameters.Any(parameter => parameter.name == "Attack" && parameter.type == AnimatorControllerParameterType.Trigger),
            "Parâmetros de locomoção/ataque incompletos.");
        PrototypeEquipmentAppearanceEditor.Validate(player.gameObject);
        Check(true, "Partições e topologia dos equipamentos inválidas.");
        ValidateBaseBindings(player);
        ValidateSwordPoses(player, animator, clips);
        ValidateTexturesAndMaterials();
    }

    private static void ValidateBaseBindings(PrototypeCharacter player)
    {
        GameObject knight = AssetDatabase.LoadAssetAtPath<GameObject>(KnightPath);
        GameObject body = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        Check(knight != null && body != null, "Faltam os modelos originais do cavaleiro/base anatômica.");
        var knightBones = knight.GetComponentsInChildren<Transform>(true).GroupBy(bone => bone.name)
            .ToDictionary(group => group.Key, group => group.First());
        SkinnedMeshRenderer[] sources = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer[] targets = player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer => renderer.name.StartsWith("BaseHero", StringComparison.Ordinal)).ToArray();
        Check(targets.Length >= 11, "O corpo vestido, a cabeça, as regiões e os acessórios não foram preparados.");
        Texture knightAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "Knight/Knight_Albedo.png");
        foreach (SkinnedMeshRenderer target in targets)
        {
            string sourceName = target.name.StartsWith("BaseHero_", StringComparison.Ordinal) ? "BaseHeroBody" : target.name;
            SkinnedMeshRenderer source = sources.FirstOrDefault(renderer => renderer.name == sourceName);
            Check(source != null && target.sharedMesh != null && target.sharedMesh.vertexCount > 0,
                "Região anatômica sem geometria original: " + target.name);
            Matrix4x4[] sourceBinds = source.sharedMesh.bindposes;
            Matrix4x4[] targetBinds = target.sharedMesh.bindposes;
            Check(target.bones.Length == source.bones.Length && targetBinds.Length == sourceBinds.Length &&
                  target.bones.Length == targetBinds.Length, "Quantidade de ossos/bindposes diferente: " + target.name);
            bool bindingsMatch = true;
            for (int i = 0; i < target.bones.Length; i++)
            {
                Transform bone = target.bones[i];
                bindingsMatch &= bone != null && source.bones[i] != null && bone.name == source.bones[i].name &&
                    bone.IsChildOf(player.VisualRoot) && MatrixClose(targetBinds[i], sourceBinds[i], .001f);
            }
            Check(bindingsMatch, "Os ossos ou bindposes do corpo foram reordenados: " + target.name);
            Check(target.sharedMaterials.All(material => material != null && material.name != "MAT_Knight" &&
                    material.GetTexture("_MainTex") != knightAlbedo),
                "A base anatômica está reutilizando a textura de armadura: " + target.name);
        }
        // Compare bind-space joints of both imported models, before animation can hide a mismatch.
        foreach (Transform bone in sources.SelectMany(renderer => renderer.bones).Distinct())
        {
            Check(bone != null && knightBones.TryGetValue(bone.name, out _), "Osso da base inexistente no cavaleiro.");
            Matrix4x4 baseRest = body.transform.worldToLocalMatrix * bone.localToWorldMatrix;
            Matrix4x4 knightRest = knight.transform.worldToLocalMatrix * knightBones[bone.name].localToWorldMatrix;
            Check(MatrixClose(baseRest, knightRest, .01f), "Repouso da base difere do rig do cavaleiro: " + bone.name);
        }
    }

    private static void ValidateSwordPoses(PrototypeCharacter player, Animator original, AnimationClip[] clips)
    {
        Check(!AnimationMode.InAnimationMode(), "Encerre a prévia de animação antes de validar as poses.");
        GameObject hero = UnityEngine.Object.Instantiate(original.gameObject);
        hero.name = "Conferência temporária de rig";
        hero.hideFlags = HideFlags.HideAndDontSave;
        hero.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
        hero.transform.localScale = original.transform.lossyScale;
        Animator animator = hero.GetComponent<Animator>();
        animator.enabled = false;
        Transform[] transforms = hero.GetComponentsInChildren<Transform>(true);
        Transform handRight = transforms.FirstOrDefault(value => value.name == "Hand.R");
        Transform handLeft = transforms.FirstOrDefault(value => value.name == "Hand.L");
        Transform bladeBase = transforms.FirstOrDefault(value => value.name == "SwordBase");
        Transform tip = transforms.FirstOrDefault(value => value.name == "SwordTip");
        Transform grip = transforms.FirstOrDefault(value => value.name == "SwordGrip");
        SkinnedMeshRenderer sword = hero.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(value => value.name == "KnightSword");
        Mesh baked = new Mesh();
        try
        {
            Check(handRight != null && handLeft != null && bladeBase != null && tip != null && sword != null,
                "Rig sem mãos, espada ou marcadores do rastro.");
            Check(grip != null, "Rig sem marcador SwordGrip no centro da empunhadura.");
            AnimationMode.StartAnimationMode();
            Sample(hero, clips.First(clip => Contains(clip.name, "Idle")), 0f);
            float side = Vector3.Dot(handRight.position - handLeft.position, player.transform.right);
            Check(side > 0f, "Hand.R está do lado anatômico esquerdo do personagem: " + side.ToString("F3") + " m.");
            Check(Vector3.Distance(bladeBase.position, handRight.position) < Vector3.Distance(bladeBase.position, handLeft.position),
                "A base da espada está mais perto da mão esquerda.");
            Vector3 idleRight = handRight.position, idleLeft = handLeft.position, idleTip = tip.position;
            foreach (string kind in new[] { "Idle", "Walk", "Run" })
            {
                AnimationClip clip = clips.First(value => Contains(value.name, kind));
                for (int frame = 0; frame <= 12; frame++)
                {
                    Sample(hero, clip, clip.length * frame / 12f);
                    Check(Mathf.Abs(Vector3.Distance(tip.position, bladeBase.position) - 1.025f) <= .005f,
                        "A lâmina mudou de comprimento em " + kind + ", pose " + frame + ".");
                    sword.BakeMesh(baked, true);
                    float distance = baked.vertices.Min(vertex => Vector3.Distance(sword.transform.TransformPoint(vertex), tip.position));
                    Check(distance <= .03f, "O rastro está afastado da ponta real em " + kind + ", pose " + frame + ": " + distance.ToString("F4") + " m.");
                    float gripDistance = Vector3.Distance(grip.position, handRight.position);
                    Check(gripDistance >= .055f && gripDistance <= .12f,
                        "A empunhadura está afastada da palma em " + kind + ", pose " + frame + ": " + gripDistance.ToString("F4") + " m.");
                }
            }
            foreach (string attackName in PrototypeAttackAnimation.ClipNames)
            {
                AnimationClip attack = clips.First(clip => clip.name.Equals(attackName, StringComparison.OrdinalIgnoreCase) ||
                    clip.name.EndsWith("|" + attackName, StringComparison.OrdinalIgnoreCase));
                Vector3 firstTip = Vector3.zero;
                float travel = 0f;
                for (int frame = 0; frame <= 12; frame++)
                {
                    Sample(hero, attack, attack.length * frame / 12f);
                    if (frame == 0 || frame == 12)
                    {
                        Check(Vector3.Distance(handRight.position, idleRight) < .015f,
                            "O punho direito salta entre a guarda e a extremidade do ataque.");
                        Check(Vector3.Distance(handLeft.position, idleLeft) < .015f,
                            "O escudo salta entre a guarda e a extremidade do ataque.");
                        Check(Vector3.Distance(tip.position, idleTip) < .015f,
                            "A lâmina salta entre a guarda e a extremidade do ataque.");
                    }
                    if (frame == 0) firstTip = tip.position;
                    travel = Mathf.Max(travel, Vector3.Distance(firstTip, tip.position));
                    Check(Mathf.Abs(Vector3.Distance(tip.position, bladeBase.position) - 1.025f) <= .005f,
                        "A lâmina mudou de comprimento na pose " + frame + ".");
                    sword.BakeMesh(baked, true);
                    float distance = baked.vertices.Min(vertex => Vector3.Distance(sword.transform.TransformPoint(vertex), tip.position));
                    Check(distance <= .03f, "O rastro está afastado da ponta real na pose " + frame + ": " + distance.ToString("F4") + " m.");
                    float gripDistance = Vector3.Distance(grip.position, handRight.position);
                    Check(gripDistance >= .055f && gripDistance <= .12f,
                        "A empunhadura está afastada da palma no ataque, pose " + frame + ": " + gripDistance.ToString("F4") + " m.");
                }
                Check(travel >= .3f, "O ataque não movimenta a lâmina o suficiente: " + travel.ToString("F3") + " m.");
                Debug.Log($"[VISUAL RIG ATTACK] {attackName}: deslocamento da ponta={travel:F3} m; guarda e empunhadura preservadas.");
            }
            Debug.Log($"[VISUAL RIG] mão direita={side:F3} m; 91 poses em Idle/Walk/Run e quatro golpes; empunhadura alinhada.");
        }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            UnityEngine.Object.DestroyImmediate(baked);
            UnityEngine.Object.DestroyImmediate(hero);
        }
    }

    private static void ValidateTexturesAndMaterials()
    {
        foreach (string folder in new[] { "Knight", "Troll", "Environment", "BaseHero" })
        foreach (string path in Directory.GetFiles(TextureRoot + folder, "*.png", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            bool albedo = name.EndsWith("Albedo", StringComparison.Ordinal);
            bool normal = name.EndsWith("Normal", StringComparison.Ordinal);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(importer != null && importer.sRGBTexture == albedo &&
                  importer.textureType == (normal ? TextureImporterType.NormalMap : TextureImporterType.Default),
                "Tipo/espaço de cor da textura PBR incorreto: " + path);
            Check(importer.mipmapEnabled && importer.filterMode == FilterMode.Trilinear && importer.anisoLevel >= 4 &&
                  importer.maxTextureSize <= 2048 && !importer.isReadable &&
                  importer.textureCompression == TextureImporterCompression.CompressedHQ,
                "Textura sem filtragem/compressão ou com cópia de CPU desnecessária: " + path);
            Check(!name.EndsWith("MetallicSmoothness", StringComparison.Ordinal) ||
                  (!importer.alphaIsTransparency && importer.alphaSource == TextureImporterAlphaSource.FromInput),
                "O alfa de smoothness foi tratado como transparência: " + path);
            if (folder != "Environment")
                Check(importer.wrapMode == TextureWrapMode.Clamp && (!albedo || importer.alphaIsTransparency || folder == "BaseHero"),
                    "Atlas do personagem sem tratamento de borda: " + path);
        }
        TextureImporter cloth = (TextureImporter)AssetImporter.GetAtPath(TextureRoot + "Troll/TrollCloth_Albedo.png");
        Check(cloth.mipMapsPreserveCoverage && Mathf.Abs(cloth.alphaTestReferenceValue - .5f) < .001f,
            "O recorte do tecido não preserva cobertura nos mipmaps.");
        Material clothMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MAT_TrollCloth.mat");
        Check(clothMaterial != null && clothMaterial.IsKeywordEnabled("_ALPHATEST_ON") && clothMaterial.GetFloat("_Mode") == 1f,
            "O tecido do troll deixou de usar o recorte de alfa.");
        Material eyes = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MAT_TrollEyes.mat");
        Texture2D eyeAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "Troll/TrollEyes_Albedo.png");
        Check(eyes != null && eyeAtlas != null && eyes.GetTexture("_MainTex") == eyeAtlas,
            "Os olhos do troll perderam o atlas original.");
        Material skin = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Equipment/MAT_BaseSkin.mat");
        Texture2D skinAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "BaseHero/BaseSkin_Albedo.png");
        Check(skin != null && skinAlbedo != null && skin.GetTexture("_MainTex") == skinAlbedo &&
              skin.GetTexture("_BumpMap") == null && !skin.IsKeywordEnabled("_NORMALMAP") &&
              skin.GetFloat("_Metallic") == 0f && skin.color == Color.white,
            "A pele da cabeça/mãos perdeu os mapas MakeHuman ou recebeu um tint que apaga o atlas.");
        foreach (string path in new[] { "Assets/Materials/MAT_Knight.mat" }.Concat(
            new[] { "MuroEscaneado", "PisoEscaneado", "TerraEscaneada", "RochaEscaneada" }
                .Select(name => "Assets/Materials/ThirdPartyEnvironment/" + name + ".mat")))
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Check(material != null && material.shader.name == "Standard" && material.GetTexture("_BumpMap") != null &&
                  material.GetTexture("_MetallicGlossMap") != null && material.IsKeywordEnabled("_NORMALMAP") &&
                  material.IsKeywordEnabled("_METALLICGLOSSMAP") && material.GetFloat("_SmoothnessTextureChannel") == 0f &&
                  !material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"),
                "Material perdeu normal/metallic/smoothness do mapa PBR: " + path);
        }
    }

    private static PrototypeCharacter FindPlayer(Scene scene)
    {
        PrototypeCharacter player = scene.GetRootGameObjects().SelectMany(root =>
            root.GetComponentsInChildren<PrototypeCharacter>(true)).FirstOrDefault();
        if (player == null || player.VisualRoot == null)
            throw new BuildFailedException("Jogador/visual ausente na cena salva.");
        return player;
    }

    private static void EnsureEditing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new BuildFailedException("Pare o Play antes de refinar ou validar os visuais.");
    }

    private static bool Contains(string value, string part) => value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new BuildFailedException(message);
        checks++;
    }

    private static bool MatrixClose(Matrix4x4 first, Matrix4x4 second, float tolerance)
    {
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
            if (Mathf.Abs(first[row, column] - second[row, column]) > tolerance) return false;
        return true;
    }

    private static void Sample(GameObject hero, AnimationClip clip, float time)
    {
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(hero, clip, time);
        AnimationMode.EndSampling();
    }

    private readonly struct RootPose
    {
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;
        public RootPose(Transform root) { position = root.position; rotation = root.rotation; scale = root.localScale; }
        public bool Matches(Transform root) => Vector3.Distance(position, root.position) < .00001f &&
            Mathf.Abs(Quaternion.Dot(rotation, root.rotation)) > .999999f && Vector3.Distance(scale, root.localScale) < .00001f;
    }
}
