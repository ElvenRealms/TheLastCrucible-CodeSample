using UnityEngine;

/// <summary>
/// Payload for a single hit: how much damage, what dealt it, and where it
/// landed (for VFX, knockback direction, and AI aggro). <see cref="source"/>
/// is the object that made contact, such as a weapon hitbox;
/// <see cref="attacker"/> is the actor that owns it, when known.
/// </summary>
public readonly struct DamageInfo
{
    public readonly float amount;
    public readonly Transform source;
    public readonly Vector3 hitPoint;
    public readonly bool parryable;
    public readonly float poiseDamage;
    public readonly Transform attacker;
    public readonly bool blockable;

    public DamageInfo(float amount, Transform source, Vector3 hitPoint, bool parryable = false, float poiseDamage = 0f, Transform attacker = null, bool blockable = false)
    {
        this.amount = amount;
        this.source = source;
        this.hitPoint = hitPoint;
        this.parryable = parryable;
        this.poiseDamage = poiseDamage;
        this.attacker = attacker;
        this.blockable = blockable;
    }

    /// <summary>Changes health damage while preserving the hit's source and defence rules.</summary>
    public DamageInfo WithAmount(float value)
    {
        return new DamageInfo(value, source, hitPoint, parryable, poiseDamage, attacker, blockable);
    }
}
