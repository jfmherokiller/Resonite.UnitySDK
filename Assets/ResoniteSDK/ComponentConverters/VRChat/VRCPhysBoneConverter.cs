using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts VRChat PhysBones into Resonite's DynamicBoneChain.
///
/// The physics models are different, so the simulation parameters are mapped with heuristics that give
/// a reasonably similar feel. They'll likely need some tweaking in Resonite for best results.
/// </summary>
[ConvertsComponentType(VRChatTypes.PhysBone)]
public class VRCPhysBoneConverter : ResoniteSingleComponentConverter<Component, FrooxEngine.DynamicBoneChainWrapper>
{
    HashSet<Component> _pendingColliders = new HashSet<Component>();

    protected override void UpdateConversion(Component target, IConversionContext context)
    {
        var chain = Binding.Data;

        // Only these are sent, everything else is left at Resonite's defaults.
        // Bones have internal drives for the bone transforms, which must not be overwritten.
        var filter = ResoniteMemberFilter.Set(Binding,
            "Inertia", "Damping", "Elasticity", "Stiffness", "Gravity", "UseUserGravityDirection",
            "SimulateTerminalBones", "IsGrabbable", "DynamicPlayerCollision", "MaxStretchRatio",
            "BaseBoneRadius", "Bones", "StaticColliders");

        filter.StripInternalFromLists.Add("Bones");
        filter.StripFromListElements.Add("GrabOverride");

        chain.Enabled = !(target is Behaviour behaviour) || behaviour.enabled;

        var root = ReflectionAccessor.GetTransform(target, "rootTransform");

        if (root == null)
            root = target.transform;

        SetupSimulation(chain, target);
        SetupBones(chain, target, root);
        SetupColliders(chain, target, context);

        // PhysBone radius is in the root transform's space, while the chain radius is relative to the chain's slot
        var radius = ReflectionAccessor.Get(target, "radius", 0f);
        chain.BaseBoneRadius = radius * MaxAxis(root.lossyScale) / Mathf.Max(1e-6f, MaxAxis(transform.lossyScale));
    }

    static void SetupSimulation(FrooxEngine.DynamicBoneChain chain, Component target)
    {
        var pull = Mathf.Clamp01(ReflectionAccessor.Get(target, "pull", 0.2f));
        var spring = Mathf.Clamp01(ReflectionAccessor.Get(target, "spring", 0.2f));
        var stiffness = Mathf.Clamp01(ReflectionAccessor.Get(target, "stiffness", 0.2f));
        var gravity = Mathf.Clamp(ReflectionAccessor.Get(target, "gravity", 0f), -1, 1);
        var immobile = Mathf.Clamp01(ReflectionAccessor.Get(target, "immobile", 0f));

        var advanced = ReflectionAccessor.GetEnumName(target, "integrationType") == "Advanced";

        // HEURISTICS! These are not physically equivalent, but give similar behavior in common cases.
        // Pull is how strongly the bones return to their rest pose
        chain.Elasticity = Mathf.Lerp(10f, 300f, pull);

        // Spring (Momentum in Simplified mode) is how much the bones keep moving/oscillating, so it's inverse of damping
        chain.Damping = Mathf.Lerp(10f, 1f, spring);

        // Stiffness is only used by the Advanced integration
        chain.Stiffness = advanced ? stiffness : 0.2f;

        // Immobile reduces how much the movement of the avatar affects the bones
        chain.Inertia = 0.2f * (1f - immobile);

        chain.Gravity = Vector3.down * (9.81f * gravity);
        chain.UseUserGravityDirection = true;

        // PhysBones simulate the tip of the last bone when there's an endpoint position
        chain.SimulateTerminalBones = ReflectionAccessor.Get(target, "endpointPosition", Vector3.zero) != Vector3.zero;

        // VRChat's AdvancedBool is False, True or Other (per-user settings), which we treat as enabled
        chain.IsGrabbable = ReflectionAccessor.GetEnumName(target, "allowGrabbing") != "False";
        chain.DynamicPlayerCollision = ReflectionAccessor.GetEnumName(target, "allowCollision") != "False";

        var maxStretch = ReflectionAccessor.Get(target, "maxStretch", 0f);
        chain.MaxStretchRatio = 1f + Mathf.Max(0, maxStretch);
    }

