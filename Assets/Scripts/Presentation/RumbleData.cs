using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Maps accepted damage to low and high frequency gamepad motor strength, with lethal overrides.
/// </summary>
[CreateAssetMenu(fileName = "Rumble", menuName = "BossFight/Rumble Data")]
public class RumbleData : CombatFeedbackEffectData
{
    [BoxGroup("Normal Hits"), ValidateInput(nameof(LowCurveValid), "lowFrequencyByDamage must contain at least one key.")]
    [Tooltip("Low frequency (heavy) motor strength, 0 to 1, by accepted damage.")]
    public AnimationCurve lowFrequencyByDamage = new(
        new Keyframe(0f, 0.2f),
        new Keyframe(30f, 0.6f));
    [BoxGroup("Normal Hits"), ValidateInput(nameof(HighCurveValid), "highFrequencyByDamage must contain at least one key.")]
    [Tooltip("High frequency (light) motor strength, 0 to 1, by accepted damage.")]
    public AnimationCurve highFrequencyByDamage = new(
        new Keyframe(0f, 0.1f),
        new Keyframe(30f, 0.4f));

    [BoxGroup("Lethal Hits"), Range(0f, 1f)]
    public float lethalLowFrequency = 1f;
    [BoxGroup("Lethal Hits"), Range(0f, 1f)]
    public float lethalHighFrequency = 0.8f;

    [BoxGroup("Timing"), MinValue(0f), Tooltip("Unscaled seconds the rumble lasts. Motors fade out over the last 40 percent.")]
    public float duration = 0.18f;

    private bool LowCurveValid => lowFrequencyByDamage != null && lowFrequencyByDamage.length > 0;
    private bool HighCurveValid => highFrequencyByDamage != null && highFrequencyByDamage.length > 0;

    /// <summary>
    /// Returns the low frequency motor strength for a hit, clamped to 0..1.
    /// </summary>
    public float GetLowFrequency(float damage, bool lethal)
    {
        if (lethal) return Mathf.Clamp01(lethalLowFrequency);
        if (!LowCurveValid) return 0f;
        return Mathf.Clamp01(lowFrequencyByDamage.Evaluate(Mathf.Max(0f, damage)));
    }

    /// <summary>
    /// Returns the high frequency motor strength for a hit, clamped to 0..1.
    /// </summary>
    public float GetHighFrequency(float damage, bool lethal)
    {
        if (lethal) return Mathf.Clamp01(lethalHighFrequency);
        if (!HighCurveValid) return 0f;
        return Mathf.Clamp01(highFrequencyByDamage.Evaluate(Mathf.Max(0f, damage)));
    }
}
