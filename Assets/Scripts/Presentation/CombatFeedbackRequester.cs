using UnityEngine;

/// <summary>
/// Converts accepted damage on the sibling Health into a scene-level combat
/// feedback request.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class CombatFeedbackRequester : CombatFeedbackRequesterBase
{
    private Health health;

    protected override void Awake()
    {
        base.Awake();
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        health.Damaged += HandleDamaged;
    }

    private void OnDisable()
    {
        health.Damaged -= HandleDamaged;
    }

    private void HandleDamaged(DamageInfo info)
    {
        Forward(info.amount, health.IsDead, info.source, info.hitPoint);
    }
}
