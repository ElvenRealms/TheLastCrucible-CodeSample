/// <summary>
/// PlayMode test groups, applied to fixtures as NUnit categories so a change runs only the
/// groups it touches (for example, filter the Test Runner by the Boss and Combat categories).
/// Every fixture belongs to exactly one group.
/// </summary>
public static class TestGroups
{
    /// <summary>Player stamina.</summary>
    public const string Player = "Player";
    /// <summary>Health, block and parry.</summary>
    public const string Combat = "Combat";
    /// <summary>Boss states, attack selection and stagger.</summary>
    public const string Boss = "Boss";
    /// <summary>Time scale.</summary>
    public const string Feedback = "Feedback";
}
