using System;
using System.Collections;
using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Presentation that follows an Encounter Victory. Owns the Victory Beat control
/// lock, time slowing and camera framing, and the Victory Banner; never owns
/// encounter or boss state. Starts on <see cref="ArenaManager.EncounterWon"/> and
/// ends on <see cref="ArenaManager.EncounterReset"/>, which the arena times itself.
/// </summary>
public class VictorySequence : MonoBehaviour
{
    public enum SequencePhase
    {
        Idle,
        WaitingForHitStop,
        Beat,
        ReturningCamera,
        WaitingForDeathAnimation,
        Banner,
        AwaitingReset
    }

    [Tooltip("Publishes the Encounter Victory this sequence follows and the reset that ends it.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [Tooltip("Locked for input during the Victory Beat and restored once the camera returns.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStateMachine player;
    [Tooltip("Optional; the beat starts immediately when absent.")]
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    private HitStopController hitStopController;
    [Tooltip("Reports whether the victory camera cut has finished blending back.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private CinemachineBrain brain;
    [Tooltip("Composed camera cut to during the Victory Beat; follows the boss, so its follow offset is boss-relative.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private CinemachineCamera victoryCamera;
    [Tooltip("Target group framed by the victory camera; filled with the player and boss for the beat.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private CinemachineTargetGroup targetGroup;
    [Tooltip("Victory Banner shown once the boss finishes its death animation.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private VictoryBanner banner;
    [Tooltip("Source of the stats shown on the Victory Banner.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private FightStatsTracker statsTracker;

    [Tooltip("Time scale applied during the Victory Beat.")]
    [FoldoutGroup("Timing"), SerializeField, Range(0.05f, 1f)]
    private float slowMotionScale = 0.45f;
    [Tooltip("Real-seconds length of the Victory Beat, once the hitstop has released.")]
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float beatDuration = 1.75f;
    [Tooltip("Real-seconds the Victory Banner stays fully visible before it fades out. Keep the hold and fade-out within the ArenaManager retry delay.")]
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float bannerHoldDuration = 2.5f;
    [Tooltip("Cinemachine priority applied to the victory camera to force the cut.")]
    [FoldoutGroup("Timing"), SerializeField]
    private int victoryCameraPriority = 20;

    [Tooltip("Radius kept in frame around the boss's lock-on point.")]
    [FoldoutGroup("Framing"), SerializeField, MinValue(0.1f)]
    private float bossFramingRadius = 2.5f;
    [Tooltip("Planar distance to the boss within which the player joins the shot; beyond it the shot frames the boss alone.")]
    [FoldoutGroup("Framing"), SerializeField, MinValue(0f)]
    private float playerFramingRange = 6f;
    [Tooltip("Radius kept in frame around the player when they join the shot.")]
    [FoldoutGroup("Framing"), SerializeField, MinValue(0.1f)]
    private float playerFramingRadius = 1f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public SequencePhase CurrentPhase { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsRunning { get; private set; }

    private string CameraPreviewLabel => _isPreviewingCamera ? "Stop Victory Camera Preview" : "Preview Victory Camera";

    /// <summary>Raised when the encounter reset ends a running sequence.</summary>
    public event Action SequenceCompleted;

    private BossStateMachine _boss;
    private Coroutine _sequenceCoroutine;
    private bool _deathAnimationCompleted;
    private TimeScaleClaim _slowMotionClaim;
    private bool _inputSuppressed;
    private bool _isPreviewingCamera;

    private void OnEnable()
    {
        arenaManager.EncounterWon += HandleEncounterWon;
        arenaManager.EncounterReset += HandleEncounterReset;
    }

    private void OnDisable()
    {
        arenaManager.EncounterWon -= HandleEncounterWon;
        arenaManager.EncounterReset -= HandleEncounterReset;
        EndCameraPreview();
        FinishSequence();
    }

    private void HandleEncounterWon(BossStateMachine wonBoss)
    {
        if (IsRunning || wonBoss == null) return;

        EndCameraPreview();
        _boss = wonBoss;
        IsRunning = true;
        _sequenceCoroutine = StartCoroutine(RunSequence());
    }

    private void HandleEncounterReset()
    {
        if (!IsRunning) return;

        FinishSequence();
        SequenceCompleted?.Invoke();
    }

    private void HandleDeathAnimationCompleted()
    {
        _deathAnimationCompleted = true;
    }

    // The encounter reset ends the sequence from any phase, so no phase needs its own exit.
    private IEnumerator RunSequence()
    {
        _boss.DeathAnimationCompleted += HandleDeathAnimationCompleted;
        player.SuppressInput();
        _inputSuppressed = true;

        FrameVictoryCamera(_boss);

        CurrentPhase = SequencePhase.WaitingForHitStop;
        while (hitStopController != null && hitStopController.IsStopping)
            yield return null;

        CurrentPhase = SequencePhase.Beat;
        _slowMotionClaim = TimeScaleArbiter.Claim(slowMotionScale, this);
        Log($"Slow motion claimed at {slowMotionScale:0.##}.");
        yield return new WaitForSecondsRealtime(beatDuration);

        ReleaseSlowMotion();
        CurrentPhase = SequencePhase.ReturningCamera;
        ReleaseVictoryCamera();
        yield return null;
        while (brain.IsBlending)
            yield return null;

        RestoreInput();

        CurrentPhase = SequencePhase.WaitingForDeathAnimation;
        while (!_deathAnimationCompleted)
            yield return null;

        CurrentPhase = SequencePhase.Banner;
        banner.SetStats(statsTracker.Stats);
        banner.Show();
        yield return new WaitForSecondsRealtime(bannerHoldDuration);
        banner.Hide();
        while (banner.IsVisible)
            yield return null;

        CurrentPhase = SequencePhase.AwaitingReset;
        _sequenceCoroutine = null;
    }

    private void FrameVictoryCamera(BossStateMachine framedBoss)
    {
        Transform bossTransform = framedBoss.transform;
        Transform bossFocus = framedBoss.LockOnTarget != null && framedBoss.LockOnTarget.TargetPoint != null
            ? framedBoss.LockOnTarget.TargetPoint
            : bossTransform;

        targetGroup.Targets.Clear();
        targetGroup.AddMember(bossFocus, 1f, bossFramingRadius);
        if (IsPlayerInFramingRange(bossTransform))
            targetGroup.AddMember(player.transform, 1f, playerFramingRadius);

        victoryCamera.Follow = bossTransform;
        // The custom blend asset makes this switch a cut, not a blend-in.
        victoryCamera.enabled = true;
        victoryCamera.Priority.Value = victoryCameraPriority;
    }

    private bool IsPlayerInFramingRange(Transform bossTransform)
    {
        Vector3 offset = player.transform.position - bossTransform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= playerFramingRange * playerFramingRange;
    }

    private void ReleaseVictoryCamera()
    {
        if (victoryCamera == null) return;

        victoryCamera.Priority = new PrioritySettings();
        victoryCamera.enabled = false;
    }

    // Kept apart from ReleaseVictoryCamera so the framing holds while the camera blends back.
    private void ClearVictoryFraming()
    {
        if (targetGroup != null) targetGroup.Targets.Clear();
        if (victoryCamera != null) victoryCamera.Follow = null;
    }

    private void ReleaseSlowMotion()
    {
        _slowMotionClaim?.Release();
        _slowMotionClaim = null;
    }

    private void RestoreInput()
    {
        if (!_inputSuppressed) return;

        player.RestoreInput();
        _inputSuppressed = false;
    }

    private void FinishSequence()
    {
        if (_sequenceCoroutine != null) StopCoroutine(_sequenceCoroutine);
        _sequenceCoroutine = null;
        ReleaseSlowMotion();
        RestoreInput();
        ReleaseVictoryCamera();
        ClearVictoryFraming();
        if (banner != null && banner.IsVisible) banner.Hide();

        if (_boss != null) _boss.DeathAnimationCompleted -= HandleDeathAnimationCompleted;
        _boss = null;
        _deathAnimationCompleted = false;
        IsRunning = false;
        CurrentPhase = SequencePhase.Idle;
    }

    // Frames the player and boss where they stand, without slow motion, input lock or banner.
    [FoldoutGroup("Debug"), Button("$CameraPreviewLabel"), DisableIf(nameof(IsRunning))]
    private void ToggleCameraPreview()
    {
        if (_isPreviewingCamera) EndCameraPreview();
        else BeginCameraPreview();
    }

    private void BeginCameraPreview()
    {
        // The arena only registers its boss in Play Mode.
        BossStateMachine previewBoss = arenaManager.Boss != null ? arenaManager.Boss : FindAnyObjectByType<BossStateMachine>();
        if (previewBoss == null)
        {
            Debug.LogWarning("[VictorySequence] The victory camera preview needs a boss in the scene.", this);
            return;
        }

        FrameVictoryCamera(previewBoss);
        _isPreviewingCamera = true;
#if UNITY_EDITOR
        // Edit Mode preview changes are unrecorded scene state, so end the preview
        // before a save, Play Mode switch or domain reload can keep them.
        EditorSceneManager.sceneSaving += HandleSceneSaving;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += EndCameraPreview;
        EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    private void EndCameraPreview()
    {
        if (!_isPreviewingCamera) return;

        _isPreviewingCamera = false;
        ReleaseVictoryCamera();
        ClearVictoryFraming();
#if UNITY_EDITOR
        EditorSceneManager.sceneSaving -= HandleSceneSaving;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= EndCameraPreview;
        EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

#if UNITY_EDITOR
    private void HandleSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
    {
        EndCameraPreview();
    }

    private void HandlePlayModeStateChanged(PlayModeStateChange change)
    {
        EndCameraPreview();
    }
#endif

    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"[VictorySequence] {message}", this);
    }
}
