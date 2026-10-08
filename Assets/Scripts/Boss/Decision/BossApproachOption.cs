using UnityEngine;

/// <summary>
/// Closes the distance to the target.
/// </summary>
[System.Serializable]
public class BossApproachAction : BossAction
{
    public override string InspectorLabel => "Approach";

    public override BossOption CreateOption(BossStateMachine machine, BossOptionEntry entry)
    {
        return new BossApproachOption(machine, entry);
    }
}

/// <summary>
/// Closing the distance to the target through BossMotor, with an authored turn first when the
/// path leads far enough away from where the boss is facing.
/// </summary>
public class BossApproachOption : BossOption
{
    public BossApproachOption(BossStateMachine machine, BossOptionEntry entry) : base(machine, entry) { }

    public override bool IsSustained => true;

    public override void Tick()
    {
        if (!machine.Motor.PreparePursuit(machine.Target, out Vector3 pathDirection))
        {
            machine.Animation.SmoothLocomotionSpeed(0f, false);
            return;
        }
        if (machine.TryStartTurn(pathDirection))
        {
            return;
        }

        machine.Motor.Pursue();
        machine.Animation.SmoothLocomotionSpeed(machine.Motor.PlanarSpeed, machine.Motor.IsPathPending);
    }
}
