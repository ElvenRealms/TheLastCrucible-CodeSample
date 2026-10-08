using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// Maps each <see cref="SocketId"/> to one of the character's transforms, so clip events place
/// effects without knowing the rig. An assigned Transform always wins. Otherwise Root is this
/// component's transform, and the body sockets resolve from the humanoid Animator's bones.
/// Weapon and WeaponTip have no bone and must be assigned.
/// </summary>
[DisallowMultipleComponent]
public class CharacterSockets : MonoBehaviour
{
    private static readonly int SocketCount = Enum.GetValues(typeof(SocketId)).Length;

    [FoldoutGroup("References"), SerializeField]
    [Tooltip("Humanoid Animator whose bones fill the unassigned body sockets. Found in children when empty.")]
    private Animator animator;

    [FoldoutGroup("Config"), SerializeField, Tooltip("Leave empty to use this component's transform.")]
    private Transform root;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Leave empty to use the Animator's Chest bone, or Spine when the rig has no chest.")]
    private Transform chest;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Leave empty to use the Animator's Head bone.")]
    private Transform head;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Leave empty to use the Animator's LeftHand bone.")]
    private Transform leftHand;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Leave empty to use the Animator's RightHand bone.")]
    private Transform rightHand;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Leave empty to use the Animator's LeftFoot bone.")]
    private Transform leftFoot;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Leave empty to use the Animator's RightFoot bone.")]
    private Transform rightFoot;
    [FoldoutGroup("Config"), SerializeField, Required, Tooltip("The weapon's root, which trails and weapon glows follow.")]
    private Transform weapon;
    [FoldoutGroup("Config"), SerializeField, Required, Tooltip("The weapon's striking end, such as a blade tip or mace head.")]
    private Transform weaponTip;

    private Transform[] _resolved;
    private bool[] _warned;

    private void OnValidate()
    {
        _resolved = null;
    }

    /// <summary>
    /// Returns the socket's Transform, or null with a one-time warning per socket when it can't be resolved.
    /// </summary>
    public Transform Get(SocketId id)
    {
        if (TryGet(id, out Transform socket))
        {
            return socket;
        }

        _warned ??= new bool[SocketCount];
        if (!_warned[(int)id])
        {
            _warned[(int)id] = true;
            Debug.LogWarning($"{name} has no Transform for socket {id}. Assign it on {nameof(CharacterSockets)}.", this);
        }
        return null;
    }

    /// <summary>Finds the socket's Transform without warning when it can't be resolved.</summary>
    public bool TryGet(SocketId id, out Transform socket)
    {
        _resolved ??= ResolveAll();
        socket = _resolved[(int)id];
        return socket != null;
    }

    private Transform[] ResolveAll()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        var resolved = new Transform[SocketCount];
        for (int i = 0; i < SocketCount; i++)
        {
            var id = (SocketId)i;
            Transform assigned = Assigned(id);
            resolved[i] = assigned != null ? assigned : Resolve(id);
        }
        return resolved;
    }

    private Transform Assigned(SocketId id)
    {
        return id switch
        {
            SocketId.Root => root,
            SocketId.Chest => chest,
            SocketId.Head => head,
            SocketId.LeftHand => leftHand,
            SocketId.RightHand => rightHand,
            SocketId.LeftFoot => leftFoot,
            SocketId.RightFoot => rightFoot,
            SocketId.Weapon => weapon,
            SocketId.WeaponTip => weaponTip,
            _ => null
        };
    }

    private Transform Resolve(SocketId id)
    {
        return id switch
        {
            SocketId.Root => transform,
            SocketId.Chest => Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine),
            SocketId.Head => Bone(HumanBodyBones.Head),
            SocketId.LeftHand => Bone(HumanBodyBones.LeftHand),
            SocketId.RightHand => Bone(HumanBodyBones.RightHand),
            SocketId.LeftFoot => Bone(HumanBodyBones.LeftFoot),
            SocketId.RightFoot => Bone(HumanBodyBones.RightFoot),
            _ => null
        };
    }

    // Returns a true null rather than a destroyed Transform so ?? falls through correctly.
    private Transform Bone(HumanBodyBones bone)
    {
        if (animator == null || !animator.isHuman)
        {
            return null;
        }

        Transform boneTransform = animator.GetBoneTransform(bone);
        return boneTransform != null ? boneTransform : null;
    }
}
