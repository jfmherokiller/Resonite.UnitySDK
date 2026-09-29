using FrooxEngine;
using UnityEngine;

public class PoiyomiBlendModeComputer
{
    public static BlendMode FromPoiyomi(UnityEngine.Material material)
    {
        // Locked Poiyomi variants that need a grab pass (refraction, glass/wet-eye style distortion effects) render
        // their "transparency" by sampling and redistorting the screen behind them, not through alpha blending -
        // _SrcBlend/_DstBlend read as a plain opaque overwrite either way, so none of the blend heuristics below
        // ever match and the Cutout fallback kicks in. With AlphaCutoff typically 0 (nothing gets cut) that renders
        // fully opaque using the flat AlbedoColor - often a glaring solid white, since these materials are usually
        // untextured. The effect itself can't be reproduced with a PBS material, so fall back to Transparent
        // instead, which at least respects a low AlbedoColor alpha and reads as faint/see-through rather than solid.
        if (material.shader.name.Contains("Grab Pass"))
        {
            Debug.LogWarning($"Material {material.name} uses a Poiyomi grab pass (refraction/distortion) variant, " +
                $"which can't be converted - falling back to a transparent material.", material);
            return BlendMode.Transparent;
        }

        if (material.GetFloat("_AlphaForceOpaque") > 0)
        {
            return BlendMode.Opaque;
        }

        if (material.GetFloat("_DstBlend") == 10)
        {
            // TransClipping, Fade, Transparent
            return BlendMode.Transparent;
        }

        if (material.GetFloat("_DstBlend") == 1)
        {
            // Additive, Soft Additive
            return BlendMode.Additive;
        }

        if (material.GetFloat("_SrcBlend") == 2)
        {
            // Multiplicative, 2x Multiplicative
            return BlendMode.Multiply;
        }

        if (
            material.GetFloat("_AlphaToCoverage") > 0 ||
            material.GetFloat("_AlphaDithering") > 0 ||
            material.GetFloat("_AlphaDistanceFade") > 0
        )
        {
            // TODO: Figure out how Poiyomi's advanced alpha parameters work exactly,
            return BlendMode.Alpha;
        }
        else
        {
            return BlendMode.Cutout;
        }
    }
}
