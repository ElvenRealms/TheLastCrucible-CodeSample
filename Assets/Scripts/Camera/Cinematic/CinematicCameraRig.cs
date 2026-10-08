using System;
using System.Collections;
using System.IO;
using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Cinematic camera for capturing screenshots and footage of the fight. F8 takes over the view with a
/// Cinemachine camera that either the <see cref="CinematicDirector"/> operates, cutting between framed
/// shots while the fight plays, or the player flies by hand (<see cref="CinematicFreeFly"/>). Capture
/// controls slow or freeze time, hide the HUD, add letterbox bars and depth of field, and save
/// screenshots. The camera runs on real time throughout, so it keeps moving while time is frozen.
/// F8 is live in the Editor and development builds only, so a release build never leaves gameplay.
/// </summary>
[RequireComponent(typeof(CinemachineCamera))]
// Before the CinemachineBrain's LateUpdate, so the brain renders this frame's pose.
[DefaultExecutionOrder(-20)]
public class CinematicCameraRig : MonoBehaviour
{
    public enum CameraMode
    {
        Off,
        Director,
        FreeFly
    }

    private const float MaxDeltaTime = 0.1f;

    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private CinemachineBrain brain;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStateMachine player;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    [Tooltip("Optional; shots frame the player alone without it.")]
    private BossStateMachine boss;
    [FoldoutGroup("References"), SerializeField, Required, AssetsOnly]
    [Tooltip("Gameplay input, muted while the free camera has the keyboard and mouse.")]
    private InputReader inputReader;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    [Tooltip("Optional; cinematic controls and camera motion pause while its menu is open.")]
    private PauseMenuController pauseMenu;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    [Tooltip("Optional; its mouse orbit is held while the free camera uses the mouse.")]
    private LockOnCamera lockOnCamera;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private CinematicOverlay overlay;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    private CinematicDepthOfField depthOfField;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    [Tooltip("Documents the Hide HUD control hides.")]
    private UIDocument[] hudDocuments = Array.Empty<UIDocument>();

    [FoldoutGroup("Director"), SerializeField, Required, InlineEditor]
    private CinematicShotSet shotSet;

    [FoldoutGroup("Free Camera"), SerializeField, HideLabel]
    private CinematicFreeFly freeFly = new CinematicFreeFly();

