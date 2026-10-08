using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Plays screen pulses by driving one global post-processing volume per <see cref="ScreenPulseData"/>.
/// Envelopes run on unscaled time so pulses keep playing through hit stop and slow motion.
/// </summary>
public class ScreenPulseEffect : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int _pulseCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _lastPeakWeight;

    private readonly Dictionary<ScreenPulseData, Pulse> _pulses = new();

    public int PulseCount => _pulseCount;
    public float LastPeakWeight => _lastPeakWeight;

    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        ScreenPulseData data = effectData as ScreenPulseData;
        if (data == null || data.profile == null || !isActiveAndEnabled) return false;

        float peak = data.GetPeakWeight(request.damage, request.lethal);
        _lastPeakWeight = peak;
        _pulseCount++;
        if (peak <= 0f) return true;

        Pulse pulse = GetOrCreatePulse(data);
        pulse.envelope.Trigger(peak, data.attack, data.hold, data.release);
        pulse.volume.weight = pulse.envelope.Value;
        if (showDebugLogs) Debug.Log($"{name} started a screen pulse with peak weight {peak:0.###}.", this);
        return true;
    }

    private void OnEnable()
    {
        if (controller == null) controller = FindAnyObjectByType<CombatFeedbackController>();
        if (controller == null) Debug.LogWarning($"{name} has no CombatFeedbackController to register with.", this);
        else controller.Register(this);
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        foreach (Pulse pulse in _pulses.Values)
        {
            if (!pulse.envelope.IsActive) continue;
            pulse.volume.weight = pulse.envelope.Advance(deltaTime);
        }
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
        foreach (Pulse pulse in _pulses.Values)
        {
            pulse.envelope.Clear();
            pulse.volume.weight = 0f;
        }
    }

    private void OnDestroy()
    {
        foreach (Pulse pulse in _pulses.Values)
        {
            if (pulse.volume != null) Destroy(pulse.volume.gameObject);
        }
        _pulses.Clear();
    }

    private Pulse GetOrCreatePulse(ScreenPulseData data)
    {
        if (_pulses.TryGetValue(data, out Pulse pulse)) return pulse;

        var volumeObject = new GameObject($"ScreenPulse_{data.name}");
        volumeObject.transform.SetParent(transform, false);
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = data.profile;
        volume.priority = data.priority;
        volume.weight = 0f;

        pulse = new Pulse(volume);
        _pulses.Add(data, pulse);
        return pulse;
    }

    private sealed class Pulse
    {
        public readonly Volume volume;
        public FeedbackEnvelope envelope;

        public Pulse(Volume volume)
        {
            this.volume = volume;
        }
    }
}
