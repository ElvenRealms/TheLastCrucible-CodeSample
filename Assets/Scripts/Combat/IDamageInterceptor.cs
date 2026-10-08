/// <summary>
/// Sibling component that may negate a hit before Health applies it (dodge i-frames, block, parry).
/// </summary>
public interface IDamageInterceptor
{
    /// <summary>
    /// Returns the outcome when this interceptor negates the hit, or null to let it through.
    /// </summary>
    HitResult? Intercept(DamageInfo info);
}
