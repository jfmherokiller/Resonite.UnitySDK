using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts legacy Dynamic Bone colliders into Resonite dynamic bone sphere colliders.
/// Spheres and capsules (including tapered ones) are converted, plane and "inside" colliders aren't supported.
/// </summary>
[ConvertsComponentType(DynamicBoneTypes.Collider)]
[ConvertsComponentType(DynamicBoneTypes.PlaneCollider)]
public class DynamicBoneColliderConverter : ResoniteComponentConverter<Component>, IDynamicBoneColliderSource
{
    public DynamicBoneColliderBuilder Builder = new DynamicBoneColliderBuilder();

    readonly ConversionReporter _report = new ConversionReporter();

    protected override void UpdateConversion(Component target, IConversionContext context) => Rebuild();

    public IEnumerable<FrooxEngine.DynamicBoneSphereCollider> GetColliders()
    {
        if (!Builder.Exists)
            Rebuild();

        return Builder.GetColliders();
    }

    void Rebuild()
    {
        var target = Target;

        if (target == null)
            return;

        if (target.GetType().FullName == DynamicBoneTypes.PlaneCollider
            || ReflectionAccessor.GetEnumName(target, "m_Bound", "Outside") != "Outside")
        {
            _report.Warning("shape", $"Dynamic Bone collider on {target.name} is a plane or keeps bones inside, " +
                $"which isn't supported by Resonite.", target);

            Builder.Clear();
            return;
        }

        var radius = ReflectionAccessor.Get(target, "m_Radius", 0.5f);

        // Newer versions support capsules with different radius on each end
        float? endRadius = null;

        if (ReflectionAccessor.Has(target, "m_Radius2"))
        {
            var radius2 = ReflectionAccessor.Get(target, "m_Radius2", radius);

            if (radius2 > 0)
                endRadius = radius2;
        }

        Builder.Build("[Resonite] Dynamic Bone Collider", target.transform,
            ReflectionAccessor.Get(target, "m_Center", Vector3.zero),
            AxisRotation(ReflectionAccessor.GetEnumName(target, "m_Direction", "Y")),
            radius,
            ReflectionAccessor.Get(target, "m_Height", 0f),
            !(target is Behaviour behaviour) || behaviour.enabled,
            endRadius);
    }

    // The builder creates capsules along the local Y axis
    static Quaternion AxisRotation(string direction)
    {
        switch (direction)
        {
            case "X": return Quaternion.FromToRotation(Vector3.up, Vector3.right);
            case "Z": return Quaternion.FromToRotation(Vector3.up, Vector3.forward);
            default: return Quaternion.identity;
        }
    }

    protected override void Cleanup() => Builder.Clear();
}
