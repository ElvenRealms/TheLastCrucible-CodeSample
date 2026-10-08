using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The Clip Events window's timeline: a scrubber above one row per <see cref="ClipEventLane"/> for
/// one clip, plus a panel that edits the selected event. Handles selection and dragging, and edits
/// the library through <see cref="ClipEventTrackEdits"/>; <see cref="ClipEventLaneView"/> draws the lanes.
/// </summary>
public sealed class ClipEventTimeline : IDisposable
{
    private const float RightPadding = 7f;
    private const float ScrubberHeight = 28f;
    private const int NewSpanFrames = 10;
    // The span length used when the clip has no frame rate to count frames with.
    private const float FallbackSpanLength = 0.1f;
    private static readonly int ControlHint = nameof(ClipEventTimeline).GetHashCode();

    private readonly EditorWindow _owner;
    private readonly ClipEventAudition _audition;
    private readonly ClipEventLaneView _view = new();
    private ClipEventLibrary _library;
    private AnimationClip _clip;
    private ClipEventTrackEdits _edits;
    private PropertyTree _propertyTree;
    private ClipEventTrack _track;
    private float _playhead;
    private ClipEventRef _selection = ClipEventRef.None;
    // The selection the panel draws; it only changes on Layout events so IMGUI sees the same
    // controls in every event of a frame.
    private ClipEventRef _panelSelection = ClipEventRef.None;
    private ClipEventRef _hover = ClipEventRef.None;
    private ClipEventRef _drag = ClipEventRef.None;
    private DragMode _dragMode;
    private float _dragOffset;
    private int _undoGroup;

    public ClipEventTimeline(EditorWindow owner, ClipEventAudition audition)
    {
        _owner = owner;
        _audition = audition;
    }

    /// <summary>True while the user drags on a lane, to scrub or to move or resize an event.</summary>
    public bool IsDragging => _dragMode != DragMode.None;

    /// <summary>The bound clip's track, or null when the clip has no events.</summary>
    public ClipEventTrack Track => _track;

    /// <summary>The bound clip's sound events in time order, or an empty list.</summary>
    public IReadOnlyList<ClipSoundEvent> SoundEvents =>
        _track != null ? _track.SoundEvents : Array.Empty<ClipSoundEvent>();

    public bool CanAddEvent => _library != null && AnimationClipPreview.IsSampleable(_clip);

    /// <summary>A starting name for a new cue, from the bound library ("BossClipEvents" gives "BossCue").</summary>
    public string SuggestedCueName => _library != null ? $"{_library.name.Replace("ClipEvents", string.Empty)}Cue" : "NewCue";

    private float FrameStep => _clip != null && _clip.length > 0f && _clip.frameRate > 0f
        ? 1f / (_clip.length * _clip.frameRate)
        : 0f;
    // The shortest span an edge drag allows, so a span always keeps time < end time.
    private float MinimumSpan => FrameStep > 0f ? FrameStep : 0.001f;

    /// <summary>Points the timeline at a library and clip; the selection resets when either changes.</summary>
    public void Bind(ClipEventLibrary library, AnimationClip clip)
    {
        // The last check catches a library asset deleted while bound.
        if (library != _library || clip != _clip || (library == null) != (_edits == null))
        {
            _library = library;
            _clip = clip;
            _propertyTree?.Dispose();
            _edits = library != null ? new ClipEventTrackEdits(library, clip) : null;
            _propertyTree = _edits != null ? PropertyTree.Create(_edits.SerializedLibrary) : null;
            ClearSelection();
        }

        _edits?.SerializedLibrary.UpdateIfRequiredOrScript();
        RefreshTrack();
    }

    /// <summary>The bound clip's events in one lane, in time order, or an empty list.</summary>
    public IReadOnlyList<ClipEvent> EventsIn(ClipEventLane lane) => lane.Events(_track);

    /// <summary>Draws the scrubber and lanes, and returns the normalized time to show.</summary>
    public float Draw(float normalizedTime)
    {
        _playhead = normalizedTime;
        Rect scrubberRow = GUILayoutUtility.GetRect(100f, ScrubberHeight, GUILayout.ExpandWidth(true));
        float time = DrawScrubber(scrubberRow, normalizedTime);
        if (_library == null)
        {
            return time;
        }

        _view.LayoutLanes();
        time = HandleLaneEvents(time);
        if (Event.current.type == EventType.Repaint)
        {
            _view.Draw(_playhead, _selection, _hover);
        }
        return time;
    }

