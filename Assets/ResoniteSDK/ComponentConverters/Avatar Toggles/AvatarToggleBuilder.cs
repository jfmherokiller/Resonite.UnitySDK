using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Builds avatar toggles in Resonite's context menu. All toggles are placed under an "Avatar Toggles" item in the root
/// context menu of the user wearing the avatar, with submenus following the menu path.
///
/// - Toggle: pressing the item flips a bool state, which drives all the controlled properties between their on and
///   off values (<see cref="BuildToggle"/>)
/// - Selector: multiple items set an index (e.g. VRChat int parameter), which selects the value of each controlled
///   property (<see cref="BuildSelector"/>)
///
/// Everything is generated as helper objects, which are not saved with the scene.
/// </summary>
public static class AvatarToggleBuilder
{
    public const string MENU_ROOT_NAME = "[Resonite] Avatar Toggles";

    /// <summary>
    /// Returns the menu for the given folder path (e.g. "Clothing/Tops"), creating it if needed
    /// </summary>
    public static GameObject EnsureMenu(Transform avatarRoot, IEnumerable<string> folders)
    {
        var menu = EnsureMenu(avatarRoot, MENU_ROOT_NAME, "Avatar Toggles", isRoot: true);

        foreach (var folder in folders)
            menu = EnsureMenu(menu.transform, "[Resonite] " + folder, folder, isRoot: false);

        return menu;
    }

    /// <summary>
    /// Makes sure the item object exists under the menu. It's recreated if the menu or the kind of the item has changed,
    /// so no components from a different kind of item remain.
    /// </summary>
    /// <param name="kind">Kind of the item, e.g. "Toggle" or "Option"</param>
    public static GameObject EnsureItem(ref GameObject item, GameObject menu, string label, string kind = "Toggle")
    {
        var name = $"[Resonite] {kind}: {label}";

        if (item == null || item.transform.parent != menu.transform || item.name != name)
        {
            GeneratedObjectHelper.DestroyWithEmptyParents(ref item);
            item = GeneratedObjectHelper.Create(menu.transform, name);
        }

        SetupItem(item, label);

        return item;
    }

    /// <summary>
    /// Builds an on/off toggle, which sets the properties to the values of the on or off state
    /// </summary>
    /// <returns>The toggle's state, which other items can toggle too (see <see cref="BuildToggleLink"/>)</returns>
    public static FrooxEngine.IField<bool> BuildToggle(GameObject item, bool defaultOn, ToggleState on, ToggleState off, IConversionContext context)
    {
        // The toggle state, which the menu button flips. It drives the state of all the individual properties.
        var stateDriver = ConverterComponentHelper.GetOrAdd<ValueMultiDriverBoolWrapper>(item).Data;
        stateDriver.Value = defaultOn;
        stateDriver.Drives.Clear();

        var button = ConverterComponentHelper.GetOrAdd<FrooxEngine.ButtonToggleWrapper>(item).Data;
        button.TargetValue = stateDriver.Value_Element.Member;

        // Properties set by either state. Properties missing from a state use their rest value.
        var bools = on.Bools.Values.Select(v => v.target)
            .Concat(off.Bools.Values.Select(v => v.target)).GroupBy(t => t.Key).Select(g => g.First()).ToList();

        var boolDrivers = EnsureCount<BooleanValueDriverBoolWrapper>(item, bools.Count);

        for (int i = 0; i < bools.Count; i++)
        {
            var driver = boolDrivers[i].Data;
            var target = bools[i];

            driver.TrueValue = on.Bools.TryGetValue(target.Key, out var onValue) ? onValue.value : target.RestValue;
            driver.FalseValue = off.Bools.TryGetValue(target.Key, out var offValue) ? offValue.value : target.RestValue;

            target.ResolveField(context, field => driver.TargetField = field);
            stateDriver.Drives.Add(driver.State_Element.Member);
        }

        var floats = on.Floats.Values.Select(v => v.target)
            .Concat(off.Floats.Values.Select(v => v.target)).GroupBy(t => t.Key).Select(g => g.First()).ToList();

        var floatDrivers = EnsureCount<BooleanValueDriverFloatWrapper>(item, floats.Count);

        for (int i = 0; i < floats.Count; i++)
        {
            var driver = floatDrivers[i].Data;
            var target = floats[i];

            driver.TrueValue = on.Floats.TryGetValue(target.Key, out var onValue) ? onValue.value : target.RestValue;
            driver.FalseValue = off.Floats.TryGetValue(target.Key, out var offValue) ? offValue.value : target.RestValue;

            target.ResolveField(context, field => driver.TargetField = field);
            stateDriver.Drives.Add(driver.State_Element.Member);
        }

        return stateDriver.Value_Element.Member;
    }

