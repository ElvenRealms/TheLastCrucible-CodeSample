using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Authored identity and variation settings for one finite audio one-shot.
/// Runtime selection state is owned by <see cref="AudioPlaybackService"/>.
/// </summary>
[CreateAssetMenu(fileName = "AudioCue", menuName = "BossFight/Audio Cue")]
public class AudioCue : ScriptableObject
{
    [BoxGroup("Cue"), SerializeField, ValidateInput(nameof(ClipsValid), "clips must contain at least one non-null AudioClip.")]
    private AudioClip[] clips;
    [BoxGroup("Cue"), SerializeField]
    private AudioCategory category = AudioCategory.SFX;
    [BoxGroup("Cue"), SerializeField]
    private AudioSpatialMode spatialMode = AudioSpatialMode.TwoD;

    [BoxGroup("Variation"), SerializeField, MinMaxSlider(0f, 1f, true), ValidateInput(nameof(VolumeRangeValid), "volumeRange must be ordered and stay within 0..1.")]
    private Vector2 volumeRange = Vector2.one;
    [BoxGroup("Variation"), SerializeField, MinMaxSlider(0.01f, 3f, true), ValidateInput(nameof(PitchRangeValid), "pitchRange must be ordered and greater than zero.")]
    private Vector2 pitchRange = Vector2.one;
    [BoxGroup("Variation"), SerializeField]
    private AudioCueSelectionMode selectionMode = AudioCueSelectionMode.Random;
    [BoxGroup("Variation"), SerializeField, MinValue(0), Tooltip("Zero allows unlimited simultaneous voices for this cue.")]
    private int maxSimultaneousInstances;
    [BoxGroup("Variation"), SerializeField, MinValue(0f), Tooltip("Zero allows unrestricted retriggering.")]
    private float minRetriggerInterval;

    [BoxGroup("Spatial"), SerializeField, MinValue(0f), ValidateInput(nameof(SpatialDistanceValid), "Spatial cues require positive ordered distances.")]
    private float minimumDistance = 1f;
    [BoxGroup("Spatial"), SerializeField, MinValue(0f), ValidateInput(nameof(SpatialDistanceValid), "Spatial cues require positive ordered distances.")]
    private float maximumDistance = 20f;
    [BoxGroup("Spatial"), SerializeField]
    private AudioRolloffType rolloff = AudioRolloffType.Logarithmic;

    public AudioClip[] Clips => clips;
    public AudioCategory Category => category;
    public AudioSpatialMode SpatialMode => spatialMode;
    public Vector2 VolumeRange => volumeRange;
    public Vector2 PitchRange => pitchRange;
    public AudioCueSelectionMode SelectionMode => selectionMode;
    public int MaxSimultaneousInstances => maxSimultaneousInstances;
    public float MinRetriggerInterval => minRetriggerInterval;
    public float MinimumDistance => minimumDistance;
    public float MaximumDistance => maximumDistance;
    public AudioRolloffType Rolloff => rolloff;

    private bool ClipsValid => CountValidClips() > 0;
    private bool VolumeRangeValid => IsFinite(volumeRange.x) && IsFinite(volumeRange.y)
        && volumeRange.x >= 0f && volumeRange.y <= 1f && volumeRange.x <= volumeRange.y;
    private bool PitchRangeValid => IsFinite(pitchRange.x) && IsFinite(pitchRange.y)
        && pitchRange.x > 0f && pitchRange.y > 0f && pitchRange.x <= pitchRange.y;
    private bool SpatialDistanceValid => spatialMode != AudioSpatialMode.Spatial
        || (IsFinite(minimumDistance) && IsFinite(maximumDistance)
            && minimumDistance > 0f && maximumDistance >= minimumDistance);

    internal int CountValidClips()
    {
        if (clips == null) return 0;

        int count = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null) count++;
        }

        return count;
    }

    internal void CollectValidClips(List<AudioClip> destination)
    {
        if (destination == null) return;
        destination.Clear();
        if (clips == null) return;

        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = clips[i];
            if (clip != null) destination.Add(clip);
        }
    }

    internal bool TryGetRuntimeValidationError(out string error)
    {
        if (CountValidClips() == 0)
        {
            error = "AudioCue must contain at least one non-null AudioClip.";
            return false;
        }

        if (!VolumeRangeValid)
        {
            error = "AudioCue volumeRange must be ordered and stay within 0..1.";
            return false;
        }

        if (!PitchRangeValid)
        {
            error = "AudioCue pitchRange must be ordered and greater than zero.";
            return false;
        }

        if (maxSimultaneousInstances < 0)
        {
            error = "AudioCue maxSimultaneousInstances cannot be negative.";
            return false;
        }

        if (!IsFinite(minRetriggerInterval) || minRetriggerInterval < 0f)
        {
            error = "AudioCue minRetriggerInterval must be finite and non-negative.";
            return false;
        }

        if (!SpatialDistanceValid)
        {
            error = "Spatial AudioCue distances must be finite, positive, and maximumDistance must not be below minimumDistance.";
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
