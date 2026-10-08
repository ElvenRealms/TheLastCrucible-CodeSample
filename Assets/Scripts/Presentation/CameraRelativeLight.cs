using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Keeps a directional light at a fixed angle to the active camera, so a rim or fill light stays behind or in
/// front of the characters however the camera orbits. Pair it with a rendering layer mask that only the
/// characters' renderers share, so the arena's lighting is untouched.
/// </summary>
[RequireComponent(typeof(Light))]
public class CameraRelativeLight : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField]
    [Tooltip("Camera to follow. Uses Camera.main when empty.")]
    private Camera targetCamera;
    [FoldoutGroup("Config"), SerializeField, Range(-180f, 180f), SuffixLabel("deg")]
    [Tooltip("Yaw of the light relative to the camera's view direction. 180 shines back toward the camera for a rim; 0 shines along the view for a fill.")]
    private float yawOffset = 180f;
    [FoldoutGroup("Config"), SerializeField, Range(-89f, 89f), SuffixLabel("deg")]
    [Tooltip("Downward pitch of the light. Positive values shine from above.")]
    private float pitch = 35f;

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                return;
            }
        }

        Vector3 forward = targetCamera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return;
        }
        float yaw = Quaternion.LookRotation(forward).eulerAngles.y + yawOffset;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
