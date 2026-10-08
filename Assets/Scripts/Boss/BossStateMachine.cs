using Animancer;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Owns the authoritative boss combat states and routes encounter lifecycle
/// events into them. Focused components perform movement and presentation.
/// </summary>
[RequireComponent(typeof(Health), typeof(Poise), typeof(BossMotor))]
[RequireComponent(typeof(BossAnimation))]
public class BossStateMachine : MonoBehaviour, IBossDecisionSource
{
    [FormerlySerializedAs("arena")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [FoldoutGroup("Movement"), SerializeField, Required, AssetsOnly, InlineEditor]
    private BossMovementConfigData movementConfig;
    [FoldoutGroup("Animations"), SerializeField, Required, AssetsOnly, InlineEditor]
    private BossAnimationData animationData;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private LockOnTarget lockOnTarget;

    [FoldoutGroup("Decisions"), SerializeField, Required, AssetsOnly, InlineEditor]
    [Tooltip("Everything the boss can choose to do while it is free to act, each with its priority, weight, conditions, considerations and action, and how it paces its attacks.")]
    private BossOptionSet optionSet;

    [FoldoutGroup("Attacks"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Volley fired once during the enrage beat. Leave empty for an enrage without a burst.")]
    private BossProjectileData enrageProjectile;
    [FoldoutGroup("Attacks"), SerializeField, Range(0f, 1f), ShowIf(nameof(enrageProjectile))]
    [Tooltip("Normalized time in the enrage clip at which the burst fires.")]
    private float enrageBurstTime = 0.64f;
    [FoldoutGroup("Stagger"), SerializeField, MinValue(1f)]
    [Tooltip("Health damage scale while staggered. Poise damage and defence costs keep their original values.")]
    private float staggerDamageMultiplier = 1.5f;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private MeleeHitbox meleeHitbox;
    [FormerlySerializedAs("enrageBurst")]
    [FoldoutGroup("References"), SerializeField, Required, ChildGameObjectsOnly]
    [Tooltip("Fires the projectiles of attack steps and of the enrage beat.")]
    private BossProjectileLauncher projectileLauncher;
    [FoldoutGroup("References"), SerializeField, Required, ChildGameObjectsOnly]
    private Transform attackOrigin;

    [FoldoutGroup("Turns"), SerializeField, Required, AssetsOnly, InlineEditor]
    private BossTurnSet turnSet;

    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private Collider rootBodyCollider;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField]
    private bool showLineOfAttackRay = true;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private string _currentStateName;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private BossAttackDefinition _previousAttack;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, SuffixLabel("s")]
    private float _attackDelayRemaining;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool _attacksScripted;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private string ScriptedAttack => _scriptedOption != null ? _scriptedOption.Name : "None";
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int _chainedAttacks;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsEnraged { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsRetaliating => Reactions != null && Reactions.IsRetaliating;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, SuffixLabel("m/s")]
    public float TargetRetreatSpeed => Reactions != null ? Reactions.TargetRetreatSpeed : 0f;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public Transform Target { get; private set; }
    /// <summary>
    /// What the boss is doing, by the option it chose: the attack in progress, or the option's
    /// name. A state no option leads to (stagger, intro, dormant, dead) reads as that state, and
    /// the frame of pursuit before the first choice reads as "Deciding".
    /// </summary>
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public string CurrentOption
    {
        get
        {
            if (_currentState == null) return "None";
            if (_currentState == Attack && Attack.ActiveAttack != null) return $"Attack: {Attack.ActiveAttack.OpeningAttack.name}";
            bool chosenByBrain = _currentState == Pursue || _currentState == Turn || _currentState == Enrage;
            if (chosenByBrain && Brain.Chosen != null) return Brain.Chosen.Name;
            if (_currentState == Pursue) return "Deciding";
            return _currentStateName.Replace("Boss", string.Empty).Replace("State", string.Empty);
        }
    }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, TableList(IsReadOnly = true, AlwaysExpanded = true)]
    private List<BossOptionScore> OptionScores => Application.isPlaying && Brain != null ? ScoreOptions() : null;

