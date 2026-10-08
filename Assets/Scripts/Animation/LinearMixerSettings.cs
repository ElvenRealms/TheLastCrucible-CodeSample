using Animancer;
using Sirenix.OdinInspector;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Project-owned tuning for a mixer blended by one float parameter. Builds the
/// Animancer <see cref="LinearMixerTransition"/> that states play.
/// </summary>
[Serializable]
public sealed class LinearMixerSettings
{
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds spent blending from the current animation into this mixer.")]
    private float fadeDuration = 0.25f;
    [SerializeField]
    [Tooltip("Playback speed multiplier applied to every clip in the mixer.")]
    private float speed = 1f;
    [SerializeField, TableList(AlwaysExpanded = true)]
    [ValidateInput(nameof(AreEntriesValid), "Each entry needs a non-legacy clip and a finite threshold.")]
    private LinearMixerEntry[] entries;

    [NonSerialized]
    private LinearMixerTransition _transition;

    public LinearMixerTransition Transition => _transition ??= CreateTransition();
    public bool IsValid => float.IsFinite(fadeDuration) && fadeDuration >= 0f && float.IsFinite(speed) &&
        AreEntriesValid;

    private bool AreEntriesValid =>
        entries != null &&
        entries.Length > 0 &&
        Array.TrueForAll(entries, entry => entry.IsValid);

    /// <summary>Drops the cached transition so the next play uses edited values.</summary>
    public void ResetTransition()
    {
        _transition = null;
    }

    private LinearMixerTransition CreateTransition()
    {
        var transition = new LinearMixerTransition
        {
            FadeDuration = fadeDuration,
            Speed = speed
        };
        transition.Animations = Array.ConvertAll(entries, entry => (Object)entry.Clip);
        transition.Thresholds = Array.ConvertAll(entries, entry => entry.Threshold);
        return transition;
    }
}

/// <summary>One clip in a <see cref="LinearMixerSettings"/> and the parameter value at which it plays alone.</summary>
[Serializable]
public struct LinearMixerEntry
{
    [SerializeField, Required, AssetsOnly]
    private AnimationClip clip;
    [SerializeField]
    [Tooltip("Parameter value at which this clip plays at full weight.")]
    private float threshold;

    public LinearMixerEntry(AnimationClip clip, float threshold)
    {
        this.clip = clip;
        this.threshold = threshold;
    }

    public AnimationClip Clip => clip;
    public float Threshold => threshold;
    public bool IsValid => clip != null && !clip.legacy && float.IsFinite(threshold);
}
