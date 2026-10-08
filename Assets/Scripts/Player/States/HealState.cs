using UnityEngine;

/// <summary>
/// Committed healing window with reduced-speed movement. PlayerHealing owns
/// the timer and the commit; this state keeps the player moving slowly and
/// ends when the attempt does.
/// </summary>
public class HealState : PlayerState
{
    public HealState(PlayerStateMachine machine) : base(machine)
    {
    }

    public override bool CanEnter() => machine.Healing.CanUse;

    public override bool CanExit() => false;

    public override void Enter()
    {
        // Subscribe before opening the window so the state is fully set up by
        // the time AttemptStarted reaches listeners.
        machine.Health.Damaged += HandleDamaged;
        machine.Motor.Move(Vector3.zero);
        // CanEnter already guaranteed CanUse, so this should never fail; no
        // transition here — Tick completes normally once IsAttempting is false.
        if (!machine.Healing.BeginAttempt())
        {
            Debug.LogError("HealState entered but PlayerHealing refused the attempt; CanEnter should have gated this.",
                machine);
        }
    }

    public override void Tick()
    {
        Vector3 moveDirection = machine.GetCameraRelativeInput();
        machine.Motor.RotateToward(machine.LockOn.IsLockedOn ? machine.LockOn.PlanarDirectionToTarget : moveDirection);
        machine.Motor.Move(
            moveDirection *
            (machine.MovementConfig.WalkSpeed * machine.Healing.MoveSpeedMultiplier));
        machine.Healing.Tick(Time.deltaTime);
        if (!machine.Healing.IsAttempting) machine.CompleteAction();
    }

    public override void Exit()
    {
        machine.Health.Damaged -= HandleDamaged;
        machine.Healing.CancelAttempt();
    }

    private void HandleDamaged(DamageInfo info)
    {
        if (machine.Health.IsDead) return;
        // This same Damaged invocation may already have broken Poise and
        // replaced Heal with Flinch (Exit already cancelled the attempt):
        // unsubscribing there does not remove this handler from the delegate
        // snapshot already being invoked, so CompleteAction here would cancel
        // that Flinch instead of Heal.
        if (machine.CurrentState != this) return;
        machine.Healing.CancelAttempt();
        machine.CompleteAction();
    }
}