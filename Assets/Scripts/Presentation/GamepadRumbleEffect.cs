using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives gamepad motors from rumble requests. Overlapping requests merge into the strongest
/// active motor speeds, each fading out over the last part of its duration on unscaled time.
/// Motors are silenced while paused, unfocused or disabled.
/// </summary>
public class GamepadRumbleEffect : MonoBehaviour, ICombatFeedbackExecutor
{
    private const float TailFraction = 0.4f;

    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int _requestCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _lowSpeed;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _highSpeed;

    private readonly List<Rumble> _rumbles = new();
    private Gamepad _appliedGamepad;
    private bool _pauseSuppressed;
    // Run In Background keeps combat going while unfocused; the pad belongs to whatever has focus.
    private bool _hasFocus = true;

    public int RequestCount => _requestCount;

    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        RumbleData data = effectData as RumbleData;
        if (data == null || !isActiveAndEnabled) return false;

        // Claimed even without a gamepad so the controller does not warn about an undelivered effect.
        if (!_hasFocus || Gamepad.current == null || data.duration <= 0f) return true;

        float low = data.GetLowFrequency(request.damage, request.lethal);
        float high = data.GetHighFrequency(request.damage, request.lethal);
        if (low <= 0f && high <= 0f) return true;

        _rumbles.Add(new Rumble(low, high, data.duration));
        _requestCount++;
        if (showDebugLogs) Debug.Log($"{name} queued a rumble with low {low:0.###}, high {high:0.###}.", this);
        return true;
    }

    /// <summary>
    /// Silences the motors and freezes active rumbles while pause holds gameplay.
    /// </summary>
    public void SuspendForPause()
    {
        if (_pauseSuppressed) return;

        _pauseSuppressed = true;
        ApplyMotorSpeeds(0f, 0f);
    }

    /// <summary>
    /// Lets suspended rumbles continue with the time they had left.
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

        AdvanceRumbles(Time.unscaledDeltaTime, out float low, out float high);
        ApplyMotorSpeeds(low, high);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        _hasFocus = hasFocus;
        if (hasFocus) return;

        _rumbles.Clear();
        ApplyMotorSpeeds(0f, 0f);
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
        _rumbles.Clear();
        ApplyMotorSpeeds(0f, 0f);
    }

    private void AdvanceRumbles(float deltaTime, out float low, out float high)
    {
        low = 0f;
        high = 0f;
        for (int i = _rumbles.Count - 1; i >= 0; i--)
        {
            Rumble rumble = _rumbles[i];
            rumble.remaining -= deltaTime;
            if (rumble.remaining <= 0f)
            {
                _rumbles.RemoveAt(i);
                continue;
            }

            _rumbles[i] = rumble;
            float strength = rumble.Strength;
            low = Mathf.Max(low, rumble.low * strength);
            high = Mathf.Max(high, rumble.high * strength);
        }
    }

    private void ApplyMotorSpeeds(float low, float high)
    {
        Gamepad gamepad = Gamepad.current;
        if (_appliedGamepad != gamepad)
        {
            if (_appliedGamepad != null && _appliedGamepad.added) _appliedGamepad.SetMotorSpeeds(0f, 0f);
            _appliedGamepad = gamepad;
            _lowSpeed = 0f;
            _highSpeed = 0f;
        }

        if (gamepad == null) return;
        if (Mathf.Approximately(low, _lowSpeed) && Mathf.Approximately(high, _highSpeed)) return;

        gamepad.SetMotorSpeeds(low, high);
        _lowSpeed = low;
        _highSpeed = high;
    }

    private struct Rumble
    {
        public readonly float low;
        public readonly float high;
        public readonly float duration;
        public float remaining;

        public Rumble(float low, float high, float duration)
        {
            this.low = low;
            this.high = high;
            this.duration = duration;
            remaining = duration;
        }

        public float Strength => Mathf.Clamp01(remaining / (duration * TailFraction));
    }
}
