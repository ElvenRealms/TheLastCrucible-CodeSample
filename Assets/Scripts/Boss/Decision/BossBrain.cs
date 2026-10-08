using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What BossBrain reads about the fight in order to decide. BossStateMachine is the source in
/// the game.
/// </summary>
public interface IBossDecisionSource
{
    BossDecisionContext CreateDecisionContext();
    /// <summary>The share of the best score at a priority that an option needs to stay in the running, from 0 to 1.</summary>
    float ScoreCutoff { get; }
}

/// <summary>
/// Chooses what the boss does while it is free to act. Each frame it works down the priorities
/// from the highest, scores the options at one, and starts one of them: by weighted chance
/// among those that score close enough to the best. A sustained option keeps running for as long as it stays on
/// offer, so the boss does not change its mind every frame. Plain C#; owned by
/// BossStateMachine and driven by pursuit.
/// </summary>
public class BossBrain
{
    private readonly IBossDecisionSource _source;
    private readonly BossOption[] _options;
    private readonly List<int> _prioritiesHighestFirst = new();
    private readonly List<BossOption> _offered = new();
    private readonly List<float> _scores = new();
    private readonly List<BossOptionScore> _scoreTable = new();
    private BossOption _sustained;
    private BossDecisionContext _context;

    public IReadOnlyList<BossOption> Options => _options;
    /// <summary>The sustained option the boss is carrying out, or null before its first choice.</summary>
    public BossOption Sustained => _sustained;
    /// <summary>The option the last decision went with, whether it is still running in pursuit or handed over to a committed state.</summary>
    public BossOption Chosen { get; private set; }
    /// <summary>The fight as read for the decision in progress. Read once per decision, because it casts against the level.</summary>
    public BossDecisionContext Context => _context;

    public BossBrain(IBossDecisionSource source, BossOption[] options)
    {
        _source = source;
        _options = options;
        foreach (BossOption option in options)
        {
            if (!_prioritiesHighestFirst.Contains(option.Priority))
            {
                _prioritiesHighestFirst.Add(option.Priority);
            }
        }
        _prioritiesHighestFirst.Sort((a, b) => b.CompareTo(a));
    }

    /// <summary>Forgets the last choice without ending a sustained option, for a fresh return to pursuit.</summary>
    public void Reset()
    {
        _sustained = null;
        Chosen = null;
    }

    /// <summary>Forgets what the boss remembers about every option, for a new attempt.</summary>
    public void ForgetHistory()
    {
        foreach (BossOption option in _options)
        {
            option.Memory.Reset();
        }
    }

    /// <summary>Advances every option's memory. Driven by BossStateMachine so it pauses when the boss does.</summary>
    public void Tick(float deltaTime)
    {
        foreach (BossOption option in _options)
        {
            option.Memory.Tick(deltaTime);
        }
    }

    /// <summary>
    /// Reads the fight again for the rest of this decision, after an option changed what the
    /// boss can do without taking over.
    /// </summary>
    public void Refresh()
    {
        _context = _source.CreateDecisionContext();
    }

    public void Decide()
    {
        Refresh();
        foreach (int priority in _prioritiesHighestFirst)
        {
            BossOption chosen = Choose(priority);
            if (chosen == null)
            {
                continue;
            }

            if (chosen.IsSustained)
            {
                Chosen = chosen;
                Sustain(chosen);
                return;
            }
            if (chosen.TryStart())
            {
                Chosen = chosen;
                NoteStarted(chosen);
                return;
            }
        }
    }

    /// <summary>
    /// What every option scores right now. Reads the fight afresh, which casts against the
    /// level, so it is for the Inspector and Play Mode checks rather than for deciding.
    /// </summary>
    public List<BossOptionScore> ScoreOptions()
    {
        Refresh();
        _scoreTable.Clear();
        foreach (BossOption option in _options)
        {
            _scoreTable.Add(new BossOptionScore(option.Name, option.Priority, option.Score(_context), option.Describe(_context)));
        }
        return _scoreTable;
    }

    /// <summary>
    /// Records that an option started: its cooldown begins, and it and every option that opens
    /// the same way become the last started at its priority.
    /// </summary>
    public void NoteStarted(BossOption started)
    {
        started.Memory.NoteStarted();
        foreach (BossOption option in _options)
        {
            if (option.Priority != started.Priority)
            {
                continue;
            }

            option.Memory.StartedLast = option.OpensLike(started);
            if (option.Memory.StartedLast && option != started)
            {
                option.Memory.NoteShown();
            }
        }
    }

    private BossOption Choose(int priority)
    {
        _offered.Clear();
        _scores.Clear();
        foreach (BossOption option in _options)
        {
            if (option.Priority != priority)
            {
                continue;
            }

            float score = option.Score(_context);
            if (score <= 0f)
            {
                continue;
            }
            if (option == _sustained)
            {
                return option;
            }
            _offered.Add(option);
            _scores.Add(score);
        }

        if (_offered.Count == 0)
        {
            return null;
        }
        int index = _offered.Count == 1 ? 0 : BossOptionPicker.PickIndex(_scores, Random.value, _source.ScoreCutoff);
        return index >= 0 ? _offered[index] : null;
    }

    private void Sustain(BossOption option)
    {
        if (option != _sustained)
        {
            _sustained?.Stop();
            _sustained = option;
            option.Begin();
            NoteStarted(option);
        }
        option.Tick();
    }
}
