using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// One procedural camera angle for the <see cref="CinematicDirector"/>. The camera sits on an orbit
/// around an anchor, measured from the fight axis (player toward boss), so the angle stays meaningful
/// however the fighters move.
/// </summary>
[Serializable]
public class CinematicShot
{
    public enum Subject
    {
        Player,
        Boss,
        Midpoint
    }

    [SerializeField]
    private string name = "Shot";
    [SerializeField, MinValue(0f)]
    [Tooltip("Relative chance of the director picking this shot.")]
    private float weight = 1f;
    [SerializeField, MinMaxSlider(1f, 20f, true), SuffixLabel("s")]
    [Tooltip("Real-time length range; the director picks a length inside it on each cut.")]
    private Vector2 duration = new Vector2(4f, 7f);

    [FoldoutGroup("Placement"), SerializeField]
    [Tooltip("Point the camera orbits.")]
    private Subject anchor = Subject.Midpoint;
    [FoldoutGroup("Placement"), SerializeField, Range(-180f, 180f), SuffixLabel("deg")]
    [Tooltip("Orbit angle from the fight axis. 0 sits ahead of the anchor toward the boss, 180 behind it toward the player, 90 to the side.")]
    private float yawFromAxis = 90f;
    [FoldoutGroup("Placement"), SerializeField, Range(-30f, 30f), SuffixLabel("deg/s")]
    [Tooltip("Orbit drift per real second. Runs while time is frozen, for bullet-time orbits.")]
    private float orbitSpeed;
    [FoldoutGroup("Placement"), SerializeField, MinValue(0.5f), SuffixLabel("m")]
    private float distance = 7f;
    [FoldoutGroup("Placement"), SerializeField, MinValue(0f)]
    [Tooltip("Extra distance per metre between the fighters, so both stay in frame as they separate.")]
    private float distancePerSeparation = 0.5f;
    [FoldoutGroup("Placement"), SerializeField, Range(-1f, 15f), SuffixLabel("m")]
    [Tooltip("Camera height above the anchor's feet.")]
    private float height = 2f;

    [FoldoutGroup("Framing"), SerializeField]
    private Subject lookAt = Subject.Midpoint;
    [FoldoutGroup("Framing"), SerializeField, Range(0f, 4f), SuffixLabel("m")]
    [Tooltip("Aim height above the look subject's feet.")]
    private float lookHeight = 1.3f;
    [FoldoutGroup("Framing"), SerializeField, Range(10f, 90f), SuffixLabel("deg")]
    private float fieldOfView = 40f;
    [FoldoutGroup("Framing"), SerializeField, Range(0f, 3f), SuffixLabel("deg")]
    [Tooltip("Amplitude of the handheld wobble added to the aim.")]
    private float handheld = 0.4f;

    public string Name => name;
    public float Weight => weight;
    public float FieldOfView => fieldOfView;
    public float Handheld => handheld;

    public float PickDuration()
    {
        return UnityEngine.Random.Range(duration.x, duration.y);
    }

    /// <summary>
    /// Camera position and aim point for this shot, before smoothing and collision.
    /// </summary>
    public CinematicFraming Frame(Vector3 player, Vector3 boss, float axisYaw, float elapsed)
    {
        Vector3 planarSeparation = boss - player;
        planarSeparation.y = 0f;

        float yaw = axisYaw + yawFromAxis + orbitSpeed * elapsed;
        Vector3 orbitDirection = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        float orbitDistance = distance + planarSeparation.magnitude * distancePerSeparation;

        Vector3 position = Resolve(anchor, player, boss) + orbitDirection * orbitDistance + Vector3.up * height;
        Vector3 aim = Resolve(lookAt, player, boss) + Vector3.up * lookHeight;
        return new CinematicFraming(position, aim);
    }

    private static Vector3 Resolve(Subject subject, Vector3 player, Vector3 boss)
    {
        switch (subject)
        {
            case Subject.Player:
                return player;
            case Subject.Boss:
                return boss;
            default:
                return Vector3.Lerp(player, boss, 0.5f);
        }
    }
}

/// <summary>
/// Where a cinematic camera sits and the point it aims at.
/// </summary>
public readonly struct CinematicFraming
{
    public CinematicFraming(Vector3 position, Vector3 aim)
    {
        Position = position;
        Aim = aim;
    }

    public Vector3 Position { get; }
    public Vector3 Aim { get; }
}
