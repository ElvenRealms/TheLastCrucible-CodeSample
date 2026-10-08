using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sweeps ground-plane body footprints and slides the mover along contact without
/// moving the other body. Independent of physics layers and damage hitboxes.
/// Vertical movement is preserved; airborne actors still reserve their footprint.
/// </summary>
public sealed class CombatBodyConstraint
{
    private const float ContactPadding = 0.01f;
    private const float MinimumMovementSquared = 0.00000001f;
    private const int MaximumSlides = 4;
    private static readonly List<CombatBodyConstraint> Bodies = new();

    private readonly Collider _collider;
    private readonly float _localRadius;
    private readonly Vector3 _localCenter;

    private Vector3 Center => _collider.transform.TransformPoint(_localCenter);
    private float Radius
    {
        get
        {
            Vector3 scale = _collider.transform.lossyScale;
            return _localRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
    }

    public CombatBodyConstraint(CharacterController controller)
    {
        _collider = controller;
        _localRadius = controller.radius;
        _localCenter = controller.center;
    }

    public CombatBodyConstraint(CapsuleCollider capsule)
    {
        _collider = capsule;
        _localRadius = capsule.radius;
        _localCenter = capsule.center;
    }

    public void Register()
    {
        Bodies.RemoveAll(body => body._collider == null);
        if (!Bodies.Contains(this))
        {
            Bodies.Add(this);
        }
    }

    public void Unregister()
    {
        Bodies.Remove(this);
    }

    /// <summary>
    /// Clips the entire requested path, including fast root motion, against live
    /// transform positions. Existing overlaps permit escape but no deeper movement.
    /// </summary>
    public Vector3 Constrain(Vector3 displacement)
    {
        Vector3 remaining = displacement;
        remaining.y = 0f;
        Vector3 position = Center;
        Vector3 applied = Vector3.zero;

        for (int slide = 0; slide < MaximumSlides && remaining.sqrMagnitude > MinimumMovementSquared; slide++)
        {
            float fraction = 1f;
            Vector3 normal = Vector3.zero;
            foreach (var other in Bodies)
            {
                if (other == this || other._collider == null || !other._collider.enabled ||
                    !other._collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 offset = position - other.Center;
                offset.y = 0f;
                float radius = Radius + other.Radius + ContactPadding;
                if (TryGetContact(offset, remaining, radius, out float contact) && contact < fraction)
                {
                    fraction = contact;
                    normal = (offset + remaining * contact).normalized;
                }
            }

            Vector3 travel = remaining * fraction;
            position += travel;
            applied += travel;
            remaining -= travel;
            if (fraction >= 1f)
            {
                break;
            }

            // Only remove inward motion: tangential movement and retreat stay responsive.
            remaining -= normal * Mathf.Min(0f, Vector3.Dot(remaining, normal));
        }

        applied.y = displacement.y;
        return applied;
    }

    private static bool TryGetContact(Vector3 offset, Vector3 displacement, float radius, out float fraction)
    {
        fraction = 0f;
        float approach = Vector3.Dot(offset, displacement);
        if (approach >= 0f)
        {
            return false;
        }

        float separation = offset.sqrMagnitude - radius * radius;
        if (separation <= 0f)
        {
            return true;
        }

        float lengthSquared = displacement.sqrMagnitude;
        float discriminant = approach * approach - lengthSquared * separation;
        if (discriminant <= 0f)
        {
            return false;
        }

        fraction = separation / (-approach + Mathf.Sqrt(discriminant));
        return fraction >= 0f && fraction < 1f;
    }
}
