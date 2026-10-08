using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Previews an animation clip on an isolated model and places clip events on its timeline,
/// stored in a <see cref="ClipEventLibrary"/>.
/// </summary>
public sealed class ClipEventsWindow : OdinEditorWindow
{
    private const string DefaultModelPath = "Assets/Prefabs/Enemies/Boss Demon.prefab";
    private const string Title = "Clip Events";

    [BoxGroup("Source"), SerializeField, AssetsOnly, Required, LabelText("Event Library")]
    [OnValueChanged(nameof(OnSourceChanged))]
    private ClipEventLibrary library;

    [BoxGroup("Source"), SerializeField, AssetsOnly, LabelText("Animation Clip")]
    [ValueDropdown(nameof(LibraryClips), AppendNextDrawer = true)]
    [Tooltip("Any clip. The dropdown lists the clips that already have events.")]
    [OnValueChanged(nameof(OnSourceChanged))]
    private AnimationClip animationClip;

    [BoxGroup("Source"), SerializeField, AssetsOnly, Required, LabelText("Preview Model")]
    [OnValueChanged(nameof(OnModelChanged))]
    private GameObject modelPrefab;

    [SerializeField, HideInInspector]
    private AudioCue newEventCue;

    [SerializeField, HideInInspector]
    private GameObject newEventPrefab;

    [SerializeField, HideInInspector]
    private CameraShakeData newEventShake;

    [SerializeField, HideInInspector]
    private CameraMotionData newEventMotion;

    [SerializeField, HideInInspector]
    private bool newEventIsMove;

    [SerializeField, HideInInspector]
    private int newEventLane;

    [SerializeField, HideInInspector]
    private bool newEventTimed;

    [SerializeField, HideInInspector]
    private bool loop = true;

    [SerializeField, HideInInspector]
    private bool auditionDuringPlayback = true;

    [SerializeField, HideInInspector]
    private bool showVfx = true;

    [SerializeField, HideInInspector]
    private float normalizedTime;

    [SerializeField, HideInInspector]
    private AnimationClipPreview preview = new();

    private readonly ClipEventAudition _audition = new();
    private readonly ClipEventVfxPreview _vfxPreview = new();
    private ClipEventTimeline _timeline;
    private bool _isPlaying;
    private double _lastUpdateTime;
    private GUIStyle _timeStyle;

    public override bool UseScrollView { get; set; } = true;