    /// <summary>Draws the selected event's settings with Odin, under a header naming it.</summary>
    public void DrawSelection()
    {
        if (Event.current.type == EventType.Layout)
        {
            _panelSelection = IsValid(_selection) ? _selection : ClipEventRef.None;
        }

        if (!IsValid(_panelSelection))
        {
            return;
        }

        InspectorProperty property = _propertyTree.GetPropertyAtUnityPath(
            _edits.EventPath(_panelSelection.Lane, _panelSelection.Index));
        if (property == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"{_panelSelection.Lane.DisplayName} {_panelSelection.Index + 1}", EditorStyles.boldLabel);
            if (_panelSelection.Lane == ClipEventLane.Sound)
            {
                var newCueContent = new GUIContent("New Cue", "Create an empty audio cue and assign it to this event.");
                if (GUILayout.Button(newCueContent, EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    AssignNewCue(_panelSelection);
                    GUIUtility.ExitGUI();
                }
                using (new EditorGUI.DisabledScope(!EditorAudioPreview.IsAvailable))
                {
                    if (GUILayout.Button("Audition", EditorStyles.miniButton, GUILayout.Width(64f)))
                    {
                        Audition(_panelSelection);
                    }
                }
            }
            var removeContent = new GUIContent("Remove", "The Delete key also removes the selected event.");
            if (GUILayout.Button(removeContent, EditorStyles.miniButton, GUILayout.Width(64f)))
            {
                Remove(_panelSelection);
                GUIUtility.ExitGUI();
            }
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _propertyTree.BeginDraw(true);
            for (int i = 0; i < property.Children.Count; i++)
            {
                property.Children[i].Draw();
            }
            _propertyTree.EndDraw();
        }
    }

    /// <summary>Selects an event by its lane and its index in that lane.</summary>
    public void Select(ClipEventLane lane, int index)
    {
        var target = new ClipEventRef(lane, index);
        _selection = IsValid(target) ? target : ClipEventRef.None;
        _owner.Repaint();
    }

    public bool IsSelected(ClipEventLane lane, int index) => _selection.Is(lane, index);

    /// <summary>
    /// Adds an event at a normalized time, creating the clip's track if it has none, and selects it.
    /// A timed event starts 10 frames long.
    /// </summary>
    public void AddEvent(ClipEventLane lane, float time, Object asset, bool timed)
    {
        if (!CanAddEvent)
        {
            return;
        }

        time = Snap(time);
        float? endTime = timed && lane.SupportsSpans ? DefaultEndTime(time) : null;
        int index = _edits.Add(lane, time, asset, endTime);
        RefreshTrack();
        _selection = new ClipEventRef(lane, index);
        _owner.Repaint();
    }

    public void Dispose()
    {
        _propertyTree?.Dispose();
        _propertyTree = null;
    }

    private void RefreshTrack()
    {
        int trackIndex = _edits != null ? _edits.FindTrackIndex() : -1;
        _track = trackIndex >= 0 ? _library.Tracks[trackIndex] : null;
        _view.Track = _track;
    }

    private void ClearSelection()
    {
        _selection = ClipEventRef.None;
        _panelSelection = ClipEventRef.None;
        _hover = ClipEventRef.None;
        _dragMode = DragMode.None;
    }

    private float DrawScrubber(Rect row, float normalizedTime)
    {
        const float labelWidth = ClipEventLaneView.LabelWidth;
        var content = new GUIContent("Time", "Drag to scrub the preview.");
        GUI.Label(new Rect(row.x + 4f, row.y + 5f, labelWidth - 4f, 18f), content, EditorStyles.miniLabel);
        var sliderRect = new Rect(row.x + labelWidth, row.y + 5f, row.width - labelWidth - RightPadding, 18f);
        _view.SetTimeRange(sliderRect.x + ClipEventLaneView.ThumbInset, sliderRect.xMax - ClipEventLaneView.ThumbInset);
        if (Event.current.type == EventType.Repaint)
        {
            foreach (var lane in ClipEventLane.All)
            {
                foreach (var clipEvent in EventsIn(lane))
                {
                    float x = _view.TimeToX(clipEvent.Time);
                    Color color = ClipEventLaneView.AssetColor(lane.AssetOf(clipEvent));
                    EditorGUI.DrawRect(new Rect(x - 1f, sliderRect.y - 2f, 2f, sliderRect.height + 4f), color);
                }
            }
        }
        return GUI.HorizontalSlider(sliderRect, normalizedTime, 0f, 1f);
    }

