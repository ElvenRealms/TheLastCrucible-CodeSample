using Animancer;
using UnityEngine;

/// <summary>
/// Light combo, Heavy combo, or the single-step Running attack. All share
/// chaining, hit-window, root-motion, tracking, buffering, and interruption
/// cleanup while retaining distinct action identities.
/// </summary>
public class AttackState : PlayerState
{
    private readonly PlayerAction _attackAction;
    private int _comboIndex;
    private bool _hitWindowSuppressed;
    private AnimancerState _currentAnimState;
    private readonly AttackPlayback _playback = new();

    public bool IsAttackBuffered => machine.InputBuffer.Pending == _attackAction;
    public bool IsHitWindowSuppressed => _hitWindowSuppressed;

    public override bool CanBufferInput =>
        _currentAnimState != null && CurrentStep != null &&
        _currentAnimState.NormalizedTime >= CurrentStep.BufferWindowStart;

    private PlayerAttackData[] Combo => _attackAction switch
    {
        PlayerAction.HeavyAttack => machine.AnimationData.HeavyCombo,
        PlayerAction.RunningAttack => machine.AnimationData.RunningCombo,
        _ => machine.AnimationData.LightCombo
    };

    private PlayerAttackData CurrentStep => GetStep(_comboIndex);
    private PlayerAttackData EntryStep => GetStep(0);
    private bool IsConfigured => EntryStep != null && EntryStep.IsPlayerValid;

    public AttackState(PlayerStateMachine machine, PlayerAction attackAction) : base(machine)
    {
        _attackAction = attackAction;
    }

    public override bool CanEnter() =>
        IsConfigured && machine.Stamina.CanAfford(StaminaAction.Attack, EntryStep.StaminaCost);

    public override bool CanExit() =>
        _currentAnimState == null || CurrentStep == null ||
        _currentAnimState.NormalizedTime >= CurrentStep.CancelWindowStart;

    public override void Enter()
    {
        _comboIndex = 0;
        _hitWindowSuppressed = false;

        // Cost is spent here, never in a check: CanEnter already guaranteed affordability.
        machine.Stamina.TrySpend(StaminaAction.Attack, EntryStep.StaminaCost);

        if (!machine.LockOn.IsLockedOn)
        {
            Vector3 direction = machine.GetCameraRelativeInput();
            if (direction != Vector3.zero)
            {
                machine.Motor.FaceInstantly(direction);
            }
        }

        PlayAttack(0);
    }

    public override void Tick()
    {
        if (_currentAnimState == null || CurrentStep == null)
        {
            machine.Hitbox.EndWindow();
            return;
        }

        if (!_hitWindowSuppressed &&
            _currentAnimState.NormalizedTime >= CurrentStep.ComboWindowStart &&
            machine.InputBuffer.TryConsume(_attackAction))
        {
            TryPlayAttack(GetNextComboIndex());
        }

        _currentAnimState.Speed = _playback.Tick(_currentAnimState.NormalizedTime, Time.deltaTime);
        UpdateTracking();
        UpdateHitWindow();
    }

    // Locked on, the swing tracks the target; otherwise the stick steers it
    // relative to the camera. Both stop at the step's tracking end.
    private void UpdateTracking()
    {
        PlayerAttackData step = CurrentStep;
        if (step == null || _currentAnimState.NormalizedTime > step.TrackingEnd)
        {
            return;
        }

        if (machine.LockOn.HasValidTarget)
        {
            TrackLockOnTarget(step);
        }
        else
        {
            machine.Motor.RotateToward(machine.GetCameraRelativeInput(), machine.MovementConfig.AttackSteeringSpeed);
        }
    }

    private void TrackLockOnTarget(PlayerAttackData step)
    {
        if (step.TrackingTurnSpeed > 0f)
        {
            machine.Motor.RotateToward(machine.LockOn.PlanarDirectionToTarget, step.TrackingTurnSpeed);
        }
        else
        {
            machine.Motor.RotateToward(machine.LockOn.PlanarDirectionToTarget);
        }
    }

