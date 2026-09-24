#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts the toggles in a VRChat avatar's expression menu into Resonite context menu toggles
/// (see <see cref="AvatarToggleBuilder"/>), following the menu's submenus.
///
/// What each toggle does is determined from the FX animator controller (see <see cref="AnimatorToggleAnalyzer"/>):
/// - Bool (and float) parameters become on/off toggles
/// - Int parameters with multiple options become selectors, where each option's item selects its value.
///   An int parameter with a single option becomes an on/off toggle.
///
/// Each property can only be driven by one toggle in Resonite, so if multiple toggles control the same property,
/// only the first one gets it. Buttons and puppets aren't converted.
/// </summary>
public static class VRCExpressionMenuToggles
{
    const int MAX_SELECTOR_INDEX = 255;

    class MenuControl
    {
        public string[] Folders;
        public string Label;
        public string Parameter;
        public float Value;
    }

    /// <param name="items">Generated menu items, reused between updates in the same order</param>
    /// <param name="selectors">Generated selector states, reused between updates in the same order</param>
    public static void Apply(Component descriptor, List<GameObject> items, List<GameObject> selectors, IConversionContext context,
        ConversionReporter report)
    {
        var usedItems = 0;
        var usedSelectors = 0;

        GameObject NextItem(GameObject menu, string label)
        {
            if (usedItems == items.Count)
                items.Add(null);

            var item = items[usedItems];
            AvatarToggleBuilder.EnsureItem(ref item, menu, label);
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
                selector = GeneratedObjectHelper.Create(descriptor.transform, name);
            }

            selectors[usedSelectors++] = selector;

            return selector;
        }

        Build(descriptor, context, report, NextItem, NextSelector);

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

    static void Build(Component descriptor, IConversionContext context, ConversionReporter report,
        Func<GameObject, string, GameObject> nextItem, Func<string, GameObject> nextSelector)
    {
        if (!ReflectionAccessor.Get(descriptor, "customExpressions", false))
            return;

        var menu = ReflectionAccessor.GetRaw(descriptor, "expressionsMenu");
        var controller = FindFXController(descriptor);

        if (menu == null || (menu is UnityEngine.Object menuObject && menuObject == null))
            return;

        if (controller == null)
        {
            report.Warning("nofx", $"VRChat avatar {descriptor.name} has an expression menu, but no FX controller. " +
                $"Menu toggles can't be converted.", descriptor);
            return;
        }

        var parameters = ReadParameters(ReflectionAccessor.GetRaw(descriptor, "expressionParameters"));
        var root = descriptor.transform;

        var controls = new List<MenuControl>();
        CollectControls(menu, new string[0], controls, new HashSet<object>(), descriptor, report);

        // Properties already driven by a previous toggle
        var claimed = new HashSet<string>();

        foreach (var group in controls.GroupBy(c => c.Parameter))
        {
            var parameter = group.Key;

            if (!parameters.TryGetValue(parameter, out var definition))
            {
                report.Warning("param:" + parameter, $"Menu parameter {parameter} isn't in the expression parameters.", descriptor);
                continue;
            }

            var unsupported = new HashSet<string>();
            var options = group.ToList();

            if (definition.type == "Int" && options.Count > 1)
                BuildSelector(root, parameter, definition.defaultValue, options, controller, claimed, unsupported, context, nextItem, nextSelector, report, descriptor);
            else
            {
                // Bool parameters are set to true by their toggles
                var control = options[0];
                var onValue = definition.type == "Bool" ? 1f : control.Value;

                var on = ReadState(controller, parameter, onValue, root, unsupported);
                var off = ReadState(controller, parameter, 0, root, unsupported);

                Claim(claimed, parameter, report, descriptor, on, off);

                if (on.IsEmpty && off.IsEmpty)
                    report.Info("empty:" + parameter, $"Menu toggle {control.Label} ({parameter}) doesn't animate anything " +
                        $"that can be converted.", descriptor);
                else
                {
                    var defaultOn = definition.type == "Bool" ? definition.defaultValue != 0 : Mathf.Approximately(definition.defaultValue, onValue);
                    var item = nextItem(AvatarToggleBuilder.EnsureMenu(root, control.Folders), control.Label);

                    AvatarToggleBuilder.BuildToggle(item, defaultOn, on, off, context);
                }

                if (options.Count > 1)
                    report.Info("dupe:" + parameter, $"Parameter {parameter} is used by multiple menu toggles, only the first is converted.", descriptor);
            }

            if (unsupported.Count > 0)
                report.Info("unsupported:" + parameter, $"Menu toggle for {parameter} also animates {string.Join(", ", unsupported)}, " +
                    $"which isn't converted.", descriptor);
        }
    }

