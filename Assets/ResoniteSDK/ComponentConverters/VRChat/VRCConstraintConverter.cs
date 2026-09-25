using UnityEngine;

/// <summary>
/// Converts VRChat constraints (VRC.Dynamics). These are mostly equivalent to Unity's constraints, so they're
/// translated into the same intermediate description and converted with the same logic.
/// </summary>
[ConvertsComponentType(VRChatTypes.ParentConstraint)]
[ConvertsComponentType(VRChatTypes.PositionConstraint)]
[ConvertsComponentType(VRChatTypes.RotationConstraint)]
[ConvertsComponentType(VRChatTypes.ScaleConstraint)]
[ConvertsComponentType(VRChatTypes.AimConstraint)]
[ConvertsComponentType(VRChatTypes.LookAtConstraint)]
public class VRCConstraintConverter : ConstraintConverterBase<Component>
{
    protected override ConstraintData ReadConstraint(Component target)
    {
        var data = new ConstraintData();

        switch (target.GetType().Name)
        {
            case "VRCParentConstraint": data.Kind = ConstraintKind.Parent; break;
            case "VRCPositionConstraint": data.Kind = ConstraintKind.Position; break;
            case "VRCRotationConstraint": data.Kind = ConstraintKind.Rotation; break;
            case "VRCScaleConstraint": data.Kind = ConstraintKind.Scale; break;
            case "VRCAimConstraint": data.Kind = ConstraintKind.Aim; break;
            case "VRCLookAtConstraint": data.Kind = ConstraintKind.LookAt; break;
        }

        var enabled = !(target is Behaviour behaviour) || behaviour.enabled;

        data.Active = enabled && ReflectionAccessor.Get(target, "IsActive", true);
        data.GlobalWeight = ReflectionAccessor.Get(target, "GlobalWeight", 1f);

        // Constraints can target a different transform than the one they're on
        var targetTransform = ReflectionAccessor.GetTransform(target, "TargetTransform");
        data.Target = targetTransform != null ? targetTransform : target.transform;

        data.SolveInLocalSpace = ReflectionAccessor.Get(target, "SolveInLocalSpace", false);
        data.FreezeToWorld = ReflectionAccessor.Get(target, "FreezeToWorld", false);

        foreach (var source in ReflectionAccessor.GetList(target, "Sources"))
            data.Sources.Add(new ConstraintSourceData()
            {
                Source = ReflectionAccessor.GetTransform(source, "SourceTransform"),
                Weight = ReflectionAccessor.Get(source, "Weight", 1f),
                PositionOffset = ReflectionAccessor.Get(source, "ParentPositionOffset", Vector3.zero),
                RotationOffset = ReflectionAccessor.Get(source, "ParentRotationOffset", Vector3.zero),
            });

        data.PositionOffset = ReflectionAccessor.Get(target, "PositionOffset", Vector3.zero);
        data.RotationOffset = ReflectionAccessor.Get(target, "RotationOffset", Vector3.zero);
        data.ScaleOffset = ReflectionAccessor.Get(target, "ScaleOffset", Vector3.one);

        if (ReflectionAccessor.Has(target, "PositionAtRest"))
            data.RestPosition = ReflectionAccessor.Get(target, "PositionAtRest", Vector3.zero);

        if (ReflectionAccessor.Has(target, "RotationAtRest"))
            data.RestRotation = Quaternion.Euler(ReflectionAccessor.Get(target, "RotationAtRest", Vector3.zero));

        // Axes default to affected when the fields are missing
        data.PositionX = ReflectionAccessor.Get(target, "AffectsPositionX", true);
        data.PositionY = ReflectionAccessor.Get(target, "AffectsPositionY", true);
        data.PositionZ = ReflectionAccessor.Get(target, "AffectsPositionZ", true);
        data.AllRotationAxes = AffectsAll(target, "AffectsRotation");
        data.AllScaleAxes = AffectsAll(target, "AffectsScale");

        switch (data.Kind)
        {
            case ConstraintKind.Aim:
                data.AimAxis = ReflectionAccessor.Get(target, "AimAxis", Vector3.forward);
                data.UpAxis = ReflectionAccessor.Get(target, "UpAxis", Vector3.up);

                // The enum names match Unity's AimConstraint.WorldUpType
                System.Enum.TryParse(ReflectionAccessor.GetEnumName(target, "WorldUp", "SceneUp"),
                    out UnityEngine.Animations.AimConstraint.WorldUpType worldUp);

                data.WorldUp = ConstraintData.ResolveWorldUp(worldUp,
                    ReflectionAccessor.Get(target, "WorldUpVector", Vector3.up),
                    ReflectionAccessor.GetTransform(target, "WorldUpTransform"));
                break;

            case ConstraintKind.LookAt:
                data.Roll = ReflectionAccessor.Get(target, "Roll", 0f);

                var upTransform = ReflectionAccessor.GetTransform(target, "WorldUpTransform");

                if (ReflectionAccessor.Get(target, "UseUpTransform", false) && upTransform != null)
                    data.WorldUp = upTransform.up;
                break;
        }

        return data;
    }

    static bool AffectsAll(Component target, string prefix) =>
        ReflectionAccessor.Get(target, prefix + "X", true)
        && ReflectionAccessor.Get(target, prefix + "Y", true)
        && ReflectionAccessor.Get(target, prefix + "Z", true);
}
