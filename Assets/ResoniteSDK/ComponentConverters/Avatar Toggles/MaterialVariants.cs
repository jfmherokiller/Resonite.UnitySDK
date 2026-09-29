using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Material variants for animated material properties. Resonite toggles can't animate a converted material's
/// properties directly (each shader converter maps them differently), so a copy of the material with the animated
/// values applied is converted by the regular material converters, and the toggle swaps it in.
/// Variants aren't saved and are shared by equal (material, properties) pairs.
/// </summary>
public static class MaterialVariants
{
    static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

    /// <summary>
    /// The material with the properties applied, or the material itself if there are none
    /// </summary>
    public static Material Get(Material material, IReadOnlyDictionary<string, float> properties)
    {
        if (material == null || properties == null || properties.Count == 0)
            return material;

        var key = material.GetInstanceID() + "|" + string.Join("|", properties.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value:R}"));

        if (!_cache.TryGetValue(key, out var variant) || variant == null)
        {
            variant = new Material(material)
            {
                name = $"{material.name} (toggle variant)",
                hideFlags = HideFlags.DontSave,
            };

            _cache[key] = variant;
        }
        else
        {
            // Follow edits to the source material (realtime mode reconverts)
            variant.shader = material.shader;
            variant.CopyPropertiesFromMaterial(material);
        }

        foreach (var property in properties)
            TryApply(variant, property.Key, property.Value);

        return variant;
    }

    /// <summary>
    /// Whether the material has the animated property, e.g. "_Color.r", "_Glossiness" or "_MainTex_ST.z"
    /// </summary>
    public static bool Has(Material material, string property) => Resolve(material, property, out _, out _, out _);

    /// <summary>
    /// Current value of an animated property, as its curve would read it (0 if the material doesn't have it)
    /// </summary>
    public static float GetValue(Material material, string property)
    {
        if (!Resolve(material, property, out var name, out var type, out var component))
            return 0;

        switch (type)
        {
            case ShaderPropertyType.Int: return material.GetInteger(name);
            case ShaderPropertyType.Color: return material.GetColor(name)[component];
            case ShaderPropertyType.Vector: return material.GetVector(name)[component];
            case ShaderPropertyType.Texture:
                return component < 2 ? material.GetTextureScale(name)[component] : material.GetTextureOffset(name)[component - 2];
            default: return material.GetFloat(name);
        }
    }

    /// <summary>
    /// Sets an animated property the way an animation curve would (one component of colors/vectors at a time)
    /// </summary>
    public static bool TryApply(Material material, string property, float value)
    {
        if (!Resolve(material, property, out var name, out var type, out var component))
            return false;

        switch (type)
        {
            case ShaderPropertyType.Float:
            case ShaderPropertyType.Range:
                material.SetFloat(name, value);
                break;

            case ShaderPropertyType.Int:
                material.SetInteger(name, Mathf.RoundToInt(value));
                break;

            case ShaderPropertyType.Color:
                var color = material.GetColor(name);
                color[component] = value;
                material.SetColor(name, color);
                break;

            case ShaderPropertyType.Vector:
                var vector = material.GetVector(name);
                vector[component] = value;
                material.SetVector(name, vector);
                break;

            case ShaderPropertyType.Texture:
                // Texture scale/offset (_ST): x/y scale, z/w offset
                if (component < 2)
                {
                    var scale = material.GetTextureScale(name);
                    scale[component] = value;
                    material.SetTextureScale(name, scale);
                }
                else
                {
                    var offset = material.GetTextureOffset(name);
                    offset[component - 2] = value;
                    material.SetTextureOffset(name, offset);
                }
                break;
        }

        return true;
    }

    static bool Resolve(Material material, string property, out string name, out ShaderPropertyType type, out int component)
    {
        name = property;
        type = default;
        component = 0;

        if (material == null || material.shader == null || string.IsNullOrEmpty(property))
            return false;

        var dot = property.LastIndexOf('.');

        if (dot > 0)
        {
            component = "rgbaxyzw".IndexOf(property[dot + 1]) % 4;

            if (dot != property.Length - 2 || component < 0)
                return false;

            name = property.Substring(0, dot);
        }

        var shader = material.shader;
        var index = shader.FindPropertyIndex(name);

        if (index >= 0)
        {
            type = shader.GetPropertyType(index);

            // Colors and vectors are animated per component, textures only through their _ST
            return type != ShaderPropertyType.Texture && (dot > 0) == (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector);
        }

        if (dot > 0 && name.EndsWith("_ST"))
        {
            name = name.Substring(0, name.Length - 3);
            index = shader.FindPropertyIndex(name);
            type = ShaderPropertyType.Texture;

            return index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Texture;
        }

        return false;
    }
}