    public BossMovementConfigData MovementConfig => movementConfig;
    public LinearMixerTransition LocomotionMixer => animationData.LocomotionMixer;
    /// <summary>How the boss paces its attacks in its current phase.</summary>
    public BossPacing Pacing => optionSet.GetPacing(IsEnraged);
    public ClipTransition StrafeLeftClip => animationData.StrafeLeftClip;
    public ClipTransition StrafeRightClip => animationData.StrafeRightClip;
    public ClipTransition IntroClip => animationData.IntroClip;
    public ClipTransition EnrageClip => animationData.EnrageClip;
    public BossProjectileData EnrageProjectile => enrageProjectile;
    public float EnrageBurstTime => enrageBurstTime;
    public BossProjectileLauncher Projectiles => projectileLauncher;
    public ClipTransition StaggerEnterClip => animationData.StaggerEnterClip;
    public ClipTransition StaggerLoopClip => animationData.StaggerLoopClip;
    public ClipTransition StaggerExitClip => animationData.StaggerExitClip;
    public float StaggerLoopDuration => animationData.StaggerLoopDuration;
    public float StaggerDamageMultiplier => staggerDamageMultiplier;
    public ClipTransition StaggerHitClip => animationData.StaggerHitClip;
    public ClipTransition DeathClip => animationData.DeathClip;
    public BossAnimation Animation { get; private set; }
    public BossMotor Motor { get; private set; }
    public MeleeHitbox Hitbox => meleeHitbox;
    public LockOnTarget LockOnTarget => lockOnTarget;
    public Collider RootBodyCollider => rootBodyCollider;
    public Health Health { get; private set; }
    public Poise Poise { get; private set; }
    public BossDormantState Dormant { get; private set; }
    public BossIntroState Intro { get; private set; }
    public BossPursueState Pursue { get; private set; }
    public BossAttackState Attack { get; private set; }
    public BossTurnState Turn { get; private set; }
    public BossStaggerState Stagger { get; private set; }
    public BossEnrageState Enrage { get; private set; }
    public BossReactions Reactions { get; private set; }
    public BossBrain Brain { get; private set; }
    /// <summary>Every attack option the boss has, in authored order, across both phases.</summary>
    public IReadOnlyList<BossAttackOption> AttackOptions => _attackOptions;
    public BossDeadState Dead { get; private set; }
    public BossState CurrentState => _currentState;
    public bool IsPauseSuppressed { get; private set; }
    public bool IsCombatPivotArmed => _combatPivotArmed;
    public BossAttackDefinition PreviousAttack => _previousAttack;
    public float AttackDelayRemaining => _attackDelayRemaining;
    public bool AreAttacksScripted => _attacksScripted;
    /// <summary>The attack option a fight script has queued, or null.</summary>
    public BossAttackOption ScriptedOption => _scriptedOption;
    public Vector3 AuthoredPosition => _authoredPosition;
    public Quaternion AuthoredRotation => _authoredRotation;
    public float ScoreCutoff => optionSet.ScoreCutoff;
    public bool IsTargetHealing => TargetPlayer != null && TargetPlayer.CurrentState == TargetPlayer.Heal;
    public bool IsTargetGuarding => TargetPlayer != null && TargetPlayer.Defense.IsGuarding;
    public bool IsTargetAttacking => TargetPlayer != null && TargetPlayer.CurrentState is AttackState;
    public bool IsTargetExhausted => TargetPlayer != null && TargetPlayer.Stamina.IsExhausted;

    // Null while the boss is targeting something other than the player, such as a test dummy.
    private PlayerStateMachine TargetPlayer
    {
        get
        {
            PlayerStateMachine player = arenaManager != null ? arenaManager.ActivePlayer : null;
            return player != null && Target == player.transform ? player : null;
        }
    }