    [FoldoutGroup("Capture"), SerializeField]
    [Tooltip("Time scales the slow-motion control steps through before returning to normal speed.")]
    private float[] slowMotionSteps = { 0.5f, 0.25f, 0.1f };
    [FoldoutGroup("Capture"), SerializeField, Range(1, 4)]
    [Tooltip("Screenshot resolution multiplier over the current screen size.")]
    private int screenshotSuperSize = 2;
    [FoldoutGroup("Capture"), SerializeField]
    [Tooltip("Folder for screenshots, relative to the project folder (the game folder in a build).")]
    private string screenshotFolder = "Screenshots";
    [FoldoutGroup("Capture"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Real seconds the controls panel stays up after entering the cinematic camera.")]
    private float helpDuration = 5f;
    [FoldoutGroup("Capture"), SerializeField]
    [Tooltip("Cinemachine priority while live; must beat every gameplay and presentation camera.")]
    private int livePriority = 100;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private CameraMode mode;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private string currentShot;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private float slowMotionScale = 1f;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isFrozen;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isHudHidden;

    private CinemachineCamera _camera;
    private CinematicDirector _director;
    private InputActionMap _actions;
    private InputAction _toggle;
    private InputAction _switchMode;
    private InputAction _nextShot;
    private InputAction _holdShot;
    private InputAction _slowMotion;
    private InputAction _freeze;
    private InputAction _hud;
    private InputAction _letterbox;
    private InputAction _depthOfField;
    private InputAction _screenshot;
    private InputAction _help;
    private InputAction _flyMove;
    private InputAction _flyVertical;
    private InputAction _flyLook;
    private InputAction _flyZoom;
    private InputAction _flyFast;
    private InputAction _flySlow;
    private int _slowMotionStep = -1;
    private TimeScaleClaim _slowMotionClaim;
    private TimeScaleClaim _freezeClaim;
    private bool _brainIgnoredTimeScale;
    private bool _suppressedOrbit;
    private float _helpHideTime;
    private CinematicShot _statusShot;
    private Coroutine _screenshotRoutine;

    public CameraMode Mode => mode;

    private bool IsPauseMenuOpen => pauseMenu != null && pauseMenu.IsPaused;
    private Vector3 PlayerPosition => player.transform.position;

    private Vector3 BossPosition => boss != null && boss.isActiveAndEnabled
        ? boss.transform.position
        : PlayerPosition;

    private void Awake()
    {
        _camera = GetComponent<CinemachineCamera>();
        _camera.enabled = false;
        _director = new CinematicDirector(shotSet);
        CreateActions();
    }

    private void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _toggle.Enable();
#endif
    }

    private void OnDisable()
    {
        SetMode(CameraMode.Off);
        _toggle.Disable();
    }

    private void OnDestroy()
    {
        _toggle?.Dispose();
        _actions?.Dispose();
    }

    private void Update()
    {
        if (IsPauseMenuOpen)
        {
            return;
        }

        if (_toggle.WasPerformedThisFrame())
        {
            SetMode(mode == CameraMode.Off ? CameraMode.Director : CameraMode.Off);
        }
        if (mode == CameraMode.Off)
        {
            return;
        }

        HandleCaptureInput();
        if (overlay.IsHelpVisible && _helpHideTime > 0f && Time.unscaledTime >= _helpHideTime)
        {
            _helpHideTime = 0f;
            overlay.SetHelpVisible(false);
        }
    }

    private void LateUpdate()
    {
        if (mode == CameraMode.Off || IsPauseMenuOpen)
        {
            return;
        }

        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, MaxDeltaTime);
        CinematicCameraPose pose = mode == CameraMode.Director
            ? _director.Tick(PlayerPosition, BossPosition, deltaTime)
            : freeFly.Tick(ReadFlyInput(), deltaTime);
        Apply(pose);

        if (_director.CurrentShot != _statusShot)
        {
            RefreshStatus();
        }
    }

    /// <summary>
    /// Switches the cinematic camera off, to the director, or to free flight. Entering free flight
    /// starts from the current cinematic view.
    /// </summary>
    public void SetMode(CameraMode newMode)
    {
        if (newMode == mode)
        {
            return;
        }

        CameraMode previous = mode;
        mode = newMode;
        if (previous == CameraMode.FreeFly)
        {
            RestoreGameplayInput();
        }

        if (newMode == CameraMode.Off)
        {
            Deactivate();
        }
        else if (previous == CameraMode.Off)
        {
            Activate();
        }

        if (newMode == CameraMode.Director)
        {
            _director.Begin(PlayerPosition, BossPosition);
        }
        else if (newMode == CameraMode.FreeFly)
        {
            freeFly.Begin(transform.position, transform.rotation, _camera.Lens.FieldOfView);
            SuppressGameplayInput();
        }

        if (newMode != CameraMode.Off)
        {
            RefreshStatus();
        }
        Log($"Mode {previous} -> {newMode}.");
    }

    private void Activate()
    {
        _camera.enabled = true;
        _camera.Priority.Value = livePriority;
        // Damping and blends would otherwise stall while time is frozen.
        _brainIgnoredTimeScale = brain.IgnoreTimeScale;
        brain.IgnoreTimeScale = true;
        _actions.Enable();
        overlay.Show(true);
        overlay.SetHelpVisible(true);
        _helpHideTime = Time.unscaledTime + helpDuration;
    }

    private void Deactivate()
    {
        if (_screenshotRoutine != null)
        {
            StopCoroutine(_screenshotRoutine);
            _screenshotRoutine = null;
        }
        _actions.Disable();
        ReleaseTimeClaims();
        SetHudHidden(false);
        _director.SetHolding(false);
        _camera.enabled = false;
        // Scene teardown can destroy these before this rig.
        if (depthOfField != null)
        {
            depthOfField.SetOn(false);
        }
        if (overlay != null)
        {
            overlay.SetCapturing(false);
            overlay.Show(false);
        }
        if (brain != null)
        {
            brain.IgnoreTimeScale = _brainIgnoredTimeScale;
        }
    }

