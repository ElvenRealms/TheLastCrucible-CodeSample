using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// One authored camera beat in an animation clip: a camera shake, a directed camera move, a gamepad
/// rumble, or any mix, started when playback crosses a normalized time. Each effect brings its own
/// length, so camera events are never timed. Clip events carry no damage, so the shake and rumble
/// use their zero-damage strength.
/// </summary>
[Serializable]
public sealed class ClipCameraEvent : ClipEvent
{
    [SerializeField, AssetsOnly, InlineEditor]
    [ValidateInput(nameof(HasEffect), "Assign a shake, a move or a rumble, or the event does nothing.")]
    private CameraShakeData shake;
    [SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional directed camera move: a push, turn or tilt that follows the swing.")]
    private CameraMotionData motion;
    [SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional gamepad rumble played with the shake or move.")]
    private RumbleData rumble;

    public CameraShakeData Shake => shake;
    public CameraMotionData Motion => motion;
    public RumbleData Rumble => rumble;

    private bool HasEffect => shake != null || motion != null || rumble != null;

    protected override bool SupportsSpans => false;
}
