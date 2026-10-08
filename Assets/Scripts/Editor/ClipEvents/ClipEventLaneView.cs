using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Lays out, hit-tests and draws the Clip Events timeline's lanes for one track. One-shots draw as
/// markers across the whole lane; timed events draw as bars that stack into sub-rows where they overlap,
/// so the lane grows instead of hiding them.
/// </summary>
public sealed class ClipEventLaneView
{
    public const float LabelWidth = 58f;
    // GUI.HorizontalSlider thumbs stop this far inside the slider rect; the lanes use the same span
    // so they line up with the scrubber.
    public const float ThumbInset = 5f;
    private const float SubRowHeight = 22f;
    private const float LaneSpacing = 2f;
    private const float MarkerGrabDistance = 5f;
    private const float EdgeGrabDistance = 4f;

    private readonly Dictionary<ClipEventLane, LaneLayout> _layouts = new();
    private float _trackMin;
    private float _trackMax;
    private GUIStyle _markerLabelStyle;

    /// <summary>The track whose events the lanes show; null shows empty lanes.</summary>
    public ClipEventTrack Track { get; set; }

    private GUIStyle MarkerLabelStyle => _markerLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleLeft,
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
    private static Color MissingAssetColor => new Color(1f, 0.3f, 0.25f);

    /// <summary>Each asset keeps one color, so repeated sounds and effects read at a glance.</summary>
    public static Color AssetColor(Object asset)
    {
        if (asset == null)
        {
            return MissingAssetColor;
        }

        float hue = (uint)asset.name.GetHashCode() % 360 / 360f;
        return Color.HSVToRGB(hue, 0.55f, 1f);
    }

    /// <summary>The name of the asset an event plays, or a placeholder when it has none.</summary>
    public static string AssetName(ClipEventLane lane, ClipEvent clipEvent)
    {
        Object asset = lane.AssetOf(clipEvent);
        if (asset != null)
        {
            return asset.name;
        }
        return lane == ClipEventLane.Camera ? "No shake or move" : $"No {lane.AssetField}";
    }

    /// <summary>Sets the x range that normalized time 0 to 1 maps onto.</summary>
    public void SetTimeRange(float xMin, float xMax)
    {
        _trackMin = xMin;
        _trackMax = xMax;
    }

    public float TimeToX(float time) => Mathf.Lerp(_trackMin, _trackMax, Mathf.Clamp01(time));

    public float XToTime(float x) => Mathf.Clamp01(Mathf.InverseLerp(_trackMin, _trackMax, x));

    /// <summary>Reserves one GUILayout row per lane, tall enough for its stacked spans.</summary>
    public void LayoutLanes()
    {
        foreach (var lane in ClipEventLane.All)
        {
            LaneLayout layout = BuildLayout(lane);
            Rect area = GUILayoutUtility.GetRect(100f, layout.Height + LaneSpacing * 2f, GUILayout.ExpandWidth(true));
            layout.labelX = area.x + 4f;
            layout.rect = new Rect(_trackMin - ThumbInset, area.y + LaneSpacing,
                _trackMax - _trackMin + ThumbInset * 2f, layout.Height);
        }
    }

