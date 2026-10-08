using UnityEngine;

/// <summary>
/// Base class for player states. Plain C#; created and driven by PlayerStateMachine.
/// </summary>
public abstract class PlayerState
{
    protected readonly PlayerStateMachine machine;

    protected PlayerState(PlayerStateMachine machine)
    {
        this.machine = machine;
    }

    public virtual void Enter()
    {
    }

    public virtual void Tick()
    {
    }

    public virtual void Exit()
    {
    }

    // Cross-state actions go through the machine's gate. Heal is opt-in: only a
    // state that overrides this may forward it (Locomotion), so Heal never cancels
    // or buffers out of a committed action.
    public virtual bool OnInput(PlayerAction action)
    {
        return action != PlayerAction.Heal && machine.RequestAction(action);
    }

    public virtual void OnRootMotion(Vector3 deltaPosition)
    {
    }

    public virtual bool CanEnter() => true; // destination's own prerequisites
    public virtual bool CanExit() => true; // source's destination-independent timing window
    public virtual bool CanBufferInput => false; // source's authored late-input window is open
    public virtual PlayerActionPriority Priority => PlayerActionPriority.Medium;
}