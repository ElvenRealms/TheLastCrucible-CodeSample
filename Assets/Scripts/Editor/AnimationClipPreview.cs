using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Renders an animation clip on an isolated copy of a model prefab, sampled at a normalized time,
/// with an orbiting camera. Editor windows embed it and keep it as a serialized field so the camera
/// survives domain reloads.
/// </summary>
[Serializable]
public sealed class AnimationClipPreview
{
    private const float PreviewHeight = 340f;
    private const float MinimumCameraDistance = 0.25f;

    [SerializeField]
    private float cameraYaw = 135f;
    [SerializeField]
    private float cameraPitch = 8f;
    [SerializeField]
    private float cameraDistance = 4f;
    [SerializeField]
    private Vector3 cameraPivot;

    [NonSerialized]
    private EditorWindow _owner;
    [NonSerialized]
    private PreviewRenderUtility _previewUtility;
    [NonSerialized]
    private GameObject _previewRoot;
    [NonSerialized]
    private GameObject _previewInstance;
    [NonSerialized]
    private Animator _animator;
    [NonSerialized]
    private AnimationClip _clip;
    [NonSerialized]
    private GameObject _model;
    private PlayableGraph _playableGraph;
    private AnimationClipPlayable _clipPlayable;
    private Bounds _previewBounds;

    private static bool IsEditModeUnavailable => EditorApplication.isPlayingOrWillChangePlaymode;

    /// <summary>Runs on every Repaint just before the preview renders, once the model is posed.</summary>
    [field: NonSerialized]
    public event Action BeforeRender;

    /// <summary>The isolated copy of the model, or null while there is no preview.</summary>
    public GameObject PreviewInstance => _previewInstance;

    /// <summary>The root of the preview scene's objects; children of it render in the preview.</summary>
    public Transform PreviewRoot => _previewRoot != null ? _previewRoot.transform : null;

    /// <summary>True when a clip can be sampled: assigned, non-legacy and with a duration.</summary>
    public static bool IsSampleable(AnimationClip clip) => clip != null && !clip.legacy && clip.length > 0f;

    /// <summary>Sets the window that repaints when the camera moves. Call it from the window's OnEnable.</summary>
    public void Initialize(EditorWindow owner)
    {
        _owner = owner;
    }

    /// <summary>Explains why a clip and model can't be previewed, if they can't.</summary>
    public static void DrawGuidance(AnimationClip clip, GameObject model)
    {
        if (clip == null)
        {
            return;
        }

        if (clip.legacy)
        {
            EditorGUILayout.HelpBox("Legacy animation clips cannot be sampled by this preview.", MessageType.Error);
            return;
        }

        if (clip.length <= 0f)
        {
            EditorGUILayout.HelpBox("The selected animation clip has no sampleable duration.", MessageType.Error);
            return;
        }

        if (model == null)
        {
            EditorGUILayout.HelpBox("Choose a model prefab containing an Animator.", MessageType.Warning);
            return;
        }

        Animator[] animators = model.GetComponentsInChildren<Animator>(true);
        if (animators.Length == 0)
        {
            EditorGUILayout.HelpBox("The preview model does not contain an Animator.", MessageType.Warning);
            return;
        }

        if (!HasCompatibleAnimator(clip, animators))
        {
            EditorGUILayout.HelpBox(
                "This is a Humanoid clip, but the preview model has no valid Humanoid Avatar.",
                MessageType.Warning);
        }
    }

