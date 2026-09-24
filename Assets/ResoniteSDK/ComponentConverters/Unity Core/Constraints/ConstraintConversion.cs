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
                Weight = source.weight,
            });
        }
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
/// Handles creating Resonite components that approximate a constraint.
/// - Parent constraint maps to VirtualParent
/// - Aim & LookAt constraints map to LookAt
/// - Scale constraint maps to CopyGlobalScale
/// Position & Rotation constraints currently don't have a good equivalent without ProtoFlux, so they're reported.
/// </summary>
public abstract class ConstraintConverterBase<T> : ResoniteComponentConverter<T>
    where T : Component
{
    public PartialVirtualParentWrapper VirtualParent;
    public PartialLookAtWrapper LookAt;
    public PartialCopyGlobalScaleWrapper CopyScale;

    HashSet<string> _reported = new HashSet<string>();

    protected abstract ConstraintData ReadConstraint(T target);

    protected override void UpdateConversion(T target, IConversionContext context)
    {
        var data = ReadConstraint(target);

        var sources = data.Sources.Where(s => s.Source != null && s.Weight > 0).ToList();

        GameObject parentTarget = null, lookAtTarget = null, scaleTarget = null;

        if (data.Active && data.Target != null && sources.Count > 0)
        {
            if (sources.Count > 1)
                ReportOnce("multisource", $"{target.GetType().Name} on {target.name} has {sources.Count} sources. " +
                    $"Only the one with highest weight is converted.");

            var source = sources.OrderByDescending(s => s.Weight).First();

            switch (data.Kind)
            {
                case ConstraintKind.Parent:
                    parentTarget = data.Target.gameObject;
                    break;

                case ConstraintKind.Aim:
                case ConstraintKind.LookAt:
                    lookAtTarget = data.Target.gameObject;
                    break;

                case ConstraintKind.Scale:
                    scaleTarget = data.Target.gameObject;
                    break;

                default:
                    ReportOnce("unsupported", $"{target.GetType().Name} on {target.name} isn't supported for conversion yet " +
                        $"(Resonite has no direct equivalent for {data.Kind} constraint).");
                    break;
            }

            if (parentTarget != null)
                SetupParent(data, source, parentTarget);

            if (lookAtTarget != null)
                SetupLookAt(data, source, lookAtTarget);

            if (scaleTarget != null)
                SetupScale(data, source, scaleTarget);
        }

        // Remove anything that's not used (anymore)
        if (parentTarget == null)
            ConverterComponentHelper.Remove(ref VirtualParent);

        if (lookAtTarget == null)
            ConverterComponentHelper.Remove(ref LookAt);

        if (scaleTarget == null)
            ConverterComponentHelper.Remove(ref CopyScale);
    }

    void SetupParent(ConstraintData data, ConstraintSourceData source, GameObject target)
    {
        var wrapper = ConverterComponentHelper.EnsureOn(ref VirtualParent, target);
        wrapper.Members = new List<string> { "OverrideParent", "LocalPosition", "LocalRotation", "LocalScale" };

        var parent = wrapper.Data;

        parent.persistent = true;
        parent.Enabled = true;
        parent.OverrideParent = source.Source.GetSlot();

        parent.LocalPosition = source.PositionOffset;
        parent.LocalRotation = Quaternion.Euler(source.RotationOffset);

        // Parent constraints don't affect scale, but VirtualParent does. Keep the current global scale.
        parent.LocalScale = ConverterComponentHelper.SafeDivide(data.Target.lossyScale, source.Source.lossyScale);
    }

    void SetupLookAt(ConstraintData data, ConstraintSourceData source, GameObject target)
    {
        var wrapper = ConverterComponentHelper.EnsureOn(ref LookAt, target);
        wrapper.Members = new List<string> { "Target", "Up", "RotationOffset" };

        var lookAt = wrapper.Data;

        lookAt.persistent = true;
        lookAt.Enabled = true;
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

    void SetupScale(ConstraintData data, ConstraintSourceData source, GameObject target)
    {
        var wrapper = ConverterComponentHelper.EnsureOn(ref CopyScale, target);
        wrapper.Members = new List<string> { "Source", "NonUniform" };

        var scale = wrapper.Data;

        scale.persistent = true;
        scale.Enabled = true;
        scale.Source = source.Source.GetSlot();
        scale.NonUniform = true;

        if (data.ScaleOffset != Vector3.one)
            ReportOnce("scaleoffset", $"Scale offset on {data.Target.name} is not supported and will be ignored.");
    }

    static Quaternion SafeLookRotation(Vector3 forward, Vector3 up)
    {
        if (forward.sqrMagnitude < 1e-8f)
            forward = Vector3.forward;

        if (up.sqrMagnitude < 1e-8f || Vector3.Cross(forward, up).sqrMagnitude < 1e-8f)
            up = Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        return Quaternion.LookRotation(forward, up);
    }

    protected void ReportOnce(string key, string message)
    {
        if (_reported == null)
            _reported = new HashSet<string>();

        if (_reported.Add(key))
            Debug.LogWarning(message, Target);
    }

    protected override void Cleanup()
    {
        ConverterComponentHelper.Remove(ref VirtualParent);
        ConverterComponentHelper.Remove(ref LookAt);
        ConverterComponentHelper.Remove(ref CopyScale);
    }
}
