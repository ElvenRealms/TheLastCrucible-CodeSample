using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// One authored sound in an animation clip: a cue that plays when playback crosses a normalized time.
/// A timed sound is cut off when its span ends early; it never loops.
/// </summary>
[Serializable]
public sealed class ClipSoundEvent : ClipEvent
{
    [SerializeField, AssetsOnly, Required, InlineEditor]
    private AudioCue cue;

    public ClipSoundEvent()
    {
    }

    public ClipSoundEvent(float time, AudioCue cue) : base(time)
    {
        this.cue = cue;
    }

    public AudioCue Cue => cue;
}
