/// <summary>
/// Plays one authored in-place turn clip; BossMotor routes the clip's root
/// yaw through OnAnimatorMove, rescaled each frame to land on a live target
/// heading instead of the clip's own authored angle. Combat pivots track the
/// target directly and navigation turns track the path, both within a bounded
/// correction. Finishes once the boss faces the target heading, or
/// when the clip completes, whichever comes first.
/// </summary>
public class BossTurnState : BossState
{
    private BossTurnDefinition preparedTurn;
    private float preparedRequestedSignedAngle;
    private bool _preparedCombatPivot;
    private bool _isCombatPivot;

    public BossTurnState(BossStateMachine machine) : base(machine) { }

    public void Prepare(BossTurnDefinition turn, float requestedSignedAngle, bool isCombatPivot)
    {
        preparedTurn = turn ?? throw new System.ArgumentNullException(nameof(turn));
        preparedRequestedSignedAngle = requestedSignedAngle;
        _preparedCombatPivot = isCombatPivot;
    }

    public override void Enter()
    {
        BossTurnDefinition turn = preparedTurn ?? throw new System.InvalidOperationException("BossTurnState requires a prepared turn before entry.");
        float requestedSignedAngle = preparedRequestedSignedAngle;
        _isCombatPivot = _preparedCombatPivot;
        preparedTurn = null;
        _preparedCombatPivot = false;
        machine.Motor.HoldNavigation();
        machine.Motor.BeginTurn(requestedSignedAngle, turn.signedAngle, machine.MovementConfig.turnMaxTargetAdjustment);
        machine.LogDebug($"Turn started (clip {turn.signedAngle:F0} deg, requested {requestedSignedAngle:F0} deg).");
        machine.Animation.PlayTurn(turn.clip.Transition, machine.MovementConfig.turnSpeedMultiplier, HandleTurnCompleted);
    }

    public override void Tick()
    {
        if (machine.Target != null)
        {
            machine.Motor.UpdateTurnTarget(_isCombatPivot
                ? machine.PlanarDirectionToTarget
                : machine.Motor.TrackPathDirection(machine.Target));
        }

        if (machine.Motor.RemainingTurnAngle <= machine.MovementConfig.turnExitAngle)
        {
            machine.FinishAction();
        }
    }

    public override void Exit()
    {
        // Owns ending the turn for every exit path, normal completion and
        // early ones alike (death, deactivation mid-turn): EndTurn only
        // clears turn state and never snaps the transform, so it is safe to
        // call here regardless of how the state is being left.
        machine.Motor.EndTurn();
        machine.Animation.ClearCompletion();
        preparedTurn = null;
        preparedRequestedSignedAngle = 0f;
        _preparedCombatPivot = false;
        _isCombatPivot = false;
    }

    private void HandleTurnCompleted()
    {
        // Leaving the state calls Exit(), which ends the turn; no need to
        // call EndTurn() again here.
        machine.FinishAction();
    }
}
