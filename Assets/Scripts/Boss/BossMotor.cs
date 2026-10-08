using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Owns boss navigation and facing so states express movement intent without
/// accessing NavMeshAgent directly. Root motion is routed through this
/// motor's OnAnimatorMove, making it the sole writer of the transform: root
/// translation is constrained by combat bodies and the NavMesh, while a turn clip's
/// yaw is scaled to land on the live target heading instead of the clip's own.
/// </summary>
[RequireComponent(typeof(NavMeshAgent)), RequireComponent(typeof(Animator))]
public class BossMotor : MonoBehaviour
{
    private const float PursuitDestinationRefreshDistance = 0.5f;
    // Bounds the per-frame turn yaw scale, which spikes when the target moves
    // late in a clip whose own yaw is nearly spent.
    private const float MaxTurnYawScale = 3f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public float PlanarSpeed => PlanarVelocity.magnitude;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsOnNavMesh => agent != null && agent.enabled && agent.isOnNavMesh;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, SuffixLabel("m")]
    public float DistanceMatchOffset => _matchOffset;
    public bool IsNavigationEnabled => agent != null && agent.enabled;
    public bool IsPathPending => IsOnNavMesh && agent.pathPending;

    // Reports what the motor actually applied rather than the agent's internal
    // simulation, which no longer drives the transform.
    public Vector3 PlanarVelocity => appliedPlanarVelocity;

    private NavMeshAgent agent;
    private Animator animator;
    private BossMovementConfigData config;
    private Vector3 appliedPlanarVelocity;
    private float yawVelocity;
    private bool isTurning;
    private bool _isRootMotionSuspended;
    private bool _allowVerticalRootMotion;
    private float _rootMotionScale = 1f;
    private float _travelScale = 1f;
    private bool _isPauseSuppressed;
    private float _turnClipSignedAngle;
    private float _turnClipYaw;
    private float _turnAppliedYaw;
    private float _turnStartYaw;
    private float _turnRequestedSignedAngle;
    private float _turnMaxTargetAdjustment;
    private float _turnTargetAdjustment;
    private Transform _pursuitTarget;
    private Vector3 _pursuitDestination;
    private bool _hasPursuitDestination;
    private bool _isWalking;
    private bool _isDistanceMatching;
    private float _matchMaxExtension;
    private float _matchReachDistance;
    private float _matchClipTravel;
    private float _matchAppliedTravel;
    private float _matchOffset;
    private float _matchFraction;
    private Vector3 _matchTargetPosition;
    private CombatBodyConstraint _bodyConstraint;
    private Vector3 _shoveDisplacement;
    private float _shoveDuration;
    private float _shoveElapsed;
    private float _shoveProgress = 1f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule == null || capsule.direction != 1)
        {
            Debug.LogError("BossMotor requires an upright CapsuleCollider for combat body blocking.", this);
            enabled = false;
            return;
        }
        _bodyConstraint = new CombatBodyConstraint(capsule);
    }

    private void OnEnable()
    {
        _bodyConstraint?.Register();
    }

    private void OnDisable()
    {
        _bodyConstraint?.Unregister();
    }

    /// <summary>
    /// Applies root motion for the frame unless it is suspended. Outside a
    /// turn, planar translation is scaled by <see cref="SetRootMotionScale"/>,
    /// offset by any distance match (<see cref="BeginDistanceMatch"/>), then
    /// stops or slides at combat bodies; rotation is unchanged.
    /// During a turn (<see cref="BeginTurn"/>) each frame's clip yaw is
    /// scaled by the yaw still needed over the clip yaw still to come, so the
    /// boss lands on the live target heading as the clip's rotation finishes.
    /// Turn translation is still applied, so the feet stay planted while the
    /// clip steps around.
    /// </summary>
    private void OnAnimatorMove()
    {
        if (animator == null || _isRootMotionSuspended || _isPauseSuppressed)
        {
            return;
        }

        if (!isTurning)
        {
            Vector3 deltaPosition = animator.deltaPosition;
            deltaPosition.x *= _rootMotionScale;
            deltaPosition.z *= _rootMotionScale;
            float clipTravel = Vector3.Dot(deltaPosition, PlanarForward);
            deltaPosition.x *= _travelScale;
            deltaPosition.z *= _travelScale;
            if (_isDistanceMatching)
            {
                Vector3 matchStep = NextDistanceMatchStep(deltaPosition);
                _matchClipTravel += clipTravel;
                _matchAppliedTravel += Vector3.Dot(deltaPosition, PlanarForward);
                deltaPosition += matchStep;
            }
            ApplyTranslation(deltaPosition);
            transform.rotation *= animator.deltaRotation;
            return;
        }

        float clipYawDelta = animator.deltaRotation.eulerAngles.y;
        if (clipYawDelta > 180f) clipYawDelta -= 360f;
        float appliedYawDelta = clipYawDelta * CurrentTurnYawScale();
        _turnClipYaw += clipYawDelta;
        _turnAppliedYaw += appliedYawDelta;
        transform.Rotate(0f, appliedYawDelta, 0f, Space.World);
        ApplyTranslation(animator.deltaPosition);
    }

    /// <summary>
    /// Degrees the active turn still has to rotate to reach its target
    /// heading, or zero when no turn is active.
    /// </summary>
    public float RemainingTurnAngle => isTurning ? Mathf.Abs(TurnTargetSignedAngle - _turnAppliedYaw) : 0f;

    private float TurnTargetSignedAngle => _turnRequestedSignedAngle + _turnTargetAdjustment;

    private Vector3 PlanarForward
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.normalized;
        }
    }

    // The unassisted strike point lies the authored reach minus the clip's
    // travel so far ahead of the boss, so the remaining correction is the
    // target's forward distance past that point. That travel is counted before
    // the travel scale, so travel the scale holds back or adds is made up by the
    // correction and the swing lands where it would without it. The total offset
    // never exceeds the extension limit or cancels more than the travel applied.
    private Vector3 NextDistanceMatchStep(Vector3 clipDelta)
    {
        if (_matchReachDistance <= 0f || _matchFraction <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 forward = PlanarForward;
        float targetDistance = Vector3.Dot(_matchTargetPosition - transform.position, forward);
        float correction = targetDistance - (_matchReachDistance - _matchClipTravel);
        float maxStep = config.maxDistanceMatchSpeed * Time.deltaTime;
        float step = Mathf.Clamp(correction * _matchFraction, -maxStep, maxStep);
        float clipTravel = Mathf.Max(0f, _matchAppliedTravel + Vector3.Dot(clipDelta, forward));
        float offset = Mathf.Clamp(_matchOffset + step, -clipTravel, _matchMaxExtension);
        step = offset - _matchOffset;
        _matchOffset = offset;
        _matchFraction = 0f;
        return forward * step;
    }

    // Zero once the clip's own yaw is spent or the target has been passed;
    // the remaining residual is closed procedurally after the turn.
    private float CurrentTurnYawScale()
    {
        float remainingClipYaw = _turnClipSignedAngle - _turnClipYaw;
        if (remainingClipYaw * _turnClipSignedAngle <= 0f || Mathf.Abs(remainingClipYaw) < 1f) return 0f;
        float remainingTargetYaw = TurnTargetSignedAngle - _turnAppliedYaw;
        return Mathf.Clamp(remainingTargetYaw / remainingClipYaw, 0f, MaxTurnYawScale);
    }

    /// <summary>
    /// Keeps the agent's internal simulation anchored to wherever this motor's
    /// movement put the boss this frame, and holds the boss on the walkable
    /// NavMesh so root motion cannot carry it through obstacles or off the
    /// arena. The agent stays grounded even while an attack allows the
    /// animated boss to leave the NavMesh vertically.
    /// </summary>
    private void LateUpdate()
    {
        if (_isPauseSuppressed || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }
        ApplyShove();
        Vector3 position = transform.position;
        float groundHeight = agent.nextPosition.y;
        // Setting nextPosition moves the agent toward it within the NavMesh's
        // boundaries, so reading it back gives the reachable planar position.
        agent.nextPosition = new Vector3(position.x, groundHeight, position.z);
        Vector3 reachable = agent.nextPosition;
        position.x = reachable.x;
        position.z = reachable.z;
        if (!_allowVerticalRootMotion)
        {
            position.y = groundHeight;
        }
        transform.position = position;
    }

    public void Initialize(BossMovementConfigData movementConfig)
    {
        // BossStateMachine is a sibling component, so its Awake may run before
        // ours. Acquire the required agent and animator here as well to make
        // initialization independent of Unity's component Awake ordering.
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponent<Animator>();
        config = movementConfig;
        agent.speed = config.speed;
        agent.acceleration = config.acceleration;
        agent.stoppingDistance = 0f;
        // This motor is the sole writer of the transform's position and
        // rotation — procedurally in Pursue/RotateToward, and via root motion
        // routed through OnAnimatorMove otherwise — so the agent must never
        // write either itself. It stays a pathfinder that only advises.
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = false;
    }

    /// <summary>
    /// Resumes the agent and paths it to the target, reporting the planar
    /// direction to steer toward once a path is ready. While the path is
    /// pending, the direction remains zero so navigation never turns directly
    /// toward the target before the NavMesh has chosen a route. Also picks the
    /// walk or run gait from the target's distance. Returns false when the
    /// agent cannot be used.
    /// </summary>
    public bool PreparePursuit(Transform target, out Vector3 planarDirection)
    {
        planarDirection = Vector3.zero;
        if (!CanUseAgent() || target == null) return false;

        agent.isStopped = false;
        agent.speed = SelectPursuitSpeed(target.position);
        RefreshPursuitDestination(target);

        if (agent.pathPending) return true;

        planarDirection = agent.desiredVelocity;
        planarDirection.y = 0f;
        planarDirection.Normalize();
        return true;
    }

    /// <summary>
    /// Keeps a held path aimed at the target during an in-place turn and
    /// returns the planar direction to the path's next corner, or zero while
    /// the path is pending or unavailable.
    /// </summary>
    public Vector3 TrackPathDirection(Transform target)
    {
        if (!CanUseAgent() || target == null) return Vector3.zero;

        RefreshPursuitDestination(target);
        if (agent.pathPending || !agent.hasPath) return Vector3.zero;

        Vector3 direction = agent.steeringTarget - transform.position;
        direction.y = 0f;
        return direction.normalized;
    }

    private void RefreshPursuitDestination(Transform target)
    {
        Vector3 targetPosition = target.position;
        bool targetChanged = _pursuitTarget != target;
        bool destinationMoved = !_hasPursuitDestination ||
            Vector3.SqrMagnitude(targetPosition - _pursuitDestination) >= PursuitDestinationRefreshDistance * PursuitDestinationRefreshDistance;
        bool pathUnavailable = !agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid;
        bool hasReachedDestination = agent.hasPath && !float.IsInfinity(agent.remainingDistance) &&
            agent.remainingDistance <= agent.stoppingDistance + 0.05f;
        bool targetStillOffset = _hasPursuitDestination &&
            Vector3.SqrMagnitude(targetPosition - _pursuitDestination) > 0.0001f;
        bool shouldSetDestination = targetChanged || destinationMoved || pathUnavailable ||
            (hasReachedDestination && targetStillOffset);
        if (!agent.pathPending && shouldSetDestination && agent.SetDestination(targetPosition))
        {
            _pursuitTarget = target;
            _pursuitDestination = targetPosition;
            _hasPursuitDestination = true;
        }
    }

    /// <summary>
    /// Advances along the agent's path toward the destination set by
    /// <see cref="PreparePursuit"/>, scaling translation speed down by heading
    /// error while always rotating toward the unscaled direction of travel.
    /// Holds position while the path is still pending.
    /// </summary>
    public void Pursue()
    {
        if (!CanUseAgent()) return;

        if (agent.pathPending)
        {
            appliedPlanarVelocity = Vector3.zero;
            return;
        }

        Vector3 desiredVelocity = agent.desiredVelocity;
        desiredVelocity.y = 0f;
        float headingError = Vector3.Angle(transform.forward, desiredVelocity);
        Vector3 velocity = desiredVelocity * config.speedByHeadingError.Evaluate(headingError);
        ApplyTranslation(velocity * Time.deltaTime);
        RotateToward(desiredVelocity);
    }

    /// <summary>
    /// Holds position and turns in place toward the given planar direction.
    /// </summary>
    public void FaceInPlace(Vector3 planarDirection)
    {
        Stop();
        RotateToward(planarDirection);
    }

    /// <summary>
    /// Holds navigation, faces the target and steps sideways around it at
    /// <paramref name="speed"/>; a positive <paramref name="side"/> circles to the boss's
    /// right. Returns false, without stepping, when the NavMesh ends within a body
    /// width on that side.
    /// </summary>
    public bool Strafe(Vector3 planarDirectionToTarget, float side, float speed)
    {
        Stop();
        RotateToward(planarDirectionToTarget);
        planarDirectionToTarget.y = 0f;
        if (!CanUseAgent() || planarDirectionToTarget.sqrMagnitude <= 0.0001f) return false;

        Vector3 tangent = Vector3.Cross(Vector3.up, planarDirectionToTarget.normalized) * Mathf.Sign(side);
        Vector3 step = tangent * (speed * Time.deltaTime);
        if (agent.Raycast(transform.position + step + tangent * agent.radius, out _)) return false;

        ApplyTranslation(step);
        return true;
    }

    /// <summary>
    /// Whether the boss could walk to a position in a straight line. False
    /// when the NavMesh ends in between, so an attack's travel would be held
    /// at the edge and fall short. True without a usable agent, which holds
    /// nothing back.
    /// </summary>
    public bool HasWalkableLineTo(Vector3 position)
    {
        return !IsOnNavMesh || !agent.Raycast(position, out _);
    }

    public bool IsFacing(Vector3 planarDirection, float toleranceDegrees)
    {
        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude <= 0.0001f) return true;
        return Vector3.Angle(transform.forward, planarDirection) <= toleranceDegrees;
    }

    public void Stop()
    {
        appliedPlanarVelocity = Vector3.zero;
        _pursuitTarget = null;
        _hasPursuitDestination = false;
        if (!CanUseAgent()) return;
        agent.isStopped = true;
        agent.ResetPath();
    }

    /// <summary>
    /// Stops translation for an in-place navigation turn without discarding
    /// the active path.
    /// </summary>
    public void HoldNavigation()
    {
        appliedPlanarVelocity = Vector3.zero;
        if (!CanUseAgent()) return;
        agent.isStopped = true;
    }

    /// <summary>
    /// Prevents animation root motion from translating or rotating the boss.
    /// </summary>
    public void SuspendRootMotion()
    {
        _isRootMotionSuspended = true;
    }

    /// <summary>
    /// Allows animation root motion to move the boss again.
    /// </summary>
    public void ResumeRootMotion()
    {
        _isRootMotionSuspended = false;
    }

    /// <summary>
    /// Lets an attack animate above the NavMesh. Restores grounded height
    /// immediately on exit, including interruptions before the clip lands.
    /// </summary>
    public void SetVerticalRootMotionAllowed(bool allowed)
    {
        bool wasAllowed = _allowVerticalRootMotion;
        _allowVerticalRootMotion = allowed;
        if (!wasAllowed || allowed || !IsOnNavMesh)
        {
            return;
        }

        Vector3 position = transform.position;
        position.y = agent.nextPosition.y;
        transform.position = position;
    }

    /// <summary>
    /// Multiplies the horizontal travel of root motion, so an attack can cover
    /// more or less ground than its clip. 1 restores the authored distance.
    /// </summary>
    public void SetRootMotionScale(float scale)
    {
        _rootMotionScale = Mathf.Max(0f, scale);
    }

    /// <summary>
    /// Multiplies the horizontal travel of root motion for this moment of a clip, on top of
    /// <see cref="SetRootMotionScale"/>. Distance matching makes up the difference, so this
    /// moves travel to another part of the wind-up without changing where the swing lands.
    /// </summary>
    public void SetTravelScale(float scale)
    {
        _travelScale = Mathf.Max(0f, scale);
    }

    /// <summary>
    /// Starts measuring an attack step's forward root travel. When
    /// <paramref name="reachDistance"/> is positive, that travel is also
    /// stretched or shortened so a swing that connects at this distance from
    /// its start lands on the target given to <see cref="UpdateDistanceMatch"/>,
    /// carrying the boss at most <paramref name="maxExtension"/> beyond the clip's own travel.
    /// </summary>
    public void BeginDistanceMatch(float reachDistance, float maxExtension)
    {
        _isDistanceMatching = true;
        _matchReachDistance = reachDistance;
        _matchMaxExtension = Mathf.Max(0f, maxExtension);
        _matchClipTravel = 0f;
        _matchAppliedTravel = 0f;
        _matchOffset = 0f;
        _matchFraction = 0f;
    }

    /// <summary>
    /// Aims the next root-motion frame at the target, applying
    /// <paramref name="fraction"/> of the remaining correction; 1 closes it in
    /// one frame, within the configured speed and extension limits.
    /// </summary>
    public void UpdateDistanceMatch(Vector3 targetPosition, float fraction)
    {
        _matchTargetPosition = targetPosition;
        _matchFraction = Mathf.Clamp01(fraction);
    }

    /// <summary>
    /// Stops distance matching and leaves the boss where it is. Safe to call
    /// when no match is active.
    /// </summary>
    public void EndDistanceMatch()
    {
        _isDistanceMatching = false;
        _matchFraction = 0f;
    }

    /// <summary>
    /// Pushes the boss by a planar displacement over <paramref name="duration"/> seconds,
    /// fastest at the start, on top of whatever else moves it. Combat bodies and the
    /// NavMesh still constrain the push. A new shove replaces one still in progress.
    /// </summary>
    public void Shove(Vector3 planarDisplacement, float duration)
    {
        planarDisplacement.y = 0f;
        _shoveDisplacement = planarDisplacement;
        _shoveDuration = Mathf.Max(0f, duration);
        _shoveElapsed = 0f;
        _shoveProgress = 0f;
    }

    /// <summary>
    /// Drops the rest of a shove in progress. Safe to call when none is active.
    /// </summary>
    public void CancelShove()
    {
        _shoveProgress = 1f;
    }

    /// <summary>
    /// Prevents this motor from writing the boss transform while pause owns gameplay time.
    /// </summary>
    public void SetPauseSuppressed(bool suppressed)
    {
        _isPauseSuppressed = suppressed;
    }

    public void ResumeNavigation()
    {
        if (!CanUseAgent()) return;
        agent.isStopped = false;
    }

    public void DisableNavigation()
    {
        CancelShove();
        if (!agent.enabled) return;
        Stop();
        agent.enabled = false;
    }

    /// <summary>
    /// Rebinds the NavMeshAgent after a terminal death state. The authored
    /// pose is restored exactly; a retry is rejected when that pose cannot be
    /// rebound to the baked NavMesh.
    /// </summary>
    public bool ResetNavigation(Vector3 position, Quaternion rotation)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent == null) return false;

        appliedPlanarVelocity = Vector3.zero;
        _pursuitTarget = null;
        _hasPursuitDestination = false;
        EndTurn();
        EndDistanceMatch();
        CancelShove();
        _allowVerticalRootMotion = false;
        _rootMotionScale = 1f;
        _travelScale = 1f;

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        agent.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        agent.enabled = true;
        agent.updatePosition = false;
        agent.updateRotation = false;

        if (!agent.isOnNavMesh)
        {
            agent.enabled = false;
            Debug.LogError("BossMotor cannot reset navigation because its authored pose is off the NavMesh.", this);
            return false;
        }

        if (!agent.Warp(position))
        {
            agent.enabled = false;
            Debug.LogError("BossMotor failed to warp its NavMeshAgent to the authored pose.", this);
            return false;
        }

        if (!agent.isOnNavMesh)
        {
            agent.enabled = false;
            Debug.LogError("BossMotor lost its NavMesh binding while resetting to the authored pose.", this);
            return false;
        }

        agent.isStopped = true;
        return true;
    }

    /// <summary>
    /// Starts a turn clip toward the requested angle. <see cref="OnAnimatorMove"/>
    /// rescales the clip's root yaw every frame so the boss reaches the target
    /// heading as the clip's rotation ends. Animancer's cross-fade blends the
    /// clip against the outgoing one, so the yaw can under-deliver by a few
    /// degrees; that residual is closed by the motor after the turn. A bounded
    /// live-target correction is measured from the captured entry heading, so
    /// a target crossing the yaw wrap cannot reverse a committed long-way turn.
    /// </summary>
    public void BeginTurn(float requestedSignedAngle, float clipSignedAngle, float maxTargetAdjustment)
    {
        if (Mathf.Approximately(clipSignedAngle, 0f))
        {
            Debug.LogWarning("BossMotor cannot begin a turn because the clip's signed angle is zero.", this);
            return;
        }

        isTurning = true;
        yawVelocity = 0f;
        _turnClipSignedAngle = clipSignedAngle;
        _turnClipYaw = 0f;
        _turnAppliedYaw = 0f;
        _turnStartYaw = transform.eulerAngles.y;
        _turnRequestedSignedAngle = requestedSignedAngle;
        _turnMaxTargetAdjustment = Mathf.Max(0f, maxTargetAdjustment);
        _turnTargetAdjustment = 0f;
    }

    /// <summary>
    /// Updates the live target of a turn while limiting its correction around
    /// the target heading captured when the turn began.
    /// </summary>
    public void UpdateTurnTarget(Vector3 planarDirection)
    {
        if (!isTurning || _turnMaxTargetAdjustment <= 0f) return;

        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude <= 0.0001f) return;

        float targetYaw = Quaternion.LookRotation(planarDirection, Vector3.up).eulerAngles.y;
        float targetAngle = Mathf.DeltaAngle(_turnStartYaw, targetYaw);
        _turnTargetAdjustment = Mathf.Clamp(
            Mathf.DeltaAngle(_turnRequestedSignedAngle, targetAngle),
            -_turnMaxTargetAdjustment,
            _turnMaxTargetAdjustment);
    }

    /// <summary>
    /// Ends root-yaw scaling and leaves the transform where the clip put it;
    /// any residual to the target heading is closed by RotateToward on the
    /// following ticks. Safe to call when no turn is active.
    /// </summary>
    public void EndTurn()
    {
        isTurning = false;
        yawVelocity = 0f;
        _turnClipSignedAngle = 0f;
        _turnClipYaw = 0f;
        _turnAppliedYaw = 0f;
        _turnMaxTargetAdjustment = 0f;
        _turnTargetAdjustment = 0f;
    }

    public void RotateToward(Vector3 planarDirection)
    {
        RotateToward(planarDirection, 0f);
    }

    /// <summary>
    /// Turns toward a planar direction using an attack override when positive,
    /// or the boss movement configuration otherwise.
    /// </summary>
    public void RotateToward(Vector3 planarDirection, float degreesPerSecond)
    {
        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude <= 0.0001f) return;
        float targetYaw = Quaternion.LookRotation(planarDirection, Vector3.up).eulerAngles.y;
        float maximumSpeed = degreesPerSecond > 0f ? degreesPerSecond : config.rotationSpeed;
        float smoothedYaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref yawVelocity, config.rotationSmoothTime, maximumSpeed);
        Vector3 euler = transform.eulerAngles;
        euler.y = smoothedYaw;
        transform.eulerAngles = euler;
    }

    private float SelectPursuitSpeed(Vector3 targetPosition)
    {
        if (config.walkDistance <= 0f)
        {
            return config.speed;
        }

        Vector3 offset = targetPosition - transform.position;
        offset.y = 0f;
        float walkLimit = _isWalking ? config.walkDistance + config.runHysteresis : config.walkDistance;
        _isWalking = offset.magnitude <= walkLimit;
        return _isWalking ? config.walkSpeed : config.speed;
    }

    private void ApplyTranslation(Vector3 displacement)
    {
        if (_bodyConstraint == null)
        {
            return;
        }
        Vector3 applied = _bodyConstraint.Constrain(displacement);
        transform.position += applied;
        applied.y = 0f;
        appliedPlanarVelocity = Time.deltaTime > 0f ? applied / Time.deltaTime : Vector3.zero;
    }

    // Kept out of appliedPlanarVelocity so a push does not read as walking in the locomotion blend.
    private void ApplyShove()
    {
        if (_shoveProgress >= 1f || _bodyConstraint == null)
        {
            return;
        }

        _shoveElapsed += Time.deltaTime;
        float time = _shoveDuration > 0f ? Mathf.Clamp01(_shoveElapsed / _shoveDuration) : 1f;
        float progress = 1f - (1f - time) * (1f - time);
        transform.position += _bodyConstraint.Constrain(_shoveDisplacement * (progress - _shoveProgress));
        _shoveProgress = progress;
    }

    private bool CanUseAgent()
    {
        bool available = agent.enabled && agent.isOnNavMesh;
        if (!available && showDebugLogs) Debug.LogWarning("BossMotor cannot navigate because its NavMeshAgent is disabled or off the NavMesh.", this);
        return available;
    }
}
