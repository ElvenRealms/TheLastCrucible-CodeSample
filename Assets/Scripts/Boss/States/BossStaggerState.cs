using UnityEngine;

/// <summary>
/// Stationary, damageable punish window entered when the boss's Poise breaks,
/// whether weapon hits or parries drained it. The authored
/// enter clip leads into a timed looping hold, then the exit clip completes
/// before the next action.
/// </summary>
public class BossStaggerState : BossState
{
    public float LoopDuration { get; private set; }
    public float RemainingLoopTime { get; private set; }

    private bool _hasEnteredLoop;
    private bool _isExiting;
    private float _loopEndTime;
    private float _exitDuration;
    private float _totalDuration;

    public BossStaggerState(BossStateMachine machine) : base(machine)
    {
    }

    public override void Enter()
    {
        LoopDuration = Mathf.Max(0f, machine.StaggerLoopDuration);
        RemainingLoopTime = 0f;
        _hasEnteredLoop = false;
        _isExiting = false;
        _loopEndTime = 0f;

        machine.Hitbox.EndWindow();
        machine.Animation.ClearCompletion();
        machine.Motor.Stop();
        machine.Motor.SuspendRootMotion();
        machine.SetCombatAvailable(true);
        machine.Health.SetDamageMultiplier(this, machine.StaggerDamageMultiplier);
        machine.Animation.PlayStagger(machine.StaggerEnterClip, machine.StaggerLoopClip, HandleLoopStarted);
        _exitDuration = GetSafeDuration(machine.StaggerExitClip.Length / machine.StaggerExitClip.Speed);
        float enterDuration = GetSafeDuration(machine.Animation.GetRemainingDuration());
        _totalDuration = GetSafeDuration(enterDuration + LoopDuration + _exitDuration);
        machine.Poise.Suspend();
        machine.LogDebug($"Stagger started with {LoopDuration:F2}s loop duration.");
    }

    public override void Tick()
    {
        machine.Hitbox.EndWindow();

        if (machine.Health.IsDead || !machine.IsEncounterTargetValid())
        {
            machine.FinishAction();
            return;
        }

        if (!_hasEnteredLoop || _isExiting) return;

        RemainingLoopTime = Mathf.Max(0f, _loopEndTime - Time.time);
        if (RemainingLoopTime > 0f) return;

        BeginExit();
    }

    public override void Exit()
    {
        machine.Health.SetDamageMultiplier(this, 1f);
        RemainingLoopTime = 0f;
        _hasEnteredLoop = false;
        _isExiting = false;
        _loopEndTime = 0f;
        _exitDuration = 0f;
        _totalDuration = 0f;
        machine.Hitbox.EndWindow();
        machine.Animation.ClearCompletion();
        machine.Motor.Stop();
        machine.Motor.ResumeRootMotion();
        machine.Poise.RestoreFull();
        machine.Poise.Resume();
    }

    public void NotifyHit()
    {
        if (!_hasEnteredLoop || _isExiting || Time.time >= _loopEndTime) return;
        machine.Animation.PlayStaggerHit(machine.StaggerHitClip, machine.StaggerLoopClip);
    }

    /// <summary>Remaining progress across the stagger enter, hold, and exit phases.</summary>
    public float GetRemainingFraction()
    {
        if (_totalDuration <= 0f)
        {
            return 0f;
        }

        float remainingDuration;

        if (!_hasEnteredLoop)
        {
            remainingDuration = machine.Animation.GetRemainingDuration() + LoopDuration + _exitDuration;
        }
        else if (_isExiting)
        {
            remainingDuration = machine.Animation.GetRemainingDuration();
        }
        else
        {
            remainingDuration = Mathf.Max(0f, _loopEndTime - Time.time) + _exitDuration;
        }

        return Mathf.Clamp01(GetSafeDuration(remainingDuration) / _totalDuration);
    }

    private void HandleLoopStarted()
    {
        if (machine.CurrentState != this) return;

        _hasEnteredLoop = true;
        RemainingLoopTime = LoopDuration;
        _loopEndTime = Time.time + LoopDuration;
        if (LoopDuration <= 0f) BeginExit();
    }

    private void BeginExit()
    {
        if (machine.CurrentState != this || _isExiting) return;

        _isExiting = true;
        RemainingLoopTime = 0f;
        machine.Animation.PlayStaggerExit(machine.StaggerExitClip, HandleStaggerCompleted);
    }

    private void HandleStaggerCompleted()
    {
        if (machine.CurrentState != this) return;

        machine.FinishAction();
    }

    private static float GetSafeDuration(float duration)
    {
        if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
        {
            return 0f;
        }

        return duration;
    }
}
