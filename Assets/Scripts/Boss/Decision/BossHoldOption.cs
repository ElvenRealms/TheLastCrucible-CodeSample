/// <summary>
/// Stands in place and faces the target.
/// </summary>
[System.Serializable]
public class BossHoldAction : BossAction
{
    public override string InspectorLabel => "Hold";

    public override BossOption CreateOption(BossStateMachine machine, BossOptionEntry entry)
    {
        return new BossHoldOption(machine, entry);
    }
}

/// <summary>
/// Standing in place and facing the target.
/// </summary>
public class BossHoldOption : BossOption
{
    public BossHoldOption(BossStateMachine machine, BossOptionEntry entry) : base(machine, entry) { }

    public override bool IsSustained => true;

    public override void Tick()
    {
        machine.Motor.FaceInPlace(machine.PlanarDirectionToTarget);
        machine.Animation.SmoothLocomotionSpeed(machine.Motor.PlanarSpeed, false);
    }
}
