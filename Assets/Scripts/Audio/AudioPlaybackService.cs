using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Scene-owned pooled playback for finite audio cues.
/// </summary>
[DisallowMultipleComponent]
public class AudioPlaybackService : MonoBehaviour
{
    private sealed class CueRuntimeState
    {
        public AudioClip lastSelectedClip;
        public float lastPlayTime;
        public bool hasPlayed;
        public readonly AudioShuffleBag shuffleBag = new();
    }

    private sealed class AudioVoice
    {
        public GameObject gameObject;
        public AudioSource source;
        public AudioCue cue;
        public AudioVoiceHandle handle;
        public long startOrder;
        public int generation;
        public int startFrame;
        public bool active;
    }

    private static AudioPlaybackService instance;

    [FoldoutGroup("Config"), SerializeField, Min(0), Tooltip("Number of one-shot voices created before the first request.")]
    private int initialPoolSize = 16;

    [FoldoutGroup("Routing"), SerializeField]
    private AudioMixerGroup musicMixerGroup;
    [FoldoutGroup("Routing"), SerializeField]
    private AudioMixerGroup sfxMixerGroup;
    [FoldoutGroup("Routing"), SerializeField]
    private AudioMixerGroup ambienceMixerGroup;
    [FoldoutGroup("Routing"), SerializeField]
    private AudioMixerGroup uiMixerGroup;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int activeVoiceCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int poolVoiceCount;

    private readonly List<AudioVoice> voices = new();
    private readonly Dictionary<AudioCue, CueRuntimeState> cueStates = new();
    private readonly List<AudioClip> validClipBuffer = new();
    private long nextStartOrder;
    private bool ownsInstance;

    /// <summary>
    /// Gets the scene-owned service. Missing configuration is reported instead
    /// of constructing an implicit fallback service.
    /// </summary>
    public static AudioPlaybackService Instance
    {
        get
        {
            if (instance == null)
            {
                Debug.LogError("AudioPlaybackService.Instance was requested, but no enabled AudioPlaybackService exists in the active scene. Add and enable the scene audio root.");
            }

            return instance;
        }
    }

    public static bool HasInstance => instance != null;
    public int InitialPoolSize => initialPoolSize;
    public int VoiceCount => voices.Count;
    public int ActiveVoiceCount => activeVoiceCount;
    public int IdleVoiceCount => voices.Count - activeVoiceCount;

    private void Awake()
    {
        if (!enabled || !gameObject.activeInHierarchy) return;
        if (!TryClaimInstance()) return;
        EnsurePool();
    }

    private void OnEnable()
    {
        if (!TryClaimInstance()) return;
        EnsurePool();
    }

    private void Update()
    {
        if (!ownsInstance) return;
        RefreshVoices();
    }

    private void OnDisable()
    {
        if (instance != this) return;

        StopAllVoices();
        instance = null;
        ownsInstance = false;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            StopAllVoices();
            instance = null;
            ownsInstance = false;
        }

