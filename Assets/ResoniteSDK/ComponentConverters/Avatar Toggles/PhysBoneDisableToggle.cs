using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Builds a single avatar-wide "Disable PhysBones" toggle covering every VRC PhysBone and legacy Dynamic Bone on
/// the avatar, since neither VRChat nor Resonite provides one out of the box. Re-run on every conversion, so it
/// stays in sync as PhysBones/Dynamic Bones are added or removed.
/// </summary>
public static class PhysBoneDisableToggle
{
    const string LABEL = "Disable PhysBones";

    public static void Apply(Transform avatarRoot, ref GameObject toggleObject, IConversionContext context)
    {
        var sources = avatarRoot.GetComponentsInChildren<Component>(true)
            .Where(c => c != null && (c.GetType().FullName == VRChatTypes.PhysBone || c.GetType().FullName == DynamicBoneTypes.DynamicBone))
            .ToList();

        if (sources.Count == 0)
        {
            GeneratedObjectHelper.DestroyWithEmptyParents(ref toggleObject);
            return;
        }

        var on = new ToggleState();

        foreach (var source in sources)
            on.Set(new DynamicBoneChainEnabledTarget(source), false);

        var menu = AvatarToggleBuilder.EnsureMenu(avatarRoot, System.Array.Empty<string>());

        AvatarToggleBuilder.EnsureItem(ref toggleObject, menu, LABEL);
        AvatarToggleBuilder.BuildToggle(toggleObject, false, on, new ToggleState(), context);
    }
}
