using UnityEngine;

/// <summary>
/// Works out how far, and which way, a landed hit pushes the boss along the ground.
/// </summary>
public static class BossHitShove
{
    private const float MinimumDirectionSquared = 0.0001f;

    /// <summary>
    /// Planar displacement for one hit on a body standing at <paramref name="bodyPosition"/>.
    /// The distance grows with the hit's Poise Damage; the direction blends the push away
    /// from the attacker with the push away from the point that was struck. Zero when the
    /// hit is too light or has no usable direction.
    /// </summary>
    public static Vector3 Resolve(DamageInfo info, Vector3 bodyPosition, BossMovementConfigData config)
    {
        float strength = Mathf.InverseLerp(config.hitShoveMinPoiseDamage, config.hitShoveFullPoiseDamage, info.poiseDamage);
        if (strength <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 fromHitPoint = PlanarDirection(info.hitPoint, bodyPosition);
        Transform attacker = info.attacker != null ? info.attacker : info.source;
        Vector3 fromAttacker = attacker != null ? PlanarDirection(attacker.position, bodyPosition) : fromHitPoint;
        if (fromHitPoint == Vector3.zero)
        {
            fromHitPoint = fromAttacker;
        }

        Vector3 direction = Vector3.Lerp(fromAttacker, fromHitPoint, config.hitShoveHitPointBias);
        // Opposed directions cancel in the blend; the attacker's side then decides.
        direction = direction.sqrMagnitude > MinimumDirectionSquared ? direction.normalized : fromAttacker;
        return direction * (config.hitShoveDistance * strength);
    }

    private static Vector3 PlanarDirection(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        return direction.sqrMagnitude > MinimumDirectionSquared ? direction.normalized : Vector3.zero;
    }
}
