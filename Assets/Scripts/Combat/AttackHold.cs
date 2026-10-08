using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// A pause in an attack clip: playback slows at a normalized time for a set number of seconds.
/// </summary>
[Serializable]
public sealed class AttackHold
{
    [SerializeField, MinValue(0f), MaxValue(1f)]
    [Tooltip("Normalized time at which the hold begins.")]
    private float time;
    [SerializeField, MinValue(0.01f), SuffixLabel("s")]
    [Tooltip("Seconds the hold lasts, including its blend in.")]
    private float duration = 0.3f;
    [SerializeField, MinValue(0f), SuffixLabel("x")]
    [Tooltip("Playback speed multiplier during the hold. Slightly above 0 keeps the pose alive; 0 freezes it.")]
    private float speed = 0.05f;
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds to ease into the hold speed, and back out when the hold ends. 0 switches instantly.")]
    private float blend = 0.1f;

    public AttackHold()
    {
    }

    public AttackHold(float time, float duration, float speed, float blend)
    {
        this.time = time;
        this.duration = duration;
        this.speed = speed;
        this.blend = blend;
    }

    public float Time => time;
    public float Duration => duration;
    public float Speed => speed;
    public float Blend => blend;
    public bool IsValid => time >= 0f && time <= 1f && float.IsFinite(duration) && duration > 0f &&
        float.IsFinite(speed) && speed >= 0f && float.IsFinite(blend) && blend >= 0f;
}
