/// <summary>
/// How a target resolved one incoming hit, reported back to whoever dealt it.
/// </summary>
public enum HitResult
{
    /// <summary>The hit landed and Health raised Damaged, even when god mode preserved health.</summary>
    Applied,
    /// <summary>A qualifying Parry negated the hit.</summary>
    Parried,
    /// <summary>A raised guard stopped the hit at a stamina cost.</summary>
    Blocked,
    /// <summary>Dodge invulnerability negated the hit.</summary>
    Evaded,
    /// <summary>The hit had no effect: an invalid amount, an already dead target, or damage disabled.</summary>
    Ignored
}
