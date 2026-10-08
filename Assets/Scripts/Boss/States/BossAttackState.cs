using UnityEngine;

/// <summary>
/// Plays a committed sequence with independent tracking, wind-up, hit windows, speed
/// segments and holds for each step. A step with a projectile also launches its volley once,
/// at its release time. Each step releases at its chain time: into the next
/// step, or on the final step back to the boss's next choice with a blend that hides the rest of the recovery.
/// A combo may also stop short of its last step, on a failed continue roll or a target out of
/// reach; that step then plays on to its early exit time before it releases.
/// </summary>
public class BossAttackState : BossState
{
    private float _chainTime;
    private bool _isEndingEarly;
    private int _stepIndex;
    private BossAttackDefinition _preparedAttack;
    private Animancer.ClipTransition _attackClip;
    private float _damage;
    private float _poiseDamage;
    private bool _blockable;
    private bool _parryable;
    private float _parryPoiseDamage;
    private float _trackingEnd;
    private float _trackingTurnSpeed;
    private float _hitWindowStart;
    private float _hitWindowEnd;
    private float _previousTime;
    private bool _isWindingUp;
    private BossProjectileData _projectile;
    private float _projectileReleaseTime;
    private bool _projectileReleased;
    private readonly AttackPlayback _playback = new();

    public BossAttackDefinition ActiveAttack { get; private set; }
    public BossAttackData ActiveStep { get; private set; }
    public bool IsBlockable => ActiveStep != null && _blockable;
    public bool IsParryable => ActiveStep != null && _parryable;
    /// <summary>Poise Damage the boss takes when the active step is parried.</summary>
    public float ParryPoiseDamage => IsParryable ? _parryPoiseDamage : 0f;
    /// <summary>True from the start of a projectile step until its volley leaves or the step is interrupted.</summary>
    public bool IsChargingProjectile => ActiveStep != null && _projectile != null && !_projectileReleased;
    public BossProjectileData ActiveProjectile => ActiveStep != null ? _projectile : null;

    public BossAttackState(BossStateMachine machine) : base(machine) { }

    public void Prepare(BossAttackDefinition attack)
    {
        _preparedAttack = attack ?? throw new System.ArgumentNullException(nameof(attack));
    }

    public override void Enter()
    {
        ActiveAttack = _preparedAttack ?? throw new System.InvalidOperationException("BossAttackState requires a prepared attack before entry.");
        _preparedAttack = null;
        _stepIndex = 0;
        machine.Motor.CancelShove();
        StartStep();
    }

    public override void Tick()
    {
        float time = machine.Animation.NormalizedTime;
        if (_projectile != null && !_projectileReleased && time >= _projectileReleaseTime)
        {
            ReleaseProjectile();
            // A volley that lands at once can end the encounter, and this attack with it.
            if (machine.CurrentState != this)
            {
                return;
            }
        }
        if (time >= _chainTime)
        {
            if (!HasNextStep() || _isEndingEarly)
            {
                Release();
                return;
            }
            if (ShouldContinueCombo())
            {
                AdvanceStep();
                return;
            }

            _isEndingEarly = true;
            _chainTime = Mathf.Max(_chainTime, ActiveStep.EarlyExitTime);
            machine.LogDebug($"Combo stopped at step {_stepIndex + 1} of {ActiveAttack.StepCount}.");
            if (time >= _chainTime)
            {
                Release();
                return;
            }
        }
        machine.Animation.SetSpeed(_playback.Tick(time, Time.deltaTime));
        if (time < _trackingEnd)
        {
            machine.Motor.RotateToward(machine.PlanarDirectionToTarget, _trackingTurnSpeed);
        }
        machine.Motor.SetTravelScale(ActiveStep.TravelScale(time));
        if (time < _hitWindowStart && machine.Target != null)
        {
            machine.Motor.UpdateDistanceMatch(machine.Target.position, DistanceMatchFraction(time));
        }
        _previousTime = time;
        if (time >= _hitWindowStart)
        {
            EndWindUp();
        }

        if (time >= _hitWindowStart && time < _hitWindowEnd)
        {
            machine.Hitbox.BeginWindow(_damage, _parryable, _poiseDamage, _blockable);
        }
        else
        {
            machine.Hitbox.EndWindow();
        }
    }

    public override void Exit()
    {
        machine.Motor.SetVerticalRootMotionAllowed(false);
        machine.Motor.SetRootMotionScale(1f);
        machine.Motor.SetTravelScale(1f);
        EndWindUp();
        machine.Animation.ClearCompletion();
        machine.Hitbox.EndWindow();
        if (ActiveAttack != null)
        {
            machine.StartAttackDelay(ActiveAttack);
        }
        ActiveAttack = null;
        ActiveStep = null;
        _preparedAttack = null;
    }

