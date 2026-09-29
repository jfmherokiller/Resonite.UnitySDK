using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts VRCFury toggles into Resonite context menu toggles (see <see cref="AvatarToggleBuilder"/>),
/// following VRCFury's menu paths.
///
/// Supported actions:
/// - Object toggle (turn on / turn off / toggle) - drives the slot's active state
/// - Blendshape - drives the blendshape weight
/// - Animation clip - read the same way as an FX layer clip (see <see cref="AnimatorToggleAnalyzer.ReadClips"/>),
///   with paths resolved relative to the avatar root (VRCFury plays these as if they were assigned directly to an
///   FX state, so they use the same path convention as a normal Animator clip, not a path relative to this
///   component). Only object active state, renderer enabled and blendshapes are read from it, same as any other
///   clip - a clip that also animates e.g. a Transform or a third party component's field will get those specific
///   bindings reported as unsupported, but everything else still converts.
/// - Material swap / material property - drives the renderer's material slot, see <see cref="MaterialVariants"/>
/// Other actions are reported.
/// </summary>
public static class VRCFuryToggle
{
    public static void Apply(Component target, object toggle, ref GameObject toggleObject, IConversionContext context,
        ConversionReporter report)
    {
        var avatarRoot = VRChatTypes.FindAvatarRoot(target.transform);
        var path = ReflectionAccessor.Get(toggle, "name", "");

        if (string.IsNullOrWhiteSpace(path))
            path = target.name;

        if (ReflectionAccessor.Get(toggle, "slider", false))
            report.Warning("slider", $"VRCFury toggle {path} is a slider, which is converted as a simple on/off toggle.", target);

        var on = new ToggleState();
        var unsupported = new HashSet<string>();
        var state = ReflectionAccessor.GetRaw(toggle, "state");

        ReadState(avatarRoot, state, on, unsupported, report, target, $"toggle {path}");

        if (unsupported.Count > 0)
            report.Info("unsupported:" + path, $"VRCFury toggle {path} also animates {string.Join(", ", unsupported)}, " +
                $"which isn't converted.", target);

        if (on.IsEmpty)
        {
            GeneratedObjectHelper.DestroyWithEmptyParents(ref toggleObject);
            return;
        }

        var segments = VRCFuryTypes.SplitMenuPath(path);
        var menu = AvatarToggleBuilder.EnsureMenu(avatarRoot, segments.Take(Math.Max(0, segments.Length - 1)));
        var label = segments.Length > 0 ? segments[segments.Length - 1] : path;

        AvatarToggleBuilder.EnsureItem(ref toggleObject, menu, label);
        AvatarToggleBuilder.BuildToggle(toggleObject, ReflectionAccessor.Get(toggle, "defaultOn", false), on, new ToggleState(), context);
    }

    /// <summary>
    /// Reads a VRCFury State's actions into a ToggleState. Shared between Toggle (menu-driven, only the state's
    /// "on" values) and ApplyDuringUpload (applied unconditionally, no menu item at all).
    /// </summary>
    /// <param name="label">Identifies the source in warning messages, e.g. "toggle Clothes/Hoodie".</param>
    public static void ReadState(Transform avatarRoot, object state, ToggleState result, HashSet<string> unsupported,
        ConversionReporter report, Component target, string label)
    {
        foreach (var action in ReflectionAccessor.GetList(state, "actions"))
        {
            if (action == null)
                continue;

            switch (action.GetType().Name)
            {
                case "ObjectToggleAction":
                    var obj = ReflectionAccessor.GetTransform(action, "obj");

                    if (obj == null)
                        break;

                    // TurnOn, TurnOff or Toggle (flips the current state)
                    var mode = ReflectionAccessor.GetEnumName(action, "mode", "TurnOn");
                    result.Set(new ObjectActiveTarget(obj), mode == "TurnOn" || (mode != "TurnOff" && !obj.gameObject.activeSelf));
                    break;

                case "BlendShapeAction":
                    CollectBlendShapes(avatarRoot, action, result);
                    break;

                case "MaterialAction":
                    ReadMaterialSwap(action, result);
                    break;

                case "MaterialPropertyAction":
                    ReadMaterialProperty(avatarRoot, action, result, unsupported);
                    break;

#if UNITY_EDITOR
                case "AnimationClipAction":
                    var clip = GetAnimationClip(action);

                    if (clip != null)
                        AnimatorToggleAnalyzer.ReadClips(new[] { clip }, avatarRoot, result, unsupported);
                    break;
#endif

                default:
                    report.Warning(action.GetType().Name, $"VRCFury {label} uses {action.GetType().Name}, which isn't converted.", target);
                    break;
            }
        }
    }