    /// <summary>Raised after a new boss state has been entered.</summary>
    public event Action<BossState> StateChanged;
    /// <summary>Raised when an Attack begins its Wind-up; presentation reads the definition to decide what to show.</summary>
    public event Action<BossAttackData> AttackWindUpStarted;
    /// <summary>Raised when a Wind-up ends, whether its Hit Window opened or the Attack was interrupted first.</summary>
    public event Action AttackWindUpEnded;
    /// <summary>Raised once per attempt when the boss becomes enraged.</summary>
    public event Action Enraged;
    public event Action DeathStarted;
    public event Action DeathAnimationCompleted;

    private readonly List<BossAttackOption> _attackOptions = new();
    private BossAttackOption _scriptedOption;
    private BossState _currentState;
    private Collider _lastLineOfAttackBlocker;
    private bool _hasStarted;
    private bool _stateNeedsReentry;
    private bool _combatPivotArmed;
    private Vector3 _authoredPosition;
    private Quaternion _authoredRotation;
    private DamageInfo _pendingHit;
    private bool _hasPendingHit;
    private bool _hasPlayedIntro;

    private void Awake()
    {
        Motor = GetComponent<BossMotor>();
        Animation = GetComponent<BossAnimation>();
        Health = GetComponent<Health>();
        Poise = GetComponent<Poise>();
        _authoredPosition = transform.position;
        _authoredRotation = transform.rotation;
        _combatPivotArmed = true;
        Motor.Initialize(movementConfig);
        Animation.SetRun(animationData.Run);
        Dormant = new BossDormantState(this);
        Intro = new BossIntroState(this);
        Pursue = new BossPursueState(this);
        Attack = new BossAttackState(this);
        Turn = new BossTurnState(this);
        Stagger = new BossStaggerState(this);
        Enrage = new BossEnrageState(this);
        Reactions = new BossReactions(this);
        Reactions.Reset();
        Brain = new BossBrain(this, optionSet.CreateOptions(this));
        foreach (BossOption option in Brain.Options)
        {
            if (option is BossAttackOption attackOption) _attackOptions.Add(attackOption);
        }
        Dead = new BossDeadState(this);
    }

    private void OnEnable()
    {
        if (arenaManager != null)
        {
            arenaManager.RegisterBoss(this);
            arenaManager.EncounterActivated += HandleEncounterActivated;
            arenaManager.EncounterDeactivated += HandleEncounterDeactivated;
        }
        if (meleeHitbox != null) meleeHitbox.HitResolved += HandleHitResolved;
        if (projectileLauncher != null) projectileLauncher.HitResolved += HandleProjectileHitResolved;
        Health.Damaged += HandleDamaged;
        Health.Died += HandleDied;
        Poise.Broken += HandlePoiseBroken;
        if (_hasStarted)
        {
            if (_stateNeedsReentry)
            {
                _currentState = null;
                _currentStateName = null;
                _stateNeedsReentry = false;
            }
            ReconcileState();
        }
    }

    private void OnDisable()
    {
        if (arenaManager != null)
        {
            arenaManager.EncounterActivated -= HandleEncounterActivated;
            arenaManager.EncounterDeactivated -= HandleEncounterDeactivated;
            arenaManager.UnregisterBoss(this);
        }
        if (meleeHitbox != null) meleeHitbox.HitResolved -= HandleHitResolved;
        if (projectileLauncher != null) projectileLauncher.HitResolved -= HandleProjectileHitResolved;
        Health.Damaged -= HandleDamaged;
        Health.Died -= HandleDied;
        Poise.Broken -= HandlePoiseBroken;
        _hasPendingHit = false;

        if (_currentState != null)
        {
            _currentState.Exit();
            _stateNeedsReentry = true;
        }

        meleeHitbox?.EndWindow();
        Animation?.ClearCompletion();
        Motor?.Stop();
    }

    private void Start()
    {
        _hasStarted = true;
        ReconcileState();
    }

