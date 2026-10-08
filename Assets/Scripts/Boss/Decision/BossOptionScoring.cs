using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// How much the boss wants one option right now: its weight, the conditions that must all
/// hold, and the considerations that scale the weight. A score of 0 means the option is not
/// on offer.
/// </summary>
[System.Serializable]
public class BossOptionScoring
{
    [SerializeField, MinValue(0f)]
    [Tooltip("The option's score before its considerations. The boss chooses by score among the options at one priority that pass the option set's score cutoff. 0 never chooses it.")]
    private float weight = 1f;
    [SerializeReference, ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
    [ValidateInput(nameof(NoEmptyCondition), "conditions has an empty slot, which is skipped: choose a condition for it or remove it.")]
    [Tooltip("The option is on offer only while every condition holds.")]
    private BossCondition[] conditions = System.Array.Empty<BossCondition>();
    [SerializeReference, ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
    [ValidateInput(nameof(NoEmptyConsideration), "considerations has an empty slot, which is skipped: choose a consideration for it or remove it.")]
    [Tooltip("Each one multiplies the weight by how well the option suits the fight right now.")]
    private BossConsideration[] considerations = System.Array.Empty<BossConsideration>();

    public float Weight => weight;
    public IReadOnlyList<BossCondition> Conditions => conditions;
    public bool HasConditions => conditions != null && System.Array.Exists(conditions, condition => condition != null);

    private bool NoEmptyCondition => conditions == null || !System.Array.Exists(conditions, condition => condition == null);
    private bool NoEmptyConsideration => considerations == null || !System.Array.Exists(considerations, consideration => consideration == null);

    public BossOptionScoring()
    {
    }

    public BossOptionScoring(float weight, BossCondition[] conditions = null, BossConsideration[] considerations = null)
    {
        this.weight = weight;
        this.conditions = conditions ?? System.Array.Empty<BossCondition>();
        this.considerations = considerations ?? System.Array.Empty<BossConsideration>();
    }

    /// <summary>
    /// The weight scaled by every consideration, or 0 while a condition does not hold. An
    /// empty slot, left by a rule whose class no longer exists, is skipped so it cannot switch
    /// the option off.
    /// </summary>
    public float Score(in BossDecisionContext context, BossOptionMemory memory)
    {
        if (conditions != null)
        {
            foreach (var condition in conditions)
            {
                if (condition != null && !condition.IsMet(context, memory))
                {
                    return 0f;
                }
            }
        }

        float score = weight;
        if (considerations != null)
        {
            foreach (var consideration in considerations)
            {
                if (consideration != null)
                {
                    score *= Mathf.Max(0f, consideration.Score(context, memory));
                }
            }
        }
        return Mathf.Max(0f, score);
    }

    /// <summary>
    /// Whether the fight is in a state this option was authored to answer: a situation one of
    /// its considerations favours holds. A curve is a preference and never counts.
    /// </summary>
    public bool Favours(in BossDecisionContext context)
    {
        if (considerations == null)
        {
            return false;
        }

        foreach (var consideration in considerations)
        {
            if (consideration != null && consideration.Answers(context))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Why the option scores what it does right now: the first condition that fails, or every
    /// consideration that is moving the weight. For the Debug score table.
    /// </summary>
    public string Describe(in BossDecisionContext context, BossOptionMemory memory)
    {
        if (conditions != null)
        {
            foreach (var condition in conditions)
            {
                if (condition != null && !condition.IsMet(context, memory))
                {
                    return $"fails {condition.InspectorLabel}";
                }
            }
        }

        var detail = new StringBuilder();
        if (considerations != null)
        {
            foreach (var consideration in considerations)
            {
                float multiplier = consideration != null ? Mathf.Max(0f, consideration.Score(context, memory)) : 1f;
                if (!Mathf.Approximately(multiplier, 1f))
                {
                    detail.Append(detail.Length > 0 ? ", " : string.Empty)
                        .Append(System.FormattableString.Invariant($"{consideration.Subject} x{multiplier:0.##}"));
                }
            }
        }
        return detail.ToString();
    }

    /// <summary>The first condition of one type, or null. For tools that need an authored value such as an attack's range.</summary>
    public T FindCondition<T>() where T : BossCondition
    {
        foreach (var condition in conditions)
        {
            if (condition is T match)
            {
                return match;
            }
        }
        return null;
    }
}

/// <summary>
/// Weighted-random choice among the scored options that come close enough to the best one.
/// </summary>
public static class BossOptionPicker
{
    /// <summary>
    /// Index of the option a roll in [0, 1) lands on. An option scoring under the cutoff's
    /// share of the best score is left out, and each of the rest takes a share of the roll equal
    /// to its share of their total. A cutoff of 0 leaves none out and 1 leaves only the best.
    /// -1 when no option scores above 0.
    /// </summary>
    public static int PickIndex(IReadOnlyList<float> scores, float roll, float cutoff = 0f)
    {
        float best = 0f;
        for (int i = 0; i < scores.Count; i++)
        {
            best = Mathf.Max(best, scores[i]);
        }
        if (best <= 0f)
        {
            return -1;
        }

        float lowest = best * Mathf.Clamp01(cutoff);
        float total = 0f;
        for (int i = 0; i < scores.Count; i++)
        {
            if (scores[i] > 0f && scores[i] >= lowest)
            {
                total += scores[i];
            }
        }

        float threshold = Mathf.Clamp01(roll) * total;
        float cumulative = 0f;
        int lastScored = -1;
        for (int i = 0; i < scores.Count; i++)
        {
            if (scores[i] <= 0f || scores[i] < lowest)
            {
                continue;
            }

            cumulative += scores[i];
            lastScored = i;
            if (cumulative > threshold)
            {
                return i;
            }
        }

        // Float error can leave the walk short of the threshold; the last
        // scored option is the correct result in that case.
        return lastScored;
    }
}
