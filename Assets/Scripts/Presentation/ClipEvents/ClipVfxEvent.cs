using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// One authored effect in an animation clip, played at a character socket on scaled time, so
/// hit-stop and pause freeze it with the animation.
/// </summary>
[Serializable]
public sealed class ClipVfxEvent : ClipEvent
{
    [SerializeField, AssetsOnly, Required]
    [Tooltip("Effect prefab: particle systems, trail renderers, or anything else. Its particle systems switch to Hierarchy scaling so Scale applies to all of them.")]
    private GameObject prefab;
    [SerializeField, Tooltip("The character socket the effect plays at.")]
    private SocketId socket;
    [SerializeField, Tooltip("Parents the effect to the socket while it plays. Off spawns it in world space at the socket's pose.")]
    private bool followSocket = true;
    [SerializeField, Tooltip("Offset in the socket's local space, as if the effect were its child.")]
    private Vector3 positionOffset;
    [SerializeField, Tooltip("Rotation in the socket's local space, in degrees. The prefab's own rotation applies on top.")]
    private Vector3 rotationOffset;
    [SerializeField, MinValue(0.01f)]
    private float scale = 1f;
    [SerializeField, HideIf(nameof(IsTimed)), MinValue(0.1f)]
    [Tooltip("Seconds before a still-playing one-shot is stopped and returned to the pool. Catches looping prefabs.")]
    private float maxDuration = 3f;

    public GameObject Prefab => prefab;
    public SocketId Socket => socket;
    public bool FollowSocket => followSocket;
    public Vector3 PositionOffset => positionOffset;
    public Vector3 RotationOffset => rotationOffset;
    public float Scale => scale;
    public float MaxDuration => maxDuration;

    /// <summary>The world pose of the socket with this event's offsets applied.</summary>
    public Pose GetPose(Transform socketTransform)
    {
        return new Pose(
            socketTransform.TransformPoint(positionOffset),
            socketTransform.rotation * Quaternion.Euler(rotationOffset));
    }
}
