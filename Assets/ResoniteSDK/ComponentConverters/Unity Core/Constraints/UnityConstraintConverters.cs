using UnityEngine;
using UnityEngine.Animations;

public class ParentConstraintConverter : ConstraintConverterBase<ParentConstraint>
{
    protected override ConstraintData ReadConstraint(ParentConstraint target)
    {
        var data = new ConstraintData()
        {
            Kind = ConstraintKind.Parent,
            Active = target.constraintActive && target.enabled,
            Target = target.transform,
            GlobalWeight = target.weight,
            AllRotationAxes = ConstraintData.IsAllAxes(target.rotationAxis),
            RestPosition = target.translationAtRest,
            RestRotation = Quaternion.Euler(target.rotationAtRest),
        };

        data.SetPositionAxes(target.translationAxis);

        for (int i = 0; i < target.sourceCount; i++)
        {
            var source = target.GetSource(i);

            data.Sources.Add(new ConstraintSourceData()
            {
                Source = source.sourceTransform,
                Weight = source.weight,
                PositionOffset = target.GetTranslationOffset(i),
                RotationOffset = target.GetRotationOffset(i),
            });
        }

        return data;
    }
}

public class PositionConstraintConverter : ConstraintConverterBase<PositionConstraint>
{
    protected override ConstraintData ReadConstraint(PositionConstraint target)
    {
        var data = new ConstraintData()
        {
            Kind = ConstraintKind.Position,
            Active = target.constraintActive && target.enabled,
            Target = target.transform,
            PositionOffset = target.translationOffset,
            RestPosition = target.translationAtRest,
        };

        data.SetPositionAxes(target.translationAxis);
        ConstraintData.ReadSources(target, data);

        return data;
    }
}

public class RotationConstraintConverter : ConstraintConverterBase<RotationConstraint>
{
    protected override ConstraintData ReadConstraint(RotationConstraint target)
    {
        var data = new ConstraintData()
        {
            Kind = ConstraintKind.Rotation,
            Active = target.constraintActive && target.enabled,
            Target = target.transform,
            AllRotationAxes = ConstraintData.IsAllAxes(target.rotationAxis),
            RotationOffset = target.rotationOffset,
            RestRotation = Quaternion.Euler(target.rotationAtRest),
        };

        ConstraintData.ReadSources(target, data);

        return data;
    }
}

public class AimConstraintConverter : ConstraintConverterBase<AimConstraint>
{
    protected override ConstraintData ReadConstraint(AimConstraint target)
    {
        var data = new ConstraintData()
        {
            Kind = ConstraintKind.Aim,
            Active = target.constraintActive && target.enabled,
            Target = target.transform,
            AllRotationAxes = ConstraintData.IsAllAxes(target.rotationAxis),
            AimAxis = target.aimVector,
            UpAxis = target.upVector,
            RotationOffset = target.rotationOffset,
            WorldUp = ConstraintData.ResolveWorldUp(target.worldUpType, target.worldUpVector, target.worldUpObject),
        };

        ConstraintData.ReadSources(target, data);

        return data;
    }
}

public class LookAtConstraintConverter : ConstraintConverterBase<LookAtConstraint>
{
    protected override ConstraintData ReadConstraint(LookAtConstraint target)
    {
        var data = new ConstraintData()
        {
            Kind = ConstraintKind.LookAt,
            Active = target.constraintActive && target.enabled,
            Target = target.transform,
            Roll = target.roll,
            RotationOffset = target.rotationOffset,
            WorldUp = target.useUpObject && target.worldUpObject != null ? target.worldUpObject.up : Vector3.up,
        };

        ConstraintData.ReadSources(target, data);

        return data;
    }
}

public class ScaleConstraintConverter : ConstraintConverterBase<ScaleConstraint>
{
    protected override ConstraintData ReadConstraint(ScaleConstraint target)
    {
        var data = new ConstraintData()
        {
            Kind = ConstraintKind.Scale,
            Active = target.constraintActive && target.enabled,
            Target = target.transform,
            AllScaleAxes = ConstraintData.IsAllAxes(target.scalingAxis),
            ScaleOffset = target.scaleOffset,
        };

        ConstraintData.ReadSources(target, data);

        return data;
    }
}
