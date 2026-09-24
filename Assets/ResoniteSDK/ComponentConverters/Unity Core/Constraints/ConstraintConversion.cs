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

    /// <summary>
    /// Effective weight of the source, including the global weight of the constraint
    /// </summary>
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
    /// False when the constraint only affects some of the axes
    /// </summary>
    public bool AllAxes = true;

    public bool SolveInLocalSpace;
    public bool FreezeToWorld;

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
        for (int i = 0; i < constraint.sourceCount; i++)
        {
            var source = constraint.GetSource(i);

            data.Sources.Add(new ConstraintSourceData()
            {
                Source = source.sourceTransform,
                Weight = source.weight * constraint.weight,
            });
        }
    }

    public static bool IsAllAxes(Axis axis) => (axis & (Axis.X | Axis.Y | Axis.Z)) == (Axis.X | Axis.Y | Axis.Z);

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
/// Handles creating Resonite components that approximate a constraint.
/// - Parent constraint maps to VirtualParent
/// - Aim & LookAt constraints map to LookAt
/// - Scale constraint maps to CopyGlobalScale
/// - Position & Rotation constraints follow a generated anchor under the source. A generated follower next to the
///   constrained object copies the anchor's global transform, and its local position/rotation is copied to the
///   constrained object with ValueCopy. Local space constraints copy the source's local values directly.
///
/// Things that can't be represented without ProtoFlux (per-axis constraints, blending multiple sources or partial
/// weights for position/rotation, freezing to world) are reported and skipped.
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

    readonly ConversionReporter _report = new ConversionReporter();

    protected abstract ConstraintData ReadConstraint(T target);

    protected override void UpdateConversion(T target, IConversionContext context)
    {
        var data = ReadConstraint(target);

        var sources = data.Sources.Where(s => s.Source != null && s.Weight > 0).ToList();
        var name = $"{target.GetType().Name} on {target.name}";

        ConstraintKind? converted = null;

        if (data.Active && data.Target != null && sources.Count > 0)
            converted = Convert(data, sources, name);

        // Remove anything that's not used (anymore)
        if (converted != ConstraintKind.Parent)
            ConverterComponentHelper.Remove(ref VirtualParent);

        if (converted != ConstraintKind.Aim && converted != ConstraintKind.LookAt)
            ConverterComponentHelper.Remove(ref LookAt);

        if (converted != ConstraintKind.Scale)
            ConverterComponentHelper.Remove(ref CopyScale);

        if (converted != ConstraintKind.Position && converted != ConstraintKind.Rotation)
            RemoveFollow();
    }

    ConstraintKind? Convert(ConstraintData data, List<ConstraintSourceData> sources, string name)
    {
        if (data.FreezeToWorld)
        {
            _report.Warning("freeze", $"{name} is frozen to world, which isn't supported for conversion yet.", Target);
            return null;
        }

        if (!data.AllAxes)
        {
            _report.Warning("axes", $"{name} only affects some axes, which isn't supported for conversion yet.", Target);
            return null;
        }

        var source = sources.OrderByDescending(s => s.Weight).First();
        var blends = sources.Count > 1 || source.Weight < FULL_WEIGHT;

        switch (data.Kind)
        {
            case ConstraintKind.Parent:
                ReportBlend(blends, name);
                SetupParent(data, source);
                return data.Kind;

            case ConstraintKind.Aim:
            case ConstraintKind.LookAt:
                ReportBlend(blends, name);
                SetupLookAt(data, source);
                return data.Kind;

            case ConstraintKind.Scale:
                ReportBlend(blends, name);
                SetupScale(data, source);
                return data.Kind;

            case ConstraintKind.Position:
            case ConstraintKind.Rotation:
                // Converting partial weights at full strength would look worse than not converting at all
                // (e.g. twist bones), so these are skipped
                if (blends)
                {
                    _report.Warning("blend", $"{name} blends multiple sources or uses partial weight, " +
                        $"which isn't supported for conversion yet.", Target);
                    return null;
                }

                return SetupFollow(data, source, name) ? data.Kind : (ConstraintKind?)null;
        }

        return null;
    }

    void ReportBlend(bool blends, string name)
    {
        if (blends)
            _report.Warning("blend", $"{name} blends multiple sources or uses partial weight. " +
                $"Only the source with the highest weight is converted, at full weight.", Target);
    }

    void SetupParent(ConstraintData data, ConstraintSourceData source)
    {
        var wrapper = ConverterComponentHelper.EnsureOn(ref VirtualParent, data.Target.gameObject);
        ResoniteMemberFilter.Set(wrapper, "OverrideParent", "LocalPosition", "LocalRotation", "LocalScale");

        var parent = wrapper.Data;

        parent.OverrideParent = source.Source.GetSlot();
        parent.LocalPosition = source.PositionOffset;
        parent.LocalRotation = Quaternion.Euler(source.RotationOffset);

        // Parent constraints don't affect scale, but VirtualParent does. Keep the current global scale.
        parent.LocalScale = ConverterComponentHelper.SafeDivide(data.Target.lossyScale, source.Source.lossyScale);
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

    bool SetupFollow(ConstraintData data, ConstraintSourceData source, string name)
    {
        var position = data.Kind == ConstraintKind.Position;

        if (data.SolveInLocalSpace)
        {
            var hasOffset = position ? data.PositionOffset != Vector3.zero : data.RotationOffset != Vector3.zero;

            if (hasOffset)
            {
                _report.Warning("localoffset", $"{name} solves in local space with an offset, " +
                    $"which isn't supported for conversion yet.", Target);
                return false;
            }

            // Both local values are in their own parent's space, so they can be copied directly
            GeneratedObjectHelper.Destroy(ref Anchor);
            GeneratedObjectHelper.Destroy(ref Follower);

            EnsureCopy(position, data.Target.gameObject, source.Source, data.Target);
            return true;
        }

        // Anchor follows the source, including the offset
        if (Anchor == null || Anchor.transform.parent != source.Source)
        {
            GeneratedObjectHelper.Destroy(ref Anchor);
            Anchor = GeneratedObjectHelper.Create(source.Source, "[Resonite] Constraint Anchor");
        }

        if (position)
        {
            GeneratedObjectHelper.SetLocalPose(Anchor.transform,
                source.Source.InverseTransformPoint(source.Source.position + data.PositionOffset), Quaternion.identity);
        }
        else
        {
            GeneratedObjectHelper.SetLocalPose(Anchor.transform, Vector3.zero, Quaternion.Euler(data.RotationOffset));
        }

        // Follower is next to the constrained object, so its local values are in the same space
        if (Follower == null || Follower.transform.parent != data.Target.parent)
        {
            GeneratedObjectHelper.Destroy(ref Follower);
            Follower = GeneratedObjectHelper.Create(data.Target.parent, "[Resonite] Constraint Follower");
        }

        var copyTransform = ConverterComponentHelper.GetOrAdd<FrooxEngine.CopyGlobalTransformWrapper>(Follower);
        ResoniteMemberFilter.Set(copyTransform, "Source");
        copyTransform.Data.Source = Anchor.transform.GetSlot();

        EnsureCopy(position, Follower, Follower.transform, data.Target);
        return true;
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
    }
}
