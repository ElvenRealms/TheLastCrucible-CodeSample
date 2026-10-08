using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a <see cref="FightScriptRunner"/> drives. <see cref="FightChoreographer"/> implements it over
/// the live player and boss.
/// </summary>
public interface IFightPerformer
{
    float DistanceToBoss { get; }

    /// <summary>Holds player movement in a direction relative to the boss until told otherwise.</summary>
    void HoldDirection(FightDirection direction, bool sprint);

    void Press(FightPlayerAction action);

    /// <summary>Makes <paramref name="attack"/> the boss's next attack. False when the boss can't use it.</summary>
    bool QueueBossAttack(BossAttackData attack);

    void CancelBossAttack();
}

public enum FightRunnerPhase
{
    Idle,
    WaitingForCue,
    Performing,
    Finished
}

/// <summary>
/// Plays a <see cref="FightScript"/> beat by beat on its own game-time clock, so slow motion and
/// pauses stretch the choreography with the fight. Boss cues are logged as they arrive. A beat
/// matches cues from the moment the previous beat started, so a dodge cued on a wind-up still
/// catches it when the boss beat before it ended on that same wind-up. Each cue starts at most one beat.
/// </summary>
public class FightScriptRunner
{
    private readonly IFightPerformer _performer;
    private readonly List<CueRecord> _cues = new List<CueRecord>();
    private FightScript _script;
    private FightRunnerPhase _phase;
    private int _beatIndex;
    private float _time;
    private float _waitStart;
    private float _cueWindowStart;
    private float _beatStart;
    private float _startTime = float.NaN;

    public FightRunnerPhase Phase => _phase;
    public int BeatIndex => _beatIndex;
    public FightBeat CurrentBeat => IsRunning ? _script.Beats[_beatIndex] : null;
    public bool IsRunning => _phase is FightRunnerPhase.WaitingForCue or FightRunnerPhase.Performing;

    public event Action Finished;

    public FightScriptRunner(IFightPerformer performer)
    {
        _performer = performer ?? throw new ArgumentNullException(nameof(performer));
    }

    public void Start(FightScript script)
    {
        _script = script != null ? script : throw new ArgumentNullException(nameof(script));
        _cues.Clear();
        _time = 0f;
        _waitStart = 0f;
        _cueWindowStart = 0f;
        _beatIndex = 0;
        _startTime = float.NaN;
        _phase = script.Beats.Count > 0 ? FightRunnerPhase.WaitingForCue : FightRunnerPhase.Finished;
    }

    /// <summary>Stops mid-beat, releasing held movement and any queued boss attack.</summary>
    public void Stop()
    {
        if (_phase == FightRunnerPhase.Performing)
        {
            _performer.HoldDirection(FightDirection.None, false);
        }
        if (IsRunning)
        {
            _performer.CancelBossAttack();
        }
        _phase = FightRunnerPhase.Idle;
    }

    /// <summary>Records a boss cue at the current script time.</summary>
    public void Notify(FightCue cue, BossAttackData attack)
    {
        if (IsRunning)
        {
            _cues.Add(new CueRecord(cue, attack, _time));
        }
    }

    public void Tick(float deltaTime)
    {
        if (!IsRunning)
        {
            return;
        }

        _time += deltaTime;
        // Beats with no wait and no duration chain within one tick; the bound stops a looping
        // script of such beats from spinning forever.
        for (int step = 0; step <= _script.Beats.Count && IsRunning; step++)
        {
            bool advanced = _phase == FightRunnerPhase.WaitingForCue ? TryStartBeat() : TryEndBeat();
            if (!advanced)
            {
                return;
            }
        }
    }

    private bool TryStartBeat()
    {
        FightBeat beat = CurrentBeat;
        if (float.IsNaN(_startTime))
        {
            if (beat.Cue == FightCue.AfterPrevious)
            {
                _startTime = _waitStart + beat.Delay;
            }
            else if (TryTakeCue(beat.Cue, beat.CueAttack, out float cueTime))
            {
                _startTime = cueTime + beat.Delay;
            }
            else if (_time - _waitStart >= _script.CueTimeout)
            {
                Warn($"no {beat.Cue} cue arrived within {_script.CueTimeout:0.#} s, so it was skipped.");
                Advance();
                return true;
            }
        }

        if (float.IsNaN(_startTime) || _time < _startTime)
        {
            return false;
        }

        StartBeat(beat);
        return true;
    }

