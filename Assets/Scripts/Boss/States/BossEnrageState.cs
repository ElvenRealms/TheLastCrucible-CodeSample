/// <summary>
/// Stationary, damageable beat that marks the switch to the enraged
/// phase: the boss plays its enrage clip, fires its projectile burst at the
/// clip's peak, then finishes.
/// </summary>
public class BossEnrageState : BossState
{
    private bool _burstFired;

    public BossEnrageState(BossStateMachine machine) : base(machine) { }

    public override void Enter()
    {
        _burstFired = false;
        machine.Hitbox.EndWindow();
        machine.Motor.Stop();
        machine.Motor.SuspendRootMotion();
        machine.SetCombatAvailable(true);
        machine.Animation.PlayEnrage(machine.EnrageClip, HandleEnrageCompleted);
    }

    public override void Tick()
    {
        if (!machine.IsEncounterTargetValid())
        {
            machine.FinishAction();
            return;
        }

        if (!_burstFired && machine.EnrageProjectile != null && machine.Animation.NormalizedTime >= machine.EnrageBurstTime)
        {
            _burstFired = true;
            machine.Projectiles.Fire(machine.EnrageProjectile, machine.transform.forward);
        }
    }

    public override void Exit()
    {
        machine.Animation.ClearCompletion();
        machine.Motor.ResumeRootMotion();
    }

    private void HandleEnrageCompleted()
    {
        if (machine.CurrentState != this) return;

        machine.FinishAction();
    }
}
