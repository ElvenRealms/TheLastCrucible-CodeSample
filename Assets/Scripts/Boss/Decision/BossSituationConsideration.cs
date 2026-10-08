using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Favours an option while the fight is in one situation, and leaves it alone otherwise.
/// </summary>
[System.Serializable]
public class BossSituationConsideration : BossConsideration
{
    [SerializeField]
    [Tooltip("Fight situation this answers. None never holds.")]
    private BossSituation situation;
    [SerializeField, MinValue(0f), SuffixLabel("x")]
    [Tooltip("Multiplier on the option's weight while the situation holds.")]
    private float multiplier = 3f;

    public override string Subject => situation.ToLabel();
    public override string InspectorLabel => System.FormattableString.Invariant($"{Subject} x{multiplier:0.##}");

    public BossSituationConsideration()
    {
    }

    public BossSituationConsideration(BossSituation situation, float multiplier)
    {
        this.situation = situation;
        this.multiplier = multiplier;
    }

    public override float Score(in BossDecisionContext context, BossOptionMemory memory)
    {
        return context.Holds(situation) ? multiplier : 1f;
    }

    public override bool Answers(in BossDecisionContext context) => multiplier > 1f && context.Holds(situation);
}
