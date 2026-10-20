using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Fixed-step recording of the real locomotion/combat controller from selectable views.</summary>
public static class LocomotionMotionPreview
{
    private const string SourceScenePath = "Assets/Scenes/Patio.unity";
    private const int FrameRate = 60;
    private const int FrameCount = 360;
    private const int Width = 480;
    private const int Height = 600;
    private const float Step = 1f / FrameRate;
    private const float Damping = .06f;
    private const int PreviewLayer = 11;
    private static readonly int[] AttackTriggerFrames = { 60, 99, 138, 177, 216, 255 };
    private static readonly int[] AttackVariants = { 0, 1, 2, 0, 1, 3 };

    [MenuItem("Guerra das Nações/Capturar vídeo do cavaleiro em quadros a 60 fps")]
    public static void Capture()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Encerre o PlayMode antes de capturar.");
        Require(!AnimationMode.InAnimationMode(), "Encerre a prévia de animação antes de capturar.");
        bool emptyStartup = true;
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            Require(!scene.isDirty, "Salve as alterações da cena '" + scene.name + "' antes de capturar; nenhuma cena foi descartada.");
            bool emptyUntitled = string.IsNullOrEmpty(scene.path) && scene.rootCount == 0;
            Require(!string.IsNullOrEmpty(scene.path) || emptyUntitled,
                "Salve a cena sem caminho '" + scene.name + "' antes de capturar; nenhuma cena foi descartada.");
            emptyStartup &= emptyUntitled;
        }
        string destination = Environment.GetEnvironmentVariable("GN_MOTION_CAPTURE_DIR");
        Require(!string.IsNullOrWhiteSpace(destination), "Defina GN_MOTION_CAPTURE_DIR para a pasta dos quadros.");
        string view = ReadOption("GN_MOTION_CAPTURE_VIEW", "threequarter", "front", "side", "threequarter");
        string mode = ReadOption("GN_MOTION_CAPTURE_MODE", "locomotion", "combat", "locomotion");
        bool combat = mode == "combat";
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);

        Scene previousActive = SceneManager.GetActiveScene();
        Scene sourceScene = SceneManager.GetSceneByPath(SourceScenePath);
        bool openedSource = false;
        Scene preview = default;
        RenderTexture previousTexture = RenderTexture.active;
        RenderTexture target = null;
        Texture2D image = null;
        Camera camera = null;
        var snapshots = new List<SkinSnapshot>();
        var materials = new List<Material>();
        try
        {
            // Batch mode starts with an empty Untitled scene. Unity refuses NewScene(Additive)
            // while it remains loaded, even when it is clean. Only that empty startup can be
            // replaced; existing saved scenes are kept, and dirty/meaningful untitled scenes
            // were explicitly rejected before any scene operation.
            if (!sourceScene.IsValid() || !sourceScene.isLoaded)
            {
                sourceScene = EditorSceneManager.OpenScene(SourceScenePath,
                    emptyStartup ? OpenSceneMode.Single : OpenSceneMode.Additive);
                openedSource = !emptyStartup;
                if (emptyStartup) previousActive = sourceScene;
            }
            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene == sourceScene || !string.IsNullOrEmpty(scene.path) || scene.isDirty || scene.rootCount != 0) continue;
                Require(EditorSceneManager.CloseScene(scene, true), "Não foi possível fechar a cena inicial vazia.");
            }
            PrototypeCharacter player = sourceScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PrototypeCharacter>(true)).SingleOrDefault();
            Require(player != null && player.VisualRoot != null, "O cavaleiro está sem raiz visual.");

            // A normal additive scene gives the recording its own RenderSettings as well as
            // its own camera/lights. It is discarded in finally and is never saved to disk.
            preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            // NewScene can already make the scene active. SetActiveScene's return value
            // is not an activation test in that case; verify the resulting scene instead.
            if (SceneManager.GetActiveScene() != preview) SceneManager.SetActiveScene(preview);
            Scene captureActive = SceneManager.GetActiveScene();
            Require(captureActive == preview,
                "Não foi possível ativar a cena temporária (valid=" + preview.IsValid() +
                ", loaded=" + preview.isLoaded + ", active='" + captureActive.name +
                "', preview='" + preview.name + "').");
            GameObject hero = Object.Instantiate(player.VisualRoot.gameObject);
            hero.name = "Cavaleiro temporário · controller real";
            SceneManager.MoveGameObjectToScene(hero, preview);
            hero.transform.SetPositionAndRotation(
                Vector3.up * (player.VisualRoot.position.y - player.transform.position.y),
                player.VisualRoot.rotation);
            hero.transform.localScale = player.VisualRoot.lossyScale;
            hero.SetActive(true);
            foreach (Transform node in hero.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = PreviewLayer;
            foreach (MonoBehaviour behaviour in hero.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (Collider collider in hero.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (Rigidbody body in hero.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(body);
            foreach (AudioSource audio in hero.GetComponentsInChildren<AudioSource>(true))
                Object.DestroyImmediate(audio);

            Animator animator = hero.GetComponentInChildren<Animator>(true);
            Require(animator != null && animator.runtimeAnimatorController != null, "O cavaleiro está sem controller.");
            Require(HasFloat(animator, "LocomotionSpeed") && HasFloat(animator, "MoveSpeed"),
                "O controller precisa dos parâmetros LocomotionSpeed e MoveSpeed.");
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            ResetAnimator(animator, combat);
            var attackAnimation = new PrototypeAttackAnimation(animator);
            CreateSnapshots(hero.transform, snapshots);
            BakeSnapshots(snapshots);

            Bounds bounds = CaptureWorldBounds(snapshots);
            Bounds framingBounds = bounds;
            var framingPoints = new List<Vector3>();
            CaptureFramingPoints(snapshots, framingPoints);
            // Accumulate the same controller-driven sequence before recording in
            // either mode. Running also extends the blade beyond the initial guard.
            // The recording then uses a fixed camera that contains the whole sequence.
            for (int frame = 0; frame < FrameCount; frame++)
            {
                AdvanceAnimator(animator, attackAnimation, frame, combat, out _);
                BakeSnapshots(snapshots);
                framingBounds.Encapsulate(CaptureWorldBounds(snapshots));
                CaptureFramingPoints(snapshots, framingPoints);
            }
            ResetAnimator(animator, combat);
            BakeSnapshots(snapshots);
            Vector3 direction = Vector3.ProjectOnPlane(hero.transform.forward, Vector3.up).normalized;
            if (direction.sqrMagnitude < .5f) direction = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            float groundHeight = bounds.min.y - .004f;
            Transform ground = CreateGround(direction, groundHeight, materials);
            CreateLights(direction, right);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.31f, .34f, .39f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = .25f;

            GameObject cameraObject = new GameObject("Câmera temporária de locomoção");
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.cullingMask = 1 << PreviewLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.075f, .09f, .11f);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 90f;
            camera.fieldOfView = 32f;
            camera.aspect = Width / (float)Height;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            FrameCamera(camera, view, direction, right, framingBounds, framingPoints);
            target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;

            var report = new CaptureReport
            {
                controller = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),
                mode = mode,
                view = view,
                frameRate = FrameRate,
                frameCount = FrameCount,
                durationSeconds = FrameCount * Step,
                width = Width,
                height = Height,
                dampingSeconds = Damping,
                activeSkins = snapshots.Count,
                attackTriggerFrames = combat ? (int[])AttackTriggerFrames.Clone() : Array.Empty<int>(),
                attackTriggerVariants = combat ? (int[])AttackVariants.Clone() : Array.Empty<int>(),
                framingBoundsMin = framingBounds.min,
                framingBoundsMax = framingBounds.max,
                cameraPosition = camera.transform.position,
                frames = new FrameReport[FrameCount]
            };
            float distance = 0f;
            float previousSpeed = 0f;
            for (int frame = 0; frame < FrameCount; frame++)
            {
                float time = (frame + 1) * Step;
                bool attackTriggered = AdvanceAnimator(animator, attackAnimation, frame, combat, out float speed);
                distance += (previousSpeed + speed) * .5f * Step;
                previousSpeed = speed;
                // This is the following-camera reference frame: the hero stays centred,
                // while the 2 m world grid travels backwards by the actual travelled distance.
                ground.position = Vector3.up * groundHeight - direction * distance;
                BakeSnapshots(snapshots);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply(false, false);
                string filename = "Frame" + frame.ToString("D4") + ".png";
                File.WriteAllBytes(Path.Combine(destination, filename), image.EncodeToPNG());
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                AnimatorClipInfo dominant = animator.GetCurrentAnimatorClipInfo(0)
                    .OrderByDescending(clip => clip.weight).FirstOrDefault();
                report.frames[frame] = new FrameReport
                {
                    frame = frame,
                    file = filename,
                    timeSeconds = time,
                    requestedSpeedMetresPerSecond = speed,
                    locomotionSpeed = animator.GetFloat("LocomotionSpeed"),
                    moveSpeed = animator.GetFloat("MoveSpeed"),
                    travelledMetres = distance,
                    normalizedStateTime = state.normalizedTime,
                    stateHash = state.shortNameHash,
                    inTransition = animator.IsInTransition(0),
                    attackTriggered = attackTriggered,
                    attackVariant = combat ? Mathf.RoundToInt(animator.GetFloat(PrototypeAttackAnimation.VariantParameter)) : -1,
                    dominantClip = dominant.clip != null ? dominant.clip.name : "",
                    dominantClipWeight = dominant.weight,
                    inAttack = state.IsName("Attack")
                };
            }
            File.WriteAllText(Path.Combine(destination, "motion.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[LOCOMOTION MOTION PREVIEW PASS] " + FrameCount + " quadros PNG a " + FrameRate +
                " fps, controller real, modo=" + mode + ", vista=" + view + ". " + destination);
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = previousTexture;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (image != null) Object.DestroyImmediate(image);
            foreach (SkinSnapshot snapshot in snapshots)
            {
                if (snapshot.source != null) snapshot.source.enabled = snapshot.wasEnabled;
                if (snapshot.renderer != null) Object.DestroyImmediate(snapshot.renderer.gameObject);
                if (snapshot.mesh != null) Object.DestroyImmediate(snapshot.mesh);
            }
            foreach (Material material in materials) if (material != null) Object.DestroyImmediate(material);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.CloseScene(preview, true);
            if (openedSource && sourceScene.IsValid() && sourceScene.isLoaded)
                EditorSceneManager.CloseScene(sourceScene, true);
        }
    }

    private static float SpeedAt(float time)
    {
        const float walk = PrototypeCharacter.DefaultWalkSpeed;
        const float run = PrototypeCharacter.DefaultRunSpeed;
        if (time < .35f) return Mathf.Lerp(0f, walk, time / .35f);
        if (time < 2.4f) return walk;
        if (time < 2.8f) return Mathf.Lerp(walk, run, (time - 2.4f) / .4f);
        if (time < 4.5f) return run;
        if (time < 4.85f) return Mathf.Lerp(run, 0f, (time - 4.5f) / .35f);
        return 0f;
    }

    private static string ReadOption(string variable, string fallback, params string[] allowed)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        Require(Array.IndexOf(allowed, value) >= 0,
            variable + " deve ser " + string.Join(" | ", allowed) + ".");
        return value;
    }

    private static void ResetAnimator(Animator animator, bool combat)
    {
        animator.Rebind();
        Require(animator.HasState(0, Animator.StringToHash("Base Layer.Idle")), "Estado Idle ausente no controller.");
        if (combat)
            Require(animator.parameters.Any(parameter => parameter.name == "Attack" &&
                parameter.type == AnimatorControllerParameterType.Trigger) &&
                animator.HasState(0, Animator.StringToHash("Base Layer.Attack")) &&
                HasFloat(animator, PrototypeAttackAnimation.VariantParameter),
                "O controller precisa do trigger, estado Attack e seletor AttackVariant para a prévia de combate.");
        animator.SetFloat("LocomotionSpeed", 0f);
        animator.SetFloat("MoveSpeed", 0f);
        if (combat)
        {
            animator.ResetTrigger("Attack");
            animator.SetFloat(PrototypeAttackAnimation.VariantParameter, 0f);
        }
        animator.Play("Idle", 0, 0f);
        animator.Update(0f);
    }

    private static bool AdvanceAnimator(Animator animator, PrototypeAttackAnimation attackAnimation,
        int frame, bool combat, out float speed)
    {
        speed = combat ? 0f : SpeedAt((frame + 1) * Step);
        animator.SetFloat("LocomotionSpeed", speed, Damping, Step);
        animator.SetFloat("MoveSpeed", speed / PrototypeCharacter.DefaultWalkSpeed, Damping, Step);
        int trigger = combat ? Array.IndexOf(AttackTriggerFrames, frame) : -1;
        bool attack = trigger >= 0;
        if (attack) attackAnimation.PlayVariant(AttackVariants[trigger]);
        animator.Update(Step);
        return attack;
    }

    private static void FrameCamera(Camera camera, string view,
        Vector3 forward, Vector3 right, Bounds bounds, List<Vector3> framingPoints)
    {
        Vector3 offset = view == "front" ? forward * 4.235f : view == "side" ? right * 4.235f :
            forward * 3.65f + right * 2.15f;
        offset += Vector3.up * .48f;
        // Fit every corner of the accumulated CPU bounds in both dimensions,
        // accounting for camera pitch and the portrait aspect ratio.
        Vector3 focus = bounds.center;
        Vector3 back = offset.normalized;
        Quaternion rotation = Quaternion.LookRotation(-back);
        Vector3 cameraRight = rotation * Vector3.right;
        Vector3 cameraUp = rotation * Vector3.up;
        float verticalTangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
        float horizontalTangent = verticalTangent * camera.aspect;
        float distance = offset.magnitude;
        // Fit each skin in each sampled pose. Corners of the combined world AABB
        // mix extremes from different poses and unnecessarily shrink the knight.
        foreach (Vector3 worldPoint in framingPoints)
        {
            Vector3 point = worldPoint - focus;
            float projectedDepth = Vector3.Dot(point, back);
            float verticalDistance = (Mathf.Abs(Vector3.Dot(point, cameraUp)) + .08f) / verticalTangent;
            float horizontalDistance = (Mathf.Abs(Vector3.Dot(point, cameraRight)) + .08f) / horizontalTangent;
            distance = Mathf.Max(distance, projectedDepth + Mathf.Max(verticalDistance, horizontalDistance));
        }
        offset = back * distance * 1.07f;
        camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset));
    }

    private static bool HasFloat(Animator animator, string name) => animator.parameters.Any(parameter =>
        parameter.name == name && parameter.type == AnimatorControllerParameterType.Float);

    private static void CreateSnapshots(Transform root, List<SkinSnapshot> snapshots)
    {
        foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
            var snapshot = new SkinSnapshot { source = skin, wasEnabled = skin.enabled, mesh = new Mesh() };
            snapshots.Add(snapshot);
            GameObject child = new GameObject("Pose CPU · " + skin.name) { layer = PreviewLayer };
            child.transform.SetParent(skin.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = snapshot.mesh;
            snapshot.renderer = child.AddComponent<MeshRenderer>();
            snapshot.renderer.sharedMaterials = skin.sharedMaterials;
            snapshot.renderer.shadowCastingMode = skin.shadowCastingMode;
            snapshot.renderer.receiveShadows = skin.receiveShadows;
            var properties = new MaterialPropertyBlock();
            skin.GetPropertyBlock(properties);
            snapshot.renderer.SetPropertyBlock(properties);
            for (int index = 0; index < skin.sharedMaterials.Length; index++)
            {
                skin.GetPropertyBlock(properties, index);
                snapshot.renderer.SetPropertyBlock(properties, index);
            }
            skin.enabled = false;
        }
        Require(snapshots.Count > 0, "Não há skins ativas no cavaleiro clonado.");
    }

    private static void BakeSnapshots(List<SkinSnapshot> snapshots)
    {
        // Match EquipmentVisualPreviewChecks.Save: CPU skinning avoids stale GPU poses
        // in synchronous editor renders. Reuse the same objects and meshes for all 360 frames.
        foreach (SkinSnapshot snapshot in snapshots) snapshot.source.BakeMesh(snapshot.mesh, true);
    }

    private static Bounds CaptureWorldBounds(List<SkinSnapshot> snapshots)
    {
        // Renderer.bounds may still contain the pre-bake GPU bounds in this synchronous
        // editor path. Derive the framing and ground plane from the actual CPU geometry.
        Bounds combined = default;
        bool initialized = false;
        foreach (SkinSnapshot snapshot in snapshots)
        {
            snapshot.mesh.RecalculateBounds();
            Bounds local = snapshot.mesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = BoundsCorner(local, corner);
                point = snapshot.renderer.transform.TransformPoint(point);
                if (!initialized)
                {
                    combined = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else combined.Encapsulate(point);
            }
        }
        Require(initialized && combined.size.y > .1f, "As malhas CPU não fornecem limites válidos para enquadrar o cavaleiro.");
        return combined;
    }

    private static Vector3 BoundsCorner(Bounds bounds, int corner) => new Vector3(
        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);

    private static void CaptureFramingPoints(List<SkinSnapshot> snapshots, List<Vector3> points)
    {
        foreach (SkinSnapshot snapshot in snapshots)
            for (int corner = 0; corner < 8; corner++)
                points.Add(snapshot.renderer.transform.TransformPoint(BoundsCorner(snapshot.mesh.bounds, corner)));
    }

    private static Transform CreateGround(Vector3 direction, float height, List<Material> materials)
    {
        Shader shader = Shader.Find("Standard");
        Require(shader != null, "Shader Standard indisponível para o chão de referência.");
        Material floor = new Material(shader) { name = "Chão temporário", color = new Color(.14f, .17f, .19f) };
        Material line = new Material(shader) { name = "Grade temporária de 2 m", color = new Color(.30f, .34f, .37f) };
        floor.SetFloat("_Glossiness", .15f);
        line.SetFloat("_Glossiness", .15f);
        materials.Add(floor);
        materials.Add(line);
        GameObject rig = new GameObject("Chão móvel · grade de 2 m");
        rig.transform.SetPositionAndRotation(Vector3.up * height, Quaternion.LookRotation(direction));
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "Plano de referência";
        plane.layer = PreviewLayer;
        plane.transform.SetParent(rig.transform, false);
        plane.transform.localScale = new Vector3(8f, 1f, 8f);
        plane.GetComponent<Renderer>().sharedMaterial = floor;
        Object.DestroyImmediate(plane.GetComponent<Collider>());
        for (int offset = -40; offset <= 40; offset += 2)
            for (int axis = 0; axis < 2; axis++)
            {
                GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = "Linha de referência " + axis + " · " + offset + " m";
                stripe.layer = PreviewLayer;
                stripe.transform.SetParent(rig.transform, false);
                stripe.transform.localPosition = axis == 0 ? new Vector3(offset, .002f, 0f) : new Vector3(0f, .002f, offset);
                stripe.transform.localScale = axis == 0 ? new Vector3(.012f, .002f, 80f) : new Vector3(80f, .002f, .012f);
                stripe.GetComponent<Renderer>().sharedMaterial = line;
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
            }
        return rig.transform;
    }

    private static void CreateLights(Vector3 forward, Vector3 right)
    {
        for (int index = 0; index < 2; index++)
        {
            bool key = index == 0;
            GameObject source = new GameObject(key ? "Luz principal temporária" : "Preenchimento temporário");
            source.transform.rotation = Quaternion.LookRotation(-forward * 3f + right * (key ? -2f : 3f) + Vector3.down * 4f);
            Light light = source.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << PreviewLayer;
            light.shadows = LightShadows.None;
            light.intensity = key ? 1.1f : .5f;
            light.color = key ? new Color(1f, .95f, .87f) : new Color(.72f, .83f, 1f);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Prévia de locomoção: " + message);
    }

    private sealed class SkinSnapshot
    {
        public SkinnedMeshRenderer source;
        public bool wasEnabled;
        public Mesh mesh;
        public MeshRenderer renderer;
    }

    [Serializable]
    private sealed class CaptureReport
    {
        public string controller, mode, view;
        public int frameRate, frameCount, width, height, activeSkins;
        public int[] attackTriggerFrames, attackTriggerVariants;
        public float durationSeconds, dampingSeconds;
        public Vector3 framingBoundsMin, framingBoundsMax, cameraPosition;
        public FrameReport[] frames;
    }

    [Serializable]
    private sealed class FrameReport
    {
        public int frame, stateHash, attackVariant;
        public string file, dominantClip;
        public float timeSeconds, requestedSpeedMetresPerSecond, locomotionSpeed, moveSpeed;
        public float travelledMetres, normalizedStateTime, dominantClipWeight;
        public bool inTransition, attackTriggered, inAttack;
    }
}
