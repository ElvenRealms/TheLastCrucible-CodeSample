using Animancer;
using UnityEngine;

/// <summary>
/// Grounded locomotion with two modes: free-look (face movement, speed-driven
/// idle/walk/run mixer) and locked-on (face target, directional strafe mixer).
/// The strafe mixer's parameter eases toward the velocity, so a change of
/// direction blends between the strafe clips over the mixer's Parameter Smooth Time.
/// Sprinting while locked on temporarily reverts to free-look facing. A light
/// attack pressed while sprinting becomes the running attack when one is configured.
/// A held Block raises the guard as soon as Locomotion is free to leave.
/// </summary>
public class LocomotionState : PlayerState
{
    private LinearMixerState _freeMixer;
    private MixerState<Vector2> _strafeMixer;
    private bool _strafing;
    private bool _sprinting;
    private float _currentSpeed;
    private Vector2 _strafeParameter;
    private Vector2 _strafeParameterVelocity;

    public LocomotionState(PlayerStateMachine machine) : base(machine)
    {
    }

    public override void Enter()
    {
        Vector3 moveDirection = machine.GetCameraRelativeInput();
        _sprinting = machine.HasSprintIntent &&
                    moveDirection != Vector3.zero &&
                    machine.Stamina.CanSprint;
        PlayMixer(IsStrafing());
    }

    public override void Tick()
    {
        // A guard still held when another action ends comes back up without a new press.
        if (machine.InputReader.BlockHeld && machine.Block.TryResume())
        {
            return;
        }

        Vector3 moveDirection = machine.GetCameraRelativeInput();
        bool wantsSprint = machine.HasSprintIntent && moveDirection != Vector3.zero;
        _sprinting = wantsSprint && machine.Stamina.CanSprint && machine.Stamina.TrySpend(
            StaminaAction.Sprint,
            machine.Stamina.SprintCostPerSecond * Time.deltaTime);
        UpdateSpeed(moveDirection, _sprinting);

        bool strafe = IsStrafing();
        if (strafe != _strafing)
        {
            PlayMixer(strafe);
        }

        Vector3 velocity = moveDirection * _currentSpeed;
        machine.Motor.RotateToward(strafe ? machine.LockOn.PlanarDirectionToTarget : moveDirection);
        machine.Motor.Move(velocity);

        if (strafe)
        {
            _strafeParameter = Vector2.SmoothDamp(
                _strafeParameter,
                GetStrafeTarget(velocity),
                ref _strafeParameterVelocity,
                machine.AnimationData.LockedLocomotionSmoothTime);
            _strafeMixer.Parameter = _strafeParameter;
        }
        else
        {
            _freeMixer.Parameter = _currentSpeed;
        }
    }

    // Grounding is a Locomotion exit rule (see CanExit), not repeated here per action.
    public override bool CanExit() => machine.Motor.IsGrounded;

    public override bool OnInput(PlayerAction action) => machine.RequestAction(ResolveAction(action));

    private PlayerAction ResolveAction(PlayerAction action)
    {
        return action == PlayerAction.Attack && IsRunning() && machine.AnimationData.HasRunningAttack
            ? PlayerAction.RunningAttack
            : action;
    }

    // The speed check ignores an attack pressed while still accelerating out of a walk.
    private bool IsRunning()
    {
        return _sprinting && _currentSpeed > machine.MovementConfig.WalkSpeed;
    }

    // Sprinting while locked on breaks strafing (face-forward sprint), not the lock.
    private bool IsStrafing()
    {
        return machine.LockOn.IsLockedOn && !_sprinting;
    }

    private void PlayMixer(bool strafe)
    {
        _strafing = strafe;
        if (strafe)
        {
            _strafeMixer = (MixerState<Vector2>)machine.Animancer.Play(
                machine.AnimationData.LockedLocomotionMixer);
            // The mixer fades in already blended for the current movement, then eases from there.
            _strafeParameter = GetStrafeTarget(machine.GetCameraRelativeInput() * _currentSpeed);
            _strafeParameterVelocity = Vector2.zero;
            _strafeMixer.Parameter = _strafeParameter;
        }
        else
        {
            _freeMixer = (LinearMixerState)machine.Animancer.Play(
                machine.AnimationData.LocomotionMixer);
        }
    }

    // Local planar velocity in m/s (x = strafe, y = forward), the strafe mixer's parameter.
    private Vector2 GetStrafeTarget(Vector3 velocity)
    {
        Vector3 local = machine.Motor.transform.InverseTransformDirection(velocity);
        return new Vector2(local.x, local.z);
    }

    private void UpdateSpeed(Vector3 moveDirection, bool isSprinting)
    {
        MovementConfigData config = machine.MovementConfig;
        float maxSpeed = isSprinting ? config.SprintSpeed : config.WalkSpeed;
        float targetSpeed = moveDirection == Vector3.zero ? 0f : maxSpeed * moveDirection.magnitude;
        _currentSpeed = Mathf.MoveTowards(
            _currentSpeed,
            targetSpeed,
            config.SpeedChangeRate * Time.deltaTime);
    }
}
