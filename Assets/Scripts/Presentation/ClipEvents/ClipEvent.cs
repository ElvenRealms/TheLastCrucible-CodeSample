using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// Timing shared by every clip event: a one-shot at a normalized time, or, when timed, a span that
/// runs until playback crosses its end time or the action is interrupted.
/// </summary>
[Serializable]
public abstract class ClipEvent
{
    [SerializeField, MinValue(0f), MaxValue(1f)]
    [Tooltip("Normalized clip time at which the event starts.")]
    private float time;
    [SerializeField, ShowIf(nameof(SupportsSpans))]
    [Tooltip("Runs the event until End Time, ending it early when the action is interrupted.")]
    private bool isTimed;
    [SerializeField, ShowIf(nameof(IsTimed)), MinValue(0f), MaxValue(1f)]
    [ValidateInput(nameof(IsEndTimeValid), "End Time must be after Time and no later than 1.")]
    [Tooltip("Normalized clip time at which the event ends.")]
    private float endTime;

    protected ClipEvent()
    {
    }

    protected ClipEvent(float time)
    {
        this.time = time;
    }

    public float Time => time;
    public bool IsTimed => isTimed && SupportsSpans;
    public float EndTime => endTime;

    /// <summary>False for events that only make sense as one-shots; they ignore a stored span.</summary>
    protected virtual bool SupportsSpans => true;

    private bool IsEndTimeValid(float value)
    {
        return !IsTimed || (time < value && value <= 1f);
    }
}
