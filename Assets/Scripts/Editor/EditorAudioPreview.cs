using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Auditions audio cues in Edit Mode through the Editor's internal clip preview, which plays without
/// an AudioSource in the scene. Volume and pitch variation are not applied.
/// </summary>
public static class EditorAudioPreview
{
    private static readonly Type AudioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    private static readonly MethodInfo PlayClipMethod = AudioUtilType?.GetMethod(
        "PlayPreviewClip",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new[] { typeof(AudioClip), typeof(int), typeof(bool) },
        null);
    private static readonly MethodInfo StopAllMethod = AudioUtilType?.GetMethod(
        "StopAllPreviewClips",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        Type.EmptyTypes,
        null);

    /// <summary>True when this Editor version exposes the clip preview.</summary>
    public static bool IsAvailable => PlayClipMethod != null;

    /// <summary>Plays one of the cue's clips, chosen at random.</summary>
    public static void Play(AudioCue cue)
    {
        if (cue == null || !IsAvailable || cue.Clips == null)
        {
            return;
        }

        AudioClip clip = PickClip(cue.Clips);
        if (clip != null)
        {
            PlayClipMethod.Invoke(null, new object[] { clip, 0, false });
        }
    }

    /// <summary>Stops every clip the preview is playing; the preview can't stop just one.</summary>
    public static void StopAll()
    {
        StopAllMethod?.Invoke(null, null);
    }

    private static AudioClip PickClip(AudioClip[] clips)
    {
        int validCount = 0;
        foreach (AudioClip clip in clips)
        {
            if (clip != null)
            {
                validCount++;
            }
        }

        int pick = UnityEngine.Random.Range(0, validCount);
        foreach (AudioClip clip in clips)
        {
            if (clip != null && pick-- == 0)
            {
                return clip;
            }
        }
        return null;
    }
}