    // BeginWindow/EndWindow are idempotent, so polling them every frame is
    // fine. The window reads only the active state's time, so a faded-out
    // attack can never hit.
    private void UpdateHitWindow()
    {
        if (_hitWindowSuppressed)
        {
            machine.Hitbox.EndWindow();
            return;
        }

        PlayerAttackData step = CurrentStep;
        if (step == null)
        {
            machine.Hitbox.EndWindow();
            return;
        }
        float time = _currentAnimState.NormalizedTime;
        if (time >= step.HitWindowStart && time < step.HitWindowEnd)
        {
            machine.Hitbox.BeginWindow(step.Damage, step.Parryable, step.PoiseDamage, step.Blockable);
        }
        else
        {
            machine.Hitbox.EndWindow();
        }
    }

    // Presses before the buffer window are dropped, not deferred. A press of
    // the same attack chains from the combo window.
    public override bool OnInput(PlayerAction action)
    {
        if (action != _attackAction)
        {
            return base.OnInput(action);
        }

        if (_hitWindowSuppressed)
        {
            return false;
        }

        PlayerAttackData step = CurrentStep;
        if (step == null)
        {
            return false;
        }
        if (_currentAnimState.NormalizedTime >= step.ComboWindowStart)
        {
            return TryPlayAttack(GetNextComboIndex());
        }

        if (CanBufferInput)
        {
            machine.InputBuffer.Set(_attackAction);
            return true;
        }

        return false;
    }

    public void ClearBufferedInput()
    {
        machine.InputBuffer.Clear();
    }

    public void SuppressHitWindow()
    {
        _hitWindowSuppressed = true;
        machine.Hitbox.EndWindow();
    }

    public override void OnRootMotion(Vector3 deltaPosition)
    {
        PlayerAttackData step = CurrentStep;
        if (step == null || !step.AllowVerticalRootMotion)
        {
            deltaPosition.y = 0f;
        }
        if (step != null)
        {
            deltaPosition.x *= step.RootMotionScale;
            deltaPosition.z *= step.RootMotionScale;
        }
        machine.Motor.MoveWithRootMotion(deltaPosition);
    }

    public override void Exit()
    {
        ClearEndEvent();
        machine.Hitbox.EndWindow();
        _hitWindowSuppressed = false;
    }

    private PlayerAttackData GetStep(int index)
    {
        PlayerAttackData[] combo = Combo;
        return combo != null && index >= 0 && index < combo.Length ? combo[index] : null;
    }

    private void PlayAttack(int index)
    {
        ClearEndEvent();
        machine.Hitbox.EndWindow();
        _comboIndex = index;
        _hitWindowSuppressed = false;
        if (machine.LockOn.HasValidTarget)
        {
            machine.Motor.FaceInstantly(machine.LockOn.PlanarDirectionToTarget);
        }

        PlayerAttackData step = CurrentStep;
        if (step == null || !step.IsPlayerValid)
        {
            machine.CompleteAction();
            return;
        }

        _currentAnimState = machine.Animancer.Play(step.Transition);
        _playback.Start(step, _currentAnimState.NormalizedTime);
        _currentAnimState.Speed = _playback.Speed;
        _currentAnimState.Events(this).OnEnd = OnAttackEnd;
    }

    private bool TryPlayAttack(int index)
    {
        PlayerAttackData step = GetStep(index);
        if (step == null || !step.IsPlayerValid ||
            !machine.Stamina.TrySpend(StaminaAction.Attack, step.StaminaCost))
        {
            return false;
        }

        machine.InputBuffer.Clear();
        PlayAttack(index);
        return true;
    }

    private int GetNextComboIndex()
    {
        return (_comboIndex + 1) % Combo.Length;
    }

    private void OnAttackEnd()
    {
        machine.CompleteAction();
    }

    // A state faded out by a chain or cancel keeps playing during the fade and
    // can still hit its end event; clear it so only the active attack ends the state.
    private void ClearEndEvent()
    {
        if (_currentAnimState != null)
        {
            _currentAnimState.Events(this).OnEnd = null;
        }
    }
}
