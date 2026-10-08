using UnityEngine;

/// <summary>
/// Holds while the target is within an angle of straight ahead. The boss meets it by turning,
/// so an option that fails only this still counts as in reach.
/// </summary>
[System.Serializable]
public class BossFacingCondition : BossCondition
{
    [SerializeField, Range(0f, 180f)]
    [Tooltip("Largest facing error to either side, in degrees, at which this holds.")]
    private float maximumAngle = 35f;

    public float MaximumAngle => maximumAngle;
    public override string InspectorLabel => System.FormattableString.Invariant($"Facing within {maximumAngle:0.#} deg");

    public BossFacingCondition()
    {
    }

    public BossFacingCondition(float maximumAngle)
    {
        this.maximumAngle = maximumAngle;
    }

    public override bool IsMet(in BossDecisionContext context, BossOptionMemory memory)
    {
        return Mathf.Abs(context.facingAngle) <= maximumAngle;
    }
}
