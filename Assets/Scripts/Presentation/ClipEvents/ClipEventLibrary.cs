using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A character's clip events, keyed by clip, so a clip plays the same events wherever
/// it is used. Author the events in Tools/Boss Fight/Clip Events.
/// </summary>
[CreateAssetMenu(fileName = "ClipEvents", menuName = "BossFight/Clip Event Library")]
public class ClipEventLibrary : ScriptableObject
{
    [SerializeField, ListDrawerSettings(ListElementLabelName = nameof(ClipEventTrack.InspectorLabel))]
    [ValidateInput(nameof(AreClipsUnique), "Each clip can have only one track.")]
    private List<ClipEventTrack> tracks = new();

    [NonSerialized]
    private Dictionary<AnimationClip, ClipEventTrack> _tracksByClip;

    public IReadOnlyList<ClipEventTrack> Tracks => tracks;

    private bool AreClipsUnique
    {
        get
        {
            var clips = new HashSet<AnimationClip>();
            foreach (var track in tracks)
            {
                if (track.Clip != null && !clips.Add(track.Clip))
                {
                    return false;
                }
            }
            return true;
        }
    }

    private void OnValidate()
    {
        _tracksByClip = null;
    }

    /// <summary>Finds the track authored for a clip, or null when the clip has no events.</summary>
    public ClipEventTrack FindTrack(AnimationClip clip)
    {
        if (clip == null)
        {
            return null;
        }

        _tracksByClip ??= BuildLookup();
        return _tracksByClip.TryGetValue(clip, out ClipEventTrack track) ? track : null;
    }

    private Dictionary<AnimationClip, ClipEventTrack> BuildLookup()
    {
        var lookup = new Dictionary<AnimationClip, ClipEventTrack>();
        foreach (var track in tracks)
        {
            if (track.Clip != null && track.HasEvents)
            {
                lookup.TryAdd(track.Clip, track);
            }
        }
        return lookup;
    }
}
