using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Merges overlapping hit-stop requests into one <see cref="TimeScaleArbiter"/> claim held until
/// the latest requested deadline. Deadlines run on unscaled time and freeze while pause suspends them.
/// </summary>
public class HitStopController : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Range(0f, 1f), Tooltip("Time scale applied during hit stop. Keep at 0 for a full freeze.")]
    private float stoppedTimeScale;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private bool _isStopping;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _stopEndsAt;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _lastRequestedDuration;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int _requestCount;

    private TimeScaleClaim _claim;
    private float _pausedStopRemaining;
    private bool _pauseSuppressed;

    public bool IsStopping => _isStopping;
    public float LastRequestedDuration => _lastRequestedDuration;
    public int RequestCount => _requestCount;
    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        HitStopData data = effectData as HitStopData;
        if (data == null || !isActiveAndEnabled) return false;

        float duration = data.GetDuration(request.damage, request.lethal);
        return RequestHitStop(duration);
    }

    private void OnEnable()
    {
        if (controller == null) controller = FindAnyObjectByType<CombatFeedbackController>();
        if (controller == null) Debug.LogWarning($"{name} has no CombatFeedbackController to register with.", this);
        else controller.Register(this);
    }

    private void Update()
    {
        if (_pauseSuppressed) return;
        if (_isStopping && Time.unscaledTime >= _stopEndsAt) CompleteHitStop();
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
        CompleteHitStop();
    }

    public bool RequestHitStop(float duration)
    {
        duration = Mathf.Max(0f, duration);
        if (duration <= 0f || !isActiveAndEnabled) return false;

        if (!_isStopping)
        {
            _isStopping = true;
            _claim = TimeScaleArbiter.Claim(stoppedTimeScale, this);
        }

        _lastRequestedDuration = duration;
        _requestCount++;
        if (_pauseSuppressed) _pausedStopRemaining = Mathf.Max(_pausedStopRemaining, duration);
        else _stopEndsAt = Mathf.Max(_stopEndsAt, Time.unscaledTime + duration);

        if (showDebugLogs) Debug.Log($"Hit stop requested for {duration:0.###} seconds.", this);
        return true;
    }

    /// <summary>
    /// Freezes the unscaled hit-stop deadline while pause holds gameplay.
    /// </summary>
    public void SuspendForPause()
    {
        if (_pauseSuppressed) return;

        _pauseSuppressed = true;
        if (_isStopping)
            _pausedStopRemaining = Mathf.Max(0f, _stopEndsAt - Time.unscaledTime);
    }

    /// <summary>
    /// Restarts a suspended hit-stop deadline with the time it had left.
    /// </summary>
    public void ResumeFromPause()
    {
        if (!_pauseSuppressed) return;

        _pauseSuppressed = false;
        if (_isStopping) _stopEndsAt = Time.unscaledTime + _pausedStopRemaining;
    }

    private void CompleteHitStop()
    {
        if (!_isStopping) return;

        _claim.Release();
        _claim = null;
        _isStopping = false;
        _stopEndsAt = 0f;
        _pausedStopRemaining = 0f;
        if (showDebugLogs) Debug.Log("Hit stop completed.", this);
    }
}