    private void Update()
    {
        if (IsPauseSuppressed) return;
        _attackDelayRemaining = Mathf.Max(0f, _attackDelayRemaining - Time.deltaTime);
        Brain.Tick(Time.deltaTime);
        ApplyPendingHitShove();
        if (Reactions.Tick(Time.deltaTime) && !_attacksScripted)
        {
            LogDebug("Target started healing; attack delay cut short.");
            _attackDelayRemaining = 0f;
        }
        _currentState?.Tick();
    }

    /// <summary>
    /// Suspends state advancement without interrupting the active state.
    /// </summary>
    public void SetPauseSuppressed(bool suppressed)
    {
        IsPauseSuppressed = suppressed;
        Motor?.SetPauseSuppressed(suppressed);
    }

    /// <summary>
    /// Marks the intro as already played, so the first activation goes straight
    /// to pursuit. For tests and editor automation that start a fight directly.
    /// </summary>
    public void SkipIntro()
    {
        _hasPlayedIntro = true;
    }

    /// <summary>
    /// Leaves the current state for <paramref name="next"/>. Refused for the
    /// current state, out of Dead, and into anything but Dead once health is
    /// depleted; <see cref="ResetForEncounter"/> is the only way out of Dead.
    /// </summary>
    public void ChangeState(BossState next)
    {
        if (next == null || _currentState is BossDeadState || _currentState == next) return;
        if (Health != null && Health.IsDead && next != Dead) return;
        ExitCurrentState();
        EnterState(next);
    }

    public Vector3 PlanarDirectionToTarget
    {
        get
        {
            if (Target == null) return Vector3.zero;
            Vector3 direction = Target.position - transform.position;
            direction.y = 0f;
            return direction;
        }
    }

    /// <summary>
    /// Ends the current action and hands the boss back to choosing: pursuit, where BossBrain
    /// decides each frame, while the encounter still has a live target, and Dormant or Dead
    /// otherwise. States call this instead of naming what follows them.
    /// </summary>
    public void FinishAction()
    {
        if (Health.IsDead)
        {
            HandleDied();
            return;
        }
        ChangeState(IsEncounterTargetValid() ? Pursue : Dormant);
    }

    /// <summary>
    /// Reports whether an attack could start from here once the boss has turned to its target
    /// and its wait is over, with nothing blocking the line to the target.
    /// </summary>
    public bool HasAttackOpportunity()
    {
        return Target != null && HasAttackOpportunity(ReadFight());
    }

    public bool HasAttackOpportunity(in BossDecisionContext context)
    {
        if (Target == null) return false;
        foreach (BossAttackOption option in _attackOptions)
        {
            if (_scriptedOption != null && option != _scriptedOption) continue;
            if (option.IsInReach(context)) return HasClearLineOfAttack();
        }
        return false;
    }

    /// <summary>
    /// Reads the fight for one decision, including what the boss can do from here: whether an
    /// attack is in reach, whether it holds its range, and whether it may strafe.
    /// </summary>
    public BossDecisionContext CreateDecisionContext()
    {
        BossDecisionContext context = ReadFight();
        context.attackInReach = HasAttackOpportunity(context);
        context.holdingRange = context.attackInReach && !ShouldCloseInWhileWaiting();
        context.ableToStrafe = CanStrafe();
        return context;
    }

    // Beyond the close-in distance a melee attack has to travel to land, so the
    // ground in between must be walkable for the target to count as reachable.
    private BossDecisionContext ReadFight()
    {
        Vector3 planarDirection = PlanarDirectionToTarget;
        float distance = planarDirection.magnitude;
        bool reachable = distance <= movementConfig.closeInDistance || Motor.HasWalkableLineTo(Target.position);
        BossDecisionContext context = Reactions.CreateContext(distance, Vector3.Angle(transform.forward, planarDirection), reachable);
        context.enraged = IsEnraged;
        context.attackDelayElapsed = _attacksScripted || _attackDelayRemaining <= 0f;
        context.attacksScripted = _attacksScripted;
        return context;
    }

