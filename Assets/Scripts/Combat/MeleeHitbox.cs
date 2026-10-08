using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Blade capsule overlap while an attack's hit window is open, substepped
/// between last frame's pose and the current one so fast swings can't tunnel.
/// Damages each IDamageable at most once per window and reports how each hit
/// resolved. The owning attack state drives BeginWindow/EndWindow; both are
/// idempotent, so they can be called every frame from a polled time window.
/// </summary>
public class MeleeHitbox : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, ChildGameObjectsOnly]
    private Transform bladeBase;
    [FoldoutGroup("Config"), SerializeField, Required, ChildGameObjectsOnly]
    private Transform bladeTip;
    [FoldoutGroup("Config"), SerializeField, MinValue(0.01f)]
    private float radius = 0.2f;
    [FoldoutGroup("Config"), SerializeField]
    private LayerMask targetLayers;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private bool _windowActive;

    public bool IsWindowActive => _windowActive;
    /// <summary>The middle of the blade, for presentation that happens on the weapon itself.</summary>
    public Vector3 BladeCenter => (bladeBase.position + bladeTip.position) * 0.5f;

    /// <summary>Raised when a Hit Window opens; presentation observes these to follow the swing.</summary>
    public event Action WindowOpened;
    /// <summary>Raised when a Hit Window closes, whether it landed a Hit or not.</summary>
    public event Action WindowClosed;
    /// <summary>
    /// Raised after a target resolves a Hit from this blade, with the outcome the
    /// target reported. The owner reacts here, for example losing Poise when Parried.
    /// </summary>
    public event Action<HitResult, DamageInfo> HitResolved;

    // 32 substeps at radius 0.2 covers ~6.4m of blade travel in one frame
    // before gaps can reopen; query count stays travel-bound in normal frames.
    private const int MaxSubsteps = 32;

    private readonly Collider[] _overlapResults = new Collider[16];
    private readonly HashSet<IDamageable> _hitThisWindow = new();
    private Transform _attacker;
    private float _currentDamage;
    private bool _currentParryable;
    private bool _currentBlockable;
    private float _currentPoiseDamage;
    private bool _warnedBufferFull;
    private Vector3 _previousBase;
    private Vector3 _previousTip;

    private void Awake()
    {
        // The wielding actor is the nearest Health above the blade; an unowned hitbox reports no attacker.
        Health owner = GetComponentInParent<Health>();
        if (owner != null) _attacker = owner.transform;
    }

    public void BeginWindow(float damage, bool parryable = false, float poiseDamage = 0f, bool blockable = false)
    {
        if (_windowActive) return;
        _windowActive = true;
        _currentDamage = damage;
        _currentParryable = parryable;
        _currentBlockable = blockable;
        _currentPoiseDamage = poiseDamage;
        _warnedBufferFull = false;
        _hitThisWindow.Clear();
        // Fresh pose snapshot so the sweep never starts from a previous swing.
        _previousBase = bladeBase.position;
        _previousTip = bladeTip.position;
        WindowOpened?.Invoke();
    }

    public void EndWindow()
    {
        if (!_windowActive) return;
        _windowActive = false;
        WindowClosed?.Invoke();
    }

    /// <summary>
    /// Delivers this window's Hit to one target and raises HitResolved with its
    /// outcome. Returns Ignored without raising when the window is closed or the
    /// target was already hit in this window.
    /// </summary>
    public HitResult ResolveHit(IDamageable target, Vector3 hitPoint)
    {
        // A HitResolved subscriber may close the window mid-sweep; later overlaps must not land.
        if (!_windowActive || target == null || !_hitThisWindow.Add(target)) return HitResult.Ignored;

        var info = new DamageInfo(_currentDamage, transform, hitPoint, _currentParryable, _currentPoiseDamage, _attacker, _currentBlockable);
        HitResult result = target.TakeDamage(info);
        if (showDebugLogs) Debug.Log($"{name} hit {target} for {_currentDamage}: {result}.", this);
        HitResolved?.Invoke(result, info);
        return result;
    }

    private void Update()
    {
        if (!_windowActive) return;

        // Substep count scales with how far the blade moved since last frame,
        // so the interpolated capsules overlap along the swing arc.
        Vector3 currentBase = bladeBase.position;
        Vector3 currentTip = bladeTip.position;
        float travel = Mathf.Max(Vector3.Distance(_previousBase, currentBase), Vector3.Distance(_previousTip, currentTip));
        int substeps = Mathf.Clamp(Mathf.CeilToInt(travel / radius), 1, MaxSubsteps);
        for (int step = 1; step <= substeps; step++)
        {
            float t = (float)step / substeps;
            OverlapAndDamage(Vector3.Lerp(_previousBase, currentBase, t), Vector3.Lerp(_previousTip, currentTip, t));
        }
        _previousBase = currentBase;
        _previousTip = currentTip;
    }

    private void OverlapAndDamage(Vector3 basePosition, Vector3 tipPosition)
    {
        int count = Physics.OverlapCapsuleNonAlloc(basePosition, tipPosition, radius, _overlapResults, targetLayers);
        if (count == _overlapResults.Length && !_warnedBufferFull)
        {
            _warnedBufferFull = true;
            Debug.LogWarning($"{name} overlap buffer full ({count}); hits may be dropped.", this);
        }
        for (int i = 0; i < count; i++)
        {
            Collider hit = _overlapResults[i];
            var damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;

            ResolveHit(damageable, hit.ClosestPoint(basePosition));
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (bladeBase == null || bladeTip == null) return;
        Gizmos.color = _windowActive ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(bladeBase.position, radius);
        Gizmos.DrawWireSphere(bladeTip.position, radius);
        Gizmos.DrawLine(bladeBase.position, bladeTip.position);
    }
}
