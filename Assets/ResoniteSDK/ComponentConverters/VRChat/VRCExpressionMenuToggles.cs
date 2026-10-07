#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A VRChat expression menu to convert, together with the parameters and animator controllers that define
/// what its toggles do. This is either the avatar descriptor's menu, or a menu merged in by VRCFury.
/// </summary>
public class ExpressionMenuSource
{
    /// <summary>
    /// Menus (VRCExpressionsMenu) with the menu path where they appear
    /// </summary>
    public List<(object menu, string[] path)> Menus = new List<(object, string[])>();

    /// <summary>
    /// Parameters (VRCExpressionParameters)
    /// </summary>
    public List<object> Parameters = new List<object>();

    /// <summary>
    /// FX controllers
    /// </summary>
    public List<RuntimeAnimatorController> Controllers = new List<RuntimeAnimatorController>();

    /// <summary>
    /// Resolves the animated paths to transforms
    /// </summary>
    public Func<string, Transform> ResolvePath;
}

/// <summary>
/// Converts the toggles in VRChat expression menus into Resonite context menu toggles (see <see cref="AvatarToggleBuilder"/>),
/// following the menus' submenus.
///
/// Menus can contain the same submenu in multiple places, or even link back to one of their parents. Submenus are
/// followed everywhere they appear, except when they would recurse into one of their own parents.
///
/// What each toggle does is determined from the FX animator controllers (see <see cref="AnimatorToggleAnalyzer"/>):
/// - Bool and float parameters (and int parameters with a single value) become on/off toggles. If the same toggle
///   appears in multiple places, each item flips the same state.
/// - Int parameters with multiple values become selectors, where each item selects its value.
/// - Buttons work like toggles (or selector options), but only while held, like in VRChat (ProtoFlux on press/release).
/// - Radial puppets become a submenu of steps (0%, 25%...) setting the puppet's value, which drives the properties
///   through gradients sampled from the animator (including 1D/2D blend trees and motion time). Resonite's context
///   menu has no sliders, so the value can only be set in steps.
/// - Two/four axis puppets use a shared selector with nine direction presets, sampled with all axis parameters
///   together. The menu cannot provide continuous joystick positions.
///
/// Each property can only be driven by one toggle in Resonite, so if multiple parameters control the same property,
/// only the first one gets it.
/// </summary>
public static class VRCExpressionMenuToggles
{
    const int MAX_SELECTOR_INDEX = 255;
    const int MAX_MENU_DEPTH = 16;
    const int MAX_CONTROLS = 2000;

    // Values the puppet is sampled at, and the steps offered in its menu
    const int PUPPET_SAMPLES = 10;
    static readonly float[] PUPPET_STEPS = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    enum ControlKind { Toggle, Button, Puppet, AxisPuppet }

    class MenuControl
    {
        public string[] Folders;
        public string Label;
        public string Parameter;
        public float Value;
        public ControlKind Kind;
        public float Min;
        public string[] AxisParameters;
        public string[] AxisLabels;

        public bool IsPuppet => Kind == ControlKind.Puppet;
        public bool IsButton => Kind == ControlKind.Button;
    }

