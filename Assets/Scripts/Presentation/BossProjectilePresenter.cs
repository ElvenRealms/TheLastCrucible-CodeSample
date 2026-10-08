using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// The charge tell, sound and hit feedback for the boss's projectiles, authored on each volley's
/// data; observes the boss and its launcher and never drives them. The launcher plays the
/// projectile effects itself.
/// </summary>
[DisallowMultipleComponent]
public class BossProjectilePresenter : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required]
    private BossStateMachine stateMachine;
    [FoldoutGroup("References"), SerializeField, Required]
    private BossProjectileLauncher launcher;
    [FoldoutGroup("References"), SerializeField, Required]
    private CharacterSockets sockets;

    [FoldoutGroup("Config"), SerializeField, Tooltip("The socket a volley's charge effect gathers on.")]
    private SocketId chargeSocket = SocketId.WeaponTip;
    [FoldoutGroup("Config"), SerializeField, MinValue(0.1f), SuffixLabel("s")]
    [Tooltip("Longest a charge effect may play, in case its attack never releases or ends.")]
    private float maxChargeDuration = 6f;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int _requestCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private CombatFeedbackRequest _lastRequest;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private bool _isCharging;

    private Transform _vfxContainer;
    private VfxPool _vfxPool;
    private VfxPool.Handle _charge;

    public bool IsCharging => _isCharging;
    public int RequestCount => _requestCount;
    public CombatFeedbackRequest LastRequest => _lastRequest;

    private void Awake()
    {
        if (stateMachine == null)
        {
            stateMachine = GetComponent<BossStateMachine>();
        }
        if (launcher == null)
        {
            launcher = GetComponent<BossProjectileLauncher>();
        }
        if (sockets == null)
        {
            sockets = GetComponent<CharacterSockets>();
        }
        if (controller == null)
        {
            controller = FindAnyObjectByType<CombatFeedbackController>();
        }
        if (controller == null)
        {
            Debug.LogWarning($"{name} has no CombatFeedbackController to forward feedback to.", this);
        }

        // Idle instances live at the scene root so they don't ride the character.
        _vfxContainer = new GameObject($"{name} Projectile VFX").transform;
        _vfxPool = new VfxPool(_vfxContainer, 1);
    }

    private void OnEnable()
    {
        if (launcher != null)
        {
            launcher.Fired += HandleFired;
            launcher.HitResolved += HandleHitResolved;
            launcher.Ended += HandleEnded;
        }
    }

    private void OnDisable()
    {
        if (launcher != null)
        {
            launcher.Fired -= HandleFired;
            launcher.HitResolved -= HandleHitResolved;
            launcher.Ended -= HandleEnded;
        }
        _isCharging = false;
        _vfxPool.ReleaseAll();
    }

    private void OnDestroy()
    {
        if (_vfxContainer != null)
        {
            Destroy(_vfxContainer.gameObject);
        }
    }

    private void LateUpdate()
    {
        // Read after the state machine ticks so a release or an interruption ends the charge this frame.
        bool charging = stateMachine != null && stateMachine.isActiveAndEnabled &&
            stateMachine.CurrentState is BossAttackState attack && attack.IsChargingProjectile;
        if (charging != _isCharging)
        {
            _isCharging = charging;
            if (charging)
            {
                StartCharge(stateMachine.Attack.ActiveProjectile);
            }
            else
            {
                _vfxPool.StopEmitting(_charge);
            }
        }
        _vfxPool.Tick();
    }

    private void StartCharge(BossProjectileData data)
    {
        Transform socket = sockets != null ? sockets.Get(chargeSocket) : null;
        if (data.ChargePrefab == null || socket == null)
        {
            return;
        }

        _charge = _vfxPool.Play(data.ChargePrefab, new Pose(socket.position, socket.rotation), data.ChargeScale, false, maxChargeDuration, socket);
    }

    private void HandleFired(BossProjectileData data, Vector3 position)
    {
        PlayCue(data.LaunchCue, position);
    }

    private void HandleEnded(BossProjectileData data, Vector3 position)
    {
        PlayCue(data.ImpactCue, position);
    }

    private void HandleHitResolved(BossProjectileData data, HitResult result, DamageInfo info)
    {
        if (result != HitResult.Applied || controller == null || data.HitFeedback == null)
        {
            return;
        }

        _lastRequest = new CombatFeedbackRequest(data.HitFeedback, info.amount, false, info.source, stateMachine.Target, info.hitPoint);
        _requestCount++;
        controller.RequestFeedback(_lastRequest);
        if (showDebugLogs)
        {
            Debug.Log($"{name} requested '{data.HitFeedback.name}' at {info.hitPoint}.", this);
        }
    }

    private void PlayCue(AudioCue cue, Vector3 position)
    {
        if (cue == null)
        {
            return;
        }

        if (cue.CountValidClips() == 0)
        {
            Debug.LogWarning($"[BossProjectilePresenter] Skipped '{cue.name}': no AudioClip is assigned.", this);
            return;
        }

        if (cue.SpatialMode == AudioSpatialMode.Spatial)
        {
            AudioPlaybackService.Instance?.PlayAtPosition(cue, position);
        }
        else
        {
            AudioPlaybackService.Instance?.Play(cue);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[BossProjectilePresenter] Played '{cue.name}'.", this);
        }
    }
}
