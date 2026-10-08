using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>Owns the boss's additive pose, applying posture before the shared hit recoil.</summary>
[RequireComponent(typeof(Animator), typeof(BossAnimation), typeof(Health))]
[DisallowMultipleComponent]
public class BossPosePresenter : MonoBehaviour
{
    [FoldoutGroup("Posture"), SerializeField]
    private HumanBodyBones[] bones = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };
    [FoldoutGroup("Posture"), SerializeField, Range(0f, 45f)]
    private float movementTiltDegrees = 12f;
    [FoldoutGroup("Posture"), SerializeField, Range(0f, 45f)]
    private float actionTiltDegrees;
    [FoldoutGroup("Posture"), SerializeField, Range(0f, 60f)]
    private float strafeUntwistDegrees = 25f;
    [FoldoutGroup("Posture"), SerializeField, MinValue(0.01f), SuffixLabel("s")]
    private float blendSeconds = 0.25f;

    [FoldoutGroup("Recoil"), SerializeField]
    private HitRecoilSettings recoil = HitRecoilSettings.Default;

    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float currentTiltDegrees;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float currentUntwistDegrees;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float currentAngleDegrees;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float lastHitSide;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int resolvedBoneCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int resolvedRecoilBoneCount;

    private Animator _animator;
    private BossAnimation _bossAnimation;
    private Health _health;
    private Transform[] _postureBones;
    private Transform[] _recoilBones;
    private AdditiveBonePose _pose;
    private readonly HitRecoilMotion _motion = new();

    public float CurrentTiltDegrees => currentTiltDegrees;
    public float CurrentUntwistDegrees => currentUntwistDegrees;
    public float CurrentAngleDegrees => currentAngleDegrees;
    public float LastHitSide => lastHitSide;
    public int ResolvedBoneCount => resolvedBoneCount;
    public int ResolvedRecoilBoneCount => resolvedRecoilBoneCount;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _bossAnimation = GetComponent<BossAnimation>();
        _health = GetComponent<Health>();
        _postureBones = AdditiveBonePose.Resolve(_animator, bones);
        _recoilBones = AdditiveBonePose.Resolve(_animator, recoil.bones);
        resolvedBoneCount = _postureBones.Length;
        resolvedRecoilBoneCount = _recoilBones.Length;
        _pose = new AdditiveBonePose(_postureBones, _recoilBones);
        if (resolvedBoneCount == 0 && resolvedRecoilBoneCount == 0)
        {
            Debug.LogWarning("[BossPosePresenter] Could not resolve any posture or recoil bones from the humanoid Animator; disabling.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        currentTiltDegrees = movementTiltDegrees;
        currentUntwistDegrees = 0f;
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
        Quaternion posture = Quaternion.identity;
        if (_animator.isActiveAndEnabled && _postureBones.Length > 0)
        {
            float target = _bossAnimation.IsPlayingMovement ? movementTiltDegrees : actionTiltDegrees;
            float duration = Mathf.Max(0.01f, blendSeconds);
            float maxStep = Mathf.Max(movementTiltDegrees, actionTiltDegrees) / duration * Time.deltaTime;
            currentTiltDegrees = Mathf.MoveTowards(currentTiltDegrees, target, maxStep);
            float untwistTarget = _bossAnimation.StrafeSide * strafeUntwistDegrees;
            currentUntwistDegrees = Mathf.MoveTowards(currentUntwistDegrees, untwistTarget, strafeUntwistDegrees / duration * Time.deltaTime);
            posture = Quaternion.AngleAxis(currentUntwistDegrees / _postureBones.Length, Vector3.up)
                * Quaternion.AngleAxis(-currentTiltDegrees / _postureBones.Length, transform.right);
        }
        _motion.Tick(Time.deltaTime, recoil);
        currentAngleDegrees = _motion.CurrentAngleDegrees;
        Quaternion kick = _motion.GetOffset(_recoilBones.Length, recoil.twistRatio);
        _pose.Apply(_postureBones, posture, _recoilBones, kick);
    }

    private void HandleDamaged(DamageInfo info)
    {
        _motion.Receive(info, transform, recoil);
        lastHitSide = _motion.LastHitSide;
        if (recoil.showDebugLogs)
        {
            Debug.Log($"[BossPosePresenter] Bend {_motion.PeakAngleDegrees:0.#} deg, side {lastHitSide:+0;-0}, from damage {info.amount:0.#}.", this);
        }
    }
}
