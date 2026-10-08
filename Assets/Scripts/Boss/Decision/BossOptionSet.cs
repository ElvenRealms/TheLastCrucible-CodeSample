using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// One authored option: what the boss does, how it ranks against the others, and when it is
/// on offer.
/// </summary>
[System.Serializable]
public class BossOptionEntry
{
    [SerializeField]
    [Tooltip("Shown in the Inspector, the Debug score table and Play Mode recordings. Empty uses the action's name.")]
    private string name;
    [SerializeField]
    [Tooltip("Options are only weighed against others of the same priority, and the highest priority with anything on offer is decided first.")]
    private int priority;
    [SerializeField, InlineProperty, HideLabel]
    private BossOptionScoring scoring = new();
    [SerializeReference]
    [Tooltip("What the boss does when this option is chosen.")]
    private BossAction action;

    public string Name => !string.IsNullOrEmpty(name) ? name : action != null ? action.InspectorLabel : "No Action";
    public int Priority => priority;
    public BossOptionScoring Scoring => scoring;
    public BossAction Action => action;
    public string InspectorLabel => $"{priority}  {Name}";

    public BossOptionEntry()
    {
    }

    public BossOptionEntry(string name, int priority, BossOptionScoring scoring, BossAction action)
    {
        this.name = name;
        this.priority = priority;
        this.scoring = scoring;
        this.action = action;
    }
}

/// <summary>
/// The one place a boss's decisions are tuned: every option it can choose while it is free to
/// act, each with its priority, weight, conditions, considerations and action, how predictably
/// it chooses among them, and how it paces its attacks in each phase. BossBrain weighs the options every frame.
/// </summary>
[CreateAssetMenu(fileName = "BossDecisions", menuName = "BossFight/Boss Decisions")]
public class BossOptionSet : ScriptableObject
{
    [ListDrawerSettings(ListElementLabelName = "InspectorLabel", DefaultExpandedState = true, ShowPaging = false)]
    [ValidateInput(nameof(HasFallback), "options needs at least one entry with an action and no conditions, so the boss always has something to do.")]
    [SerializeField]
    private BossOptionEntry[] options = System.Array.Empty<BossOptionEntry>();

    [SerializeField, Range(0f, 1f)]
    [Tooltip("How predictable the boss is. An option scoring under this share of the best score at its priority is dropped, and the boss chooses among the rest by score. 0 drops nothing, so every option on offer has a chance. 1 always takes the best.")]
    private float scoreCutoff = 0.35f;

    [FoldoutGroup("Pacing"), SerializeField, InlineProperty, HideLabel]
    private BossPacing pacing = new();
    [FoldoutGroup("Enraged Pacing"), SerializeField, InlineProperty, HideLabel]
    private BossPacing enragedPacing = new();

    public IReadOnlyList<BossOptionEntry> Options => options;
    public float ScoreCutoff => scoreCutoff;

    private bool HasFallback => options != null &&
        System.Array.Exists(options, option => option != null && option.Action != null && !option.Scoring.HasConditions);

    /// <summary>How the boss paces its attacks in its current phase.</summary>
    public BossPacing GetPacing(bool enraged)
    {
        return enraged ? enragedPacing : pacing;
    }

    /// <summary>Builds one option per entry, in authored order. An entry without an action is left out.</summary>
    public BossOption[] CreateOptions(BossStateMachine machine)
    {
        var created = new List<BossOption>(options.Length);
        foreach (var entry in options)
        {
            if (entry != null && entry.Action != null)
            {
                created.Add(entry.Action.CreateOption(machine, entry));
            }
        }
        return created.ToArray();
    }
}
