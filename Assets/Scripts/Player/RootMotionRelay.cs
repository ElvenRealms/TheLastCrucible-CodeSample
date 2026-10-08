using UnityEngine;

/// <summary>
/// Forwards root motion from the Animator on this object to the state machine,
/// so states decide whether to consume it. Defining OnAnimatorMove also stops
/// the animator from moving the transform directly, which would bypass the
/// CharacterController.
/// </summary>
[RequireComponent(typeof(Animator))]
public class RootMotionRelay : MonoBehaviour
{
    private PlayerStateMachine machine;
    private Animator animator;

    private void Awake()
    {
        machine = GetComponentInParent<PlayerStateMachine>();
        animator = GetComponent<Animator>();
    }

    private void OnAnimatorMove()
    {
        machine.OnRootMotion(animator.deltaPosition);
    }
}
