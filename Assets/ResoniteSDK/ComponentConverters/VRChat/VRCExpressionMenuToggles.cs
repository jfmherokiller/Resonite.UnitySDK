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
/// - Radial puppets become a submenu of steps (0%, 25%...) setting the puppet's value, which drives the properties
///   through gradients sampled from the animator (including 1D blend trees and motion time). Resonite's context menu
///   has no sliders, so the value can only be set in steps.
///
/// Each property can only be driven by one toggle in Resonite, so if multiple parameters control the same property,
/// only the first one gets it. Buttons and two/four axis puppets aren't converted.
/// </summary>
public static class VRCExpressionMenuToggles
{
    const int MAX_SELECTOR_INDEX = 255;
    const int MAX_MENU_DEPTH = 16;
    const int MAX_CONTROLS = 2000;

    // Values the puppet is sampled at, and the steps offered in its menu
    const int PUPPET_SAMPLES = 10;
    static readonly float[] PUPPET_STEPS = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    class MenuControl
    {
        public string[] Folders;
        public string Label;
        public string Parameter;
        public float Value;
        public bool Radial;
    }

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

        foreach (var group in controls.GroupBy(c => c.Parameter))
        {
            var parameter = group.Key;

            if (!parameters.TryGetValue(parameter, out var definition))
            {
                report.Warning("param:" + parameter, $"Menu parameter {parameter} isn't in the expression parameters.", owner);
                continue;
            }

            var unsupported = new HashSet<string>();
            var options = group.ToList();

            if (options.Any(o => o.Radial))
            {
                BuildPuppet(owner, source, controllers, parameter, definition.defaultValue, options.Where(o => o.Radial).ToList(),
                    claimed, unsupported, context, report, nextItem, nextSelector);

                if (options.Any(o => !o.Radial))
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

        var state = AvatarToggleBuilder.BuildToggle(nextItem(first.Folders, first.Label, "Toggle"), defaultOn, on, off, context);

        // The same toggle in other places flips the same state
        foreach (var option in options.Skip(1))
            AvatarToggleBuilder.BuildToggleLink(nextItem(option.Folders, option.Label, "Toggle Link"), state);
    }

    static void BuildSelector(Component owner, ExpressionMenuSource source, List<RuntimeAnimatorController> controllers,
        string parameter, float defaultValue, List<MenuControl> options, HashSet<string> claimed, HashSet<string> unsupported,
        IConversionContext context, ConversionReporter report, Func<string[], string, string, GameObject> nextItem,
        Func<string, GameObject> nextSelector)
    {
        var defaultIndex = Mathf.RoundToInt(defaultValue);
        var indices = options.Select(o => Mathf.RoundToInt(o.Value)).Append(defaultIndex).ToList();

        if (indices.Min() < 0 || indices.Max() > MAX_SELECTOR_INDEX)
        {
            report.Warning("range:" + parameter, $"Menu parameter {parameter} uses values outside of 0...{MAX_SELECTOR_INDEX}, " +
                $"which isn't supported.", owner);
            return;
        }

        // State for each index. Only the used ones are read, the rest stay at rest values.
        var states = Enumerable.Range(0, indices.Max() + 1).Select(_ => new ToggleState()).ToList();

        foreach (var index in indices.Distinct())
            states[index] = ReadState(controllers, source, parameter, index, unsupported);

        Claim(claimed, parameter, report, owner, states.ToArray());

        if (states.All(s => s.IsEmpty))
        {
            report.Info("empty:" + parameter, $"Menu parameter {parameter} doesn't animate anything that can be converted.", owner);
            return;
        }

        var selector = nextSelector($"[Resonite] Selector {parameter}");
        var indexField = AvatarToggleBuilder.BuildSelector(selector, defaultIndex, states, context);

        // Options appearing in multiple places get an item in each of them
        foreach (var option in options)
            AvatarToggleBuilder.BuildSelectorOption(nextItem(option.Folders, option.Label, "Option"), indexField, Mathf.RoundToInt(option.Value));

        // In VRChat, pressing the active option again resets the parameter. Add an item for that instead.
        if (!options.Any(o => Mathf.RoundToInt(o.Value) == defaultIndex))
            AvatarToggleBuilder.BuildSelectorOption(nextItem(options[0].Folders, $"{parameter}: Default", "Option"), indexField, defaultIndex);
    }

    static void BuildPuppet(Component owner, ExpressionMenuSource source, List<RuntimeAnimatorController> controllers,
        string parameter, float defaultValue, List<MenuControl> puppets, HashSet<string> claimed, HashSet<string> unsupported,
        IConversionContext context, ConversionReporter report, Func<string[], string, string, GameObject> nextItem,
        Func<string, GameObject> nextSelector)
    {
        var samples = Enumerable.Range(0, PUPPET_SAMPLES + 1)
            .Select(i => i / (float)PUPPET_SAMPLES)
            .Select(position => (position, state: AnimatorToggleAnalyzer.SampleFloat(controllers, parameter, position, source.ResolvePath, unsupported)))
            .ToList();

        Claim(claimed, parameter, report, owner, samples.Select(s => s.state).ToArray());

        if (samples.All(s => s.state.IsEmpty))
        {
            report.Info("empty:" + parameter, $"Radial puppet {puppets[0].Label} ({parameter}) doesn't animate anything " +
                $"that can be converted.", owner);
            return;
        }

        var value = AvatarToggleBuilder.BuildPuppet(nextSelector($"[Resonite] Puppet {parameter}"), Mathf.Clamp01(defaultValue),
            samples, context);

        // Each place the puppet appears in gets a submenu with the steps
        foreach (var puppet in puppets)
        {
            var folders = puppet.Folders.Append(puppet.Label).ToArray();

            foreach (var step in PUPPET_STEPS)
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
                            Radial = true,
                        });
                    break;

                case "Toggle":
                    var parameter = ReflectionAccessor.Get(ReflectionAccessor.GetRaw(control, "parameter"), "name", "");

                    if (!string.IsNullOrEmpty(parameter))
                        controls.Add(new MenuControl()
                        {
                            Folders = folders,
                            Label = name,
                            Parameter = parameter,
                            Value = ReflectionAccessor.Get(control, "value", 1f),
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