    /// <summary>
    /// Whether pursuit keeps closing in, rather than holding position, while
    /// an attack in range cannot start yet.
    /// </summary>
    public bool ShouldCloseInWhileWaiting()
    {
        return movementConfig.closeInDistance > 0f && PlanarDirectionToTarget.magnitude > movementConfig.closeInDistance;
    }

    /// <summary>
    /// Whether the boss circles the target while it waits in attack range. A fight
    /// script keeps the boss on its marks, so scripted attacks hold position instead.
    /// </summary>
    public bool CanStrafe()
    {
        return !_attacksScripted && movementConfig.strafeSpeed > 0f && animationData.HasStrafeClips;
    }

    /// <summary>
    /// Commits the boss to an attack sequence. Range and facing are the business of the option
    /// that chose it; this refuses only what no attack may do: start mid-attack, without a
    /// target, or before the attack delay is over.
    /// </summary>
    public bool StartAttack(BossAttackDefinition attack)
    {
        if (attack == null || !attack.SequenceValid || Target == null || _currentState == Attack) return false;
        if (!_attacksScripted && _attackDelayRemaining > 0f) return false;
        Attack.Prepare(attack);
        ChangeState(Attack);
        if (_currentState != Attack) return false;

        _previousAttack = attack;
        if (_scriptedOption != null && _scriptedOption.Attack == attack) _scriptedOption = null;
        Reactions.NotifyAttackStarted();
        // Also covers a caller that starts an attack without going through BossBrain.
        BossAttackOption started = _attackOptions.Find(option => option.Attack == attack);
        if (started != null) Brain.NoteStarted(started);
        return true;
    }

    /// <summary>
    /// Hands attack choice to a fight script: the boss still pursues, turns and holds range, but
    /// starts only the attack queued with <see cref="QueueScriptedAttack"/>. That attack needs
    /// only its range and facing: the attack delay, cooldowns, start chances and its other
    /// conditions are ignored.
    /// </summary>
    public void SetAttacksScripted(bool scripted)
    {
        _attacksScripted = scripted;
        _scriptedOption = null;
    }

    /// <summary>
    /// Makes the first attack option of the current phase that opens with
    /// <paramref name="openingAttack"/> the next attack, whatever its weight. False when the
    /// phase has no option that opens with it.
    /// </summary>
    public bool QueueScriptedAttack(BossAttackData openingAttack)
    {
        _scriptedOption = null;
        if (!_attacksScripted || openingAttack == null) return false;
        var phase = new BossDecisionContext { enraged = IsEnraged };
        _scriptedOption = _attackOptions.Find(option => option.Attack.OpeningAttack == openingAttack && option.IsInPhase(phase));
        return _scriptedOption != null;
    }

    public void ClearScriptedAttack()
    {
        _scriptedOption = null;
    }

    /// <summary>
    /// Starts one delay after leaving an attack sequence, scaled by the attack
    /// that just ended. It continues through pursuit, turns, and stagger, while
    /// pausing with state advancement. A chained attack skips the delay.
    /// </summary>
    public void StartAttackDelay(BossAttackDefinition finishedAttack)
    {
        BossPacing pacing = Pacing;
        bool chains = _chainedAttacks < pacing.MaximumChainedAttacks && UnityEngine.Random.value < pacing.ChainChance;
        _chainedAttacks = chains ? _chainedAttacks + 1 : 0;
        _attackDelayRemaining = chains ? 0f : pacing.SampleAttackDelay() * finishedAttack.DelayScale;
    }

    /// <summary>
    /// Switches the boss to its enraged phase, once per attempt. When that happens is authored
    /// on the Enrage option. True when the enrage beat took over the state; without an enrage
    /// clip the switch happens inside pursuit.
    /// </summary>
    public bool TryStartEnrage()
    {
        if (IsEnraged) return false;

        IsEnraged = true;
        _attackDelayRemaining = 0f;
        _chainedAttacks = 0;
        Reactions.Reset();
        LogDebug("Enraged.");
        Enraged?.Invoke();
        if (!animationData.HasEnrageClip) return false;

        ChangeState(Enrage);
        return _currentState == Enrage;
    }

