using Animancer;
using Sirenix.OdinInspector;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Project-owned tuning for a mixer blended by a Vector2 parameter. Builds the
/// Animancer <see cref="MixerTransition2D"/> that states play.
/// </summary>
[Serializable]
public sealed class Mixer2DSettings
{
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds spent blending from the current animation into this mixer.")]
    private float fadeDuration = 0.25f;
    [SerializeField]
    [Tooltip("Playback speed multiplier applied to every clip in the mixer.")]
    private float speed = 1f;
    [SerializeField]
    [Tooltip("Directional suits clips arranged around the origin by angle, such as strafe sets. Cartesian blends on a grid.")]
    private MixerTransition2D.MixerType blendType = MixerTransition2D.MixerType.Directional;
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds the blend takes to follow a change of direction or speed. 0 snaps between clips.")]
    private float parameterSmoothTime = 0.12f;
    [SerializeField, TableList(AlwaysExpanded = true)]
    [ValidateInput(nameof(AreEntriesValid), "Each entry needs a non-legacy clip and a finite threshold.")]
    private Mixer2DEntry[] entries;

    [NonSerialized]
    private MixerTransition2D _transition;

    public MixerTransition2D Transition => _transition ??= CreateTransition();
    public float ParameterSmoothTime => parameterSmoothTime;
    public bool IsValid => float.IsFinite(fadeDuration) && fadeDuration >= 0f && float.IsFinite(speed) &&
        float.IsFinite(parameterSmoothTime) && parameterSmoothTime >= 0f && AreEntriesValid;

    private bool AreEntriesValid =>
        entries != null &&
        entries.Length > 0 &&
        Array.TrueForAll(entries, entry => entry.IsValid);

    /// <summary>Drops the cached transition so the next play uses edited values.</summary>
    public void ResetTransition()
    {
        _transition = null;
    }

    private MixerTransition2D CreateTransition()
    {
        var transition = new MixerTransition2D
        {
            FadeDuration = fadeDuration,
            Speed = speed
        };
        transition.Type = blendType;
        transition.Animations = Array.ConvertAll(entries, entry => (Object)entry.Clip);
        transition.Thresholds = Array.ConvertAll(entries, entry => entry.Threshold);
        return transition;
    }
}

/// <summary>One clip in a <see cref="Mixer2DSettings"/> and the parameter value at which it plays alone.</summary>
[Serializable]
public struct Mixer2DEntry
{
    [SerializeField, Required, AssetsOnly]
    private AnimationClip clip;
    [SerializeField]
    [Tooltip("Parameter value at which this clip plays at full weight.")]
    private Vector2 threshold;

    public Mixer2DEntry(AnimationClip clip, Vector2 threshold)
    {
        this.clip = clip;
        this.threshold = threshold;
    }

    public AnimationClip Clip => clip;
    public Vector2 Threshold => threshold;
    public bool IsValid => clip != null && !clip.legacy && float.IsFinite(threshold.x) && float.IsFinite(threshold.y);
}
