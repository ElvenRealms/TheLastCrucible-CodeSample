using System;
using Sirenix.OdinInspector;
using UnityEngine;

public enum StaminaAction
{
    Sprint,
    Dodge,
    Attack,
    Block
}

/// <summary>
/// Owns the player's stamina value, spend rules, exhaustion latch, and recovery.
/// Action states request spends; presentation observes events only.
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private StaminaConfigData config;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float currentStamina;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float recoveryDelayRemaining;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool isExhausted;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool sprintReleaseRequired;

    public float CurrentStamina => currentStamina;
    public float MaxStamina => config != null ? config.MaxStamina : 0f;
    public float NormalizedStamina => MaxStamina > 0f ? currentStamina / MaxStamina : 0f;
    public float SprintCostPerSecond => config != null ? config.SprintCostPerSecond : 0f;
    public float DodgeCost => config != null ? config.DodgeCost : 0f;
    public float BlockCostPerDamage => config != null ? config.BlockCostPerDamage : 0f;
    public bool IsExhausted => isExhausted;
    public bool CanSprint => !isExhausted && currentStamina > 0f;

    public event Action<float, float> Changed;
    public event Action<StaminaAction> SpendRejected;
    public event Action<bool> ExhaustionChanged;

    private void Awake()
    {
        if (config == null)
        {
            Debug.LogError("PlayerStamina requires a Stamina Config.", this);
            enabled = false;
            return;
        }

        currentStamina = Mathf.Max(0f, config.MaxStamina);
    }

    /// <summary>Read-only affordability check; raises SpendRejected on refusal so rejection feedback fires at the commit decision.</summary>
    public bool CanAfford(StaminaAction action, float amount)
    {
        if (config == null) return false;

        amount = Mathf.Max(0f, amount);
        if (amount <= 0f) return true;
        if (currentStamina + Mathf.Epsilon < amount)
        {
            SpendRejected?.Invoke(action);
            if (showDebugLogs)
            {
                Debug.Log($"[PlayerStamina] Rejected {action}: {currentStamina:F1}/{MaxStamina:F1}.", this);
            }
            return false;
        }

        return true;
    }

    public bool TrySpend(StaminaAction action, float amount)
    {
        if (config == null) return false;

        amount = Mathf.Max(0f, amount);
        if (amount <= 0f) return true;
        if (currentStamina + Mathf.Epsilon < amount)
        {
            if (action == StaminaAction.Sprint)
            {
                currentStamina = 0f;
                recoveryDelayRemaining = config.RecoveryDelay;
                sprintReleaseRequired = true;
                SetExhausted(true);
                Changed?.Invoke(currentStamina, MaxStamina);
            }

            SpendRejected?.Invoke(action);
            if (showDebugLogs)
                Debug.Log($"[PlayerStamina] Rejected {action}: {currentStamina:F1}/{MaxStamina:F1}.", this);
            return false;
        }

        Spend(action, amount);
        return true;
    }

    /// <summary>
    /// Spends a cost that cannot be refused, such as a blocked hit, emptying the
    /// pool when it cannot cover it. False when the pool fell short of the amount.
    /// </summary>
    public bool SpendUpTo(StaminaAction action, float amount)
    {
        if (config == null)
        {
            return false;
        }

        amount = Mathf.Max(0f, amount);
        if (amount <= 0f)
        {
            return true;
        }

        bool covered = currentStamina + Mathf.Epsilon >= amount;
        Spend(action, amount);
        return covered;
    }

    public void Tick(float deltaTime)
    {
        if (config == null || deltaTime <= 0f || currentStamina >= MaxStamina) return;

        if (recoveryDelayRemaining > 0f)
        {
            if (deltaTime <= recoveryDelayRemaining)
            {
                recoveryDelayRemaining -= deltaTime;
                return;
            }

            deltaTime -= recoveryDelayRemaining;
            recoveryDelayRemaining = 0f;
        }

        float previous = currentStamina;
        currentStamina = Mathf.Min(
            MaxStamina,
            currentStamina + config.RecoveryPerSecond * deltaTime);
        TryClearExhaustion();
        if (!Mathf.Approximately(previous, currentStamina)) Changed?.Invoke(currentStamina, MaxStamina);
    }

    public void NotifySprintReleased()
    {
        sprintReleaseRequired = false;
        TryClearExhaustion();
    }

    public void RestoreFull()
    {
        if (config == null) return;

        currentStamina = MaxStamina;
        recoveryDelayRemaining = 0f;
        sprintReleaseRequired = false;
        SetExhausted(false);
        Changed?.Invoke(currentStamina, MaxStamina);
        if (showDebugLogs) Debug.Log("[PlayerStamina] Restored to full.", this);
    }

    private void Spend(StaminaAction action, float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        recoveryDelayRemaining = config.RecoveryDelay;
        if (currentStamina <= 0f)
        {
            sprintReleaseRequired = action == StaminaAction.Sprint;
            SetExhausted(true);
        }

        Changed?.Invoke(currentStamina, MaxStamina);
        if (showDebugLogs)
            Debug.Log($"[PlayerStamina] Spent {amount:F1} on {action}: {currentStamina:F1}/{MaxStamina:F1}.", this);
    }

    private void SetExhausted(bool value)
    {
        if (isExhausted == value) return;
        isExhausted = value;
        ExhaustionChanged?.Invoke(value);
        if (showDebugLogs) Debug.Log($"[PlayerStamina] Exhaustion changed to {value}.", this);
    }

    private void TryClearExhaustion()
    {
        if (!isExhausted || sprintReleaseRequired) return;
        if (currentStamina >= Mathf.Min(config.SprintResumeThreshold, MaxStamina))
        {
            SetExhausted(false);
        }
    }
}