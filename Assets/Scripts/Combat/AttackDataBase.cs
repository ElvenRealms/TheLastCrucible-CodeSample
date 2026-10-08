using Animancer;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared animation, damage, tracking, and hit-window tuning for one melee swing.
/// </summary>
public abstract class AttackDataBase : ScriptableObject
{
    // Fine enough that simulated elapsed times stay within a millisecond of playback.
    private const float SimulationStep = 0.001f;
    private const int MaxSimulationSteps = 600000;

    [BoxGroup("Animation"), SerializeField, Required, AssetsOnly]
    [ValidateInput(nameof(IsAnimationClipValid), "Animation Clip must be a non-legacy clip.")]
    private AnimationClip animationClip;
    [BoxGroup("Animation"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds spent blending from the current animation into this attack.")]
    private float fadeDuration = 0.25f;
    [BoxGroup("Animation"), SerializeField]
    [Tooltip("Playback speed multiplier. 1 is the clip's authored speed.")]
    private float speed = 1f;
    [BoxGroup("Animation"), SerializeField, ToggleLeft]
    [Tooltip("Let Animancer choose the start time. An already-active state can resume from its current time.")]
    private bool useDefaultStartTime;
    [BoxGroup("Animation"), SerializeField, Range(0f, 1f), HideIf(nameof(useDefaultStartTime))]
    [ValidateInput(nameof(IsStartBeforeHitWindow), "Normalized Start Time must not come after Hit Window Start.")]
    [Tooltip("Normalized time at which playback starts.")]
    private float normalizedStartTime;
    [BoxGroup("Animation"), SerializeField, ToggleLeft]
    [Tooltip("End at 1 for forward playback or 0 for reverse playback.")]
    private bool useDefaultEndTime = true;
    [BoxGroup("Animation"), SerializeField, Range(0f, 1f), HideIf(nameof(useDefaultEndTime))]
    [ValidateInput(nameof(IsEndAfterHitWindow), "Normalized End Time must not come before Hit Window End, or the swing ends before its hit window closes.")]
    [Tooltip("Normalized time at which Animancer raises the end event.")]
    private float normalizedEndTime = 1f;
    [SerializeField, HideInInspector]
    private AnimancerEvent.Sequence.Serializable animationEvents;
    [BoxGroup("Animation"), SerializeField]
    [Tooltip("Allow this swing's animated vertical movement. Disable to keep movement grounded while retaining planar root motion.")]
    private bool allowVerticalRootMotion;

    [BoxGroup("Root Motion"), SerializeField, Range(0f, 3f)]
    [Tooltip("Multiplier on the clip's horizontal travel. 1 is the authored distance, 0 attacks in place. Vertical motion is unaffected.")]
    private float rootMotionScale = 1f;

    [BoxGroup("Damage"), SerializeField, MinValue(0f)]
    private float damage = 20f;
    [BoxGroup("Damage"), SerializeField, MinValue(0f), SuffixLabel("poise")]
    [Tooltip("Poise Damage dealt to the target when this swing lands.")]
    private float poiseDamage = 20f;
    [BoxGroup("Damage"), SerializeField]
    [Tooltip("Whether the defender's raised guard stops this swing. An unblockable swing lands through the guard.")]
    private bool blockable = true;
    [BoxGroup("Damage"), SerializeField]
    [Tooltip("Whether this swing is parried when it lands inside the defender's Parry Window.")]
    private bool parryable;
    [BoxGroup("Damage"), SerializeField, MinValue(0f), SuffixLabel("poise"), ShowIf(nameof(parryable))]
    [Tooltip("Poise Damage the attacker takes when this swing is parried.")]
    private float parryPoiseDamage = 40f;

    [BoxGroup("Tracking"), SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time through which tracking follows the current target.")]
    private float trackingEnd = 0.3f;
    [BoxGroup("Tracking"), SerializeField, MinValue(0f), SuffixLabel("deg/s")]
    [Tooltip("Maximum tracking turn speed. Zero uses the attack owner's configured turn speed.")]
    private float trackingTurnSpeed;

    [BoxGroup("Timing"), SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time at which the hitbox activates.")]
    private float hitWindowStart = 0.3f;
    [BoxGroup("Timing"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(IsHitWindowOrdered), "Hit Window End must be greater than Hit Window Start.")]
    [Tooltip("Normalized time at which the hitbox deactivates.")]
    private float hitWindowEnd = 0.55f;

    [BoxGroup("Playback Speed"), SerializeField, TableList]
    [ValidateInput(nameof(AreSpeedSegmentsValid), "Speed segments need Start < End, must be in time order, and must not overlap.")]
    [Tooltip("Spans of the clip that play faster or slower than the base speed.")]
    private List<AttackSpeedSegment> speedSegments = new();

    [NonSerialized]
    private ClipTransition _transition;

    public ClipTransition Transition => _transition ??= CreateTransition();
    public AnimationClip AnimationClip => animationClip;
    public bool AllowVerticalRootMotion => allowVerticalRootMotion;
    public float RootMotionScale => rootMotionScale;
    public float Damage => damage;
    public float PoiseDamage => poiseDamage;
    public bool Blockable => blockable;
    public bool Parryable => parryable;
    public float ParryPoiseDamage => parryPoiseDamage;
    public float TrackingEnd => trackingEnd;
    public float TrackingTurnSpeed => trackingTurnSpeed;
    public float HitWindowStart => hitWindowStart;
    public float HitWindowEnd => hitWindowEnd;
    public IReadOnlyList<AttackSpeedSegment> SpeedSegments => speedSegments;
    /// <summary>Pauses in the clip, in time order. Empty for an attack that has none.</summary>
    public virtual IReadOnlyList<AttackHold> Holds => Array.Empty<AttackHold>();
    public string InspectorLabel => animationClip != null ? animationClip.name : name;
    public virtual bool IsValid => IsAnimationClipValid && IsFinite(fadeDuration) && fadeDuration >= 0f &&
        IsFinite(speed) && speed > 0f &&
        (useDefaultStartTime || IsNormalizedTimeValid(normalizedStartTime)) &&
        (useDefaultEndTime || IsNormalizedTimeValid(normalizedEndTime)) &&
        IsFinite(rootMotionScale) && rootMotionScale >= 0f &&
        IsFinite(damage) && damage >= 0f && IsFinite(poiseDamage) && poiseDamage >= 0f &&
        IsFinite(parryPoiseDamage) && parryPoiseDamage >= 0f &&
        IsNormalizedTimeValid(trackingEnd) && IsFinite(trackingTurnSpeed) && trackingTurnSpeed >= 0f &&
        IsNormalizedTimeValid(hitWindowStart) && IsNormalizedTimeValid(hitWindowEnd) &&
        IsHitWindowOrdered && IsStartBeforeHitWindow && IsEndAfterHitWindow && AreSpeedSegmentsValid;

    protected virtual void OnValidate()
    {
        _transition = null;
    }

    /// <summary>The speed segment covering a normalized time, or null for base speed.</summary>
    public AttackSpeedSegment FindSpeedSegment(float normalizedTime)
    {
        foreach (var segment in speedSegments)
        {
            if (segment.Contains(normalizedTime))
            {
                return segment;
            }
        }
        return null;
    }

    /// <summary>
    /// Elapsed game seconds of clip playback from normalized time 0 to <paramref name="normalizedTime"/>,
    /// including speed segments, holds and their blends.
    /// </summary>
    public float GetElapsedSeconds(float normalizedTime)
    {
        float clipLength = AnimationClip != null ? AnimationClip.length : 0f;
        if (clipLength <= 0f || Transition.Speed <= 0f)
        {
            return 0f;
        }

        float target = Mathf.Clamp01(normalizedTime);
        var playback = new AttackPlayback();
        playback.Start(this, 0f);
        float time = 0f;
        float elapsed = 0f;
        for (int i = 0; time < target && i < MaxSimulationSteps; i++)
        {
            float speed = playback.Tick(time, SimulationStep);
            float nextTime = time + speed * SimulationStep / clipLength;
            if (nextTime >= target)
            {
                return elapsed + (target - time) * clipLength / speed;
            }
            time = nextTime;
            elapsed += SimulationStep;
        }
        return elapsed;
    }

    private ClipTransition CreateTransition()
    {
        AnimancerEvent.Sequence.Serializable events = animationEvents?.Clone();
        if (!useDefaultEndTime)
        {
            events ??= new AnimancerEvent.Sequence.Serializable();
            events.SetNormalizedEndTime(normalizedEndTime);
        }
        else if (events != null)
        {
            events.SetNormalizedEndTime(float.NaN);
        }

        return new ClipTransition
        {
            Clip = animationClip,
            FadeDuration = fadeDuration,
            Speed = speed,
            NormalizedStartTime = useDefaultStartTime ? float.NaN : normalizedStartTime,
            SerializedEvents = events
        };
    }

    private bool IsAnimationClipValid => animationClip != null && !animationClip.legacy;
    private bool IsHitWindowOrdered => hitWindowEnd > hitWindowStart;
    // A default start time can resume mid-clip, so only an explicit one is checked.
    private bool IsStartBeforeHitWindow => useDefaultStartTime || normalizedStartTime <= hitWindowStart;
    private bool IsEndAfterHitWindow => useDefaultEndTime || normalizedEndTime >= hitWindowEnd;

    private bool AreSpeedSegmentsValid
    {
        get
        {
            for (int i = 0; i < speedSegments.Count; i++)
            {
                AttackSpeedSegment segment = speedSegments[i];
                if (segment == null || !segment.IsValid || (i > 0 && segment.Start < speedSegments[i - 1].End))
                {
                    return false;
                }
            }
            return true;
        }
    }

    protected static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    protected static bool IsNormalizedTimeValid(float value) => IsFinite(value) && value >= 0f && value <= 1f;
}
