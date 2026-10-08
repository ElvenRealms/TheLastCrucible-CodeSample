using UnityEngine;

/// <summary>
/// Holds only while the fight is in one situation, such as the target standing behind the boss,
/// or only while it is not.
/// </summary>
[System.Serializable]
public class BossSituationCondition : BossCondition
{
    [SerializeField]
    [Tooltip("Fight situation that must hold. None never holds.")]
    private BossSituation situation;
    [SerializeField]
    [Tooltip("On holds while the fight is in the situation. Off holds while it is not.")]
    private bool mustHold = true;

    public BossSituation Situation => situation;
    public override string InspectorLabel => mustHold ? situation.ToLabel() : $"Not {situation.ToLabel()}";

    public BossSituationCondition()
    {
    }

    public BossSituationCondition(BossSituation situation, bool mustHold = true)
    {
        this.situation = situation;
        this.mustHold = mustHold;
    }

    public override bool IsMet(in BossDecisionContext context, BossOptionMemory memory)
    {
        return context.Holds(situation) == mustHold;
    }
}
