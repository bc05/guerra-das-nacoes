using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// FBX baking stores linear samples. Reconstruct continuous joint velocities
// when importing this rig, including the wrap between the last and first keys.
public sealed class KnightAnimationCurves : AssetPostprocessor
{
    public override uint GetVersion() => 1;

    private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (assetPath != "Assets/Art/Cavaleiro.fbx" ||
            !(Contains(clip.name, "Idle") || Contains(clip.name, "Walk") || Contains(clip.name, "Run"))) return;

        clip.EnsureQuaternionContinuity();
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Transform)) continue;
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length < 3) continue;
            Keyframe[] keys = curve.keys;
            int last = keys.Length - 1;
            float duration = keys[last].time - keys[0].time;
            if (duration <= 0f) continue;
            float drift = keys[last].value - keys[0].value;
            for (int i = 0; i <= last; i++)
            {
                float beforeTime = i > 0 ? keys[i - 1].time : keys[last - 1].time - duration;
                float beforeValue = i > 0 ? keys[i - 1].value : keys[last - 1].value - drift;
                float afterTime = i < last ? keys[i + 1].time : keys[1].time + duration;
                float afterValue = i < last ? keys[i + 1].value : keys[1].value + drift;
                float tangent = (afterValue - beforeValue) / (afterTime - beforeTime);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Free);
                Keyframe key = curve[i];
                key.inTangent = key.outTangent = tangent;
                key.weightedMode = WeightedMode.None;
                curve.MoveKey(i, key);
            }
            bindings.Add(binding);
            curves.Add(curve);
        }
        // Apply transform curves together to avoid repeatedly combining their keys.
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
    }

    private static bool Contains(string value, string text) =>
        value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
}
