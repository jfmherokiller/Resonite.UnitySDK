using UnityEngine;

/// <summary>
/// Full type names of VRChat SDK components. The converters reference these by name, so the SDK doesn't need to be
/// installed for the project to compile.
/// </summary>
public static class VRChatTypes
{
    public const string AvatarDescriptor = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";

    public const string PhysBone = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";
    public const string PhysBoneCollider = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider";

    public const string ParentConstraint = "VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint";
    public const string PositionConstraint = "VRC.SDK3.Dynamics.Constraint.Components.VRCPositionConstraint";
    public const string RotationConstraint = "VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint";
    public const string ScaleConstraint = "VRC.SDK3.Dynamics.Constraint.Components.VRCScaleConstraint";
    public const string AimConstraint = "VRC.SDK3.Dynamics.Constraint.Components.VRCAimConstraint";
    public const string LookAtConstraint = "VRC.SDK3.Dynamics.Constraint.Components.VRCLookAtConstraint";

    public const string SpatialAudioSource = "VRC.SDK3.Avatars.Components.VRCSpatialAudioSource";
    public const string SpatialAudioSourceWorlds = "VRC.SDK3.Components.VRCSpatialAudioSource";

    /// <summary>
    /// Finds the root of the avatar this transform belongs to. This is the closest parent with VRChat avatar
    /// descriptor, or a humanoid Animator if there's none.
    /// </summary>
    public static Transform FindAvatarRoot(Transform transform)
    {
        Transform humanoid = null;

        for (var t = transform; t != null; t = t.parent)
        {
            foreach (var component in t.GetComponents<Component>())
                if (component != null && component.GetType().FullName == AvatarDescriptor)
                    return t;

            if (humanoid == null)
            {
                var animator = t.GetComponent<Animator>();

                if (animator != null && animator.isHuman)
                    humanoid = t;
            }
        }

        return humanoid != null ? humanoid : transform.root;
    }
}