    /// <summary>
    /// Draws the normalized time, seconds and frame of a clip, and returns the normalized time,
    /// changed if the user typed a new one.
    /// </summary>
    public static float DrawTimeReadout(AnimationClip clip, float normalizedTime, EditorWindow owner)
    {
        bool hasClip = IsSampleable(clip);
        float seconds = hasClip ? normalizedTime * clip.length : 0f;
        float frameRate = hasClip ? clip.frameRate : 0f;
        int frame = hasClip ? Mathf.RoundToInt(seconds * frameRate) : 0;
        int lastFrame = hasClip ? Mathf.Max(0, Mathf.RoundToInt(clip.length * frameRate)) : 0;

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("Normalized", EditorStyles.miniBoldLabel, GUILayout.Width(68f));
            using (new EditorGUI.DisabledScope(!hasClip || IsEditModeUnavailable))
            {
                normalizedTime = EditorGUILayout.FloatField(normalizedTime, GUILayout.Width(66f));
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{seconds:0.000} s", EditorStyles.miniLabel);
            GUILayout.Space(12f);
            GUILayout.Label($"Frame  {frame} / {lastFrame}", EditorStyles.miniLabel);
            GUILayout.Space(8f);
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                EditorGUIUtility.systemCopyBuffer = normalizedTime.ToString("0.000", CultureInfo.InvariantCulture);
                owner.ShowNotification(new GUIContent($"Copied {normalizedTime:0.000}"), 1.5);
            }
        }
        return normalizedTime;
    }

    /// <summary>Draws the preview area, rebuilding the preview when the clip or model changed.</summary>
    public void Draw(AnimationClip clip, GameObject model, float normalizedTime)
    {
        EditorGUILayout.Space(4f);
        Rect previewRect = GUILayoutUtility.GetRect(100f, PreviewHeight, GUILayout.ExpandWidth(true));
        GUI.Box(previewRect, GUIContent.none, GetPreviewBackgroundStyle());

        if (!IsSampleable(clip) || model == null)
        {
            DrawCenteredMessage(previewRect, "Animation preview");
            return;
        }

        if (IsEditModeUnavailable)
        {
            DrawCenteredMessage(previewRect, "Preview is paused outside Edit Mode.");
            return;
        }

        Animator[] animators = model.GetComponentsInChildren<Animator>(true);
        if (!HasCompatibleAnimator(clip, animators))
        {
            string message = animators.Length == 0
                ? "The preview model needs an Animator component."
                : "The preview model needs a valid Humanoid Avatar for this clip.";
            DrawCenteredMessage(previewRect, message);
            return;
        }

        EnsureResources(clip, model, normalizedTime);
        if (_animator == null)
        {
            DrawCenteredMessage(previewRect, "The preview model needs an Animator component.");
            return;
        }

        HandleCameraInput(previewRect);
        if (Event.current.type == EventType.Repaint)
        {
            BeforeRender?.Invoke();
            Render(previewRect);
        }

        var helpRect = new Rect(previewRect.x + 8f, previewRect.yMax - 25f, previewRect.width - 16f, 18f);
        GUI.Label(helpRect, "Drag to orbit  •  Scroll to zoom", EditorStyles.centeredGreyMiniLabel);
    }

    /// <summary>Poses the preview model at a normalized time of the clip.</summary>
    public void Sample(float normalizedTime)
    {
        SamplePose(normalizedTime);
        RecalculateBounds();
    }

    /// <summary>Poses the preview model without updating the camera framing bounds, for many samples in a row.</summary>
    public void SamplePose(float normalizedTime)
    {
        if (!_playableGraph.IsValid() || !IsSampleable(_clip))
        {
            return;
        }

        double seconds = normalizedTime * _clip.length;
        _clipPlayable.SetTime(seconds);
        _clipPlayable.SetTime(seconds);
        _playableGraph.Evaluate(0f);
    }

    /// <summary>Points the camera at the whole model.</summary>
    public void FrameModel()
    {
        if (_previewInstance == null)
        {
            return;
        }

        RecalculateBounds();
        cameraPivot = _previewBounds.center;
        float radius = Mathf.Max(_previewBounds.extents.magnitude, 0.2f);
        cameraDistance = Mathf.Max(MinimumCameraDistance, radius * 2.8f);
        RepaintOwner();
    }

    /// <summary>Destroys the preview scene; the next Draw rebuilds it.</summary>
    public void Cleanup()
    {
        if (_playableGraph.IsValid())
        {
            _playableGraph.Destroy();
        }

        _animator = null;
        _previewInstance = null;
        _clip = null;
        _model = null;

        if (_previewRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(_previewRoot);
            _previewRoot = null;
        }

        _previewUtility?.Cleanup();
        _previewUtility = null;
    }

    private void EnsureResources(AnimationClip clip, GameObject model, float normalizedTime)
    {
        if (_previewUtility != null && _previewInstance != null && _playableGraph.IsValid() &&
            clip == _clip && model == _model)
        {
            return;
        }

        Cleanup();
        _clip = clip;
        _model = model;
        _previewUtility = new PreviewRenderUtility();
        _previewUtility.camera.fieldOfView = 30f;
        _previewUtility.camera.allowHDR = false;
        _previewUtility.camera.allowMSAA = true;
        _previewUtility.ambientColor = new Color(0.16f, 0.17f, 0.2f, 1f);
        ConfigureLights();

        _previewRoot = EditorUtility.CreateGameObjectWithHideFlags(
            "Animation Clip Preview",
            HideFlags.HideInHierarchy | HideFlags.DontSave);
        _previewRoot.SetActive(false);
        _previewUtility.AddSingleGO(_previewRoot);

        _previewInstance = UnityEngine.Object.Instantiate(model, _previewRoot.transform);
        _previewInstance.name = model.name;
        _previewInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        SetHideFlagsRecursively(_previewInstance.transform);
        DisableBehaviours();
        _previewRoot.SetActive(true);

        _animator = FindAnimator();
        if (_animator == null)
        {
            return;
        }

        Animator[] animators = _previewInstance.GetComponentsInChildren<Animator>(true);
        foreach (Animator candidate in animators)
        {
            candidate.enabled = candidate == _animator;
        }

        _animator.runtimeAnimatorController = null;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.fireEvents = false;
        _animator.applyRootMotion = false;
        _animator.enabled = true;

        _playableGraph = PlayableGraph.Create("Animation Clip Preview");
        _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _clipPlayable = AnimationClipPlayable.Create(_playableGraph, clip);
        _clipPlayable.SetApplyFootIK(false);
        _clipPlayable.SetApplyPlayableIK(false);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(
            _playableGraph,
            "Animation Clip Preview Output",
            _animator);
        output.SetSourcePlayable(_clipPlayable);
        _playableGraph.Play();

        Sample(normalizedTime);
        FrameModel();
    }

    private void Render(Rect previewRect)
    {
        Camera camera = _previewUtility.camera;
        Quaternion orbit = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        camera.transform.position = cameraPivot + orbit * Vector3.back * cameraDistance;
        camera.transform.LookAt(cameraPivot);
        camera.nearClipPlane = Mathf.Max(0.01f, cameraDistance * 0.01f);
        camera.farClipPlane = Mathf.Max(100f, cameraDistance * 10f);

        _previewUtility.BeginPreview(previewRect, GetPreviewBackgroundStyle());
        _previewUtility.Render(true, true);
        _previewUtility.EndAndDrawPreview(previewRect);
    }

    private void HandleCameraInput(Rect previewRect)
    {
        Event currentEvent = Event.current;
        if (!previewRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
        {
            cameraYaw += currentEvent.delta.x * 0.6f;
            cameraPitch = Mathf.Clamp(cameraPitch - currentEvent.delta.y * 0.6f, -75f, 75f);
            currentEvent.Use();
            RepaintOwner();
        }
        else if (currentEvent.type == EventType.ScrollWheel)
        {
            cameraDistance = Mathf.Clamp(
                cameraDistance * (1f + currentEvent.delta.y * 0.08f),
                MinimumCameraDistance,
                100f);
            currentEvent.Use();
            RepaintOwner();
        }
    }

    private void RecalculateBounds()
    {
        if (_previewInstance == null)
        {
            return;
        }

        // Effects riding the model would stretch the framing, so only the model's own meshes count.
        bool hasBounds = false;
        foreach (Renderer renderer in _previewInstance.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer)
            {
                continue;
            }

            if (hasBounds)
            {
                _previewBounds.Encapsulate(renderer.bounds);
            }
            else
            {
                _previewBounds = renderer.bounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            _previewBounds = new Bounds(_previewInstance.transform.position, Vector3.one);
        }
    }

    private void DisableBehaviours()
    {
        Behaviour[] behaviours = _previewInstance.GetComponentsInChildren<Behaviour>(true);
        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour is Animator)
            {
                continue;
            }

            behaviour.enabled = false;
            behaviour.hideFlags |= HideFlags.NotEditable;
        }
    }

    private Animator FindAnimator()
    {
        Animator[] animators = _previewInstance.GetComponentsInChildren<Animator>(true);
        if (animators.Length == 0)
        {
            return null;
        }

        if (_clip.isHumanMotion)
        {
            foreach (Animator candidate in animators)
            {
                if (IsValidHumanoid(candidate))
                {
                    return candidate;
                }
            }
        }

        return animators[0];
    }

    private void ConfigureLights()
    {
        Light[] lights = _previewUtility.lights;
        lights[0].intensity = 1.25f;
        lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
        lights[1].intensity = 0.75f;
        lights[1].transform.rotation = Quaternion.Euler(340f, 215f, 0f);
    }

    private void RepaintOwner()
    {
        if (_owner != null)
        {
            _owner.Repaint();
        }
    }

    private static bool HasCompatibleAnimator(AnimationClip clip, Animator[] animators)
    {
        return animators.Length > 0 && (!clip.isHumanMotion || Array.Exists(animators, IsValidHumanoid));
    }

    private static bool IsValidHumanoid(Animator animator)
    {
        return animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
    }

    private static void DrawCenteredMessage(Rect previewRect, string message)
    {
        GUI.Label(previewRect, message, EditorStyles.centeredGreyMiniLabel);
    }

    private static void SetHideFlagsRecursively(Transform root)
    {
        root.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        foreach (Transform child in root)
        {
            SetHideFlagsRecursively(child);
        }
    }

    private static GUIStyle GetPreviewBackgroundStyle()
    {
        return GUI.skin.FindStyle("PreBackground") ?? EditorStyles.helpBox;
    }
}
