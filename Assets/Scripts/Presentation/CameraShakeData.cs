using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Maps accepted damage to a camera-shake amplitude, with a lethal override.
/// </summary>
[CreateAssetMenu(fileName = "CameraShake", menuName = "BossFight/Camera Shake Data")]
public class CameraShakeData : CombatFeedbackEffectData
{
    [BoxGroup("Normal Hits"), ValidateInput(nameof(AmplitudeCurveValid), "amplitudeByDamage must contain at least one key.")]
    public AnimationCurve amplitudeByDamage = new(
        new Keyframe(0f, 0.15f),
        new Keyframe(30f, 0.6f));

    [BoxGroup("Lethal Hits"), MinValue(0f)]
    public float lethalAmplitude = 1f;

    [BoxGroup("Direction"), Tooltip("Kick the camera along the hit direction (source -> receiver) instead of an undirected shake.")]
    public bool directional = true;

    [BoxGroup("Distance"), MinValue(0f), SuffixLabel("m")]
    [Tooltip("Distance from the effect to the receiver at which the shake fades out completely. 0 keeps full strength at any distance.")]
    public float falloffDistance;

    private bool AmplitudeCurveValid => amplitudeByDamage != null && amplitudeByDamage.length > 0;

    public float GetAmplitude(float damage, bool lethal)
    {
        if (lethal) return Mathf.Max(0f, lethalAmplitude);
        if (!AmplitudeCurveValid) return 0f;
        return Mathf.Max(0f, amplitudeByDamage.Evaluate(Mathf.Max(0f, damage)));
    }

    /// <summary>Amplitude multiplier for an effect this far from the receiver.</summary>
    public float GetDistanceScale(float distance)
    {
        if (falloffDistance <= 0f) return 1f;
        return Mathf.Clamp01(1f - distance / falloffDistance);
    }
}
