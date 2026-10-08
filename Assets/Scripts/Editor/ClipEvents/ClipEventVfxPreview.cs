using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Shows a clip's VFX events on the Clip Events preview model. Each effect rides its socket, and its
/// particles are simulated in step with the animation from the event's start up to the playhead, so
/// scrubbing shows the effect as it would look on that frame. Timed events stop emitting at their end
/// time; one-shots disappear after their maximum duration. Particle systems replay exactly; trail
/// renderers and lights only show at the playhead's pose.
/// </summary>
public sealed class ClipEventVfxPreview
{
    private const float StepSeconds = 1f / 60f;
    private const int MaximumSteps = 1200;

    private readonly List<Entry> _entries = new();
    private GameObject _model;
    private int _layoutSignature;
    private int _timingSignature;
    // The playhead the effects were last simulated to, or negative when they need a restart.
    private float _simulatedTime = -1f;

    /// <summary>Removes every preview effect.</summary>
    public void Clear()
    {
        foreach (var entry in _entries)
        {
            if (entry.instance != null)
            {
                Object.DestroyImmediate(entry.instance);
            }
        }

        _entries.Clear();
        _model = null;
        _simulatedTime = -1f;
    }

    /// <summary>
    /// Brings the effects to the playhead: continues from the last simulated time when playback moved
    /// forward, and resimulates from the earliest started event otherwise. Leaves the model posed at
    /// the playhead.
    /// </summary>
    public void Update(AnimationClipPreview preview, AnimationClip clip, ClipEventTrack track, float playhead)
    {
        GameObject model = preview.PreviewInstance;
        if (model == null || preview.PreviewRoot == null || track == null || !AnimationClipPreview.IsSampleable(clip))
        {
            Clear();
            return;
        }

        IReadOnlyList<ClipVfxEvent> events = track.VfxEvents;
        int layoutSignature = LayoutSignature(clip, events);
        if (model != _model || layoutSignature != _layoutSignature || HasDestroyedInstance())
        {
            Build(preview, model, events);
            _layoutSignature = layoutSignature;
        }

        int timingSignature = TimingSignature(events);
        if (timingSignature != _timingSignature)
        {
            _timingSignature = timingSignature;
            _simulatedTime = -1f;
        }

        if (_entries.Count == 0)
        {
            return;
        }

        float from = _simulatedTime;
        if (from < 0f || playhead < from)
        {
            Restart();
            from = EarliestStart(playhead);
        }

        if (from >= 0f)
        {
            Step(preview, clip, from, playhead);
        }
        _simulatedTime = playhead;
        preview.SamplePose(playhead);
    }

    private void Build(AnimationClipPreview preview, GameObject model, IReadOnlyList<ClipVfxEvent> events)
    {
        Clear();
        _model = model;
        var sockets = model.GetComponentInChildren<CharacterSockets>(true);
        foreach (var vfxEvent in events)
        {
            if (vfxEvent.Prefab == null)
            {
                continue;
            }

            // A model without sockets still shows the effect, at its root.
            Transform socket = model.transform;
            if (sockets != null && !sockets.TryGet(vfxEvent.Socket, out socket))
            {
                continue;
            }

            GameObject instance = Object.Instantiate(vfxEvent.Prefab, preview.PreviewRoot);
            instance.name = vfxEvent.Prefab.name;
            SetHideFlagsRecursively(instance.transform);
            instance.SetActive(false);

            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            var emissionEnabled = new bool[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                // A fixed seed replays the same particles every time the playhead is scrubbed.
                system.useAutoRandomSeed = false;
                ParticleSystem.MainModule main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.stopAction = ParticleSystemStopAction.None;
                main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
                emissionEnabled[i] = system.emission.enabled;
            }

            _entries.Add(new Entry
            {
                clipEvent = vfxEvent,
                instance = instance,
                socket = socket,
                systems = systems,
                rootSystems = VfxPool.FindRootSystems(instance, systems),
                emissionEnabled = emissionEnabled
            });
        }
    }

    private void Restart()
    {
        foreach (var entry in _entries)
        {
            entry.started = false;
            entry.stopped = false;
            entry.expired = false;
            for (int i = 0; i < entry.systems.Length; i++)
            {
                ParticleSystem.EmissionModule emission = entry.systems[i].emission;
                emission.enabled = entry.emissionEnabled[i];
            }
            entry.instance.SetActive(false);
        }
    }