    static readonly string[] TWO_AXIS_LABELS = { "Horizontal", "Vertical" };
    static readonly string[] FOUR_AXIS_LABELS = { "Up", "Right", "Down", "Left" };
    static readonly Vector2[] AXIS_POSITIONS = {
        Vector2.zero, Vector2.up, Vector2.right, Vector2.down, Vector2.left,
        new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1), new Vector2(-1, 1)
    };

    /// <param name="owner">Component the menu belongs to. Selector states are created under it.</param>
    /// <param name="items">Generated menu items, reused between updates in the same order</param>
    /// <param name="selectors">Generated selector states, reused between updates in the same order</param>
    public static void Apply(Component owner, IEnumerable<ExpressionMenuSource> sources, Transform avatarRoot,
        List<GameObject> items, List<GameObject> selectors, IConversionContext context, ConversionReporter report)
    {
        var usedItems = 0;
        var usedSelectors = 0;

        GameObject NextItem(string[] folders, string label, string kind)
        {
            if (usedItems == items.Count)
                items.Add(null);

            var item = items[usedItems];
            AvatarToggleBuilder.EnsureItem(ref item, AvatarToggleBuilder.EnsureMenu(avatarRoot, folders), label, kind);
            items[usedItems++] = item;

            return item;
        }

        GameObject NextSelector(string name)
        {
            if (usedSelectors == selectors.Count)
                selectors.Add(null);

            var selector = selectors[usedSelectors];

            if (selector == null || selector.name != name)
            {
                GeneratedObjectHelper.Destroy(ref selector);
                selector = GeneratedObjectHelper.Create(owner.transform, name);
            }

            selectors[usedSelectors++] = selector;

            return selector;
        }

        // Properties already driven by a previous toggle
        var claimed = new HashSet<string>();

        foreach (var source in sources)
            Build(owner, source, claimed, context, report, NextItem, NextSelector);

        // Remove anything that's not used anymore
        for (int i = items.Count - 1; i >= usedItems; i--)
        {
            var item = items[i];
            GeneratedObjectHelper.DestroyWithEmptyParents(ref item);
            items.RemoveAt(i);
        }

        for (int i = selectors.Count - 1; i >= usedSelectors; i--)
        {
            var selector = selectors[i];
            GeneratedObjectHelper.Destroy(ref selector);
            selectors.RemoveAt(i);
        }
    }

    public static void Clear(List<GameObject> items, List<GameObject> selectors)
    {
        foreach (var list in new[] { items, selectors })
        {
            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                GeneratedObjectHelper.DestroyWithEmptyParents(ref obj);
            }

            list.Clear();
        }
    }

    /// <summary>
    /// Creates the source for the menu of a VRChat avatar descriptor
    /// </summary>
    public static ExpressionMenuSource FromDescriptor(Component descriptor)
    {
        if (!ReflectionAccessor.Get(descriptor, "customExpressions", false))
            return null;

        var source = new ExpressionMenuSource();
        var root = descriptor.transform;

        source.Menus.Add((ReflectionAccessor.GetRaw(descriptor, "expressionsMenu"), new string[0]));
        source.Parameters.Add(ReflectionAccessor.GetRaw(descriptor, "expressionParameters"));

        foreach (var layer in ReflectionAccessor.GetList(descriptor, "baseAnimationLayers"))
            if (ReflectionAccessor.GetEnumName(layer, "type") == "FX" && !ReflectionAccessor.Get(layer, "isDefault", true))
                source.Controllers.Add(ReflectionAccessor.GetObject<RuntimeAnimatorController>(layer, "animatorController"));

        source.ResolvePath = path => string.IsNullOrEmpty(path) ? root : root.Find(path);

        return source;
    }

    static void Build(Component owner, ExpressionMenuSource source, HashSet<string> claimed, IConversionContext context,
        ConversionReporter report, Func<string[], string, string, GameObject> nextItem, Func<string, GameObject> nextSelector)
    {
        var controllers = source.Controllers.Where(c => c != null).ToList();
        var menus = source.Menus.Where(m => m.menu is UnityEngine.Object o && o != null).ToList();

        if (menus.Count == 0)
            return;

        if (controllers.Count == 0)
        {
            report.Warning("nofx", $"Expression menu on {owner.name} has no FX controller. Menu toggles can't be converted.", owner);
            return;
        }

        var parameters = ReadParameters(source.Parameters);
        var controls = new List<MenuControl>();

        foreach (var (menu, path) in menus)
            CollectControls(menu, path, new HashSet<object>(), controls, owner, report);

        foreach (var group in controls.Where(c => c.AxisParameters != null)
            .GroupBy(c => string.Join("\u001f", c.AxisParameters)))
            BuildAxisPuppet(owner, source, controllers, parameters, group.ToList(), claimed,
                context, report, nextItem, nextSelector);

        foreach (var group in controls.Where(c => c.AxisParameters == null).GroupBy(c => c.Parameter))
        {
            var parameter = group.Key;

            if (!parameters.TryGetValue(parameter, out var definition))
            {
                report.Warning("param:" + parameter, $"Menu parameter {parameter} isn't in the expression parameters.", owner);
                continue;
            }

            var unsupported = new HashSet<string>();
            var options = group.ToList();

            if (options.Any(o => o.IsPuppet))
            {
                BuildPuppet(owner, source, controllers, parameter, definition.defaultValue, options.Where(o => o.IsPuppet).ToList(),
                    claimed, unsupported, context, report, nextItem, nextSelector);

                if (options.Any(o => !o.IsPuppet))
                    report.Info("puppettoggle:" + parameter, $"Menu parameter {parameter} is used by both a radial puppet and toggles, " +
                        $"only the puppet is converted.", owner);
            }
            // Float parameters are also used for mutually exclusive options (e.g. an outfit picker), typically
            // because a 1D blend tree - which only accepts float parameters - selects between them. BuildSelector
            // already handles this shape (distinct rounded values, one option active at a time); without this,
            // every option past the first only gets a link to the first option's own toggle.
            else if ((definition.type == "Int" || definition.type == "Float")
                && options.Select(o => Mathf.RoundToInt(o.Value)).Distinct().Count() > 1)
                BuildSelector(owner, source, controllers, parameter, definition.defaultValue, options, claimed, unsupported,
                    context, report, nextItem, nextSelector);
            else
                BuildToggle(owner, source, controllers, parameter, definition, options, claimed, unsupported,
                    context, report, nextItem);

            if (unsupported.Count > 0)
                report.Info("unsupported:" + parameter, $"Menu toggle for {parameter} also animates {string.Join(", ", unsupported)}, " +
                    $"which isn't converted.", owner);
        }
    }

    static void BuildAxisPuppet(Component owner, ExpressionMenuSource source, List<RuntimeAnimatorController> controllers,
        Dictionary<string, (string type, float defaultValue)> parameters, List<MenuControl> puppets,
        HashSet<string> claimed, IConversionContext context, ConversionReporter report,
        Func<string[], string, string, GameObject> nextItem, Func<string, GameObject> nextSelector)
    {
        var first = puppets[0];
        var names = first.AxisParameters;
        if (names.Any(p => !parameters.TryGetValue(p, out var definition) || definition.type != "Float"))
        {
            report.Warning("axis:" + string.Join(",", names), $"Axis puppet {first.Label} needs Float expression parameters for all axes.", owner);
            return;
        }

        var defaults = names.ToDictionary(p => p, p => parameters[p].defaultValue);
        var unsupported = new HashSet<string>();
        var states = new List<ToggleState> {
            AnimatorToggleAnalyzer.SampleParameters(controllers, defaults, source.ResolvePath, unsupported)
        };
        states.AddRange(AXIS_POSITIONS.Select(position =>
            AnimatorToggleAnalyzer.SampleParameters(controllers, AxisValues(names, position), source.ResolvePath, unsupported)));

        Claim(claimed, string.Join(",", names), report, owner, states.ToArray());
        if (states.All(s => s.IsEmpty))
        {
            report.Info("axisempty:" + first.Label, $"Axis puppet {first.Label} doesn't animate anything that can be converted.", owner);
            return;
        }

        var selector = nextSelector($"[Resonite] Axis Puppet {string.Join(",", names)}");
        var index = AvatarToggleBuilder.BuildSelector(selector, 0, states, context);
        foreach (var puppet in puppets)
        {
            var folders = puppet.Folders.Append(puppet.Label).ToArray();
            AvatarToggleBuilder.BuildSelectorOption(nextItem(folders, "Default", "Axis Step"), index, 0);
            for (var i = 0; i < AXIS_POSITIONS.Length; i++)
                AvatarToggleBuilder.BuildSelectorOption(nextItem(folders, AxisLabel(puppet.AxisLabels, AXIS_POSITIONS[i]), "Axis Step"), index, i + 1);
        }

        if (unsupported.Count > 0)
            report.Info("axisunsupported:" + first.Label, $"Axis puppet {first.Label} also animates {string.Join(", ", unsupported)}, which isn't converted.", owner);
    }

    static Dictionary<string, float> AxisValues(string[] names, Vector2 position)
    {
        if (names.Length == 2)
            return new Dictionary<string, float> { [names[0]] = position.x, [names[1]] = position.y };
        return new Dictionary<string, float> {
            [names[0]] = Mathf.Max(0, position.y), [names[1]] = Mathf.Max(0, position.x),
            [names[2]] = Mathf.Max(0, -position.y), [names[3]] = Mathf.Max(0, -position.x)
        };
    }

    static string AxisLabel(string[] labels, Vector2 position)
    {
        if (position == Vector2.zero) return "Center";
        var vertical = position.y > 0 ? labels[0] : position.y < 0 ? labels[2] : "";
        var horizontal = position.x > 0 ? labels[1] : position.x < 0 ? labels[3] : "";
        return string.IsNullOrEmpty(vertical) ? horizontal : string.IsNullOrEmpty(horizontal) ? vertical : vertical + " + " + horizontal;
    }

    static void BuildToggle(Component owner, ExpressionMenuSource source, List<RuntimeAnimatorController> controllers,
        string parameter, (string type, float defaultValue) definition, List<MenuControl> options, HashSet<string> claimed,
        HashSet<string> unsupported, IConversionContext context, ConversionReporter report,
        Func<string[], string, string, GameObject> nextItem)
    {
        // Bool parameters are set to true by their toggles
        var first = options[0];
        var onValue = definition.type == "Bool" ? 1f : first.Value;

        if (definition.type == "Float" && options.Any(o => !Mathf.Approximately(o.Value, first.Value)))
            report.Info("float:" + parameter, $"Menu parameter {parameter} is set to different values by multiple toggles, " +
                $"only the first value is converted.", owner);

        var on = ReadState(controllers, source, parameter, onValue, unsupported);
        var off = ReadState(controllers, source, parameter, 0, unsupported);

        Claim(claimed, parameter, report, owner, on, off);

        if (on.IsEmpty && off.IsEmpty)
        {
            report.Info("empty:" + parameter, $"Menu toggle {first.Label} ({parameter}) doesn't animate anything " +
                $"that can be converted.", owner);
            return;
        }

        var defaultOn = definition.type == "Bool" ? definition.defaultValue != 0 : Mathf.Approximately(definition.defaultValue, onValue);

        var state = first.IsButton
            ? AvatarToggleBuilder.BuildButton(nextItem(first.Folders, first.Label, "Button"), defaultOn, on, off, context)
            : AvatarToggleBuilder.BuildToggle(nextItem(first.Folders, first.Label, "Toggle"), defaultOn, on, off, context);

        // The same parameter in other places sets the same state
        foreach (var option in options.Skip(1))
        {
            if (option.IsButton)
                AvatarToggleBuilder.BuildButtonLink(nextItem(option.Folders, option.Label, "Button Link"), state);
            else
                AvatarToggleBuilder.BuildToggleLink(nextItem(option.Folders, option.Label, "Toggle Link"), state);
        }
    }

    static void BuildSelector(Component owner, ExpressionMenuSource source, List<RuntimeAnimatorController> controllers,
        string parameter, float defaultValue, List<MenuControl> options, HashSet<string> claimed, HashSet<string> unsupported,
        IConversionContext context, ConversionReporter report, Func<string[], string, string, GameObject> nextItem,
        Func<string, GameObject> nextSelector)
    {
        // The selector's index is only an internal label for Resonite's own toggle wiring, not the original
        // VRChat parameter value, so distinct values get sequential indices directly rather than rounding them
        // to an int and using that as the index. Rounding broke Float parameters with fractional thresholds
        // (e.g. a blend tree using 0.5 as one option's value and also the parameter's default): two distinct
        // values closer together than 1 apart collided into the same rounded index, and worse, that index was
        // then used as the literal value to sample the animator with instead of the real value it stood for -
        // silently reading whichever unrelated state happens to sit at that integer position.
        // VRChat toggles reset their parameter to 0 when turned off, so 0 (e.g. no outfit piece) is always a state too
        var distinctValues = options.Select(o => o.Value).Append(defaultValue).Append(0f).Distinct().OrderBy(v => v).ToList();

        if (distinctValues.Count > MAX_SELECTOR_INDEX + 1)
        {
            report.Warning("range:" + parameter, $"Menu parameter {parameter} has more than {MAX_SELECTOR_INDEX + 1} distinct " +
                $"values, which isn't supported.", owner);
            return;
        }

        int IndexOf(float value) => distinctValues.FindIndex(v => Mathf.Approximately(v, value));

        var defaultIndex = IndexOf(defaultValue);
        var offIndex = IndexOf(0);

        // State for each distinct value, read from the value itself (not its index).
        var states = distinctValues.Select(value => ReadState(controllers, source, parameter, value, unsupported)).ToList();

        Claim(claimed, parameter, report, owner, states.ToArray());

        if (states.All(s => s.IsEmpty))
        {
            report.Info("empty:" + parameter, $"Menu parameter {parameter} doesn't animate anything that can be converted.", owner);
            return;
        }

        var selector = nextSelector($"[Resonite] Selector {parameter}");
        var indexField = AvatarToggleBuilder.BuildSelector(selector, defaultIndex, states, context);

        // Options appearing in multiple places get an item in each of them. Like in VRChat, each option works as a toggle:
        // pressing it while it's active turns it off, resetting the parameter to 0.
        // Buttons select their value only while held, then reset the parameter to 0
        foreach (var option in options)
        {
            if (option.IsButton)
                AvatarToggleBuilder.BuildSelectorButton(nextItem(option.Folders, option.Label, "Button"), indexField,
                    IndexOf(option.Value), offIndex);
            else
                AvatarToggleBuilder.BuildSelectorOption(nextItem(option.Folders, option.Label, "Option"), indexField,
                    IndexOf(option.Value), offIndex);
        }

        // The default value can't be reached through the options otherwise (VRChat only restores it on reset)
        if (!Mathf.Approximately(defaultValue, 0) && !options.Any(o => Mathf.Approximately(o.Value, defaultValue)))
            AvatarToggleBuilder.BuildSelectorOption(nextItem(options[0].Folders, $"{parameter}: Default", "Option"), indexField, defaultIndex);
    }

    static void BuildPuppet(Component owner, ExpressionMenuSource source, List<RuntimeAnimatorController> controllers,
        string parameter, float defaultValue, List<MenuControl> puppets, HashSet<string> claimed, HashSet<string> unsupported,
        IConversionContext context, ConversionReporter report, Func<string[], string, string, GameObject> nextItem,
        Func<string, GameObject> nextSelector)
    {
        // Two axis puppets go from -1 to 1, so they get twice the samples
        var min = puppets.Min(p => p.Min);
        var count = Mathf.RoundToInt(PUPPET_SAMPLES * (1 - min));

        var samples = Enumerable.Range(0, count + 1)
            .Select(i => Mathf.Lerp(min, 1, i / (float)count))
            .Select(position => (position, state: AnimatorToggleAnalyzer.SampleFloat(controllers, parameter, position, source.ResolvePath, unsupported)))
            .ToList();

        Claim(claimed, parameter, report, owner, samples.Select(s => s.state).ToArray());

        if (samples.All(s => s.state.IsEmpty))
        {
            report.Info("empty:" + parameter, $"Puppet {puppets[0].Label} ({parameter}) doesn't animate anything " +
                $"that can be converted.", owner);
            return;
        }

        var value = AvatarToggleBuilder.BuildPuppet(nextSelector($"[Resonite] Puppet {parameter}"), Mathf.Clamp(defaultValue, min, 1),
            samples, context);

        // Each place the puppet appears in gets a submenu with the steps
        foreach (var puppet in puppets)
        {
            var folders = puppet.Folders.Append(puppet.Label).ToArray();

            foreach (var step in PUPPET_STEPS.Select(s => Mathf.Lerp(puppet.Min, 1, s)))
                AvatarToggleBuilder.BuildPuppetStep(nextItem(folders, $"{Mathf.RoundToInt(step * 100)}%", "Puppet Step"), value, step);
        }
    }

    // SampleFloat also covers everything the simpler FindClips/ReadClips pair does (transitions gated on the
    // parameter's value), plus states whose motion depends on the parameter directly - a 1D blend tree blended
    // by it, or motion time driven by it. Toggles/selectors built from a Button-type control sharing a Float
    // parameter with a blend tree (common for outfit-swap setups) only work through this path, not FindClips,
    // since such states are often unconditionally active rather than reached through a matching transition.
    static ToggleState ReadState(List<RuntimeAnimatorController> controllers, ExpressionMenuSource source, string parameter, float value,
        HashSet<string> unsupported) =>
        AnimatorToggleAnalyzer.SampleFloat(controllers, parameter, value, source.ResolvePath, unsupported);

    /// <summary>
    /// Removes properties already driven by previous toggles from the states and claims the rest
    /// </summary>
    static void Claim(HashSet<string> claimed, string parameter, ConversionReporter report, Component owner, params ToggleState[] states)
    {
        var conflicts = states.SelectMany(s => s.Keys).Where(claimed.Contains).Distinct().ToList();

        foreach (var key in conflicts)
            foreach (var state in states)
                state.Remove(key);

        if (conflicts.Count > 0)
            report.Warning("conflict:" + parameter, $"Menu parameter {parameter} animates {conflicts.Count} properties that are already " +
                $"controlled by another toggle. Resonite can only drive each property from one toggle, so they're skipped.", owner);

        foreach (var key in states.SelectMany(s => s.Keys))
            claimed.Add(key);
    }

    /// <param name="ancestors">Menus on the current path, to detect menus linking back to their parents</param>
    static void CollectControls(object menu, string[] folders, HashSet<object> ancestors, List<MenuControl> controls,
        Component owner, ConversionReporter report)
    {
        if (menu == null)
            return;

        if (ancestors.Contains(menu))
        {
            report.Info("recursion", $"Expression menu on {owner.name} links back to one of its parent menus " +
                $"({string.Join("/", folders)}). The loop isn't followed.", owner);
            return;
        }

        if (folders.Length > MAX_MENU_DEPTH)
        {
            report.Warning("depth", $"Expression menu on {owner.name} is nested more than {MAX_MENU_DEPTH} levels deep " +
                $"({string.Join("/", folders)}). Deeper menus aren't converted.", owner);
            return;
        }

        ancestors.Add(menu);

        foreach (var control in ReflectionAccessor.GetList(menu, "controls"))
        {
            if (control == null)
                continue;

            if (controls.Count >= MAX_CONTROLS)
            {
                report.Warning("controls", $"Expression menu on {owner.name} has over {MAX_CONTROLS} controls, the rest aren't converted.", owner);
                break;
            }

            var name = ReflectionAccessor.Get(control, "name", "");
            var type = ReflectionAccessor.GetEnumName(control, "type", "");

            switch (type)
            {
                case "SubMenu":
                    var subMenu = ReflectionAccessor.GetRaw(control, "subMenu");

                    if (subMenu is UnityEngine.Object subMenuObject && subMenuObject != null)
                        CollectControls(subMenu, folders.Append(name).ToArray(), ancestors, controls, owner, report);
                    break;

                case "RadialPuppet":
                    // The puppet sets its first sub parameter (0...1)
                    var puppetParameter = ReflectionAccessor.Get(ReflectionAccessor.GetList(control, "subParameters").FirstOrDefault(), "name", "");

                    if (!string.IsNullOrEmpty(puppetParameter))
                        controls.Add(new MenuControl()
                        {
                            Folders = folders,
                            Label = name,
                            Parameter = puppetParameter,
                            Kind = ControlKind.Puppet,
                        });
                    break;

                // Sub parameter order is the SDK editor's (ExpressionsControlOptions): horizontal, vertical / up, right, down, left
                case "TwoAxisPuppet":
                    CollectAxes(control, folders, name, TWO_AXIS_LABELS, controls);
                    break;

                case "FourAxisPuppet":
                    CollectAxes(control, folders, name, FOUR_AXIS_LABELS, controls);
                    break;

                case "Toggle":
                case "Button":
                    var parameter = ReflectionAccessor.Get(ReflectionAccessor.GetRaw(control, "parameter"), "name", "");

                    if (!string.IsNullOrEmpty(parameter))
                        controls.Add(new MenuControl()
                        {
                            Folders = folders,
                            Label = name,
                            Parameter = parameter,
                            Value = ReflectionAccessor.Get(control, "value", 1f),
                            Kind = type == "Button" ? ControlKind.Button : ControlKind.Toggle,
                        });
                    break;

                default:
                    report.Info("control:" + type, $"Expression menu {type} controls aren't converted (e.g. {name}).", owner);
                    break;
            }
        }

        // Only the current path matters, the same menu can appear again in another place
        ancestors.Remove(menu);
    }

    // The SDK stores horizontal/vertical or up/right/down/left in one control; keep them together so
    // all animated properties share one selector instead of letting the first axis claim every field.
    static void CollectAxes(object control, string[] folders, string name, string[] axisLabels, List<MenuControl> controls)
    {
        var names = ReflectionAccessor.GetList(control, "subParameters")
            .Select(p => ReflectionAccessor.Get(p, "name", "")).Take(axisLabels.Length).ToArray();
        if (names.Length != axisLabels.Length || names.Any(string.IsNullOrEmpty) || names.Distinct().Count() != names.Length)
            return;

        var labels = ReflectionAccessor.GetList(control, "labels")
            .Select(label => ReflectionAccessor.Get(label, "name", "")).ToArray();
        var directions = FOUR_AXIS_LABELS.Select((fallback, index) =>
            index < labels.Length && !string.IsNullOrEmpty(labels[index]) ? labels[index] : fallback).ToArray();

        controls.Add(new MenuControl {
            Folders = folders,
            Label = name,
            Kind = ControlKind.AxisPuppet,
            AxisParameters = names,
            AxisLabels = directions,
        });
    }

    static Dictionary<string, (string type, float defaultValue)> ReadParameters(IEnumerable<object> parameterAssets)
    {
        var parameters = new Dictionary<string, (string, float)>();

        foreach (var asset in parameterAssets)
            foreach (var parameter in ReflectionAccessor.GetList(asset, "parameters"))
            {
                var name = ReflectionAccessor.Get(parameter, "name", "");

                if (!string.IsNullOrEmpty(name) && !parameters.ContainsKey(name))
                    parameters.Add(name, (ReflectionAccessor.GetEnumName(parameter, "valueType", "Bool"),
                        ReflectionAccessor.Get(parameter, "defaultValue", 0f)));
            }

        return parameters;
    }
}
#endif
