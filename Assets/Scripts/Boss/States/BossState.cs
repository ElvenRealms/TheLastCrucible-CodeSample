/// <summary>
/// Base class for boss states. Plain C#; created and driven by BossStateMachine.
/// </summary>
public abstract class BossState
{
    protected readonly BossStateMachine machine;

    protected BossState(BossStateMachine machine)
    {
        this.machine = machine;
    }

    public virtual void Enter() { }
    public virtual void Tick() { }
    public virtual void Exit() { }
}
