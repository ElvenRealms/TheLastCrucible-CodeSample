using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Maps accepted damage to a short hit-stop duration, with a lethal override.
/// </summary>
[CreateAssetMenu(fileName = "HitStop", menuName = "BossFight/Hit Stop Data")]
public class HitStopData : CombatFeedbackEffectData
{
    [BoxGroup("Normal Hits"), ValidateInput(nameof(DurationCurveValid), "durationByDamage must contain at least one key.")]
    public AnimationCurve durationByDamage = new(
        new Keyframe(0f, 0.02f),
        new Keyframe(30f, 0.06f));

    [BoxGroup("Lethal Hits"), MinValue(0f)]
    public float lethalDuration = 0.1f;

    private bool DurationCurveValid => durationByDamage != null && durationByDamage.length > 0;

    public float GetDuration(float damage, bool lethal)
    {
        if (lethal) return Mathf.Max(0f, lethalDuration);
        if (!DurationCurveValid) return 0f;
        return Mathf.Max(0f, durationByDamage.Evaluate(Mathf.Max(0f, damage)));
    }
}