    /// <summary>
    /// Builds an item that flips the state of an existing toggle, e.g. when the same toggle is in multiple menus
    /// </summary>
    public static void BuildToggleLink(GameObject item, FrooxEngine.IField<bool> toggleState)
    {
        var button = ConverterComponentHelper.GetOrAdd<FrooxEngine.ButtonToggleWrapper>(item).Data;
        button.TargetValue = toggleState;
    }

    /// <summary>
    /// Builds a selector. The state object holds the selected index and a multiplexer for each controlled property,
    /// with the value for each index. Option items (see <see cref="BuildSelectorOption"/>) set the index.
    /// </summary>
    /// <param name="states">State for each index. Properties missing from a state use their rest value.</param>
    public static FrooxEngine.IField<int> BuildSelector(GameObject stateObject, int defaultIndex, IReadOnlyList<ToggleState> states,
        IConversionContext context)
    {
        var index = ConverterComponentHelper.GetOrAdd<ValueMultiDriverIntWrapper>(stateObject).Data;
        index.Value = defaultIndex;
        index.Drives.Clear();

        var bools = states.SelectMany(s => s.Bools.Values).GroupBy(v => v.target.Key).Select(g => g.First().target).ToList();
        var floats = states.SelectMany(s => s.Floats.Values).GroupBy(v => v.target.Key).Select(g => g.First().target).ToList();

        var boolMultiplexers = EnsureCount<ValueMultiplexerBoolWrapper>(stateObject, bools.Count);

        for (int i = 0; i < bools.Count; i++)
        {
            var multiplexer = boolMultiplexers[i].Data;
            var target = bools[i];

            multiplexer.Values.Clear();

            foreach (var state in states)
                multiplexer.Values.Add(state.Bools.TryGetValue(target.Key, out var value) ? value.value : target.RestValue);

            target.ResolveField(context, field => multiplexer.Target = field);
            index.Drives.Add(multiplexer.Index_Element.Member);
        }

        var floatMultiplexers = EnsureCount<ValueMultiplexerFloatWrapper>(stateObject, floats.Count);

        for (int i = 0; i < floats.Count; i++)
        {
            var multiplexer = floatMultiplexers[i].Data;
            var target = floats[i];

            multiplexer.Values.Clear();

            foreach (var state in states)
                multiplexer.Values.Add(state.Floats.TryGetValue(target.Key, out var value) ? value.value : target.RestValue);

            target.ResolveField(context, field => multiplexer.Target = field);
            index.Drives.Add(multiplexer.Index_Element.Member);
        }

        return index.Value_Element.Member;
    }

    /// <summary>
    /// Builds a menu item that selects the given index of a selector
    /// </summary>
    public static void BuildSelectorOption(GameObject item, FrooxEngine.IField<int> selectorIndex, int index)
    {
        var button = ConverterComponentHelper.GetOrAdd<ButtonValueSetIntWrapper>(item).Data;
        button.TargetValue = selectorIndex;
        button.SetValue = index;
    }

    static GameObject EnsureMenu(Transform parent, string objectName, string label, bool isRoot)
    {
        var menu = GeneratedObjectHelper.EnsureChild(parent, objectName);

        SetupItem(menu, label);

        var submenu = ConverterComponentHelper.GetOrAdd<FrooxEngine.ContextMenuSubmenuWrapper>(menu).Data;
        submenu.ItemsRoot = menu.GetSlot();

        if (isRoot)
        {
            // Registers the item in the root context menu of the user wearing the avatar
            var root = ConverterComponentHelper.GetOrAdd<FrooxEngine.RootContextMenuItemWrapper>(menu).Data;
            root.Item = menu.GetComponent<FrooxEngine.ContextMenuItemSourceWrapper>().Data;
        }

        return menu;
    }

    static void SetupItem(GameObject obj, string label)
    {
        var item = ConverterComponentHelper.GetOrAdd<FrooxEngine.ContextMenuItemSourceWrapper>(obj);
        ResoniteMemberFilter.Set(item, "Label");

        item.Data.Label = label;
    }

    static List<T> EnsureCount<T>(GameObject obj, int count) where T : Component
    {
        var list = obj.GetComponents<T>().ToList();

        while (list.Count < count)
            list.Add(obj.AddComponent<T>());

        while (list.Count > count)
        {
            UnityEngine.Object.DestroyImmediate(list[list.Count - 1]);
            list.RemoveAt(list.Count - 1);
        }

        return list;
    }
}