    /// <summary>The lane under a point, or null.</summary>
    public ClipEventLane LaneAt(Vector2 point)
    {
        foreach (var lane in ClipEventLane.All)
        {
            if (_layouts.TryGetValue(lane, out LaneLayout layout) && layout.rect.Contains(point))
            {
                return lane;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the event part under a point. Span edges win over markers, and markers over span
    /// bodies, so a thin target stays grabbable. While an event is locked, it is the only one that
    /// can be hit; pass <see cref="ClipEventRef.None"/> to hit any event.
    /// </summary>
    public ClipEventHit HitTest(Vector2 point, ClipEventRef locked)
    {
        ClipEventLane lane = LaneAt(point);
        if (lane == null || (locked.Lane != null && locked.Lane != lane))
        {
            return ClipEventHit.None;
        }

        LaneLayout layout = _layouts[lane];
        IReadOnlyList<ClipEvent> events = lane.Events(Track);
        int row = Mathf.FloorToInt((point.y - layout.rect.y) / SubRowHeight);
        int body = -1;
        for (int i = events.Count - 1; i >= 0; i--)
        {
            if (!events[i].IsTimed || i >= layout.subRows.Count || layout.subRows[i] != row ||
                !IsUnlocked(locked, i))
            {
                continue;
            }

            Rect bar = SpanRect(layout, i, events[i]);
            if (Mathf.Abs(point.x - bar.xMax) <= EdgeGrabDistance)
            {
                return new ClipEventHit(new ClipEventRef(lane, i), ClipEventHitPart.End);
            }
            if (Mathf.Abs(point.x - bar.x) <= EdgeGrabDistance)
            {
                return new ClipEventHit(new ClipEventRef(lane, i), ClipEventHitPart.Start);
            }
            if (body < 0 && point.x >= bar.x && point.x <= bar.xMax)
            {
                body = i;
            }
        }

        int marker = NearestMarker(events, point.x, locked);
        if (marker >= 0)
        {
            return new ClipEventHit(new ClipEventRef(lane, marker), ClipEventHitPart.Body);
        }
        return body >= 0 ? new ClipEventHit(new ClipEventRef(lane, body), ClipEventHitPart.Body) : ClipEventHit.None;
    }

    /// <summary>Draws every lane with its events, highlights and the playhead. Call on Repaint.</summary>
    public void Draw(float playhead, ClipEventRef selection, ClipEventRef hover)
    {
        foreach (var lane in ClipEventLane.All)
        {
            DrawLane(lane, _layouts[lane], playhead, selection, hover);
        }
    }

    private LaneLayout BuildLayout(ClipEventLane lane)
    {
        if (!_layouts.TryGetValue(lane, out LaneLayout layout))
        {
            layout = new LaneLayout();
            _layouts.Add(lane, layout);
        }

        // Each span takes the first sub-row whose last span has ended.
        layout.subRows.Clear();
        layout.rowEnds.Clear();
        foreach (var clipEvent in lane.Events(Track))
        {
            if (!clipEvent.IsTimed)
            {
                layout.subRows.Add(-1);
                continue;
            }

            int row = layout.rowEnds.FindIndex(end => end <= clipEvent.Time);
            if (row < 0)
            {
                row = layout.rowEnds.Count;
                layout.rowEnds.Add(0f);
            }
            layout.rowEnds[row] = clipEvent.EndTime;
            layout.subRows.Add(row);
        }
        return layout;
    }

    private void DrawLane(ClipEventLane lane, LaneLayout layout, float playhead, ClipEventRef selection, ClipEventRef hover)
    {
        Rect rect = layout.rect;
        var tooltip = $"{lane.DisplayName}s. Drag one to move it; drag a bar's edge to resize it. " +
            "While one is selected, only it can be dragged; click empty space or press Escape to deselect. " +
            "Double-click empty space to add one; right-click for more.";
        GUI.Label(new Rect(layout.labelX, rect.y, LabelWidth - 4f, SubRowHeight), new GUIContent(lane.Label, tooltip),
            EditorStyles.miniLabel);
        EditorGUI.DrawRect(new Rect(layout.labelX - 4f, rect.y, 2f, rect.height), lane.Color);
        EditorGUI.DrawRect(rect, LaneColor);

        IReadOnlyList<ClipEvent> events = lane.Events(Track);
        for (int i = 0; i < events.Count && i < layout.subRows.Count; i++)
        {
            if (events[i].IsTimed)
            {
                DrawSpan(lane, layout, events, i, selection, hover);
            }
        }
        for (int i = 0; i < events.Count; i++)
        {
            if (!events[i].IsTimed)
            {
                DrawMarker(lane, layout, events, i, selection, hover);
            }
        }

        float playheadX = TimeToX(playhead);
        EditorGUI.DrawRect(new Rect(playheadX - 0.5f, rect.y, 1f, rect.height), PlayheadColor);
    }

    private void DrawSpan(ClipEventLane lane, LaneLayout layout, IReadOnlyList<ClipEvent> events, int index,
        ClipEventRef selection, ClipEventRef hover)
    {
        ClipEvent clipEvent = events[index];
        var target = new ClipEventRef(lane, index);
        bool dimmed = IsDimmed(target, selection);
        Color color = Dim(AssetColor(lane.AssetOf(clipEvent)), dimmed);
        Rect bar = SpanRect(layout, index, clipEvent);
        EditorGUI.DrawRect(bar, new Color(color.r, color.g, color.b, color.a * 0.45f));
        EditorGUI.DrawRect(new Rect(bar.x, bar.y, 2f, bar.height), color);
        EditorGUI.DrawRect(new Rect(bar.xMax - 2f, bar.y, 2f, bar.height), color);

        string assetName = AssetName(lane, clipEvent);
        var labelRect = new Rect(bar.x + 5f, bar.y, bar.width - 9f, bar.height);
        if (labelRect.width > 12f)
        {
            GUI.Label(labelRect, assetName, MarkerLabelStyle);
        }

        DrawHighlight(target, bar, selection, hover);
        GUI.Label(bar, new GUIContent(string.Empty, $"{assetName} from {clipEvent.Time:0.000} to {clipEvent.EndTime:0.000}"));
        if (dimmed)
        {
            return;
        }
        EditorGUIUtility.AddCursorRect(bar, MouseCursor.Pan);
        EditorGUIUtility.AddCursorRect(EdgeRect(bar.x, bar), MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(EdgeRect(bar.xMax, bar), MouseCursor.ResizeHorizontal);
    }

    // A marker spans every sub-row, so one-shots stay visible above the stacked spans.
    private void DrawMarker(ClipEventLane lane, LaneLayout layout, IReadOnlyList<ClipEvent> events, int index,
        ClipEventRef selection, ClipEventRef hover)
    {
        ClipEvent clipEvent = events[index];
        var target = new ClipEventRef(lane, index);
        bool dimmed = IsDimmed(target, selection);
        Rect rect = layout.rect;
        Color color = Dim(AssetColor(lane.AssetOf(clipEvent)), dimmed);
        float x = TimeToX(clipEvent.Time);
        EditorGUI.DrawRect(new Rect(x - 1.5f, rect.y, 3f, rect.height), color);
        EditorGUI.DrawRect(new Rect(x - 4f, rect.y, 8f, 6f), color);

        string assetName = AssetName(lane, clipEvent);
        float nextX = NextMarkerX(events, index, rect.xMax);
        var labelRect = new Rect(x + 5f, rect.y + 6f, nextX - x - 9f, SubRowHeight - 6f);
        if (labelRect.width > 12f)
        {
            GUI.Label(labelRect, assetName, MarkerLabelStyle);
        }

        var grabRect = new Rect(x - MarkerGrabDistance, rect.y, MarkerGrabDistance * 2f, rect.height);
        DrawHighlight(target, grabRect, selection, hover);
        GUI.Label(grabRect, new GUIContent(string.Empty, $"{assetName} at {clipEvent.Time:0.000}"));
        if (!dimmed)
        {
            EditorGUIUtility.AddCursorRect(grabRect, MouseCursor.ResizeHorizontal);
        }
    }

    private float NextMarkerX(IReadOnlyList<ClipEvent> events, int index, float fallback)
    {
        for (int i = index + 1; i < events.Count; i++)
        {
            if (!events[i].IsTimed)
            {
                return TimeToX(events[i].Time);
            }
        }
        return fallback;
    }

    private static Color Dim(Color color, bool dimmed) => dimmed ? new Color(color.r, color.g, color.b, color.a * 0.35f) : color;

    private static void DrawHighlight(ClipEventRef target, Rect rect, ClipEventRef selection, ClipEventRef hover)
    {
        if (target.Equals(selection) || target.Equals(hover))
        {
            DrawOutline(rect, target.Equals(selection) ? OutlineColor : HoverColor);
        }
    }

    private static void DrawOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private int NearestMarker(IReadOnlyList<ClipEvent> events, float x, ClipEventRef locked)
    {
        int nearest = -1;
        float nearestDistance = MarkerGrabDistance;
        // Later markers draw on top, so they win ties.
        for (int i = events.Count - 1; i >= 0; i--)
        {
            if (events[i].IsTimed || !IsUnlocked(locked, i))
            {
                continue;
            }

            float distance = Mathf.Abs(TimeToX(events[i].Time) - x);
            if (distance < nearestDistance || (nearest < 0 && distance <= nearestDistance))
            {
                nearest = i;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    // Within the locked event's lane; other lanes are rejected before this is asked.
    private static bool IsUnlocked(ClipEventRef locked, int index) => locked.Lane == null || locked.Index == index;

    // While an event is selected, the others are dimmed to show they can't be dragged.
    private static bool IsDimmed(ClipEventRef target, ClipEventRef selection) =>
        selection.Lane != null && !target.Equals(selection);

    private Rect SpanRect(LaneLayout layout, int index, ClipEvent clipEvent)
    {
        float startX = TimeToX(clipEvent.Time);
        float endX = Mathf.Max(TimeToX(clipEvent.EndTime), startX + 2f);
        float y = layout.rect.y + layout.subRows[index] * SubRowHeight + 2f;
        return new Rect(startX, y, endX - startX, SubRowHeight - 4f);
    }

    private static Rect EdgeRect(float x, Rect bar) =>
        new(x - EdgeGrabDistance, bar.y, EdgeGrabDistance * 2f, bar.height);

    private sealed class LaneLayout
    {
        public readonly List<int> subRows = new();
        public readonly List<float> rowEnds = new();
        public Rect rect;
        public float labelX;

        public float Height => Mathf.Max(1, rowEnds.Count) * SubRowHeight;
    }
}

/// <summary>Which part of an event a point on the timeline lands on.</summary>
public enum ClipEventHitPart
{
    None,
    Body,
    Start,
    End
}

/// <summary>An event and the part of it under a point on the timeline.</summary>
public readonly struct ClipEventHit
{
    public static readonly ClipEventHit None = new(ClipEventRef.None, ClipEventHitPart.None);

    public ClipEventHit(ClipEventRef target, ClipEventHitPart part)
    {
        Target = target;
        Part = part;
    }

    public ClipEventRef Target { get; }
    public ClipEventHitPart Part { get; }
}
