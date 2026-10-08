using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// What the player did in one attempt, plus how many attempts the win took.
/// </summary>
[Serializable]
public struct FightStats
{
    [SuffixLabel("s")]
    public float duration;
    public int attempts;
    public int hitsTaken;
    public int parries;
    public int dodges;
    public int healsUsed;
}

/// <summary>
/// Counts the active player's actions during an attempt from the events the
/// player already publishes. Each activation starts a fresh count; the
/// attempt total carries over until a win.
/// </summary>
public class FightStatsTracker : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;

    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private FightStats stats;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isTracking;

    private PlayerStateMachine _player;
    private bool _lastAttemptWon;

    /// <summary>The current attempt's stats, or the last attempt's once it has ended.</summary>
    public FightStats Stats => stats;

    private void OnEnable()
    {
        arenaManager.EncounterActivated += HandleEncounterActivated;
        arenaManager.EncounterDeactivated += StopTracking;
        arenaManager.EncounterWon += HandleEncounterWon;
    }

    private void OnDisable()
    {
        arenaManager.EncounterActivated -= HandleEncounterActivated;
        arenaManager.EncounterDeactivated -= StopTracking;
        arenaManager.EncounterWon -= HandleEncounterWon;
        StopTracking();
    }

    // Pause and hit-stop hold time at zero and are not fight time; slow motion still counts in real time.
    private void Update()
    {
        if (isTracking && Time.timeScale > 0f)
        {
            stats.duration += Time.unscaledDeltaTime;
        }
    }

    private void HandleEncounterActivated(Transform target)
    {
        StopTracking();
        int attempts = _lastAttemptWon ? 1 : stats.attempts + 1;
        _lastAttemptWon = false;
        stats = new FightStats { attempts = attempts };

        _player = arenaManager.ActivePlayer;
        if (_player == null) return;

        _player.Health.Damaged += HandleDamaged;
        _player.Defense.Parried += HandleParried;
        _player.Healing.Used += HandleHealUsed;
        _player.StateChanged += HandleStateChanged;
        isTracking = true;
    }

    private void HandleEncounterWon(BossStateMachine boss)
    {
        _lastAttemptWon = true;
        StopTracking();
    }

    private void StopTracking()
    {
        isTracking = false;
        if (_player == null) return;

        _player.Health.Damaged -= HandleDamaged;
        _player.Defense.Parried -= HandleParried;
        _player.Healing.Used -= HandleHealUsed;
        _player.StateChanged -= HandleStateChanged;
        _player = null;
    }

    private void HandleDamaged(DamageInfo info)
    {
        stats.hitsTaken++;
    }

    private void HandleParried(DamageInfo info)
    {
        stats.parries++;
    }

    private void HandleHealUsed()
    {
        stats.healsUsed++;
    }

    private void HandleStateChanged(PlayerState state)
    {
        if (state == _player.Dodge) stats.dodges++;
    }
}
