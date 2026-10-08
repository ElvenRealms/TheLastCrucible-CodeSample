/// <summary>
/// One reason to favour or avoid an option, as a multiplier on its weight: 1 leaves the weight
/// alone, above 1 favours the option, and 0 rules it out. Authored inline on the option, so
/// each option holds its own values.
/// </summary>
[System.Serializable]
public abstract class BossConsideration
{
    /// <summary>The reason in a few words, shown as its row in the Inspector.</summary>
    public abstract string InspectorLabel { get; }
    /// <summary>What it reads, in a word or two. Names it in the Debug score table next to its multiplier right now.</summary>
    public abstract string Subject { get; }

    public abstract float Score(in BossDecisionContext context, BossOptionMemory memory);

    /// <summary>
    /// Whether the fight is in a state the option was authored to answer, which makes an attack
    /// go ahead whatever its start chance. A preference such as a curve does not count.
    /// </summary>
    public virtual bool Answers(in BossDecisionContext context) => false;
}