    /// <summary>
    /// AnimationClipAction stores its clip wrapped in a GuidAnimationClip (VRCFury's asset-by-GUID reference type,
    /// with the direct reference kept in its "objRef" field), or as a plain Motion in "motion" for older data.
    /// </summary>
    static AnimationClip GetAnimationClip(object action)
    {
        var wrapper = ReflectionAccessor.GetRaw(action, "clip");
        var clip = wrapper != null ? ReflectionAccessor.GetObject<AnimationClip>(wrapper, "objRef") : null;

        return clip != null ? clip : ReflectionAccessor.GetObject<Motion>(action, "motion") as AnimationClip;
    }

    /// <summary>
    /// Material Swap: replaces one material slot of a renderer
    /// </summary>
    static void ReadMaterialSwap(object action, ToggleState result)
    {
        var renderer = ReflectionAccessor.GetObject<Renderer>(action, "renderer");
        var slot = ReflectionAccessor.GetInt(action, "materialIndex");
        var wrapper = ReflectionAccessor.GetRaw(action, "mat");
        var material = wrapper != null ? ReflectionAccessor.GetObject<Material>(wrapper, "objRef") : null;

        if (renderer != null && material != null && slot >= 0 && slot < renderer.sharedMaterials.Length)
            result.SetMaterial(new MaterialSlotTarget(renderer, slot), material);
    }

    /// <summary>
    /// Material Property: sets a shader property on one renderer or all renderers of the avatar, on every material
    /// that has it. Values are applied the same way VRCFury builds its animation (per component for colors/vectors).
    /// </summary>
    static void ReadMaterialProperty(Transform avatarRoot, object action, ToggleState result, HashSet<string> unsupported)
    {
        var property = ReflectionAccessor.Get(action, "propertyName", "");

        // VRCFury itself ignores single components of a bundle
        if (string.IsNullOrEmpty(property) || property.Contains("."))
            return;

        var renderers = ReflectionAccessor.Get(action, "affectAllMeshes", false)
            ? avatarRoot.GetComponentsInChildren<Renderer>(true)
            : new[] { ReflectionAccessor.GetObject<GameObject>(action, "renderer2")?.GetComponent<Renderer>() };

        var values = new List<(string suffix, float value)>();
        var type = ReflectionAccessor.GetEnumName(action, "propertyType", "Float");

        // LegacyAuto: detect from the materials like VRCFury does
        if (type == "LegacyAuto")
            type = DetectPropertyType(renderers, property);

        switch (type)
        {
            case "Color":
                var color = ReflectionAccessor.Get(action, "valueColor", Color.white);
                values.AddRange(new[] { (".r", color.r), (".g", color.g), (".b", color.b), (".a", color.a) });
                break;

            case "Vector":
            case "St":
                var vector = ReflectionAccessor.Get(action, "valueVector", Vector4.zero);
                values.AddRange(new[] { (".x", vector.x), (".y", vector.y), (".z", vector.z), (".w", vector.w) });
                break;

            default:
                values.Add(("", ReflectionAccessor.Get(action, "value", 0f)));
                break;
        }

        var applied = false;

        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                foreach (var (suffix, value) in values)
                    if (MaterialVariants.Has(renderer.sharedMaterials[slot], property + suffix))
                    {
                        result.SetMaterialProperty(new MaterialSlotTarget(renderer, slot), property + suffix, value);
                        applied = true;
                    }
        }

        if (!applied)
            unsupported.Add($"material property {property}");
    }

    static string DetectPropertyType(IEnumerable<Renderer> renderers, string property)
    {
        foreach (var material in renderers.Where(r => r != null).SelectMany(r => r.sharedMaterials))
        {
            if (MaterialVariants.Has(material, property + ".r"))
                return property.EndsWith("_ST") ? "St" : "Color";

            if (MaterialVariants.Has(material, property))
                return "Float";
        }

        return "Float";
    }

    static void CollectBlendShapes(Transform avatarRoot, object action, ToggleState on)
    {
        var name = ReflectionAccessor.Get(action, "blendShape", "");

        if (string.IsNullOrEmpty(name))
            return;

        var value = ReflectionAccessor.Get(action, "blendShapeValue", 100f);

        IEnumerable<SkinnedMeshRenderer> renderers;

        if (ReflectionAccessor.Get(action, "allRenderers", true))
            renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        else
        {
            var renderer = ReflectionAccessor.GetObject<SkinnedMeshRenderer>(action, "renderer");
            renderers = renderer != null ? new[] { renderer } : Array.Empty<SkinnedMeshRenderer>();
        }

        foreach (var renderer in renderers)
        {
            var index = renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(name) : -1;

            if (index < 0)
                continue;

            var target = new BlendShapeTarget(renderer, index);
            on.Set(target, target.Normalize(value));
        }
    }
}
