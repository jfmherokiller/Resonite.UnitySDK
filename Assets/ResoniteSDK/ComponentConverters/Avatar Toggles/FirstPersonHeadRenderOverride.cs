using System;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("")]
public sealed class FirstPersonHeadRenderOverride : ResoniteComponent
#if VRC_SDK_VRCSDK3
    , VRC.SDKBase.IEditorOnly
#endif
{
}

// Keep the nullable wire correction local until the generated primitive conversion preserves nullable types.
[Serializable]
[ResoniteTypeName("[FrooxEngine]FrooxEngine.RenderTransformOverride")]
public sealed class FirstPersonHeadRenderOverrideBinding : FrooxEngine.RenderTransformOverride
{
    public override void CollectMembers(Dictionary<string, ResoniteLink.Member> members, IConversionContext context)
    {
        base.CollectMembers(members, context);
        members["PositionOverride"] = new ResoniteLink.Field_Nullable_float3
        {
            ID = context.GetIdOrAllocate(PositionOverride_Element.Member),
            Value = PositionOverride?.ToResoniteLink()
        };
        members["RotationOverride"] = new ResoniteLink.Field_Nullable_floatQ
        {
            ID = context.GetIdOrAllocate(RotationOverride_Element.Member),
            Value = RotationOverride?.ToResoniteLink()
        };
        members["ScaleOverride"] = new ResoniteLink.Field_Nullable_float3
        {
            ID = context.GetIdOrAllocate(ScaleOverride_Element.Member),
            Value = ScaleOverride?.ToResoniteLink()
        };
    }
}
