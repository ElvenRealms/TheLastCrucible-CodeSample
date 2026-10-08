using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Authored identity and fixed playback settings for one attached persistent sound.
/// Runtime playback state is owned by <see cref="AudioLoopPlayer"/>.
/// </summary>
[CreateAssetMenu(fileName = "AudioLoop", menuName = "BossFight/Audio Loop")]
public class AudioLoopData : ScriptableObject
{
    [BoxGroup("Loop"), SerializeField, Required, AssetsOnly]
    private AudioClip clip;
    [BoxGroup("Loop"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(IsVolumeValid), "volume must stay within 0..1.")]
    private float volume = 1f;
    [BoxGroup("Loop"), SerializeField, MinValue(0.01f)]
    [ValidateInput(nameof(IsPitchValid), "pitch must be finite and greater than zero.")]
    private float pitch = 1f;

    [BoxGroup("Spatial"), SerializeField]
    private AudioSpatialMode spatialMode = AudioSpatialMode.TwoD;
    [BoxGroup("Spatial"), SerializeField, MinValue(0f)]
    [ValidateInput(nameof(AreDistancesValid), "Distances must be finite, positive, and ordered.")]
    private float minimumDistance = 1f;
    [BoxGroup("Spatial"), SerializeField, MinValue(0f)]
    [ValidateInput(nameof(AreDistancesValid), "Distances must be finite, positive, and ordered.")]
    private float maximumDistance = 20f;
    [BoxGroup("Spatial"), SerializeField]
    private AudioRolloffType rolloff = AudioRolloffType.Logarithmic;

    public AudioClip Clip => clip;
    public float Volume => volume;
    public float Pitch => pitch;
    public AudioSpatialMode SpatialMode => spatialMode;
    public float MinimumDistance => minimumDistance;
    public float MaximumDistance => maximumDistance;
    public AudioRolloffType Rolloff => rolloff;

    private bool IsVolumeValid => IsFinite(volume) && volume >= 0f && volume <= 1f;
    private bool IsPitchValid => IsFinite(pitch) && pitch > 0f;
    private bool IsSpatialModeValid => spatialMode == AudioSpatialMode.TwoD || spatialMode == AudioSpatialMode.Spatial;
    private bool AreDistancesValid => IsFinite(minimumDistance) && IsFinite(maximumDistance)
        && minimumDistance > 0f && maximumDistance >= minimumDistance;
    private bool IsRolloffValid => rolloff == AudioRolloffType.Logarithmic || rolloff == AudioRolloffType.Linear;

    internal bool TryGetRuntimeValidationError(out string error)
    {
        if (clip == null)
        {
            error = "AudioLoopData requires an AudioClip.";
            return false;
        }

        if (!IsVolumeValid)
        {
            error = "AudioLoopData volume must be finite and stay within 0..1.";
            return false;
        }

        if (!IsPitchValid)
        {
            error = "AudioLoopData pitch must be finite and greater than zero.";
            return false;
        }

        if (!IsSpatialModeValid)
        {
            error = "AudioLoopData spatialMode is invalid.";
            return false;
        }

        if (!AreDistancesValid)
        {
            error = "AudioLoopData distances must be finite, positive, and maximumDistance must not be below minimumDistance.";
            return false;
        }

        if (!IsRolloffValid)
        {
            error = "AudioLoopData rolloff is invalid.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
