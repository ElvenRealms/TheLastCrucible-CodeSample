using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Holds once enough time has passed since the boss last started this option.
/// </summary>
[System.Serializable]
public class BossCooldownCondition : BossCondition
{
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds after this option starts before the boss may choose it again.")]
    private float seconds = 10f;

    public override string InspectorLabel => System.FormattableString.Invariant($"Cooldown {seconds:0.##} s");

    public BossCooldownCondition()
    {
    }

    public BossCooldownCondition(float seconds)
    {
        this.seconds = seconds;
    }

    public override bool IsMet(in BossDecisionContext context, BossOptionMemory memory)
    {
        return memory.SecondsSinceStarted >= seconds;
    }
}