    static void BuildSelector(Transform root, string parameter, float defaultValue, List<MenuControl> options,
        RuntimeAnimatorController controller, HashSet<string> claimed, HashSet<string> unsupported, IConversionContext context,
        Func<GameObject, string, GameObject> nextItem, Func<string, GameObject> nextSelector, ConversionReporter report, Component descriptor)
    {
        var defaultIndex = Mathf.RoundToInt(defaultValue);
        var indices = options.Select(o => Mathf.RoundToInt(o.Value)).Append(defaultIndex).ToList();

        if (indices.Min() < 0 || indices.Max() > MAX_SELECTOR_INDEX)
        {
            report.Warning("range:" + parameter, $"Menu parameter {parameter} uses values outside of 0...{MAX_SELECTOR_INDEX}, " +
                $"which isn't supported.", descriptor);
            return;
        }

        // State for each index. Only the used ones are read, the rest stay at rest values.
        var states = Enumerable.Range(0, indices.Max() + 1).Select(_ => new ToggleState()).ToList();

        foreach (var index in indices.Distinct())
            states[index] = ReadState(controller, parameter, index, root, unsupported);

        Claim(claimed, parameter, report, descriptor, states.ToArray());

        if (states.All(s => s.IsEmpty))
        {
            report.Info("empty:" + parameter, $"Menu parameter {parameter} doesn't animate anything that can be converted.", descriptor);
            return;
        }

        var selector = nextSelector($"[Resonite] Selector {parameter}");
        var indexField = AvatarToggleBuilder.BuildSelector(selector, defaultIndex, states, context);

        foreach (var option in options)
            AvatarToggleBuilder.BuildSelectorOption(nextItem(AvatarToggleBuilder.EnsureMenu(root, option.Folders), option.Label),
                indexField, Mathf.RoundToInt(option.Value));

        // In VRChat, pressing the active option again resets the parameter. Add an item for that instead.
        if (!options.Any(o => Mathf.RoundToInt(o.Value) == defaultIndex))
            AvatarToggleBuilder.BuildSelectorOption(nextItem(AvatarToggleBuilder.EnsureMenu(root, options[0].Folders),
                $"{parameter}: Default"), indexField, defaultIndex);
    }

    static ToggleState ReadState(RuntimeAnimatorController controller, string parameter, float value, Transform root, HashSet<string> unsupported)
    {
        var state = new ToggleState();
        var clips = AnimatorToggleAnalyzer.FindClips(controller, parameter, value, unsupported);

        AnimatorToggleAnalyzer.ReadClips(clips, root, state, unsupported);

        return state;
    }

    /// <summary>
    /// Removes properties already driven by previous toggles from the states and claims the rest
    /// </summary>
    static void Claim(HashSet<string> claimed, string parameter, ConversionReporter report, Component descriptor, params ToggleState[] states)
    {
        var conflicts = states.SelectMany(s => s.Keys).Where(claimed.Contains).Distinct().ToList();

        foreach (var key in conflicts)
            foreach (var state in states)
                state.Remove(key);

        if (conflicts.Count > 0)
            report.Warning("conflict:" + parameter, $"Menu parameter {parameter} animates {conflicts.Count} properties that are already " +
                $"controlled by another toggle. Resonite can only drive each property from one toggle, so they're skipped.", descriptor);

        foreach (var key in states.SelectMany(s => s.Keys))
            claimed.Add(key);
    }

    static void CollectControls(object menu, string[] folders, List<MenuControl> controls, HashSet<object> visited,
        Component descriptor, ConversionReporter report)
    {
        if (menu == null || !visited.Add(menu))
            return;

        foreach (var control in ReflectionAccessor.GetList(menu, "controls"))
        {
            if (control == null)
                continue;

            var name = ReflectionAccessor.Get(control, "name", "");
            var type = ReflectionAccessor.GetEnumName(control, "type", "");

            switch (type)
            {
                case "SubMenu":
                    var subMenu = ReflectionAccessor.GetRaw(control, "subMenu");

                    if (subMenu is UnityEngine.Object subMenuObject && subMenuObject != null)
                        CollectControls(subMenu, folders.Append(name).ToArray(), controls, visited, descriptor, report);
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
                    report.Info("control:" + type, $"Expression menu {type} controls aren't converted (e.g. {name}).", descriptor);
                    break;
            }
        }
    }

    static Dictionary<string, (string type, float defaultValue)> ReadParameters(object parametersAsset)
    {
        var parameters = new Dictionary<string, (string, float)>();

        foreach (var parameter in ReflectionAccessor.GetList(parametersAsset, "parameters"))
        {
            var name = ReflectionAccessor.Get(parameter, "name", "");

            if (!string.IsNullOrEmpty(name) && !parameters.ContainsKey(name))
                parameters.Add(name, (ReflectionAccessor.GetEnumName(parameter, "valueType", "Bool"),
                    ReflectionAccessor.Get(parameter, "defaultValue", 0f)));
        }

        return parameters;
    }

    static RuntimeAnimatorController FindFXController(Component descriptor)
    {
        foreach (var layer in ReflectionAccessor.GetList(descriptor, "baseAnimationLayers"))
            if (ReflectionAccessor.GetEnumName(layer, "type") == "FX" && !ReflectionAccessor.Get(layer, "isDefault", true))
                return ReflectionAccessor.GetObject<RuntimeAnimatorController>(layer, "animatorController");

        return null;
    }
}
#endif
