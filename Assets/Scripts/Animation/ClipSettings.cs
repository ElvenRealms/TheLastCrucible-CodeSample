using Animancer;
using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// Project-owned tuning for one animation clip. Builds the Animancer
/// <see cref="ClipTransition"/> that states play, so the Inspector shows only
/// the values we author.
/// </summary>
[Serializable]
public sealed class ClipSettings
{
    [SerializeField, Required, AssetsOnly]
    [ValidateInput(nameof(IsClipValid), "Clip must be a non-legacy AnimationClip.")]
    private AnimationClip clip;
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds spent blending from the current animation into this clip.")]
    private float fadeDuration = 0.25f;
    [SerializeField]
    [ValidateInput(nameof(IsSpeedValid), "Speed must be greater than zero.")]
    [Tooltip("Playback speed multiplier. 1 is the clip's authored speed.")]
    private float speed = 1f;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time at which playback starts, so replaying restarts the clip.")]
    private float normalizedStartTime;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time at which the clip counts as finished and hands over to whatever plays next. 1 is the clip's end; lower values skip a slow authored settle.")]
    private float normalizedEndTime = 1f;

    [NonSerialized]
    private ClipTransition _transition;

    public ClipSettings()
    {
    }

    public ClipSettings(AnimationClip clip)
    {
        this.clip = clip;
    }

    public ClipTransition Transition => _transition ??= CreateTransition();
    public AnimationClip Clip => clip;
    public bool IsLooping => clip != null && clip.isLooping;
    public bool IsValid => IsClipValid && float.IsFinite(fadeDuration) && fadeDuration >= 0f && IsSpeedValid &&
        float.IsFinite(normalizedStartTime) && normalizedStartTime >= 0f && normalizedStartTime <= 1f &&
        float.IsFinite(normalizedEndTime) && normalizedEndTime > normalizedStartTime && normalizedEndTime <= 1f;
    public string InspectorLabel => clip != null ? clip.name : "Unassigned";

    private bool IsClipValid => clip != null && !clip.legacy;
    private bool IsSpeedValid => float.IsFinite(speed) && speed > 0f;

    /// <summary>Drops the cached transition so the next play uses edited values.</summary>
    public void ResetTransition()
    {
        _transition = null;
    }

    private ClipTransition CreateTransition()
    {
        var transition = new ClipTransition
        {
            Clip = clip,
            FadeDuration = fadeDuration,
            Speed = speed,
            NormalizedStartTime = normalizedStartTime
        };
        if (normalizedEndTime < 1f)
        {
            var events = new AnimancerEvent.Sequence.Serializable();
            events.SetNormalizedEndTime(normalizedEndTime);
            transition.SerializedEvents = events;
        }
        return transition;
    }
}
