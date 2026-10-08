using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Scene-local playback authority for one continuous, non-spatial ambience loop.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class AmbienceDirector : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private AmbienceLoop ambienceLoop;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f)]
    [ValidateInput(nameof(FadeInDurationValid), "fadeInDuration must be finite and non-negative.")]
    private float fadeInDuration = 2f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;

    private readonly List<AudioClip> validClipBuffer = new();
    private AudioSource ambienceSource;
    private Tween fadeTween;

    private bool FadeInDurationValid => IsFinite(fadeInDuration) && fadeInDuration >= 0f;

    private void Awake()
    {
        ambienceSource = GetComponent<AudioSource>();
        if (ambienceSource != null)
        {
            ambienceSource.playOnAwake = false;
        }
    }

    private void OnEnable()
    {
        if (ambienceSource == null)
        {
            ambienceSource = GetComponent<AudioSource>();
        }

        StopPlayback();
        if (!TryStartPlayback())
        {
            return;
        }
    }

    private void OnDisable()
    {
        StopPlayback();
    }

    private void OnDestroy()
    {
        KillFadeTween();
    }

    private bool TryStartPlayback()
    {
        if (ambienceSource == null)
        {
            LogConfigurationError("AmbienceDirector requires a colocated AudioSource.");
            return false;
        }

        if (ambienceLoop == null)
        {
            LogConfigurationError("AmbienceDirector has no AmbienceLoop assigned.");
            return false;
        }

        if (!ambienceLoop.TryGetRuntimeValidationError(out string validationError))
        {
            LogConfigurationError($"AmbienceLoop '{ambienceLoop.name}' is invalid: {validationError}");
            return false;
        }

        if (!FadeInDurationValid)
        {
            LogConfigurationError("AmbienceDirector fadeInDuration must be finite and non-negative.");
            return false;
        }

        if (ambienceSource.outputAudioMixerGroup == null)
        {
            LogConfigurationError(
                "AmbienceDirector's AudioSource has no mixer group assigned. "
                + "Assign the Ambience mixer group in the Inspector.");
            return false;
        }

        ambienceLoop.CollectValidClips(validClipBuffer);
        if (validClipBuffer.Count == 0)
        {
            LogConfigurationError($"AmbienceLoop '{ambienceLoop.name}' has no valid AudioClip to play.");
            return false;
        }

        AudioClip selectedClip = SelectClip(validClipBuffer);
        if (selectedClip == null)
        {
            LogConfigurationError($"AmbienceLoop '{ambienceLoop.name}' could not select a valid AudioClip.");
            return false;
        }

        ConfigureSource(selectedClip);
        StartFadeIn();

        if (showDebugLogs)
        {
            Debug.Log($"{name} started AmbienceLoop '{ambienceLoop.name}' using clip '{selectedClip.name}'.", this);
        }

        return true;
    }

    private void ConfigureSource(AudioClip selectedClip)
    {
        ambienceSource.playOnAwake = false;
        ambienceSource.loop = true;
        ambienceSource.spatialBlend = 0f;
        ambienceSource.clip = selectedClip;
        ambienceSource.volume = 0f;
        ambienceSource.pitch = ambienceLoop.Pitch;
        ambienceSource.time = Random.Range(0f, selectedClip.length);
        ambienceSource.Play();
    }

    private void StartFadeIn()
    {
        if (fadeInDuration <= 0f)
        {
            ambienceSource.volume = ambienceLoop.Volume;
            return;
        }

        fadeTween = DOTween.To(
                () => ambienceSource.volume,
                value => ambienceSource.volume = value,
                ambienceLoop.Volume,
                fadeInDuration)
            .SetTarget(this)
            .SetEase(Ease.Linear)
            .SetUpdate(true);
    }

    private void StopPlayback()
    {
        KillFadeTween();
        validClipBuffer.Clear();

        if (ambienceSource == null)
        {
            return;
        }

        ambienceSource.playOnAwake = false;
        ambienceSource.Stop();
        ambienceSource.clip = null;
        ambienceSource.volume = 0f;
    }

    private void KillFadeTween()
    {
        if (fadeTween != null)
        {
            fadeTween.Kill();
        }

        fadeTween = null;
    }

    private static AudioClip SelectClip(List<AudioClip> validClips)
    {
        if (validClips == null || validClips.Count == 0)
        {
            return null;
        }

        return validClips[Random.Range(0, validClips.Count)];
    }

    private void LogConfigurationError(string message)
    {
        Debug.LogError(message, this);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
