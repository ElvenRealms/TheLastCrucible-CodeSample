using System.Collections;
using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Presentation that accompanies the boss's intro beat. Owns the control lock,
/// the intro camera and the Boss Name Card; never owns encounter or boss state.
/// Runs for as long as the boss stays in its intro state, so the boss's intro
/// clip sets the length.
/// </summary>
public class IntroSequence : MonoBehaviour
{
    [Tooltip("Publishes the activation this sequence follows.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [Tooltip("Locked for input until the boss finishes its intro.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStateMachine player;
    [Tooltip("Composed camera blended to during the intro; follows the boss, so its follow offset is boss-relative.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private CinemachineCamera introCamera;
    [Tooltip("Boss Name Card shown as the intro starts.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private BossNameCard nameCard;

    [Tooltip("Cinemachine priority applied to the intro camera to take over the shot.")]
    [FoldoutGroup("Timing"), SerializeField]
    private int introCameraPriority = 20;
    [Tooltip("Seconds before the intro ends at which the camera starts back to the player. Match the blend out of the intro camera, so control returns with the gameplay view.")]
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float cameraReturnLead = 0.8f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsRunning { get; private set; }

    private BossStateMachine _boss;
    private Coroutine _sequenceCoroutine;
    private bool _inputSuppressed;

    private bool IsBossInIntro => _boss != null && _boss.CurrentState == _boss.Intro;

    private void OnEnable()
    {
        arenaManager.PhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        arenaManager.PhaseChanged -= HandlePhaseChanged;
        FinishSequence();
    }

    // PhaseChanged follows EncounterActivated, so the boss has already chosen its intro state.
    private void HandlePhaseChanged(EncounterPhase phase)
    {
        if (phase != EncounterPhase.Active)
        {
            FinishSequence();
            return;
        }

        BossStateMachine boss = arenaManager.Boss;
        if (IsRunning || boss == null || boss.CurrentState != boss.Intro) return;

        _boss = boss;
        IsRunning = true;
        _sequenceCoroutine = StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        player.SuppressInput();
        _inputSuppressed = true;
        FrameIntroCamera();
        nameCard.Show();
        Log("Intro started.");

        while (IsBossInIntro && _boss.Animation.GetRemainingDuration() > cameraReturnLead)
            yield return null;

        ReleaseIntroCamera();
        while (IsBossInIntro)
            yield return null;

        _sequenceCoroutine = null;
        FinishSequence();
    }

    private void FrameIntroCamera()
    {
        Transform bossTransform = _boss.transform;
        introCamera.Follow = bossTransform;
        introCamera.LookAt = _boss.LockOnTarget != null && _boss.LockOnTarget.TargetPoint != null
            ? _boss.LockOnTarget.TargetPoint
            : bossTransform;
        introCamera.enabled = true;
        introCamera.Priority.Value = introCameraPriority;
    }

    private void ReleaseIntroCamera()
    {
        if (introCamera == null) return;

        introCamera.Priority = new PrioritySettings();
        introCamera.enabled = false;
    }

    // The name card keeps its own timing, so an intro that ends normally leaves it to fade out by itself.
    private void FinishSequence()
    {
        if (!IsRunning) return;

        if (_sequenceCoroutine != null)
        {
            StopCoroutine(_sequenceCoroutine);
            _sequenceCoroutine = null;
            if (nameCard != null) nameCard.Hide();
        }

        ReleaseIntroCamera();
        if (_inputSuppressed) player.RestoreInput();
        _inputSuppressed = false;
        _boss = null;
        IsRunning = false;
        Log("Intro finished.");
    }

    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"[IntroSequence] {message}", this);
    }
}
