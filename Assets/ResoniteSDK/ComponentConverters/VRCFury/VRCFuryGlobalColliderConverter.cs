using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts VRCFury Global Colliders. In VRChat these act as extra hand/finger colliders, so here they're
/// converted into dynamic bone colliders that are added to all the PhysBones on the avatar that allow collisions.
/// </summary>
[ConvertsComponentType(VRCFuryTypes.GlobalCollider)]
public class VRCFuryGlobalColliderConverter : ResoniteComponentConverter<Component>, IDynamicBoneColliderSource
{
    public DynamicBoneColliderBuilder Builder = new DynamicBoneColliderBuilder();

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

        var root = ReflectionAccessor.GetTransform(target, "rootTransform");

        if (root == null)
            root = target.transform;

        Builder.Build("[Resonite] Global Collider", root, Vector3.zero, Quaternion.identity,
            ReflectionAccessor.Get(target, "radius", 0.1f),
            ReflectionAccessor.Get(target, "height", 0f),
            !(target is Behaviour behaviour) || behaviour.enabled);
    }

    protected override void Cleanup() => Builder.Clear();
}
