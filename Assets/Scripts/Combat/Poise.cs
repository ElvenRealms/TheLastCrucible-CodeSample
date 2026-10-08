using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// One authority for the poise pool shared by the player and the boss.
/// Drains on the sibling Health's Damaged event by the Hit's Poise Damage and
/// through <see cref="TakeDamage"/>, recovers delayed-then-linear from its own
/// Update, and raises Broken when the pool empties. Suspend/Resume are owned
/// entirely by the break-reaction state's Enter/Exit pair; nothing else touches them.
/// </summary>
[RequireComponent(typeof(Health))]
public class Poise : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private PoiseConfigData config;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float currentPoise;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float recoveryDelayRemaining;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool isSuspended;

    private Health health;

    public float Current => currentPoise;
    public float Max => config != null ? config.MaxPoise : 0f;
    public bool IsSuspended => isSuspended;
    /// <summary>Whoever dealt the Poise Damage that drained the latest break; null when it is unknown.</summary>
    public Transform LastBreakAttacker { get; private set; }

    public event Action<float, float> Changed;
    public event Action Broken;

    private void Awake()
    {
        if (config == null)
        {
            Debug.LogError("Poise requires a Poise Config.", this);
            enabled = false;
            return;
        }

        health = GetComponent<Health>();
        currentPoise = Mathf.Max(0f, config.MaxPoise);
    }

    private void OnEnable()
    {
        health.Damaged += HandleDamaged;
    }

    private void OnDisable()
    {
        health.Damaged -= HandleDamaged;
    }

    private void Update()
    {
        if (config == null || isSuspended || currentPoise >= Max) return;

        float deltaTime = Time.deltaTime;
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

        float previous = currentPoise;
        currentPoise = Mathf.Min(Max, currentPoise + config.RecoveryPerSecond * deltaTime);
        if (!Mathf.Approximately(previous, currentPoise)) Changed?.Invoke(currentPoise, Max);
    }

    /// <summary>
    /// Drains the pool by Poise Damage that arrives without a landed hit, such as
    /// a parried attack recoiling on its attacker, and raises Broken when the pool
    /// empties. Lethal hits never break, and a suspended pool ignores Poise Damage
    /// entirely so the break reaction plays out undisturbed.
    /// </summary>
    public void TakeDamage(float amount, Transform attacker)
    {
        if (config == null || health.IsDead || isSuspended || !IsFinitePositive(amount))
        {
            return;
        }

        currentPoise = Mathf.Max(0f, currentPoise - amount);
        recoveryDelayRemaining = config.RecoveryDelay;
        if (showDebugLogs) Debug.Log($"[Poise] Drained {amount} ({currentPoise:F1}/{Max:F1}).", this);
        Changed?.Invoke(currentPoise, Max);

        if (currentPoise <= 0f)
        {
            if (showDebugLogs) Debug.Log("[Poise] Broken.", this);
            LastBreakAttacker = attacker;
            Broken?.Invoke();
        }
    }

    public void RestoreFull()
    {
        if (config == null) return;

        currentPoise = Max;
        recoveryDelayRemaining = 0f;
        Changed?.Invoke(currentPoise, Max);
        if (showDebugLogs) Debug.Log("[Poise] Restored to full.", this);
    }

    public void Suspend()
    {
        isSuspended = true;
    }

    public void Resume()
    {
        isSuspended = false;
    }

    private void HandleDamaged(DamageInfo info)
    {
        TakeDamage(info.poiseDamage, info.attacker != null ? info.attacker : info.source);
    }

    private static bool IsFinitePositive(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }
}