    /// <summary>
    /// Rearms combat pivots after the boss returns within the rearm angle.
    /// </summary>
    public void UpdateCombatPivotArming()
    {
        if (_combatPivotArmed || Target == null) return;
        if (Motor.IsFacing(PlanarDirectionToTarget, movementConfig.combatPivotRearmAngle)) _combatPivotArmed = true;
    }

    /// <summary>
    /// Starts a large combat pivot toward the target when it is beyond the
    /// combat pivot start angle; smaller corrections stay procedural.
    /// </summary>
    public bool TryStartCombatTurn()
    {
        if (Target == null) return false;

        Vector3 planarDirection = PlanarDirectionToTarget;
        float facingAngle = Vector3.Angle(transform.forward, planarDirection);
        UpdateCombatPivotArming();
        if (!_combatPivotArmed) return false;

        if (facingAngle < movementConfig.combatPivotStartAngle) return false;
        if (!TryStartTurn(planarDirection, movementConfig.combatPivotStartAngle, true)) return false;

        _combatPivotArmed = false;
        return true;
    }

    /// <summary>
    /// Starts an authored in-place turn toward an arbitrary planar direction
    /// when it is beyond the turn set's minimum angle; smaller corrections
    /// stay procedural.
    /// </summary>
    public bool TryStartTurn(Vector3 planarDirection)
    {
        return TryStartTurn(planarDirection, turnSet != null ? turnSet.minimumTurnAngle : 0f, false);
    }

    private bool HasClearLineOfAttack()
    {
        Vector3 origin = attackOrigin.position;
        Vector3 toTarget = Target.position - origin;
        if (toTarget.sqrMagnitude <= 0.0001f) return true;
        // Stop just short of the target so the ground directly beneath a
        // root-level target does not falsely block an otherwise clear path.
        float obstacleCheckDistance = Mathf.Max(0f, toTarget.magnitude - 0.05f);
        Vector3 direction = toTarget.normalized;
        bool blocked = Physics.Raycast(origin, direction, out RaycastHit hit, obstacleCheckDistance, movementConfig.lineOfAttackObstacles, QueryTriggerInteraction.Ignore);
        if (showLineOfAttackRay) Debug.DrawRay(origin, direction * obstacleCheckDistance, blocked ? Color.red : Color.green);

        if (blocked)
        {
            if (_lastLineOfAttackBlocker != hit.collider)
            {
                LogDebug($"Line of Attack blocked by '{hit.collider.name}' on layer '{LayerMask.LayerToName(hit.collider.gameObject.layer)}' at {hit.distance:F2}m.");
                _lastLineOfAttackBlocker = hit.collider;
            }
            return false;
        }

        if (_lastLineOfAttackBlocker != null) LogDebug("Line of Attack is clear again.");
        _lastLineOfAttackBlocker = null;
        return true;
    }

    /// <summary>
    /// What every option scores right now. Reads the fight afresh, which casts against the
    /// level, so it is for the Inspector and Play Mode checks rather than for deciding.
    /// </summary>
    public List<BossOptionScore> ScoreOptions()
    {
        return Target != null ? Brain.ScoreOptions() : null;
    }

    public void LogDebug(string message)
    {
        if (showDebugLogs) Debug.Log($"[BossStateMachine] {message}", this);
    }

    /// <summary>
    /// Opens or closes the boss to damage and lock-on. The damage block is
    /// keyed by this machine so other owners' blocks are left alone.
    /// </summary>
    public void SetCombatAvailable(bool available)
    {
        SetDamageable(available);
        if (lockOnTarget != null) lockOnTarget.enabled = available;
    }

    /// <summary>
    /// Opens or closes the boss to damage alone, so a lock-on taken beforehand survives.
    /// </summary>
    public void SetDamageable(bool damageable)
    {
        if (Health != null) Health.SetDamageBlocked(this, !damageable);
    }

