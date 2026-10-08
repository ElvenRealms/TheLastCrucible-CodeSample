using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Low-level movement for the player: gravity, grounding, rotation, and the
/// CharacterController.Move call. States decide intent and call exactly one
/// Move method per frame.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool isGrounded;

    public bool IsGrounded => isGrounded;

    private MovementConfigData config;
    private CharacterController controller;
    private float verticalVelocity;
    private CombatBodyConstraint _bodyConstraint;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        _bodyConstraint = new CombatBodyConstraint(controller);
    }

    private void OnEnable()
    {
        _bodyConstraint.Register();
    }

    private void OnDisable()
    {
        _bodyConstraint?.Unregister();
    }

    // PlayerStateMachine owns the config asset and injects it in Awake.
    public void Initialize(MovementConfigData movementConfig)
    {
        config = movementConfig;
    }

    public void Move(Vector3 planarVelocity)
    {
        UpdateGravity();
        controller.Move(_bodyConstraint.Constrain((planarVelocity + Vector3.up * verticalVelocity) * Time.deltaTime));
    }

    public void MoveWithRootMotion(Vector3 deltaPosition)
    {
        UpdateGravity();
        controller.Move(_bodyConstraint.Constrain(deltaPosition + Vector3.up * (verticalVelocity * Time.deltaTime)));
    }

    public void RotateToward(Vector3 direction)
    {
        RotateToward(direction, config.RotationSpeed);
    }

    public void RotateToward(Vector3 direction, float degreesPerSecond)
    {
        if (direction == Vector3.zero) return;
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            Mathf.Max(0f, degreesPerSecond) * Time.deltaTime);
    }

    public void FaceInstantly(Vector3 direction)
    {
        if (direction == Vector3.zero) return;
        transform.rotation = Quaternion.LookRotation(direction);
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        bool wasEnabled = controller.enabled;
        if (wasEnabled) controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (wasEnabled) controller.enabled = true;
        verticalVelocity = 0f;
        isGrounded = false;
    }

    private void UpdateGravity()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = config.GroundedGravity;
            return;
        }
        verticalVelocity = Mathf.Max(
            verticalVelocity + config.Gravity * Time.deltaTime,
            -config.MaxFallSpeed);
    }
}
