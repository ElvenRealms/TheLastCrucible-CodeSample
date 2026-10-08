/// <summary>
/// Switches the boss to its enraged phase, playing the enrage beat when there is one.
/// </summary>
[System.Serializable]
public class BossEnrageAction : BossAction
{
    public override string InspectorLabel => "Enrage";

    public override BossOption CreateOption(BossStateMachine machine, BossOptionEntry entry)
    {
        return new BossEnrageOption(machine, entry);
    }
}

/// <summary>
/// Becoming enraged. Without an enrage clip the switch happens at once and the boss carries
/// on choosing.
/// </summary>
public class BossEnrageOption : BossOption
{
    public BossEnrageOption(BossStateMachine machine, BossOptionEntry entry) : base(machine, entry) { }

    public override bool TryStart()
    {
        if (machine.IsEnraged)
        {
            return false;
        }
        if (machine.TryStartEnrage())
        {
            return true;
        }

        // The phase changed under the options still to be weighed.
        machine.Brain.Refresh();
        return false;
    }
}
