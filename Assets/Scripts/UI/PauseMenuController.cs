using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the scene's pause menu visibility and the gameplay state suspended by it.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [FoldoutGroup("Gameplay"), SerializeField, Required, AssetsOnly]
    private InputReader inputReader;
    [FoldoutGroup("Gameplay"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStateMachine player;
    [FoldoutGroup("Gameplay"), SerializeField, SceneObjectsOnly]
    private BossStateMachine boss;
    [FoldoutGroup("Gameplay"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [FoldoutGroup("Gameplay"), SerializeField, SceneObjectsOnly]
    private HitStopController hitStopController;
    [FoldoutGroup("Gameplay"), SerializeField, SceneObjectsOnly, Tooltip("Resolves via FindAnyObjectByType when pause opens and this is empty.")]
    private GamepadRumbleEffect gamepadRumble;
    [FoldoutGroup("Gameplay"), SerializeField, SceneObjectsOnly, Tooltip("Resolves via FindAnyObjectByType when pause opens and this is empty.")]
    private SlowMotionEffect slowMotion;
    [FoldoutGroup("Gameplay"), SerializeField, SceneObjectsOnly]
    private LockOnCamera lockOnCamera;
    [FoldoutGroup("Gameplay"), SerializeField, SceneObjectsOnly]
    private Behaviour cameraInputAxis;

    [FoldoutGroup("Quit"), SerializeField, Required]
    [Tooltip("Scene Quit returns to on the web. It must be enabled in the build scene list.")]
    private string titleScene = "Title";

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsPaused { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool _isResuming;

    public bool CanPause => !IsPaused && !_isResuming && IsPauseAvailable();
    public bool IsAudioMenuOpen { get; private set; }
    public bool IsMenuVisible => IsPaused && !_isResuming;

    private readonly List<HitFlashReceiver> _pausedFlashReceivers = new();
    private InputAction _pauseAction;
    private InputAction _cancelAction;
    private Coroutine _resumeCoroutine;
    private bool _cameraInputWasEnabled;
    private TimeScaleClaim _timeScaleClaim;

    private void Start()
    {
        LockCursorForGameplay();
    }

    private void OnEnable()
    {
        CreatePauseActions();
        _pauseAction.Enable();
        _cancelAction.Enable();
    }

    private void OnDisable()
    {
        _pauseAction?.Disable();
        _cancelAction?.Disable();
        ReleasePauseState();
        IsAudioMenuOpen = false;
    }

    private void OnDestroy()
    {
        _pauseAction?.Dispose();
        _cancelAction?.Dispose();
    }

    /// <summary>
    /// Opens pause when it is available, otherwise resumes an existing pause.
    /// </summary>
    public void RequestPauseToggle()
    {
        if (_isResuming)
        {
            return;
        }
        if (IsPaused)
        {
            Resume();
            return;
        }

        OpenPauseMenu();
    }

    /// <summary>
    /// Opens the menu and immediately suspends gameplay state owned by pause.
    /// </summary>
    public void OpenPauseMenu()
    {
        if (!CanPause)
        {
            return;
        }

        IsPaused = true;
        _cameraInputWasEnabled = cameraInputAxis != null && cameraInputAxis.enabled;

        ResolveFeedbackEffects();
        hitStopController?.SuspendForPause();
        gamepadRumble?.SuspendForPause();
        slowMotion?.SuspendForPause();
        inputReader?.SetPauseSuppressed(true);
        player?.SetPauseSuppressed(true);
        ResolveBoss()?.SetPauseSuppressed(true);
        lockOnCamera?.SetPauseSuppressed(true);
        SuspendFlashTimers();
        if (cameraInputAxis != null)
        {
            cameraInputAxis.enabled = false;
        }

        _timeScaleClaim = TimeScaleArbiter.Claim(0f, this);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsAudioMenuOpen = false;
        Log("Opened.");
    }

    /// <summary>
    /// Begins safe pause release after the menu input has completed.
    /// </summary>
    public void Resume()
    {
        if (!IsPaused || _isResuming)
        {
            return;
        }

        _isResuming = true;
        IsAudioMenuOpen = false;
        _resumeCoroutine = StartCoroutine(ResumeAfterMenuInput());
    }

    /// <summary>
    /// Leaves the game: stops Play Mode in the Editor, returns to the title screen on the web,
    /// where a page cannot close itself, and exits the application everywhere else.
    /// </summary>
    public void Quit()
    {
        Log("Quit.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        ReturnToTitle();
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Loads the title scene. Unloading this scene releases the pause-owned gameplay state.
    /// </summary>
    public void ReturnToTitle()
    {
        if (!Application.CanStreamedLevelBeLoaded(titleScene))
        {
            Debug.LogError($"[PauseMenuController] Scene '{titleScene}' is not in the build scene list.", this);
            return;
        }

        SceneManager.LoadScene(titleScene);
    }

    /// <summary>
    /// Opens the audio panel while retaining all pause-owned gameplay state.
    /// </summary>
    public void OpenAudioMenu()
    {
        if (!IsPaused || _isResuming)
        {
            return;
        }

        IsAudioMenuOpen = true;
    }

    /// <summary>
    /// Returns from the audio page without releasing pause-owned gameplay state.
    /// </summary>
    public void CloseAudioMenu()
    {
        IsAudioMenuOpen = false;
    }

    private void CreatePauseActions()
    {
        if (_pauseAction == null)
        {
            _pauseAction = new InputAction("Pause", InputActionType.Button);
            _pauseAction.AddBinding("<Keyboard>/escape");
            _pauseAction.AddBinding("<Gamepad>/start");
            _pauseAction.performed += HandlePausePerformed;
        }

        if (_cancelAction == null)
        {
            _cancelAction = new InputAction("Pause Cancel", InputActionType.Button);
            _cancelAction.AddBinding("<Gamepad>/buttonEast");
            _cancelAction.performed += HandleCancelPerformed;
        }
    }

    private void HandlePausePerformed(InputAction.CallbackContext context)
    {
        if (IsPaused && IsAudioMenuOpen && context.control.device is Keyboard)
        {
            CloseAudioMenu();
            return;
        }

        RequestPauseToggle();
    }

    private void HandleCancelPerformed(InputAction.CallbackContext context)
    {
        if (IsAudioMenuOpen)
        {
            CloseAudioMenu();
            return;
        }

        if (IsPaused)
        {
            Resume();
        }
    }

    private IEnumerator ResumeAfterMenuInput()
    {
        yield return null;
        while (AreMenuControlsHeld())
        {
            yield return null;
        }

        _resumeCoroutine = null;
        ReleasePauseState();
        Log("Resumed.");
    }

    private void ReleasePauseState()
    {
        if (_resumeCoroutine != null)
        {
            StopCoroutine(_resumeCoroutine);
            _resumeCoroutine = null;
        }

        if (!IsPaused && !_isResuming)
        {
            return;
        }

        _timeScaleClaim?.Release();
        _timeScaleClaim = null;
        hitStopController?.ResumeFromPause();
        gamepadRumble?.ResumeFromPause();
        slowMotion?.ResumeFromPause();
        IsAudioMenuOpen = false;
        inputReader?.SetPauseSuppressed(false);
        player?.SetPauseSuppressed(false);
        ResolveBoss()?.SetPauseSuppressed(false);
        lockOnCamera?.SetPauseSuppressed(false);
        ResumeFlashTimers();
        if (cameraInputAxis != null)
        {
            cameraInputAxis.enabled = _cameraInputWasEnabled;
        }
        LockCursorForGameplay();
        IsPaused = false;
        _isResuming = false;
    }

    private static void LockCursorForGameplay()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private bool IsPauseAvailable()
    {
        if (player != null && player.Health != null && player.Health.IsDead)
        {
            return false;
        }
        // Victory presentation runs from the win until the encounter resets.
        return arenaManager == null || arenaManager.Phase != EncounterPhase.Won;
    }

    private BossStateMachine ResolveBoss()
    {
        if (boss == null)
        {
            boss = FindAnyObjectByType<BossStateMachine>();
        }
        return boss;
    }

    private void ResolveFeedbackEffects()
    {
        if (gamepadRumble == null)
        {
            gamepadRumble = FindAnyObjectByType<GamepadRumbleEffect>();
        }
        if (slowMotion == null)
        {
            slowMotion = FindAnyObjectByType<SlowMotionEffect>();
        }
    }

    private void SuspendFlashTimers()
    {
        _pausedFlashReceivers.Clear();
        HitFlashReceiver[] receivers = FindObjectsByType<HitFlashReceiver>();
        foreach (HitFlashReceiver receiver in receivers)
        {
            receiver.SetPauseSuppressed(true);
            _pausedFlashReceivers.Add(receiver);
        }
    }

    private void ResumeFlashTimers()
    {
        foreach (HitFlashReceiver receiver in _pausedFlashReceivers)
        {
            if (receiver != null)
            {
                receiver.SetPauseSuppressed(false);
            }
        }
        _pausedFlashReceivers.Clear();
    }

    private bool AreMenuControlsHeld()
    {
        if (Keyboard.current != null
            && (Keyboard.current.escapeKey.isPressed
                || Keyboard.current.enterKey.isPressed
                || Keyboard.current.numpadEnterKey.isPressed))
        {
            return true;
        }

        if (Gamepad.current == null)
        {
            return false;
        }
        return Gamepad.current.startButton.isPressed
               || Gamepad.current.buttonEast.isPressed
               || Gamepad.current.buttonSouth.isPressed;
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[PauseMenuController] {message}", this);
        }
    }
}
