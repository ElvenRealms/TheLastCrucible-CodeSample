/// <summary>
/// Visible but inactive state used before activation and after a failed
/// attempt is deactivated.
/// </summary>
public class BossDormantState : BossState
{
    public BossDormantState(BossStateMachine machine) : base(machine) { }

    public override void Enter()
    {
        machine.Hitbox.EndWindow();
        machine.Motor.Stop();
        machine.ClearTarget();
        machine.SetCombatAvailable(true);
        machine.Animation.PlayLocomotion(machine.LocomotionMixer);
        machine.Animation.SetLocomotionSpeed(0f);
        machine.Poise.RestoreFull();
        machine.Poise.Suspend();
    }

    public override void Exit()
    {
        machine.Poise.Resume();
    }
}
