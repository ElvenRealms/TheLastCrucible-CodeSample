using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Observes or restores one clean pose before applying ordered offsets, then records the final write.
/// Frozen Animator frames reuse the clean pose instead of treating an earlier offset as animation.
/// </summary>
public sealed class AdditiveBonePose
{
    private readonly BonePose[] _bones;

    private sealed class BonePose
    {
        public Transform bone;
        public Quaternion clean;
        public Quaternion written;
        public bool offsetApplied;
    }

    public AdditiveBonePose(params Transform[][] groups)
    {
        var unique = new HashSet<Transform>();
        foreach (var group in groups)
        {
            foreach (var bone in group)
            {
                if (bone != null)
                {
                    unique.Add(bone);
                }
            }
        }
        var poses = new List<BonePose>();
        foreach (var bone in unique)
        {
            poses.Add(new BonePose { bone = bone });
        }
        poses.Sort((a, b) => Depth(a.bone).CompareTo(Depth(b.bone)));
        _bones = poses.ToArray();
    }

    public void Apply(Transform[] bones, Quaternion offset)
    {
        Apply(bones, offset, Array.Empty<Transform>(), Quaternion.identity);
    }

    public void Apply(Transform[] postureBones, Quaternion postureOffset, Transform[] recoilBones, Quaternion recoilOffset)
    {
        foreach (var pose in _bones)
        {
            if (pose.bone == null)
            {
                continue;
            }
            if (!pose.offsetApplied || !Approximately(pose.bone.localRotation, pose.written))
            {
                pose.clean = pose.bone.localRotation;
            }
            pose.bone.localRotation = pose.clean;
        }
        ApplyOffset(postureBones, postureOffset);
        ApplyOffset(recoilBones, recoilOffset);
        foreach (var pose in _bones)
        {
            if (pose.bone != null)
            {
                pose.written = pose.bone.localRotation;
                pose.offsetApplied = true;
            }
        }
    }

    public void Restore()
    {
        foreach (var pose in _bones)
        {
            if (pose.bone != null && pose.offsetApplied && Approximately(pose.bone.localRotation, pose.written))
            {
                pose.bone.localRotation = pose.clean;
            }
            pose.offsetApplied = false;
        }
    }

    public static Transform[] Resolve(Animator animator, HumanBodyBones[] configuredBones)
    {
        if (animator == null || !animator.isHuman || configuredBones == null)
        {
            return Array.Empty<Transform>();
        }
        var resolved = new List<Transform>();
        foreach (var humanBone in configuredBones)
        {
            Transform bone = animator.GetBoneTransform(humanBone);
            if (bone != null && !resolved.Contains(bone))
            {
                resolved.Add(bone);
            }
        }
        resolved.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
        return resolved.ToArray();
    }

    private static void ApplyOffset(Transform[] bones, Quaternion offset)
    {
        if (Approximately(offset, Quaternion.identity))
        {
            return;
        }
        foreach (var bone in bones)
        {
            if (bone != null)
            {
                bone.rotation = offset * bone.rotation;
            }
        }
    }

    private static int Depth(Transform bone)
    {
        int depth = 0;
        for (Transform parent = bone.parent; parent != null; parent = parent.parent)
        {
            depth++;
        }
        return depth;
    }

    private static bool Approximately(Quaternion a, Quaternion b)
    {
        return Quaternion.Angle(a, b) < 0.01f;
    }
}
