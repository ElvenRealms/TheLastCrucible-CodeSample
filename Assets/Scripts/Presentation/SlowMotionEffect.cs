using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Plays slow-motion beats through one <see cref="TimeScaleArbiter"/> claim held while any beat is
/// below full speed. Overlapping beats resolve to the lowest scale. Timelines run on unscaled time
/// and freeze while pause suspends them.
/// </summary>
public class SlowMotionEffect : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int _requestCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _currentScale = 1f;

    private readonly List<SlowBeat> _beats = new();
    private TimeScaleClaim _claim;
    private bool _pauseSuppressed;

    public int RequestCount => _requestCount;
    public bool IsSlowing => _claim != null;

    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        SlowMotionData data = effectData as SlowMotionData;
        if (data == null || !isActiveAndEnabled) return false;
        if (data.lethalOnly && !request.lethal) return true;

        _beats.Add(new SlowBeat(data));
        _requestCount++;
        if (showDebugLogs) Debug.Log($"{name} queued slow motion at scale {data.scale:0.###} after {data.delay:0.###} seconds.", this);
        return true;
    }

    /// <summary>
    /// Freezes every slow-motion timeline while pause holds gameplay.
    /// </summary>
    public void SuspendForPause()
    {
        _pauseSuppressed = true;
    }

    /// <summary>
    /// Restarts the timelines with the time they had left.
    /// </summary>
    public void ResumeFromPause()
    {
        _pauseSuppressed = false;
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

        ApplyScale(AdvanceBeats(Time.unscaledDeltaTime));
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
        _beats.Clear();
        ApplyScale(1f);
    }

    private float AdvanceBeats(float deltaTime)
    {
        float scale = 1f;
        for (int i = _beats.Count - 1; i >= 0; i--)
        {
            SlowBeat beat = _beats[i];
            beat.elapsed += deltaTime;
            if (beat.IsFinished)
            {
                _beats.RemoveAt(i);
                continue;
            }

            _beats[i] = beat;
            scale = Mathf.Min(scale, beat.Scale);
        }
        return scale;
    }

    private void ApplyScale(float scale)
    {
        _currentScale = scale;
        if (scale >= 1f)
        {
            if (_claim == null) return;

            _claim.Release();
            _claim = null;
            if (showDebugLogs) Debug.Log("Slow motion completed.", this);
            return;
        }

        if (_claim == null) _claim = TimeScaleArbiter.Claim(scale, this);
        else _claim.SetScale(scale);
    }

    private struct SlowBeat
    {
        public readonly float delay;
        public readonly float duration;
        public readonly float recover;
        public readonly float slowScale;
        public float elapsed;

        public SlowBeat(SlowMotionData data)
        {
            delay = data.delay;
            duration = data.duration;
            recover = data.recover;
            slowScale = data.scale;
            elapsed = 0f;
        }

        public bool IsFinished => elapsed >= delay + duration + recover;

        public float Scale
        {
            get
            {
                float held = elapsed - delay;
                if (held < 0f) return 1f;
                if (held < duration) return slowScale;
                return Mathf.SmoothStep(slowScale, 1f, (held - duration) / recover);
            }
        }
    }
}
