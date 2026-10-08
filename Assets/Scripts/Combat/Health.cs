using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Hit points with damage/death events. Knows nothing about its listeners:
/// UI, VFX, and AI subscribe to Damaged/Died. Sibling IDamageInterceptors
/// (dodge i-frames, block, parry) get first refusal on an incoming hit.
/// </summary>
public class Health : MonoBehaviour, IDamageable
{
    private const float DefaultMaxHealth = 100f;

    [FoldoutGroup("Config"), SerializeField, MinValue(0.0001f)]
    private float maxHealth = DefaultMaxHealth;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField, MinValue(0f)]
    private float debugDamageAmount = 20f;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float _currentHealth;

    private IDamageInterceptor[] _damageInterceptors;
    private readonly HashSet<object> _damageBlockers = new();
    private readonly Dictionary<object, float> _damageMultipliers = new();

    public float MaxHealth => maxHealth;
    public float CurrentHealth => _currentHealth;
    public bool IsDead => _currentHealth <= 0f;

    /// <summary>False while any owner blocks damage through <see cref="SetDamageBlocked"/>.</summary>
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool AcceptsDamage => _damageBlockers.Count == 0;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool GodModeEnabled { get; private set; }

    public event Action<DamageInfo> Damaged;
    public event Action Died;
    public event Action<float, float> Changed;

    private void Awake()
    {
        SanitizeMaxHealth();
        _currentHealth = maxHealth;
        _damageInterceptors = GetComponents<IDamageInterceptor>();
    }

    // Tests and code may assign out-of-range values directly, so the serialized
    // MinValue attribute alone is not enough to keep the bounds finite and positive.
    private void OnValidate()
    {
        SanitizeMaxHealth();
        _currentHealth = IsFinite(_currentHealth) ? Mathf.Clamp(_currentHealth, 0f, maxHealth) : maxHealth;
    }

    /// <summary>
    /// Resolves one hit. Interceptors get first refusal; only an Applied hit
    /// raises Damaged, Changed and, when lethal, Died.
    /// </summary>
    public HitResult TakeDamage(DamageInfo info)
    {
        if (!IsFinitePositive(info.amount) || _currentHealth <= 0f) return HitResult.Ignored;
        foreach (IDamageInterceptor interceptor in _damageInterceptors)
        {
            HitResult? intercepted = interceptor.Intercept(info);
            if (intercepted.HasValue)
            {
                if (showDebugLogs) Debug.Log($"{name} negated {info.amount} damage ({intercepted.Value}).", this);
                return intercepted.Value;
            }
        }
        if (!AcceptsDamage)
        {
            if (showDebugLogs) Debug.Log($"{name} ignored {info.amount} damage (damage blocked).", this);
            return HitResult.Ignored;
        }

        float amount = info.amount;
        foreach (float multiplier in _damageMultipliers.Values)
        {
            amount *= multiplier;
        }
        if (!IsFinitePositive(amount)) return HitResult.Ignored;
        info = info.WithAmount(amount);

        if (!GodModeEnabled)
        {
            _currentHealth = Mathf.Clamp(_currentHealth - info.amount, 0f, maxHealth);
        }
        if (showDebugLogs)
        {
            var godModeStatus = GodModeEnabled ? " while god mode preserved health" : string.Empty;
            Debug.Log($"{name} took {info.amount} damage{godModeStatus} ({_currentHealth}/{maxHealth}).", this);
        }
        Damaged?.Invoke(info);
        Changed?.Invoke(_currentHealth, maxHealth);
        if (IsDead)
        {
            if (showDebugLogs) Debug.Log($"{name} died.", this);
            Died?.Invoke();
        }
        return HitResult.Applied;
    }

    public bool TryHeal(float amount)
    {
        if (!IsFinitePositive(amount) || _currentHealth <= 0f || _currentHealth >= maxHealth) return false;

        float previousHealth = _currentHealth;
        _currentHealth = Mathf.Clamp(_currentHealth + amount, 0f, maxHealth);
        if (_currentHealth <= previousHealth) return false;

        Changed?.Invoke(_currentHealth, maxHealth);
        if (showDebugLogs) Debug.Log($"{name} healed for {_currentHealth - previousHealth} ({_currentHealth}/{maxHealth}).", this);
        return true;
    }

    /// <summary>
    /// Adds or removes one owner's block on damage. Damage is refused while any
    /// owner holds a block, so owners never clear each other's.
    /// </summary>
    public void SetDamageBlocked(object owner, bool blocked)
    {
        if (owner == null)
        {
            return;
        }
        if (blocked)
        {
            _damageBlockers.Add(owner);
        }
        else
        {
            _damageBlockers.Remove(owner);
        }
    }

    /// <summary>
    /// Enables or disables health loss without suppressing accepted-hit events.
    /// </summary>
    public void SetGodMode(bool enabled)
    {
        GodModeEnabled = enabled;
    }

    /// <summary>Sets one owner's damage scale; 1 releases it without clearing another owner's scale.</summary>
    public void SetDamageMultiplier(object owner, float multiplier)
    {
        if (owner == null || !IsFinitePositive(multiplier)) return;
        if (multiplier == 1f)
        {
            _damageMultipliers.Remove(owner);
        }
        else
        {
            _damageMultipliers[owner] = multiplier;
        }
    }

    public void RestoreFull()
    {
        _currentHealth = maxHealth;
        Changed?.Invoke(_currentHealth, maxHealth);
        if (showDebugLogs) Debug.Log($"{name} restored to full health ({_currentHealth}/{maxHealth}).", this);
    }

    [FoldoutGroup("Debug"), Button("Deal Debug Damage"), DisableInEditorMode]
    private void DealDebugDamage()
    {
        TakeDamage(new DamageInfo(debugDamageAmount, null, transform.position));
    }

    private void SanitizeMaxHealth()
    {
        if (!IsFinitePositive(maxHealth)) maxHealth = DefaultMaxHealth;
    }

    private static bool IsFinitePositive(float value)
    {
        return IsFinite(value) && value > 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
