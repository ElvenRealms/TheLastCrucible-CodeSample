using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Cinemachine extension that adds the current <see cref="CameraMotionEffect"/> offset to the camera
/// after the pipeline has finished, as a correction, so the orbit and aim never chase the move.
/// </summary>
public class CinemachineCameraMotion : CinemachineExtension
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType while empty; assign it to skip the search.")]
    private CameraMotionEffect effect;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;

        ResolveEffect();
        if (effect == null) return;

        state.PositionCorrection += state.GetCorrectedOrientation() * effect.PositionOffset;
        state.OrientationCorrection = state.OrientationCorrection * Quaternion.Euler(effect.RotationOffset);
    }

    private void ResolveEffect()
    {
        if (effect != null) return;

        effect = FindAnyObjectByType<CameraMotionEffect>();
    }
}
