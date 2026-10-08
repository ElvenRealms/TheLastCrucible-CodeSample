using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Configures a slow-motion beat, optionally limited to lethal hits. All times are unscaled seconds.
/// </summary>
[CreateAssetMenu(fileName = "SlowMotion", menuName = "BossFight/Slow Motion Data")]
public class SlowMotionData : CombatFeedbackEffectData
{
    [BoxGroup("Trigger"), Tooltip("Only lethal hits start this slow motion.")]
    public bool lethalOnly;

    [BoxGroup("Timing"), MinValue(0f), Tooltip("Seconds before slow motion starts, so it can begin after a hit-stop freeze.")]
    public float delay;
    [BoxGroup("Timing"), MinValue(0f), Tooltip("Seconds slow motion holds its scale.")]
    public float duration = 0.35f;
    [BoxGroup("Timing"), MinValue(0f), Tooltip("Seconds to ease the scale back to 1.")]
    public float recover = 0.25f;

    [BoxGroup("Scale"), Range(0.05f, 1f), Tooltip("Time scale held during slow motion.")]
    public float scale = 0.35f;
}
