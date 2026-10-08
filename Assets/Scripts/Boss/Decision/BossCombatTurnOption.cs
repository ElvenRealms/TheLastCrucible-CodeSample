/// <summary>
/// Plays an authored pivot toward a target that has got round the boss.
/// </summary>
[System.Serializable]
public class BossCombatTurnAction : BossAction
{
    public override string InspectorLabel => "Combat Turn";

    public override BossOption CreateOption(BossStateMachine machine, BossOptionEntry entry)
    {
        return new BossCombatTurnOption(machine, entry);
    }
}

/// <summary>
/// An authored pivot toward a target that has got far enough round the boss. Declines while
/// the facing error is under the combat pivot angle or the pivot has not rearmed.
/// </summary>
public class BossCombatTurnOption : BossOption
{
    public BossCombatTurnOption(BossStateMachine machine, BossOptionEntry entry) : base(machine, entry) { }

    public override bool TryStart()
    {
        return machine.TryStartCombatTurn();
    }
}
