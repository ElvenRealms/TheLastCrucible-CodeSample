using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Maps accepted damage to a camera field-of-view punch in degrees, with a lethal override.
/// Negative values zoom in.
/// </summary>
[CreateAssetMenu(fileName = "FovKick", menuName = "BossFight/FOV Kick Data")]
public class FovKickData : CombatFeedbackEffectData
{
    [BoxGroup("Normal Hits"), ValidateInput(nameof(DegreesCurveValid), "degreesByDamage must contain at least one key.")]
    [Tooltip("Field-of-view change in degrees by accepted damage. Negative zooms in.")]
    public AnimationCurve degreesByDamage = new(
        new Keyframe(0f, -1.5f),
        new Keyframe(30f, -4f));

    [BoxGroup("Lethal Hits")]
    public float lethalDegrees = -6f;

    [BoxGroup("Envelope"), MinValue(0f), Tooltip("Seconds to ease to the full kick.")]
    public float attack = 0.03f;
    [BoxGroup("Envelope"), MinValue(0f), Tooltip("Seconds to ease back to the resting field of view.")]
    public float release = 0.3f;

    private bool DegreesCurveValid => degreesByDamage != null && degreesByDamage.length > 0;

    /// <summary>
    /// Returns the signed field-of-view change in degrees for a hit.
    /// </summary>
    public float GetDegrees(float damage, bool lethal)
    {
        if (lethal) return lethalDegrees;
        if (!DegreesCurveValid) return 0f;
        return degreesByDamage.Evaluate(Mathf.Max(0f, damage));
    }
}
