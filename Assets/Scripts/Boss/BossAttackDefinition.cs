using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// An opening attack and its committed follow-up assets. When the boss may start it is authored
/// on the option that holds it, as conditions and considerations.
/// </summary>
[System.Serializable]
public class BossAttackDefinition
{
    [BoxGroup("Sequence"), SerializeField, Required, AssetsOnly, InlineEditor]
    [LabelText("Opening Attack")]
    private BossAttackData openingAttack;
    [BoxGroup("Sequence"), SerializeField, AssetsOnly, InlineEditor]
    [ListDrawerSettings(ListElementLabelName = "InspectorLabel", DefaultExpandedState = false)]
    [Tooltip("Played in order without reselecting or returning to pursuit.")]
    private BossAttackData[] followUpAttacks = System.Array.Empty<BossAttackData>();
    [BoxGroup("Sequence"), SerializeField, Range(0f, 1f)]
    [Tooltip("Chance that each follow-up plays. A failed roll ends the combo on the current step, which then plays out to its Early Exit time.")]
    private float continueChance = 1f;
    [BoxGroup("Sequence"), SerializeField, MinValue(0f), SuffixLabel("m")]
    [Tooltip("Follow-ups only play while the target is within this planar distance, so the boss does not swing at empty air. 0 ignores distance.")]
    private float continueMaximumRange;

    [BoxGroup("Pacing"), SerializeField, MinValue(0f), SuffixLabel("x")]
    [Tooltip("Multiplier on the attack delay that follows this attack. Below 1 for quick pokes, above 1 for heavy finishers.")]
    private float delayScale = 1f;
    [BoxGroup("Pacing"), SerializeField, Range(0f, 1f)]
    [Tooltip("Chance the boss goes through with this attack once it is chosen. On a failed roll it passes on the attack and keeps moving or picks another. An attack one of its considerations favours right now always goes ahead.")]
    private float startChance = 1f;
    [BoxGroup("Pacing"), SerializeField, MinValue(0f), SuffixLabel("s"), HideIf(nameof(startChance), 1f)]
    [Tooltip("Seconds the boss leaves this attack alone after passing on it.")]
    private float passDuration = 3f;

    public BossAttackData OpeningAttack => openingAttack;
    public BossAttackData[] FollowUpAttacks => followUpAttacks;
    public float ContinueChance => continueChance;
    public float ContinueMaximumRange => continueMaximumRange;
    public float DelayScale => delayScale;
    public float PassDuration => passDuration;
    public string InspectorLabel => openingAttack != null ? openingAttack.InspectorLabel : "Unassigned Attack";
    public int StepCount => 1 + (followUpAttacks?.Length ?? 0);
    public bool SequenceValid
    {
        get
        {
            for (int i = 0; i < StepCount; i++)
            {
                BossAttackData step = GetStep(i);
                if (step == null || !step.IsValid)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public BossAttackDefinition()
    {
    }

    public BossAttackDefinition(BossAttackData openingAttack, BossAttackData[] followUpAttacks, float continueChance, float continueMaximumRange,
        float delayScale, float startChance, float passDuration)
    {
        this.openingAttack = openingAttack;
        this.followUpAttacks = followUpAttacks ?? System.Array.Empty<BossAttackData>();
        this.continueChance = continueChance;
        this.continueMaximumRange = continueMaximumRange;
        this.delayScale = delayScale;
        this.startChance = startChance;
        this.passDuration = passDuration;
    }

    /// <summary>
    /// Whether the boss goes through with this attack once it is chosen: always when the fight
    /// favours it, otherwise on a roll in [0, 1) under its start chance.
    /// </summary>
    public bool Commits(bool favoured, float roll)
    {
        return favoured || startChance >= 1f || roll < startChance;
    }

    public BossAttackData GetStep(int index)
    {
        if (index == 0)
        {
            return openingAttack;
        }

        int followUpIndex = index - 1;
        return followUpAttacks != null && followUpIndex >= 0 && followUpIndex < followUpAttacks.Length
            ? followUpAttacks[followUpIndex]
            : null;
    }
}
