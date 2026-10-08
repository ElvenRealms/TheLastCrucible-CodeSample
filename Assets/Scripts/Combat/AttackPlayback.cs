using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks one attack step's playback speed through its speed segments and holds,
/// easing between speeds over each entry's blend. The attack states, timing queries and the
/// timing preview all run it, so their speeds agree.
/// </summary>
public sealed class AttackPlayback
{
    private AttackDataBase _attack;
    private int _nextHoldIndex;
    private AttackHold _activeHold;
    private float _holdElapsed;
    // The hold or segment currently setting the speed; null means base speed.
    private object _speedSource;
    private float _multiplier = 1f;
    private float _blendFrom;
    private float _blendTarget = 1f;
    private float _blendDuration;
    private float _blendElapsed;

    /// <summary>Current playback speed, including the attack's base speed.</summary>
    public float Speed => _attack != null ? _attack.Transition.Speed * _multiplier : 1f;

    /// <summary>Resets playback to <paramref name="normalizedTime"/> at that point's speed, without blending.</summary>
    public void Start(AttackDataBase attack, float normalizedTime)
    {
        _attack = attack;
        _nextHoldIndex = 0;
        IReadOnlyList<AttackHold> holds = attack.Holds;
        while (_nextHoldIndex < holds.Count && holds[_nextHoldIndex].Time < normalizedTime)
        {
            _nextHoldIndex++;
        }

        _activeHold = null;
        _holdElapsed = 0f;
        AttackSpeedSegment segment = attack.FindSpeedSegment(normalizedTime);
        _speedSource = segment;
        _multiplier = segment?.Speed ?? 1f;
        _blendTarget = _multiplier;
        _blendDuration = 0f;
        _blendElapsed = 0f;
    }

    /// <summary>Advances holds and blends by one frame and returns the playback speed to apply.</summary>
    public float Tick(float normalizedTime, float deltaTime)
    {
        UpdateHold(normalizedTime, deltaTime);
        UpdateSpeedSource(normalizedTime);
        _blendElapsed += deltaTime;
        _multiplier = _blendElapsed >= _blendDuration
            ? _blendTarget
            : Mathf.Lerp(_blendFrom, _blendTarget, Mathf.SmoothStep(0f, 1f, _blendElapsed / _blendDuration));
        return Speed;
    }

    private void UpdateHold(float normalizedTime, float deltaTime)
    {
        if (_activeHold != null)
        {
            _holdElapsed += deltaTime;
            if (_holdElapsed < _activeHold.Duration)
            {
                return;
            }
            _activeHold = null;
        }

        IReadOnlyList<AttackHold> holds = _attack.Holds;
        if (_nextHoldIndex < holds.Count && normalizedTime >= holds[_nextHoldIndex].Time)
        {
            _activeHold = holds[_nextHoldIndex];
            _nextHoldIndex++;
            _holdElapsed = 0f;
        }
    }

    private void UpdateSpeedSource(float normalizedTime)
    {
        object source = _activeHold != null ? _activeHold : _attack.FindSpeedSegment(normalizedTime);
        if (source == _speedSource)
        {
            return;
        }

        _blendFrom = _multiplier;
        _blendTarget = source switch
        {
            AttackHold hold => hold.Speed,
            AttackSpeedSegment segment => segment.Speed,
            _ => 1f
        };
        _blendDuration = TransitionBlend(_speedSource, source);
        _blendElapsed = 0f;
        _speedSource = source;
    }

    // A hold owns both its ease in and ease out; otherwise the segment being
    // entered sets the blend, or the one being left when returning to base speed.
    private static float TransitionBlend(object from, object to)
    {
        if (from is AttackHold fromHold)
        {
            return fromHold.Blend;
        }
        if (to is AttackHold toHold)
        {
            return toHold.Blend;
        }
        if (to is AttackSpeedSegment toSegment)
        {
            return toSegment.Blend;
        }
        return from is AttackSpeedSegment fromSegment ? fromSegment.Blend : 0f;
    }
}
