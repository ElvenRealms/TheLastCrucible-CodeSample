using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pools effect prefabs for one owner: plays instances at a pose, returns each to the pool once it
/// has finished or its maximum duration passes, and recycles a prefab's oldest instance when all of
/// them are playing. Particle systems finish when their particles die, trail renderers when their
/// trail fades, and a prefab with neither (a light, a mesh flash) when it is stopped. The owner calls <see cref="Tick"/> every frame and
/// <see cref="ReleaseAll"/> when it is disabled.
/// </summary>
public sealed class VfxPool
{
    private readonly Transform _container;
    private readonly int _sizePerPrefab;
    private readonly Dictionary<GameObject, List<PooledInstance>> _pools = new();
    private readonly List<PooledInstance> _active = new();
    private int _playCount;

    /// <param name="container">Parent of idle instances, and of playing ones given no parent.</param>
    /// <param name="sizePerPrefab">Instances kept per prefab before the oldest is recycled.</param>
    public VfxPool(Transform container, int sizePerPrefab)
    {
        _container = container;
        _sizePerPrefab = Mathf.Max(1, sizePerPrefab);
    }

    public int ActiveCount => _active.Count;

    /// <summary>
    /// Plays a prefab at a world pose. The prefab's own rotation applies on top of the pose's, and its
    /// own scale is multiplied by <paramref name="scale"/> as a local scale under the parent.
    /// </summary>
    /// <param name="useUnscaledTime">Runs the particles, and the maximum duration, on unscaled time. Trail renderers always fade on scaled time.</param>
    /// <param name="maxDuration">Seconds before a still-playing instance is stopped and returned.</param>
    /// <param name="parent">Carries the instance while it plays; the pool's container when null.</param>
    public Handle Play(GameObject prefab, Pose pose, float scale, bool useUnscaledTime, float maxDuration,
        Transform parent = null)
    {
        PooledInstance instance = Rent(prefab);
        Transform root = instance.root.transform;
        Transform prefabTransform = prefab.transform;
        root.SetParent(parent != null ? parent : _container, false);
        root.SetPositionAndRotation(pose.position, pose.rotation * prefabTransform.rotation);
        root.localScale = prefabTransform.localScale * scale;

        foreach (var system in instance.systems)
        {
            ParticleSystem.MainModule main = system.main;
            main.useUnscaledTime = useUnscaledTime;
        }

        instance.root.SetActive(true);
        foreach (var system in instance.rootSystems)
        {
            system.Play(true);
        }

        // Clearing after the move keeps a trail from streaking in from where the instance last played.
        foreach (var trail in instance.trails)
        {
            trail.Clear();
            trail.emitting = true;
        }

        instance.isPlaying = true;
        instance.isStopped = false;
        instance.playOrder = ++_playCount;
        instance.useUnscaledTime = useUnscaledTime;
        instance.stopAt = Now(useUnscaledTime) + maxDuration;
        _active.Add(instance);
        return new Handle(instance, instance.playOrder);
    }

    /// <summary>
    /// Stops a playing instance from emitting; it returns to the pool once its particles die and its
    /// trails fade. Does nothing when the handle has gone stale.
    /// </summary>
    public void StopEmitting(Handle handle)
    {
        if (!handle.IsPlaying)
        {
            return;
        }

        foreach (var system in handle.Instance.rootSystems)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        foreach (var trail in handle.Instance.trails)
        {
            if (trail != null)
            {
                trail.emitting = false;
            }
        }

        handle.Instance.isStopped = true;
        handle.Instance.trailsFadeAt = Time.time + handle.Instance.trailTime;
    }

    /// <summary>Returns instances that have finished or whose maximum duration has passed.</summary>
    public void Tick()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            PooledInstance instance = _active[i];
            if (instance.root != null && Now(instance.useUnscaledTime) < instance.stopAt && IsAlive(instance))
            {
                continue;
            }

