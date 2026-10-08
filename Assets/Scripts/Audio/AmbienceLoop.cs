using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Authored identity and fixed playback settings for one persistent ambience loop.
/// Runtime selection state is owned by <see cref="AmbienceDirector"/>.
/// </summary>
[CreateAssetMenu(fileName = "AmbienceLoop", menuName = "BossFight/Ambience Loop")]
public class AmbienceLoop : ScriptableObject
{
    [BoxGroup("Loop"), SerializeField]
    [ValidateInput(nameof(ClipsValid), "clips must contain at least one non-null AudioClip.")]
    private AudioClip[] clips;
    [BoxGroup("Loop"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(VolumeValid), "volume must stay within 0..1.")]
    private float volume = 1f;
    [BoxGroup("Loop"), SerializeField, MinValue(0.01f)]
    [ValidateInput(nameof(PitchValid), "pitch must be finite and greater than zero.")]
    private float pitch = 1f;

    public float Volume => volume;
    public float Pitch => pitch;

    private bool ClipsValid => CountValidClips() > 0;
    private bool VolumeValid => IsFinite(volume) && volume >= 0f && volume <= 1f;
    private bool PitchValid => IsFinite(pitch) && pitch > 0f;

    internal int CountValidClips()
    {
        if (clips == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    internal void CollectValidClips(List<AudioClip> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        if (clips == null)
        {
            return;
        }

        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = clips[i];
            if (clip != null)
            {
                destination.Add(clip);
            }
        }
    }

    internal bool TryGetRuntimeValidationError(out string error)
    {
        if (CountValidClips() == 0)
        {
            error = "AmbienceLoop must contain at least one non-null AudioClip.";
            return false;
        }

        if (!VolumeValid)
        {
            error = "AmbienceLoop volume must be finite and stay within 0..1.";
            return false;
        }

        if (!PitchValid)
        {
            error = "AmbienceLoop pitch must be finite and greater than zero.";
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
