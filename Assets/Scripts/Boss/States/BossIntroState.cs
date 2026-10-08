/// <summary>
/// Stationary beat that opens the first attempt: the boss plays its intro
/// clip where it stands, closed to damage and lock-on, then finishes.
/// </summary>
public class BossIntroState : BossState
{
    public BossIntroState(BossStateMachine machine) : base(machine) { }

    public override void Enter()
    {
        machine.Hitbox.EndWindow();
        machine.Motor.Stop();
        machine.Motor.SuspendRootMotion();
        // Damage only: the lock-on target stays up so a lock taken before the fight holds.
        machine.SetDamageable(false);
        machine.Poise.Suspend();
        machine.Animation.PlayIntro(machine.IntroClip, HandleIntroCompleted);
    }

    public override void Tick()
    {
        if (!machine.IsEncounterTargetValid())
        {
            machine.FinishAction();
        }
    }

    public override void Exit()
    {
        machine.Animation.ClearCompletion();
        machine.Motor.ResumeRootMotion();
        machine.SetCombatAvailable(true);
        machine.Poise.Resume();
    }

    private void HandleIntroCompleted()
    {
        if (machine.CurrentState != this) return;

        machine.FinishAction();
    }
}
