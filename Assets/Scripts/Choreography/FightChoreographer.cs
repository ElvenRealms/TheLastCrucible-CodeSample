using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Plays a <see cref="FightScript"/> on the live player and boss, for capturing footage. While it
/// plays, the player's devices are ignored and the boss starts only the attacks the script queues.
/// F10 starts and stops it in the Editor and development builds; a release build ships no hotkey.
/// It stops by itself when the script ends or the encounter does.
/// </summary>
public class FightChoreographer : MonoBehaviour, IFightPerformer
{
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStateMachine player;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private BossStateMachine boss;

    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private FightScript script;
    [FoldoutGroup("Config"), SerializeField]
    [Tooltip("Input binding path that starts and stops the script. Live in the Editor and development builds only.")]
    private string toggleBinding = "<Keyboard>/f10";

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isPlaying;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private FightRunnerPhase phase;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int beatIndex = -1;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private string beatLabel;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private FightDirection heldDirection;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly, SuffixLabel("s")]
    [Tooltip("Game seconds the last boss wind-up took to reach its hit window. Time parries and dodges against it.")]
    private float lastWindUpLength;

    private FightScriptRunner _runner;
    private InputAction _toggle;
    private bool _heldSprint;
    private BossState _bossState;
    private BossAttackData _bossOpening;
    private BossAttackData _windUpStep;
    private float _windUpStartTime;
    private bool _playerWasInvulnerable;
    private bool _bossWasInvulnerable;
    private bool _startLockOnPending;

    public bool IsPlaying => isPlaying;
    public FightScriptRunner Runner => _runner;
    public float DistanceToBoss => PlanarToBoss.magnitude;

    private Vector3 PlanarToBoss
    {
        get
        {
            Vector3 toBoss = boss.transform.position - player.transform.position;
            toBoss.y = 0f;
            return toBoss;
        }
    }

    private void Awake()
    {
        _runner = new FightScriptRunner(this);
        _runner.Finished += Stop;
        _toggle = new InputAction("Fight Script Toggle", InputActionType.Button, toggleBinding);
    }

    private void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _toggle.Enable();
