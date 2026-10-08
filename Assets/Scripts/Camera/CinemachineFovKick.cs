using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Cinemachine extension that adds the current <see cref="FovKickEffect"/> offset to the camera's
/// field of view after the pipeline has finished, so the kick sits on top of any other lens changes.
/// </summary>
public class CinemachineFovKick : CinemachineExtension
{
    private const float MinFieldOfView = 1f;

    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType while empty; assign it to skip the search.")]
    private FovKickEffect effect;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;

        ResolveEffect();
        if (effect == null) return;

        state.Lens.FieldOfView = Mathf.Max(MinFieldOfView, state.Lens.FieldOfView + effect.CurrentOffset);
    }

    private void ResolveEffect()
    {
        if (effect != null) return;

        effect = FindAnyObjectByType<FovKickEffect>();
    }
}
