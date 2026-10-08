using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// How the boss paces its attacks in one phase of the fight: the wait between them, how often
/// it chains them, and what cuts a wait short.
/// </summary>
[System.Serializable]
public class BossPacing
{
    [BoxGroup("Attack Delay"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Minimum wait after an attack sequence ends or is interrupted, before the boss may start another. Does not delay combo steps.")]
    private float minimumAttackDelay = 1f;
    [BoxGroup("Attack Delay"), SerializeField, MinValue(nameof(minimumAttackDelay)), SuffixLabel("s")]
    [Tooltip("Maximum wait between attack sequences. The boss pursues or holds attack range during this wait.")]
    private float maximumAttackDelay = 2f;
    [BoxGroup("Attack Delay"), SerializeField, Range(0f, 1f)]
    [Tooltip("Chance that a wait is a longer breather instead, during which the boss circles the target. 0 never takes one.")]
    private float breatherChance;
    [BoxGroup("Attack Delay"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Minimum length of a breather.")]
    private float minimumBreatherDelay = 3f;
    [BoxGroup("Attack Delay"), SerializeField, MinValue(nameof(minimumBreatherDelay)), SuffixLabel("s")]
    [Tooltip("Maximum length of a breather.")]
    private float maximumBreatherDelay = 5f;
    [BoxGroup("Attack Delay"), SerializeField, Range(0f, 1f)]
    [Tooltip("Chance that an attack is followed straight away by another, with no wait at all.")]
    private float chainChance;
    [BoxGroup("Attack Delay"), SerializeField, MinValue(1)]
    [Tooltip("Most attacks the boss may chain back to back before it must take a normal wait.")]
    private int maximumChainedAttacks = 1;

    [BoxGroup("Reactions"), SerializeField, MinValue(0)]
    [Tooltip("Fewest hits the boss takes between attacks before it cuts its wait short and retaliates. 0 never retaliates.")]
    private int minimumRetaliationHits;
    [BoxGroup("Reactions"), SerializeField, MinValue(nameof(minimumRetaliationHits))]
    [Tooltip("Most hits the boss takes between attacks before it retaliates. The count is rolled anew each time, so the player cannot count hits.")]
    private int maximumRetaliationHits;
    [BoxGroup("Reactions"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Hits further apart than this start the retaliation count again.")]
    private float retaliationWindow = 4f;
    [BoxGroup("Reactions"), SerializeField, Range(0f, 1f)]
    [Tooltip("Chance that the boss cuts its wait short the moment the target starts to heal.")]
    private float healReactionChance;
    [BoxGroup("Reactions"), SerializeField, Range(0f, 180f), SuffixLabel("deg")]
    [Tooltip("Facing error beyond which the target counts as behind the boss.")]
    private float behindAngle = 100f;
    [BoxGroup("Reactions"), SerializeField, MinValue(0f), SuffixLabel("m/s")]
    [Tooltip("Speed at which the target must be moving away from the boss to count as retreating.")]
    private float retreatSpeed = 2.5f;

    public float ChainChance => chainChance;
    public int MaximumChainedAttacks => maximumChainedAttacks;
    public float RetaliationWindow => retaliationWindow;
    public float HealReactionChance => healReactionChance;
    public float BehindAngle => behindAngle;
    public float RetreatSpeed => retreatSpeed;

    /// <summary>Samples one nonnegative wait before the boss may start its next attack.</summary>
    public float SampleAttackDelay()
    {
        bool isBreather = Random.value < breatherChance;
        float minimum = Mathf.Max(0f, isBreather ? minimumBreatherDelay : minimumAttackDelay);
        float maximum = Mathf.Max(minimum, isBreather ? maximumBreatherDelay : maximumAttackDelay);
        return Random.Range(minimum, maximum);
    }

    /// <summary>Samples how many hits the boss takes before its next retaliation; 0 when it never retaliates.</summary>
    public int SampleRetaliationHits()
    {
        int minimum = Mathf.Max(0, minimumRetaliationHits);
        return minimum == 0 ? 0 : Random.Range(minimum, Mathf.Max(minimum, maximumRetaliationHits) + 1);
    }
}
