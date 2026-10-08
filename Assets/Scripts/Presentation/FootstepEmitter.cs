using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Presentation-only footsteps: watches the Humanoid foot bones and plays a step
/// wherever a foot comes down after being lifted, so any animation (locomotion,
/// attacks, turns) produces steps without authored events. Optionally kicks a small
/// camera impulse per step for heavy characters.
/// </summary>
[RequireComponent(typeof(Animator))]
public class FootstepEmitter : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly, InlineEditor]
    private AudioCue stepCue;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f)]
    [Tooltip("Foot bone height above the root, in metres, that counts as planted. Tune from the Debug height range while walking.")]
    private float plantHeight = 0.12f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f)]
    [Tooltip("Foot bone height above the root, in metres, the foot must rise past before its next plant counts as a step.")]
    private float liftHeight = 0.2f;

    [FoldoutGroup("Camera"), SerializeField]
    [Tooltip("Optional. Leave empty for no camera tremor.")]
    private CinemachineImpulseSource stepImpulse;
    [FoldoutGroup("Camera"), SerializeField, MinValue(0f)]
    private float impulseForce = 0.08f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private float leftFootHeight;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private float rightFootHeight;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    [Tooltip("Lowest and highest foot heights seen this session.")]
    private Vector2 observedHeightRange;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int stepCount;

    private Foot _left;
    private Foot _right;

    public int StepCount => stepCount;

    private sealed class Foot
    {
        public Transform bone;
        public bool lifted;
    }

    private void Awake()
    {
        Animator animator = GetComponent<Animator>();
        Transform leftBone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        Transform rightBone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightFoot) : null;
        if (leftBone == null || rightBone == null)
        {
            Debug.LogWarning("[FootstepEmitter] Could not resolve both foot bones from a Humanoid Animator; disabling.", this);
            enabled = false;
            return;
        }

        _left = new Foot { bone = leftBone };
        _right = new Foot { bone = rightBone };
        observedHeightRange = new Vector2(float.MaxValue, float.MinValue);
    }

    private void LateUpdate()
    {
        // Hit-stop and pause freeze the pose; skipping keeps a frozen foot from re-triggering.
        if (Time.deltaTime <= 0f)
        {
            return;
        }

        leftFootHeight = UpdateFoot(_left);
        rightFootHeight = UpdateFoot(_right);
    }

    private float UpdateFoot(Foot foot)
    {
        float height = foot.bone.position.y - transform.position.y;
        observedHeightRange = new Vector2(Mathf.Min(observedHeightRange.x, height), Mathf.Max(observedHeightRange.y, height));

        if (!foot.lifted)
        {
            foot.lifted = height > liftHeight;
        }
        else if (height < plantHeight)
        {
            foot.lifted = false;
            Step(foot.bone.position);
        }

        return height;
    }

    private void Step(Vector3 position)
    {
        stepCount++;
        if (stepCue != null)
        {
            AudioPlaybackService.Instance?.PlayAtPosition(stepCue, position);
        }

        if (stepImpulse != null && impulseForce > 0f)
        {
            stepImpulse.GenerateImpulseAtPositionWithVelocity(position, Vector3.down * impulseForce);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[FootstepEmitter] Step {stepCount} at {position}.", this);
        }
    }
}
