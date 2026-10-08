/// <summary>
/// The boss while it is free to act. BossBrain decides each frame what that
/// means: becoming enraged, starting an attack, pivoting toward the target, or
/// one way of moving (closing in, circling, or standing and facing).
/// </summary>
public class BossPursueState : BossState
{
    public BossPursueState(BossStateMachine machine) : base(machine) { }

    public override void Enter()
    {
        machine.Brain.Reset();
        machine.SetCombatAvailable(true);
        machine.Motor.ResumeNavigation();
        machine.Animation.PlayLocomotion(machine.LocomotionMixer);
    }

    public override void Tick()
    {
        if (machine.Target == null) return;
        machine.UpdateCombatPivotArming();
        machine.Brain.Decide();
    }
}
