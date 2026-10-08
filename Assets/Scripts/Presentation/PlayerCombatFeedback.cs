using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>Forwards accepted damage and each guard outcome through their authored feedback profiles.</summary>
[RequireComponent(typeof(Health), typeof(PlayerDefense))]
[DisallowMultipleComponent]
public class PlayerCombatFeedback : CombatFeedbackRequesterBase
{
    [FoldoutGroup("Guard"), SerializeField, AssetsOnly, InlineEditor]
    private CombatFeedbackProfile parryProfile;
    [FoldoutGroup("Guard"), SerializeField, AssetsOnly, InlineEditor]
    private CombatFeedbackProfile blockProfile;
    [FoldoutGroup("Guard"), SerializeField, AssetsOnly, InlineEditor]
    private CombatFeedbackProfile guardBreakProfile;
    [FoldoutGroup("Guard"), SerializeField]
    private MeleeHitbox blade;

    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int incomingRequestCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int parryRequestCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int blockRequestCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int guardBreakRequestCount;

    private Health _health;
    private PlayerDefense _defense;
    private CombatFeedbackRequest _incomingLastRequest;
    private CombatFeedbackRequest _parryLastRequest;
    private CombatFeedbackRequest _blockLastRequest;
    private CombatFeedbackRequest _guardBreakLastRequest;

    public int IncomingRequestCount => incomingRequestCount;
    public int ParryRequestCount => parryRequestCount;
    public int BlockRequestCount => blockRequestCount;
    public int GuardBreakRequestCount => guardBreakRequestCount;
    public CombatFeedbackRequest IncomingLastRequest => _incomingLastRequest;
    public CombatFeedbackRequest ParryLastRequest => _parryLastRequest;
    public CombatFeedbackRequest BlockLastRequest => _blockLastRequest;
    public CombatFeedbackRequest GuardBreakLastRequest => _guardBreakLastRequest;

    protected override void Awake()
    {
        base.Awake();
        _health = GetComponent<Health>();
        _defense = GetComponent<PlayerDefense>();
    }

    private void OnEnable()
    {
        _health.Damaged += HandleDamaged;
        _defense.Parried += HandleParried;
        _defense.Blocked += HandleBlocked;
        _defense.GuardBroken += HandleGuardBroken;
    }

    private void OnDisable()
    {
        _health.Damaged -= HandleDamaged;
        _defense.Parried -= HandleParried;
        _defense.Blocked -= HandleBlocked;
        _defense.GuardBroken -= HandleGuardBroken;
    }

    private void HandleDamaged(DamageInfo info)
    {
        int previous = ForwardedRequestCount;
        Forward(info.amount, _health.IsDead, info.source, info.hitPoint);
        if (ForwardedRequestCount != previous)
        {
            incomingRequestCount++;
            _incomingLastRequest = LastRequest;
        }
    }

    private void HandleParried(DamageInfo info)
    {
        if (ForwardGuard(parryProfile, info))
        {
            parryRequestCount++;
            _parryLastRequest = LastRequest;
        }
    }

    private void HandleBlocked(DamageInfo info)
    {
        if (ForwardGuard(blockProfile, info))
        {
            blockRequestCount++;
            _blockLastRequest = LastRequest;
        }
    }

    private void HandleGuardBroken(DamageInfo info)
    {
        if (ForwardGuard(guardBreakProfile, info))
        {
            guardBreakRequestCount++;
            _guardBreakLastRequest = LastRequest;
        }
    }

    private bool ForwardGuard(CombatFeedbackProfile guardProfile, DamageInfo info)
    {
        return Forward(guardProfile, info.amount, false, info.source, blade != null ? blade.BladeCenter : info.hitPoint);
    }
}
