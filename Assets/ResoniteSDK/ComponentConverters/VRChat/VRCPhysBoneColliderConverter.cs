using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts VRChat PhysBone colliders into Resonite dynamic bone sphere colliders.
/// - Sphere is converted into a single sphere collider
/// - Capsule is approximated by a row of overlapping spheres
/// - Plane and "inside bounds" colliders are not supported
///
/// The colliders are referenced by <see cref="VRCPhysBoneConverter"/> for any PhysBones that use them.
/// </summary>
[ConvertsComponentType(VRChatTypes.PhysBoneCollider)]
public class VRCPhysBoneColliderConverter : ResoniteComponentConverter<Component>, IDynamicBoneColliderSource
{
    public DynamicBoneColliderBuilder Builder = new DynamicBoneColliderBuilder();

    readonly ConversionReporter _report = new ConversionReporter();

    protected override void UpdateConversion(Component target, IConversionContext context) => Rebuild();

    /// <summary>
    /// Returns all the generated colliders. This will build them if they don't exist yet.
    /// </summary>
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

        var shape = ReflectionAccessor.GetEnumName(target, "shapeType", "Sphere");
        var insideBounds = ReflectionAccessor.Get(target, "insideBounds", false);

        if ((shape != "Sphere" && shape != "Capsule") || insideBounds)
        {
            _report.Warning("shape", $"PhysBone collider on {target.name} uses shape or mode that's not supported by Resonite " +
                $"(only non-inverted sphere and capsule colliders are converted).", target);

            Builder.Clear();
            return;
        }

        var root = ReflectionAccessor.GetTransform(target, "rootTransform");

        if (root == null)
            root = target.transform;

        Builder.Build("[Resonite] PhysBone Collider", root,
            ReflectionAccessor.Get(target, "position", Vector3.zero),
            ReflectionAccessor.Get(target, "rotation", Quaternion.identity),
            ReflectionAccessor.Get(target, "radius", 0.5f),
            shape == "Capsule" ? ReflectionAccessor.Get(target, "height", 2f) : 0,
            !(target is Behaviour behaviour) || behaviour.enabled);
    }

    protected override void Cleanup() => Builder.Clear();
}