    public void ClearTarget()
    {
        Target = null;
        _combatPivotArmed = true;
        _attackDelayRemaining = 0f;
        _chainedAttacks = 0;
        Reactions.Reset();
        Brain.ForgetHistory();
    }

    /// <summary>
    /// Returns the boss, alive or dead, to its authored pose in Dormant with
    /// full health and poise, its body collider restored, and no combat memory
    /// (target, previous attack, attack delay, combat pivot, line-of-attack
    /// blocker). The arena calls this once a dead player respawns and for the retry
    /// after a win; it is the only way out of Dead. Refused when the authored
    /// pose cannot be rebound to the NavMesh.
    /// </summary>
    public bool ResetForEncounter()
    {
        if (Motor == null || !Motor.ResetNavigation(_authoredPosition, _authoredRotation))
        {
            LogDebug("Encounter reset rejected because the authored pose could not be restored on the NavMesh.");
            return false;
        }

        // Exit first: an interrupted attack starts its attack delay on exit,
        // and that memory must be cleared along with the rest.
        ExitCurrentState();
        Animation?.ClearCompletion();
        meleeHitbox?.EndWindow();
        if (projectileLauncher != null) projectileLauncher.StopAll();
        IsEnraged = false;
        ClearTarget();
        _previousAttack = null;
        _scriptedOption = null;
        _lastLineOfAttackBlocker = null;
        _hasPendingHit = false;
        if (rootBodyCollider != null) rootBodyCollider.enabled = true;
        Health.RestoreFull();
        EnterState(Dormant);
        return true;
    }

    public void NotifyAttackWindUpStarted(BossAttackData attack)
    {
        AttackWindUpStarted?.Invoke(attack);
    }

    public void NotifyAttackWindUpEnded()
    {
        AttackWindUpEnded?.Invoke();
    }

    public void NotifyDeathAnimationCompleted()
    {
        if (_currentState is BossDeadState) DeathAnimationCompleted?.Invoke();
    }

    // A state already exited by OnDisable is not exited twice.
    private void ExitCurrentState()
    {
        if (!_stateNeedsReentry) _currentState?.Exit();
        _stateNeedsReentry = false;
    }

    private void EnterState(BossState next)
    {
        string previousStateName = _currentState != null ? _currentState.GetType().Name : "None";
        _currentState = next;
        _currentStateName = next.GetType().Name;
        LogDebug($"State changed: {previousStateName} -> {_currentStateName}.");
        _currentState.Enter();
        StateChanged?.Invoke(next);
    }

    private bool TryStartTurn(Vector3 planarDirection, float minimumTurnAngle, bool isCombatPivot)
    {
        if (turnSet == null) return false;
        if (planarDirection.sqrMagnitude <= 0.0001f) return false;
        float signedAngle = Vector3.SignedAngle(transform.forward, planarDirection, Vector3.up);
        if (Mathf.Abs(signedAngle) < minimumTurnAngle) return false;

        BossTurnDefinition turn = turnSet.Select(signedAngle);
        if (turn == null) return false;

        // The selected clip's authored sign can differ from the request (only
        // the 180 clip, which always turns left). Re-sign the request the
        // long way round so the transform turns the way the clip animates.
        float requestedSignedAngle = signedAngle;
        if (Mathf.Sign(requestedSignedAngle) != Mathf.Sign(turn.signedAngle)) requestedSignedAngle -= 360f * Mathf.Sign(requestedSignedAngle);

        Turn.Prepare(turn, requestedSignedAngle, isCombatPivot);
        ChangeState(Turn);
        return _currentState == Turn;
    }

    private void HandleEncounterActivated(Transform target)
    {
        if (target == null || Health.IsDead) return;
        if (Target != target) _combatPivotArmed = true;
        Target = target;
        // The intro belongs to the first attempt only; retries go straight to pursuit.
        bool playsIntro = !_hasPlayedIntro && animationData.HasIntroClip;
        _hasPlayedIntro = true;
        ChangeState(playsIntro ? Intro : Pursue);
    }

