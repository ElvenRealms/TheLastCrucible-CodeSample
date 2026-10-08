using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Shared healing item charge and recovery tuning for the player.
/// </summary>
[CreateAssetMenu(fileName = "HealingItemConfig", menuName = "BossFight/Healing Item Config")]
public class HealingItemConfigData : ScriptableObject
{
    [BoxGroup("Capacity"), MinValue(0), Tooltip("Healing item charges the player starts each attempt with.")]
    public int maxCharges = 3;

    [BoxGroup("Recovery"), MinValue(0f), SuffixLabel("health"), Tooltip("Total health restored once a Heal Window completes uninterrupted.")]
    public float healAmount = 40f;
    [BoxGroup("Recovery"), MinValue(0f), SuffixLabel("s"), Tooltip("Time to apply the full heal amount. Healing lands incrementally over this window, so an interrupt keeps whatever healed so far.")]
    public float useDuration = 0.9f;

    [BoxGroup("Movement"), Range(0f, 1f), Tooltip("Fraction of walk speed the player moves at while a Heal Window is open. Zero holds the player still.")]
    public float moveSpeedMultiplier = 0.4f;
}
