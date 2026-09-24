using System.Linq;
using UnityEngine;

/// <summary>
/// Applies VRChat spatial audio settings on top of the AudioSource conversion.
/// </summary>
[ConvertsComponentType(VRChatTypes.SpatialAudioSource)]
[ConvertsComponentType(VRChatTypes.SpatialAudioSourceWorlds)]
public class VRCSpatialAudioSourceConverter : ResoniteComponentConverter<Component>
{
    protected override void UpdateConversion(Component target, IConversionContext context)
    {
        var source = target.GetComponent<AudioSource>();

        if (source == null)
            return;

        // The AudioSource is required by the VRChat component, so it's always before it on the GameObject
        // and has been converted already at this point.
        if (!TryApply(target, source))
            Debug.LogWarning($"AudioSource on {target.name} hasn't been converted before {target.GetType().Name}. " +
                $"Spatial audio settings won't be applied.", target);
    }

    static bool TryApply(Component target, AudioSource source)
    {
        var converter = source.GetComponents<AudioSourceConverter>().FirstOrDefault(c => c.Target == source);

        if (converter == null || converter.Output == null)
            return false;

        var output = converter.Output.Data;

        if (!ReflectionAccessor.Get(target, "EnableSpatialization", true))
            output.SpatialBlend = 0;

        // By default, VRChat overrides the AudioSource's falloff with its own
        if (!ReflectionAccessor.Get(target, "UseAudioSourceVolumeCurve", false))
        {
            var near = ReflectionAccessor.Get(target, "Near", 0f);
            var far = ReflectionAccessor.Get(target, "Far", 40f);
            var volumetricRadius = ReflectionAccessor.Get(target, "VolumetricRadius", 0f);

            output.MinDistance = Mathf.Max(near, volumetricRadius);
            output.MaxDistance = Mathf.Max(output.MinDistance, far);
            output.RolloffMode = Awwdio.AudioRolloffCurve.Logarithmic;
        }

        // Gain is intentionally not converted - VRChat boosts avatar audio by default, which would be very loud in Resonite

        return true;
    }

    protected override void Cleanup()
    {
    }
}
