/// <summary>
/// Anything that can receive damage from the combat pipeline.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Resolves one hit and reports how it was resolved.
    /// </summary>
    HitResult TakeDamage(DamageInfo info);
}
