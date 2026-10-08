using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Shared poise capacity and recovery tuning for one participant. The player
/// and the boss each author their own asset.
/// </summary>
[CreateAssetMenu(fileName = "PoiseConfig", menuName = "BossFight/Poise Config")]
public class PoiseConfigData : ScriptableObject
{
    [BoxGroup("Capacity"), SerializeField, MinValue(0f), SuffixLabel("poise")]
    private float maxPoise = 100f;

    [BoxGroup("Recovery"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float recoveryDelay = 3f;
    [BoxGroup("Recovery"), SerializeField, MinValue(0f), SuffixLabel("poise/s")]
    private float recoveryPerSecond = 20f;

    public float MaxPoise => maxPoise;
    public float RecoveryDelay => recoveryDelay;
    public float RecoveryPerSecond => recoveryPerSecond;
}
