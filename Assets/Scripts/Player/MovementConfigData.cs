using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Tuning data for the player movement controller.
/// </summary>
[CreateAssetMenu(fileName = "MovementConfig", menuName = "BossFight/Movement Config")]
public class MovementConfigData : ScriptableObject
{
    [BoxGroup("Speed"), SerializeField, SuffixLabel("m/s"), MinValue(0)]
    private float walkSpeed = 4f;
    [BoxGroup("Speed"), SerializeField, SuffixLabel("m/s"), MinValue(0)]
    private float sprintSpeed = 7f;
    [BoxGroup("Speed"), SerializeField, SuffixLabel("m/s"), MinValue(0)]
    [Tooltip("Planar speed while the guard is raised.")]
    private float guardSpeed = 0.9f;
    [BoxGroup("Speed"), SerializeField, SuffixLabel("m/s²"), MinValue(0)]
    private float speedChangeRate = 25f;

    [BoxGroup("Rotation"), SerializeField, SuffixLabel("deg/s"), MinValue(0)]
    private float rotationSpeed = 720f;
    [BoxGroup("Rotation"), SerializeField, SuffixLabel("deg/s"), MinValue(0)]
    [Tooltip("Turn speed toward the camera-relative move input during an attack's tracking window while not locked on. Zero disables steering.")]
    private float attackSteeringSpeed = 360f;

    [BoxGroup("Gravity"), SerializeField, SuffixLabel("m/s²")]
    private float gravity = -25f;
    [BoxGroup("Gravity"), SerializeField, SuffixLabel("m/s"), MinValue(0)]
    private float maxFallSpeed = 30f;
    [BoxGroup("Gravity"), SerializeField, SuffixLabel("m/s")]
    [PropertyTooltip("Constant downward velocity while grounded so the controller stays snapped to the ground.")]
    private float groundedGravity = -2f;

    public float WalkSpeed => walkSpeed;
    public float SprintSpeed => sprintSpeed;
    public float GuardSpeed => guardSpeed;
    public float SpeedChangeRate => speedChangeRate;
    public float RotationSpeed => rotationSpeed;
    public float AttackSteeringSpeed => attackSteeringSpeed;
    public float Gravity => gravity;
    public float MaxFallSpeed => maxFallSpeed;
    public float GroundedGravity => groundedGravity;
}
