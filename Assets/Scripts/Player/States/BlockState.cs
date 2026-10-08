using System;
using Animancer;
using UnityEngine;

/// <summary>
/// The raised guard. Block Begin leads into a looping hold for as long as Block
/// is held, and Block End lowers it; a tap keeps the guard up until its Parry
/// Window closes. PlayerDefense decides what each hit does to the guard, and this
/// state plays the result: a parry clip picked by the side the hit came from, a
/// short slide back from a blocked hit, and the Flinch of a broken guard. A raised
/// guard moves at Guard Speed, with the legs walking on their own layer under the
/// guard clips; a parry roots it until its clip ends. Other
/// actions interrupt the guard at any time, except during a parry's short
/// recovery, which buffers them and keeps the guard up. Pressing Block again never
/// leaves the state: it renews the guard and its Parry Window. A guard that comes
/// back up because Block is still held, with no new press, opens no Parry Window.
/// A lowered guard stays down for PlayerDefense's Guard Cooldown, whatever Block does.
/// </summary>
public class BlockState : PlayerState
{
    // A hit landing this far to one side of the body's centre plays that side's parry.
    private const float SideParryOffset = 0.12f;
    // Masked to the legs, above the base layer that plays the guard clips.
    private const int LegsLayerIndex = 1;

    private enum Phase
    {
        Raising,
        Holding,
        Parrying,
        Lowering
    }

    private readonly KnockbackSlide _pushback = new();
    private Phase _phase;
    private AnimancerState _currentAnimState;
    private float _parryElapsed;
    private bool _isResuming;
    private MixerState<Vector2> _legsMixer;
    private bool _isWalking;
    private Vector2 _legsParameter;
    private Vector2 _legsParameterVelocity;

    public override bool CanBufferInput => IsRecoveringFromParry;

    private bool CanMove => _phase == Phase.Raising || _phase == Phase.Holding;

    private bool IsRecoveringFromParry =>
        _phase == Phase.Parrying && _parryElapsed < machine.AnimationData.ParryRecovery;
    private bool IsGuardWanted => machine.InputReader.BlockHeld || machine.Defense.IsParryWindowOpen;

    public BlockState(PlayerStateMachine machine) : base(machine)
    {
    }

    // Refuses without rejection feedback, because Locomotion asks every frame while Block is held.
    public override bool CanEnter() =>
        machine.AnimationData.IsBlockConfigured && !machine.Stamina.IsExhausted && machine.Defense.CanRaiseGuard;

    public override bool CanExit() => !IsRecoveringFromParry;

    /// <summary>
    /// Brings a guard that is still held back up without a new press, so without a Parry Window.
    /// </summary>
    public bool TryResume()
    {
        _isResuming = true;
        bool resumed = machine.RequestAction(PlayerAction.Block);
        _isResuming = false;
        return resumed;
    }

    public override void Enter()
    {
        machine.Defense.Parried += HandleParried;
        machine.Defense.Blocked += HandleBlocked;
        machine.Defense.GuardBroken += HandleGuardBroken;
        _pushback.Stop();

        if (machine.LockOn.IsLockedOn)
        {
            machine.Motor.FaceInstantly(machine.LockOn.PlanarDirectionToTarget);
        }
        else
        {
            machine.Motor.FaceInstantly(machine.GetCameraRelativeInput());
        }

        if (_isResuming)
        {
            machine.Defense.ResumeGuard();
        }
        else
        {
            machine.Defense.RaiseGuard();
        }
        PlayRaise();
    }

    public override void Tick()
    {
        machine.Motor.RotateToward(machine.LockOn.IsLockedOn
            ? machine.LockOn.PlanarDirectionToTarget
            : machine.GetCameraRelativeInput());
        Vector3 velocity = CanMove
            ? machine.GetCameraRelativeInput() * machine.MovementConfig.GuardSpeed
            : Vector3.zero;
        // Move runs every frame because the motor only applies gravity and grounding inside it.
        machine.Motor.Move(velocity + _pushback.Advance(Time.deltaTime));
        UpdateLegs(velocity);

        switch (_phase)
        {
            case Phase.Raising:
            case Phase.Holding:
                if (!IsGuardWanted)
                {
                    LowerGuard();
                }
                break;
            case Phase.Parrying:
                _parryElapsed += Time.deltaTime;
                if (!IsRecoveringFromParry && !machine.InputReader.BlockHeld)
                {
                    machine.CompleteAction();
                }
                break;
            case Phase.Lowering:
                // A press the Guard Cooldown refused raises the guard once the wait is over, as a held guard does.
                if (machine.InputReader.BlockHeld && CanEnter())
                {
                    machine.Defense.ResumeGuard();
                    PlayRaise();
                    break;
                }
                // Moving off cuts the lowering short, so letting go of Block never roots the player.
                if (machine.InputReader.MoveInput != Vector2.zero)
                {
                    machine.CompleteAction();
                }
                break;
        }
    }

