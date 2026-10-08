using UnityEngine;

/// <summary>
/// One frame of cinematic camera output: where the camera is, where it looks, its lens, and the
/// distance depth of field should focus at.
/// </summary>
public readonly struct CinematicCameraPose
{
    public CinematicCameraPose(Vector3 position, Quaternion rotation, float fieldOfView, float focusDistance)
    {
        Position = position;
        Rotation = rotation;
        FieldOfView = fieldOfView;
        FocusDistance = focusDistance;
    }

    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public float FieldOfView { get; }
    public float FocusDistance { get; }
}
