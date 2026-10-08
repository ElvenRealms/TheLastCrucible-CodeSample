using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Shared stamina capacity, recovery, and action-cost tuning for the player.
/// </summary>
[CreateAssetMenu(fileName = "StaminaConfig", menuName = "BossFight/Stamina Config")]
public class StaminaConfigData : ScriptableObject
{
    [BoxGroup("Capacity"), SerializeField, MinValue(0f), SuffixLabel("stamina")]
    private float maxStamina = 100f;

    [BoxGroup("Recovery"), SerializeField, MinValue(0f), SuffixLabel("stamina/s")]
    private float recoveryPerSecond = 24f;
    [BoxGroup("Recovery"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float recoveryDelay = 0.8f;
    [BoxGroup("Recovery"), SerializeField, MinValue(0f)]
    [ValidateInput(nameof(IsSprintResumeThresholdValid), "sprintResumeThreshold must not exceed maxStamina.")]
    [Tooltip("Stamina required before sprint can resume after exhaustion.")]
    private float sprintResumeThreshold = 20f;

    [BoxGroup("Costs"), SerializeField, MinValue(0f), SuffixLabel("stamina/s")]
    private float sprintCostPerSecond = 18f;
    [BoxGroup("Costs"), SerializeField, MinValue(0f), SuffixLabel("stamina")]
    private float dodgeCost = 25f;
    [BoxGroup("Costs"), SerializeField, MinValue(0f), SuffixLabel("stamina per damage")]
    [Tooltip("Stamina a blocked hit costs for each point of damage it would have dealt.")]
    private float blockCostPerDamage = 0.8f;

    public float MaxStamina => maxStamina;
    public float RecoveryPerSecond => recoveryPerSecond;
    public float RecoveryDelay => recoveryDelay;
    public float SprintResumeThreshold => sprintResumeThreshold;
    public float SprintCostPerSecond => sprintCostPerSecond;
    public float DodgeCost => dodgeCost;
    public float BlockCostPerDamage => blockCostPerDamage;

    private bool IsSprintResumeThresholdValid => sprintResumeThreshold <= maxStamina;
}
