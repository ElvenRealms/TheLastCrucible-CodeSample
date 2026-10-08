using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Plays directed camera moves from combat feedback requests and exposes their summed offset.
/// The moves run on scaled time, so hit-stop and slow motion hold them with the swing they
/// emphasize. <see cref="CinemachineCameraMotion"/> applies the offset to the camera.
/// </summary>
public class CameraMotionEffect : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int _moveCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int ActiveMoveCount => _playback.ActiveCount;

    private readonly CameraMotionPlayback _playback = new();

    /// <summary>Camera-space position offset to add to the camera this frame.</summary>
    public Vector3 PositionOffset => _playback.PositionOffset;
    /// <summary>Camera-space rotation in degrees to add to the camera this frame.</summary>
    public Vector3 RotationOffset => _playback.RotationOffset;
    public int MoveCount => _moveCount;

    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        CameraMotionData data = effectData as CameraMotionData;
        if (data == null || !isActiveAndEnabled) return false;

        _playback.Trigger(data);
        _moveCount++;
        if (showDebugLogs) Debug.Log($"{name} started camera move '{data.name}'.", this);
        return true;
    }

    private void OnEnable()
    {
        _moveCount = 0;
        if (controller == null) controller = FindAnyObjectByType<CombatFeedbackController>();
        if (controller == null) Debug.LogWarning($"{name} has no CombatFeedbackController to register with.", this);
        else controller.Register(this);
    }

    private void Update()
    {
        _playback.Advance(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
        _playback.Clear();
    }
}
