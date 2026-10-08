using System.Text.RegularExpressions;

/// <summary>
/// A yes-or-no fact about the fight that a condition or consideration can be authored to answer.
/// </summary>
public enum BossSituation
{
    None,
    TargetHealing,
    TargetBehind,
    TargetRetreating,
    Retaliating,
    /// <summary>An attack could start from here once the boss has turned to its target and its wait is over, and nothing blocks the line to the target.</summary>
    AttackInReach,
    /// <summary>An attack is in reach and the target is inside the close-in distance, so the boss waits where it is.</summary>
    HoldingRange,
    /// <summary>The boss has strafe clips and a strafe speed, and no fight script is keeping it on its marks.</summary>
    AbleToStrafe,
    /// <summary>The boss has become enraged this attempt.</summary>
    Enraged,
    /// <summary>The wait after the last attack is over. Always holds while a fight script chooses the attacks.</summary>
    AttackDelayElapsed,
    /// <summary>A fight script is choosing the boss's attacks.</summary>
    AttacksScripted,
    /// <summary>The target has its guard raised.</summary>
    TargetGuarding,
    /// <summary>The target is in one of its own attacks.</summary>
    TargetAttacking,
    /// <summary>The target has run out of stamina and cannot dodge, block or attack until it recovers.</summary>
    TargetExhausted
}

public static class BossSituationExtensions
{
    /// <summary>The situation's name as words, for Inspector rows.</summary>
    public static string ToLabel(this BossSituation situation)
    {
        return Regex.Replace(situation.ToString(), "(?<!^)([A-Z])", " $1");
    }
}

/// <summary>
/// What the boss knows about the fight at the moment it makes a choice.
/// </summary>
public struct BossDecisionContext
{
    public float planarDistance;
    public float facingAngle;
    public bool targetHealing;
    public bool targetBehind;
    public bool targetRetreating;
    public bool retaliating;
    public bool targetGuarding;
    public bool targetAttacking;
    public bool targetExhausted;
    /// <summary>The ground between the boss and its target is walkable, so an attack made on foot can get there.</summary>
    public bool targetReachable;
    /// <summary>The boss's health as a share of its maximum, from 0 to 1.</summary>
    public float healthFraction;
    public bool attackInReach;
    public bool holdingRange;
    public bool ableToStrafe;
    public bool enraged;
    public bool attackDelayElapsed;
    public bool attacksScripted;

    public BossDecisionContext(float planarDistance, float facingAngle)
        : this(planarDistance, facingAngle, false, false, false, false)
    {
    }

    public BossDecisionContext(float planarDistance, float facingAngle, bool targetHealing, bool targetBehind, bool targetRetreating, bool retaliating,
        bool targetReachable = true, float healthFraction = 1f)
    {
        this = default;
        this.planarDistance = planarDistance;
        this.facingAngle = facingAngle;
        this.targetHealing = targetHealing;
        this.targetBehind = targetBehind;
        this.targetRetreating = targetRetreating;
        this.retaliating = retaliating;
        this.targetReachable = targetReachable;
        this.healthFraction = healthFraction;
        attackDelayElapsed = true;
    }

    /// <summary>
    /// The same fight as it would read once the boss faced its target with its wait over.
    /// An attack that scores above 0 in it is in reach: the boss only has to turn or wait.
    /// </summary>
    public readonly BossDecisionContext AsIfReady()
    {
        BossDecisionContext ready = this;
        ready.facingAngle = 0f;
        ready.attackDelayElapsed = true;
        return ready;
    }

    /// <summary>Whether a situation holds right now. <see cref="BossSituation.None"/> never holds.</summary>
    public readonly bool Holds(BossSituation situation)
    {
        return situation switch
        {
            BossSituation.TargetHealing => targetHealing,
            BossSituation.TargetBehind => targetBehind,
            BossSituation.TargetRetreating => targetRetreating,
            BossSituation.Retaliating => retaliating,
            BossSituation.AttackInReach => attackInReach,
            BossSituation.HoldingRange => holdingRange,
            BossSituation.AbleToStrafe => ableToStrafe,
            BossSituation.Enraged => enraged,
            BossSituation.AttackDelayElapsed => attackDelayElapsed,
            BossSituation.AttacksScripted => attacksScripted,
            BossSituation.TargetGuarding => targetGuarding,
            BossSituation.TargetAttacking => targetAttacking,
            BossSituation.TargetExhausted => targetExhausted,
            _ => false
        };
    }
}
