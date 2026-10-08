using UnityEditor;
using UnityEngine;

/// <summary>
/// Undoable edits to one clip's track in a <see cref="ClipEventLibrary"/>. Every edit goes through the
/// library's SerializedObject, so it records Undo, and every lane stays sorted by start time.
/// </summary>
public sealed class ClipEventTrackEdits
{
    private const string TracksField = "tracks";

    private readonly ClipEventLibrary _library;
    private readonly AnimationClip _clip;

    public ClipEventTrackEdits(ClipEventLibrary library, AnimationClip clip)
    {
        _library = library;
        _clip = clip;
        SerializedLibrary = new SerializedObject(library);
    }

    public SerializedObject SerializedLibrary { get; }

    /// <summary>The index of the clip's track in the library, or -1 when it has none.</summary>
    public int FindTrackIndex()
    {
        if (_clip == null)
        {
            return -1;
        }

        for (int i = 0; i < _library.Tracks.Count; i++)
        {
            if (_library.Tracks[i].Clip == _clip)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The Unity property path of an event in the clip's track.</summary>
    public string EventPath(ClipEventLane lane, int index)
    {
        return $"{TrackPath(FindTrackIndex())}.{lane.PropertyName}.Array.data[{index}]";
    }

    /// <summary>
    /// Adds an event at its start-time position, creating the clip's track if it has none, and
    /// returns its index. A null end time adds a one-shot.
    /// </summary>
    public int Add(ClipEventLane lane, float time, Object asset, float? endTime)
    {
        SerializedLibrary.Update();
        int trackIndex = FindTrackIndex();
        if (trackIndex < 0)
        {
            trackIndex = AddTrack();
        }

        SerializedProperty events = LaneProperty(trackIndex, lane);
        int index = 0;
        while (index < events.arraySize &&
            events.GetArrayElementAtIndex(index).FindPropertyRelative("time").floatValue <= time)
        {
            index++;
        }

        // Inserting copies a neighbouring event, so every field is written explicitly.
        events.InsertArrayElementAtIndex(index);
        SerializedProperty clipEvent = events.GetArrayElementAtIndex(index);
        WriteTiming(clipEvent, time, endTime);
        if (lane == ClipEventLane.Camera)
        {
            WriteCameraAssets(clipEvent, asset);
        }
        else
        {
            clipEvent.FindPropertyRelative(lane.AssetField).objectReferenceValue = asset;
            if (lane == ClipEventLane.Vfx)
            {
                WriteVfxDefaults(clipEvent);
            }
        }
        Apply($"Add {lane.DisplayName}");
        return index;
    }

    /// <summary>Sets the cue, prefab or camera shake an event plays.</summary>
    public void SetAsset(ClipEventLane lane, int index, Object asset)
    {
        SerializedLibrary.Update();
        EventProperty(lane, index).FindPropertyRelative(lane.AssetField).objectReferenceValue = asset;
        Apply($"Set {lane.DisplayName} {lane.AssetType.Name}");
    }

    /// <summary>Sets an event's start and end times. The end time is ignored for a one-shot.</summary>
    public void SetTimes(ClipEventLane lane, int index, float time, float endTime, string undoName)
    {
        SerializedLibrary.Update();
        SerializedProperty clipEvent = EventProperty(lane, index);
        clipEvent.FindPropertyRelative("time").floatValue = time;
        clipEvent.FindPropertyRelative("endTime").floatValue = endTime;
        Apply(undoName);
    }

    /// <summary>Makes an event a span, giving it a default end time when its current one is invalid.</summary>
    public void MakeTimed(ClipEventLane lane, int index, float defaultEndTime)
    {
        SerializedLibrary.Update();
        SerializedProperty clipEvent = EventProperty(lane, index);
        float time = clipEvent.FindPropertyRelative("time").floatValue;
        SerializedProperty endTime = clipEvent.FindPropertyRelative("endTime");
        if (endTime.floatValue <= time || endTime.floatValue > 1f)
        {
            endTime.floatValue = defaultEndTime;
        }
        clipEvent.FindPropertyRelative("isTimed").boolValue = true;
        Apply($"Make {lane.DisplayName} Timed");
    }

    /// <summary>Makes an event a one-shot; its end time is kept in case it is made timed again.</summary>
    public void MakeOneShot(ClipEventLane lane, int index)
    {
        SerializedLibrary.Update();
        EventProperty(lane, index).FindPropertyRelative("isTimed").boolValue = false;
        Apply($"Make {lane.DisplayName} One-Shot");
    }

    /// <summary>Removes an event, and the clip's track once every lane is empty.</summary>
    public void Remove(ClipEventLane lane, int index)
    {
        SerializedLibrary.Update();
        int trackIndex = FindTrackIndex();
        LaneProperty(trackIndex, lane).DeleteArrayElementAtIndex(index);
        if (IsTrackEmpty(trackIndex))
        {
            SerializedLibrary.FindProperty(TracksField).DeleteArrayElementAtIndex(trackIndex);
        }
        Apply($"Remove {lane.DisplayName}");
    }

    private int AddTrack()
    {
        SerializedProperty tracks = SerializedLibrary.FindProperty(TracksField);
        tracks.arraySize++;
        int trackIndex = tracks.arraySize - 1;
        // A new array element copies the previous track, so its lanes start cleared.
        SerializedProperty track = tracks.GetArrayElementAtIndex(trackIndex);
        track.FindPropertyRelative("clip").objectReferenceValue = _clip;
        foreach (var lane in ClipEventLane.All)
        {
            track.FindPropertyRelative(lane.PropertyName).ClearArray();
        }
        return trackIndex;
    }

    private bool IsTrackEmpty(int trackIndex)
    {
        foreach (var lane in ClipEventLane.All)
        {
            if (LaneProperty(trackIndex, lane).arraySize > 0)
            {
                return false;
            }
        }
        return true;
    }

    private static void WriteTiming(SerializedProperty clipEvent, float time, float? endTime)
    {
        clipEvent.FindPropertyRelative("time").floatValue = time;
        clipEvent.FindPropertyRelative("isTimed").boolValue = endTime.HasValue;
        clipEvent.FindPropertyRelative("endTime").floatValue = endTime ?? 0f;
    }

    // A camera event starts with the one asset it was added with: a shake or a move.
    private static void WriteCameraAssets(SerializedProperty clipEvent, Object asset)
    {
        clipEvent.FindPropertyRelative("shake").objectReferenceValue = asset as CameraShakeData;
        clipEvent.FindPropertyRelative("motion").objectReferenceValue = asset as CameraMotionData;
        clipEvent.FindPropertyRelative("rumble").objectReferenceValue = null;
    }

    // Mirrors the ClipVfxEvent field initializers, which a serialized insert doesn't run.
    private static void WriteVfxDefaults(SerializedProperty clipEvent)
    {
        clipEvent.FindPropertyRelative("socket").enumValueIndex = (int)SocketId.Root;
        clipEvent.FindPropertyRelative("followSocket").boolValue = true;
        clipEvent.FindPropertyRelative("positionOffset").vector3Value = Vector3.zero;
        clipEvent.FindPropertyRelative("rotationOffset").vector3Value = Vector3.zero;
        clipEvent.FindPropertyRelative("scale").floatValue = 1f;
        clipEvent.FindPropertyRelative("maxDuration").floatValue = 3f;
    }

    private void Apply(string undoName)
    {
        SerializedLibrary.ApplyModifiedProperties();
        Undo.SetCurrentGroupName(undoName);
    }

    private SerializedProperty EventProperty(ClipEventLane lane, int index)
    {
        return LaneProperty(FindTrackIndex(), lane).GetArrayElementAtIndex(index);
    }

    private SerializedProperty LaneProperty(int trackIndex, ClipEventLane lane)
    {
        return SerializedLibrary.FindProperty($"{TrackPath(trackIndex)}.{lane.PropertyName}");
    }

    private static string TrackPath(int trackIndex) => $"{TracksField}.Array.data[{trackIndex}]";
}