    private void StartStep()
    {
        ActiveStep = ActiveAttack.GetStep(_stepIndex);
        _attackClip = ActiveStep.Transition;
        _damage = ActiveStep.Damage;
        _poiseDamage = ActiveStep.PoiseDamage;
        _blockable = ActiveStep.Blockable;
        _parryable = ActiveStep.Parryable;
        _parryPoiseDamage = ActiveStep.ParryPoiseDamage;
        _trackingEnd = ActiveStep.TrackingEnd;
        _trackingTurnSpeed = ActiveStep.TrackingTurnSpeed;
        _hitWindowStart = ActiveStep.HitWindowStart;
        _hitWindowEnd = ActiveStep.HitWindowEnd;
        _chainTime = ActiveStep.ChainTime;
        _projectile = ActiveStep.Projectile;
        _projectileReleaseTime = ActiveStep.ProjectileReleaseTime;
        _projectileReleased = false;
        _isEndingEarly = false;
        _previousTime = float.NaN;
        machine.Motor.Stop();
        machine.Motor.BeginDistanceMatch(ActiveStep.ReachDistance, ActiveStep.MaxReachExtension);
        machine.Motor.SetVerticalRootMotionAllowed(ActiveStep.AllowVerticalRootMotion);
        machine.Motor.SetRootMotionScale(ActiveStep.RootMotionScale);
        machine.Motor.SetTravelScale(ActiveStep.TravelScale(0f));
        machine.LogDebug("Attack animation started.");
        machine.Animation.PlayAttack(_attackClip, HandleClipEnded);
        _playback.Start(ActiveStep, 0f);
        machine.Animation.SetSpeed(_playback.Speed);
        _isWindingUp = true;
        machine.NotifyAttackWindUpStarted(ActiveStep);
    }

    // Share of the remaining distance correction for the coming frame, assuming
    // it advances as far as the last one did, so holds and speed segments
    // spread the correction over the time the wind-up actually takes. The step's
    // travel curve decides where in the wind-up the correction is spent.
    private float DistanceMatchFraction(float time)
    {
        float advance = time - _previousTime;
        if (float.IsNaN(advance) || advance <= 0f)
        {
            return 0f;
        }
        return ActiveStep.TravelShare(time, time + advance);
    }

    // Idempotent so the polled Tick and an interrupting Exit publish one end
    // per wind-up between them. Distance matching ends with the wind-up so
    // the swing itself plays at its authored speed.
    private void EndWindUp()
    {
        if (!_isWindingUp)
        {
            return;
        }

        _isWindingUp = false;
        machine.Motor.EndDistanceMatch();
        machine.NotifyAttackWindUpEnded();
    }

    // Aims at the target as far as the volley's aim angle allows, so late tracking errors
    // and a target on higher or lower ground do not send it wide.
    private void ReleaseProjectile()
    {
        _projectileReleased = true;
        Vector3 aim = machine.transform.forward;
        if (machine.Target != null)
        {
            Vector3 toTarget = machine.Target.position + Vector3.up * _projectile.Height - machine.Projectiles.GetOrigin(_projectile);
            aim = Vector3.RotateTowards(aim, toTarget.normalized, _projectile.AimAngle * Mathf.Deg2Rad, 0f);
        }
        machine.LogDebug($"Projectile '{_projectile.name}' released.");
        machine.Projectiles.Fire(_projectile, aim);
    }

    private bool HasNextStep() => _stepIndex + 1 < ActiveAttack.StepCount;

    // A fight script plays its combos in full so its beats stay on their marks.
    private bool ShouldContinueCombo()
    {
        if (machine.AreAttacksScripted)
        {
            return true;
        }

        float maximumRange = ActiveAttack.ContinueMaximumRange;
        if (maximumRange > 0f && machine.PlanarDirectionToTarget.magnitude > maximumRange)
        {
            return false;
        }
        return Random.value < ActiveAttack.ContinueChance;
    }

    private void AdvanceStep()
    {
        EndWindUp();
        machine.Animation.ClearCompletion();
        machine.Hitbox.EndWindow();
        _stepIndex++;
        StartStep();
    }

    // A step that chains on ignores its clip's end; the chain time in Tick moves it along.
    private void HandleClipEnded()
    {
        if (ActiveAttack == null || !HasNextStep() || _isEndingEarly)
        {
            Release();
        }
    }

    // Reached by the chain time or, when that is 1, possibly first by the clip's end event.
    private void Release()
    {
        if (machine.CurrentState != this || ActiveAttack == null)
        {
            return;
        }
        machine.LogDebug("Attack sequence completed.");
        machine.Animation.SetNextLocomotionFade(ActiveStep.ExitFadeDuration);
        machine.FinishAction();
    }
}
