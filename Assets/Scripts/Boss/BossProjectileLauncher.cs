using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fires projectile volleys outward from the boss. Each projectile flies straight until it hits a
/// target, an obstacle or the end of its range, then plays its impact effect. Targets are hit by the
/// projectile's full radius; obstacles only stop its center, so it can skim floors and steps.
/// A blocked or parried projectile is spent like one that landed. A dodged or ignored hit lets the
/// projectile fly on, and it never resolves the same target twice.
/// </summary>
public class BossProjectileLauncher : MonoBehaviour
{
    [FoldoutGroup("Collision"), SerializeField]
    private LayerMask targetLayers;
    [FoldoutGroup("Collision"), SerializeField, Tooltip("Layers whose solid colliders stop a projectile, such as walls and props.")]
    private LayerMask obstacleLayers;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private int _inFlightCount;

    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly Dictionary<BossProjectileData, Projectile[]> _volleys = new();
    private Transform _container;

    public int InFlightCount => _inFlightCount;

    /// <summary>Raised once per volley with the point its aimed projectile appears at.</summary>
    public event Action<BossProjectileData, Vector3> Fired;
    /// <summary>Raised after a target resolves a hit from a projectile, with the outcome the target reported.</summary>
    public event Action<BossProjectileData, HitResult, DamageInfo> HitResolved;
    /// <summary>Raised where a projectile ends, whether on a target, an obstacle or the end of its range.</summary>
    public event Action<BossProjectileData, Vector3> Ended;

    private void OnDisable()
    {
        StopAll();
    }

    private void OnDestroy()
    {
        if (_container != null)
        {
            Destroy(_container.gameObject);
        }
    }

    private void Update()
    {
        if (_inFlightCount == 0)
        {
            return;
        }

        foreach (var volley in _volleys.Values)
        {
            foreach (var projectile in volley)
            {
                if (projectile.isFlying)
                {
                    Advance(projectile, projectile.data.Speed * Time.deltaTime);
                }
            }
        }
    }

    /// <summary>The point a volley leaves from: the data's height above the boss's root.</summary>
    public Vector3 GetOrigin(BossProjectileData data)
    {
        return transform.position + Vector3.up * data.Height;
    }

    /// <summary>
    /// Launches one volley along a direction, spread around the vertical axis. A volley still in
    /// flight from the same data is cut short and reused.
    /// </summary>
    public void Fire(BossProjectileData data, Vector3 direction)
    {
        Projectile[] volley = GetVolley(data);
        volley[0].resolvedVolleyTargets?.Clear();
        Vector3 aim = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
        Vector3 origin = GetOrigin(data);
        for (int i = 0; i < volley.Length; i++)
        {
            Launch(volley[i], origin, Quaternion.AngleAxis(data.GetYawOffset(i), Vector3.up) * aim);
        }

        if (showDebugLogs)
        {
            Debug.Log($"{name} fired {volley.Length} '{data.name}' projectiles.", this);
        }
        Fired?.Invoke(data, origin + aim * data.SpawnDistance);
    }

    /// <summary>Removes every projectile in flight without an impact.</summary>
    public void StopAll()
    {
        foreach (var volley in _volleys.Values)
        {
            foreach (var projectile in volley)
            {
                if (projectile.isFlying)
                {
                    projectile.isFlying = false;
                    Stop(projectile.particles, true);
                }
            }
        }
        _inFlightCount = 0;
    }

    private void Launch(Projectile projectile, Vector3 origin, Vector3 direction)
    {
        if (projectile.isFlying)
        {
            projectile.isFlying = false;
            _inFlightCount--;
            Stop(projectile.particles, true);
        }

        BossProjectileData data = projectile.data;
        projectile.position = origin;
        projectile.direction = direction;
        projectile.travelled = 0f;
        projectile.resolvedTarget = null;
        projectile.isFlying = true;
        projectile.visual.rotation = Quaternion.LookRotation(direction) * data.ProjectilePrefab.transform.localRotation;
        _inFlightCount++;

        // Sweeping out from the root catches a target standing closer than the spawn distance.
        Advance(projectile, data.SpawnDistance);
        if (!projectile.isFlying)
        {
            return;
        }

        foreach (var particle in projectile.particles)
        {
            particle.Play(false);
        }
        PlayOneShot(projectile.launch, projectile.launchParticles, projectile.position, Quaternion.LookRotation(direction));
    }

    private void Advance(Projectile projectile, float distance)
    {
        float range = projectile.data.Range;
        distance = Mathf.Min(distance, range - projectile.travelled);
        if (TryFindStop(projectile, distance, out float stopDistance))
        {
            End(projectile, projectile.position + projectile.direction * stopDistance);
            return;
        }

        projectile.position += projectile.direction * distance;
        projectile.travelled += distance;
        projectile.visual.position = projectile.position;
        if (projectile.travelled >= range)
        {
            End(projectile, projectile.position);
        }
    }

