using UnityEditor;
using UnityEngine;

/// <summary>
/// Auditions sound events in the Clip Events window. A timed sound stops at its end time, measured
/// at the clip's real-time length. The Editor preview can only stop every clip at once, so ending a
/// timed sound also cuts off any other sound still playing.
/// </summary>
public sealed class ClipEventAudition
{
    private double _stopAt = -1d;

    /// <summary>Plays a sound event's cue, scheduling a timed one to stop after its span.</summary>
    public void Play(ClipSoundEvent soundEvent, AnimationClip clip)
    {
        if (soundEvent == null || !EditorAudioPreview.IsAvailable)
        {
            return;
        }

        EditorAudioPreview.Play(soundEvent.Cue);
        _stopAt = soundEvent.IsTimed && clip != null && clip.length > 0f
            ? EditorApplication.timeSinceStartup + (soundEvent.EndTime - soundEvent.Time) * clip.length
            : -1d;
    }

    /// <summary>Stops a timed sound once its end time has passed. Call every Editor update.</summary>
    public void Update()
    {
        if (_stopAt >= 0d && EditorApplication.timeSinceStartup >= _stopAt)
        {
            _stopAt = -1d;
            EditorAudioPreview.StopAll();
        }
    }
}