    private void HandleCaptureInput()
    {
        bool changed = false;
        if (_switchMode.WasPerformedThisFrame())
        {
            SetMode(mode == CameraMode.Director ? CameraMode.FreeFly : CameraMode.Director);
        }
        if (_nextShot.WasPerformedThisFrame())
        {
            if (mode == CameraMode.FreeFly)
            {
                SetMode(CameraMode.Director);
            }
            else
            {
                _director.Cut(PlayerPosition, BossPosition);
            }
            changed = true;
        }
        if (_holdShot.WasPerformedThisFrame())
        {
            _director.SetHolding(!_director.IsHolding);
            changed = true;
        }
        if (_slowMotion.WasPerformedThisFrame())
        {
            CycleSlowMotion();
            changed = true;
        }
        if (_freeze.WasPerformedThisFrame())
        {
            SetFrozen(!isFrozen);
            changed = true;
        }
        if (_hud.WasPerformedThisFrame())
        {
            SetHudHidden(!isHudHidden);
            changed = true;
        }
        if (_letterbox.WasPerformedThisFrame())
        {
            overlay.SetLetterbox(!overlay.IsLetterboxed);
            changed = true;
        }
        if (_depthOfField.WasPerformedThisFrame() && depthOfField != null)
        {
            depthOfField.SetOn(!depthOfField.IsOn);
            changed = true;
        }
        if (_help.WasPerformedThisFrame())
        {
            _helpHideTime = 0f;
            overlay.SetHelpVisible(!overlay.IsHelpVisible);
        }
        if (_screenshot.WasPerformedThisFrame() && _screenshotRoutine == null)
        {
            _screenshotRoutine = StartCoroutine(CaptureScreenshot());
        }

        if (changed)
        {
            RefreshStatus();
        }
    }

    private void Apply(in CinematicCameraPose pose)
    {
        transform.SetPositionAndRotation(pose.Position, pose.Rotation);
        LensSettings lens = _camera.Lens;
        lens.FieldOfView = pose.FieldOfView;
        _camera.Lens = lens;
        if (depthOfField != null)
        {
            depthOfField.SetFocusDistance(pose.FocusDistance);
        }
    }

    private CinematicFlyInput ReadFlyInput()
    {
        return new CinematicFlyInput(
            _flyMove.ReadValue<Vector2>(),
            _flyVertical.ReadValue<float>(),
            _flyLook.ReadValue<Vector2>(),
            _flyZoom.ReadValue<float>(),
            _flyFast.IsPressed(),
            _flySlow.IsPressed());
    }

    private void SuppressGameplayInput()
    {
        inputReader.SetCinematicSuppressed(true);
        if (lockOnCamera != null && !lockOnCamera.IsInputSuppressed)
        {
            lockOnCamera.SuppressInput();
            _suppressedOrbit = true;
        }
    }

    private void RestoreGameplayInput()
    {
        inputReader.SetCinematicSuppressed(false);
        if (_suppressedOrbit)
        {
            _suppressedOrbit = false;
            lockOnCamera.RestoreInput();
        }
    }

    private void CycleSlowMotion()
    {
        _slowMotionStep = _slowMotionStep + 1 >= slowMotionSteps.Length ? -1 : _slowMotionStep + 1;
        slowMotionScale = _slowMotionStep < 0 ? 1f : slowMotionSteps[_slowMotionStep];
        if (_slowMotionStep < 0)
        {
            _slowMotionClaim?.Release();
            _slowMotionClaim = null;
        }
        else if (_slowMotionClaim == null)
        {
            _slowMotionClaim = TimeScaleArbiter.Claim(slowMotionScale, this);
        }
        else
        {
            _slowMotionClaim.SetScale(slowMotionScale);
        }
    }

    private void SetFrozen(bool frozen)
    {
        isFrozen = frozen;
        if (frozen)
        {
            _freezeClaim ??= TimeScaleArbiter.Claim(0f, this);
        }
        else
        {
            _freezeClaim?.Release();
            _freezeClaim = null;
        }
    }

