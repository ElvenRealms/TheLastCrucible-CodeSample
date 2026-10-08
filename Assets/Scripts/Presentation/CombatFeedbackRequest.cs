using UnityEngine;

/// <summary>
/// Runtime context paired with an authored combat feedback profile.
/// </summary>
public readonly struct CombatFeedbackRequest
{
    public readonly CombatFeedbackProfile profile;
    public readonly float damage;
    public readonly bool lethal;
    public readonly Transform source;
    public readonly Transform receiver;
    public readonly Vector3 position;

    public CombatFeedbackRequest(
        CombatFeedbackProfile profile,
        float damage,
        bool lethal,
        Transform source,
        Transform receiver,
        Vector3 position)
    {
        this.profile = profile;
        this.damage = damage;
        this.lethal = lethal;
        this.source = source;
        this.receiver = receiver;
        this.position = position;
    }
}