    // Stands down where it is; the arena resets the pose once the player respawns.
    private void HandleEncounterDeactivated()
    {
        ChangeState(Dormant);
    }

    // Only this boss's own blade reports here, so a Parry of any other source
    // can never drain its Poise.
    private void HandleHitResolved(HitResult result, DamageInfo info)
    {
        if (result != HitResult.Parried || _currentState != Attack) return;

        TakeParryPoiseDamage(Attack.ParryPoiseDamage);
    }

    private void HandleProjectileHitResolved(BossProjectileData data, HitResult result, DamageInfo info)
    {
        if (result != HitResult.Parried) return;

        TakeParryPoiseDamage(data.ParryPoiseDamage);
    }

    // A parry builds toward a Poise Break instead of forcing one, so the boss
    // carries on with its attack unless this drains the last of its Poise.
    private void TakeParryPoiseDamage(float amount)
    {
        if (!IsEncounterTargetValid()) return;

        Poise.TakeDamage(amount, Target);
    }

    // The shove waits for the next Update so the state it is judged against already reflects
    // a Poise Break or death caused by the same hit, whichever order the listeners ran in.
    private void HandleDamaged(DamageInfo info)
    {
        if (_currentState == Stagger && !Health.IsDead)
        {
            Stagger.NotifyHit();
        }
        _pendingHit = info;
        _hasPendingHit = true;
        CountHitTowardRetaliation();
    }

    // Only hits the boss takes while it is free to act provoke it: a hit during
    // its own attack or its stagger is an opening the player earned.
    private void CountHitTowardRetaliation()
    {
        bool freeToAct = _currentState == Pursue || _currentState == Turn || _currentState == Enrage;
        if (!freeToAct || _attacksScripted || !Reactions.RegisterHit()) return;

        LogDebug("Retaliating; attack delay cut short.");
        _attackDelayRemaining = 0f;
    }

    // A committed attack keeps its authored travel, so a hit only moves the boss outside of one.
    private void ApplyPendingHitShove()
    {
        if (!_hasPendingHit) return;
        _hasPendingHit = false;
        if (_currentState == Attack || _currentState is BossDeadState) return;

        Vector3 shove = BossHitShove.Resolve(_pendingHit, transform.position, movementConfig);
        if (shove == Vector3.zero) return;
        Motor.Shove(shove, movementConfig.hitShoveDuration);
        LogDebug($"Hit shove of {shove.magnitude:F2}m from {_pendingHit.poiseDamage:F0} Poise Damage.");
    }

    private void HandleDied()
    {
        if (_currentState is BossDeadState) return;
        ChangeState(Dead);
        arenaManager?.NotifyBossDied(this);
        DeathStarted?.Invoke();
    }

    // ChangeState already refuses same-state and dead transitions, but the
    // exclusion is kept explicit here to document the Poise Break contract.
    private void HandlePoiseBroken()
    {
        if (Health.IsDead || _currentState == Stagger || _currentState is BossDeadState) return;
        ChangeState(Stagger);
    }

    private void ReconcileState()
    {
        if (Health.IsDead)
        {
            HandleDied();
            return;
        }

        if (arenaManager != null && arenaManager.IsActive && arenaManager.ActivePlayer != null)
        {
            HandleEncounterActivated(arenaManager.ActivePlayer.transform);
            return;
        }

        ChangeState(Dormant);
    }

    public bool IsEncounterTargetValid()
    {
        if (arenaManager == null || !arenaManager.IsActive || arenaManager.ActivePlayer == null || Target == null)
            return false;

        PlayerStateMachine activePlayer = arenaManager.ActivePlayer;
        return activePlayer.isActiveAndEnabled && activePlayer.Health != null && !activePlayer.Health.IsDead &&
            Target == activePlayer.transform;
    }
}
