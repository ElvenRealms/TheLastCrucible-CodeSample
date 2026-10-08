using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Single owner of the scene-local encounter lifecycle: activation, player
/// death, victory, and the resets that return the boss to its authored pose.
/// Activation zones and participants report through public methods; the boss
/// registers itself on enable, so the arena needs no reference to it.
/// </summary>
public class ArenaManager : MonoBehaviour
{
    [Tooltip("Real seconds between the boss finishing its death animation and the encounter resetting for another attempt. Covers the Victory Banner hold and fade-out. Time spent paused does not count.")]
    [FoldoutGroup("Retry"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float retryDelay = 3.25f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private EncounterPhase _phase;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private PlayerStateMachine _activePlayer;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private BossStateMachine _boss;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private PlayerStateMachine _respawningPlayer;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool _isRetryPending;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, SuffixLabel("s")]
    private float _retryTimeRemaining;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private PlayerStateMachine _victor;

    public EncounterPhase Phase => _phase;
    public bool IsActive => _phase == EncounterPhase.Active;
    public bool IsBossDead => _phase == EncounterPhase.Won;
    public PlayerStateMachine ActivePlayer => _activePlayer;
    public BossStateMachine Boss => _boss;
    /// <summary>True from a player death until a boss reset succeeds after it.</summary>
    public bool IsResetPending => _respawningPlayer != null;

    /// <summary>Raised after every phase change, once the matching lifecycle event has been published.</summary>
    public event Action<EncounterPhase> PhaseChanged;
    /// <summary>Raised when a player starts an attempt; carries the player's transform as the boss target.</summary>
    public event Action<Transform> EncounterActivated;
    /// <summary>Raised when the active player's death ends an attempt; the boss goes Dormant where it stands until the reset.</summary>
    public event Action EncounterDeactivated;
    /// <summary>Raised when the registered boss dies, whether or not an attempt was active.</summary>
    public event Action<BossStateMachine> EncounterWon;
    /// <summary>Raised once the boss has been reset to its authored pose: after the retry that follows a win, and after the player respawns from a death.</summary>
    public event Action EncounterReset;

    // Pause and hit-stop hold time at zero, so the retry waits with them; slow motion still counts in real time.
    private void Update()
    {
        if (!_isRetryPending || Time.timeScale <= 0f)
        {
            return;
        }

        _retryTimeRemaining = Mathf.Max(0f, _retryTimeRemaining - Time.unscaledDeltaTime);
        if (_retryTimeRemaining > 0f)
        {
            return;
        }

        _isRetryPending = false;
        if (!ResetWonEncounter())
        {
            Debug.LogWarning("[ArenaManager] Retry skipped: no registered boss accepted the reset. The encounter stays Won until ResetAfterBossDeath succeeds.", this);
        }
    }

    private void OnDestroy()
    {
        if (_boss != null)
        {
            _boss.DeathAnimationCompleted -= HandleBossDeathAnimationCompleted;
        }
        CancelPendingReset();
        LiftPlayerProtection();
    }

    /// <summary>
    /// Adopts the encounter's boss. The boss calls this when it is enabled; an
    /// arena holds one boss, so a second registration is refused.
    /// </summary>
    public void RegisterBoss(BossStateMachine boss)
    {
        if (boss == null || boss == _boss)
        {
            return;
        }
        if (_boss != null)
        {
            Debug.LogWarning($"[ArenaManager] Refused to register '{boss.name}': '{_boss.name}' is already this arena's boss.", this);
            return;
        }

        _boss = boss;
        _boss.DeathAnimationCompleted += HandleBossDeathAnimationCompleted;
    }

    /// <summary>
    /// Releases the registered boss. The boss calls this when it is disabled
    /// and reconciles with <see cref="Phase"/> when it is enabled again.
    /// </summary>
    public void UnregisterBoss(BossStateMachine boss)
    {
        if (boss == null || boss != _boss)
        {
            return;
        }

        _boss.DeathAnimationCompleted -= HandleBossDeathAnimationCompleted;
        _boss = null;
    }

    /// <summary>
    /// Begins the encounter for a living player. Normal gameplay calls this
    /// through an activation zone; editor automation may call it directly.
    /// Refused unless the encounter is Idle. A boss reset still waiting on a
    /// respawn runs first, so every attempt starts from the authored pose;
    /// activation is refused while that reset cannot run.
    /// </summary>
    public bool TryActivate(PlayerStateMachine player)
    {
        if (_phase == EncounterPhase.Won)
        {
            Log("Activation refused: the boss is dead awaiting retry.");
            return false;
        }
        if (_phase != EncounterPhase.Idle || player == null || player.Health.IsDead)
        {
            return false;
        }

        if (IsResetPending && !RunPendingReset())
        {
            Log("Activation refused: the boss reset after the last player death has not run.");
            return false;
        }

        _phase = EncounterPhase.Active;
        _activePlayer = player;
        Log("Encounter activated.");
        EncounterActivated?.Invoke(player.transform);
        PhaseChanged?.Invoke(EncounterPhase.Active);
        return true;
    }

    /// <summary>
    /// Accepts the authoritative death report from the active player and ends
    /// the attempt, returning the encounter to Idle. The boss reset waits for
    /// the player's respawn so it happens out of sight. Reports for other
    /// players, living players, or an inactive encounter are ignored.
    /// </summary>
    public bool NotifyPlayerDied(PlayerStateMachine player)
    {
        if (_phase != EncounterPhase.Active || player == null || player != _activePlayer || !player.Health.IsDead)
        {
            return false;
        }

        _phase = EncounterPhase.Idle;
        _activePlayer = null;
        CancelPendingReset();
        _respawningPlayer = player;
        _respawningPlayer.Respawned += HandlePlayerRespawned;
        Log("Encounter deactivated after player death; boss reset waits for the respawn.");
        EncounterDeactivated?.Invoke();
        PhaseChanged?.Invoke(EncounterPhase.Idle);
        return true;
    }

    /// <summary>
    /// Accepts the authoritative death report from the registered boss and
    /// moves the encounter to Won, whether or not an attempt was active,
    /// since a dormant kill is still a win. The victorious player is protected
    /// from damage until the reset, which also refills their health, poise,
    /// stamina and healing charges. The retry is armed once the boss finishes
    /// its death animation. Reports for any other boss, a living boss, or a
    /// boss already reported dead are ignored.
    /// </summary>
    public bool NotifyBossDied(BossStateMachine boss)
    {
        if (_phase == EncounterPhase.Won || boss == null || boss != _boss || boss.Health == null || !boss.Health.IsDead)
        {
            return false;
        }

        // The win retry resets the boss, so a reset still waiting on a respawn is dropped.
        CancelPendingReset();
        PlayerStateMachine victor = _activePlayer;
        _phase = EncounterPhase.Won;
        _activePlayer = null;
        _isRetryPending = false;
        if (victor != null)
        {
            ProtectPlayer(victor);
        }

        Log("Encounter won.");
        EncounterWon?.Invoke(boss);
        PhaseChanged?.Invoke(EncounterPhase.Won);
        return true;
    }

    /// <summary>
    /// Resets a won encounter immediately instead of waiting for the retry
    /// delay. Only the registered, dead boss is accepted; the encounter stays
    /// Won when the boss refuses its reset.
    /// </summary>
    public bool ResetAfterBossDeath(BossStateMachine boss)
    {
        if (_phase != EncounterPhase.Won || boss == null || boss != _boss || !boss.Health.IsDead)
        {
            return false;
        }

        return ResetWonEncounter();
    }

    private void HandlePlayerRespawned()
    {
        RunPendingReset();
    }

    // A refused reset stays pending, so the next respawn or activation retries
    // it instead of starting an attempt against a stale boss.
    private bool RunPendingReset()
    {
        if (_boss != null && !_boss.ResetForEncounter())
        {
            Debug.LogWarning("[ArenaManager] The boss refused its reset after player death; the reset stays pending and activation is refused until it succeeds.", this);
            return false;
        }

        CancelPendingReset();
        Log("Encounter reset after player death.");
        EncounterReset?.Invoke();
        return true;
    }

    private void CancelPendingReset()
    {
        if (_respawningPlayer != null)
        {
            _respawningPlayer.Respawned -= HandlePlayerRespawned;
        }
        _respawningPlayer = null;
    }

    private void HandleBossDeathAnimationCompleted()
    {
        if (_phase != EncounterPhase.Won)
        {
            return;
        }

        // Armed here but run from Update: this callback fires inside the death clip's end event.
        _retryTimeRemaining = retryDelay;
        _isRetryPending = true;
        Log($"Retry armed for {retryDelay:0.##} s.");
    }

    private bool ResetWonEncounter()
    {
        _isRetryPending = false;
        _retryTimeRemaining = 0f;
        if (_boss == null || !_boss.ResetForEncounter())
        {
            return false;
        }

        // The winner keeps fighting from where they stand, so the next attempt needs their resources back.
        PlayerStateMachine victor = _victor;
        LiftPlayerProtection();
        if (victor != null)
        {
            victor.RestoreResources();
        }
        _phase = EncounterPhase.Idle;
        Log("Encounter reset after victory.");
        PhaseChanged?.Invoke(EncounterPhase.Idle);
        EncounterReset?.Invoke();
        return true;
    }

    // A boss hit resolving in the same instant as the killing blow must not
    // also kill the player: the attempt is already won.
    private void ProtectPlayer(PlayerStateMachine victor)
    {
        LiftPlayerProtection();
        _victor = victor;
        _victor.Health.SetDamageBlocked(this, true);
    }

    private void LiftPlayerProtection()
    {
        if (_victor != null && _victor.Health != null)
        {
            _victor.Health.SetDamageBlocked(this, false);
        }
        _victor = null;
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[ArenaManager] {message}", this);
        }
    }
}
