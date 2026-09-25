using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;

public enum ConstraintKind
{
    Parent,
    Position,
    Rotation,
    Scale,
    Aim,
    LookAt,
}

public struct ConstraintSourceData
{
    public Transform Source;
    public float Weight;

    // Only used by parent constraints
    public Vector3 PositionOffset;
    public Vector3 RotationOffset;
}

/// <summary>
/// Common description of a constraint, which is filled from either Unity's built-in constraints or VRChat constraints.
/// This lets both share the same conversion into Resonite components.
/// </summary>
public class ConstraintData
{
    public ConstraintKind Kind;
    public bool Active = true;

    /// <summary>
    /// The transform that is being constrained
    /// </summary>
    public Transform Target;

    public List<ConstraintSourceData> Sources = new List<ConstraintSourceData>();

    /// <summary>
    /// Blends between the rest pose (0) and the constrained pose (1)
    /// </summary>
    public float GlobalWeight = 1;

    // Axes affected by the constraint
    public bool PositionX = true, PositionY = true, PositionZ = true;
    public bool AllRotationAxes = true;
    public bool AllScaleAxes = true;

    public bool AllPositionAxes => PositionX && PositionY && PositionZ;

    public bool SolveInLocalSpace;
    public bool FreezeToWorld;

    /// <summary>
    /// Local pose used when the constraint isn't fully applied. Uses the current pose when not set.
    /// </summary>
    public Vector3? RestPosition;
    public Quaternion? RestRotation;

    public Vector3 PositionOffset;

    // Euler angles
    public Vector3 RotationOffset;
    public Vector3 ScaleOffset = Vector3.one;

    // Aim & LookAt
    public Vector3 AimAxis = Vector3.forward;
    public Vector3 UpAxis = Vector3.up;
    public Vector3 WorldUp = Vector3.up;
    public float Roll;

    public static void ReadSources(IConstraint constraint, ConstraintData data)
    {
        data.GlobalWeight = constraint.weight;

        for (int i = 0; i < constraint.sourceCount; i++)
        {
            var source = constraint.GetSource(i);

            data.Sources.Add(new ConstraintSourceData()
            {
                Source = source.sourceTransform,
                Weight = source.weight,
            });
        }
    }

    public static bool IsAllAxes(Axis axis) => (axis & (Axis.X | Axis.Y | Axis.Z)) == (Axis.X | Axis.Y | Axis.Z);

    public void SetPositionAxes(Axis axis)
    {
        PositionX = (axis & Axis.X) != 0;
        PositionY = (axis & Axis.Y) != 0;
        PositionZ = (axis & Axis.Z) != 0;
    }

    public static Vector3 ResolveWorldUp(AimConstraint.WorldUpType type, Vector3 vector, Transform upObject)
    {
        switch (type)
        {
            case AimConstraint.WorldUpType.Vector:
                return vector;

            // Resonite's LookAt uses static up vector, so we just approximate it by the current orientation
            case AimConstraint.WorldUpType.ObjectRotationUp:
                return upObject != null ? upObject.rotation * vector : Vector3.up;

            default:
                return Vector3.up;
        }
    }
}

