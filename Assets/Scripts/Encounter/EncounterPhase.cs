/// <summary>
/// Lifecycle phase of the boss encounter, owned by <see cref="ArenaManager"/>.
/// </summary>
public enum EncounterPhase
{
    /// <summary>Waiting for a living player to activate the encounter.</summary>
    Idle,
    /// <summary>An attempt is in progress.</summary>
    Active,
    /// <summary>The boss is dead; the encounter resets to Idle after the retry delay.</summary>
    Won
}
