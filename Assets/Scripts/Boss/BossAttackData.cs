using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One boss attack swing with committed-sequence timing.
/// </summary>
[CreateAssetMenu(fileName = "BossAttack", menuName = "BossFight/Attacks/Boss Attack")]
public sealed class BossAttackData : AttackDataBase
{
    // Enough to follow a curve drawn with a handful of keys across one wind-up.
    private const int TravelWeightSamples = 24;

    [BoxGroup("Playback Speed"), SerializeField, TableList]
    [ValidateInput(nameof(AreHoldsValid), "Holds need a positive Duration and must be in time order.")]
    [Tooltip("Pauses that slow the clip at a normalized time for a set number of seconds. Tracking keeps turning during a hold placed before Tracking End.")]
    private List<AttackHold> holds = new();

    [BoxGroup("Root Motion"), SerializeField]
    [Tooltip("Multiplier on the clip's horizontal travel at each normalized time, on top of Root Motion Scale. Draw it at 0 where the feet are planted and above 1 where the boss should cover more ground. Distance matching follows it: the stretch toward the target is spent where the curve is high and none where it is 0, and with a Reach Distance set it makes up the difference so the swing still lands on the target.")]
    private AnimationCurve travelByTime = AnimationCurve.Constant(0f, 1f, 1f);

    [BoxGroup("Distance Matching"), SerializeField, MinValue(0f), SuffixLabel("m")]
    [Tooltip("Planar distance from the boss's start position at which this swing connects without help. Until the Hit Window opens, forward travel is stretched or shortened so the swing lands on the target instead. 0 disables distance matching.")]
    private float reachDistance;
    [BoxGroup("Distance Matching"), SerializeField, MinValue(0f), SuffixLabel("m")]
    [Tooltip("Farthest the wind-up may carry the boss beyond the clip's own travel to land on the target. Shortening is limited to cancelling the clip's forward travel, so the boss never slides backward.")]
    private float maxReachExtension = 3f;

    [BoxGroup("Boss Chain"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(IsChainAfterHitWindow), "Chain / Exit must not come before Hit Window End, or the swing is cut short.")]
    [Tooltip("Time this step releases after its hit window closes: the next step starts here, or on the final step the boss blends back into locomotion. 1 plays the whole clip.")]
    private float chainTime = 0.8f;
    [BoxGroup("Boss Chain"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(IsEarlyExitAfterChain), "Early Exit before Chain / Exit has no effect: a stopped combo releases at Chain / Exit.", InfoMessageType.Warning)]
    [Tooltip("Time this step blends back into locomotion when the combo stops here instead of chaining on. Unused on a final step, which releases at Chain / Exit.")]
    private float earlyExitTime = 0.8f;
    [BoxGroup("Boss Chain"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Final step only: seconds to blend from the release pose into locomotion. An early release needs a longer blend to hide the unfinished recovery.")]
    private float exitFadeDuration = 0.4f;

    [BoxGroup("Projectile"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional volley this step launches, making it a ranged attack. The Hit Window still covers the weapon itself.")]
    private BossProjectileData projectile;
    [BoxGroup("Projectile"), SerializeField, Range(0f, 1f), ShowIf(nameof(projectile))]
    [ValidateInput(nameof(IsReleaseBeforeChain), "Projectile Release must not come after Chain / Exit, or the step ends before it fires.")]
    [Tooltip("Normalized time at which the volley leaves the boss.")]
    private float projectileReleaseTime = 0.3f;

    [BoxGroup("Impact Feedback"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional feedback played when this attack lands, on top of the target's own hit feedback.")]
    private CombatFeedbackProfile hitFeedback;

    public override IReadOnlyList<AttackHold> Holds => holds;
    public float ReachDistance => reachDistance;
    public float MaxReachExtension => maxReachExtension;
    public float ChainTime => chainTime;
    public float EarlyExitTime => earlyExitTime;
    public float ExitFadeDuration => exitFadeDuration;
    public BossProjectileData Projectile => projectile;
    public float ProjectileReleaseTime => projectileReleaseTime;
    public CombatFeedbackProfile HitFeedback => hitFeedback;

    public override bool IsValid => base.IsValid && IsFinite(reachDistance) && reachDistance >= 0f &&
        IsFinite(maxReachExtension) && maxReachExtension >= 0f &&
        IsFinite(exitFadeDuration) && exitFadeDuration >= 0f && IsNormalizedTimeValid(chainTime) &&
        IsNormalizedTimeValid(earlyExitTime) && IsChainAfterHitWindow && IsReleaseBeforeChain &&
        AreHoldsValid;

    /// <summary>Multiplier on the clip's horizontal travel at a normalized time, never below 0.</summary>
    public float TravelScale(float normalizedTime)
    {
        return travelByTime == null || travelByTime.length == 0 ? 1f : Mathf.Max(0f, travelByTime.Evaluate(normalizedTime));
    }

    /// <summary>
    /// Share of the distance correction still owed that the span from one normalized time to
    /// another should take, when the rest is spread up to the Hit Window by the travel curve.
    /// 1 once nothing is left after the span, and 0 for a span where the curve is 0, so a boss
    /// whose feet are planted up to the hit is never moved.
    /// </summary>
    public float TravelShare(float from, float to)
    {
        float spanEnd = Mathf.Min(to, HitWindowStart);
        float span = TravelWeight(from, spanEnd);
        float rest = TravelWeight(spanEnd, HitWindowStart);
        if (rest <= 0f)
        {
            return span > 0f ? 1f : 0f;
        }
        return span / (span + rest);
    }

    // The curve's area between two times, by the midpoint rule.
    private float TravelWeight(float from, float to)
    {
        if (to <= from)
        {
            return 0f;
        }

        float step = (to - from) / TravelWeightSamples;
        float sum = 0f;
        for (int i = 0; i < TravelWeightSamples; i++)
        {
            sum += TravelScale(from + (i + 0.5f) * step);
        }
        return sum * step;
    }

    // The release ends the step, so a hit window after it would never play out.
    private bool IsChainAfterHitWindow => chainTime >= HitWindowEnd;
    private bool IsReleaseBeforeChain => projectile == null ||
        (IsNormalizedTimeValid(projectileReleaseTime) && projectileReleaseTime <= chainTime);
    // Advisory only: the combo decides to stop at Chain / Exit, so an earlier exit clamps to it
    // and can never cut the hit window short.
    private bool IsEarlyExitAfterChain => earlyExitTime >= chainTime;

    private bool AreHoldsValid
    {
        get
        {
            for (int i = 0; i < holds.Count; i++)
            {
                AttackHold hold = holds[i];
                if (hold == null || !hold.IsValid || (i > 0 && hold.Time < holds[i - 1].Time))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
