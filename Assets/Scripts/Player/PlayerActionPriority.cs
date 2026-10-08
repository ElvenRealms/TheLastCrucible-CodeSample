/// <summary>
/// The rank of a player action; a higher-ranked incoming action interrupts
/// the active one regardless of its timing window, while equal or lower rank
/// waits for that window.
/// </summary>
public enum PlayerActionPriority
{
    Low,
    Medium,
    High
}