    private void StartBeat(FightBeat beat)
    {
        _beatStart = _time;
        _cueWindowStart = _time;
        _cues.RemoveAll(record => record.Time < _cueWindowStart);
        _phase = FightRunnerPhase.Performing;

        if (beat.Actor == FightActor.Boss)
        {
            if (!_performer.QueueBossAttack(beat.BossAttack))
            {
                Warn($"the boss has no attack option in its current phase that opens with '{(beat.BossAttack != null ? beat.BossAttack.name : "nothing")}'.");
                Advance();
            }
            return;
        }

        if (beat.Direction != FightDirection.None)
        {
            _performer.HoldDirection(beat.Direction, beat.Sprint);
        }
        // After the direction, so a dodge rolls the way the beat holds.
        if (beat.Action != FightPlayerAction.None)
        {
            _performer.Press(beat.Action);
        }
    }

    private bool TryEndBeat()
    {
        FightBeat beat = CurrentBeat;
        if (beat.Actor == FightActor.Boss)
        {
            if (beat.WaitForWindUp && !HasCue(FightCue.BossWindUp, beat.BossAttack, _beatStart))
            {
                if (_time - _beatStart < _script.CueTimeout)
                {
                    return false;
                }
                Warn($"the boss didn't start '{beat.BossAttack.name}' within {_script.CueTimeout:0.#} s, so it was dropped. Check the attack's range and facing angle against where the player stands.");
                _performer.CancelBossAttack();
            }
            Advance();
            return true;
        }

        if (_time - _beatStart < beat.Duration && !HasReachedStopDistance(beat))
        {
            return false;
        }

        if (beat.Direction != FightDirection.None)
        {
            _performer.HoldDirection(FightDirection.None, false);
        }
        Advance();
        return true;
    }

    private bool HasReachedStopDistance(FightBeat beat)
    {
        if (beat.StopDistance <= 0f)
        {
            return false;
        }

        return beat.Direction switch
        {
            FightDirection.Toward => _performer.DistanceToBoss <= beat.StopDistance,
            FightDirection.Away => _performer.DistanceToBoss >= beat.StopDistance,
            _ => false
        };
    }

    private void Advance()
    {
        _waitStart = _time;
        _startTime = float.NaN;
        _phase = FightRunnerPhase.WaitingForCue;
        if (++_beatIndex < _script.Beats.Count)
        {
            return;
        }

        if (_script.Loop)
        {
            _beatIndex = 0;
            return;
        }

        _phase = FightRunnerPhase.Finished;
        Finished?.Invoke();
    }

    private bool TryTakeCue(FightCue cue, BossAttackData attack, out float time)
    {
        int index = FindCue(cue, attack, _cueWindowStart);
        time = index >= 0 ? _cues[index].Time : 0f;
        if (index >= 0)
        {
            _cues.RemoveAt(index);
        }
        return index >= 0;
    }

    private bool HasCue(FightCue cue, BossAttackData attack, float since) => FindCue(cue, attack, since) >= 0;

    private int FindCue(FightCue cue, BossAttackData attack, float since)
    {
        for (int i = 0; i < _cues.Count; i++)
        {
            CueRecord record = _cues[i];
            if (record.Cue == cue && record.Time >= since && (attack == null || record.Attack == attack))
            {
                return i;
            }
        }
        return -1;
    }

    private void Warn(string message)
    {
        Debug.LogWarning($"[FightScript] '{_script.name}' beat {_beatIndex} ({CurrentBeat.Label}): {message}");
    }

    private readonly struct CueRecord
    {
        public readonly FightCue Cue;
        public readonly BossAttackData Attack;
        public readonly float Time;

        public CueRecord(FightCue cue, BossAttackData attack, float time)
        {
            Cue = cue;
            Attack = attack;
            Time = time;
        }
    }
}