    // Walks this step's targets nearest first, resolving them until one takes the hit, then
    // falls back to the obstacle that blocks the step, if any.
    private bool TryFindStop(Projectile projectile, float distance, out float stopDistance)
    {
        bool blocked = TryFindObstacle(projectile, distance, out float obstacleDistance);
        int hitCount = Physics.SphereCastNonAlloc(projectile.position, projectile.data.HitRadius, projectile.direction, _hits, distance, targetLayers);
        Array.Sort(_hits, 0, hitCount, HitDistanceComparer.Instance);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hits[i];
            if (blocked && hit.distance > obstacleDistance)
            {
                break;
            }
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && target != projectile.resolvedTarget && ResolveHit(projectile, target, hit))
            {
                stopDistance = hit.distance;
                return true;
            }
        }

        stopDistance = blocked ? obstacleDistance : distance;
        return blocked;
    }

    private bool TryFindObstacle(Projectile projectile, float distance, out float obstacleDistance)
    {
        int hitCount = Physics.RaycastNonAlloc(projectile.position, projectile.direction, _hits, distance, obstacleLayers, QueryTriggerInteraction.Ignore);
        obstacleDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            if (_hits[i].distance < obstacleDistance && !_hits[i].collider.transform.IsChildOf(transform))
            {
                obstacleDistance = _hits[i].distance;
            }
        }
        return obstacleDistance < float.MaxValue;
    }

    // True when the hit landed, was blocked or was parried; a dodge or an ignored hit lets the projectile through.
    private bool ResolveHit(Projectile projectile, IDamageable target, RaycastHit hit)
    {
        BossProjectileData data = projectile.data;
        projectile.resolvedTarget = target;
        if (projectile.resolvedVolleyTargets != null && !projectile.resolvedVolleyTargets.Add(target))
        {
            return false;
        }
        // A cast that starts inside the target reports no point, so the projectile's center stands in.
        Vector3 hitPoint = hit.distance > 0f ? hit.point : projectile.position;
        var info = new DamageInfo(data.Damage, projectile.visual, hitPoint, data.Parryable, data.PoiseDamage, transform, data.Blockable);
        HitResult result = target.TakeDamage(info);
        if (showDebugLogs)
        {
            Debug.Log($"{name} projectile hit {target} for {data.Damage}: {result}.", this);
        }
        HitResolved?.Invoke(data, result, info);
        return result is HitResult.Applied or HitResult.Parried or HitResult.Blocked;
    }

    private void End(Projectile projectile, Vector3 point)
    {
        projectile.isFlying = false;
        _inFlightCount--;
        projectile.visual.position = point;
        Stop(projectile.particles, false);
        PlayOneShot(projectile.impact, projectile.impactParticles, point, Quaternion.LookRotation(-projectile.direction));
        Ended?.Invoke(projectile.data, point);
    }

    private static void PlayOneShot(Transform effect, ParticleSystem[] particles, Vector3 position, Quaternion rotation)
    {
        if (effect == null)
        {
            return;
        }

        effect.SetPositionAndRotation(position, rotation);
        foreach (var particle in particles)
        {
            particle.Clear(false);
            particle.Play(false);
        }
    }

    // Ring-buffered particles never expire on their own, so they are cleared; the rest burn out.
    private static void Stop(ParticleSystem[] particles, bool clear)
    {
        foreach (var particle in particles)
        {
            if (particle == null)
            {
                continue;
            }
            bool keepsParticles = particle.main.ringBufferMode != ParticleSystemRingBufferMode.Disabled;
            particle.Stop(false, clear || keepsParticles ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private Projectile[] GetVolley(BossProjectileData data)
    {
        if (_volleys.TryGetValue(data, out Projectile[] volley))
        {
            return volley;
        }

        // The projectiles fly in world space, so they live at the scene root rather than on the boss.
        if (_container == null)
        {
            _container = new GameObject($"{name} Projectiles").transform;
        }

        volley = new Projectile[data.Count];
        var resolvedTargets = data.HitOncePerVolley ? new HashSet<IDamageable>() : null;
        for (int i = 0; i < volley.Length; i++)
        {
            var projectile = new Projectile { data = data, resolvedVolleyTargets = resolvedTargets };
            projectile.visual = Instantiate(data.ProjectilePrefab, _container).transform;
            projectile.visual.localScale = data.ProjectileScale;
            projectile.particles = PrepareParticles(projectile.visual);
            if (data.LaunchPrefab != null)
            {
                projectile.launch = Instantiate(data.LaunchPrefab, _container).transform;
                projectile.launch.localScale = Vector3.one * data.LaunchScale;
                projectile.launchParticles = PrepareParticles(projectile.launch);
            }
            if (data.ImpactPrefab != null)
            {
                projectile.impact = Instantiate(data.ImpactPrefab, _container).transform;
                projectile.impact.localScale = Vector3.one * data.ImpactScale;
                projectile.impactParticles = PrepareParticles(projectile.impact);
            }
            volley[i] = projectile;
        }
        _volleys.Add(data, volley);
        return volley;
    }

    private static ParticleSystem[] PrepareParticles(Transform root)
    {
        ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var particle in particles)
        {
            ParticleSystem.MainModule main = particle.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.None;
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        return particles;
    }

    private sealed class Projectile
    {
        public BossProjectileData data;
        public Transform visual;
        public ParticleSystem[] particles;
        public Transform launch;
        public ParticleSystem[] launchParticles;
        public Transform impact;
        public ParticleSystem[] impactParticles;
        public Vector3 position;
        public Vector3 direction;
        public float travelled;
        public IDamageable resolvedTarget;
        public HashSet<IDamageable> resolvedVolleyTargets;
        public bool isFlying;
    }

    private sealed class HitDistanceComparer : IComparer<RaycastHit>
    {
        public static readonly HitDistanceComparer Instance = new();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
