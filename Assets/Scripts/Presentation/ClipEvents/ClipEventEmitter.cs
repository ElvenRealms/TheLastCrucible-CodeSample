using Animancer;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Presentation-only clip events: watches the character's Animancer states and starts each
/// authored sound, VFX and camera event when a clip's playback crosses its time. Speed changes, holds and
/// loops are followed because it reads the states' actual normalized time. Timed events run as
/// spans until their end time or until the action is interrupted.
/// </summary>
public class ClipEventEmitter : MonoBehaviour
{
    // Lets an event at a state's exact start time play on the frame the state starts.
    private const float StartTolerance = 0.0001f;
    // Caps how many loop cycles one frame can play, so a long frame spike can't burst events.
    private const int MaximumCyclesPerFrame = 2;

    [FoldoutGroup("References"), SerializeField, Required]
    private AnimancerComponent animancer;
    [FoldoutGroup("References"), SerializeField, Tooltip("Places VFX events. Found on this object when empty.")]
    private CharacterSockets sockets;
    [FoldoutGroup("References"), SerializeField, Tooltip("Plays camera events. Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController feedbackController;

    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private ClipEventLibrary library;
    [FoldoutGroup("Config"), SerializeField, MinValue(1)]
    [Tooltip("Instances kept per VFX prefab before the oldest is recycled. Trails need 3 or more, so a chained swing's trail starts while the last one fades.")]
    private int vfxPoolSize = 4;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int playedEventCount;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, InlineEditor]
    private AudioCue _lastPlayedCue;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int playedVfxCount;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private GameObject lastPlayedVfx;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int activeSpanCount;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int playedCameraCount;

    private Dictionary<AnimancerState, float> _previousTimes = new();
    private Dictionary<AnimancerState, float> _currentTimes = new();
    private Transform _vfxContainer;
    private VfxPool _vfxPool;
    private ClipEventSpans _spans;
    private VfxPool.Handle _lastVfx;
    private bool _warnedMissingSockets;
    private bool _warnedMissingController;

    public int PlayedEventCount => playedEventCount;
    public int PlayedVfxCount => playedVfxCount;
    public int ActiveSpanCount => activeSpanCount;
    public int PlayedCameraCount => playedCameraCount;
    /// <summary>The most recently started VFX instance; stale once it returns to the pool.</summary>
    public VfxPool.Handle LastVfx => _lastVfx;

    private void Awake()
    {
        if (animancer == null)
        {
            animancer = GetComponentInChildren<AnimancerComponent>();
        }
        if (sockets == null)
        {
            sockets = GetComponent<CharacterSockets>();
        }
        if (feedbackController == null)
        {
            feedbackController = FindAnyObjectByType<CombatFeedbackController>();
        }

        // Idle and world-space instances live at the scene root so they don't ride the character.
        _vfxContainer = new GameObject($"{name} Clip VFX").transform;
        _vfxPool = new VfxPool(_vfxContainer, vfxPoolSize);
        _spans = new ClipEventSpans(_vfxPool);
    }

    private void OnEnable()
    {
        playedEventCount = 0;
        _lastPlayedCue = null;
        playedVfxCount = 0;
        lastPlayedVfx = null;
        activeSpanCount = 0;
        playedCameraCount = 0;
        _lastVfx = default;
        _previousTimes.Clear();
    }

    private void OnDisable()
    {
        _spans.EndAll();
        _vfxPool.ReleaseAll();
        activeSpanCount = 0;
    }

    private void OnDestroy()
    {
        if (_vfxContainer != null)
        {
            Destroy(_vfxContainer.gameObject);
        }
    }

    // Runs after the Animator has advanced this frame's states.
    private void LateUpdate()
    {
        if (animancer != null && library != null && animancer.IsGraphInitialized)
        {
            _currentTimes.Clear();
            AnimancerLayerList layers = animancer.Layers;
            for (int i = 0; i < layers.Count; i++)
            {
                UpdateLayer(layers[i]);
            }

            _spans.EndFinished(_currentTimes);
            (_previousTimes, _currentTimes) = (_currentTimes, _previousTimes);
        }

        _vfxPool.Tick();
        activeSpanCount = _spans.Count;
    }

    private void UpdateLayer(AnimancerLayer layer)
    {
        IReadOnlyIndexedList<AnimancerState> states = layer.ActiveStates;
        for (int i = 0; i < states.Count; i++)
        {
            AnimancerState state = states[i];
            // A state fading out has handed over to the next one, so its events would double up.
            if (state.TargetWeight <= 0f)
            {
                continue;
            }

            ClipEventTrack track = library.FindTrack(state.Clip);
            if (track == null)
            {
                continue;
            }

            float time = state.NormalizedTime;
            if (!_previousTimes.TryGetValue(state, out float previous) || time < previous)
            {
                // The state started or restarted this frame; this frame's advance began at its start time.
                previous = time - FrameAdvance(state) - StartTolerance;
            }

            StartCrossedEvents(state, track, previous, time);
            _currentTimes[state] = time;
        }
    }

    private void StartCrossedEvents(AnimancerState state, ClipEventTrack track, float previous, float time)
    {
        bool looping = state.IsLooping;
        int lastCycle = looping ? Mathf.FloorToInt(time) : 0;
        int firstCycle = looping ? Mathf.Max(Mathf.FloorToInt(previous), lastCycle - MaximumCyclesPerFrame + 1) : 0;
        for (int cycle = firstCycle; cycle <= lastCycle; cycle++)
        {
            foreach (var soundEvent in track.SoundEvents)
            {
                if (IsCrossed(cycle + soundEvent.Time, previous, time))
                {
                    StartSound(state, soundEvent, cycle, time);
                }
            }

            foreach (var vfxEvent in track.VfxEvents)
            {
                if (IsCrossed(cycle + vfxEvent.Time, previous, time))
                {
                    StartVfx(state, vfxEvent, cycle, time);
                }
            }

            foreach (var cameraEvent in track.CameraEvents)
            {
                if (IsCrossed(cycle + cameraEvent.Time, previous, time))
                {
                    PlayCamera(cameraEvent);
                }
            }
        }
    }

    private void StartSound(AnimancerState state, ClipSoundEvent soundEvent, int cycle, float time)
    {
        AudioVoiceHandle voice = PlayCue(soundEvent.Cue);
        if (soundEvent.IsTimed && voice != null)
        {
            _spans.AddSound(state, soundEvent, cycle, time, voice);
        }
    }

    private AudioVoiceHandle PlayCue(AudioCue cue)
    {
        if (cue == null)
        {
            return null;
        }

        AudioVoiceHandle voice = cue.SpatialMode == AudioSpatialMode.Spatial
            ? AudioPlaybackService.Instance?.PlayAtPosition(cue, transform.position)
            : AudioPlaybackService.Instance?.Play(cue);

        playedEventCount++;
        _lastPlayedCue = cue;
        if (showDebugLogs)
        {
            Debug.Log($"[ClipEventEmitter] Played '{cue.name}'.", this);
        }
        return voice;
    }

    private void StartVfx(AnimancerState state, ClipVfxEvent vfxEvent, int cycle, float time)
    {
        Transform socket = FindSocket(vfxEvent.Socket);
        if (vfxEvent.Prefab == null || socket == null)
        {
            return;
        }

        // Spans end on their own terms, so only a one-shot has a lifetime cap.
        float maxDuration = vfxEvent.IsTimed ? float.PositiveInfinity : vfxEvent.MaxDuration;
        Transform parent = vfxEvent.FollowSocket ? socket : null;
        _lastVfx = _vfxPool.Play(vfxEvent.Prefab, vfxEvent.GetPose(socket), vfxEvent.Scale, false, maxDuration, parent);
        if (vfxEvent.IsTimed)
        {
            _spans.AddVfx(state, vfxEvent, cycle, time, _lastVfx);
        }

        playedVfxCount++;
        lastPlayedVfx = vfxEvent.Prefab;
        if (showDebugLogs)
        {
            Debug.Log($"[ClipEventEmitter] Played VFX '{vfxEvent.Prefab.name}' at {vfxEvent.Socket}.", this);
        }
    }

    // The camera feels the shake, so its distance falloff is measured from the camera.
    private void PlayCamera(ClipCameraEvent cameraEvent)
    {
        if (feedbackController == null)
        {
            if (!_warnedMissingController)
            {
                _warnedMissingController = true;
                Debug.LogWarning($"{name} has camera events but no {nameof(CombatFeedbackController)}, so they are skipped.", this);
            }
            return;
        }

        Camera viewer = Camera.main;
        var request = new CombatFeedbackRequest(null, 0f, false, transform, viewer != null ? viewer.transform : null, transform.position);
        feedbackController.RequestEffect(cameraEvent.Shake, in request);
        feedbackController.RequestEffect(cameraEvent.Rumble, in request);
        feedbackController.RequestEffect(cameraEvent.Motion, in request);

        playedCameraCount++;
        if (showDebugLogs)
        {
            string shakeName = cameraEvent.Shake != null ? cameraEvent.Shake.name : "none";
            string motionName = cameraEvent.Motion != null ? cameraEvent.Motion.name : "none";
            Debug.Log($"[ClipEventEmitter] Played camera event with shake '{shakeName}' and move '{motionName}'.", this);
        }
    }

    // An unresolved socket skips the event; CharacterSockets warns once per socket.
    private Transform FindSocket(SocketId id)
    {
        if (sockets != null)
        {
            return sockets.Get(id);
        }

        if (!_warnedMissingSockets)
        {
            _warnedMissingSockets = true;
            Debug.LogWarning($"{name} has VFX events but no {nameof(CharacterSockets)}, so they are skipped.", this);
        }
        return null;
    }

    private static bool IsCrossed(float eventTime, float previous, float time)
    {
        return eventTime > previous && eventTime <= time;
    }

    private static float FrameAdvance(AnimancerState state)
    {
        float length = state.Length;
        return length > 0f ? state.EffectiveSpeed * Time.deltaTime / length : 0f;
    }
}
