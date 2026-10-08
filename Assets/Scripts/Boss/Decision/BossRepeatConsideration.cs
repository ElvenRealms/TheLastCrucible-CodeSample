using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Steers the boss away from the option it last started among those sharing its priority, and
/// from any that opens the same way, so it does not open with the same move twice running.
/// </summary>
[System.Serializable]
public class BossRepeatConsideration : BossConsideration
{
    [SerializeField, MinValue(0f), SuffixLabel("x")]
    [Tooltip("Multiplier on the option's weight while it is the one the boss last started. A repeat that falls under the option set's score cutoff is ruled out while another option is on offer, and still happens when nothing else is. 0 rules it out.")]
    private float multiplier = 0.1f;

    public override string Subject => "Just Used";
    public override string InspectorLabel => System.FormattableString.Invariant($"{Subject} x{multiplier:0.###}");

    public BossRepeatConsideration()
    {
    }

    public BossRepeatConsideration(float multiplier)
    {
        this.multiplier = multiplier;
    }

    public override float Score(in BossDecisionContext context, BossOptionMemory memory)
    {
        return memory.StartedLast ? multiplier : 1f;
    }
}