    private void ReleaseTimeClaims()
    {
        SetFrozen(false);
        _slowMotionStep = -1;
        slowMotionScale = 1f;
        _slowMotionClaim?.Release();
        _slowMotionClaim = null;
    }

    // HUD documents belong to other presenters, so they are hidden inline rather than through their stylesheets.
    private void SetHudHidden(bool hidden)
    {
        isHudHidden = hidden;
        foreach (var document in hudDocuments)
        {
            if (document != null && document.rootVisualElement != null)
            {
                document.rootVisualElement.style.display = hidden ? DisplayStyle.None : StyleKeyword.Null;
            }
        }
    }

    private IEnumerator CaptureScreenshot()
    {
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", screenshotFolder);
        Directory.CreateDirectory(folder);
        string file = $"BossFight_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";

        // One frame for the panel to drop the help and toast, then capture at the end of the next.
        overlay.SetCapturing(true);
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, file), screenshotSuperSize);
        yield return new WaitForEndOfFrame();
        yield return null;
        overlay.SetCapturing(false);

        overlay.ShowToast($"Saved {file}");
        Log($"Screenshot saved to {Path.Combine(folder, file)}.");
        _screenshotRoutine = null;
    }

    private void RefreshStatus()
    {
        _statusShot = _director.CurrentShot;
        currentShot = _statusShot != null ? _statusShot.Name : string.Empty;

        string view = mode == CameraMode.FreeFly
            ? "Free camera"
            : $"Director: {currentShot}{(_director.IsHolding ? " (held)" : string.Empty)}";
        string time = isFrozen ? "Frozen" : $"{slowMotionScale:0.##}x speed";
        string extras = string.Concat(
            isHudHidden ? " · HUD hidden" : string.Empty,
            overlay.IsLetterboxed ? " · Letterbox" : string.Empty,
            depthOfField != null && depthOfField.IsOn ? " · Depth of field" : string.Empty);
        overlay.SetStatus($"{view} · {time}{extras}");
    }

    private void CreateActions()
    {
        _toggle = new InputAction("Cinematic Toggle", InputActionType.Button, "<Keyboard>/f8");

        _actions = new InputActionMap("Cinematic");
        _switchMode = _actions.AddAction("Switch Mode", InputActionType.Button, "<Keyboard>/f9");
        _nextShot = _actions.AddAction("Next Shot", InputActionType.Button, "<Keyboard>/n");
        _holdShot = _actions.AddAction("Hold Shot", InputActionType.Button, "<Keyboard>/l");
        _slowMotion = _actions.AddAction("Slow Motion", InputActionType.Button, "<Keyboard>/t");
        _freeze = _actions.AddAction("Freeze", InputActionType.Button, "<Keyboard>/p");
        _hud = _actions.AddAction("Hide HUD", InputActionType.Button, "<Keyboard>/h");
        _letterbox = _actions.AddAction("Letterbox", InputActionType.Button, "<Keyboard>/b");
        _depthOfField = _actions.AddAction("Depth Of Field", InputActionType.Button, "<Keyboard>/f");
        _screenshot = _actions.AddAction("Screenshot", InputActionType.Button, "<Keyboard>/f12");
        _help = _actions.AddAction("Help", InputActionType.Button, "<Keyboard>/f3");

        _flyMove = _actions.AddAction("Fly Move", InputActionType.Value);
        _flyMove.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        _flyVertical = _actions.AddAction("Fly Vertical", InputActionType.Value);
        _flyVertical.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/q")
            .With("Positive", "<Keyboard>/e");
        _flyLook = _actions.AddAction("Fly Look", InputActionType.Value, "<Mouse>/delta");
        _flyZoom = _actions.AddAction("Fly Zoom", InputActionType.Value, "<Mouse>/scroll/y");
        _flyFast = _actions.AddAction("Fly Fast", InputActionType.Button, "<Keyboard>/leftShift");
        _flySlow = _actions.AddAction("Fly Slow", InputActionType.Button, "<Keyboard>/leftCtrl");
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[CinematicCameraRig] {message}", this);
        }
    }
}
