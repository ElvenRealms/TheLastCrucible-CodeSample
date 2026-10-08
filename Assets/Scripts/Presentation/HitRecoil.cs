using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Presentation-only hit reaction: bends the spine away from an accepted hit, holds through
/// hit-stop, then eases back. Purely additive on top of the animated pose; never touches
/// gameplay state, root motion, or hitboxes. Generic enough to sit on any damageable with a
/// Humanoid Animator.
/// </summary>
[RequireComponent(typeof(Health))]
public class HitRecoil : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField]
    [Tooltip("Humanoid bones that share the bend, lowest first. Spreading it over the spine moves the shoulders and head far more than one bone can.")]
    private HumanBodyBones[] bones = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };
    [FoldoutGroup("Config"), SerializeField, MinValue(0f)]
    [Tooltip("Total bend in degrees across all bones at or above referenceDamage. Smaller hits scale down linearly.")]
    private float maxAngleDegrees = 30f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0.0001f)]
    [Tooltip("Damage amount that produces the full maxAngleDegrees bend.")]
    private float referenceDamage = 45f;
    [FoldoutGroup("Config"), SerializeField, Range(0f, 1f)]
    [Tooltip("0 leans straight back from the attacker, which is nearly invisible from the attacker's camera; 1 leans fully sideways.")]
    private float sidewaysBias = 0.7f;
    [FoldoutGroup("Config"), SerializeField, Range(0f, 1f)]
    [Tooltip("Shoulder twist away from the struck side, as a fraction of the bend angle.")]
    private float twistRatio = 0.5f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f)]
    [Tooltip("Scaled-time seconds the full bend is held before recovering. Hit-stop pauses this too.")]
    private float holdSeconds = 0.05f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0.01f)]
    [Tooltip("Scaled-time seconds to ease back to the animated pose after the hold.")]
    private float recoverySeconds = 0.3f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float currentAngleDegrees;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float lastHitSide;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int resolvedBoneCount;

    private Health _health;
    private Transform[] _bones = Array.Empty<Transform>();
    private AdditiveBonePose _pose;
    private readonly HitRecoilMotion _motion = new();

    public float CurrentAngleDegrees => currentAngleDegrees;
    public float LastHitSide => lastHitSide;

    private HitRecoilSettings Settings => new HitRecoilSettings
    {
        bones = bones,
        maxAngleDegrees = maxAngleDegrees,
        referenceDamage = referenceDamage,
        sidewaysBias = sidewaysBias,
        twistRatio = twistRatio,
        holdSeconds = holdSeconds,
        recoverySeconds = recoverySeconds,
        showDebugLogs = showDebugLogs
    };

    private void Awake()
    {
        _health = GetComponent<Health>();
        _bones = AdditiveBonePose.Resolve(GetComponentInChildren<Animator>(), bones);
        _pose = new AdditiveBonePose(_bones);
        resolvedBoneCount = _bones.Length;
        if (_bones.Length == 0)
        {
            Debug.LogWarning("[HitRecoil] Could not resolve any bones from a humanoid Animator on this object or its children; disabling.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (_health != null)
        {
            _health.Damaged += HandleDamaged;
        }
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.Damaged -= HandleDamaged;
        }

        _pose?.Restore();
        _motion.Reset();
        currentAngleDegrees = 0f;
    }

    private void LateUpdate()
    {
        _motion.Tick(Time.deltaTime, Settings);
        currentAngleDegrees = _motion.CurrentAngleDegrees;
        _pose.Apply(_bones, _motion.GetOffset(_bones.Length, twistRatio));
    }

    private void HandleDamaged(DamageInfo info)
    {
        _motion.Receive(info, transform, Settings);
        lastHitSide = _motion.LastHitSide;

        if (showDebugLogs)
        {
            Debug.Log($"[HitRecoil] Bend {_motion.PeakAngleDegrees:0.#} deg, side {lastHitSide:+0;-0}, from damage {info.amount:0.#}.", this);
        }
    }
}
