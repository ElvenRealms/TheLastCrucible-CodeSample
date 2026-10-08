using UnityEngine;

/// <summary>
/// Passive scene marker for the player's respawn pose.
/// </summary>
public class RespawnPoint : MonoBehaviour
{
    public Vector3 Position => transform.position;
    public Quaternion Rotation => transform.rotation;
}
