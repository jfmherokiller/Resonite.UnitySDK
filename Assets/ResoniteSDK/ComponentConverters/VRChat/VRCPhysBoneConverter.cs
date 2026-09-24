using System.Linq;
using UnityEngine;

/// <summary>
/// Converts VRChat PhysBones into Resonite's DynamicBoneChain
/// </summary>
[ConvertsComponentType(VRChatTypes.PhysBone)]
public class VRCPhysBoneConverter : DynamicBoneChainConverterBase
{
    protected override DynamicBoneChainSettings ReadSettings(Component target)
    {
        var root = ReflectionAccessor.GetTransform(target, "rootTransform");

        if (root == null)
            root = target.transform;

        var settings = new DynamicBoneChainSettings()
        {
            Enabled = !(target is Behaviour behaviour) || behaviour.enabled,
            RadiusCurve = ReflectionAccessor.Get<AnimationCurve>(target, "radiusCurve"),

            Pull = ReflectionAccessor.Get(target, "pull", 0.2f),

            // Spring is called Momentum in the Simplified mode
            Momentum = ReflectionAccessor.Get(target, "spring", 0.2f),
            Immobile = ReflectionAccessor.Get(target, "immobile", 0f),

            // Stiffness is only used by the Advanced integration
            Stiffness = ReflectionAccessor.GetEnumName(target, "integrationType") == "Advanced"
                ? ReflectionAccessor.Get(target, "stiffness", 0.2f) : 0.2f,

            // Gravity is a fraction of the actual gravity
            Gravity = Vector3.down * (9.81f * Mathf.Clamp(ReflectionAccessor.Get(target, "gravity", 0f), -1, 1)),

            // PhysBones simulate the tip of the last bone when there's an endpoint position
            SimulateTerminalBones = ReflectionAccessor.Get(target, "endpointPosition", Vector3.zero) != Vector3.zero,

            // VRChat's AdvancedBool is False, True or Other (per-user settings), which we treat as enabled
            IsGrabbable = ReflectionAccessor.GetEnumName(target, "allowGrabbing") != "False",
            DynamicPlayerCollision = ReflectionAccessor.GetEnumName(target, "allowCollision") != "False",

            MaxStretchRatio = 1f + Mathf.Max(0, ReflectionAccessor.Get(target, "maxStretch", 0f)),

            // PhysBone radius is in the root transform's space, while the chain radius is relative to the chain's slot
            BaseBoneRadius = ReflectionAccessor.Get(target, "radius", 0f) * MaxAxis(root.lossyScale)
                / Mathf.Max(1e-6f, MaxAxis(target.transform.lossyScale)),
        };

        settings.Roots.Add(root);

        foreach (var ignored in ReflectionAccessor.GetList(target, "ignoreTransforms").OfType<Transform>())
            if (ignored != null)
                settings.Ignored.Add(ignored);

        settings.Colliders.AddRange(ReflectionAccessor.GetList(target, "colliders").OfType<Component>());

        // VRCFury global colliders act as extra hand colliders, which affect all PhysBones that allow collision
        if (settings.DynamicPlayerCollision == true)
            foreach (var component in VRChatTypes.FindAvatarRoot(target.transform).GetComponentsInChildren<Component>(true))
                if (component != null && component.GetType().FullName == VRCFuryTypes.GlobalCollider)
                    settings.Colliders.Add(component);

        return settings;
    }
}
