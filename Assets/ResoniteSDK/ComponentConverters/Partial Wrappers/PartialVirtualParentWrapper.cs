using System.Collections.Generic;

/// <summary>
/// Variant of the VirtualParentWrapper that only sends the members listed in <see cref="Members"/>, so everything else
/// stays at Resonite defaults. See <see cref="PartialMembers"/>.
/// </summary>
public class PartialVirtualParentWrapper : FrooxEngine.VirtualParentWrapper
{
    public List<string> Members = new List<string>();

    public override ResoniteLink.Component CollectData(IConversionContext context)
    {
        var data = PartialMembers.Filter(base.CollectData(context), Members);

        return data;
    }
}
