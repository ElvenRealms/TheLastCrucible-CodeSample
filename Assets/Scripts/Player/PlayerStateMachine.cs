using System;
using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Owns the player states and routes input and animation callbacks to the
/// active one. States call into PlayerMotor for actual movement.
/// </summary>
[RequireComponent(typeof(PlayerMotor), typeof(LockOnController), typeof(Health))]
[RequireComponent(typeof(PlayerDefense), typeof(PlayerStamina), typeof(PlayerHealing))]
[RequireComponent(typeof(Poise))]
public class PlayerStateMachine : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private InputReader inputReader;

    [FoldoutGroup("Config"), SerializeField, Required, InlineEditor]
    private MovementConfigData movementConfig;

    [FoldoutGroup("Config"), SerializeField, Required, InlineEditor]
    private PlayerAnimationData animationData;

    [FoldoutGroup("Config"), SerializeField, Required, SceneObjectsOnly]
    private AnimancerComponent animancer;

    [FoldoutGroup("Config"), SerializeField, Required, SceneObjectsOnly]
    private MeleeHitbox meleeHitbox;

    [FoldoutGroup("Config"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;

    [FoldoutGroup("Config"), SerializeField, Required, SceneObjectsOnly]
    private RespawnPoint respawnPoint;

    [FoldoutGroup("Config"), SerializeField, SceneObjectsOnly, Tooltip("Optional; falls back to Camera.main.")]
    private Camera cameraOverride;

    [FoldoutGroup("Config"), SerializeField, SceneObjectsOnly]
    [Tooltip("Optional; its view direction drives camera-relative movement and lock-on choice instead of the camera's. Assign the gameplay CinemachineCamera so a cinematic camera never steers the player.")]
    private Transform movementReference;

    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private string currentStateName;

    public InputReader InputReader => inputReader;
    public MovementConfigData MovementConfig => movementConfig;
    public PlayerAnimationData AnimationData => animationData;
    public AnimancerComponent Animancer => animancer;
    public MeleeHitbox Hitbox => meleeHitbox;
    public PlayerMotor Motor { get; private set; }
    public LockOnController LockOn { get; private set; }
    public Health Health { get; private set; }
    public Poise Poise { get; private set; }
    public PlayerDefense Defense { get; private set; }
    public PlayerStamina Stamina { get; private set; }
    public PlayerHealing Healing { get; private set; }
    public LocomotionState Locomotion { get; private set; }
    public DodgeState Dodge { get; private set; }
    public AttackState Attack { get; private set; }
    public AttackState HeavyAttack { get; private set; }
    public AttackState RunningAttack { get; private set; }
    public BlockState Block { get; private set; }
    public HealState Heal { get; private set; }
    public FlinchState Flinch { get; private set; }
    public DeadState Dead { get; private set; }
    public PlayerState CurrentState => currentState;
    public bool IsInputSuppressed { get; private set; }
    public bool IsPauseSuppressed { get; private set; }
    public bool HasSprintIntent => _hasSprintIntent;
    public PlayerInputBuffer InputBuffer { get; } = new PlayerInputBuffer();

    /// <summary>Raised after a state has been entered; presentation observes it to follow player actions.</summary>
    public event Action<PlayerState> StateChanged;

    /// <summary>Raised after a completed respawn has restored the player and returned them to Locomotion.</summary>
    public event Action Respawned;

    private PlayerState currentState;
    private Transform cameraTransform;
    private bool hasReportedDeath;
    private bool hasStarted;
    private bool _hasSprintIntent;
    private bool _hasSprintMovement;
    private bool isTransitioning;

    private void Awake()
    {
        Motor = GetComponent<PlayerMotor>();
        LockOn = GetComponent<LockOnController>();
        Health = GetComponent<Health>();
        Poise = GetComponent<Poise>();
        Defense = GetComponent<PlayerDefense>();
        if (Defense == null) Defense = gameObject.AddComponent<PlayerDefense>();
        Stamina = GetComponent<PlayerStamina>();
        Healing = GetComponent<PlayerHealing>();
        Camera cam = cameraOverride != null ? cameraOverride : Camera.main;
        if (cam == null)
        {
            Debug.LogError("PlayerStateMachine has no camera: assign Camera Override or tag a camera as MainCamera.",
                this);
            enabled = false;
            return;
        }

        cameraTransform = movementReference != null ? movementReference : cam.transform;
        Motor.Initialize(movementConfig);
        LockOn.Initialize(inputReader, cameraTransform);
        Locomotion = new LocomotionState(this);
        Dodge = new DodgeState(this);
        Attack = new AttackState(this, PlayerAction.Attack);
        HeavyAttack = new AttackState(this, PlayerAction.HeavyAttack);
        RunningAttack = new AttackState(this, PlayerAction.RunningAttack);
        Block = new BlockState(this);
        Heal = new HealState(this);
        Flinch = new FlinchState(this);
        Dead = new DeadState(this);
    }

    private void OnEnable()
    {
        if (IsInputSuppressed) inputReader.SuppressPlayerActions();
        else inputReader.EnablePlayerActions();
        inputReader.Move += HandleMove;
        inputReader.Dodge += HandleDodge;
        inputReader.Attack += HandleAttack;
        inputReader.HeavyAttack += HandleHeavyAttack;
        inputReader.BlockPressed += HandleBlock;
        inputReader.Heal += HandleHeal;
        inputReader.SprintPressed += HandleSprintPressed;
        inputReader.PlayerActionsDisabled += ClearSprintIntent;
        Health.Died += HandleDied;
        Poise.Broken += HandlePoiseBroken;
        Stamina.ExhaustionChanged += HandleExhaustionChanged;
        if (hasStarted && Health.IsDead) HandleDied();
        else if (hasStarted && currentState == null) ForceChangeState(Locomotion);
    }

    private void OnDisable()
    {
        ClearSprintIntent();
        inputReader.Move -= HandleMove;
        inputReader.Dodge -= HandleDodge;
        inputReader.Attack -= HandleAttack;
        inputReader.HeavyAttack -= HandleHeavyAttack;
        inputReader.BlockPressed -= HandleBlock;
        inputReader.Heal -= HandleHeal;
        inputReader.SprintPressed -= HandleSprintPressed;
        inputReader.PlayerActionsDisabled -= ClearSprintIntent;
        Health.Died -= HandleDied;
        Poise.Broken -= HandlePoiseBroken;
        Stamina.ExhaustionChanged -= HandleExhaustionChanged;

        if (currentState == null) return;

        currentState.Exit();
        currentState = null;
        currentStateName = null;
    }

    // Guard against lethal damage during another component's startup: if Died
    // already put us in Dead, don't re-enter it; if it fired before OnEnable
    // subscribed, enter Dead instead of Locomotion.
    private void Start()
    {
#if UNITY_EDITOR
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
#endif

        hasStarted = true;
        if (Health.IsDead)
        {
            HandleDied();
            return;
        }

        if (currentState == null) ForceChangeState(Locomotion);
    }

    private void Update()
    {
        if (IsPauseSuppressed) return;

        Defense.Tick(Time.deltaTime);
        currentState.Tick();
        if (currentState != Dead) Stamina.Tick(Time.deltaTime);
        DrainInputBuffer();
    }

    /// <summary>
    /// Player-requested action. Refused if the source's timing window is
    /// closed and the incoming action does not outrank it, or the
    /// destination refuses its own prerequisites. A started action
    /// supersedes any stale intent.
    /// </summary>
    public bool TryStartAction(PlayerState target)
    {
        if (!CanRequestTransition(target)) return false;
        if (!CanInterruptCurrent(target)) return false;
        if (!target.CanEnter()) return false;
        InputBuffer.Clear();
        return CommitStateChange(target);
    }

    /// <summary>
    /// Input entry point used by states for cross-state actions: start now, or
    /// hold the press if the source is committed but inside its buffer window.
    /// Destination refusal never buffers, so rejection feedback fires once.
    /// </summary>
    public bool RequestAction(PlayerAction action)
    {
        PlayerState target = StateFor(action);
        if (!CanInterruptCurrent(target))
        {
            if (currentState.CanBufferInput) InputBuffer.Set(action);
            return false;
        }

        return TryStartAction(target);
    }

    /// <summary>
    /// State-owned completion. The source is finishing voluntarily, so its
    /// gate is moot: a buffered action that can enter wins, otherwise Locomotion.
    /// </summary>
    public bool CompleteAction()
    {
        if (!CanRequestTransition(Locomotion)) return false;
        if (InputBuffer.TryTake(out PlayerAction action))
        {
            PlayerState target = StateFor(action);
            if (target.CanEnter()) return CommitStateChange(target);
        }

        return CommitStateChange(Locomotion);
    }

    /// <summary>
    /// Exceptional lifecycle authority: startup, death, respawn. Bypasses
    /// both gates but still runs the source's Exit. Clears the buffer: a
    /// forced change invalidates pending intent.
    /// </summary>
    public void ForceChangeState(PlayerState next)
    {
        if (!CanRequestTransition(next)) return;
        InputBuffer.Clear();
        CommitStateChange(next);
    }

    // Called by RootMotionRelay; the active state decides whether to consume it.
    public void OnRootMotion(Vector3 deltaPosition)
    {
        if (IsPauseSuppressed) return;
        currentState?.OnRootMotion(deltaPosition);
    }

    /// <summary>
    /// Suspends state advancement without clearing combat state or gameplay
    /// input ownership. PauseMenuController is the sole caller.
    /// </summary>
    public void SetPauseSuppressed(bool suppressed)
    {
        IsPauseSuppressed = suppressed;
        if (!suppressed && !inputReader.SprintHeld) ClearSprintIntent();
    }

    public void Respawn()
    {
        if (currentState != Dead || !Health.IsDead) return;
        if (respawnPoint == null)
        {
            Debug.LogError("PlayerStateMachine cannot respawn without a Respawn Point.", this);
            return;
        }

        ClearSprintIntent();
        meleeHitbox.EndWindow();
        LockOn.Release();
        Motor.Teleport(respawnPoint.Position, respawnPoint.Rotation);
        Health.RestoreFull();
        Poise.RestoreFull();
        Defense.ResetDefense();
        Stamina.RestoreFull();
        Healing.RestoreCharges();
        hasReportedDeath = false;
        ForceChangeState(Locomotion);
        Respawned?.Invoke();
    }

    /// <summary>
    /// Refills a living player's health, poise, stamina and healing charges where they stand,
    /// so the attempt after a win starts fresh.
    /// </summary>
    public void RestoreResources()
    {
        if (Health.IsDead) return;

        Health.RestoreFull();
        Poise.RestoreFull();
        Stamina.RestoreFull();
        Healing.RestoreCharges();
    }

    /// <summary>
    /// Stops new gameplay input while leaving the active state and animation
    /// free to finish naturally (notably the lethal attack recovery).
    /// </summary>
    public void SuppressInput()
    {
        IsInputSuppressed = true;
        ClearSprintIntent();
        inputReader?.SuppressPlayerActions();
        Attack?.ClearBufferedInput();
        Attack?.SuppressHitWindow();
        HeavyAttack?.SuppressHitWindow();
        RunningAttack?.SuppressHitWindow();
        meleeHitbox?.EndWindow();
    }

    public void RestoreInput()
    {
        IsInputSuppressed = false;
        inputReader?.RestorePlayerActions();
    }

    public void ClearSprintIntent()
    {
        _hasSprintIntent = false;
        _hasSprintMovement = false;
        Stamina?.NotifySprintReleased();
    }

    // Camera-relative: input mapped onto the camera's forward/right flattened to XZ.
    public Vector3 GetCameraRelativeInput()
    {
        Vector2 input = inputReader.MoveInput;
        if (input == Vector2.zero) return Vector3.zero;

        Vector3 forward = Flatten(cameraTransform.forward);
        Vector3 right = Flatten(cameraTransform.right);
        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    /// <summary>
    /// The move input that steers toward a world direction: the inverse of
    /// <see cref="GetCameraRelativeInput"/>, so scripted movement goes where it means to.
    /// </summary>
    public Vector2 ToMoveInput(Vector3 worldDirection)
    {
        Vector3 forward = Flatten(cameraTransform.forward);
        Vector3 right = Flatten(cameraTransform.right);
        return Vector2.ClampMagnitude(
            new Vector2(Vector3.Dot(worldDirection, right), Vector3.Dot(worldDirection, forward)), 1f);
    }

    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.normalized;
    }

    private void HandleDodge()
    {
        currentState?.OnInput(PlayerAction.Dodge);
    }

    private void HandleMove(Vector2 moveInput)
    {
        if (!_hasSprintIntent)
        {
            return;
        }
        if (moveInput != Vector2.zero)
        {
            _hasSprintMovement = true;
            return;
        }

        if (_hasSprintMovement)
        {
            ClearSprintIntent();
        }
    }

    private void HandleSprintPressed()
    {
        if (currentState != Locomotion || !Stamina.CanSprint)
        {
            return;
        }

        if (_hasSprintIntent)
        {
            ClearSprintIntent();
            return;
        }

        _hasSprintIntent = true;
        _hasSprintMovement = inputReader.MoveInput != Vector2.zero;
    }

    private void HandleExhaustionChanged(bool isExhausted)
    {
        if (isExhausted)
        {
            ClearSprintIntent();
        }
    }

    private void HandleAttack()
    {
        currentState?.OnInput(PlayerAction.Attack);
    }

    private void HandleHeavyAttack()
    {
        currentState?.OnInput(PlayerAction.HeavyAttack);
    }

    private void HandleBlock()
    {
        currentState?.OnInput(PlayerAction.Block);
    }

    private void HandleHeal()
    {
        currentState?.OnInput(PlayerAction.Heal);
    }

    private void HandleDied()
    {
        if (!hasReportedDeath)
        {
            hasReportedDeath = true;
            arenaManager?.NotifyPlayerDied(this);
        }

        if (currentState != Dead) ForceChangeState(Dead);
    }

    // TryStartAction, not ForceChangeState: Flinch's High priority already
    // interrupts any committed action through the normal gate, and Dead is
    // also High so a lethal hit never gets displaced by its own Poise Break.
    private void HandlePoiseBroken()
    {
        if (currentState == null || currentState == Dead || currentState == Flinch) return;
        TryStartAction(Flinch);
    }

    // The source lets go when the incoming action outranks it, or when its own
    // timing window is open. Equal and lower priority defer to the window.
    private bool CanInterruptCurrent(PlayerState target)
    {
        return target.Priority > currentState.Priority || currentState.CanExit();
    }

    private PlayerState StateFor(PlayerAction action)
    {
        switch (action)
        {
            case PlayerAction.Attack: return Attack;
            case PlayerAction.HeavyAttack: return HeavyAttack;
            case PlayerAction.RunningAttack: return RunningAttack;
            case PlayerAction.Dodge: return Dodge;
            case PlayerAction.Block: return Block;
            case PlayerAction.Heal: return Heal;
            default: throw new System.ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    // Fires a held cross-state press on the first frame the source lets go.
    // Attack self-chains remain state-owned because their combo window may
    // open before their cancel window.
    private void DrainInputBuffer()
    {
        if (!InputBuffer.HasPending) return;
        PlayerState target = StateFor(InputBuffer.Pending.Value);
        if (target == currentState && target is AttackState)
        {
            return;
        }

        if (!CanInterruptCurrent(target))
        {
            return;
        }
        InputBuffer.TryTake(out _);
        TryStartAction(target);
    }

    private bool CanRequestTransition(PlayerState next)
    {
        if (next == null) return false;
        if (!isTransitioning) return true;
        Debug.LogError($"Re-entrant transition to {next.GetType().Name} refused.", this);
        return false;
    }

    private bool CommitStateChange(PlayerState next)
    {
        isTransitioning = true;
        try
        {
            if (next != Locomotion)
            {
                ClearSprintIntent();
            }
            currentState?.Exit();
            currentState = next;
            currentStateName = next.GetType().Name;
            next.Enter();
            StateChanged?.Invoke(next);
            return true;
        }
        finally
        {
            isTransitioning = false;
        }
    }
}