    public override bool OnInput(PlayerAction action)
    {
        if (action != PlayerAction.Block)
        {
            return base.OnInput(action);
        }
        if (!machine.Defense.CanRaiseGuard)
        {
            return true;
        }

        machine.Defense.RaiseGuard();
        if (_phase == Phase.Lowering)
        {
            PlayRaise();
        }
        return true;
    }

    public override void Exit()
    {
        machine.Defense.Parried -= HandleParried;
        machine.Defense.Blocked -= HandleBlocked;
        machine.Defense.GuardBroken -= HandleGuardBroken;
        machine.Defense.LowerGuard();
        ClearEndEvent();
        UpdateLegs(Vector3.zero);
    }

    private void UpdateLegs(Vector3 velocity)
    {
        bool isWalking = velocity != Vector3.zero;
        if (isWalking != _isWalking)
        {
            SetWalking(isWalking);
        }

        if (isWalking && _legsMixer != null)
        {
            Vector3 local = machine.Motor.transform.InverseTransformDirection(velocity);
            _legsParameter = Vector2.SmoothDamp(
                _legsParameter,
                new Vector2(local.x, local.z),
                ref _legsParameterVelocity,
                machine.AnimationData.LockedLocomotionSmoothTime);
            _legsMixer.Parameter = _legsParameter;
        }
    }

    private void SetWalking(bool isWalking)
    {
        _isWalking = isWalking;
        PlayerAnimationData data = machine.AnimationData;
        // An unmasked layer would replace the whole guard pose, so without a mask the guard slides.
        if (data.GuardLegsMask == null)
        {
            return;
        }

        AnimancerLayer legs = machine.Animancer.Layers[LegsLayerIndex];
        float fadeDuration = data.LockedLocomotionMixer.FadeDuration;
        if (!isWalking)
        {
            legs.StartFade(0f, fadeDuration);
            return;
        }

        if (_legsMixer == null)
        {
            legs.Mask = data.GuardLegsMask;
            // Its own state: Locomotion's mixer stays on the base layer, free to fade in and out there.
            _legsMixer = data.LockedLocomotionMixer.CreateState();
        }
        legs.Play(_legsMixer, fadeDuration);
    }

    private void PlayRaise()
    {
        _phase = Phase.Raising;
        Play(machine.AnimationData.BlockBegin, HoldGuard);
    }

    private void HoldGuard()
    {
        _phase = Phase.Holding;
        Play(machine.AnimationData.BlockLoop, null);
    }

    private void LowerGuard()
    {
        machine.Defense.LowerGuard();
        _phase = Phase.Lowering;
        Play(machine.AnimationData.BlockEnd, HandleGuardLowered);
    }

    private void HandleGuardLowered()
    {
        machine.CompleteAction();
    }

    private void HandleParried(DamageInfo info)
    {
        _phase = Phase.Parrying;
        _parryElapsed = 0f;
        _pushback.Stop();
        Play(SelectParryClip(info.hitPoint), HoldGuard);
    }

    private void HandleBlocked(DamageInfo info)
    {
        PlayerAnimationData data = machine.AnimationData;
        _pushback.StartAwayFrom(GetAttacker(info), machine.transform.position, data.BlockPushbackDistance, data.BlockPushbackDuration);
    }

    private void HandleGuardBroken(DamageInfo info)
    {
        // PlayerDefense has already dropped the guard; without a Flinch to play, lower it here too.
        if (!machine.Flinch.TryStartGuardBreak(GetAttacker(info)))
        {
            LowerGuard();
        }
    }

    private ClipTransition SelectParryClip(Vector3 hitPoint)
    {
        PlayerAnimationData data = machine.AnimationData;
        float side = machine.transform.InverseTransformPoint(hitPoint).x;
        if (side > SideParryOffset)
        {
            return data.ParryRight;
        }
        if (side < -SideParryOffset)
        {
            return data.ParryLeft;
        }
        return data.ParryFront;
    }

    private void Play(ClipTransition clip, Action onEnd)
    {
        ClearEndEvent();
        _currentAnimState = machine.Animancer.Play(clip);
        _currentAnimState.Events(this).OnEnd = onEnd;
    }

    // A clip faded out by the next one keeps playing during the fade and can still
    // reach its end event; clear it so only the active clip moves the guard on.
    private void ClearEndEvent()
    {
        if (_currentAnimState != null)
        {
            _currentAnimState.Events(this).OnEnd = null;
        }
    }

    private static Transform GetAttacker(DamageInfo info)
    {
        return info.attacker != null ? info.attacker : info.source;
    }
}
