using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Owns lock-on state for the player: on toggle input, acquires whichever
/// registered LockOnTarget the camera is looking at. When the target dies it
/// retargets to the best target in view, releasing if none; leaving break
/// range always releases. Locomotion and the camera observe this; it never
/// drives them directly. PlayerStateMachine injects dependencies via Initialize.
/// </summary>
public class LockOnController : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, MinValue(0f)]
    private float acquireRange = 20f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f), ValidateInput(nameof(AcquireRangeWithinBreakRange), "breakRange must be greater than or equal to acquireRange.")]
    private float breakRange = 25f;
    [FoldoutGroup("Config"), SerializeField, Range(0f, 90f), Tooltip("Max angle from camera forward for a target to count as looked at.")]
    private float acquireAngle = 30f;
    [FoldoutGroup("Config"), SerializeField, Range(0f, 90f), Tooltip("Max angle from camera forward when auto-retargeting after the target dies; more forgiving than acquireAngle.")]
    private float retargetAngle = 60f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsLockedOn { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private LockOnTarget target;

    // Fired with the target on acquire, null on release.
    public event Action<LockOnTarget> TargetChanged;

    // Null-guarded: a destroyed target can be queried before Update releases it.
    public LockOnTarget CurrentTarget => IsLockedOn && target != null ? target : null;
    public Transform TargetPoint => IsLockedOn && target != null ? target.TargetPoint : null;
    public bool HasValidTarget => IsLockedOn && target != null && target.isActiveAndEnabled &&
                                  target.TargetPoint != null && DistanceToTarget() <= breakRange;

    // Flattened direction from the player to the target; zero when not locked.
    public Vector3 PlanarDirectionToTarget
    {
        get
        {
            if (!IsLockedOn || target == null || target.TargetPoint == null) return Vector3.zero;
            Vector3 direction = target.TargetPoint.position - transform.position;
            direction.y = 0f;
            return direction.normalized;
        }
    }

    private InputReader inputReader;
    private Transform cameraTransform;
    private bool subscribed;

    private bool AcquireRangeWithinBreakRange => acquireRange <= breakRange;

    // Awake/OnEnable can interleave across components during activation, so
    // whichever of Initialize/OnEnable runs last performs the subscription.
    public void Initialize(InputReader reader, Transform camera)
    {
        inputReader = reader;
        cameraTransform = camera;
        TrySubscribe();
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (!subscribed) return;
        inputReader.LockOn -= Toggle;
        subscribed = false;
    }

    private void TrySubscribe()
    {
        if (subscribed || inputReader == null || !isActiveAndEnabled) return;
        inputReader.LockOn += Toggle;
        subscribed = true;
    }

    private void Update()
    {
        if (!IsLockedOn) return;
        if (target == null || !target.isActiveAndEnabled || target.TargetPoint == null)
        {
            Retarget();
        }
        else if (DistanceToTarget() > breakRange)
        {
            Release();
        }
    }

    private void Toggle()
    {
        if (IsLockedOn)
        {
            Release();
            return;
        }

        LockOnTarget best = FindBestTarget(acquireAngle);
        if (best == null)
        {
            Log("No lock-on target in view");
            return;
        }
        target = best;
        IsLockedOn = true;
        Log($"Locked on to {target.name}");
        TargetChanged?.Invoke(target);
    }

    // Smallest angle from camera forward wins, within maxAngle and acquireRange.
    private LockOnTarget FindBestTarget(float maxAngle)
    {
        LockOnTarget best = null;
        float bestAngle = maxAngle;
        foreach (LockOnTarget candidate in LockOnTarget.Active)
        {
            if (candidate.TargetPoint == null) continue;
            Vector3 targetPosition = candidate.TargetPoint.position;
            if (Vector3.Distance(transform.position, targetPosition) > acquireRange) continue;
            float angle = Vector3.Angle(cameraTransform.forward, targetPosition - cameraTransform.position);
            if (angle > bestAngle) continue;
            bestAngle = angle;
            best = candidate;
        }
        return best;
    }

    // A dead or destroyed target hands the lock to the best target the camera
    // is currently looking at, like acquisition but with the more forgiving
    // retargetAngle. Releases when nothing qualifies.
    private void Retarget()
    {
        LockOnTarget next = FindBestTarget(retargetAngle);
        if (next == null)
        {
            Release();
            return;
        }
        target = next;
        Log($"Retargeted to {target.name}");
        TargetChanged?.Invoke(target);
    }

    public void Release()
    {
        if (!IsLockedOn && target == null) return;
        IsLockedOn = false;
        target = null;
        Log("Lock-on released");
        TargetChanged?.Invoke(null);
    }

    private float DistanceToTarget()
    {
        return Vector3.Distance(transform.position, target.TargetPoint.position);
    }

    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"[LockOnController] {message}", this);
    }
}
