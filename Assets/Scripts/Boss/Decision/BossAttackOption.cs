using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Starts one attack sequence.
/// </summary>
[System.Serializable]
public class BossAttackAction : BossAction
{
    [SerializeField, InlineProperty, HideLabel]
    private BossAttackDefinition attack = new();

    public BossAttackDefinition Attack => attack;
    public override string InspectorLabel => attack.InspectorLabel;

    public BossAttackAction()
    {
    }

    public BossAttackAction(BossAttackDefinition attack)
    {
        this.attack = attack;
    }

    public override BossOption CreateOption(BossStateMachine machine, BossOptionEntry entry)
    {
        return new BossAttackOption(machine, entry, attack);
    }
}

/// <summary>
/// Starting one attack. The boss can still pass on it at its start chance, which sets it
/// aside for a while and leaves the boss moving. While a fight script chooses the attacks,
/// only the one it queued is on offer, and that one needs only its range and facing.
/// </summary>
public class BossAttackOption : BossOption
{
    private readonly BossAttackDefinition _attack;
    private readonly BossOptionScoring _scoring;

    public BossAttackDefinition Attack => _attack;
    public BossOptionScoring Scoring => _scoring;

    public BossAttackOption(BossStateMachine machine, BossOptionEntry entry, BossAttackDefinition attack) : base(machine, entry)
    {
        _attack = attack;
        _scoring = entry.Scoring;
    }

    public override float Score(in BossDecisionContext context)
    {
        if (!_attack.SequenceValid)
        {
            return 0f;
        }
        if (!machine.AreAttacksScripted)
        {
            return base.Score(context);
        }
        return machine.ScriptedOption == this && Holds<BossRangeCondition>(context) && Holds<BossFacingCondition>(context) ? 1f : 0f;
    }

    // Two attacks that open with the same clip read as one move until the swing lands.
    public override bool OpensLike(BossOption other)
    {
        return other == this || (other is BossAttackOption attack && _attack.SequenceValid && attack._attack.SequenceValid &&
            attack._attack.GetStep(0).AnimationClip == _attack.GetStep(0).AnimationClip);
    }

    public override string Describe(in BossDecisionContext context)
    {
        if (!_attack.SequenceValid)
        {
            return "sequence not valid";
        }
        if (!machine.AreAttacksScripted)
        {
            return base.Describe(context);
        }
        return machine.ScriptedOption == this ? "queued by the fight script" : "attacks scripted";
    }

    /// <summary>Whether this attack could start from here once the boss has turned to its target and its wait is over.</summary>
    public bool IsInReach(in BossDecisionContext context)
    {
        if (!_attack.SequenceValid)
        {
            return false;
        }
        return machine.ScriptedOption == this ? Holds<BossRangeCondition>(context) : base.Score(context.AsIfReady()) > 0f;
    }

    /// <summary>Whether this attack belongs to the phase the boss is in: its conditions on being enraged hold.</summary>
    public bool IsInPhase(in BossDecisionContext context)
    {
        foreach (var condition in _scoring.Conditions)
        {
            if (condition is BossSituationCondition situation && situation.Situation == BossSituation.Enraged && !situation.IsMet(context, Memory))
            {
                return false;
            }
        }
        return true;
    }

    public override bool TryStart()
    {
        if (!machine.AreAttacksScripted && !Commits(machine.Brain.Context))
        {
            machine.LogDebug($"Passed on '{Name}'.");
            return false;
        }
        return machine.StartAttack(_attack);
    }

    // An attack answering the fight as it stands always goes ahead, whatever its start chance.
    private bool Commits(in BossDecisionContext context)
    {
        if (_attack.Commits(_scoring.Favours(context), Random.value))
        {
            return true;
        }

        Memory.SetAside(_attack.PassDuration);
        return false;
    }

    // True when the attack has no condition of the type.
    private bool Holds<T>(in BossDecisionContext context) where T : BossCondition
    {
        foreach (var condition in _scoring.Conditions)
        {
            if (condition is T && !condition.IsMet(context, Memory))
            {
                return false;
            }
        }
        return true;
    }
}
