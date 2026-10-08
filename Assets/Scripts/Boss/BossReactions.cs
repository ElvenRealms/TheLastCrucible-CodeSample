using UnityEngine;

/// <summary>
/// Reads the fight for the boss: whether the target is healing, guarding, attacking,
/// out of stamina, behind it or backing away, and how many hits the boss has taken since it last attacked.
/// The boss's options are weighed by this, and a heal or a run of hits
/// tells the boss to stop waiting. Plain C#; owned and driven by BossStateMachine.
/// </summary>
public class BossReactions
{
    // Smooths the target's frame-to-frame speed so one dodge step does not read as a retreat.
    private const float RetreatSmoothing = 8f;

    private readonly BossStateMachine _machine;
    private Vector3 _lastTargetPosition;
    private bool _hasLastTargetPosition;
    private bool _targetWasHealing;
    private int _hitsTaken;
    private int _hitsUntilRetaliation;
    private float _lastHitTime;

    /// <summary>Speed at which the target is moving away from the boss; negative while it approaches.</summary>
    public float TargetRetreatSpeed { get; private set; }
    /// <summary>True from the hit that provoked the boss until its next attack starts.</summary>
    public bool IsRetaliating { get; private set; }

    public BossReactions(BossStateMachine machine)
    {
        _machine = machine;
    }

    /// <summary>Forgets everything read so far, for a new attempt or a new phase.</summary>
    public void Reset()
    {
        _hasLastTargetPosition = false;
        _targetWasHealing = false;
        TargetRetreatSpeed = 0f;
        IsRetaliating = false;
        _hitsTaken = 0;
        _hitsUntilRetaliation = SampleHitsUntilRetaliation();
    }

    /// <summary>
    /// Updates the read of the target. True on the frame the target starts a
    /// heal that the boss chooses to answer, so its wait should end.
    /// </summary>
    public bool Tick(float deltaTime)
    {
        UpdateRetreatSpeed(deltaTime);

        bool healing = _machine.IsTargetHealing;
        bool healStarted = healing && !_targetWasHealing;
        _targetWasHealing = healing;
        return healStarted && Random.value < _machine.Pacing.HealReactionChance;
    }

    /// <summary>Counts one hit taken between attacks. True when it provokes a retaliation, so the boss's wait should end.</summary>
    public bool RegisterHit()
    {
        if (_hitsUntilRetaliation <= 0)
        {
            return false;
        }

        if (Time.time - _lastHitTime > _machine.Pacing.RetaliationWindow)
        {
            _hitsTaken = 0;
        }
        _lastHitTime = Time.time;
        _hitsTaken++;
        if (_hitsTaken < _hitsUntilRetaliation)
        {
            return false;
        }

        _hitsTaken = 0;
        _hitsUntilRetaliation = SampleHitsUntilRetaliation();
        IsRetaliating = true;
        return true;
    }

    /// <summary>An attack answers whatever provoked it, so the count starts again.</summary>
    public void NotifyAttackStarted()
    {
        IsRetaliating = false;
        _hitsTaken = 0;
    }

    public BossDecisionContext CreateContext(float planarDistance, float facingAngle, bool targetReachable)
    {
        BossPacing pacing = _machine.Pacing;
        bool behind = facingAngle > pacing.BehindAngle;
        bool retreating = TargetRetreatSpeed > pacing.RetreatSpeed;
        Health health = _machine.Health;
        float healthFraction = health.MaxHealth > 0f ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth) : 0f;
        return new BossDecisionContext(planarDistance, facingAngle, _machine.IsTargetHealing, behind, retreating, IsRetaliating, targetReachable, healthFraction)
        {
            targetGuarding = _machine.IsTargetGuarding,
            targetAttacking = _machine.IsTargetAttacking,
            targetExhausted = _machine.IsTargetExhausted
        };
    }

    private void UpdateRetreatSpeed(float deltaTime)
    {
        Transform target = _machine.Target;
        if (target == null || deltaTime <= 0f)
        {
            _hasLastTargetPosition = false;
            TargetRetreatSpeed = 0f;
            return;
        }

        Vector3 position = target.position;
        if (_hasLastTargetPosition)
        {
            Vector3 away = _machine.PlanarDirectionToTarget.normalized;
            float speed = Vector3.Dot((position - _lastTargetPosition) / deltaTime, away);
            TargetRetreatSpeed = Mathf.Lerp(TargetRetreatSpeed, speed, 1f - Mathf.Exp(-RetreatSmoothing * deltaTime));
        }
        _lastTargetPosition = position;
        _hasLastTargetPosition = true;
    }

    private int SampleHitsUntilRetaliation()
    {
        return _machine.Pacing.SampleRetaliationHits();
    }
}