    private bool HasValidClip => AnimationClipPreview.IsSampleable(animationClip);
    private bool IsEditModeUnavailable => EditorApplication.isPlayingOrWillChangePlaymode;
    private bool CanEdit => HasValidClip && !IsEditModeUnavailable;
    private GUIStyle TimeStyle => _timeStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };

    [MenuItem("Tools/Boss Fight/Clip Events")]
    private static void ShowWindow()
    {
        Open(null);
    }

    /// <summary>Opens the window with a library selected, or keeps the current one when null.</summary>
    public static void Open(ClipEventLibrary selectedLibrary)
    {
        var window = GetWindow<ClipEventsWindow>();
        if (selectedLibrary != null)
        {
            window.library = selectedLibrary;
            window.OnSourceChanged();
        }
        window.position = EditorWindowPlacement.CenteredRect(760f, 820f);
        window.Show();
        window.Focus();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        titleContent = new GUIContent(Title);
        minSize = new Vector2(560f, 620f);
        wantsMouseMove = true;
        _timeline = new ClipEventTimeline(this, _audition);
        preview.Initialize(this);
        preview.BeforeRender += UpdateVfxPreview;
        if (modelPrefab == null)
        {
            modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModelPath);
        }

        AssemblyReloadEvents.beforeAssemblyReload += preview.Cleanup;
        AssemblyReloadEvents.beforeAssemblyReload += DisposeTimeline;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnEditorUpdate;
    }

    protected override void OnDestroy()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= preview.Cleanup;
        AssemblyReloadEvents.beforeAssemblyReload -= DisposeTimeline;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
        preview.BeforeRender -= UpdateVfxPreview;
        _vfxPreview.Clear();
        preview.Cleanup();
        DisposeTimeline();
        base.OnDestroy();
    }

    private void UpdateVfxPreview()
    {
        if (showVfx)
        {
            _vfxPreview.Update(preview, animationClip, _timeline.Track, normalizedTime);
        }
        else
        {
            _vfxPreview.Clear();
        }
    }

    // A domain reload skips OnDestroy, and OnEnable then builds a new timeline, so the old one is disposed first.
    private void DisposeTimeline()
    {
        _timeline?.Dispose();
    }

    [OnInspectorGUI, PropertyOrder(100)]
    private void DrawEventTool()
    {
        if (IsEditModeUnavailable)
        {
            EditorGUILayout.HelpBox($"{Title} is available in Edit Mode only.", MessageType.Info);
        }

        DrawSourceGuidance();
        preview.Draw(animationClip, modelPrefab, normalizedTime);
        DrawTimeline();
        DrawPlaybackControls();
        _timeline.DrawSelection();
        DrawAddControls();
        DrawEventList();
    }

    private void DrawSourceGuidance()
    {
        if (library == null)
        {
            EditorGUILayout.HelpBox(
                "Choose the character's clip event library. Create one with Create > BossFight > Clip Event Library.",
                MessageType.Info);
            return;
        }

        if (animationClip == null)
        {
            EditorGUILayout.HelpBox("Choose an animation clip to place events on.", MessageType.Info);
            return;
        }

        AnimationClipPreview.DrawGuidance(animationClip, modelPrefab);
    }

    private void DrawTimeline()
    {
        _timeline.Bind(library, animationClip);
        using (new EditorGUI.DisabledScope(!CanEdit))
        {
            float value = _timeline.Draw(normalizedTime);
            if (_timeline.IsDragging && _isPlaying)
            {
                SetPlaying(false);
            }
            if (value != normalizedTime)
            {
                SetNormalizedTime(value);
            }
        }

        float typedTime = AnimationClipPreview.DrawTimeReadout(animationClip, normalizedTime, this);
        if (typedTime != normalizedTime)
        {
            SetNormalizedTime(typedTime);
        }
    }

    private void DrawPlaybackControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(!CanEdit))
        {
            if (GUILayout.Button("◀ Frame", GUILayout.Height(26f)))
            {
                StepFrame(-1);
            }

            if (GUILayout.Button(_isPlaying ? "Pause" : "Play", GUILayout.Height(26f)))
            {
                SetPlaying(!_isPlaying);
            }

            if (GUILayout.Button("Frame ▶", GUILayout.Height(26f)))
            {
                StepFrame(1);
            }

            loop = GUILayout.Toggle(loop, "Loop", EditorStyles.miniButton, GUILayout.Width(58f), GUILayout.Height(26f));

            using (new EditorGUI.DisabledScope(!EditorAudioPreview.IsAvailable))
            {
                var auditionContent = new GUIContent("Sounds", "Play each sound event as playback passes it. Timed sounds stop at their end time.");
                auditionDuringPlayback = GUILayout.Toggle(
                    auditionDuringPlayback, auditionContent, EditorStyles.miniButton,
                    GUILayout.Width(64f), GUILayout.Height(26f));
            }

            var vfxContent = new GUIContent("VFX", "Show VFX events in the preview, simulated up to the playhead.");
            showVfx = GUILayout.Toggle(showVfx, vfxContent, EditorStyles.miniButton, GUILayout.Width(48f), GUILayout.Height(26f));

            if (GUILayout.Button("Frame Model", GUILayout.Width(92f), GUILayout.Height(26f)))
            {
                preview.FrameModel();
            }
        }
    }

    private void DrawAddControls()
    {
        if (library == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Add at Current Position", EditorStyles.boldLabel);
        IReadOnlyList<ClipEventLane> lanes = ClipEventLane.All;
        var laneLabels = new string[lanes.Count];
        for (int i = 0; i < lanes.Count; i++)
        {
            laneLabels[i] = lanes[i].Label;
        }
        newEventLane = Mathf.Clamp(newEventLane, 0, lanes.Count - 1);

        using (new EditorGUILayout.HorizontalScope())
        {
            newEventLane = GUILayout.Toolbar(newEventLane, laneLabels, EditorStyles.miniButton, GUILayout.Width(170f), GUILayout.Height(20f));
            ClipEventLane lane = lanes[newEventLane];
            Object asset = DrawNewEventAsset(lane);
            using (new EditorGUI.DisabledScope(!lane.SupportsSpans))
            {
                var timedContent = new GUIContent("Timed", "Add a span that starts 10 frames long, instead of a one-shot. Camera events are always one-shots.");
                newEventTimed = GUILayout.Toggle(newEventTimed, timedContent, EditorStyles.miniButton, GUILayout.Width(52f), GUILayout.Height(20f));
            }
            using (new EditorGUI.DisabledScope(!CanEdit || !_timeline.CanAddEvent || asset == null))
            {
                if (GUILayout.Button($"Add {lane.DisplayName}", GUILayout.Width(120f), GUILayout.Height(20f)))
                {
                    _timeline.AddEvent(lane, normalizedTime, asset, newEventTimed);
                }
            }
        }
    }

    private Object DrawNewEventAsset(ClipEventLane lane)
    {
        if (lane == ClipEventLane.Sound)
        {
            newEventCue = (AudioCue)EditorGUILayout.ObjectField(newEventCue, typeof(AudioCue), false, GUILayout.Height(20f));
            var newContent = new GUIContent("New", "Create an empty audio cue and use it for the next sound event.");
            if (GUILayout.Button(newContent, EditorStyles.miniButton, GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                AudioCue cue = AudioCueAssets.CreateWithDialog(_timeline.SuggestedCueName);
                if (cue != null)
                {
                    newEventCue = cue;
                }
                // The save dialog ran inside this GUI pass, so the rest of it is stale.
                GUIUtility.ExitGUI();
            }
            return newEventCue;
        }

        if (lane == ClipEventLane.Camera)
        {
            var moveContent = new GUIContent("Move", "Add a directed camera move instead of a shake. The event's settings take both.");
            newEventIsMove = GUILayout.Toggle(newEventIsMove, moveContent, EditorStyles.miniButton, GUILayout.Width(48f), GUILayout.Height(20f));
            if (newEventIsMove)
            {
                newEventMotion = (CameraMotionData)EditorGUILayout.ObjectField(newEventMotion, typeof(CameraMotionData), false, GUILayout.Height(20f));
                return newEventMotion;
            }

            newEventShake = (CameraShakeData)EditorGUILayout.ObjectField(newEventShake, typeof(CameraShakeData), false, GUILayout.Height(20f));
            return newEventShake;
        }

        newEventPrefab = (GameObject)EditorGUILayout.ObjectField(newEventPrefab, typeof(GameObject), false, GUILayout.Height(20f));
        return newEventPrefab;
    }

    private void DrawEventList()
    {
        foreach (var lane in ClipEventLane.All)
        {
            IReadOnlyList<ClipEvent> events = _timeline.EventsIn(lane);
            if (events.Count == 0)
            {
                continue;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"{lane.DisplayName}s", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < events.Count; i++)
                {
                    DrawEventRow(lane, events[i], i);
                }
            }
        }
    }

    private void DrawEventRow(ClipEventLane lane, ClipEvent clipEvent, int index)
    {
        Rect row = EditorGUILayout.GetControlRect(false, 18f);
        var swatch = new Rect(row.x, row.y + 4f, 10f, 10f);
        EditorGUI.DrawRect(swatch, ClipEventLaneView.AssetColor(lane.AssetOf(clipEvent)));
        var nameRect = new Rect(swatch.xMax + 5f, row.y, row.width - 120f, row.height);
        var timeRect = new Rect(row.xMax - 96f, row.y, 96f, row.height);
        GUIStyle style = _timeline.IsSelected(lane, index) ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel;
        var nameContent = new GUIContent(ClipEventLaneView.AssetName(lane, clipEvent), "Select this event and move the preview to it.");
        if (GUI.Button(nameRect, nameContent, style))
        {
            _timeline.Select(lane, index);
            SetPlaying(false);
            SetNormalizedTime(clipEvent.Time);
        }

        string times = clipEvent.IsTimed ? $"{clipEvent.Time:0.000} - {clipEvent.EndTime:0.000}" : clipEvent.Time.ToString("0.000");
        GUI.Label(timeRect, times, TimeStyle);
    }

    private IEnumerable<ValueDropdownItem<AnimationClip>> LibraryClips()
    {
        if (library == null)
        {
            yield break;
        }

        foreach (var track in library.Tracks)
        {
            if (track.Clip != null)
            {
                yield return new ValueDropdownItem<AnimationClip>(
                    $"{track.Clip.name} ({track.EventCount})", track.Clip);
            }
        }
    }

    private void OnSourceChanged()
    {
        SetPlaying(false);
        SetNormalizedTime(0f);
    }

    private void OnModelChanged()
    {
        SetPlaying(false);
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
        {
            SetPlaying(false);
            preview.Cleanup();
        }
    }

    private void OnEditorUpdate()
    {
        _audition.Update();
        if (!_isPlaying || !HasValidClip || IsEditModeUnavailable)
        {
            if (IsEditModeUnavailable)
            {
                SetPlaying(false);
            }
            return;
        }

        double currentTime = EditorApplication.timeSinceStartup;
        float deltaTime = (float)(currentTime - _lastUpdateTime);
        _lastUpdateTime = currentTime;
        float previousTime = normalizedTime;
        float nextTime = normalizedTime + deltaTime / animationClip.length;
        bool wrapped = false;

        if (nextTime >= 1f)
        {
            if (loop)
            {
                nextTime %= 1f;
                wrapped = true;
            }
            else
            {
                nextTime = 1f;
                SetPlaying(false);
            }
        }

        AuditionCrossedEvents(previousTime, nextTime, wrapped);
        SetNormalizedTime(nextTime);
    }

    private void AuditionCrossedEvents(float previousTime, float nextTime, bool wrapped)
    {
        if (!auditionDuringPlayback)
        {
            return;
        }

        foreach (var soundEvent in _timeline.SoundEvents)
        {
            float time = soundEvent.Time;
            bool crossed = wrapped
                ? time > previousTime || time <= nextTime
                : time > previousTime && time <= nextTime;
            if (crossed)
            {
                _audition.Play(soundEvent, animationClip);
            }
        }
    }

    private void SetPlaying(bool value)
    {
        _isPlaying = value && HasValidClip;
        _lastUpdateTime = EditorApplication.timeSinceStartup;
        // Starting on an event's exact time should still play it.
        if (_isPlaying && auditionDuringPlayback)
        {
            foreach (var soundEvent in _timeline.SoundEvents)
            {
                if (Mathf.Approximately(soundEvent.Time, normalizedTime))
                {
                    _audition.Play(soundEvent, animationClip);
                }
            }
        }
    }

    private void StepFrame(int direction)
    {
        if (!HasValidClip || animationClip.frameRate <= 0f)
        {
            return;
        }

        SetPlaying(false);
        float normalizedFrame = 1f / (animationClip.length * animationClip.frameRate);
        SetNormalizedTime(normalizedTime + normalizedFrame * direction);
    }

    private void SetNormalizedTime(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return;
        }

        normalizedTime = Mathf.Clamp01(value);
        preview.Sample(normalizedTime);
        Repaint();
    }
}

/// <summary>Adds direct access to the Clip Events window from clip event library assets.</summary>
[CustomEditor(typeof(ClipEventLibrary))]
public sealed class ClipEventLibraryEditor : OdinEditor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open Clip Events", GUILayout.Height(28f)))
        {
            ClipEventsWindow.Open((ClipEventLibrary)target);
        }

        EditorGUILayout.Space(4f);
        base.OnInspectorGUI();
    }
}
