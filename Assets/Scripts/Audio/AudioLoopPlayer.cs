using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Plays one authored persistent sound through a dedicated colocated AudioSource.
/// Starts automatically when configured, or lets callers control playback through
/// <see cref="Play"/>, <see cref="Stop"/>, <see cref="FadeIn"/> and <see cref="FadeOut"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class AudioLoopPlayer : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private AudioLoopData loopData;
    [FoldoutGroup("Config"), SerializeField]
    [Tooltip("Start when enabled. Waits until the next enable if no clip is assigned.")]
    private bool playOnEnable;

    private AudioSource _loopSource;
    private Tween _fadeTween;

    private void Awake()
    {
        AudioSource source = GetLoopSource();
        if (source != null)
        {
            source.playOnAwake = false;
        }
    }

    private void OnEnable()
    {
        if (playOnEnable && loopData != null && loopData.Clip != null)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        Stop();
    }

    private void OnDestroy()
    {
        _fadeTween?.Kill();
    }

    public void Play()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        AudioSource source = GetLoopSource();
        if (source == null)
        {
            LogConfigurationError("AudioLoopPlayer requires a colocated AudioSource.");
            return;
        }

        if (source.isPlaying)
        {
            return;
        }

        if (loopData == null)
        {
            LogConfigurationError("AudioLoopPlayer has no AudioLoopData assigned.");
            return;
        }

        if (!loopData.TryGetRuntimeValidationError(out string validationError))
        {
            LogConfigurationError($"AudioLoopData '{loopData.name}' is invalid: {validationError}");
            return;
        }

        if (source.outputAudioMixerGroup == null)
        {
            LogConfigurationError(
                "AudioLoopPlayer's AudioSource has no mixer group assigned. "
                + "Assign the appropriate mixer group in the Inspector.");
            return;
        }

        ConfigureSource(source);
        source.Play();
    }

    public void Stop()
    {
        _fadeTween?.Kill();
        AudioSource source = GetLoopSource();
        if (source == null)
        {
            return;
        }

        source.Stop();
    }

    /// <summary>
    /// Starts the loop from its beginning at silence and fades up to its authored volume
    /// over unscaled seconds. A loop that is already playing fades back up from where it is.
    /// </summary>
    public void FadeIn(float seconds)
    {
        _fadeTween?.Kill();
        AudioSource source = GetLoopSource();
        if (source == null || loopData == null)
        {
            return;
        }

        if (!source.isPlaying)
        {
            Play();
            if (!source.isPlaying)
            {
                return;
            }

            source.volume = 0f;
        }

        _fadeTween = FadeVolume(source, loopData.Volume, seconds);
    }

    /// <summary>Fades the loop to silence over unscaled seconds, then stops it.</summary>
    public void FadeOut(float seconds)
    {
        _fadeTween?.Kill();
        AudioSource source = GetLoopSource();
        if (source == null || !source.isPlaying)
        {
            return;
        }

        _fadeTween = FadeVolume(source, 0f, seconds).OnComplete(source.Stop);
    }

    // Unscaled so hit-stop, pause and victory slow motion don't stretch the fade.
    private Tween FadeVolume(AudioSource source, float target, float seconds)
    {
        return DOTween.To(() => source.volume, value => source.volume = value, target, Mathf.Max(0f, seconds))
            .SetUpdate(true)
            .SetTarget(this);
    }

    private AudioSource GetLoopSource()
    {
        if (_loopSource == null)
        {
            _loopSource = GetComponent<AudioSource>();
        }

        return _loopSource;
    }

    private void ConfigureSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.clip = loopData.Clip;
        source.loop = true;
        source.volume = loopData.Volume;
        source.pitch = loopData.Pitch;
        source.spatialBlend = loopData.SpatialMode == AudioSpatialMode.Spatial ? 1f : 0f;
        source.minDistance = loopData.MinimumDistance;
        source.maxDistance = loopData.MaximumDistance;
        source.rolloffMode = loopData.Rolloff == AudioRolloffType.Linear
            ? UnityEngine.AudioRolloffMode.Linear
            : UnityEngine.AudioRolloffMode.Logarithmic;
    }

    private void LogConfigurationError(string message)
    {
        Debug.LogError(message, this);
    }
}
