using UnityEngine;

/// <summary>
/// Holds while the boss's health sits inside a share of its maximum, so an option can belong
/// to one part of the fight.
/// </summary>
[System.Serializable]
public class BossHealthCondition : BossCondition
{
    [SerializeField, Range(0f, 1f)]
    [Tooltip("Lowest share of maximum health at which this holds.")]
    private float minimumFraction;
    [SerializeField, Range(0f, 1f), Sirenix.OdinInspector.ValidateInput(nameof(FractionsOrdered), "maximumFraction must be greater than or equal to minimumFraction.")]
    [Tooltip("Highest share of maximum health at which this holds.")]
    private float maximumFraction = 1f;

    public override string InspectorLabel => System.FormattableString.Invariant($"Health {minimumFraction:0.#%} to {maximumFraction:0.#%}");

    private bool FractionsOrdered => maximumFraction >= minimumFraction;

    public BossHealthCondition()
    {
    }

    public BossHealthCondition(float minimumFraction, float maximumFraction)
    {
        this.minimumFraction = minimumFraction;
        this.maximumFraction = maximumFraction;
    }

    public override bool IsMet(in BossDecisionContext context, BossOptionMemory memory)
    {
        return context.healthFraction >= minimumFraction && context.healthFraction <= maximumFraction;
    }
}