#endif
        arenaManager.PhaseChanged += HandlePhaseChanged;
        boss.AttackWindUpStarted += HandleWindUpStarted;
        boss.AttackWindUpEnded += HandleWindUpEnded;
        boss.StateChanged += HandleBossStateChanged;
        boss.Hitbox.HitResolved += HandleBossHitResolved;
        player.Hitbox.HitResolved += HandlePlayerHitResolved;
        player.StateChanged += HandlePlayerStateChanged;
    }

    private void OnDisable()
    {
        Stop();
        _toggle.Disable();
        if (arenaManager != null)
        {
            arenaManager.PhaseChanged -= HandlePhaseChanged;
        }
        if (boss != null)
        {
            boss.AttackWindUpStarted -= HandleWindUpStarted;
            boss.AttackWindUpEnded -= HandleWindUpEnded;
            boss.StateChanged -= HandleBossStateChanged;
            if (boss.Hitbox != null)
            {
                boss.Hitbox.HitResolved -= HandleBossHitResolved;
            }
        }
        if (player != null)
        {
            player.StateChanged -= HandlePlayerStateChanged;
            if (player.Hitbox != null)
            {
                player.Hitbox.HitResolved -= HandlePlayerHitResolved;
            }
        }
    }

    private void OnDestroy()
    {
        _toggle?.Dispose();
    }

    private void Update()
    {
        if (_toggle.WasPerformedThisFrame())
        {
            if (isPlaying)
            {
                Stop();
            }
            else
            {
                Play();
            }
        }

        if (!isPlaying)
        {
            return;
        }

        TryStartLockOn();
        _runner.Tick(Time.deltaTime);
        if (isPlaying)
        {
            SteerHeldDirection();
            RefreshDebugState();
        }
    }

    /// <summary>
    /// Starts the assigned script, activating the encounter first if needed. Refused while the
    /// encounter can't activate, such as after a win before its reset.
    /// </summary>
    public void Play()
    {
        Stop();
        if (script == null)
        {
            Debug.LogWarning("[FightChoreographer] No fight script is assigned.", this);
            return;
        }
        // An empty script finishes without raising Finished, so it would never hand control back.
        if (script.Beats.Count == 0)
        {
            Debug.LogWarning($"[FightChoreographer] Can't play '{script.name}': it has no beats.", this);
            return;
        }
        if (!arenaManager.IsActive && !arenaManager.TryActivate(player))
        {
            Debug.LogWarning($"[FightChoreographer] Can't play '{script.name}': the encounter couldn't start ({arenaManager.Phase}).", this);
            return;
        }

        isPlaying = true;
        player.InputReader.SetScriptedControl(true);
        boss.SetAttacksScripted(true);
        _playerWasInvulnerable = player.Health.GodModeEnabled;
        _bossWasInvulnerable = boss.Health.GodModeEnabled;
        player.Health.SetGodMode(_playerWasInvulnerable || script.PlayerInvulnerable);
        boss.Health.SetGodMode(_bossWasInvulnerable || script.BossInvulnerable);
        _bossState = boss.CurrentState;
        _windUpStep = null;
        if (script.StartLockedOn && !player.LockOn.IsLockedOn)
        {
            _startLockOnPending = true;
            TryStartLockOn();
        }

        _runner.Start(script);
        Log($"Playing '{script.name}' ({script.Beats.Count} beats).");
        RefreshDebugState();
    }

    /// <summary>Stops the script and hands the player and boss back to their usual control.</summary>
    public void Stop()
    {
        if (!isPlaying)
        {
            return;
        }

        isPlaying = false;
        _startLockOnPending = false;
        _runner.Stop();
        if (player != null)
        {
            HoldDirection(FightDirection.None, false);
            player.InputReader.SetScriptedControl(false);
            player.Health.SetGodMode(_playerWasInvulnerable);
        }
        if (boss != null)
        {
            boss.SetAttacksScripted(false);
            boss.Health.SetGodMode(_bossWasInvulnerable);
        }
        Log("Stopped.");
        RefreshDebugState();
    }

    public void HoldDirection(FightDirection direction, bool sprint)
    {
        heldDirection = direction;
        _heldSprint = sprint && direction != FightDirection.None;
        if (direction == FightDirection.None)
        {
            player.InputReader.InjectMove(Vector2.zero);
        }
        else
        {
            SteerHeldDirection();
        }
        // After the move, so sprint sees the movement it needs to start.
        player.InputReader.InjectSprint(_heldSprint);
    }

    public void Press(FightPlayerAction action)
    {
        ScriptedPress? press = action switch
        {
            FightPlayerAction.Attack => ScriptedPress.Attack,
            FightPlayerAction.HeavyAttack => ScriptedPress.HeavyAttack,
            FightPlayerAction.Dodge => ScriptedPress.Dodge,
            FightPlayerAction.Block => ScriptedPress.Block,
            FightPlayerAction.Heal => ScriptedPress.Heal,
            FightPlayerAction.LockOn => ScriptedPress.LockOn,
            _ => null
        };
        if (press.HasValue)
        {
            player.InputReader.InjectPress(press.Value);
        }
    }

    public bool QueueBossAttack(BossAttackData attack) => boss.QueueScriptedAttack(attack);

    public void CancelBossAttack()
    {
        boss.ClearScriptedAttack();
    }

    private void TryStartLockOn()
    {
        if (!_startLockOnPending || player.IsInputSuppressed || player.IsPauseSuppressed ||
            !boss.LockOnTarget.isActiveAndEnabled) return;
        if (!player.LockOn.IsLockedOn)
        {
            player.InputReader.InjectPress(ScriptedPress.LockOn);
        }
        _startLockOnPending = !player.LockOn.IsLockedOn;
    }

    // Recomputed every frame, so strafing circles the boss and approaches track it as both move.
    private void SteerHeldDirection()
    {
        if (heldDirection == FightDirection.None)
        {
            return;
        }

        Vector3 toBoss = PlanarToBoss;
        toBoss = toBoss.sqrMagnitude > 0.0001f ? toBoss.normalized : player.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, toBoss);
        Vector3 world = heldDirection switch
        {
            FightDirection.Toward => toBoss,
            FightDirection.Away => -toBoss,
            FightDirection.Left => -right,
            FightDirection.Right => right,
            _ => Vector3.zero
        };
        player.InputReader.InjectMove(player.ToMoveInput(world));
    }

    private void HandleWindUpStarted(BossAttackData step)
    {
        _windUpStep = step;
        _windUpStartTime = Time.time;
        _bossOpening = boss.Attack.ActiveAttack?.OpeningAttack;
        _runner.Notify(FightCue.BossWindUp, step);
    }

    // Also raised when something interrupts a wind-up; only a wind-up that reached its hit window swings.
    private void HandleWindUpEnded()
    {
        if (_windUpStep == null)
        {
            return;
        }

        if (boss.Animation.NormalizedTime >= _windUpStep.HitWindowStart)
        {
            lastWindUpLength = Time.time - _windUpStartTime;
            Log($"{_windUpStep.name} swings {lastWindUpLength:0.000} s after its wind-up started.");
            _runner.Notify(FightCue.BossSwing, _windUpStep);
        }
        _windUpStep = null;
    }

    private void HandleBossStateChanged(BossState state)
    {
        if (_bossState == boss.Attack && state != boss.Attack)
        {
            _runner.Notify(FightCue.BossAttackEnd, _bossOpening);
        }
        if (state == boss.Stagger)
        {
            _runner.Notify(FightCue.BossStagger, null);
        }
        _bossState = state;
    }

    private void HandleBossHitResolved(HitResult result, DamageInfo info)
    {
        if (isPlaying)
        {
            Log($"Boss hit on player: {result}.");
        }
    }

    private void HandlePlayerHitResolved(HitResult result, DamageInfo info)
    {
        if (isPlaying)
        {
            Log($"Player hit on boss: {result}.");
        }
    }

    // Shows whether a pressed action took: a refused press leaves no state change behind it.
    private void HandlePlayerStateChanged(PlayerState state)
    {
        if (isPlaying)
        {
            Log($"Player entered {state.GetType().Name}.");
        }
    }

    private void HandlePhaseChanged(EncounterPhase encounterPhase)
    {
        if (encounterPhase != EncounterPhase.Active)
        {
            Stop();
        }
    }

    private void RefreshDebugState()
    {
        bool started = _runner.Phase == FightRunnerPhase.Performing &&
            (phase != FightRunnerPhase.Performing || _runner.BeatIndex != beatIndex);
        phase = _runner.Phase;
        beatIndex = _runner.IsRunning ? _runner.BeatIndex : -1;
        beatLabel = _runner.CurrentBeat?.Label;
        if (started)
        {
            Log($"Beat {beatIndex} started: {beatLabel}");
        }
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[FightChoreographer] {message}", this);
        }
    }
}
