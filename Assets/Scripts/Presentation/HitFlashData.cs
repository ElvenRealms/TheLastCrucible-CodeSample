using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Tuning for a material emission flash applied on hit, with a lethal override.
/// </summary>
[CreateAssetMenu(fileName = "HitFlash", menuName = "BossFight/Hit Flash Data")]
public class HitFlashData : CombatFeedbackEffectData
{
    public Color flashColor = Color.white;
    [MinValue(0f)]
    public float duration = 0.08f;
    [MinValue(0f)]
    public float lethalDuration = 0.2f;
    [MinValue(0f), Tooltip("Multiplier applied to the emission color.")]
    public float intensity = 1f;

    public float GetDuration(bool lethal)
    {
        return Mathf.Max(0f, lethal ? lethalDuration : duration);
    }
}
