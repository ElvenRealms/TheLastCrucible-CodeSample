using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// A run played over the locomotion mixer on masked layers, so the legs run while the arms
/// and weapon keep the walk's pose. Leave the clip empty for a boss that only walks.
/// </summary>
[Serializable]
public sealed class BossRunSettings
{
    [SerializeField, AssetsOnly]
    [ValidateInput(nameof(IsClipUsable), "Clip must be a looping non-legacy AnimationClip.")]
    private AnimationClip clip;
    [SerializeField, AssetsOnly]
    [Tooltip("Body parts the run takes over completely, normally both legs and their foot IK.")]
    private AvatarMask legsMask;
    [SerializeField, AssetsOnly]
    [Tooltip("Body parts the run only leans on by Torso Weight, normally the body without arms or head. Leave empty to keep the walk's torso untouched.")]
    private AvatarMask torsoMask;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("How much of the run's lean and bounce reaches the Torso Mask. 0 keeps the walk's torso, 1 uses the run's.")]
    private float torsoWeight = 0.4f;
    [SerializeField, MinValue(0f), SuffixLabel("m/s")]
    [Tooltip("Speed above which the run starts to replace the walk's legs. Normally the mixer's walk threshold.")]
    private float blendFromSpeed = 1.58f;
    [SerializeField, MinValue(0.01f), SuffixLabel("m/s")]
    [Tooltip("Speed at which the run fully replaces the walk's legs and plays at its authored rate. Above or below it the clip speeds up or slows down so the feet keep pace with the ground.")]
    private float matchSpeed = 4.3f;
    [SerializeField, MinValue(0.01f), SuffixLabel("s")]
    [Tooltip("Seconds the run takes to blend fully in or out, e.g. when an attack starts mid-run.")]
    private float blendTime = 0.2f;

    public bool IsValid => clip != null && IsClipUsable && legsMask != null && matchSpeed > blendFromSpeed;
    public AnimationClip Clip => clip;
    public AvatarMask LegsMask => legsMask;
    public AvatarMask TorsoMask => torsoMask;
    public float TorsoWeight => torsoMask != null ? torsoWeight : 0f;
    public float BlendTime => blendTime;

    private bool IsClipUsable => clip == null || (!clip.legacy && clip.isLooping);

    /// <summary>How much of the run shows at this speed, from 0 at Blend From Speed to 1 at Match Speed.</summary>
    public float Weight(float speed)
    {
        return Mathf.InverseLerp(blendFromSpeed, matchSpeed, speed);
    }

    /// <summary>Clip speed that keeps the feet in pace with the ground at this speed.</summary>
    public float PlaybackSpeed(float speed)
    {
        return Mathf.Max(MinPlaybackSpeed, speed / matchSpeed);
    }

    // Below this the legs hang mid-stride while the run blends out.
    private const float MinPlaybackSpeed = 0.5f;
}