    // Steps the pose and every started effect together, so trails sweep and world-space particles
    // stay where they were emitted.
    private void Step(AnimationClipPreview preview, AnimationClip clip, float from, float to)
    {
        float length = clip.length;
        float stepSize = Mathf.Max(StepSeconds / length, (to - from) / MaximumSteps);
        int steps = Mathf.CeilToInt((to - from) / stepSize);
        for (int i = 0; i <= steps; i++)
        {
            float time = i == steps ? to : from + i * stepSize;
            preview.SamplePose(time);
            foreach (var entry in _entries)
            {
                if (!entry.started && entry.clipEvent.Time <= time)
                {
                    Begin(entry);
                }
                if (entry.started && !entry.expired)
                {
                    Advance(entry, time, length);
                }
            }
        }
    }

    private static void Begin(Entry entry)
    {
        ClipVfxEvent vfxEvent = entry.clipEvent;
        Transform root = entry.instance.transform;
        Transform prefabTransform = vfxEvent.Prefab.transform;
        Pose pose = vfxEvent.GetPose(entry.socket);
        if (vfxEvent.FollowSocket)
        {
            root.SetParent(entry.socket, false);
        }
        root.SetPositionAndRotation(pose.position, pose.rotation * prefabTransform.rotation);
        root.localScale = prefabTransform.localScale * vfxEvent.Scale;

        entry.instance.SetActive(true);
        foreach (var system in entry.rootSystems)
        {
            system.Simulate(0f, true, true, false);
        }
        entry.started = true;
        entry.simulatedTo = vfxEvent.Time;
    }

    private static void Advance(Entry entry, float time, float clipLength)
    {
        ClipVfxEvent vfxEvent = entry.clipEvent;
        if (vfxEvent.IsTimed && !entry.stopped && time >= vfxEvent.EndTime)
        {
            Simulate(entry, (vfxEvent.EndTime - entry.simulatedTo) * clipLength);
            StopEmitting(entry);
            entry.simulatedTo = vfxEvent.EndTime;
        }

        Simulate(entry, (time - entry.simulatedTo) * clipLength);
        entry.simulatedTo = time;

        if (!vfxEvent.IsTimed && (time - vfxEvent.Time) * clipLength >= vfxEvent.MaxDuration)
        {
            entry.expired = true;
            entry.instance.SetActive(false);
        }
    }

    private static void Simulate(Entry entry, float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        foreach (var system in entry.rootSystems)
        {
            system.Simulate(seconds, true, false, false);
        }
    }

    // Disabling emission rather than stopping keeps the simulated particles advancing on later steps.
    private static void StopEmitting(Entry entry)
    {
        foreach (var system in entry.systems)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
        }
        foreach (var trail in entry.instance.GetComponentsInChildren<TrailRenderer>(true))
        {
            trail.emitting = false;
        }
        entry.stopped = true;
    }

    private float EarliestStart(float playhead)
    {
        float earliest = -1f;
        foreach (var entry in _entries)
        {
            float time = entry.clipEvent.Time;
            if (time <= playhead && (earliest < 0f || time < earliest))
            {
                earliest = time;
            }
        }
        return earliest;
    }

    private bool HasDestroyedInstance()
    {
        foreach (var entry in _entries)
        {
            if (entry.instance == null)
            {
                return true;
            }
        }
        return false;
    }

    // Anything that changes which effects exist or where they sit; a change rebuilds the instances.
    private static int LayoutSignature(AnimationClip clip, IReadOnlyList<ClipVfxEvent> events)
    {
        var hash = new HashCode();
        hash.Add(clip);
        foreach (var vfxEvent in events)
        {
            hash.Add(vfxEvent.Prefab);
            hash.Add(vfxEvent.Socket);
            hash.Add(vfxEvent.FollowSocket);
            hash.Add(vfxEvent.PositionOffset);
            hash.Add(vfxEvent.RotationOffset);
            hash.Add(vfxEvent.Scale);
        }
        return hash.ToHashCode();
    }

    // Timing only restarts the simulation, so dragging an event doesn't rebuild its instance.
    private static int TimingSignature(IReadOnlyList<ClipVfxEvent> events)
    {
        var hash = new HashCode();
        foreach (var vfxEvent in events)
        {
            hash.Add(vfxEvent.Time);
            hash.Add(vfxEvent.IsTimed);
            hash.Add(vfxEvent.EndTime);
            hash.Add(vfxEvent.MaxDuration);
        }
        return hash.ToHashCode();
    }

    private static void SetHideFlagsRecursively(Transform root)
    {
        root.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        foreach (Transform child in root)
        {
            SetHideFlagsRecursively(child);
        }
    }

    private sealed class Entry
    {
        public ClipVfxEvent clipEvent;
        public GameObject instance;
        public Transform socket;
        public ParticleSystem[] systems;
        public ParticleSystem[] rootSystems;
        public bool[] emissionEnabled;
        public bool started;
        public bool stopped;
        public bool expired;
        public float simulatedTo;
    }
}