/// <summary>
/// Handles creating Resonite components that represent a constraint.
///
/// Simple cases use regular components:
/// - Parent constraint maps to VirtualParent
/// - Aim & LookAt constraints map to LookAt
/// - Scale constraint maps to CopyGlobalScale
/// - Position & Rotation constraints follow a generated anchor under the source. A generated follower next to the
///   constrained object copies the anchor's global transform, and its local position/rotation is copied to the
///   constrained object with ValueCopy. Local space constraints copy the source's local values directly.
///
/// Parent, Position & Rotation constraints with multiple sources, partial weights or only some position axes are
/// converted with ProtoFlux (see <see cref="ProtoFluxGraph"/>): each source gets a follower next to the constrained
/// object, and their local poses are blended by weight (and with the rest pose) and drive the constrained object.
///
/// Constraints frozen to world or affecting only some rotation/scale axes are reported and skipped.
/// Aim, LookAt and Scale constraints use the source with the highest weight at full strength.
/// </summary>
public abstract class ConstraintConverterBase<T> : ResoniteComponentConverter<T>
    where T : Component
{
    const float FULL_WEIGHT = 0.99f;

    public FrooxEngine.VirtualParentWrapper VirtualParent;
    public FrooxEngine.LookAtWrapper LookAt;
    public FrooxEngine.CopyGlobalScaleWrapper CopyScale;

    // Position & Rotation constraints
    public GameObject Anchor;
    public GameObject Follower;
    public ResoniteComponent LocalCopy;

    // Blended constraints
    public List<GameObject> BlendAnchors = new List<GameObject>();
    public List<GameObject> BlendFollowers = new List<GameObject>();
    public GameObject BlendGraph;

    readonly ConversionReporter _report = new ConversionReporter();

    enum Method
    {
        None,
        VirtualParent,
        LookAt,
        CopyScale,
        Follow,
        Blend,
    }

    protected abstract ConstraintData ReadConstraint(T target);

    protected override void UpdateConversion(T target, IConversionContext context)
    {
        var data = ReadConstraint(target);
        var sources = data.Sources.Where(s => s.Source != null && s.Weight > 0).ToList();

        var method = Method.None;

        if (data.Active && data.Target != null && sources.Count > 0 && data.GlobalWeight > 0)
            method = Convert(data, sources, $"{target.GetType().Name} on {target.name}");

        // Remove anything that's not used (anymore)
        if (method != Method.VirtualParent)
            ConverterComponentHelper.Remove(ref VirtualParent);

        if (method != Method.LookAt)
            ConverterComponentHelper.Remove(ref LookAt);

        if (method != Method.CopyScale)
            ConverterComponentHelper.Remove(ref CopyScale);

        if (method != Method.Follow)
            RemoveFollow();

        if (method != Method.Blend)
            RemoveBlend();
    }

    Method Convert(ConstraintData data, List<ConstraintSourceData> sources, string name)
    {
        if (data.FreezeToWorld)
        {
            _report.Warning("freeze", $"{name} is frozen to world, which isn't supported for conversion yet.", Target);
            return Method.None;
        }

        var drivesRotation = data.Kind == ConstraintKind.Parent || data.Kind == ConstraintKind.Rotation
            || data.Kind == ConstraintKind.Aim || data.Kind == ConstraintKind.LookAt;

        if ((drivesRotation && !data.AllRotationAxes) || (data.Kind == ConstraintKind.Scale && !data.AllScaleAxes))
        {
            _report.Warning("axes", $"{name} only affects some rotation/scale axes, which isn't supported for conversion yet.", Target);
            return Method.None;
        }

        var source = sources.OrderByDescending(s => s.Weight).First();
        var blends = sources.Count > 1 || source.Weight * data.GlobalWeight < FULL_WEIGHT;

        switch (data.Kind)
        {
            case ConstraintKind.Parent:
            case ConstraintKind.Position:
            case ConstraintKind.Rotation:
                var partialAxes = data.Kind != ConstraintKind.Rotation && !data.AllPositionAxes;

                if (data.SolveInLocalSpace && HasOffset(data, sources))
                {
                    _report.Warning("localoffset", $"{name} solves in local space with an offset, " +
                        $"which isn't supported for conversion yet.", Target);
                    return Method.None;
                }

                // Local space parent constraints need both position & rotation from the source, which the graph handles
                if (blends || partialAxes || (data.Kind == ConstraintKind.Parent && data.SolveInLocalSpace))
                {
                    SetupBlend(data, sources);
                    return Method.Blend;
                }

                if (data.Kind == ConstraintKind.Parent)
                {
                    SetupParent(data, source);
                    return Method.VirtualParent;
                }

                SetupFollow(data, source);
                return Method.Follow;

            case ConstraintKind.Aim:
            case ConstraintKind.LookAt:
                ReportBlend(blends, name);
                SetupLookAt(data, source);
                return Method.LookAt;

            case ConstraintKind.Scale:
                ReportBlend(blends, name);
                SetupScale(data, source);
                return Method.CopyScale;
        }

        return Method.None;
    }

    static bool HasOffset(ConstraintData data, List<ConstraintSourceData> sources)
    {
        switch (data.Kind)
        {
            case ConstraintKind.Parent:
                return sources.Any(s => s.PositionOffset != Vector3.zero || s.RotationOffset != Vector3.zero);

            case ConstraintKind.Position:
                return data.PositionOffset != Vector3.zero;

            default:
                return data.RotationOffset != Vector3.zero;
        }
    }

    void ReportBlend(bool blends, string name)
    {
        if (blends)
            _report.Warning("blend", $"{name} blends multiple sources or uses partial weight. " +
                $"Only the source with the highest weight is converted, at full weight.", Target);
    }

    #region PARENT, AIM, LOOK AT, SCALE

    void SetupParent(ConstraintData data, ConstraintSourceData source)
    {
        var wrapper = ConverterComponentHelper.EnsureOn(ref VirtualParent, data.Target.gameObject);
        SetupVirtualParent(wrapper, source, data.Target);
    }

    static void SetupVirtualParent(FrooxEngine.VirtualParentWrapper wrapper, ConstraintSourceData source, Transform target)
    {
        ResoniteMemberFilter.Set(wrapper, "OverrideParent", "LocalPosition", "LocalRotation", "LocalScale");

        var parent = wrapper.Data;

        parent.OverrideParent = source.Source.GetSlot();
        parent.LocalPosition = source.PositionOffset;
        parent.LocalRotation = Quaternion.Euler(source.RotationOffset);

        // Parent constraints don't affect scale, but VirtualParent does. Keep the current global scale.
        parent.LocalScale = ConverterComponentHelper.SafeDivide(target.lossyScale, source.Source.lossyScale);
    }

    void SetupLookAt(ConstraintData data, ConstraintSourceData source)
    {
        var wrapper = ConverterComponentHelper.EnsureOn(ref LookAt, data.Target.gameObject);
        ResoniteMemberFilter.Set(wrapper, "Target", "Up", "RotationOffset");

        var lookAt = wrapper.Data;

        lookAt.Target = source.Source.GetSlot();
        lookAt.Up = data.WorldUp;

        var offset = Quaternion.Euler(data.RotationOffset);

        if (data.Kind == ConstraintKind.Aim)
        {
            // LookAt points the forward (Z) axis at the target. Aim constraint can use arbitrary axes,
            // so we need to rotate the aim & up axes onto the forward & up.
            var axes = SafeLookRotation(data.AimAxis, data.UpAxis);
            lookAt.RotationOffset = Quaternion.Inverse(axes) * offset;
        }
        else
            lookAt.RotationOffset = Quaternion.Euler(0, 0, data.Roll) * offset;
    }

    void SetupScale(ConstraintData data, ConstraintSourceData source)
    {
        var wrapper = ConverterComponentHelper.EnsureOn(ref CopyScale, data.Target.gameObject);
        ResoniteMemberFilter.Set(wrapper, "Source", "NonUniform");

        wrapper.Data.Source = source.Source.GetSlot();
        wrapper.Data.NonUniform = true;

        if (data.ScaleOffset != Vector3.one)
            _report.Warning("scaleoffset", $"Scale offset on {data.Target.name} is not supported and will be ignored.", Target);
    }

    #endregion

    #region POSITION & ROTATION (SINGLE SOURCE)

    void SetupFollow(ConstraintData data, ConstraintSourceData source)
    {
        var position = data.Kind == ConstraintKind.Position;

        if (data.SolveInLocalSpace)
        {
            // Both local values are in their own parent's space, so they can be copied directly
            GeneratedObjectHelper.Destroy(ref Anchor);
            GeneratedObjectHelper.Destroy(ref Follower);

            EnsureCopy(position, data.Target.gameObject, source.Source, data.Target);
            return;
        }

        Anchor = EnsureAnchor(Anchor, data, source);
        Follower = EnsureFollower(Follower, data.Target, Anchor.transform, "[Resonite] Constraint Follower");

        EnsureCopy(position, Follower, Follower.transform, data.Target);
    }

    /// <summary>
    /// Anchor under the source, which includes the constraint's offset
    /// </summary>
    static GameObject EnsureAnchor(GameObject anchor, ConstraintData data, ConstraintSourceData source)
    {
        if (anchor == null || anchor.transform.parent != source.Source)
        {
            GeneratedObjectHelper.Destroy(ref anchor);
            anchor = GeneratedObjectHelper.Create(source.Source, "[Resonite] Constraint Anchor");
        }

        if (data.Kind == ConstraintKind.Position)
            GeneratedObjectHelper.SetLocalPose(anchor.transform,
                source.Source.InverseTransformPoint(source.Source.position + data.PositionOffset), Quaternion.identity);
        else
            GeneratedObjectHelper.SetLocalPose(anchor.transform, Vector3.zero, Quaternion.Euler(data.RotationOffset));

        return anchor;
    }

    /// <summary>
    /// Follower is next to the constrained object, so its local values are in the same space. It copies the global
    /// transform of the anchor.
    /// </summary>
    static GameObject EnsureFollower(GameObject follower, Transform target, Transform anchor, string name)
    {
        if (follower == null || follower.transform.parent != target.parent)
        {
            GeneratedObjectHelper.Destroy(ref follower);
            follower = GeneratedObjectHelper.Create(target.parent, name);
        }

        var copyTransform = ConverterComponentHelper.GetOrAdd<FrooxEngine.CopyGlobalTransformWrapper>(follower);
        ResoniteMemberFilter.Set(copyTransform, "Source");
        copyTransform.Data.Source = anchor.GetSlot();

        return follower;
    }

    void EnsureCopy(bool position, GameObject host, Transform from, Transform to)
    {
        if (LocalCopy != null && (LocalCopy.gameObject != host || (position ? !(LocalCopy is ValueCopyFloat3Wrapper) : !(LocalCopy is ValueCopyFloatQWrapper))))
            ConverterComponentHelper.Remove(ref LocalCopy);

        if (position)
        {
            var copy = LocalCopy as ValueCopyFloat3Wrapper;

            if (copy == null)
                LocalCopy = copy = host.AddComponent<ValueCopyFloat3Wrapper>();

            copy.Data.Source = new SlotPositionField(from);
            copy.Data.Target = new SlotPositionField(to);
        }
        else
        {
            var copy = LocalCopy as ValueCopyFloatQWrapper;

            if (copy == null)
                LocalCopy = copy = host.AddComponent<ValueCopyFloatQWrapper>();

            copy.Data.Source = new SlotRotationField(from);
            copy.Data.Target = new SlotRotationField(to);
        }
    }

    void RemoveFollow()
    {
        ConverterComponentHelper.Remove(ref LocalCopy);
        GeneratedObjectHelper.Destroy(ref Anchor);
        GeneratedObjectHelper.Destroy(ref Follower);
    }

    #endregion

    #region BLENDED (PROTOFLUX)

    void SetupBlend(ConstraintData data, List<ConstraintSourceData> sources)
    {
        var target = data.Target;
        var followers = new List<Transform>();

        if (data.SolveInLocalSpace)
        {
            // The local values of the sources can be blended directly
            ClearObjects(BlendAnchors, 0);
            ClearObjects(BlendFollowers, 0);

            followers.AddRange(sources.Select(s => s.Source));
        }
        else
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (BlendFollowers.Count == i)
                    BlendFollowers.Add(null);

                var follower = BlendFollowers[i];

                if (data.Kind == ConstraintKind.Parent)
                {
                    // Follower acts like a child of the source with the offset, the same as the single source parent constraint
                    if (follower == null || follower.transform.parent != target.parent)
                    {
                        GeneratedObjectHelper.Destroy(ref follower);
                        follower = GeneratedObjectHelper.Create(target.parent, $"[Resonite] Constraint Follower {i}");
                    }

                    SetupVirtualParent(ConverterComponentHelper.GetOrAdd<FrooxEngine.VirtualParentWrapper>(follower), sources[i], target);
                }
                else
                {
                    if (BlendAnchors.Count == i)
                        BlendAnchors.Add(null);

                    BlendAnchors[i] = EnsureAnchor(BlendAnchors[i], data, sources[i]);
                    follower = EnsureFollower(follower, target, BlendAnchors[i].transform, $"[Resonite] Constraint Follower {i}");
                }

                BlendFollowers[i] = follower;
                followers.Add(follower.transform);
            }

            ClearObjects(BlendAnchors, data.Kind == ConstraintKind.Parent ? 0 : sources.Count);
            ClearObjects(BlendFollowers, sources.Count);
        }

        if (BlendGraph == null || BlendGraph.transform.parent != target.parent)
        {
            GeneratedObjectHelper.Destroy(ref BlendGraph);
            BlendGraph = GeneratedObjectHelper.Create(target.parent, "[Resonite] Constraint ProtoFlux");
        }

        var graph = new ProtoFluxGraph(BlendGraph);
        graph.Begin();

        var poses = followers.Select(f => graph.LocalTransform(f)).ToList();

        // Unity & VRChat blend the remaining weight (when the source weights add up to less than 1) with the rest pose,
        // and the global weight blends between the rest pose and the result
        var strength = Mathf.Clamp01(sources.Sum(s => s.Weight)) * data.GlobalWeight;

        if (data.Kind != ConstraintKind.Rotation)
        {
            var rest = data.RestPosition ?? target.localPosition;
            var position = graph.WeightedBlend(rest, poses.Select((p, i) => (p.position, sources[i].Weight)).ToList(), strength);

            // Axes that aren't constrained keep their current value
            if (!data.AllPositionAxes)
                position = graph.SelectAxes(position, graph.Constant(target.localPosition), data.PositionX, data.PositionY, data.PositionZ);

            graph.Drive(position, new SlotPositionField(target));
        }

        if (data.Kind != ConstraintKind.Position)
        {
            var rest = data.RestRotation ?? target.localRotation;
            var rotation = graph.WeightedBlend(rest, poses.Select((p, i) => (p.rotation, sources[i].Weight)).ToList(), strength);

            graph.Drive(rotation, new SlotRotationField(target));
        }

        graph.End();
    }

    static void ClearObjects(List<GameObject> objects, int keep)
    {
        for (int i = objects.Count - 1; i >= keep; i--)
        {
            var obj = objects[i];
            GeneratedObjectHelper.Destroy(ref obj);
            objects.RemoveAt(i);
        }
    }

    void RemoveBlend()
    {
        ClearObjects(BlendAnchors, 0);
        ClearObjects(BlendFollowers, 0);
        GeneratedObjectHelper.Destroy(ref BlendGraph);
    }

    #endregion

    static Quaternion SafeLookRotation(Vector3 forward, Vector3 up)
    {
        if (forward.sqrMagnitude < 1e-8f)
            forward = Vector3.forward;

        if (up.sqrMagnitude < 1e-8f || Vector3.Cross(forward, up).sqrMagnitude < 1e-8f)
            up = Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        return Quaternion.LookRotation(forward, up);
    }

    protected override void Cleanup()
    {
        ConverterComponentHelper.Remove(ref VirtualParent);
        ConverterComponentHelper.Remove(ref LookAt);
        ConverterComponentHelper.Remove(ref CopyScale);
        RemoveFollow();
        RemoveBlend();
    }
}
