/// <summary>
/// A yes-or-no rule an option needs before the boss may choose it. Authored inline on the
/// option, so each option holds its own values.
/// </summary>
[System.Serializable]
public abstract class BossCondition
{
    /// <summary>The rule in a few words, shown as its row in the Inspector.</summary>
    public abstract string InspectorLabel { get; }

    public abstract bool IsMet(in BossDecisionContext context, BossOptionMemory memory);
}
