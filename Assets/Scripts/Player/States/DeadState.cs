using Animancer;
using UnityEngine;

/// <summary>
/// Plays the death clip and ignores input. After the clip ends, holds the
/// corpse pose for the configured delay before asking the machine to respawn.
/// Root motion still goes through the motor so the CharacterController keeps
/// handling collision.
/// </summary>
public class DeadState : PlayerState
{
    private AnimancerState currentAnimState;
    private float respawnTime;
    private bool waitingToRespawn;

    public DeadState(PlayerStateMachine machine) : base(machine)
    {
    }

    public override bool CanExit() => false;

    // Death itself still arrives via ForceChangeState, which bypasses this
    // gate entirely; High makes the ordering visible in code and admits a
    // future TryStartAction(Dead) caller through any window.
    public override PlayerActionPriority Priority => PlayerActionPriority.High;

    public override void Enter()
    {
        Debug.Log("Player died.", machine);
        machine.Hitbox.EndWindow();
        machine.LockOn.Release();
        waitingToRespawn = false;
        currentAnimState = machine.Animancer.Play(machine.AnimationData.Death);
        currentAnimState.NormalizedTime = 0f;
        currentAnimState.Events(this).OnEnd = HandleDeathAnimationEnded;
    }

    public override void Tick()
    {
        if (!waitingToRespawn || Time.time < respawnTime) return;

        waitingToRespawn = false;
        machine.Respawn();
    }

    public override void OnRootMotion(Vector3 deltaPosition)
    {
        machine.Motor.MoveWithRootMotion(deltaPosition);
    }

    public override void Exit()
    {
        if (currentAnimState != null) currentAnimState.Events(this).OnEnd = null;
        waitingToRespawn = false;
    }

    private void HandleDeathAnimationEnded()
    {
        currentAnimState.Events(this).OnEnd = null;
        respawnTime = Time.time + machine.AnimationData.RespawnDelay;
        waitingToRespawn = true;
    }
}