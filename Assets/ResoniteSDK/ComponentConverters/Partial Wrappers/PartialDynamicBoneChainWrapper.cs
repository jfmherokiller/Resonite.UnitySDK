using System.Collections.Generic;

/// <summary>
/// Variant of the DynamicBoneChainWrapper that only sends the members listed in <see cref="Members"/>, so everything else
/// stays at Resonite defaults. See <see cref="PartialMembers"/>.
/// </summary>
public class PartialDynamicBoneChainWrapper : FrooxEngine.DynamicBoneChainWrapper
{
    public List<string> Members = new List<string>();

    public override ResoniteLink.Component CollectData(IConversionContext context)
    {
        var data = PartialMembers.Filter(base.CollectData(context), Members);

        // Bones have internal drives for the bone transforms, which must not be overwritten
        PartialMembers.StripInternalFromListElements(data, "Bones", "GrabOverride");
        return data;
    }
}