    static void SetupBones(FrooxEngine.DynamicBoneChain chain, Component target, Transform root)
    {
        var ignored = new HashSet<Transform>(ReflectionAccessor.GetList(target, "ignoreTransforms")
            .OfType<Transform>().Where(t => t != null));

        // The root itself is the anchor of the chain and isn't simulated, only its descendants are
        var bones = new List<(Transform bone, int depth)>();
        CollectBones(root, 0, ignored, bones);

        var maxDepth = bones.Count > 0 ? bones.Max(b => b.depth) : 1;
        var radiusCurve = ReflectionAccessor.Get<AnimationCurve>(target, "radiusCurve");

        if (radiusCurve != null && radiusCurve.length == 0)
            radiusCurve = null;

        for (int i = 0; i < bones.Count; i++)
        {
            // Reuse existing elements, so their ID's remain stable between updates
            var element = i < chain.Bones.Count ? chain.Bones.GetElement(i) : chain.Bones.Add();
            var bone = bones[i].bone;

            element.BoneSlot = bone.GetSlot();
            element.OrigPosition = bone.localPosition;
            element.OrigRotation = bone.localRotation;
            element.Collide = true;

            // The curves are evaluated along the chain, from the root (0) to the furthest bone (1)
            var normalizedDepth = maxDepth > 1 ? (bones[i].depth - 1) / (float)(maxDepth - 1) : 0f;
            element.RadiusModifier = radiusCurve != null ? radiusCurve.Evaluate(normalizedDepth) : 1f;
        }

        while (chain.Bones.Count > bones.Count)
            chain.Bones.RemoveAt(chain.Bones.Count - 1);
    }

    static void CollectBones(Transform parent, int depth, HashSet<Transform> ignored, List<(Transform, int)> bones)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);

            if (ignored.Contains(child))
                continue;

            // Skip any helper objects that the converters generate
            if (GeneratedObjectHelper.IsGenerated(child))
                continue;

            bones.Add((child, depth + 1));
            CollectBones(child, depth + 1, ignored, bones);
        }
    }

    void SetupColliders(FrooxEngine.DynamicBoneChain chain, Component target, IConversionContext context)
    {
        chain.StaticColliders.Clear();

        if (_pendingColliders == null)
            _pendingColliders = new HashSet<Component>();

        var sources = ReflectionAccessor.GetList(target, "colliders").OfType<Component>().Where(c => c != null).ToList();

        // VRCFury global colliders act as extra hand colliders, which affect all PhysBones that allow collision
        if (chain.DynamicPlayerCollision)
            foreach (var component in VRChatTypes.FindAvatarRoot(target.transform).GetComponentsInChildren<Component>(true))
                if (component != null && component.GetType().FullName == VRCFuryTypes.GlobalCollider)
                    sources.Add(component);

        foreach (var collider in sources)
        {
            var converter = FindColliderConverter(collider);

            if (converter != null)
            {
                AddColliders(chain, converter);
                continue;
            }

            // Collider hasn't been converted yet (e.g. it's later in the hierarchy). Add it once it is.
            if (_pendingColliders.Add(collider))
                context.RunOnConverted(collider, () =>
                {
                    _pendingColliders.Remove(collider);

                    var pending = FindColliderConverter(collider);

                    if (pending != null && Binding != null)
                        AddColliders(Binding.Data, pending);
                });
        }
    }

    static IDynamicBoneColliderSource FindColliderConverter(Component collider) =>
        collider.GetComponents<ResoniteComponentConverter>()
            .FirstOrDefault(c => c.Target == collider && c is IDynamicBoneColliderSource) as IDynamicBoneColliderSource;

    static void AddColliders(FrooxEngine.DynamicBoneChain chain, IDynamicBoneColliderSource source)
    {
        foreach (var sphere in source.GetColliders())
            chain.StaticColliders.Add(sphere);
    }

    static float MaxAxis(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
}
