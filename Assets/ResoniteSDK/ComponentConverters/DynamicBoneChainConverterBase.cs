using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Common description of a bone chain simulation, filled from different Unity components (VRChat PhysBones,
/// legacy Dynamic Bones...). Values left null are not sent, so they stay at Resonite's defaults.
/// </summary>
public class DynamicBoneChainSettings
{
    public bool Enabled = true;

    /// <summary>
    /// Roots of the chains. The roots themselves are anchors and aren't simulated, only their descendants are.
    /// </summary>
    public List<Transform> Roots = new List<Transform>();
    public HashSet<Transform> Ignored = new HashSet<Transform>();

    /// <summary>
    /// Radius multiplier along the chain, from the root (0) to the furthest bone (1)
    /// </summary>
    public AnimationCurve RadiusCurve;

    // Normalized 0...1 values describing the motion. These are mapped to Resonite's parameters with heuristics.
    /// <summary>How strongly the bones return to their rest pose</summary>
    public float Pull = 0.2f;
    /// <summary>How much the bones keep moving/oscillating (inverse of damping)</summary>
    public float Momentum = 0.2f;
    /// <summary>How much the bones keep their rest orientation</summary>
    public float? Stiffness;
    /// <summary>How much the movement of the object doesn't affect the bones</summary>
    public float Immobile;

    /// <summary>
    /// Gravity in m/s^2
    /// </summary>
    public Vector3? Gravity;

    public bool? SimulateTerminalBones;
    public bool? IsGrabbable;
    public bool? DynamicPlayerCollision;
    public float? MaxStretchRatio;

    /// <summary>
    /// Bone radius, relative to the transform of the converted component
    /// </summary>
    public float? BaseBoneRadius;

    /// <summary>
    /// Collider components, whose converters implement <see cref="IDynamicBoneColliderSource"/>
    /// </summary>
    public List<Component> Colliders = new List<Component>();
}

/// <summary>
/// Converts a bone chain simulation component into Resonite's DynamicBoneChain.
/// Derived converters only need to read their component into <see cref="DynamicBoneChainSettings"/>.
///
/// The physics models are different, so the simulation parameters are mapped with heuristics that give
/// a reasonably similar feel. They'll likely need some tweaking in Resonite for best results.
/// </summary>
public abstract class DynamicBoneChainConverterBase : ResoniteSingleComponentConverter<Component, FrooxEngine.DynamicBoneChainWrapper>
{
    HashSet<Component> _pendingColliders = new HashSet<Component>();

    protected readonly ConversionReporter Report = new ConversionReporter();

    protected abstract DynamicBoneChainSettings ReadSettings(Component target);

    protected override void UpdateConversion(Component target, IConversionContext context)
    {
        var settings = ReadSettings(target);
        var chain = Binding.Data;

        // Only what's set is sent, everything else is left at Resonite's defaults
        var members = new List<string> { "Inertia", "Damping", "Elasticity", "Bones", "StaticColliders" };

        chain.Enabled = settings.Enabled;

        // HEURISTICS! These are not physically equivalent, but give similar behavior in common cases.
        chain.Elasticity = Mathf.Lerp(10f, 300f, Mathf.Clamp01(settings.Pull));
        chain.Damping = Mathf.Lerp(10f, 1f, Mathf.Clamp01(settings.Momentum));
        chain.Inertia = 0.2f * (1f - Mathf.Clamp01(settings.Immobile));

        if (settings.Stiffness.HasValue)
        {
            chain.Stiffness = Mathf.Clamp01(settings.Stiffness.Value);
            members.Add("Stiffness");
        }

        if (settings.Gravity.HasValue)
        {
            chain.Gravity = settings.Gravity.Value;
            chain.UseUserGravityDirection = true;
            members.Add("Gravity");
            members.Add("UseUserGravityDirection");
        }

        Assign(settings.SimulateTerminalBones, v => chain.SimulateTerminalBones = v, "SimulateTerminalBones", members);
        Assign(settings.IsGrabbable, v => chain.IsGrabbable = v, "IsGrabbable", members);
        Assign(settings.DynamicPlayerCollision, v => chain.DynamicPlayerCollision = v, "DynamicPlayerCollision", members);
        Assign(settings.MaxStretchRatio, v => chain.MaxStretchRatio = v, "MaxStretchRatio", members);
        Assign(settings.BaseBoneRadius, v => chain.BaseBoneRadius = v, "BaseBoneRadius", members);

        // Bones have internal drives for the bone transforms, which must not be overwritten
        var filter = ResoniteMemberFilter.Set(Binding, members.ToArray());
        filter.StripInternalFromLists.Add("Bones");
        filter.StripFromListElements.Add("GrabOverride");

        SetupBones(chain, settings);
        SetupColliders(chain, settings.Colliders, context);
    }

    static void Assign<T>(T? value, System.Action<T> assign, string member, List<string> members) where T : struct
    {
        if (!value.HasValue)
            return;

        assign(value.Value);
        members.Add(member);
    }

    static void SetupBones(FrooxEngine.DynamicBoneChain chain, DynamicBoneChainSettings settings)
    {
        var bones = new List<(Transform bone, int depth)>();

        foreach (var root in settings.Roots.Where(r => r != null).Distinct())
            CollectBones(root, 0, settings.Ignored, bones);

        var maxDepth = bones.Count > 0 ? bones.Max(b => b.depth) : 1;
        var radiusCurve = settings.RadiusCurve != null && settings.RadiusCurve.length > 0 ? settings.RadiusCurve : null;

        for (int i = 0; i < bones.Count; i++)
        {
            // Reuse existing elements, so their ID's remain stable between updates
            var element = i < chain.Bones.Count ? chain.Bones.GetElement(i) : chain.Bones.Add();
            var bone = bones[i].bone;

            element.BoneSlot = bone.GetSlot();
            element.OrigPosition = bone.localPosition;
            element.OrigRotation = bone.localRotation;
            element.Collide = true;

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

    void SetupColliders(FrooxEngine.DynamicBoneChain chain, List<Component> colliders, IConversionContext context)
    {
        chain.StaticColliders.Clear();

        if (_pendingColliders == null)
            _pendingColliders = new HashSet<Component>();

        foreach (var collider in colliders.Where(c => c != null).Distinct())
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

    protected static float MaxAxis(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
}
