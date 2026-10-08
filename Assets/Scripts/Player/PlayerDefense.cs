using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Owns the player's dodge invulnerability and guard. A raised guard stops
/// blockable hits from the front at a stamina cost, and parries every parryable
/// hit that lands inside the Parry Window a Block press opens. A blocked hit
/// that stamina cannot pay for still deals no damage but breaks the guard.
/// A lowered guard cannot come back up until its Guard Cooldown has passed.
/// Intercepts incoming hits via IDamageInterceptor before Health applies them.
/// </summary>
[RequireComponent(typeof(PlayerStamina))]
public class PlayerDefense : MonoBehaviour, IDamageInterceptor
{
    [FormerlySerializedAs("parryArc")]
    [FoldoutGroup("Config"), SerializeField, Range(0f, 360f), Tooltip("Total frontal angle in which the guard stops a hit.")]
    private float guardArc = 120f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds after a Block press in which a parryable hit is parried instead of blocked.")]
    private float parryWindow = 0.15f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds after a Block press before another press opens a new Parry Window, so mashing Block only blocks. A parry clears the wait.")]
    private float parryRearmDelay = 0.5f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds after the guard lowers before Block can raise it again. A guard that parried lowers without the wait.")]
    private float guardCooldown = 0.2f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsDodgeInvulnerable { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsGuarding { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, SuffixLabel("s")]
    private float _parryWindowRemaining;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, SuffixLabel("s")]
    private float _parryRearmRemaining;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, SuffixLabel("s")]
    private float _guardCooldownRemaining;

    private PlayerStamina _stamina;
    private bool _hasParried;

    public float ParryWindow => parryWindow;
    public bool IsParryWindowOpen => IsGuarding && _parryWindowRemaining > 0f;
    public bool CanRaiseGuard => _guardCooldownRemaining <= 0f;

    /// <summary>Raised when a lowered guard comes up.</summary>
    public event Action GuardRaised;
    /// <summary>Raised when the guard parries a hit.</summary>
    public event Action<DamageInfo> Parried;
    /// <summary>Raised when the guard stops a hit and holds.</summary>
    public event Action<DamageInfo> Blocked;
    /// <summary>Raised when the guard stops a hit that stamina could not pay for, and drops.</summary>
    public event Action<DamageInfo> GuardBroken;

    private void Awake()
    {
        _stamina = GetComponent<PlayerStamina>();
    }

    public void SetDodgeInvulnerable(bool active)
    {
        IsDodgeInvulnerable = active;
    }

    /// <summary>
    /// Raises the guard for a Block press, or renews a raised one. The Parry Window
    /// opens unless the previous press was inside the re-arm delay.
    /// </summary>
    public void RaiseGuard()
    {
        _parryWindowRemaining = _parryRearmRemaining > 0f ? 0f : parryWindow;
        _parryRearmRemaining = parryRearmDelay;
        SetGuarding();
    }

    /// <summary>
    /// Raises the guard without a Parry Window, for a guard that comes back up
    /// because Block is still held rather than freshly pressed.
    /// </summary>
    public void ResumeGuard()
    {
        _parryWindowRemaining = 0f;
        SetGuarding();
    }

    /// <summary>
    /// Lowers the guard and starts the Guard Cooldown, unless the guard parried while it was up.
    /// </summary>
    public void LowerGuard()
    {
        if (IsGuarding && !_hasParried)
        {
            _guardCooldownRemaining = guardCooldown;
        }
        IsGuarding = false;
        _parryWindowRemaining = 0f;
    }

    /// <summary>Counts down the Parry Window, the re-arm delay and the Guard Cooldown.</summary>
    public void Tick(float deltaTime)
    {
        _parryWindowRemaining = Mathf.Max(0f, _parryWindowRemaining - deltaTime);
        _parryRearmRemaining = Mathf.Max(0f, _parryRearmRemaining - deltaTime);
        _guardCooldownRemaining = Mathf.Max(0f, _guardCooldownRemaining - deltaTime);
    }

    /// <summary>
    /// Evades any hit during dodge invulnerability. A raised guard facing the hit
    /// parries it inside the Parry Window when it is parryable, and otherwise blocks
    /// it when it is blockable; any other hit is let through.
    /// </summary>
    public HitResult? Intercept(DamageInfo info)
    {
        if (IsDodgeInvulnerable)
        {
            return HitResult.Evaded;
        }
        if (!IsGuarding || !IsInGuardArc(info.source))
        {
            return null;
        }

        if (info.parryable && IsParryWindowOpen)
        {
            // The next press may parry at once, so a combo can be parried hit by hit.
            _parryRearmRemaining = 0f;
            _hasParried = true;
            Log($"parried a hit from {info.source.name}");
            Parried?.Invoke(info);
            return HitResult.Parried;
        }
        if (!info.blockable)
        {
            return null;
        }

        if (_stamina.SpendUpTo(StaminaAction.Block, info.amount * _stamina.BlockCostPerDamage))
        {
            Log($"blocked a hit from {info.source.name}");
            Blocked?.Invoke(info);
        }
        else
        {
            LowerGuard();
            Log($"had its guard broken by a hit from {info.source.name}");
            GuardBroken?.Invoke(info);
        }
        return HitResult.Blocked;
    }

    public void ResetDefense()
    {
        IsDodgeInvulnerable = false;
        LowerGuard();
        _parryRearmRemaining = 0f;
        _guardCooldownRemaining = 0f;
    }

    private void SetGuarding()
    {
        if (IsGuarding)
        {
            return;
        }

        IsGuarding = true;
        _hasParried = false;
        GuardRaised?.Invoke();
    }

    private bool IsInGuardArc(Transform source)
    {
        if (source == null)
        {
            return false;
        }

        Vector3 sourceDirection = source.position - transform.position;
        sourceDirection.y = 0f;
        return sourceDirection.sqrMagnitude > Mathf.Epsilon &&
            Vector3.Angle(transform.forward, sourceDirection) <= guardArc * 0.5f;
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"{name} {message}.", this);
        }
    }
}
