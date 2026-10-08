using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Owns normalized audio-category volume values and their PlayerPrefs-backed
/// mixer application. It intentionally has no global access point or playback
/// responsibility.
/// </summary>
public class AudioSettingsController : MonoBehaviour
{
    public const string MasterVolumeKey = "BossFightDemo.Audio.MasterVolume";
    public const string MusicVolumeKey = "BossFightDemo.Audio.MusicVolume";
    public const string SfxVolumeKey = "BossFightDemo.Audio.SFXVolume";
    public const string AmbienceVolumeKey = "BossFightDemo.Audio.AmbienceVolume";
    public const string UiVolumeKey = "BossFightDemo.Audio.UIVolume";
    public const string SFXVolumeKey = SfxVolumeKey;
    public const string UIVolumeKey = UiVolumeKey;

    public const string MasterVolumeParameter = "MasterVolume";
    public const string MusicVolumeParameter = "MusicVolume";
    public const string SfxVolumeParameter = "SFXVolume";
    public const string AmbienceVolumeParameter = "AmbienceVolume";
    public const string UiVolumeParameter = "UIVolume";
    public const string SFXVolumeParameter = SfxVolumeParameter;
    public const string UIVolumeParameter = UiVolumeParameter;

    [FoldoutGroup("Config"), SerializeField, Required]
    private AudioMixer mixer;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private bool initialized;

    private float masterVolume = 1f;
    private float musicVolume = 1f;
    private float sfxVolume = 1f;
    private float ambienceVolume = 1f;
    private float uiVolume = 1f;

    [FoldoutGroup("Volumes"), ShowInInspector, PropertyRange(0f, 1f)]
    [InfoBox("These sliders edit saved local volumes, just like the audio menu. Changes persist after Play Mode. 0 = mute, 1 = full volume.")]
    public float MasterVolume
    {
        get => Application.isPlaying ? masterVolume : Load(MasterVolumeKey);
        private set => SetMasterVolume(value);
    }

    [FoldoutGroup("Volumes"), ShowInInspector, PropertyRange(0f, 1f)]
    public float MusicVolume
    {
        get => Application.isPlaying ? musicVolume : Load(MusicVolumeKey);
        private set => SetVolume(AudioCategory.Music, value);
    }

    [FoldoutGroup("Volumes"), ShowInInspector, PropertyRange(0f, 1f), LabelText("SFX Volume")]
    public float SfxVolume
    {
        get => Application.isPlaying ? sfxVolume : Load(SfxVolumeKey);
        private set => SetVolume(AudioCategory.SFX, value);
    }

    [FoldoutGroup("Volumes"), ShowInInspector, PropertyRange(0f, 1f)]
    public float AmbienceVolume
    {
        get => Application.isPlaying ? ambienceVolume : Load(AmbienceVolumeKey);
        private set => SetVolume(AudioCategory.Ambience, value);
    }

    [FoldoutGroup("Volumes"), ShowInInspector, PropertyRange(0f, 1f), LabelText("UI Volume")]
    public float UiVolume
    {
        get => Application.isPlaying ? uiVolume : Load(UiVolumeKey);
        private set => SetVolume(AudioCategory.UI, value);
    }

    public AudioMixer Mixer => mixer;

    private void Awake()
    {
        if (!enabled || !gameObject.activeInHierarchy) return;
        Initialize();
    }

    private void OnEnable()
    {
        if (!initialized) Initialize();
        else ApplyAllVolumes();
    }

    private void Start()
    {
        ApplyAllVolumes();
    }

    public float GetMasterVolume()
    {
        return masterVolume;
    }

    public float GetVolume(AudioCategory category)
    {
        switch (category)
        {
            case AudioCategory.Music: return musicVolume;
            case AudioCategory.SFX: return sfxVolume;
            case AudioCategory.Ambience: return ambienceVolume;
            case AudioCategory.UI: return uiVolume;
            default:
                LogConfigurationError($"AudioSettingsController received unsupported AudioCategory value {category}.");
                return 1f;
        }
    }

    public float GetCategoryVolume(AudioCategory category)
    {
        return GetVolume(category);
    }

    public void SetMasterVolume(float normalizedValue)
    {
        masterVolume = Normalize(normalizedValue);
        ApplyAndPersist(masterVolume, MasterVolumeParameter, MasterVolumeKey);
    }

    public void SetVolume(AudioCategory category, float normalizedValue)
    {
        float value = Normalize(normalizedValue);
        switch (category)
        {
            case AudioCategory.Music:
                musicVolume = value;
                ApplyAndPersist(musicVolume, MusicVolumeParameter, MusicVolumeKey);
                break;
            case AudioCategory.SFX:
                sfxVolume = value;
                ApplyAndPersist(sfxVolume, SfxVolumeParameter, SfxVolumeKey);
                break;
            case AudioCategory.Ambience:
                ambienceVolume = value;
                ApplyAndPersist(ambienceVolume, AmbienceVolumeParameter, AmbienceVolumeKey);
                break;
            case AudioCategory.UI:
                uiVolume = value;
                ApplyAndPersist(uiVolume, UiVolumeParameter, UiVolumeKey);
                break;
            default:
                LogConfigurationError($"AudioSettingsController received unsupported AudioCategory value {category}.");
                break;
        }
    }

    public void SetCategoryVolume(AudioCategory category, float normalizedValue)
    {
        SetVolume(category, normalizedValue);
    }

    public static float ToDecibels(float normalizedValue)
    {
        float value = Normalize(normalizedValue);
        return value <= 0f ? -80f : 20f * Mathf.Log10(value);
    }

    private void Initialize()
    {
        masterVolume = Load(MasterVolumeKey);
        musicVolume = Load(MusicVolumeKey);
        sfxVolume = Load(SfxVolumeKey);
        ambienceVolume = Load(AmbienceVolumeKey);
        uiVolume = Load(UiVolumeKey);
        initialized = true;
        ApplyAllVolumes();
    }

    private float Load(string key)
    {
        return PlayerPrefs.HasKey(key) ? Normalize(PlayerPrefs.GetFloat(key)) : 1f;
    }

    private void ApplyAllVolumes()
    {
        ApplyVolume(masterVolume, MasterVolumeParameter);
        ApplyVolume(musicVolume, MusicVolumeParameter);
        ApplyVolume(sfxVolume, SfxVolumeParameter);
        ApplyVolume(ambienceVolume, AmbienceVolumeParameter);
        ApplyVolume(uiVolume, UiVolumeParameter);
    }

    private void ApplyAndPersist(float value, string parameter, string key)
    {
        if (Application.isPlaying)
        {
            ApplyVolume(value, parameter);
        }
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
    }

    private void ApplyVolume(float normalizedValue, string parameter)
    {
        if (mixer == null)
        {
            LogConfigurationError("AudioSettingsController has no AudioMixer assigned. Assign the scene mixer before applying volume settings.");
            return;
        }

        if (!mixer.SetFloat(parameter, ToDecibels(normalizedValue)))
        {
            LogConfigurationError($"AudioSettingsController could not set mixer parameter '{parameter}'. Expose that parameter on the assigned AudioMixer.");
            return;
        }

        if (showDebugLogs) Debug.Log($"{name} applied {parameter}.", this);
    }

    private static float Normalize(float value)
    {
        if (float.IsNaN(value) || float.IsNegativeInfinity(value)) return 0f;
        if (float.IsPositiveInfinity(value)) return 1f;
        return Mathf.Clamp01(value);
    }

    private void LogConfigurationError(string message)
    {
        Debug.LogError(message, this);
    }
}
