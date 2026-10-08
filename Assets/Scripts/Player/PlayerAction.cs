/// <summary>
/// A player-requested action that can be buffered or routed through the
/// state machine's request/transition gates.
/// </summary>
public enum PlayerAction
{
    Attack,
    Dodge,
    Block,
    Heal,
    HeavyAttack,
    RunningAttack
}
