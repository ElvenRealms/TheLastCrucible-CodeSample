using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Adapts the player camera while locked on. The camera keeps its existing
/// orbit, drives yaw and pitch toward the target, and aims at a script-owned
/// point biased from the player toward the target, without handing control to
/// a second CinemachineCamera.
/// </summary>
[RequireComponent(typeof(CinemachineCamera), typeof(CinemachineOrbitalFollow), typeof(CinemachineInputAxisController))]
[RequireComponent(typeof(CinemachineCameraOffset))]
public class LockOnCamera : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, SceneObjectsOnly]
    private LockOnController lockOn;

    [FoldoutGroup("Camera Tuning"), SerializeField, PropertyRange(20f, 80f)]
    private float fieldOfView = 52f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0.1f)]
    private float orbitDistance = 5f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0.1f)]
    [Tooltip("Orbit distance while locked on and the target is at or beyond lockedFarDistance.")]
    private float lockedFarOrbitDistance = 6.5f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0f)]
    [Tooltip("Target distance at which the locked orbit starts pulling back from orbitDistance.")]
    private float lockedNearDistance = 4f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0f)]
    [Tooltip("Target distance at which the locked orbit reaches lockedFarOrbitDistance.")]
    private float lockedFarDistance = 12f;
    [FoldoutGroup("Camera Tuning"), SerializeField, PropertyRange(-10f, 45f)]
    private float defaultVerticalAngle = 14f;
    [FoldoutGroup("Camera Tuning"), SerializeField, PropertyRange(-10f, 45f)]
    private float lockedVerticalAngle = 20f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0f)]
    private float verticalDamping = 0.15f;
    [FoldoutGroup("Camera Tuning"), SerializeField, PropertyRange(-2f, 2f)]
    [Tooltip("Physical camera-space offset during free look. Positive values move the camera to the right shoulder.")]
    private float freeLookCameraOffsetX = 0.7f;
    [FoldoutGroup("Camera Tuning"), SerializeField, PropertyRange(-2f, 2f)]
    [Tooltip("Physical camera-space offset while locked on. Positive values move the camera to the right shoulder.")]
    private float lockedCameraOffsetX = 0.4f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0f)]
    private float shoulderTransitionDamping = 0.2f;
    [FoldoutGroup("Camera Tuning"), SerializeField, PropertyRange(-0.5f, 0.5f)]
    private float freeLookScreenPositionY = 0.05f;
    [FoldoutGroup("Camera Tuning"), SerializeField]
    private float freeLookTargetOffsetY = 1.5f;
    [FoldoutGroup("Camera Tuning"), SerializeField]
    [Tooltip("Height above the player origin of the locked aim point.")]
    private float lockedAimHeight = 1.2f;
    [FoldoutGroup("Camera Tuning"), SerializeField, PropertyRange(0f, 1f)]
    [Tooltip("Fraction of the player-to-target distance the locked aim point leads toward the target.")]
    private float lockedAimBias = 0.35f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0f)]
    [Tooltip("Caps the locked aim lead so a distant target cannot push the player out of frame.")]
    private float lockedAimMaxLead = 2.5f;
    [FoldoutGroup("Camera Tuning"), SerializeField, MinValue(0f)]
    private float horizontalDamping = 0.15f;

    private CinemachineCamera cinemachineCamera;
    private CinemachineOrbitalFollow orbitalFollow;
    private CinemachineRotationComposer rotationComposer;
    private CinemachineCameraOffset cameraOffset;
    private CinemachineInputAxisController inputAxisController;
    private Transform currentTargetPoint;
    private Transform lockedAimPoint;
    private Vector3 freeLookTargetOffset;
    private float targetCameraOffsetX;
    private float shoulderOffsetVelocity;
    private float horizontalDampingVelocity;
    private float verticalDampingVelocity;
    private bool inputSuppressed;
    private bool pauseSuppressed;

    public bool IsInputSuppressed => inputSuppressed;
    public bool IsPauseSuppressed => pauseSuppressed;

    private void Awake()
    {
        CacheComponents();
        if (cameraOffset == null) cameraOffset = gameObject.AddComponent<CinemachineCameraOffset>();
        lockedAimPoint = new GameObject("Lock-On Aim Point").transform;
        ApplyCameraTuning();
    }

    private void OnDestroy()
    {
        if (lockedAimPoint != null) Destroy(lockedAimPoint.gameObject);
    }

    private void OnValidate()
    {
        CacheComponents();
        ApplyCameraTuning();
    }

    private void CacheComponents()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        orbitalFollow = GetComponent<CinemachineOrbitalFollow>();
        rotationComposer = GetComponent<CinemachineRotationComposer>();
        cameraOffset = GetComponent<CinemachineCameraOffset>();
        inputAxisController = GetComponent<CinemachineInputAxisController>();
    }

    private void ApplyCameraTuning()
    {
        if (cinemachineCamera != null)
            cinemachineCamera.Lens.FieldOfView = fieldOfView;

        if (orbitalFollow != null)
        {
            orbitalFollow.Radius = orbitDistance;
            orbitalFollow.VerticalAxis.Center = defaultVerticalAngle;
            orbitalFollow.VerticalAxis.Value = defaultVerticalAngle;
        }

        if (rotationComposer != null)
        {
            freeLookTargetOffset = rotationComposer.TargetOffset;
            freeLookTargetOffset.y = freeLookTargetOffsetY;
            ApplyRotationComposition(currentTargetPoint != null);
        }

        if (cameraOffset != null)
        {
            cameraOffset.enabled = true;
            cameraOffset.ApplyAfter = CinemachineCore.Stage.Aim;
            cameraOffset.PreserveComposition = false;
            SetCameraOffsetTarget(currentTargetPoint != null, true);
        }
    }

    private void ApplyRotationComposition(bool isLocked)
    {
        if (rotationComposer == null) return;

        ScreenComposerSettings composition = rotationComposer.Composition;
        composition.ScreenPosition.x = 0f;
        composition.ScreenPosition.y = freeLookScreenPositionY;
        rotationComposer.Composition = composition;

        // The locked aim point already includes its height, so no composer offset applies.
        rotationComposer.TargetOffset = isLocked ? Vector3.zero : freeLookTargetOffset;
    }

    private void SetCameraOffsetTarget(bool isLocked, bool snap)
    {
        targetCameraOffsetX = isLocked ? lockedCameraOffsetX : freeLookCameraOffsetX;
        shoulderOffsetVelocity = 0f;
        if (!snap || cameraOffset == null) return;

        Vector3 offset = cameraOffset.Offset;
        offset.x = targetCameraOffsetX;
        cameraOffset.Offset = offset;
    }

    private void UpdateCameraOffset()
    {
        if (cameraOffset == null) return;

        Vector3 offset = cameraOffset.Offset;
        offset.x = shoulderTransitionDamping <= 0f
            ? targetCameraOffsetX
            : Mathf.SmoothDamp(
                offset.x,
                targetCameraOffsetX,
                ref shoulderOffsetVelocity,
                shoulderTransitionDamping);
        cameraOffset.Offset = offset;
    }

    private void OnEnable()
    {
        if (lockOn == null) return;
        lockOn.TargetChanged += HandleTargetChanged;
        ApplyTarget(lockOn.CurrentTarget);
    }

    private void OnDisable()
    {
        if (lockOn != null) lockOn.TargetChanged -= HandleTargetChanged;
        ApplyTarget(null);
        SetCameraOffsetTarget(false, true);
    }

    /// <summary>
    /// Cancels any active Cinemachine input driver and keeps both orbit axes
    /// inert while a presentation sequence owns the camera.
    /// </summary>
    public void SuppressInput()
    {
        inputSuppressed = true;
        CancelOrbitInput();
        if (inputAxisController != null) inputAxisController.enabled = false;
    }

    public void RestoreInput()
    {
        inputSuppressed = false;
        SetCameraMode(currentTargetPoint != null);
    }

    /// <summary>
    /// Freezes lock-on framing updates while pause owns the active camera.
    /// </summary>
    public void SetPauseSuppressed(bool suppressed)
    {
        pauseSuppressed = suppressed;
    }

    private void Update()
    {
        if (pauseSuppressed) return;
        UpdateCameraOffset();
        if (currentTargetPoint == null || lockOn == null || orbitalFollow == null) return;
        Vector3 direction = lockOn.PlanarDirectionToTarget;
        if (direction.sqrMagnitude < Mathf.Epsilon) return;

        Vector3 playerPosition = lockOn.transform.position;
        Vector3 planarToTarget = currentTargetPoint.position - playerPosition;
        planarToTarget.y = 0f;
        float targetDistance = planarToTarget.magnitude;

        float targetYaw = Vector3.SignedAngle(Vector3.forward, direction, Vector3.up);
        orbitalFollow.HorizontalAxis.Value = Mathf.SmoothDampAngle(
            orbitalFollow.HorizontalAxis.Value,
            targetYaw,
            ref horizontalDampingVelocity,
            horizontalDamping);
        orbitalFollow.VerticalAxis.Value = Mathf.SmoothDamp(
            orbitalFollow.VerticalAxis.Value,
            lockedVerticalAngle,
            ref verticalDampingVelocity,
            verticalDamping);

        float pullBack = Mathf.InverseLerp(lockedNearDistance, lockedFarDistance, targetDistance);
        orbitalFollow.Radius = Mathf.Lerp(orbitDistance, lockedFarOrbitDistance, pullBack);

        float lead = Mathf.Min(targetDistance * lockedAimBias, lockedAimMaxLead);
        lockedAimPoint.position = playerPosition + Vector3.up * lockedAimHeight + direction * lead;
    }

    private void HandleTargetChanged(LockOnTarget target)
    {
        ApplyTarget(target);
    }

    private void ApplyTarget(LockOnTarget target)
    {
        horizontalDampingVelocity = 0f;
        verticalDampingVelocity = 0f;
        currentTargetPoint = target != null ? target.TargetPoint : null;
        SetCameraMode(currentTargetPoint != null);
    }

    private void SetCameraMode(bool isLocked)
    {
        CancelOrbitInput();
        if (cinemachineCamera != null)
        {
            CameraTarget cameraTarget = cinemachineCamera.Target;
            cameraTarget.CustomLookAtTarget = isLocked;
            cameraTarget.LookAtTarget = isLocked ? lockedAimPoint : null;
            cinemachineCamera.Target = cameraTarget;
        }
        if (!isLocked && orbitalFollow != null) orbitalFollow.Radius = orbitDistance;
        ApplyRotationComposition(isLocked);
        SetCameraOffsetTarget(isLocked, false);
        if (inputAxisController != null) inputAxisController.enabled = !isLocked && !inputSuppressed;
    }

    private void CancelOrbitInput()
    {
        if (inputAxisController == null || orbitalFollow == null) return;

        var horizontalController = inputAxisController.GetController("Look Orbit X");
        if (horizontalController != null)
            horizontalController.Driver.CancelCurrentInput(ref orbitalFollow.HorizontalAxis);

        var verticalController = inputAxisController.GetController("Look Orbit Y");
        if (verticalController != null)
            verticalController.Driver.CancelCurrentInput(ref orbitalFollow.VerticalAxis);

        var radialController = inputAxisController.GetController("Orbit Scale");
        if (radialController != null)
            radialController.Driver.CancelCurrentInput(ref orbitalFollow.RadialAxis);
    }
}
