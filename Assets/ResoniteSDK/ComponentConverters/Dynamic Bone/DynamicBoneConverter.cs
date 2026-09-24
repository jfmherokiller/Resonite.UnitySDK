using System.Linq;
using UnityEngine;

/// <summary>
/// Converts the legacy Dynamic Bone asset (by Will Hong), still used by many older VRChat avatars, into Resonite's
/// DynamicBoneChain. The asset doesn't need to be installed for the project to compile - the data is read through
/// reflection when it is.
///
/// Not converted: distribution curves other than radius, freeze axis, distant disable and update rate settings.
/// </summary>
[ConvertsComponentType(DynamicBoneTypes.DynamicBone)]
public class DynamicBoneConverter : DynamicBoneChainConverterBase
{
    // Dynamic Bone's gravity & force are per-update displacements rather than accelerations.
    // This scale gives similar sag for typical values (e.g. -0.01 ... -0.1).
    const float GRAVITY_SCALE = 98.1f;

    protected override DynamicBoneChainSettings ReadSettings(Component target)
    {
        var gravity = ReflectionAccessor.Get(target, "m_Gravity", Vector3.zero) + ReflectionAccessor.Get(target, "m_Force", Vector3.zero);

        var settings = new DynamicBoneChainSettings()
        {
            Enabled = !(target is Behaviour behaviour) || behaviour.enabled,
            RadiusCurve = ReflectionAccessor.Get<AnimationCurve>(target, "m_RadiusDistrib"),

            Pull = ReflectionAccessor.Get(target, "m_Elasticity", 0.1f),
            Momentum = 1f - Mathf.Clamp01(ReflectionAccessor.Get(target, "m_Damping", 0.1f)),
            Stiffness = ReflectionAccessor.Get(target, "m_Stiffness", 0.1f),
            Immobile = ReflectionAccessor.Get(target, "m_Inert", 0f),

            Gravity = gravity * GRAVITY_SCALE,

            // Dynamic Bone adds a simulated tip to the last bones when it has end length or offset
            SimulateTerminalBones = ReflectionAccessor.Get(target, "m_EndLength", 0f) > 0
                || ReflectionAccessor.Get(target, "m_EndOffset", Vector3.zero) != Vector3.zero,

            // Dynamic Bones can't be grabbed or stretched
            IsGrabbable = false,
            MaxStretchRatio = 1f,

            // Dynamic Bone radius is scaled by the component's transform, same as the chain's slot
            BaseBoneRadius = ReflectionAccessor.Get(target, "m_Radius", 0f),
        };

        // Newer versions support multiple roots
        settings.Roots.AddRange(ReflectionAccessor.GetList(target, "m_Roots").OfType<Transform>().Where(t => t != null));

        if (settings.Roots.Count == 0)
        {
            var root = ReflectionAccessor.GetTransform(target, "m_Root");
            settings.Roots.Add(root != null ? root : target.transform);
        }

        foreach (var excluded in ReflectionAccessor.GetList(target, "m_Exclusions").OfType<Transform>())
            if (excluded != null)
                settings.Ignored.Add(excluded);

        settings.Colliders.AddRange(ReflectionAccessor.GetList(target, "m_Colliders").OfType<Component>());

        if (ReflectionAccessor.GetEnumName(target, "m_FreezeAxis", "None") != "None")
            Report.Warning("freeze", $"Dynamic Bone on {target.name} uses freeze axis, which isn't supported and will be ignored.", target);

        return settings;
    }
}
