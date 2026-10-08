/// <summary>
/// What the boss does when an option is chosen. Authored inline on the option. Each action
/// builds the BossOption that carries it out, so a new action is one new class and nothing
/// else in the decision code changes.
/// </summary>
[System.Serializable]
public abstract class BossAction
{
    /// <summary>The action in a word or two. Names an option that has no name of its own.</summary>
    public abstract string InspectorLabel { get; }

    public abstract BossOption CreateOption(BossStateMachine machine, BossOptionEntry entry);
}

/// <summary>
/// What one option scores at one moment, for the Inspector and Play Mode checks.
/// </summary>
[System.Serializable]
public struct BossOptionScore
{
    public string option;
    public int priority;
    public float score;
    /// <summary>The condition that fails, or the considerations moving the weight.</summary>
    public string detail;

    public BossOptionScore(string option, int priority, float score, string detail)
    {
        this.option = option;
        this.priority = priority;
        this.score = score;
        this.detail = detail;
    }
}

/// <summary>
/// One thing the boss can choose while it is free to act, built from its entry in the boss's
/// BossOptionSet. BossBrain scores every option each frame and starts one. Plain C#.
/// </summary>
public abstract class BossOption
{
    protected readonly BossStateMachine machine;
    private readonly BossOptionEntry _entry;

    protected BossOption(BossStateMachine machine, BossOptionEntry entry)
    {
        this.machine = machine;
        _entry = entry;
    }

    /// <summary>Short name shown in the Inspector and recorded by Play Mode checks.</summary>
    public virtual string Name => _entry.Name;
    /// <summary>Options are only weighed against others of the same priority, and the highest priority with anything on offer is decided first.</summary>
    public virtual int Priority => _entry.Priority;
    /// <summary>True for an option that runs frame by frame while it stays on offer, instead of handing over to a committed state.</summary>
    public virtual bool IsSustained => false;
    public BossOptionMemory Memory { get; } = new();

    /// <summary>How much the boss wants this right now. 0 means it is not on offer.</summary>
    public virtual float Score(in BossDecisionContext context)
    {
        return Memory.IsSetAside ? 0f : _entry.Scoring.Score(context, Memory);
    }

    /// <summary>
    /// Whether starting this option would look like a repeat of another: the same option, or
    /// one the player cannot tell apart from it as it begins.
    /// </summary>
    public virtual bool OpensLike(BossOption other) => other == this;

    /// <summary>Why the option scores what it does right now, for the Debug score table.</summary>
    public virtual string Describe(in BossDecisionContext context)
    {
        return Memory.IsSetAside ? "set aside" : _entry.Scoring.Describe(context, Memory);
    }

    /// <summary>
    /// Begins the option. False when the boss decided against it after all, which leaves the
    /// choice to the priorities below.
    /// </summary>
    public virtual bool TryStart() => true;

    /// <summary>Prepares a sustained option that is taking over from another, or from none.</summary>
    public virtual void Begin() { }

    /// <summary>Runs a sustained option for one frame.</summary>
    public virtual void Tick() { }

    /// <summary>Ends a sustained option when another one takes over.</summary>
    public virtual void Stop() { }
}