        DestroyVoiceObjects();
    }

    /// <summary>
    /// Plays a two-dimensional one-shot.
    /// </summary>
    public AudioVoiceHandle Play(AudioCue cue)
    {
        return PlayInternal(cue, Vector3.zero, false);
    }

    /// <summary>
    /// Plays a spatial one-shot at the supplied world position. The pooled
    /// source captures this position and does not track a caller transform.
    /// </summary>
    public AudioVoiceHandle PlayAtPosition(AudioCue cue, Vector3 position)
    {
        return PlayInternal(cue, position, true);
    }

    internal bool IsHandleValid(AudioVoiceHandle handle)
    {
        RefreshVoices();
        AudioVoice voice = GetHandleVoice(handle);
        return voice != null && voice.active;
    }

    internal bool IsHandlePlaying(AudioVoiceHandle handle)
    {
        RefreshVoices();
        AudioVoice voice = GetHandleVoice(handle);
        return voice != null && voice.active && voice.source != null && voice.source.isPlaying;
    }

    internal bool StopHandle(AudioVoiceHandle handle)
    {
        RefreshVoices();
        AudioVoice voice = GetHandleVoice(handle);
        if (voice == null || !voice.active) return false;

        RecycleVoice(voice);
        return true;
    }

    private AudioVoiceHandle PlayInternal(AudioCue cue, Vector3 position, bool spatialRequest)
    {
        if (!ownsInstance || !isActiveAndEnabled)
        {
            LogConfigurationError("AudioPlaybackService received a playback request while it is disabled or is not the scene owner.");
            return null;
        }

        RefreshVoices();

        if (cue == null)
        {
            LogConfigurationError("AudioPlaybackService received a null AudioCue.");
            return null;
        }

        if (!cue.TryGetRuntimeValidationError(out string validationError))
        {
            LogConfigurationError($"AudioCue '{cue.name}' is invalid: {validationError}");
            return null;
        }

        AudioSpatialMode expectedMode = spatialRequest ? AudioSpatialMode.Spatial : AudioSpatialMode.TwoD;
        if (cue.SpatialMode != expectedMode)
        {
            LogConfigurationError($"AudioCue '{cue.name}' uses {cue.SpatialMode} mode, but {nameof(AudioPlaybackService)} received a {(spatialRequest ? "spatial" : "2D")} request.");
            return null;
        }

        CueRuntimeState cueState = GetCueState(cue);
        float playTime = GetPlayTime(cue);
        if (cueState.hasPlayed && cue.MinRetriggerInterval > 0f
            && playTime - cueState.lastPlayTime < cue.MinRetriggerInterval)
        {
            return null;
        }

        cue.CollectValidClips(validClipBuffer);
        if (validClipBuffer.Count == 0)
        {
            LogConfigurationError($"AudioCue '{cue.name}' has no valid AudioClip to play.");
            return null;
        }

        AudioMixerGroup mixerGroup = GetMixerGroup(cue.Category);
        if (mixerGroup == null)
        {
            LogConfigurationError($"AudioPlaybackService has no mixer group assigned for AudioCategory.{cue.Category}. Assign the category group on the scene audio root.");
            return null;
        }

        AudioClip selectedClip = cue.SelectionMode == AudioCueSelectionMode.ShuffleBag
            ? SelectShuffleBagClip(validClipBuffer, cueState)
            : SelectClip(validClipBuffer, cueState.lastSelectedClip);
        if (selectedClip == null)
        {
            LogConfigurationError($"AudioCue '{cue.name}' could not select a valid AudioClip.");
            return null;
        }

        if (cue.MaxSimultaneousInstances > 0
            && CountActiveVoices(cue) >= cue.MaxSimultaneousInstances)
        {
            AudioVoice oldestVoice = FindOldestVoice(cue);
            if (oldestVoice != null) RecycleVoice(oldestVoice);
        }

        AudioVoice voice = RentVoice();
        ConfigureSource(voice, cue, selectedClip, position, mixerGroup);

        voice.cue = cue;
        voice.startOrder = ++nextStartOrder;
        voice.generation++;
        voice.active = true;
        voice.startFrame = Time.frameCount;
        voice.handle = new AudioVoiceHandle(this, voices.IndexOf(voice), voice.generation);
        voice.source.Play();

        cueState.lastSelectedClip = selectedClip;
        cueState.lastPlayTime = playTime;
        cueState.hasPlayed = true;
        UpdateDebugCounts();

        if (showDebugLogs)
        {
            Debug.Log($"{name} played AudioCue '{cue.name}' using category {cue.Category}.", this);
        }

        return voice.handle;
    }

    private bool TryClaimInstance()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError($"Duplicate AudioPlaybackService '{name}' was disabled. The scene already owns '{instance.name}'.", this);
            enabled = false;
            return false;
        }

        instance = this;
        ownsInstance = true;
        return true;
    }

    private void EnsurePool()
    {
        if (initialPoolSize < 0)
        {
            LogConfigurationError("AudioPlaybackService initialPoolSize cannot be negative.");
            initialPoolSize = 0;
        }

        while (voices.Count < initialPoolSize) CreateVoice();
        UpdateDebugCounts();
    }

    private AudioVoice CreateVoice()
    {
        GameObject voiceObject = new($"Audio Voice {voices.Count + 1}");
        voiceObject.transform.SetParent(transform, false);
        AudioSource source = voiceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        AudioVoice voice = new AudioVoice
        {
            gameObject = voiceObject,
            source = source
        };
        ResetSource(source);
        voices.Add(voice);
        return voice;
    }

    private AudioVoice RentVoice()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            if (!voices[i].active) return voices[i];
        }

        return CreateVoice();
    }

    private void ConfigureSource(AudioVoice voice, AudioCue cue, AudioClip clip, Vector3 position, AudioMixerGroup mixerGroup)
    {
        AudioSource source = voice.source;
        ResetSource(source);
        source.transform.SetParent(null, true);
        source.outputAudioMixerGroup = mixerGroup;
        source.clip = clip;
        source.volume = Random.Range(cue.VolumeRange.x, cue.VolumeRange.y);
        source.pitch = Random.Range(cue.PitchRange.x, cue.PitchRange.y);
        source.loop = false;

        if (cue.SpatialMode == AudioSpatialMode.Spatial)
        {
            source.spatialBlend = 1f;
            source.minDistance = cue.MinimumDistance;
            source.maxDistance = cue.MaximumDistance;
            source.rolloffMode = cue.Rolloff == AudioRolloffType.Linear
                ? UnityEngine.AudioRolloffMode.Linear
                : UnityEngine.AudioRolloffMode.Logarithmic;
            source.transform.position = position;
        }
        else
        {
            source.spatialBlend = 0f;
            source.transform.localPosition = Vector3.zero;
        }
    }

    private void RefreshVoices()
    {
        for (int i = voices.Count - 1; i >= 0; i--)
        {
            AudioVoice voice = voices[i];
            if (!voice.active) continue;
            if (voice.source == null)
            {
                RecycleVoice(voice);
                continue;
            }
            if (Time.frameCount <= voice.startFrame) continue;
            if (!voice.source.isPlaying) RecycleVoice(voice);
        }

        UpdateDebugCounts();
    }

    private void RecycleVoice(AudioVoice voice)
    {
        if (voice == null || !voice.active) return;

        if (voice.source != null) ResetSource(voice.source);
        voice.active = false;
        voice.cue = null;
        voice.startOrder = 0;
        voice.handle = null;
        voice.generation++;
        UpdateDebugCounts();
    }

    private void StopAllVoices()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            RecycleVoice(voices[i]);
        }

        cueStates.Clear();
        UpdateDebugCounts();
    }

    private void DestroyVoiceObjects()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i].gameObject == null) continue;
            if (Application.isPlaying) Destroy(voices[i].gameObject);
            else DestroyImmediate(voices[i].gameObject);
        }

        voices.Clear();
        cueStates.Clear();
        UpdateDebugCounts();
    }

    private void ResetSource(AudioSource source)
    {
        if (source == null) return;

        source.Stop();
        source.clip = null;
        source.outputAudioMixerGroup = null;
        source.volume = 1f;
        source.pitch = 1f;
        source.loop = false;
        source.spatialBlend = 0f;
        source.minDistance = 1f;
        source.maxDistance = 500f;
        source.rolloffMode = UnityEngine.AudioRolloffMode.Logarithmic;
        source.panStereo = 0f;
        source.dopplerLevel = 1f;
        source.spread = 0f;
        source.reverbZoneMix = 1f;
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = false;
        source.spatialize = false;
        source.spatializePostEffects = false;
        source.ignoreListenerVolume = false;
        source.transform.localPosition = Vector3.zero;
        source.transform.localRotation = Quaternion.identity;
        source.transform.localScale = Vector3.one;
    }

    private CueRuntimeState GetCueState(AudioCue cue)
    {
        if (!cueStates.TryGetValue(cue, out CueRuntimeState state))
        {
            state = new CueRuntimeState();
            cueStates.Add(cue, state);
        }

        return state;
    }

    private static AudioClip SelectClip(List<AudioClip> validClips, AudioClip previous)
    {
        if (validClips == null || validClips.Count == 0) return null;

        int index = Random.Range(0, validClips.Count);
        if (validClips.Count > 1 && validClips[index] == previous)
        {
            index = (index + Random.Range(1, validClips.Count)) % validClips.Count;
        }

        return validClips[index];
    }

    private static AudioClip SelectShuffleBagClip(List<AudioClip> validClips, CueRuntimeState cueState)
    {
        if (cueState == null || !cueState.shuffleBag.TryTake(validClips, cueState.lastSelectedClip, out AudioClip selectedClip))
        {
            return null;
        }

        return selectedClip;
    }

    private AudioVoice FindOldestVoice(AudioCue cue)
    {
        AudioVoice oldest = null;
        for (int i = 0; i < voices.Count; i++)
        {
            AudioVoice voice = voices[i];
            if (!voice.active || voice.cue != cue) continue;
            if (oldest == null || voice.startOrder < oldest.startOrder) oldest = voice;
        }

        return oldest;
    }

    private static float GetPlayTime(AudioCue cue)
    {
        return cue.Category == AudioCategory.UI ? Time.unscaledTime : Time.time;
    }

    private int CountActiveVoices(AudioCue cue)
    {
        int count = 0;
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i].active && voices[i].cue == cue) count++;
        }

        return count;
    }

    private AudioMixerGroup GetMixerGroup(AudioCategory category)
    {
        switch (category)
        {
            case AudioCategory.Music: return musicMixerGroup;
            case AudioCategory.SFX: return sfxMixerGroup;
            case AudioCategory.Ambience: return ambienceMixerGroup;
            case AudioCategory.UI: return uiMixerGroup;
            default: return null;
        }
    }

    private AudioVoice GetHandleVoice(AudioVoiceHandle handle)
    {
        if (handle == null || handle.Service != this || !ownsInstance || !isActiveAndEnabled) return null;
        if (handle.VoiceIndex < 0 || handle.VoiceIndex >= voices.Count) return null;

        AudioVoice voice = voices[handle.VoiceIndex];
        return voice.generation == handle.Generation ? voice : null;
    }

    private void UpdateDebugCounts()
    {
        int active = 0;
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i].active) active++;
        }

        activeVoiceCount = active;
        poolVoiceCount = voices.Count;
    }

    private void LogConfigurationError(string message)
    {
        Debug.LogError(message, this);
    }
}
