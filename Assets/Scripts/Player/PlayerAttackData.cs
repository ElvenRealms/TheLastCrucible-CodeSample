using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// One player attack swing with stamina, buffering, combo, and cancellation timing.
/// </summary>
[CreateAssetMenu(fileName = "PlayerAttack", menuName = "BossFight/Attacks/Player Attack")]
public sealed class PlayerAttackData : AttackDataBase
{
    [BoxGroup("Player Input"), SerializeField, MinValue(0f)]
    [Tooltip("Stamina spent when this swing begins.")]
    private float staminaCost = 20f;
    [BoxGroup("Player Input"), SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time from which another attack press buffers.")]
    private float bufferWindowStart = 0.35f;
    [BoxGroup("Player Input"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(IsComboWindowOrdered), "Combo Window Start must be greater than or equal to Buffer Window Start.")]
    [Tooltip("Normalized time from which a queued press of the same attack chains into the next combo step.")]
    private float comboWindowStart = 0.8f;
    [BoxGroup("Player Input"), SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time from which another action may cancel this swing.")]
    private float cancelWindowStart = 0.8f;

    public float StaminaCost => staminaCost;
    public float BufferWindowStart => bufferWindowStart;
    public float ComboWindowStart => comboWindowStart;
    public float CancelWindowStart => cancelWindowStart;
    public bool IsPlayerValid => IsValid && staminaCost >= 0f &&
        bufferWindowStart >= 0f && bufferWindowStart <= 1f &&
        comboWindowStart >= 0f && comboWindowStart <= 1f &&
        cancelWindowStart >= 0f && cancelWindowStart <= 1f && IsComboWindowOrdered;

    private bool IsComboWindowOrdered => comboWindowStart >= bufferWindowStart;
}
