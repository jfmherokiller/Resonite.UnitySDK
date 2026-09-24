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
/// Other actions (animation clips, materials...) are reported.
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
        var state = ReflectionAccessor.GetRaw(toggle, "state");

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
                    on.Set(new ObjectActiveTarget(obj), mode == "TurnOn" || (mode != "TurnOff" && !obj.gameObject.activeSelf));
                    break;

                case "BlendShapeAction":
                    CollectBlendShapes(avatarRoot, action, on);
                    break;

                default:
                    report.Warning(action.GetType().Name, $"VRCFury toggle {path} uses {action.GetType().Name}, which isn't converted.", target);
                    break;
            }
        }

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
