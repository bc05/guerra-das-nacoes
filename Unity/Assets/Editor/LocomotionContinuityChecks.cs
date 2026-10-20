using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Measures the imported gait, including motion through the loop seam.</summary>
public static class LocomotionContinuityChecks
{
    private const string ScenePath = "Assets/Scenes/Patio.unity";
    private const string ModelPath = "Assets/Art/Cavaleiro.fbx";
    private const float SampleRate = 120f;
    // Stay well inside the endpoint Hermite segments. At 1/240 s the O(h²)
    // derivative error falsely reports discontinuity on high-curvature mocap.
    private const float SeamStep = 1f / 960f;
    private const float ReferenceStep = 1f / 60f;
    private static readonly string[] BoneNames =
    {
        "Pelvis", "Thigh.R", "Thigh.L", "Shin.R", "Shin.L",
        "Foot.R", "Foot.L", "Hand.R", "Hand.L"
    };

    [MenuItem("Guerra das Nações/Validar continuidade dos ciclos de locomoção")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Encerre o PlayMode antes de medir os clipes.");
        Require(!AnimationMode.InAnimationMode(), "Encerre a prévia de animação antes de medir os clipes.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Salve as cenas abertas antes de validar a locomoção.");

        var report = new Report();
        var failures = new List<string>();
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        bool changedScene = SceneManager.GetActiveScene().path != ScenePath;
        Scene preview = default;
        GameObject hero = null;
        TransformPose[] sourcePose = null, clonePose = null;
        Animator originalAnimator = null;
        bool originalAnimatorEnabled = false;
        try
        {
            Scene scene = changedScene
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : SceneManager.GetActiveScene();
            PrototypeCharacter player = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PrototypeCharacter>(true)).Single();
            Require(player.VisualRoot != null, "O herói está sem raiz visual.");
            originalAnimator = player.VisualRoot.GetComponentInChildren<Animator>(true);
            Require(originalAnimator != null && originalAnimator.runtimeAnimatorController != null,
                "O herói está sem Animator/controller.");
            originalAnimatorEnabled = originalAnimator.enabled;
            sourcePose = CaptureTransforms(originalAnimator.transform);
            AnimationClip[] clips = originalAnimator.runtimeAnimatorController.animationClips;

            preview = EditorSceneManager.NewPreviewScene();
            hero = UnityEngine.Object.Instantiate(originalAnimator.gameObject);
            hero.name = "Diagnóstico temporário de continuidade da locomoção";
            hero.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(hero, preview);
            // Keep the actual model scale, but avoid subtracting large world coordinates
            // while measuring submillimetre differences at 240 Hz.
            hero.transform.SetPositionAndRotation(Vector3.zero, originalAnimator.transform.rotation);
            hero.transform.localScale = originalAnimator.transform.lossyScale;
            Animator animator = hero.GetComponent<Animator>();
            animator.enabled = false;
            clonePose = CaptureTransforms(hero.transform);
            Transform[] hierarchy = hero.GetComponentsInChildren<Transform>(true);
            Transform[] bones = BoneNames.Select(name => FindBone(hierarchy, name, true)).ToArray();
            Transform rootBone = FindBone(hierarchy, "Root", false);

            AnimationMode.StartAnimationMode();
            foreach (string name in new[] { "Walk", "Run" })
            {
                AnimationClip clip = clips.FirstOrDefault(value =>
                    value.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                Require(clip != null && clip.length > ReferenceStep * 4f, "Clipe ausente ou curto demais: " + name);
                Require(AssetDatabase.GetAssetPath(clip) == ModelPath, "O check deve medir o FBX final: " + name);
                report.clips.Add(MeasureClip(hero, clip, bones, rootBone, failures));
            }
        }
        catch (Exception error)
        {
            failures.Add(error.ToString());
        }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            report.animationModeRestored = !AnimationMode.InAnimationMode();
            report.clonePoseRestored = clonePose != null && TransformsMatch(clonePose);
            report.sourcePosePreserved = sourcePose != null && TransformsMatch(sourcePose) &&
                originalAnimator != null && originalAnimator.enabled == originalAnimatorEnabled;
            if (hero != null) UnityEngine.Object.DestroyImmediate(hero);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (changedScene)
            {
                // A batch invocation may start without any loaded scene. Unity
                // rejects restoring that empty setup; leave an empty scene instead.
                if (previousSetup.Any(value => value.isLoaded && value.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        Check(report.animationModeRestored, failures, "AnimationMode não foi restaurado.");
        Check(report.clonePoseRestored, failures, "A amostragem não restaurou a pose temporária após StopAnimationMode.");
        Check(report.sourcePosePreserved, failures, "A amostragem alterou a pose/Animator do herói original.");
        report.failures = failures.ToArray();
        report.passed = failures.Count == 0;
        string output = Environment.GetEnvironmentVariable("GN_GAIT_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(output))
        {
            string path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("[LOCOMOTION CONTINUITY REPORT] " + path);
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("[LOCOMOTION CONTINUITY FAIL] " + string.Join("\n", failures));
        Debug.Log("[LOCOMOTION CONTINUITY PASS] Walk/Run importados, poses/velocidades/acelerações a120Hz, " +
            "continuidade e retenção de velocidade na emenda; raiz fixa e restauração da prévia verificadas.");
        // Batch callers use -quit. This also leaves the menu command usable in the Editor.
    }

    private static ClipReport MeasureClip(GameObject hero, AnimationClip clip, Transform[] bones,
        Transform rootBone, List<string> failures)
    {
        int intervals = Mathf.CeilToInt(clip.length * SampleRate);
        float step = clip.length / intervals;
        var result = new ClipReport
        {
            clip = clip.name, durationSeconds = clip.length, effectiveSampleRate = 1f / step,
            minAnkleTrackWidthMetres = float.MaxValue,
            hasRootBone = rootBone != null, frames = new FramePose[intervals + 1],
            bones = BoneNames.Select(name => new BoneReport { bone = name }).ToArray()
        };
        Vector3 rootPosition = rootBone != null ? rootBone.localPosition : Vector3.zero;
        Quaternion rootRotation = rootBone != null ? rootBone.localRotation : Quaternion.identity;
        Vector3 objectPosition = hero.transform.position;
        Quaternion objectRotation = hero.transform.rotation;
        for (int frame = 0; frame <= intervals; frame++)
        {
            Pose[] poses = Sample(hero, clip, step * frame, bones);
            result.frames[frame] = new FramePose { timeSeconds = step * frame, bones = poses };
            float ankleWidth = Vector3.Dot(poses[5].position - poses[6].position, hero.transform.right);
            float kneeWidth = Vector3.Dot(poses[3].position - poses[4].position, hero.transform.right);
            result.minAnkleTrackWidthMetres = Mathf.Min(result.minAnkleTrackWidthMetres, ankleWidth);
            result.maxAnkleTrackWidthMetres = Mathf.Max(result.maxAnkleTrackWidthMetres, ankleWidth);
            result.maxKneeTrackWidthMetres = Mathf.Max(result.maxKneeTrackWidthMetres, kneeWidth);
            result.maxObjectTranslationMetres = Mathf.Max(result.maxObjectTranslationMetres,
                Vector3.Distance(hero.transform.position, objectPosition));
            result.maxObjectRotationDegrees = Mathf.Max(result.maxObjectRotationDegrees,
                Quaternion.Angle(hero.transform.rotation, objectRotation));
            if (rootBone == null) continue;
            result.maxRootTranslationMetres = Mathf.Max(result.maxRootTranslationMetres,
                Vector3.Distance(rootBone.localPosition, rootPosition));
            result.maxRootRotationDegrees = Mathf.Max(result.maxRootRotationDegrees,
                Quaternion.Angle(rootBone.localRotation, rootRotation));
        }
        Check(result.maxObjectTranslationMetres <= .001f && result.maxObjectRotationDegrees <= .1f,
            failures, clip.name + ": o clipe desloca/gira o objeto que pertence ao controlador físico.");
        Check(!result.hasRootBone || (result.maxRootTranslationMetres <= .001f && result.maxRootRotationDegrees <= .1f),
            failures, clip.name + ": Root deixou de ser uma raiz fixa para locomoção in-place.");
        Check(result.minAnkleTrackWidthMetres > .08f && result.maxAnkleTrackWidthMetres < .38f &&
              result.maxKneeTrackWidthMetres < .38f, failures,
            clip.name + ": os pés/joelhos cruzam ou abrem fora das faixas da passada (pés=" +
            result.minAnkleTrackWidthMetres.ToString("F3") + "–" + result.maxAnkleTrackWidthMetres.ToString("F3") +
            "m; joelhos=" + result.maxKneeTrackWidthMetres.ToString("F3") + "m).");

        for (int frame = 0; frame <= intervals; frame++)
        {
            // Use circular neighbours so the acceleration report includes the seam itself.
            Pose[] previous = result.frames[(frame + intervals - 1) % intervals].bones;
            Pose[] current = result.frames[frame].bones;
            Pose[] next = result.frames[(frame + 1) % intervals].bones;
            for (int i = 0; i < bones.Length; i++)
            {
                Pose pose = current[i];
                pose.velocityMetresPerSecond = (next[i].position - previous[i].position) / (2f * step);
                pose.accelerationMetresPerSecondSquared =
                    (next[i].position - 2f * pose.position + previous[i].position) / (step * step);
                pose.angularVelocityDegreesPerSecond = AngularVelocity(previous[i].rotation, next[i].rotation, 2f * step);
                pose.angularAccelerationDegreesPerSecondSquared =
                    (AngularVelocity(pose.rotation, next[i].rotation, step) -
                     AngularVelocity(previous[i].rotation, pose.rotation, step)) / step;
                BoneReport metrics = result.bones[i];
                metrics.peakSpeedMetresPerSecond = Mathf.Max(metrics.peakSpeedMetresPerSecond,
                    pose.velocityMetresPerSecond.magnitude);
                float acceleration = pose.accelerationMetresPerSecondSquared.magnitude;
                if (acceleration > metrics.peakAccelerationMetresPerSecondSquared)
                {
                    metrics.peakAccelerationMetresPerSecondSquared = acceleration;
                    metrics.peakAccelerationAtSeconds = result.frames[frame].timeSeconds;
                }
                metrics.peakAngularSpeedDegreesPerSecond = Mathf.Max(metrics.peakAngularSpeedDegreesPerSecond,
                    pose.angularVelocityDegreesPerSecond.magnitude);
                metrics.peakAngularAccelerationDegreesPerSecondSquared = Mathf.Max(
                    metrics.peakAngularAccelerationDegreesPerSecondSquared,
                    pose.angularAccelerationDegreesPerSecondSquared.magnitude);
            }
        }

        Pose[] start = Sample(hero, clip, 0f, bones);
        Pose[] after = Sample(hero, clip, SeamStep, bones);
        Pose[] afterTwice = Sample(hero, clip, 2f * SeamStep, bones);
        Pose[] before = Sample(hero, clip, clip.length - SeamStep, bones);
        Pose[] beforeTwice = Sample(hero, clip, clip.length - 2f * SeamStep, bones);
        Pose[] end = Sample(hero, clip, clip.length, bones);
        Pose[] referenceAfter = Sample(hero, clip, ReferenceStep, bones);
        Pose[] referenceBefore = Sample(hero, clip, clip.length - ReferenceStep, bones);
        for (int i = 0; i < bones.Length; i++)
        {
            BoneReport metrics = result.bones[i];
            metrics.seamPoseErrorMetres = Vector3.Distance(start[i].position, end[i].position);
            metrics.seamRotationErrorDegrees = Quaternion.Angle(start[i].rotation, end[i].rotation);
            // Estimate both derivatives at the same boundary. First-order secants
            // compare different times and mistake ordinary acceleration for a seam
            // discontinuity; the second-order formulas cancel that leading error.
            metrics.seamIncomingVelocity =
                (3f * end[i].position - 4f * before[i].position + beforeTwice[i].position) / (2f * SeamStep);
            metrics.seamOutgoingVelocity =
                (-3f * start[i].position + 4f * after[i].position - afterTwice[i].position) / (2f * SeamStep);
            metrics.seamVelocityJumpMetresPerSecond =
                (metrics.seamIncomingVelocity - metrics.seamOutgoingVelocity).magnitude;
            metrics.nearbyReferenceVelocity =
                ((referenceAfter[i].position - start[i].position) +
                 (end[i].position - referenceBefore[i].position)) / (2f * ReferenceStep);
            float referenceSpeed = metrics.nearbyReferenceVelocity.magnitude;
            // Matching endpoint poses do not prove a seamless loop. Nor do two zero
            // endpoint derivatives: AUTO_CLAMPED handles can stop both sides equally.
            // Compare the fine derivative with motion over the surrounding 1/60 s.
            // A loss above 28% at this scale exposes an artificial pause. Ignore
            // naturally stationary channels below 8 cm/s to avoid numerical ratios.
            metrics.velocityRetentionChecked = referenceSpeed >= .08f;
            if (metrics.velocityRetentionChecked)
            {
                Vector3 fineVelocity = (metrics.seamIncomingVelocity + metrics.seamOutgoingVelocity) * .5f;
                metrics.seamVelocityRetention = Vector3.Dot(fineVelocity, metrics.nearbyReferenceVelocity) /
                    metrics.nearbyReferenceVelocity.sqrMagnitude;
                Check(metrics.seamVelocityRetention >= .72f, failures,
                    clip.name + "/" + metrics.bone + ": a velocidade cai artificialmente na emenda; retenção=" +
                    metrics.seamVelocityRetention.ToString("F3") + ", referência=" + referenceSpeed.ToString("F3") + "m/s.");
            }
            // At the 120 Hz playback sampling, these discontinuities represent at most 1.25 mm
            // of foot travel or 0.5 mm of hip travel in a displayed frame. Other
            // measured joints use the foot tolerance; peak accelerations are reported
            // rather than bounded by assumptions about the authored gait.
            float velocityTolerance = metrics.bone == "Pelvis" ? .06f : .15f;
            Check(metrics.seamPoseErrorMetres <= .001f && metrics.seamRotationErrorDegrees <= .25f,
                failures, clip.name + "/" + metrics.bone + ": a pose não fecha na emenda (" +
                metrics.seamPoseErrorMetres.ToString("F5") + "m; " + metrics.seamRotationErrorDegrees.ToString("F3") + "°).");
            Check(metrics.seamVelocityJumpMetresPerSecond <= velocityTolerance, failures,
                clip.name + "/" + metrics.bone + ": salto de velocidade na emenda=" +
                metrics.seamVelocityJumpMetresPerSecond.ToString("F3") + "m/s (limite=" + velocityTolerance + ").");
        }
        Debug.Log("[LOCOMOTION CONTINUITY METRICS] " + clip.name + " samples=" + result.frames.Length +
            "; maxSeamVelocityJump=" + result.bones.Max(value => value.seamVelocityJumpMetresPerSecond).ToString("F4") +
            "m/s; minSeamVelocityRetention=" + result.bones.Where(value => value.velocityRetentionChecked)
                .Select(value => value.seamVelocityRetention).DefaultIfEmpty(1f).Min().ToString("F3") +
            "; peakAcceleration=" + result.bones.Max(value => value.peakAccelerationMetresPerSecondSquared).ToString("F2") + "m/s².");
        return result;
    }

    private static Pose[] Sample(GameObject hero, AnimationClip clip, float seconds, Transform[] bones)
    {
        AnimationMode.BeginSampling();
        try { AnimationMode.SampleAnimationClip(hero, clip, seconds); }
        finally { AnimationMode.EndSampling(); }
        return bones.Select(bone =>
        {
            Vector3 position = bone.position;
            Quaternion rotation = bone.rotation;
            Require(IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z) &&
                IsFinite(rotation.x) && IsFinite(rotation.y) && IsFinite(rotation.z) && IsFinite(rotation.w),
                clip.name + "/" + bone.name + ": pose inválida em " + seconds + "s.");
            return new Pose { position = position, rotation = rotation };
        }).ToArray();
    }

    private static Transform FindBone(Transform[] hierarchy, string name, bool required)
    {
        Transform[] matches = hierarchy.Where(value => value.name == name).ToArray();
        Require(matches.Length <= 1 && (!required || matches.Length == 1), "Osso ausente/duplicado: " + name);
        return matches.FirstOrDefault();
    }

    private static Vector3 AngularVelocity(Quaternion from, Quaternion to, float seconds)
    {
        Quaternion delta = to * Quaternion.Inverse(from);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        return Mathf.Abs(angle) < .00001f ? Vector3.zero : axis * (angle / seconds);
    }

    private static TransformPose[] CaptureTransforms(Transform root) => root.GetComponentsInChildren<Transform>(true)
        .Select(value => new TransformPose(value)).ToArray();
    private static bool TransformsMatch(TransformPose[] poses) => poses.All(value => value.Matches());
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Check(bool condition, List<string> failures, string message)
    {
        if (!condition) failures.Add(message);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TransformPose
    {
        private readonly Transform transform;
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;
        public TransformPose(Transform value)
        {
            transform = value; position = value.localPosition; rotation = value.localRotation; scale = value.localScale;
        }
        public bool Matches() => transform != null && Vector3.Distance(transform.localPosition, position) < .00001f &&
            Quaternion.Angle(transform.localRotation, rotation) < .01f && Vector3.Distance(transform.localScale, scale) < .00001f;
    }

    [Serializable] private sealed class Report
    {
        public float sampleRate = SampleRate;
        public float seamStepSeconds = SeamStep;
        public float nearbyReferenceStepSeconds = ReferenceStep;
        public bool passed, animationModeRestored, clonePoseRestored, sourcePosePreserved;
        public List<ClipReport> clips = new List<ClipReport>();
        public string[] failures;
    }

    [Serializable] private sealed class ClipReport
    {
        public string clip;
        public float durationSeconds, effectiveSampleRate;
        public bool hasRootBone;
        public float maxRootTranslationMetres, maxRootRotationDegrees, maxObjectTranslationMetres, maxObjectRotationDegrees;
        public float minAnkleTrackWidthMetres, maxAnkleTrackWidthMetres, maxKneeTrackWidthMetres;
        public BoneReport[] bones;
        public FramePose[] frames;
    }

    [Serializable] private sealed class BoneReport
    {
        public string bone;
        public float peakSpeedMetresPerSecond, peakAccelerationMetresPerSecondSquared, peakAccelerationAtSeconds;
        public float peakAngularSpeedDegreesPerSecond, peakAngularAccelerationDegreesPerSecondSquared;
        public float seamPoseErrorMetres, seamRotationErrorDegrees, seamVelocityJumpMetresPerSecond;
        public Vector3 seamIncomingVelocity, seamOutgoingVelocity, nearbyReferenceVelocity;
        public bool velocityRetentionChecked;
        public float seamVelocityRetention;
    }

    [Serializable] private sealed class FramePose
    {
        public float timeSeconds;
        public Pose[] bones;
    }

    [Serializable] private sealed class Pose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocityMetresPerSecond, accelerationMetresPerSecondSquared;
        public Vector3 angularVelocityDegreesPerSecond, angularAccelerationDegreesPerSecondSquared;
    }
}
