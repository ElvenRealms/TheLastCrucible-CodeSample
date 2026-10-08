using UnityEngine;

/// <summary>
/// Circles the target.
/// </summary>
[System.Serializable]
public class BossStrafeAction : BossAction
{
    public override string InspectorLabel => "Strafe";

    public override BossOption CreateOption(BossStateMachine machine, BossOptionEntry entry)
    {
        return new BossStrafeOption(machine, entry);
    }
}

/// <summary>
/// Circling the target, switching sides after a random
/// time or when the way is blocked.
/// </summary>
public class BossStrafeOption : BossOption
{
    // Keeps a boss boxed in on both sides from swapping strafe clips every frame.
    private const float MinimumBlockedSwitchInterval = 0.5f;

    // Zero while not strafing, otherwise -1 for left or 1 for right.
    private float _strafeSide;
    private float _strafeElapsed;
    private float _strafeDuration;

    public BossStrafeOption(BossStateMachine machine, BossOptionEntry entry) : base(machine, entry) { }

    public override bool IsSustained => true;

    // A return to pursuit forgets this option without stopping it, so the side can be stale.
    public override void Begin()
    {
        _strafeSide = 0f;
    }

    public override void Tick()
    {
        _strafeElapsed += Time.deltaTime;
        if (_strafeSide == 0f)
        {
            StartStrafing(Random.value < 0.5f ? -1f : 1f);
        }
        else if (_strafeElapsed >= _strafeDuration)
        {
            StartStrafing(-_strafeSide);
        }

        bool moved = machine.Motor.Strafe(machine.PlanarDirectionToTarget, _strafeSide, machine.MovementConfig.strafeSpeed);
        if (!moved && _strafeElapsed >= MinimumBlockedSwitchInterval)
        {
            StartStrafing(-_strafeSide);
        }
    }

    public override void Stop()
    {
        if (_strafeSide == 0f)
        {
            return;
        }

        _strafeSide = 0f;
        machine.Animation.PlayLocomotion(machine.LocomotionMixer);
    }

    private void StartStrafing(float side)
    {
        BossMovementConfigData config = machine.MovementConfig;
        _strafeSide = side;
        _strafeElapsed = 0f;
        _strafeDuration = Random.Range(config.strafeMinDuration, Mathf.Max(config.strafeMinDuration, config.strafeMaxDuration));
        machine.Animation.PlayStrafe(side < 0f ? machine.StrafeLeftClip : machine.StrafeRightClip, side);
        machine.LogDebug($"Strafing {(side < 0f ? "left" : "right")} for up to {_strafeDuration:F1}s.");
    }
}
