using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Marks something the player can lock on to and provides the point the
/// camera and player should aim at (e.g. a chest-height child transform).
/// Active instances register themselves so LockOnController can discover
/// candidates without scene searches.
/// </summary>
public class LockOnTarget : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, SceneObjectsOnly]
    private Transform targetPoint;

    public Transform TargetPoint => targetPoint;

    private static readonly List<LockOnTarget> active = new();
    public static IReadOnlyList<LockOnTarget> Active => active;

    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        active.Add(this);
        if (health != null) health.Died += HandleDied;
    }

    private void OnDisable()
    {
        active.Remove(this);
        if (health != null) health.Died -= HandleDied;
    }

    // Health is optional; targets without one stay valid until disabled or
    // destroyed. Disabling unregisters us and auto-releases an existing lock.
    private void HandleDied()
    {
        enabled = false;
    }
}
