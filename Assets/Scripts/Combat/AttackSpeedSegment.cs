using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// A span of an attack clip that plays faster or slower than the attack's base speed.
/// </summary>
[Serializable]
public sealed class AttackSpeedSegment
{
    [SerializeField, MinValue(0f), MaxValue(1f)]
    [Tooltip("Normalized time at which this speed begins.")]
    private float start;
    [SerializeField, MinValue(0f), MaxValue(1f)]
    [Tooltip("Normalized time at which playback returns to the attack's base speed.")]
    private float end = 0.1f;
    [SerializeField, MinValue(0.01f), SuffixLabel("x")]
    [Tooltip("Playback speed multiplier on top of the attack's base speed.")]
    private float speed = 1f;
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds to ease into this speed, and back out when playback returns to base speed. 0 switches instantly.")]
    private float blend;

    public AttackSpeedSegment()
    {
    }

    public AttackSpeedSegment(float start, float end, float speed, float blend)
    {
        this.start = start;
        this.end = end;
        this.speed = speed;
        this.blend = blend;
    }

    public float Start => start;
    public float End => end;
    public float Speed => speed;
    public float Blend => blend;
    public bool IsValid => start >= 0f && end <= 1f && start < end &&
        float.IsFinite(speed) && speed > 0f && float.IsFinite(blend) && blend >= 0f;

    public bool Contains(float normalizedTime) => normalizedTime >= start && normalizedTime < end;
}
