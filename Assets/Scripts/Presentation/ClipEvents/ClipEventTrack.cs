using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The events authored for one animation clip, one list per lane, each kept in start-time order.
/// </summary>
[Serializable]
public sealed class ClipEventTrack
{
    [SerializeField, AssetsOnly, Required]
    private AnimationClip clip;
    [SerializeField, ListDrawerSettings(ShowFoldout = false), FormerlySerializedAs("events")]
    [ValidateInput(nameof(IsOrdered), "Sound events must stay in time order.")]
    private List<ClipSoundEvent> soundEvents = new();
    [SerializeField, ListDrawerSettings(ShowFoldout = false)]
    [ValidateInput(nameof(IsOrdered), "VFX events must stay in time order.")]
    private List<ClipVfxEvent> vfxEvents = new();
    [SerializeField, ListDrawerSettings(ShowFoldout = false)]
    [ValidateInput(nameof(IsOrdered), "Camera events must stay in time order.")]
    private List<ClipCameraEvent> cameraEvents = new();

    public ClipEventTrack()
    {
    }

    public ClipEventTrack(AnimationClip clip)
    {
        this.clip = clip;
    }

    public AnimationClip Clip => clip;
    public IReadOnlyList<ClipSoundEvent> SoundEvents => soundEvents;
    public IReadOnlyList<ClipVfxEvent> VfxEvents => vfxEvents;
    public IReadOnlyList<ClipCameraEvent> CameraEvents => cameraEvents;
    public int EventCount => soundEvents.Count + vfxEvents.Count + cameraEvents.Count;
    public bool HasEvents => EventCount > 0;
    public string InspectorLabel => clip != null ? clip.name : "Unassigned";

    private static bool IsOrdered(IReadOnlyList<ClipEvent> events)
    {
        for (int i = 1; i < events.Count; i++)
        {
            if (events[i].Time < events[i - 1].Time)
            {
                return false;
            }
        }
        return true;
    }
}
