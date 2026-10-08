using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Previews an attack clip on an isolated model and writes normalized timing values back to its asset.
/// </summary>
public sealed class AttackTimingWindow : OdinEditorWindow
{
    private const string DefaultModelPath = "Assets/Prefabs/Enemies/Boss Demon.prefab";

    [BoxGroup("Source"), SerializeField, AssetsOnly, LabelText("Attack Asset")]
    [OnValueChanged(nameof(OnAttackChanged))]
    private AttackDataBase attack;

    [BoxGroup("Source"), SerializeField, AssetsOnly, LabelText("Animation Clip")]
    [EnableIf(nameof(CanEditClip)), OnValueChanged(nameof(OnClipChanged))]
    private AnimationClip animationClip;

    [BoxGroup("Source"), SerializeField, AssetsOnly, Required, LabelText("Preview Model")]
    [OnValueChanged(nameof(OnModelChanged))]
    private GameObject modelPrefab;

    [SerializeField, HideInInspector]
    private bool loop = true;

    [SerializeField, HideInInspector]
    private bool previewAuthoredSpeed = true;

    [SerializeField, HideInInspector]
    private float normalizedTime;

    [SerializeField, HideInInspector]
    private AnimationClipPreview preview = new();

    private bool _isPlaying;
    private double _lastUpdateTime;
    private string _statusMessage;
    private readonly AttackPlayback _playback = new();
    private AttackTimeline _timeline;
    // Normalized time the preview playback last advanced to; any other value means the
    // user scrubbed, so playback restarts from the new time. -1 forces a restart.
    private float _playbackTime = -1f;

    public override bool UseScrollView { get; set; } = true;

    private bool CanEditClip => attack == null;
    private bool HasValidClip => AnimationClipPreview.IsSampleable(animationClip);
    private bool IsEditModeUnavailable => EditorApplication.isPlayingOrWillChangePlaymode;
    private bool CanApplyHitStart => CanApplyTimings && normalizedTime < attack.HitWindowEnd;
    private bool CanApplyHitEnd => CanApplyTimings &&
        normalizedTime > attack.HitWindowStart &&
        (attack is not BossAttackData bossAttack || normalizedTime <= bossAttack.ChainTime);
    private bool CanApplyChainTime => CanApplyTimings &&
        attack is BossAttackData &&
        normalizedTime >= attack.HitWindowEnd;
    private bool CanApplyTimings => attack != null && HasValidClip && !IsEditModeUnavailable;
    private bool CanAddHold => CanApplyTimings && _timeline.CanAddHold(normalizedTime);
    private bool CanAddSpeedSegment => CanApplyTimings && _timeline.CanAddSpeedSegment(normalizedTime);

    [MenuItem("Tools/Boss Fight/Attack Timing")]
    private static void ShowWindow()
    {
        var window = GetWindow<AttackTimingWindow>();
        window.titleContent = new GUIContent("Attack Timing");
        window.minSize = new Vector2(560f, 620f);
        window.position = EditorWindowPlacement.CenteredRect(760f, 820f);
        window.Show();
    }

    /// <summary>
    /// Opens the timing window with the supplied attack selected. When the attack is shown
    /// inside a prefab's inspector, that prefab becomes the preview model.
    /// </summary>
    public static void Open(AttackDataBase selectedAttack, GameObject context)
    {
        var window = GetWindow<AttackTimingWindow>();
        window.titleContent = new GUIContent("Attack Timing");
        window.minSize = new Vector2(560f, 620f);
        window.attack = selectedAttack;
        window.animationClip = selectedAttack != null ? selectedAttack.AnimationClip : null;
        GameObject contextModel = FindPrefabAsset(context);
        if (contextModel != null && contextModel.GetComponentInChildren<Animator>(true) != null)
        {
            window.modelPrefab = contextModel;
        }
        window.normalizedTime = 0f;
        window._statusMessage = null;
        window.SetPlaying(false);
        window.position = EditorWindowPlacement.CenteredRect(760f, 820f);
        window.Show();
        window.Focus();
    }

    // Resolves a selected object to the prefab asset it belongs to: the prefab open in Prefab Mode,
    // the prefab asset itself, or the source of an outermost scene instance.
    private static GameObject FindPrefabAsset(GameObject context)
    {
        if (context == null)
        {
            return null;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(context);
        if (prefabStage != null)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabStage.assetPath);
        }

        if (EditorUtility.IsPersistent(context))
        {
            return context.transform.root.gameObject;
        }

        GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(context);
        return instanceRoot != null ? PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot) : null;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        titleContent = new GUIContent("Attack Timing");
        minSize = new Vector2(560f, 620f);
        wantsMouseMove = true;
        _timeline = new AttackTimeline(this);
        preview.Initialize(this);
        if (modelPrefab == null)
        {
            modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModelPath);
        }

        AssemblyReloadEvents.beforeAssemblyReload += preview.Cleanup;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnEditorUpdate;
    }

    protected override void OnDestroy()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= preview.Cleanup;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
        preview.Cleanup();
        _timeline?.Dispose();
        base.OnDestroy();
    }

    [OnInspectorGUI, PropertyOrder(100)]
    private void DrawTimingTool()
    {
        if (IsEditModeUnavailable)
        {
            EditorGUILayout.HelpBox(
                "Attack Timing Preview is available in Edit Mode only.",
                MessageType.Info);
        }

        DrawSourceGuidance();
        preview.Draw(animationClip, modelPrefab, normalizedTime);
        DrawTimeline();
        DrawPlaybackControls();
        _timeline.DrawSelection();
        DrawTimingSummary();
        DrawApplyControls();

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
        }
    }

    private void DrawSourceGuidance()
    {
        if (animationClip == null)
        {
            EditorGUILayout.HelpBox("Choose an attack asset or animation clip to begin.", MessageType.Info);
            return;
        }

        AnimationClipPreview.DrawGuidance(animationClip, modelPrefab);
    }

    private void DrawTimeline()
    {
        _timeline.Bind(attack, animationClip);
        using (new EditorGUI.DisabledScope(!HasValidClip || IsEditModeUnavailable))
        {
            float value = _timeline.Draw(normalizedTime);
            if (_timeline.IsDragging && _isPlaying)
            {
                SetPlaying(false);
            }
            if (value != normalizedTime)
            {
                SetNormalizedTime(value, true);
            }
        }

        float typedTime = AnimationClipPreview.DrawTimeReadout(animationClip, normalizedTime, this);
        if (typedTime != normalizedTime)
        {
            SetNormalizedTime(typedTime, true);
        }
    }

    private void DrawPlaybackControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(!HasValidClip || IsEditModeUnavailable))
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

            using (new EditorGUI.DisabledScope(attack is not BossAttackData))
            {
                var authoredSpeedContent = new GUIContent(
                    "Authored Speed",
                    "Play the preview at the attack's speed, with its speed segments, holds and blends.");
                previewAuthoredSpeed = GUILayout.Toggle(
                    previewAuthoredSpeed, authoredSpeedContent, EditorStyles.miniButton,
                    GUILayout.Width(104f), GUILayout.Height(26f));
            }

            if (GUILayout.Button("Frame Model", GUILayout.Width(92f), GUILayout.Height(26f)))
            {
                preview.FrameModel();
            }
        }
    }

    private void DrawTimingSummary()
    {
        if (attack == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Attack Markers", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawMarkerRow("Tracking End", attack.TrackingEnd, AttackTimeline.TrackingColor);
            DrawMarkerRow("Hit Start", attack.HitWindowStart, AttackTimeline.HitStartColor);
            DrawMarkerRow("Hit End", attack.HitWindowEnd, AttackTimeline.HitEndColor);
            if (attack is BossAttackData bossAttack)
            {
                DrawMarkerRow("Chain / Exit", bossAttack.ChainTime, AttackTimeline.ChainColor);
                foreach (var segment in bossAttack.SpeedSegments)
                {
                    DrawRangeRow($"Speed ×{segment.Speed:0.##}", segment.Start, segment.End,
                        AttackTimeline.SegmentColor(segment));
                }
                foreach (var hold in bossAttack.Holds)
                {
                    DrawMarkerRow($"Hold {hold.Duration:0.##} s", hold.Time, AttackTimeline.HoldColor);
                }
            }
        }

        if (attack is BossAttackData timedAttack && timedAttack.IsValid)
        {
            EditorGUILayout.LabelField(
                "Hit lands after",
                $"{timedAttack.GetElapsedSeconds(attack.HitWindowStart):0.000} s of game time",
                EditorStyles.miniLabel);
        }
    }

    private void DrawApplyControls()
    {
        if (attack == null)
        {
            EditorGUILayout.HelpBox("Select an attack asset to apply the current time to its timing fields.", MessageType.None);
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Apply Current Position", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!CanApplyHitStart))
            {
                if (GUILayout.Button("Set Hit Start", GUILayout.Height(28f)))
                {
                    ApplyTime("hitWindowStart", "Hit Start");
                }
            }

            using (new EditorGUI.DisabledScope(!CanApplyHitEnd))
            {
                if (GUILayout.Button("Set Hit End", GUILayout.Height(28f)))
                {
                    ApplyTime("hitWindowEnd", "Hit End");
                }
            }

            using (new EditorGUI.DisabledScope(!CanApplyTimings))
            {
                if (GUILayout.Button("Set Tracking End", GUILayout.Height(28f)))
                {
                    ApplyTime("trackingEnd", "Tracking End");
                }
            }

            if (attack is BossAttackData)
            {
                using (new EditorGUI.DisabledScope(!CanApplyChainTime))
                {
                    if (GUILayout.Button("Set Chain / Exit", GUILayout.Height(28f)))
                    {
                        ApplyTime("chainTime", "Chain / Exit");
                    }
                }
            }
        }

        if (attack is BossAttackData)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!CanAddHold))
                {
                    if (GUILayout.Button("Add Hold", GUILayout.Height(28f)))
                    {
                        _timeline.AddHold(normalizedTime);
                    }
                }

                using (new EditorGUI.DisabledScope(!CanAddSpeedSegment))
                {
                    if (GUILayout.Button("Add Speed Segment", GUILayout.Height(28f)))
                    {
                        _timeline.AddSpeedSegment(normalizedTime);
                    }
                }
            }
        }

        if (CanApplyTimings && attack is BossAttackData &&
            (!CanApplyHitStart || !CanApplyHitEnd || !CanApplyChainTime))
        {
            EditorGUILayout.LabelField(
                "Timing order is guarded: Hit Start < Hit End ≤ Chain / Exit.",
                EditorStyles.centeredGreyMiniLabel);
        }
        else if (CanApplyTimings && attack is not BossAttackData && (!CanApplyHitStart || !CanApplyHitEnd))
        {
            EditorGUILayout.LabelField(
                "Timing order is guarded: Hit Start < Hit End.",
                EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void OnAttackChanged()
    {
        SetPlaying(false);
        _statusMessage = null;
        animationClip = attack != null ? attack.AnimationClip : animationClip;
        SetNormalizedTime(0f, false);
    }

    private void OnClipChanged()
    {
        SetPlaying(false);
        _statusMessage = null;
        SetNormalizedTime(0f, false);
    }

    private void OnModelChanged()
    {
        SetPlaying(false);
        _statusMessage = null;
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
        float nextTime = normalizedTime + deltaTime * PreviewSpeed(deltaTime) / animationClip.length;
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

        SetNormalizedTime(nextTime, true);
        _playbackTime = wrapped ? -1f : normalizedTime;
        Repaint();
    }

    private float PreviewSpeed(float deltaTime)
    {
        if (!previewAuthoredSpeed || attack is not BossAttackData bossAttack)
        {
            return 1f;
        }

        if (normalizedTime != _playbackTime)
        {
            _playback.Start(bossAttack, normalizedTime);
        }
        return _playback.Tick(normalizedTime, deltaTime);
    }

    private void SetPlaying(bool value)
    {
        _isPlaying = value && HasValidClip;
        _lastUpdateTime = EditorApplication.timeSinceStartup;
        _playbackTime = -1f;
    }

    private void StepFrame(int direction)
    {
        if (!HasValidClip || animationClip.frameRate <= 0f)
        {
            return;
        }

        SetPlaying(false);
        float normalizedFrame = 1f / (animationClip.length * animationClip.frameRate);
        SetNormalizedTime(normalizedTime + normalizedFrame * direction, true);
    }

    private void SetNormalizedTime(float value, bool sample)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return;
        }

        normalizedTime = Mathf.Clamp01(value);
        if (sample)
        {
            preview.Sample(normalizedTime);
        }
        Repaint();
    }

    private void ApplyTime(string propertyName, string label)
    {
        if (!CanApplyTimings)
        {
            return;
        }

        var serializedAttack = new SerializedObject(attack);
        SerializedProperty property = serializedAttack.FindProperty(propertyName);
        if (property == null)
        {
            _statusMessage = $"Could not find the {label} field on {attack.name}.";
            return;
        }

        Undo.RecordObject(attack, $"Set {label}");
        serializedAttack.Update();
        property.floatValue = normalizedTime;
        serializedAttack.ApplyModifiedProperties();
        EditorUtility.SetDirty(attack);
        _statusMessage = $"Set {label} to {normalizedTime:0.000} on {attack.name}.";
    }

    private static void DrawMarkerRow(string label, float value, Color color)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            Rect swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
            swatch.y += 3f;
            EditorGUI.DrawRect(swatch, color);
            GUILayout.Space(5f);
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value.ToString("0.000"), EditorStyles.miniBoldLabel, GUILayout.Width(48f));
        }
    }

    private static void DrawRangeRow(string label, float start, float end, Color color)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            Rect swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
            swatch.y += 3f;
            EditorGUI.DrawRect(swatch, color);
            GUILayout.Space(5f);
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{start:0.000}–{end:0.000}", EditorStyles.miniBoldLabel, GUILayout.Width(88f));
        }
    }
}

/// <summary>Adds direct access to the timing preview from attack assets.</summary>
[CustomEditor(typeof(AttackDataBase), true)]
public sealed class AttackDataTimingEditor : OdinEditor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open Attack Timing Preview", GUILayout.Height(28f)))
        {
            AttackTimingWindow.Open((AttackDataBase)target, Selection.activeGameObject);
        }

        EditorGUILayout.Space(4f);
        base.OnInspectorGUI();
    }
}
