using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Forwards incoming damage, poise breaks and the melee hit window's captured attack profile.
/// The window snapshot survives encounter shutdown during a killing blow.
/// </summary>
[RequireComponent(typeof(Health), typeof(Poise))]
[DisallowMultipleComponent]
public class BossCombatFeedback : CombatFeedbackRequesterBase
{
    [FoldoutGroup("References"), SerializeField]
    private BossStateMachine stateMachine;
    [FoldoutGroup("References"), SerializeField]
    private MeleeHitbox hitbox;

    [FoldoutGroup("Poise Break"), SerializeField, AssetsOnly, InlineEditor]
    private CombatFeedbackProfile poiseBreakProfile;
    [FoldoutGroup("Poise Break"), SerializeField]
    private HumanBodyBones anchorBone = HumanBodyBones.Chest;

    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int incomingRequestCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int poiseBreakRequestCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int outgoingRequestCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private BossAttackData _windowAttack;

    private Health _health;
    private Poise _poise;
    private Transform _anchor;
    private CombatFeedbackRequest _incomingLastRequest;
    private CombatFeedbackRequest _poiseBreakLastRequest;
    private CombatFeedbackRequest _outgoingLastRequest;

    public int IncomingRequestCount => incomingRequestCount;
    public int PoiseBreakRequestCount => poiseBreakRequestCount;
    public int RequestCount => outgoingRequestCount;
    public CombatFeedbackRequest IncomingLastRequest => _incomingLastRequest;
    public CombatFeedbackRequest PoiseBreakLastRequest => _poiseBreakLastRequest;
    public CombatFeedbackRequest OutgoingLastRequest => _outgoingLastRequest;

    protected override void Awake()
    {
        base.Awake();
        _health = GetComponent<Health>();
        _poise = GetComponent<Poise>();
        if (stateMachine == null)
        {
            stateMachine = GetComponent<BossStateMachine>();
        }
        if (hitbox == null)
        {
            hitbox = GetComponentInChildren<MeleeHitbox>();
        }
        Animator animator = GetComponent<Animator>();
        _anchor = animator != null && animator.isHuman ? animator.GetBoneTransform(anchorBone) : null;
        if (_anchor == null)
        {
            _anchor = transform;
        }
    }

    private void OnEnable()
    {
        _health.Damaged += HandleDamaged;
        _poise.Broken += HandleBroken;
        if (hitbox != null)
        {
            hitbox.WindowOpened += HandleWindowOpened;
            hitbox.HitResolved += HandleHitResolved;
        }
    }

    private void OnDisable()
    {
        _health.Damaged -= HandleDamaged;
        _poise.Broken -= HandleBroken;
        if (hitbox != null)
        {
            hitbox.WindowOpened -= HandleWindowOpened;
            hitbox.HitResolved -= HandleHitResolved;
        }
        _windowAttack = null;
    }

    private void HandleDamaged(DamageInfo info)
    {
        int previous = ForwardedRequestCount;
        Forward(info.amount, _health.IsDead, info.source, info.hitPoint);
        if (ForwardedRequestCount != previous)
        {
            incomingRequestCount++;
            _incomingLastRequest = base.LastRequest;
        }
    }

    private void HandleBroken()
    {
        if (Forward(poiseBreakProfile, 0f, false, null, _anchor.position))
        {
            poiseBreakRequestCount++;
            _poiseBreakLastRequest = base.LastRequest;
        }
    }

    private void HandleWindowOpened()
    {
        _windowAttack = stateMachine != null ? stateMachine.Attack?.ActiveStep : null;
    }

    private void HandleHitResolved(HitResult result, DamageInfo info)
    {
        if (result != HitResult.Applied || _windowAttack == null || stateMachine == null)
        {
            return;
        }
        if (Forward(_windowAttack.HitFeedback, info.amount, false, stateMachine.transform, stateMachine.Target, info.hitPoint))
        {
            outgoingRequestCount++;
            _outgoingLastRequest = base.LastRequest;
        }
    }
}
