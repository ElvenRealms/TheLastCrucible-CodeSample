/// <summary>
/// Terminal boss state. Combat and navigation stop immediately; the root body
/// collider is disabled only after the death animation completes.
/// </summary>
public class BossDeadState : BossState
{
    public BossDeadState(BossStateMachine machine) : base(machine) { }

    public override void Enter()
    {
        machine.Hitbox.EndWindow();
        machine.ClearTarget();
        machine.SetCombatAvailable(false);
        machine.Motor.DisableNavigation();
        machine.Animation.PlayDeath(machine.DeathClip, HandleDeathCompleted);
    }

    private void HandleDeathCompleted()
    {
        machine.RootBodyCollider.enabled = false;
        machine.NotifyDeathAnimationCompleted();
    }
}
