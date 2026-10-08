using Animancer;
using UnityEngine;

/// <summary>
/// Root-motion dodge roll. Free-look: face the input direction and roll
/// forward. Locked on: keep facing the target and pick the 8-directional roll
/// matching the input direction relative to facing. Opens the i-frame window
/// on PlayerDefense during the configured normalized-time span. The roll's
/// tail is a buffer window: the machine's shared Input Buffer holds the
/// latest Attack, Dodge, or Block press and fires it when the roll ends.
/// </summary>
public class DodgeState : PlayerState
{
    private AnimancerState currentAnimState;

    public DodgeState(PlayerStateMachine machine) : base(machine)
    {
    }

    public override bool CanEnter() => machine.Stamina.CanAfford(StaminaAction.Dodge, machine.Stamina.DodgeCost);

    public override bool CanExit() => false;

    public override bool CanBufferInput =>
        currentAnimState.NormalizedTime >= machine.AnimationData.DodgeInputBufferStart;

    public override void Enter()
    {
        // Cost is spent here, never in a check: CanEnter already guaranteed affordability.
        machine.Stamina.TrySpend(StaminaAction.Dodge, machine.Stamina.DodgeCost);

        Vector3 direction = machine.GetCameraRelativeInput();

        ClipTransition roll;
        if (machine.LockOn.IsLockedOn)
        {
            machine.Motor.FaceInstantly(machine.LockOn.PlanarDirectionToTarget);
            // No input rolls away from the target, souls-style backstep.
            if (direction == Vector3.zero) direction = -machine.Motor.transform.forward;
            roll = GetDirectionalRoll(direction);
        }
        else
        {
            if (direction == Vector3.zero) direction = machine.Motor.transform.forward;
            machine.Motor.FaceInstantly(direction);
            roll = machine.AnimationData.GetDodgeRoll(0);
        }

        currentAnimState = machine.Animancer.Play(roll);
        currentAnimState.Events(this).OnEnd = OnRollEnd;
    }

    // Same idempotent polling pattern as AttackState.UpdateHitWindow.
    public override void Tick()
    {
        float time = currentAnimState.NormalizedTime;
        machine.Defense.SetDodgeInvulnerable(
            time >= machine.AnimationData.DodgeIFrameStart &&
            time < machine.AnimationData.DodgeIFrameEnd
        );
    }

    public override void OnRootMotion(Vector3 deltaPosition)
    {
        machine.Motor.MoveWithRootMotion(deltaPosition);
    }

    // A forced state change mid-roll (e.g. death) must not leave the flag set
    // or the end event armed on the fading state (see AttackState.ClearEndEvent).
    public override void Exit()
    {
        machine.Defense.SetDodgeInvulnerable(false);
        currentAnimState.Events(this).OnEnd = null;
    }

    // Maps a world direction to the nearest of the 8 rolls (clockwise from forward).
    private ClipTransition GetDirectionalRoll(Vector3 worldDirection)
    {
        Vector3 local = machine.Motor.transform.InverseTransformDirection(worldDirection);
        float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        int index = (Mathf.RoundToInt(angle / 45f) + 8) % 8;
        return machine.AnimationData.GetDodgeRoll(index);
    }

    private void OnRollEnd()
    {
        machine.CompleteAction();
    }
}