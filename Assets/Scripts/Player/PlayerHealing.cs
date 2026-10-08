using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Owns healing item charges and the Heal Attempt window. The charge is
/// consumed the instant the attempt opens; healing is then applied
/// incrementally over the window. Presentation observes the attempt events
/// plus ChargesChanged and Used. Rejected attempts are silent.
/// </summary>
[RequireComponent(typeof(Health))]
public class PlayerHealing : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private HealingItemConfigData config;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int currentCharges;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool isAttempting;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float healedSoFar;

    private Health health;

    public int CurrentCharges => currentCharges;
    public int MaxCharges => config.maxCharges;
    public float HealAmount => config.healAmount;
    public float UseDuration => config.useDuration;
    public float MoveSpeedMultiplier => config.moveSpeedMultiplier;
    public bool CanUse => currentCharges > 0 && !health.IsDead
        && health.CurrentHealth < health.MaxHealth;
    public bool IsAttempting => isAttempting;

    /// <summary>
    /// Heal Window completion in 0..1 based on health applied so far, and zero
    /// whenever no attempt is active.
    /// </summary>
    public float AttemptProgress
    {
        get
        {
            if (!isAttempting) return 0f;
            float amount = HealAmount;
            return amount > 0f ? Mathf.Clamp01(healedSoFar / amount) : 1f;
        }
    }

    public event Action<int, int> ChargesChanged;
    public event Action Used;
    public event Action AttemptStarted;
    /// <summary>Fires once per attempt with true when it completed (full amount applied,
    /// health full, or a heal was rejected) or false when it was cancelled.</summary>
    public event Action<bool> AttemptEnded;

    private void Awake()
    {
        health = GetComponent<Health>();
        if (config == null)
        {
            Debug.LogError("PlayerHealing requires a Healing Item Config.", this);
            enabled = false;
            return;
        }

        RestoreCharges();
    }

    /// <summary>
    /// Opens a Heal Window, consuming a charge immediately. Returns false when
    /// one is already open or the item cannot be used, in which case nothing
    /// is raised or consumed.
    /// </summary>
    public bool BeginAttempt()
    {
        if (isAttempting || !CanUse) return false;

        currentCharges = Mathf.Max(0, currentCharges - 1);
        ChargesChanged?.Invoke(currentCharges, MaxCharges);
        Used?.Invoke();
        if (showDebugLogs) Debug.Log($"[PlayerHealing] Used charge: {currentCharges}/{MaxCharges}.", this);

        isAttempting = true;
        healedSoFar = 0f;
        if (showDebugLogs) Debug.Log($"[PlayerHealing] Began heal attempt over {UseDuration:0.##}s.", this);
        AttemptStarted?.Invoke();
        return true;
    }

    /// <summary>
    /// Applies this tick's share of the heal and ends the attempt once the full
    /// amount lands, health is full, or a heal is rejected. Callers pass scaled
    /// time, so hit-stop extends the window. The charge is already spent, so
    /// whatever healed before an interrupt stays applied.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!isAttempting) return;

        float healAmount = HealAmount;
        float duration = UseDuration;
        float remaining = Mathf.Max(0f, healAmount - healedSoFar);
        float delta = duration > 0f ? healAmount * Mathf.Max(0f, deltaTime) / duration : remaining;
        delta = Mathf.Min(delta, remaining);

        if (delta > 0f)
        {
            if (!health.TryHeal(delta))
            {
                EndAttempt(true);
                return;
            }
            healedSoFar += delta;
        }

        if (healedSoFar >= healAmount || health.CurrentHealth >= health.MaxHealth) EndAttempt(true);
    }

    /// <summary>
    /// Stops the open Heal Window without a refund; whatever healed so far
    /// stays applied. Reported as not completed.
    /// </summary>
    public void CancelAttempt()
    {
        EndAttempt(false);
    }

    public void RestoreCharges()
    {
        int previous = currentCharges;
        currentCharges = MaxCharges;
        if (currentCharges == previous) return;

        ChargesChanged?.Invoke(currentCharges, MaxCharges);
        if (showDebugLogs) Debug.Log($"[PlayerHealing] Restored charges: {currentCharges}/{MaxCharges}.", this);
    }

    // The only terminal path for an attempt, and idempotent: whichever caller
    // gets here first wins and later calls no-op. The charge is already spent
    // by BeginAttempt, so this never refunds or heals further.
    private void EndAttempt(bool completed)
    {
        if (!isAttempting) return;

        isAttempting = false;
        healedSoFar = 0f;
        AttemptEnded?.Invoke(completed);
    }
}
