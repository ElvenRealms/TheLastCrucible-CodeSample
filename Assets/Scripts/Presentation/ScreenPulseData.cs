using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Maps accepted damage to the peak weight of a post-processing volume punch, with a lethal override.
/// </summary>
[CreateAssetMenu(fileName = "ScreenPulse", menuName = "BossFight/Screen Pulse Data")]
public class ScreenPulseData : CombatFeedbackEffectData
{
    [BoxGroup("Volume"), Required, Tooltip("Post-processing overrides blended in while the pulse plays.")]
    public VolumeProfile profile;
    [BoxGroup("Volume"), Tooltip("Volume priority. Higher priority volumes override lower ones.")]
    public int priority = 50;

    [BoxGroup("Normal Hits"), ValidateInput(nameof(WeightCurveValid), "weightByDamage must contain at least one key.")]
    [Tooltip("Peak volume weight (0 to 1) by accepted damage.")]
    public AnimationCurve weightByDamage = new(
        new Keyframe(0f, 0.25f),
        new Keyframe(30f, 0.7f));

    [BoxGroup("Lethal Hits"), Range(0f, 1f)]
    public float lethalWeight = 1f;

    [BoxGroup("Envelope"), MinValue(0f), Tooltip("Seconds to ease up to the peak weight.")]
    public float attack = 0.03f;
    [BoxGroup("Envelope"), MinValue(0f), Tooltip("Seconds to hold the peak weight.")]
    public float hold = 0.04f;
    [BoxGroup("Envelope"), MinValue(0f), Tooltip("Seconds to ease back to zero.")]
    public float release = 0.35f;

    private bool WeightCurveValid => weightByDamage != null && weightByDamage.length > 0;

    /// <summary>
    /// Returns the peak volume weight for a hit, clamped to 0..1.
    /// </summary>
    public float GetPeakWeight(float damage, bool lethal)
    {
        if (lethal) return Mathf.Clamp01(lethalWeight);
        if (!WeightCurveValid) return 0f;
        return Mathf.Clamp01(weightByDamage.Evaluate(Mathf.Max(0f, damage)));
    }
}
