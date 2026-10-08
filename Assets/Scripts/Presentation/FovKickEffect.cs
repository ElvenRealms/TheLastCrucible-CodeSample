using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Computes a field-of-view offset from combat feedback requests. The offset runs on unscaled
/// time and overlapping kicks keep the stronger one. <see cref="CinemachineFovKick"/> applies it to the camera.
/// </summary>
public class FovKickEffect : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int _kickCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _lastDegrees;

    private FeedbackEnvelope _envelope;

    /// <summary>
    /// Field-of-view change in degrees to add to the camera this frame.
    /// </summary>
    public float CurrentOffset => _envelope.Value;
    public int KickCount => _kickCount;

    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        FovKickData data = effectData as FovKickData;
        if (data == null || !isActiveAndEnabled) return false;

        float degrees = data.GetDegrees(request.damage, request.lethal);
        _lastDegrees = degrees;
        _kickCount++;
        if (Mathf.Approximately(degrees, 0f)) return true;

        _envelope.Trigger(degrees, data.attack, 0f, data.release);
        if (showDebugLogs) Debug.Log($"{name} started a FOV kick of {degrees:0.###} degrees.", this);
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
        _envelope.Advance(Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
        _envelope.Clear();
    }
}
