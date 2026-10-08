using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Holds while the target stands inside a band of planar distance and, for an option the boss
/// carries out on foot, while the ground in between is walkable.
/// </summary>
[System.Serializable]
public class BossRangeCondition : BossCondition
{
    [SerializeField, MinValue(0f), SuffixLabel("m")]
    [Tooltip("Shortest planar distance to the target at which this holds.")]
    private float minimum;
    [SerializeField, MinValue(0f), SuffixLabel("m"), ValidateInput(nameof(RangeOrdered), "maximum must be greater than or equal to minimum.")]
    [Tooltip("Longest planar distance to the target at which this holds.")]
    private float maximum = 2.5f;
    [SerializeField]
    [Tooltip("On for an attack that travels to its target, so the ground in between must be walkable. Off for one that lands from where the boss stands, such as a projectile.")]
    private bool reachedOnFoot = true;

    public float Minimum => minimum;
    public float Maximum => maximum;
    public override string InspectorLabel => System.FormattableString.Invariant($"Range {minimum:0.##} to {maximum:0.##} m{(reachedOnFoot ? ", on foot" : string.Empty)}");

    private bool RangeOrdered => maximum >= minimum;

    public BossRangeCondition()
    {
    }

    public BossRangeCondition(float minimum, float maximum, bool reachedOnFoot = true)
    {
        this.minimum = minimum;
        this.maximum = maximum;
        this.reachedOnFoot = reachedOnFoot;
    }

    public override bool IsMet(in BossDecisionContext context, BossOptionMemory memory)
    {
        bool inBand = context.planarDistance >= minimum && context.planarDistance <= maximum;
        return inBand && (!reachedOnFoot || context.targetReachable);
    }
}
