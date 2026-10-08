using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Authored voice categories and timing for one boss encounter.
/// </summary>
[CreateAssetMenu(fileName = "BossVoiceSet", menuName = "BossFight/Boss Voice Set")]
public class BossVoiceSet : ScriptableObject
{
    [BoxGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played once when an encounter activates.")]
    private AudioCue battleStartCue;
    [BoxGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot selected periodically during an active encounter.")]
    private AudioCue midBattleCue;
    [BoxGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played once when health reaches the low-health threshold.")]
    private AudioCue lowHealthCue;
    [BoxGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the boss enrages, interrupting any line in progress.")]
    private AudioCue enrageCue;
    [BoxGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the boss dies.")]
    private AudioCue deathCue;

    [BoxGroup("Timing"), SerializeField, Range(0f, 1f)]
    [Tooltip("Low-health voice threshold as a fraction of max health.")]
    private float lowHealthThreshold = 0.3f;
    [BoxGroup("Timing"), SerializeField, MinMaxSlider(0f, 120f, true)]
    [ValidateInput(nameof(MidBattleIntervalRangeValid), "midBattleIntervalRange must be ordered, finite, and non-negative.")]
    [Tooltip("Scaled seconds between periodic mid-battle voice opportunities.")]
    private Vector2 midBattleIntervalRange = new(15f, 25f);

    public AudioCue BattleStartCue => battleStartCue;
    public AudioCue MidBattleCue => midBattleCue;
    public AudioCue LowHealthCue => lowHealthCue;
    public AudioCue EnrageCue => enrageCue;
    public AudioCue DeathCue => deathCue;
    public float LowHealthThreshold => lowHealthThreshold;
    public Vector2 MidBattleIntervalRange => midBattleIntervalRange;

    private bool MidBattleIntervalRangeValid => IsFinite(midBattleIntervalRange.x)
        && IsFinite(midBattleIntervalRange.y)
        && midBattleIntervalRange.x >= 0f
        && midBattleIntervalRange.x <= midBattleIntervalRange.y;

    private void OnValidate()
    {
        lowHealthThreshold = Mathf.Clamp01(lowHealthThreshold);
        if (!MidBattleIntervalRangeValid)
        {
            float minimum = Mathf.Max(0f, IsFinite(midBattleIntervalRange.x) ? midBattleIntervalRange.x : 0f);
            float maximum = Mathf.Max(minimum, IsFinite(midBattleIntervalRange.y) ? midBattleIntervalRange.y : minimum);
            midBattleIntervalRange = new Vector2(minimum, maximum);
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
