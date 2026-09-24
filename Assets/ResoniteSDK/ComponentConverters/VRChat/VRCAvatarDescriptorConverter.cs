using UnityEngine;

/// <summary>
/// Sets up Resonite avatar conversion for VRChat avatars. If the avatar doesn't have
/// <see cref="ResoniteBipedAvatarDescriptor"/> yet, it's added automatically with the viewpoint placed at the
/// VRChat view position. Existing descriptors are never modified, so any manual setup is preserved.
/// </summary>
[ConvertsComponentType(VRChatTypes.AvatarDescriptor)]
public class VRCAvatarDescriptorConverter : ResoniteComponentConverter<Component>
{
    protected override void Initialize(Component target)
    {
        if (target.GetComponent<ResoniteBipedAvatarDescriptor>() != null)
            return;

        var animator = target.GetComponent<Animator>();

        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogWarning($"VRChat avatar {target.name} doesn't have a humanoid Animator. " +
                $"Resonite avatar can't be set up automatically.", target);
            return;
        }

        // This will generate the references and position them based on the humanoid rig
        var descriptor = target.gameObject.AddComponent<ResoniteBipedAvatarDescriptor>();
        descriptor.EnsureReferencesExist();

        // VRChat's view position is more reliable than the one estimated from the eye bones, since the creator set it
        var viewPosition = ReflectionAccessor.Get(target, "ViewPosition", Vector3.zero);

        if (viewPosition != Vector3.zero && descriptor.ViewpointReference != null)
            descriptor.ViewpointReference.position = target.transform.TransformPoint(viewPosition);

        Debug.Log($"Added ResoniteBipedAvatarDescriptor to VRChat avatar {target.name}. " +
            $"Check the generated references before saving the avatar.", target);
    }

    protected override void UpdateConversion(Component target, IConversionContext context)
    {
        // Nothing to do, the actual conversion is handled by the ResoniteBipedAvatarDescriptor
    }

    protected override void Cleanup()
    {
        // We intentionally keep the generated descriptor, since user might have adjusted it
    }
}
