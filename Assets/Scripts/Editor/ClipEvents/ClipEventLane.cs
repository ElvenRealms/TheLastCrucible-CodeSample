using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// One lane of the Clip Events window: which list of a <see cref="ClipEventTrack"/> it edits, how it
/// is labelled, and which asset each of its events plays.
/// </summary>
public sealed class ClipEventLane
{
    public static readonly ClipEventLane Sound = new(
        "soundEvents", "Sounds", "Sound Event", new Color(0.35f, 0.75f, 1f), true,
        "cue", typeof(AudioCue), track => track.SoundEvents, clipEvent => ((ClipSoundEvent)clipEvent).Cue);

    public static readonly ClipEventLane Vfx = new(
        "vfxEvents", "VFX", "VFX Event", new Color(1f, 0.6f, 0.25f), true,
        "prefab", typeof(GameObject), track => track.VfxEvents, clipEvent => ((ClipVfxEvent)clipEvent).Prefab);

    public static readonly ClipEventLane Camera = new(
        "cameraEvents", "Camera", "Camera Event", new Color(0.55f, 0.9f, 0.45f), false,
        "shake", typeof(CameraShakeData), track => track.CameraEvents, CameraAssetOf);

    /// <summary>Every lane, in the order the window draws them.</summary>
    public static readonly IReadOnlyList<ClipEventLane> All = new[] { Sound, Vfx, Camera };

    private readonly Func<ClipEventTrack, IReadOnlyList<ClipEvent>> _events;
    private readonly Func<ClipEvent, Object> _asset;

    private ClipEventLane(string propertyName, string label, string displayName, Color color, bool supportsSpans,
        string assetField, Type assetType, Func<ClipEventTrack, IReadOnlyList<ClipEvent>> events, Func<ClipEvent, Object> asset)
    {
        PropertyName = propertyName;
        Label = label;
        DisplayName = displayName;
        Color = color;
        SupportsSpans = supportsSpans;
        AssetField = assetField;
        AssetType = assetType;
        _events = events;
        _asset = asset;
    }

    /// <summary>The serialized list's field name on a track.</summary>
    public string PropertyName { get; }
    /// <summary>The short row label on the timeline.</summary>
    public string Label { get; }
    /// <summary>The name of one event, such as "VFX Event".</summary>
    public string DisplayName { get; }
    /// <summary>The lane's own color, used for its label and list header.</summary>
    public Color Color { get; }
    /// <summary>False for lanes whose events are always one-shots.</summary>
    public bool SupportsSpans { get; }
    /// <summary>The serialized field on an event that holds the asset it plays.</summary>
    public string AssetField { get; }
    public Type AssetType { get; }

    /// <summary>The lane's events in a track, in start-time order, or an empty list.</summary>
    public IReadOnlyList<ClipEvent> Events(ClipEventTrack track)
    {
        return track != null ? _events(track) : Array.Empty<ClipEvent>();
    }

    /// <summary>The asset an event of this lane plays: its cue, prefab, or camera shake or move.</summary>
    public Object AssetOf(ClipEvent clipEvent)
    {
        return _asset(clipEvent);
    }

    // A camera event is named and coloured by its shake, or by its move when it has no shake.
    private static Object CameraAssetOf(ClipEvent clipEvent)
    {
        var cameraEvent = (ClipCameraEvent)clipEvent;
        return cameraEvent.Shake != null ? cameraEvent.Shake : cameraEvent.Motion;
    }
}
