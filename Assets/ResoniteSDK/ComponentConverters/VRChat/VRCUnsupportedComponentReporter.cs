using UnityEngine;

/// <summary>
/// Reports VRChat components that don't have a Resonite equivalent yet, so it's clear what didn't convert.
/// </summary>
[ConvertsComponentType("VRC.SDK3.Dynamics.Contact.Components.VRCContactSender")]
[ConvertsComponentType("VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver")]
[ConvertsComponentType("VRC.SDK3.Avatars.Components.VRCStation")]
[ConvertsComponentType("VRC.SDK3.Avatars.Components.VRCHeadChop")]
public class VRCUnsupportedComponentReporter : ResoniteComponentConverter<Component>
{
    protected override void Initialize(Component target)
    {
        Debug.Log($"{target.GetType().Name} on {target.name} has no Resonite equivalent yet and won't be converted.", target);
    }

    protected override void UpdateConversion(Component target, IConversionContext context)
    {
    }

    protected override void Cleanup()
    {
    }
}
