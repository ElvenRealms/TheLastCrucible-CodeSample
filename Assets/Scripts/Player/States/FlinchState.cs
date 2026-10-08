using Animancer;
using UnityEngine;

/// <summary>
/// The player's involuntary reaction to its own Poise Break or to a broken guard.
/// Committed: it outranks every other action, closes the hitbox, slides briefly
/// away from the attacker that caused it, and plays a single clip before releasing
/// control. Its authored tail is the machine's shared Input Buffer, open from
/// FlinchInputBufferStart until the clip ends.
/// </summary>
public class FlinchState : PlayerState
{
    private readonly KnockbackSlide _knockback = new();
    private AnimancerState _currentAnimState;
    private bool _isGuardBreak;
    private Transform _guardBreakAttacker;

    public FlinchState(PlayerStateMachine machine) : base(machine)
    {
    }

    public override bool CanEnter() => !machine.Health.IsDead && machine.AnimationData.IsFlinchConfigured;

    public override bool CanExit() => false;

    public override PlayerActionPriority Priority => PlayerActionPriority.High;

    public override bool CanBufferInput =>
        _currentAnimState.NormalizedTime >= machine.AnimationData.FlinchInputBufferStart;

    /// <summary>
    /// Enters as a guard break: it plays the guard-break clip and slides away from
    /// <paramref name="attacker"/>, because no Poise Break names one.
    /// </summary>
    public bool TryStartGuardBreak(Transform attacker)
    {
        _isGuardBreak = true;
        _guardBreakAttacker = attacker;
        bool started = machine.TryStartAction(this);
        _isGuardBreak = false;
        _guardBreakAttacker = null;
        return started;
    }

    public override void Enter()
    {
        PlayerAnimationData data = machine.AnimationData;
        Transform attacker = _isGuardBreak ? _guardBreakAttacker : machine.Poise.LastBreakAttacker;
        ClipTransition clip = _isGuardBreak && data.IsGuardBreakConfigured ? data.GuardBreak : data.Flinch;

        machine.Poise.Suspend();
        machine.Hitbox.EndWindow();
        machine.Motor.Move(Vector3.zero);
        _knockback.StartAwayFrom(attacker, machine.transform.position, data.FlinchKnockbackDistance, data.FlinchKnockbackDuration);
        _currentAnimState = machine.Animancer.Play(clip);
        _currentAnimState.NormalizedTime = 0f;
        _currentAnimState.Events(this).OnEnd = OnFlinchEnd;
    }

    // Move runs every frame even after the slide, because the motor only applies
    // gravity and grounding inside it.
    public override void Tick()
    {
        machine.Motor.Move(_knockback.Advance(Time.deltaTime));
    }

    public override void Exit()
    {
        _currentAnimState.Events(this).OnEnd = null;
        machine.Poise.RestoreFull();
        machine.Poise.Resume();
    }

    private void OnFlinchEnd()
    {
        machine.CompleteAction();
    }
}