    private float HandleLaneEvents(float time)
    {
        int controlId = GUIUtility.GetControlID(ControlHint, FocusType.Passive);
        Event current = Event.current;
        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown when current.button == 0 && GUI.enabled && _view.LaneAt(current.mousePosition) != null:
                return BeginDrag(controlId, current, time);
            case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                current.Use();
                return ContinueDrag(current, time);
            case EventType.MouseUp when GUIUtility.hotControl == controlId:
                current.Use();
                EndDrag();
                break;
            case EventType.MouseMove:
                UpdateHover(current.mousePosition);
                break;
            case EventType.ContextClick when GUI.enabled && _view.LaneAt(current.mousePosition) != null:
                current.Use();
                ShowContextMenu(current.mousePosition);
                break;
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape && IsValid(_selection) &&
            !EditorGUIUtility.editingTextField)
        {
            current.Use();
            _selection = ClipEventRef.None;
            _hover = ClipEventRef.None;
            _owner.Repaint();
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Delete && GUI.enabled &&
            !EditorGUIUtility.editingTextField && IsValid(_selection))
        {
            current.Use();
            Remove(_selection);
        }
        return time;
    }

    private float BeginDrag(int controlId, Event current, float time)
    {
        current.Use();
        GUI.FocusControl(null);
        float pointer = _view.XToTime(current.mousePosition.x);
        ClipEventHit hit = _view.HitTest(current.mousePosition, Locked);
        if (!IsValid(hit.Target))
        {
            ClipEventLane lane = _view.LaneAt(current.mousePosition);
            if (current.clickCount == 2 && CanAddEvent)
            {
                AddEvent(lane, pointer, LastAsset(lane), false);
                return time;
            }

            _selection = ClipEventRef.None;
            _dragMode = DragMode.Scrub;
            GUIUtility.hotControl = controlId;
            return pointer;
        }

        ClipEvent clipEvent = EventAt(hit.Target);
        _selection = hit.Target;
        _drag = hit.Target;
        _dragMode = hit.Part switch
        {
            ClipEventHitPart.Start => DragMode.Start,
            ClipEventHitPart.End => DragMode.End,
            _ => DragMode.Move
        };
        _dragOffset = pointer - clipEvent.Time;
        GUIUtility.hotControl = controlId;
        Undo.IncrementCurrentGroup();
        _undoGroup = Undo.GetCurrentGroup();
        return _dragMode == DragMode.End ? clipEvent.EndTime : clipEvent.Time;
    }

    private float ContinueDrag(Event current, float time)
    {
        float pointer = _view.XToTime(current.mousePosition.x);
        if (_dragMode == DragMode.Scrub)
        {
            return pointer;
        }

        if (!IsValid(_drag))
        {
            return time;
        }

        // Neighbours bound the start so the lane stays in start-time order.
        IReadOnlyList<ClipEvent> events = EventsIn(_drag.Lane);
        ClipEvent clipEvent = events[_drag.Index];
        float lower = _drag.Index > 0 ? events[_drag.Index - 1].Time : 0f;
        float upper = _drag.Index < events.Count - 1 ? events[_drag.Index + 1].Time : 1f;
        float start = clipEvent.Time;
        float end = clipEvent.EndTime;
        switch (_dragMode)
        {
            case DragMode.Move when clipEvent.IsTimed:
                float length = end - start;
                start = ClampOrdered(Snap(pointer - _dragOffset), lower, Mathf.Min(upper, 1f - length));
                end = start + length;
                break;
            case DragMode.Move:
                start = Mathf.Clamp(Snap(pointer), lower, upper);
                break;
            case DragMode.Start:
                start = ClampOrdered(Snap(pointer), lower, Mathf.Min(upper, end - MinimumSpan));
                break;
            case DragMode.End:
                end = Mathf.Clamp(Snap(pointer), start + MinimumSpan, 1f);
                break;
        }

        string verb = _dragMode == DragMode.Move ? "Move" : "Resize";
        _edits.SetTimes(_drag.Lane, _drag.Index, start, end, $"{verb} {_drag.Lane.DisplayName}");
        return _dragMode == DragMode.End ? end : start;
    }

    private void EndDrag()
    {
        GUIUtility.hotControl = 0;
        if (_dragMode != DragMode.Scrub)
        {
            Undo.CollapseUndoOperations(_undoGroup);
        }
        _dragMode = DragMode.None;
    }

    private void UpdateHover(Vector2 mousePosition)
    {
        ClipEventRef hover = _view.HitTest(mousePosition, Locked).Target;
        if (!hover.Equals(_hover))
        {
            _hover = hover;
            _owner.Repaint();
        }
    }

    private void ShowContextMenu(Vector2 point)
    {
        ClipEventHit hit = _view.HitTest(point, Locked);
        var menu = new GenericMenu();
        if (IsValid(hit.Target))
        {
            ClipEventRef target = hit.Target;
            _selection = target;
            if (target.Lane == ClipEventLane.Sound)
            {
                menu.AddItem(new GUIContent("Audition"), false, () => Audition(target));
            }
            if (EventAt(target).IsTimed)
            {
                menu.AddItem(new GUIContent("Make One-Shot"), false, () => MakeOneShot(target));
            }
            else if (target.Lane.SupportsSpans)
            {
                menu.AddItem(new GUIContent("Make Timed"), false, () => MakeTimed(target));
            }
            menu.AddItem(new GUIContent("Remove"), false, () => Remove(target));
        }
        else
        {
            ClipEventLane lane = _view.LaneAt(point);
            float time = _view.XToTime(point.x);
            menu.AddItem(new GUIContent($"Add {lane.DisplayName} Here"), false,
                () => AddEvent(lane, time, LastAsset(lane), false));
            if (lane.SupportsSpans)
            {
                menu.AddItem(new GUIContent($"Add Timed {lane.DisplayName} Here"), false,
                    () => AddEvent(lane, time, LastAsset(lane), true));
            }
        }
        menu.ShowAsContext();
    }

    // New events from a lane reuse the selected event's asset, or the lane's last one, since a
    // clip's events are often repeats (footsteps, cloth, trails).
    private Object LastAsset(ClipEventLane lane)
    {
        IReadOnlyList<ClipEvent> events = EventsIn(lane);
        if (IsValid(_selection) && _selection.Lane == lane)
        {
            return lane.AssetOf(events[_selection.Index]);
        }
        return events.Count > 0 ? lane.AssetOf(events[events.Count - 1]) : null;
    }

    private void Audition(ClipEventRef target)
    {
        if (IsValid(target) && EventAt(target) is ClipSoundEvent soundEvent)
        {
            _audition.Play(soundEvent, _clip);
        }
    }

    private void AssignNewCue(ClipEventRef target)
    {
        if (!IsValid(target))
        {
            return;
        }

        AudioCue cue = AudioCueAssets.CreateWithDialog(SuggestedCueName);
        if (cue != null)
        {
            _edits.SetAsset(target.Lane, target.Index, cue);
            _owner.Repaint();
        }
    }

    private void MakeTimed(ClipEventRef target)
    {
        if (IsValid(target))
        {
            _edits.MakeTimed(target.Lane, target.Index, DefaultEndTime(EventAt(target).Time));
            _owner.Repaint();
        }
    }

    private void MakeOneShot(ClipEventRef target)
    {
        if (IsValid(target))
        {
            _edits.MakeOneShot(target.Lane, target.Index);
            _owner.Repaint();
        }
    }

    private void Remove(ClipEventRef target)
    {
        if (!IsValid(target))
        {
            return;
        }

        _edits.Remove(target.Lane, target.Index);
        RefreshTrack();
        _selection = ClipEventRef.None;
        _hover = ClipEventRef.None;
        _owner.Repaint();
    }

    private float DefaultEndTime(float time)
    {
        float length = FrameStep > 0f ? FrameStep * NewSpanFrames : FallbackSpanLength;
        return Mathf.Min(1f, time + length);
    }

    private float Snap(float time)
    {
        float frame = FrameStep;
        return frame > 0f ? Mathf.Clamp01(Mathf.Round(time / frame) * frame) : Mathf.Clamp01(time);
    }

    // Mathf.Clamp returns the upper bound when the bounds cross; the lower bound keeps start order.
    private static float ClampOrdered(float value, float lower, float upper)
    {
        return Mathf.Clamp(value, lower, Mathf.Max(lower, upper));
    }

    private ClipEvent EventAt(ClipEventRef target) => EventsIn(target.Lane)[target.Index];

    // A selected event is the only one the lanes let you drag, so a stray drag can't move another.
    private ClipEventRef Locked => IsValid(_selection) ? _selection : ClipEventRef.None;

    private bool IsValid(ClipEventRef target) =>
        target.Lane != null && target.Index >= 0 && target.Index < EventsIn(target.Lane).Count;

    private enum DragMode
    {
        None,
        Scrub,
        Move,
        Start,
        End
    }
}
