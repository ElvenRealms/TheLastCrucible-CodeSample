using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Attack Timing window's timeline: a scrubber above lanes of draggable timing markers, speed
/// segments and holds, plus a panel that edits the selected item.
/// </summary>
public sealed class AttackTimeline : IDisposable
{
    private const float LabelWidth = 58f;
    private const float RightPadding = 7f;
    // GUI.HorizontalSlider thumbs stop this far inside the slider rect; the lanes use the same span
    // so every lane lines up with the scrubber.
    private const float ThumbInset = 5f;
    private const float ScrubberHeight = 28f;
    private const float MarkerLaneHeight = 18f;
    private const float SpeedLaneHeight = 22f;
    private const float HoldLaneHeight = 20f;
    private const float LaneSpacing = 2f;
    private const float MarkerGrabDistance = 5f;
    private const float EdgeGrabWidth = 6f;
    private const float MinimumBlockWidth = 6f;
    private const float SnapDistance = 8f;
    private const float DragThreshold = 3f;
    private const float SpeedPerPixel = 0.01f;
    private const float SpeedStep = 0.05f;
    private const float MinimumSpeed = 0.05f;
    private const float MaximumSpeed = 4f;
    private const float DurationStep = 0.01f;
    private const float MinimumHoldDuration = 0.05f;
    private const float MinimumSpan = 0.001f;
    private const float NewSegmentLength = 0.1f;
    private const string TrackingEndField = "trackingEnd";
    private const string HitStartField = "hitWindowStart";
    private const string HitEndField = "hitWindowEnd";
    private const string ChainTimeField = "chainTime";
    private const string SegmentsField = "speedSegments";
    private const string HoldsField = "holds";
    private static readonly int ControlHint = nameof(AttackTimeline).GetHashCode();
    private static readonly float[] SpeedPresets = { 0.5f, 0.75f, 1.25f, 1.5f };

    private readonly EditorWindow _owner;
    private readonly List<Marker> _markers = new();
    private AttackDataBase _attack;
    private BossAttackData _bossAttack;
    private AnimationClip _clip;
    private SerializedObject _serializedAttack;
    private PropertyTree _propertyTree;
    private float _trackMin;
    private float _trackMax;
    private float _playhead;
    private Rect _markerLane;
    private Rect _speedLane;
    private Rect _holdLane;
    private TimelineItem _selection;
    // The selection the panel draws; it only changes on Layout events so IMGUI sees the same
    // controls in every event of a frame.
    private TimelineItem _panelSelection;
    private TimelineItem _hover;
    private TimelineItem _drag;
    private DragMode _dragMode;
    private Vector2 _dragOrigin;
    private float _dragStart;
    private float _dragEnd;
    private float _dragSpeed;
    private bool _dragAxisChosen;
    private bool _dragChangesSpeed;
    private int _undoGroup;
    private GUIStyle _blockLabelStyle;

    public static Color TrackingColor => new Color(0.25f, 0.8f, 1f);
    public static Color HoldColor => new Color(0.95f, 0.95f, 0.95f);
    public static Color HitStartColor => new Color(1f, 0.78f, 0.2f);
    public static Color HitEndColor => new Color(1f, 0.3f, 0.25f);
    public static Color ChainColor => new Color(0.8f, 0.4f, 1f);

    /// <summary>True while the user drags on the lanes, to scrub or to edit an item.</summary>
    public bool IsDragging => _dragMode != DragMode.None;