            Release(instance);
        }
    }

    /// <summary>Stops and returns every playing instance.</summary>
    public void ReleaseAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Release(_active[i]);
        }
    }

    private PooledInstance Rent(GameObject prefab)
    {
        if (!_pools.TryGetValue(prefab, out List<PooledInstance> pool))
        {
            pool = new List<PooledInstance>();
            _pools.Add(prefab, pool);
        }

        pool.RemoveAll(instance => instance.root == null);

        PooledInstance oldest = null;
        foreach (var instance in pool)
        {
            if (!instance.isPlaying)
            {
                return instance;
            }

            if (oldest == null || instance.playOrder < oldest.playOrder)
            {
                oldest = instance;
            }
        }

        if (pool.Count < _sizePerPrefab)
        {
            PooledInstance created = Create(prefab);
            pool.Add(created);
            return created;
        }

        Release(oldest);
        return oldest;
    }

    private PooledInstance Create(GameObject prefab)
    {
        GameObject root = Object.Instantiate(prefab, _container);
        root.name = prefab.name;
        root.SetActive(false);

        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var system in systems)
        {
            ParticleSystem.MainModule main = system.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;
            // A ring buffer keeps its last particles alive until replaced, so the instance would never come back.
            main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
        }

        TrailRenderer[] trails = root.GetComponentsInChildren<TrailRenderer>(true);
        float trailTime = 0f;
        foreach (var trail in trails)
        {
            trailTime = Mathf.Max(trailTime, trail.time);
        }

        return new PooledInstance
        {
            root = root,
            systems = systems,
            rootSystems = FindRootSystems(root, systems),
            trails = trails,
            trailTime = trailTime
        };
    }

    /// <summary>
    /// The topmost particle systems of an effect. Playing a sub-emitter or nested system directly would
    /// make it emit on its own, so only these are driven and they cascade to their children.
    /// </summary>
    public static ParticleSystem[] FindRootSystems(GameObject root, ParticleSystem[] systems)
    {
        var rootSystems = new List<ParticleSystem>();
        foreach (var system in systems)
        {
            Transform parent = system.transform.parent;
            bool nested = false;
            while (parent != null && parent != root.transform.parent)
            {
                if (parent.TryGetComponent<ParticleSystem>(out _))
                {
                    nested = true;
                    break;
                }

                parent = parent.parent;
            }

            if (!nested)
            {
                rootSystems.Add(system);
            }
        }

        return rootSystems.ToArray();
    }

    private static bool IsAlive(PooledInstance instance)
    {
        // Nothing here ends on its own, so the effect lasts until it is stopped or times out.
        if (instance.systems.Length == 0 && instance.trails.Length == 0)
        {
            return !instance.isStopped;
        }

        foreach (var system in instance.rootSystems)
        {
            if (system != null && system.IsAlive(true))
            {
                return true;
            }
        }

        // A trail's points only expire when it renders, so its fade is timed from the stop instead.
        return instance.trails.Length > 0 && (!instance.isStopped || Time.time < instance.trailsFadeAt);
    }

    private void Release(PooledInstance instance)
    {
        _active.Remove(instance);
        instance.isPlaying = false;
        if (instance.root == null)
        {
            return;
        }

        foreach (var system in instance.rootSystems)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        foreach (var trail in instance.trails)
        {
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }
        }

        instance.root.SetActive(false);
        // Unity refuses reparenting while the parent's hierarchy is being deactivated. An instance
        // left under an inactive parent stays idle there until Play reparents it.
        Transform parent = instance.root.transform.parent;
        if (parent != _container && parent != null && parent.gameObject.activeInHierarchy)
        {
            instance.root.transform.SetParent(_container, false);
        }
    }

    private static float Now(bool unscaled) => unscaled ? Time.unscaledTime : Time.time;

    /// <summary>
    /// Refers to one play of a pooled instance. It goes stale once that instance is returned or
    /// recycled, so a late stop can't cut off the instance's next effect.
    /// </summary>
    public readonly struct Handle
    {
        private readonly int _playOrder;

        internal Handle(PooledInstance instance, int playOrder)
        {
            Instance = instance;
            _playOrder = playOrder;
        }

        public bool IsPlaying => Instance != null && Instance.root != null && Instance.isPlaying &&
            Instance.playOrder == _playOrder;

        /// <summary>The playing instance's root, or null once the handle has gone stale.</summary>
        public Transform Transform => IsPlaying ? Instance.root.transform : null;

        internal PooledInstance Instance { get; }
    }

    internal sealed class PooledInstance
    {
        public GameObject root;
        public ParticleSystem[] systems;
        public ParticleSystem[] rootSystems;
        public TrailRenderer[] trails;
        public float trailTime;
        public float trailsFadeAt;
        public bool isPlaying;
        public bool isStopped;
        public int playOrder;
        public bool useUnscaledTime;
        public float stopAt;
    }
}