    private float ClipLength => _clip != null ? _clip.length : 0f;
    private float FrameStep => _clip != null && _clip.length > 0f && _clip.frameRate > 0f
        ? 1f / (_clip.length * _clip.frameRate)
        : 0f;
    private float MinimumSegmentLength => Mathf.Max(FrameStep, MinimumSpan);
    private float LanesHeight => _bossAttack != null
        ? MarkerLaneHeight + SpeedLaneHeight + HoldLaneHeight + LaneSpacing * 4f
        : MarkerLaneHeight + LaneSpacing * 2f;
    private GUIStyle BlockLabelStyle => _blockLabelStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        clipping = TextClipping.Clip,
        padding = new RectOffset()
    };

    private static Color LaneColor => EditorGUIUtility.isProSkin
        ? new Color(0.15f, 0.15f, 0.15f)
        : new Color(0.62f, 0.62f, 0.62f);
    private static Color OutlineColor => EditorGUIUtility.isProSkin ? Color.white : Color.black;
    private static Color PlayheadColor => EditorGUIUtility.isProSkin
        ? new Color(1f, 1f, 1f, 0.7f)
        : new Color(0f, 0f, 0f, 0.6f);
    private static Color HoverColor => new Color(1f, 1f, 1f, 0.15f);
    private static Color HoldFillColor => new Color(0.85f, 0.85f, 0.85f, 0.35f);

    /// <summary>Faster spans read warm, slower spans cool.</summary>
    public static Color SegmentColor(AttackSpeedSegment segment) => segment.Speed >= 1f
        ? new Color(1f, 0.55f, 0.2f, 0.45f)
        : new Color(0.35f, 0.55f, 1f, 0.45f);

    public AttackTimeline(EditorWindow owner)
    {
        _owner = owner;
    }

    /// <summary>Points the timeline at an attack and clip; the selection resets when the attack changes.</summary>
    public void Bind(AttackDataBase attack, AnimationClip clip)
    {
        _clip = clip;
        // The second check catches an attack asset deleted while bound.
        if (attack != _attack || (attack == null) != (_serializedAttack == null))
        {
            _attack = attack;
            _bossAttack = attack as BossAttackData;
            _propertyTree?.Dispose();
            _serializedAttack = attack != null ? new SerializedObject(attack) : null;
            _propertyTree = _serializedAttack != null ? PropertyTree.Create(_serializedAttack) : null;
            _selection = default;
            _panelSelection = default;
            _hover = default;
            _dragMode = DragMode.None;
        }

        RebuildMarkers();
    }

    /// <summary>Draws the scrubber and lanes, and returns the normalized time to show.</summary>
    public float Draw(float normalizedTime)
    {
        _serializedAttack?.UpdateIfRequiredOrScript();
        _playhead = normalizedTime;
        Rect scrubberRow = GUILayoutUtility.GetRect(100f, ScrubberHeight, GUILayout.ExpandWidth(true));
        float time = DrawScrubber(scrubberRow, normalizedTime);
        if (_attack == null)
        {
            return time;
        }

        Rect area = GUILayoutUtility.GetRect(100f, LanesHeight, GUILayout.ExpandWidth(true));
        LayoutLanes(area);
        time = HandleLaneEvents(area, time);
        if (Event.current.type == EventType.Repaint)
        {
            DrawLanes(area);
        }
        return time;
    }

    /// <summary>Draws the selected item's settings with Odin, under a header naming it.</summary>
    public void DrawSelection()
    {
        if (Event.current.type == EventType.Layout)
        {
            _panelSelection = IsValidItem(_selection) ? _selection : default;
        }

        if (_panelSelection.Kind == ItemKind.None || !IsValidItem(_panelSelection))
        {
            return;
        }

        InspectorProperty property = SelectedProperty(_panelSelection);
        if (property == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(ItemLabel(_panelSelection), EditorStyles.boldLabel);
            if (_panelSelection.Kind != ItemKind.Marker)
            {
                var removeContent = new GUIContent("Remove", "The Delete key also removes the selected item.");
                if (GUILayout.Button(removeContent, EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    Remove(_panelSelection);
                    GUIUtility.ExitGUI();
                }
            }
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _propertyTree.BeginDraw(true);
            if (_panelSelection.Kind == ItemKind.Marker)
            {
                property.Draw();
            }
            else
            {
                for (int i = 0; i < property.Children.Count; i++)
                {
                    property.Children[i].Draw();
                }
            }
            _propertyTree.EndDraw();
        }

        if (_bossAttack != null && !_bossAttack.IsValid)
        {
            EditorGUILayout.HelpBox(
                "These timings are invalid. Speed segments need Start < End without overlapping, and holds must stay in time order.",
                MessageType.Warning);
        }
    }

    public bool CanAddHold(float time)
    {
        if (_bossAttack == null)
        {
            return false;
        }

        foreach (var hold in _bossAttack.Holds)
        {
            if (Mathf.Approximately(hold.Time, time))
            {
                return false;
            }
        }
        return true;
    }

    public bool CanAddSpeedSegment(float time) => _bossAttack != null &&
        _bossAttack.FindSpeedSegment(time) == null &&
        NextSegmentStart(time) > time;

    /// <summary>Adds a hold with default settings at a normalized time and selects it.</summary>
    public void AddHold(float time)
    {
        int index = 0;
        while (index < _bossAttack.Holds.Count && _bossAttack.Holds[index].Time <= time)
        {
            index++;
        }

        var defaults = new AttackHold();
        _serializedAttack.Update();
        SerializedProperty holds = _serializedAttack.FindProperty(HoldsField);
        holds.InsertArrayElementAtIndex(index);
        SerializedProperty hold = holds.GetArrayElementAtIndex(index);
        hold.FindPropertyRelative("time").floatValue = time;
        hold.FindPropertyRelative("duration").floatValue = defaults.Duration;
        hold.FindPropertyRelative("speed").floatValue = defaults.Speed;
        hold.FindPropertyRelative("blend").floatValue = defaults.Blend;
        _serializedAttack.ApplyModifiedProperties();
        _selection = new TimelineItem(ItemKind.Hold, index, ItemPart.Body);
    }

    /// <summary>Adds a speed segment with default settings starting at a normalized time and selects it.</summary>
    public void AddSpeedSegment(float time)
    {
        int index = 0;
        while (index < _bossAttack.SpeedSegments.Count && _bossAttack.SpeedSegments[index].Start < time)
        {
            index++;
        }

        var defaults = new AttackSpeedSegment();
        float end = Mathf.Min(time + NewSegmentLength, NextSegmentStart(time));
        _serializedAttack.Update();
        SerializedProperty segments = _serializedAttack.FindProperty(SegmentsField);
        segments.InsertArrayElementAtIndex(index);
        SerializedProperty segment = segments.GetArrayElementAtIndex(index);
        segment.FindPropertyRelative("start").floatValue = time;
        segment.FindPropertyRelative("end").floatValue = end;
        segment.FindPropertyRelative("speed").floatValue = defaults.Speed;
        segment.FindPropertyRelative("blend").floatValue = defaults.Blend;
        _serializedAttack.ApplyModifiedProperties();
        _selection = new TimelineItem(ItemKind.Segment, index, ItemPart.Body);
    }

    public void Dispose()
    {
        _propertyTree?.Dispose();
        _propertyTree = null;
    }

    private void RebuildMarkers()
    {
        _markers.Clear();
        if (_attack == null)
        {
            return;
        }

        _markers.Add(new Marker(TrackingEndField, "Tracking End", TrackingColor));
        _markers.Add(new Marker(HitStartField, "Hit Start", HitStartColor));
        _markers.Add(new Marker(HitEndField, "Hit End", HitEndColor));
        if (_bossAttack != null)
        {
            _markers.Add(new Marker(ChainTimeField, "Chain / Exit", ChainColor));
        }
    }

    private float DrawScrubber(Rect row, float normalizedTime)
    {
        var content = new GUIContent("Time", "Drag to scrub the preview.");
        GUI.Label(new Rect(row.x + 4f, row.y + 5f, LabelWidth - 4f, 18f), content, EditorStyles.miniLabel);
        var sliderRect = new Rect(row.x + LabelWidth, row.y + 5f, row.width - LabelWidth - RightPadding, 18f);
        _trackMin = sliderRect.x + ThumbInset;
        _trackMax = sliderRect.xMax - ThumbInset;
        if (Event.current.type == EventType.Repaint)
        {
            foreach (var marker in _markers)
            {
                float x = TimeToX(MarkerValue(marker));
                EditorGUI.DrawRect(new Rect(x - 1.5f, sliderRect.y - 2f, 3f, sliderRect.height + 4f), marker.Color);
            }
        }
        return GUI.HorizontalSlider(sliderRect, normalizedTime, 0f, 1f);
    }

    private void LayoutLanes(Rect area)
    {
        float x = _trackMin - ThumbInset;
        float width = _trackMax - _trackMin + ThumbInset * 2f;
        _markerLane = new Rect(x, area.y + LaneSpacing, width, MarkerLaneHeight);
        if (_bossAttack == null)
        {
            _speedLane = default;
            _holdLane = default;
            return;
        }

        _speedLane = new Rect(x, _markerLane.yMax + LaneSpacing, width, SpeedLaneHeight);
        _holdLane = new Rect(x, _speedLane.yMax + LaneSpacing, width, HoldLaneHeight);
    }

    private float HandleLaneEvents(Rect area, float time)
    {
        var inputRect = new Rect(_markerLane.x, area.y, _markerLane.width, area.height);
        int controlId = GUIUtility.GetControlID(ControlHint, FocusType.Passive, inputRect);
        Event current = Event.current;
        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown when current.button == 0 && GUI.enabled && inputRect.Contains(current.mousePosition):
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
            case EventType.ContextClick when GUI.enabled && inputRect.Contains(current.mousePosition):
                current.Use();
                ShowContextMenu(current.mousePosition);
                break;
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Delete && GUI.enabled &&
            !EditorGUIUtility.editingTextField && _selection.Kind is ItemKind.Segment or ItemKind.Hold &&
            IsValidItem(_selection))
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
        TimelineItem hit = HitTest(current.mousePosition);
        if (hit.Kind == ItemKind.None)
        {
            if (current.clickCount == 2 && TryAddAt(current.mousePosition))
            {
                return time;
            }

            _selection = default;
            _dragMode = DragMode.Scrub;
            GUIUtility.hotControl = controlId;
            return XToTime(current.mousePosition.x);
        }

        _selection = hit;
        _drag = hit;
        _dragMode = DragMode.Item;
        _dragOrigin = current.mousePosition;
        _dragAxisChosen = false;
        CaptureDragStart(hit);
        GUIUtility.hotControl = controlId;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName($"Edit {ItemLabel(hit)}");
        _undoGroup = Undo.GetCurrentGroup();
        return hit.Kind == ItemKind.Marker ? _dragStart : time;
    }

    private void CaptureDragStart(TimelineItem item)
    {
        switch (item.Kind)
        {
            case ItemKind.Marker:
                _dragStart = MarkerValue(_markers[item.Index]);
                break;
            case ItemKind.Segment:
                AttackSpeedSegment segment = _bossAttack.SpeedSegments[item.Index];
                _dragStart = segment.Start;
                _dragEnd = segment.End;
                _dragSpeed = segment.Speed;
                break;
            case ItemKind.Hold:
                AttackHold hold = _bossAttack.Holds[item.Index];
                _dragStart = hold.Time;
                _dragEnd = hold.Duration;
                break;
        }
    }

    private float ContinueDrag(Event current, float time)
    {
        if (_dragMode == DragMode.Scrub)
        {
            return XToTime(current.mousePosition.x);
        }

        if (!IsValidItem(_drag))
        {
            return time;
        }

        _serializedAttack.Update();
        return _drag.Kind switch
        {
            ItemKind.Marker => DragMarker(current),
            ItemKind.Segment => DragSegment(current, time),
            ItemKind.Hold => DragHold(current, time),
            _ => time
        };
    }

    private float DragMarker(Event current)
    {
        Marker marker = _markers[_drag.Index];
        float value = ClampMarker(marker.Field, Snap(XToTime(current.mousePosition.x)));
        SetFloat(marker.Field, value);
        return value;
    }

    private float DragSegment(Event current, float time)
    {
        IReadOnlyList<AttackSpeedSegment> segments = _bossAttack.SpeedSegments;
        int index = _drag.Index;
        float lower = index > 0 ? segments[index - 1].End : 0f;
        float upper = index < segments.Count - 1 ? segments[index + 1].Start : 1f;
        string path = ElementPath(SegmentsField, index);
        switch (_drag.Part)
        {
            case ItemPart.StartEdge:
                float start = Mathf.Clamp(Snap(XToTime(current.mousePosition.x)), lower, _dragEnd - MinimumSegmentLength);
                SetFloat(path + ".start", start);
                return start;
            case ItemPart.EndEdge:
                float end = Mathf.Clamp(Snap(XToTime(current.mousePosition.x)), _dragStart + MinimumSegmentLength, upper);
                SetFloat(path + ".end", end);
                return end;
        }

        if (!ChooseDragAxis(current))
        {
            return time;
        }

        if (_dragChangesSpeed)
        {
            float rawSpeed = _dragSpeed - (current.mousePosition.y - _dragOrigin.y) * SpeedPerPixel;
            float speed = Mathf.Clamp(Mathf.Round(rawSpeed / SpeedStep) * SpeedStep, MinimumSpeed, MaximumSpeed);
            SetFloat(path + ".speed", speed);
            return time;
        }

        float length = _dragEnd - _dragStart;
        float movedStart = Mathf.Clamp(Snap(_dragStart + DragDelta(current)), lower, upper - length);
        SetFloat(path + ".start", movedStart);
        SetFloat(path + ".end", movedStart + length);
        return time;
    }

    private float DragHold(Event current, float time)
    {
        IReadOnlyList<AttackHold> holds = _bossAttack.Holds;
        int index = _drag.Index;
        string path = ElementPath(HoldsField, index);
        if (_drag.Part == ItemPart.EndEdge)
        {
            float seconds = (XToTime(current.mousePosition.x) - _dragStart) * ClipLength;
            float duration = Mathf.Max(MinimumHoldDuration, Mathf.Round(seconds / DurationStep) * DurationStep);
            SetFloat(path + ".duration", duration);
            return time;
        }

        float lower = index > 0 ? holds[index - 1].Time : 0f;
        float upper = index < holds.Count - 1 ? holds[index + 1].Time : 1f;
        float holdTime = Mathf.Clamp(Snap(_dragStart + DragDelta(current)), lower, upper);
        SetFloat(path + ".time", holdTime);
        return holdTime;
    }

    // A speed segment body drag either moves the segment or changes its speed; the first few
    // pixels of movement pick which, so a slightly diagonal drag doesn't do both.
    private bool ChooseDragAxis(Event current)
    {
        if (_dragAxisChosen)
        {
            return true;
        }

        Vector2 offset = current.mousePosition - _dragOrigin;
        if (offset.magnitude < DragThreshold)
        {
            return false;
        }

        _dragAxisChosen = true;
        _dragChangesSpeed = Mathf.Abs(offset.y) > Mathf.Abs(offset.x);
        return true;
    }

    private void EndDrag()
    {
        GUIUtility.hotControl = 0;
        if (_dragMode == DragMode.Item)
        {
            Undo.CollapseUndoOperations(_undoGroup);
        }
        _dragMode = DragMode.None;
    }

    private void UpdateHover(Vector2 mousePosition)
    {
        TimelineItem hover = HitTest(mousePosition);
        if (!hover.Equals(_hover))
        {
            _hover = hover;
            _owner.Repaint();
        }
    }

    private bool TryAddAt(Vector2 point)
    {
        float time = Snap(XToTime(point.x));
        if (_speedLane.Contains(point) && CanAddSpeedSegment(time))
        {
            AddSpeedSegment(time);
            return true;
        }

        if (_holdLane.Contains(point) && CanAddHold(time))
        {
            AddHold(time);
            return true;
        }
        return false;
    }

    private void ShowContextMenu(Vector2 point)
    {
        TimelineItem hit = HitTest(point);
        float time = Snap(XToTime(point.x));
        var menu = new GenericMenu();
        switch (hit.Kind)
        {
            case ItemKind.Segment:
                _selection = hit;
                float currentSpeed = _bossAttack.SpeedSegments[hit.Index].Speed;
                foreach (float preset in SpeedPresets)
                {
                    string path = ElementPath(SegmentsField, hit.Index) + ".speed";
                    menu.AddItem(
                        new GUIContent($"Speed ×{preset:0.##}"),
                        Mathf.Approximately(currentSpeed, preset),
                        () => SetFloatFromMenu(path, preset));
                }
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Remove"), false, () => Remove(hit));
                break;
            case ItemKind.Hold:
                _selection = hit;
                menu.AddItem(new GUIContent("Remove"), false, () => Remove(hit));
                break;
            case ItemKind.None when _speedLane.Contains(point):
                AddMenuItem(menu, "Add Speed Segment Here", CanAddSpeedSegment(time), () => AddSpeedSegment(time));
                break;
            case ItemKind.None when _holdLane.Contains(point):
                AddMenuItem(menu, "Add Hold Here", CanAddHold(time), () => AddHold(time));
                break;
        }

        if (menu.GetItemCount() > 0)
        {
            menu.ShowAsContext();
        }
    }

    private void AddMenuItem(GenericMenu menu, string label, bool enabled, GenericMenu.MenuFunction action)
    {
        if (enabled)
        {
            menu.AddItem(new GUIContent(label), false, () =>
            {
                action();
                _owner.Repaint();
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent(label));
        }
    }

    private void SetFloatFromMenu(string path, float value)
    {
        _serializedAttack.Update();
        SetFloat(path, value);
        _owner.Repaint();
    }

    private void Remove(TimelineItem item)
    {
        if (!IsValidItem(item) || item.Kind == ItemKind.Marker)
        {
            return;
        }

        _serializedAttack.Update();
        _serializedAttack.FindProperty(item.Kind == ItemKind.Segment ? SegmentsField : HoldsField)
            .DeleteArrayElementAtIndex(item.Index);
        _serializedAttack.ApplyModifiedProperties();
        _selection = default;
        _hover = default;
        _owner.Repaint();
    }

    private void DrawLanes(Rect area)
    {
        DrawLane(area, _markerLane, "Markers",
            "Timing markers. Drag one to move it; the preview follows. Hold Ctrl to snap to other items.");
        if (_bossAttack != null)
        {
            DrawLane(area, _speedLane, "Speed",
                "Speed segments. Drag to move, drag an edge to resize, drag up or down to change speed. " +
                "Double-click empty space to add one; right-click for speed presets.");
            DrawLane(area, _holdLane, "Holds",
                "Holds. Drag to move, drag the right edge to change the duration. Double-click empty space to add one.");
            DrawMarkerGuides();
            DrawSegments();
            DrawHolds();
        }

        DrawMarkers();
        float playheadX = TimeToX(_playhead);
        float bottom = _bossAttack != null ? _holdLane.yMax : _markerLane.yMax;
        EditorGUI.DrawRect(new Rect(playheadX - 0.5f, _markerLane.y, 1f, bottom - _markerLane.y), PlayheadColor);
    }

    private static void DrawLane(Rect area, Rect lane, string label, string tooltip)
    {
        GUI.Label(new Rect(area.x + 4f, lane.y, LabelWidth - 4f, lane.height),
            new GUIContent(label, tooltip), EditorStyles.miniLabel);
        EditorGUI.DrawRect(lane, LaneColor);
    }

    private void DrawMarkerGuides()
    {
        foreach (var marker in _markers)
        {
            float x = TimeToX(MarkerValue(marker));
            Color color = marker.Color;
            color.a = 0.35f;
            EditorGUI.DrawRect(new Rect(x - 0.5f, _speedLane.y, 1f, _holdLane.yMax - _speedLane.y), color);
        }
    }

    private void DrawSegments()
    {
        IReadOnlyList<AttackSpeedSegment> segments = _bossAttack.SpeedSegments;
        for (int i = 0; i < segments.Count; i++)
        {
            AttackSpeedSegment segment = segments[i];
            Rect rect = SegmentRect(segment);
            var item = new TimelineItem(ItemKind.Segment, i, ItemPart.Body);
            DrawBlock(rect, SegmentColor(segment), item, $"×{segment.Speed:0.##}");
            GUI.Label(rect, new GUIContent(string.Empty,
                $"Speed ×{segment.Speed:0.##} from {segment.Start:0.000} to {segment.End:0.000}"));
            float edge = EdgeWidth(rect);
            EditorGUIUtility.AddCursorRect(new Rect(rect.x, rect.y, edge, rect.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(rect.xMax - edge, rect.y, edge, rect.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(rect.x + edge, rect.y, rect.width - edge * 2f, rect.height), MouseCursor.Pan);
        }
    }

    private void DrawHolds()
    {
        IReadOnlyList<AttackHold> holds = _bossAttack.Holds;
        for (int i = 0; i < holds.Count; i++)
        {
            AttackHold hold = holds[i];
            Rect rect = HoldRect(hold);
            var item = new TimelineItem(ItemKind.Hold, i, ItemPart.Body);
            DrawBlock(rect, HoldFillColor, item, $"{hold.Duration:0.##} s");
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), HoldColor);
            GUI.Label(rect, new GUIContent(string.Empty,
                $"Hold {hold.Duration:0.##} s at {hold.Time:0.000}, speed ×{hold.Speed:0.##}"));
            float edge = EdgeWidth(rect);
            EditorGUIUtility.AddCursorRect(new Rect(rect.x, rect.y, rect.width - edge, rect.height), MouseCursor.Pan);
            EditorGUIUtility.AddCursorRect(new Rect(rect.xMax - edge, rect.y, edge, rect.height), MouseCursor.ResizeHorizontal);
        }
    }

    private void DrawBlock(Rect rect, Color color, TimelineItem item, string label)
    {
        EditorGUI.DrawRect(rect, color);
        if (item.IsSameItem(_hover))
        {
            EditorGUI.DrawRect(rect, HoverColor);
        }
        if (item.IsSameItem(_selection))
        {
            DrawOutline(rect, OutlineColor);
        }
        if (BlockLabelStyle.CalcSize(new GUIContent(label)).x <= rect.width - 4f)
        {
            GUI.Label(rect, label, BlockLabelStyle);
        }
    }

    private void DrawMarkers()
    {
        for (int i = 0; i < _markers.Count; i++)
        {
            Marker marker = _markers[i];
            float value = MarkerValue(marker);
            float x = TimeToX(value);
            EditorGUI.DrawRect(new Rect(x - 1.5f, _markerLane.y, 3f, _markerLane.height), marker.Color);
            EditorGUI.DrawRect(new Rect(x - 4f, _markerLane.y, 8f, 6f), marker.Color);
            var item = new TimelineItem(ItemKind.Marker, i, ItemPart.Body);
            var grabRect = new Rect(x - MarkerGrabDistance, _markerLane.y, MarkerGrabDistance * 2f, _markerLane.height);
            if (item.IsSameItem(_selection) || item.IsSameItem(_hover))
            {
                DrawOutline(grabRect, item.IsSameItem(_selection) ? OutlineColor : HoverColor);
            }
            GUI.Label(grabRect, new GUIContent(string.Empty, $"{marker.Label} {value:0.000}"));
            EditorGUIUtility.AddCursorRect(grabRect, MouseCursor.ResizeHorizontal);
        }
    }

    private static void DrawOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private TimelineItem HitTest(Vector2 point)
    {
        if (_attack == null)
        {
            return default;
        }

        if (_markerLane.Contains(point))
        {
            return HitMarker(point.x);
        }

        if (_bossAttack == null)
        {
            return default;
        }

        if (_speedLane.Contains(point))
        {
            return HitSegment(point.x);
        }

        return _holdLane.Contains(point) ? HitHold(point.x) : default;
    }

    private TimelineItem HitMarker(float x)
    {
        int nearest = -1;
        float nearestDistance = MarkerGrabDistance;
        // Later markers draw on top, so they win ties.
        for (int i = _markers.Count - 1; i >= 0; i--)
        {
            float distance = Mathf.Abs(TimeToX(MarkerValue(_markers[i])) - x);
            if (distance < nearestDistance || (nearest < 0 && distance <= nearestDistance))
            {
                nearest = i;
                nearestDistance = distance;
            }
        }
        return nearest >= 0 ? new TimelineItem(ItemKind.Marker, nearest, ItemPart.Body) : default;
    }

    private TimelineItem HitSegment(float x)
    {
        IReadOnlyList<AttackSpeedSegment> segments = _bossAttack.SpeedSegments;
        for (int i = 0; i < segments.Count; i++)
        {
            Rect rect = SegmentRect(segments[i]);
            if (x < rect.x || x > rect.xMax)
            {
                continue;
            }

            float edge = EdgeWidth(rect);
            ItemPart part = x - rect.x <= edge ? ItemPart.StartEdge
                : rect.xMax - x <= edge ? ItemPart.EndEdge
                : ItemPart.Body;
            return new TimelineItem(ItemKind.Segment, i, part);
        }
        return default;
    }

    private TimelineItem HitHold(float x)
    {
        IReadOnlyList<AttackHold> holds = _bossAttack.Holds;
        // Later holds draw on top where long ones overlap.
        for (int i = holds.Count - 1; i >= 0; i--)
        {
            Rect rect = HoldRect(holds[i]);
            if (x < rect.x || x > rect.xMax)
            {
                continue;
            }

            ItemPart part = rect.xMax - x <= EdgeWidth(rect) ? ItemPart.EndEdge : ItemPart.Body;
            return new TimelineItem(ItemKind.Hold, i, part);
        }
        return default;
    }

    private Rect SegmentRect(AttackSpeedSegment segment)
    {
        float xMin = TimeToX(segment.Start);
        float xMax = TimeToX(segment.End);
        return new Rect(xMin, _speedLane.y + 2f, Mathf.Max(xMax - xMin, 2f), _speedLane.height - 4f);
    }

    // A hold's width shows its duration against the clip at 1x, capped at the lane's end.
    private Rect HoldRect(AttackHold hold)
    {
        float xMin = TimeToX(hold.Time);
        float span = ClipLength > 0f ? hold.Duration / ClipLength * (_trackMax - _trackMin) : 0f;
        float width = Mathf.Min(Mathf.Max(span, MinimumBlockWidth), _holdLane.xMax - xMin);
        return new Rect(xMin, _holdLane.y + 2f, width, _holdLane.height - 4f);
    }

    private static float EdgeWidth(Rect rect) => Mathf.Min(EdgeGrabWidth, rect.width / 3f);

    private float Snap(float time)
    {
        if (EditorGUI.actionKey && TryFindSnapTarget(time, out float target))
        {
            return target;
        }

        float frame = FrameStep;
        return frame > 0f ? Mathf.Clamp01(Mathf.Round(time / frame) * frame) : Mathf.Clamp01(time);
    }

    private bool TryFindSnapTarget(float time, out float target)
    {
        target = time;
        float bestDistance = SnapDistance;
        foreach (float candidate in SnapCandidates())
        {
            float distance = Mathf.Abs(TimeToX(candidate) - TimeToX(time));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                target = candidate;
            }
        }
        return bestDistance < SnapDistance;
    }

    private IEnumerable<float> SnapCandidates()
    {
        for (int i = 0; i < _markers.Count; i++)
        {
            if (_drag.Kind != ItemKind.Marker || _drag.Index != i)
            {
                yield return MarkerValue(_markers[i]);
            }
        }

        if (_bossAttack == null)
        {
            yield break;
        }

        for (int i = 0; i < _bossAttack.SpeedSegments.Count; i++)
        {
            if (_drag.Kind != ItemKind.Segment || _drag.Index != i)
            {
                yield return _bossAttack.SpeedSegments[i].Start;
                yield return _bossAttack.SpeedSegments[i].End;
            }
        }

        for (int i = 0; i < _bossAttack.Holds.Count; i++)
        {
            if (_drag.Kind != ItemKind.Hold || _drag.Index != i)
            {
                yield return _bossAttack.Holds[i].Time;
            }
        }
    }

    private float ClampMarker(string field, float value)
    {
        float gap = Mathf.Max(FrameStep, MinimumSpan);
        float clamped = field switch
        {
            HitStartField => Mathf.Min(value, MarkerValue(HitEndField) - gap),
            HitEndField => Mathf.Clamp(
                value,
                MarkerValue(HitStartField) + gap,
                _bossAttack != null ? MarkerValue(ChainTimeField) : 1f),
            ChainTimeField => Mathf.Max(value, MarkerValue(HitEndField)),
            _ => value
        };
        return Mathf.Clamp01(clamped);
    }

    private bool IsValidItem(TimelineItem item) => item.Kind switch
    {
        ItemKind.Marker => item.Index < _markers.Count,
        ItemKind.Segment => _bossAttack != null && item.Index < _bossAttack.SpeedSegments.Count,
        ItemKind.Hold => _bossAttack != null && item.Index < _bossAttack.Holds.Count,
        _ => false
    };

    private InspectorProperty SelectedProperty(TimelineItem item) => item.Kind switch
    {
        ItemKind.Marker => _propertyTree.GetPropertyAtUnityPath(_markers[item.Index].Field),
        ItemKind.Segment => _propertyTree.GetPropertyAtUnityPath(ElementPath(SegmentsField, item.Index)),
        ItemKind.Hold => _propertyTree.GetPropertyAtUnityPath(ElementPath(HoldsField, item.Index)),
        _ => null
    };

    private string ItemLabel(TimelineItem item) => item.Kind switch
    {
        ItemKind.Marker => _markers[item.Index].Label,
        ItemKind.Segment => $"Speed Segment {item.Index + 1}",
        ItemKind.Hold => $"Hold {item.Index + 1}",
        _ => string.Empty
    };

    private float NextSegmentStart(float time)
    {
        foreach (var segment in _bossAttack.SpeedSegments)
        {
            if (segment.Start >= time)
            {
                return segment.Start;
            }
        }
        return 1f;
    }

    private float MarkerValue(Marker marker) => MarkerValue(marker.Field);

    private float MarkerValue(string field) => _serializedAttack.FindProperty(field).floatValue;

    private void SetFloat(string path, float value)
    {
        _serializedAttack.FindProperty(path).floatValue = value;
        _serializedAttack.ApplyModifiedProperties();
    }

    private static string ElementPath(string listField, int index) => $"{listField}.Array.data[{index}]";

    private float DragDelta(Event current) => (current.mousePosition.x - _dragOrigin.x) / (_trackMax - _trackMin);

    private float TimeToX(float time) => Mathf.Lerp(_trackMin, _trackMax, Mathf.Clamp01(time));

    private float XToTime(float x) => Mathf.Clamp01(Mathf.InverseLerp(_trackMin, _trackMax, x));

    private enum ItemKind
    {
        None,
        Marker,
        Segment,
        Hold
    }

    private enum ItemPart
    {
        Body,
        StartEdge,
        EndEdge
    }

    private enum DragMode
    {
        None,
        Scrub,
        Item
    }

    private readonly struct Marker
    {
        public readonly string Field;
        public readonly string Label;
        public readonly Color Color;

        public Marker(string field, string label, Color color)
        {
            Field = field;
            Label = label;
            Color = color;
        }
    }

    private readonly struct TimelineItem : IEquatable<TimelineItem>
    {
        public readonly ItemKind Kind;
        public readonly int Index;
        public readonly ItemPart Part;

        public TimelineItem(ItemKind kind, int index, ItemPart part)
        {
            Kind = kind;
            Index = index;
            Part = part;
        }

        public bool IsSameItem(TimelineItem other) => Kind != ItemKind.None && Kind == other.Kind && Index == other.Index;

        public bool Equals(TimelineItem other) => Kind == other.Kind && Index == other.Index && Part == other.Part;

        public override bool Equals(object obj) => obj is TimelineItem other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Index, Part);
    }
}
