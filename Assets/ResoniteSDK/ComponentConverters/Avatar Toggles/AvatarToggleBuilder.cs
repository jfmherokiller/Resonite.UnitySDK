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
/// - Puppet: a float state interpolates controlled properties across sampled values; a UIX slider adjusts it
///   continuously, and menu presets can set specific values (<see cref="BuildPuppet"/>).
///
/// Material slots (swaps and animated material properties) are driven with asset drivers / multiplexers, switching
/// between the converted materials of each state.
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
        var state = BuildToggleState(item, defaultOn, on, off, context);
        BuildToggleLink(item, state);
        return state;
    }

    /// <summary>
    /// Builds a momentary button: the properties use the on state only while the item is held (like a VRChat button)
    /// </summary>
    public static FrooxEngine.IField<bool> BuildButton(GameObject item, bool defaultOn, ToggleState on, ToggleState off, IConversionContext context)
    {
        var state = BuildToggleState(item, defaultOn, on, off, context);
        BuildButtonLink(item, state);
        return state;
    }

    /// <summary>
    /// Builds an item that sets an existing toggle's state while held
    /// </summary>
    public static void BuildButtonLink(GameObject item, FrooxEngine.IField<bool> toggleState)
    {
        var graph = ButtonGraph(item, out var button);
        graph.ButtonEvents(button, graph.Write(toggleState, true), graph.Write(toggleState, false));
        graph.End();
    }

    /// <summary>
    /// Builds an item that selects the index of a selector while held, then selects the rest index again
    /// </summary>
    public static void BuildSelectorButton(GameObject item, FrooxEngine.IField<int> selectorIndex, int index, int restIndex)
    {
        var graph = ButtonGraph(item, out var button);
        graph.ButtonEvents(button, graph.Write(selectorIndex, index), graph.Write(selectorIndex, restIndex));
        graph.End();
    }

    // Press/release need ProtoFlux (button components only react to presses). Nodes live on a child, since
    // ProtoFluxGraph.End removes every other Resonite component on its host.
    static ProtoFluxGraph ButtonGraph(GameObject item, out FrooxEngine.IButton button)
    {
        button = item.GetComponent<FrooxEngine.ContextMenuItemSourceWrapper>().Data;

        var graph = new ProtoFluxGraph(GeneratedObjectHelper.EnsureChild(item.transform, "[Resonite] Button Flux"));
        graph.Begin();
        return graph;
    }

    /// <summary>
    /// Builds the bool state of a toggle, which drives all the controlled properties between their on and off values
    /// </summary>
    public static FrooxEngine.IField<bool> BuildToggleState(GameObject item, bool defaultOn, ToggleState on, ToggleState off, IConversionContext context)
    {
        var stateDriver = ConverterComponentHelper.GetOrAdd<ValueMultiDriverBoolWrapper>(item).Data;
        stateDriver.Value = defaultOn;
        stateDriver.Drives.Clear();

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

        var materials = MaterialSlots(new[] { on, off });
        var materialDrivers = EnsureCount<BooleanAssetDriverMaterialWrapper>(item, materials.Count);

        for (int i = 0; i < materials.Count; i++)
        {
            var driver = materialDrivers[i].Data;
            var target = materials[i];

            driver.TrueTarget = GetMaterial(context, on.ResolveMaterial(target));
            driver.FalseTarget = GetMaterial(context, off.ResolveMaterial(target));

            target.ResolveSlot(context, slot => driver.Target_Element.Data = slot);
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

        var materials = MaterialSlots(states);
        var materialMultiplexers = EnsureCount<AssetMultiplexerMaterialWrapper>(stateObject, materials.Count);

        for (int i = 0; i < materials.Count; i++)
        {
            var multiplexer = materialMultiplexers[i].Data;
            var target = materials[i];

            multiplexer.Assets.Clear();

            foreach (var state in states)
                multiplexer.Assets.Add(GetMaterial(context, state.ResolveMaterial(target)));

            target.ResolveSlot(context, slot => multiplexer.Target_Element.Data = slot);
            index.Drives.Add(multiplexer.Index_Element.Member);
        }

        return index.Value_Element.Member;
    }

    /// <summary>
    /// Builds a menu item that selects the given index of a selector
    /// </summary>
    /// <param name="offIndex">If set, the item works like a toggle: pressing it while its index is selected selects this
    /// index instead (like a VRChat toggle resetting its parameter when turned off)</param>
    public static void BuildSelectorOption(GameObject item, FrooxEngine.IField<int> selectorIndex, int index, int? offIndex = null)
    {
        // The item may have been built with the other kind of button before
        var set = item.GetComponent<ButtonValueSetIntWrapper>();
        var cycle = item.GetComponent<ButtonValueCycleIntWrapper>();

        if (offIndex.HasValue && offIndex.Value != index)
        {
            ConverterComponentHelper.Remove(ref set);

            // Cycles to the next value after the current one, or to the first if the current value isn't in the list
            var button = ConverterComponentHelper.GetOrAdd<ButtonValueCycleIntWrapper>(item).Data;
            button.TargetValue = selectorIndex;
            button.Values.Clear();
            button.Values.Add(index);
            button.Values.Add(offIndex.Value);
        }
        else
        {
            ConverterComponentHelper.Remove(ref cycle);

            var button = ConverterComponentHelper.GetOrAdd<ButtonValueSetIntWrapper>(item).Data;
            button.TargetValue = selectorIndex;
            button.SetValue = index;
        }
    }

    /// <summary>
    /// Builds a puppet - a float value (0...1) that sets the properties by interpolating between the sampled states.
    /// Blendshapes are interpolated, on/off properties switch at the sample points. Items made with
    /// <see cref="BuildPuppetStep"/> set the value.
    /// </summary>
    /// <param name="samples">State at each sampled value, in increasing order. Properties missing from a sample use their rest value.</param>
    /// <returns>The puppet's value</returns>
    public static FrooxEngine.IField<float> BuildPuppet(GameObject stateObject, float defaultValue,
        IReadOnlyList<(float position, ToggleState state)> samples, IConversionContext context)
    {
        var value = ConverterComponentHelper.GetOrAdd<ValueMultiDriverFloatWrapper>(stateObject).Data;
        value.Value = defaultValue;
        value.Drives.Clear();

        var floats = samples.SelectMany(s => s.state.Floats.Values).GroupBy(v => v.target.Key).Select(g => g.First().target).ToList();
        var bools = samples.SelectMany(s => s.state.Bools.Values).GroupBy(v => v.target.Key).Select(g => g.First().target).ToList();

        var floatDrivers = EnsureCount<ValueGradientDriverFloatWrapper>(stateObject, floats.Count);

        for (int i = 0; i < floats.Count; i++)
        {
            var driver = floatDrivers[i].Data;
            var target = floats[i];

            driver.Interpolate = true;
            driver.Points.Clear();

            foreach (var (position, state) in samples)
            {
                var point = driver.Points.Add();
                point.Position = position;
                point.Value = state.Floats.TryGetValue(target.Key, out var sampled) ? sampled.value : target.RestValue;
            }

            target.ResolveField(context, field => driver.Target = field);
            value.Drives.Add(driver.Progress_Element.Member);
        }

        var boolDrivers = EnsureCount<ValueGradientDriverBoolWrapper>(stateObject, bools.Count);

        for (int i = 0; i < bools.Count; i++)
        {
            var driver = boolDrivers[i].Data;
            var target = bools[i];

            driver.Interpolate = false;
            driver.Points.Clear();

            foreach (var (position, state) in samples)
            {
                var point = driver.Points.Add();
                point.Position = position;
                point.Value = state.Bools.TryGetValue(target.Key, out var sampled) ? sampled.value : target.RestValue;
            }

            target.ResolveField(context, field => driver.Target = field);
            value.Drives.Add(driver.Progress_Element.Member);
        }

        // Materials can't be interpolated: each sample's material is picked by index, stepping at the sample points
        var materials = MaterialSlots(samples.Select(s => s.state));
        var materialPickers = EnsureCount<ValueGradientDriverIntWrapper>(stateObject, materials.Count);
        var materialMultiplexers = EnsureCount<AssetMultiplexerMaterialWrapper>(stateObject, materials.Count);

        for (int i = 0; i < materials.Count; i++)
        {
            var picker = materialPickers[i].Data;
            var multiplexer = materialMultiplexers[i].Data;
            var target = materials[i];

            picker.Interpolate = false;
            picker.Points.Clear();
            multiplexer.Assets.Clear();

            for (int s = 0; s < samples.Count; s++)
            {
                var point = picker.Points.Add();
                point.Position = samples[s].position;
                point.Value = s;

                multiplexer.Assets.Add(GetMaterial(context, samples[s].state.ResolveMaterial(target)));
            }

            picker.Target = multiplexer.Index_Element.Member;
            target.ResolveSlot(context, slot => multiplexer.Target_Element.Data = slot);
            value.Drives.Add(picker.Progress_Element.Member);
        }

        return value.Value_Element.Member;
    }

    /// <summary>Builds a draggable UIX slider that writes into a continuous puppet state.</summary>
    public static void BuildContinuousSlider(GameObject panel, FrooxEngine.IField<float> puppetValue,
        float initial, float min, float max)
    {
        SetupSliderCanvas(panel, 0.12f);
        AddSlider(panel, "[Resonite] Track", puppetValue, initial, min, max, 0.3f, 0.7f);
    }

    static void SetupSliderCanvas(GameObject panel, float height)
    {
        var canvas = ConverterComponentHelper.GetOrAdd<FrooxEngine.UIX.CanvasWrapper>(panel).Data;
        canvas.Size = new Vector2(0.5f, height);
        canvas.AcceptRemoteTouch = true;
        canvas.AcceptPhysicalTouch = true;
        ConverterComponentHelper.GetOrAdd<FrooxEngine.UIX.RectTransformWrapper>(panel);
    }

    static void AddSlider(GameObject panel, string name, FrooxEngine.IField<float> value,
        float initial, float min, float max, float bottom, float top)
    {
        var track = GeneratedObjectHelper.EnsureChild(panel.transform, name);
        var rect = ConverterComponentHelper.GetOrAdd<FrooxEngine.UIX.RectTransformWrapper>(track).Data;
        rect.AnchorMin = new Vector2(0.08f, bottom);
        rect.AnchorMax = new Vector2(0.92f, top);
        rect.OffsetMin = Vector2.zero;
        rect.OffsetMax = Vector2.zero;
        ConverterComponentHelper.GetOrAdd<FrooxEngine.UIX.ImageWrapper>(track).Data.InteractionTarget = true;
        var caption = ConverterComponentHelper.GetOrAdd<FrooxEngine.UIX.TextWrapper>(track).Data;
        caption.Content = name == "[Resonite] Track" ? "Value" : name.Substring("[Resonite] ".Length);
        caption.Size = 0.022f;
        caption.InteractionTarget = false;

        var slider = ConverterComponentHelper.GetOrAdd<ContinuousPuppetSliderWrapper>(track).Data;
        slider.Min = min;
        slider.Max = max;
        slider.Value = initial;
        slider.Clamp = true;
        slider.Integers = false;
        slider.Power = 1f;
        slider.AnchorOffset = new Vector2(0, 0.5f);

        var handle = GeneratedObjectHelper.EnsureChild(track.transform, "[Resonite] Handle");
        var handleRect = ConverterComponentHelper.GetOrAdd<FrooxEngine.UIX.RectTransformWrapper>(handle).Data;
        handleRect.OffsetMin = new Vector2(-0.012f, -0.035f);
        handleRect.OffsetMax = new Vector2(0.012f, 0.035f);
        slider.HandleAnchorMinDrive = handleRect.AnchorMin_Element.Member;
        slider.HandleAnchorMaxDrive = handleRect.AnchorMax_Element.Member;
        ConverterComponentHelper.GetOrAdd<FrooxEngine.UIX.ImageWrapper>(handle).Data.InteractionTarget = false;

        var copy = ConverterComponentHelper.GetOrAdd<ValueCopyFloatWrapper>(track).Data;
        copy.Source = slider.Value_Element.Member;
        copy.Target = value;
        copy.WriteBack = true;
    }

    /// <summary>Blends a 3x3 axis grid continuously along X, then Y.</summary>
    public static (FrooxEngine.IField<float> x, FrooxEngine.IField<float> y) BuildContinuousAxes(
        GameObject stateObject, GameObject panel, Vector2 defaults, IReadOnlyList<ToggleState> grid, IConversionContext context)
    {
        if (grid.Count != 9)
            throw new ArgumentException("Axis grid needs nine samples", nameof(grid));

        var axes = EnsureCount<ValueMultiDriverFloatWrapper>(stateObject, 2);
        var x = axes[0].Data;
        var y = axes[1].Data;
        x.Value = defaults.x;
        y.Value = defaults.y;
        x.Drives.Clear();
        y.Drives.Clear();

        var rows = GeneratedObjectHelper.EnsureChild(stateObject.transform, "[Resonite] Axis Rows");
        var floats = grid.SelectMany(s => s.Floats.Values).GroupBy(v => v.target.Key).Select(g => g.First().target).ToList();
        var rowDrivers = EnsureCount<ValueGradientDriverFloatWrapper>(rows, floats.Count * 3);
        var rowValues = EnsureCount<ValueMultiDriverFloatWrapper>(rows, floats.Count * 3);
        var outerDrivers = EnsureCount<ValueGradientDriverFloatWrapper>(stateObject, floats.Count);
        var copies = EnsureCount<ValueCopyFloatWrapper>(stateObject, floats.Count * 3);
        for (var i = 0; i < floats.Count; i++)
        {
            var target = floats[i];
            var outer = outerDrivers[i].Data;
            outer.Interpolate = true;
            outer.Points.Clear();
            target.ResolveField(context, field => outer.Target = field);
            y.Drives.Add(outer.Progress_Element.Member);
            for (var row = 0; row < 3; row++)
            {
                var index = i * 3 + row;
                var rowValue = rowValues[index].Data;
                rowValue.Drives.Clear();
                var inner = rowDrivers[index].Data;
                inner.Interpolate = true;
                inner.Points.Clear();
                inner.Target = rowValue.Value_Element.Member;
                x.Drives.Add(inner.Progress_Element.Member);
                for (var column = 0; column < 3; column++)
                {
                    var point = inner.Points.Add();
                    point.Position = column - 1;
                    var sample = grid[row * 3 + column];
                    point.Value = sample.Floats.TryGetValue(target.Key, out var entry) ? entry.value : target.RestValue;
                }
                var outerPoint = outer.Points.Add();
                outerPoint.Position = row - 1;
                var copy = copies[index].Data;
                copy.Source = rowValue.Value_Element.Member;
                copy.Target = outerPoint.Value_Element.Member;
                copy.WriteBack = false;
            }
        }

        var bools = grid.SelectMany(s => s.Bools.Values).GroupBy(v => v.target.Key).Select(g => g.First().target).ToList();
        var boolRows = EnsureCount<ValueGradientDriverBoolWrapper>(rows, bools.Count * 3);
        var boolValues = EnsureCount<ValueMultiDriverBoolWrapper>(rows, bools.Count * 3);
        var boolOuter = EnsureCount<ValueGradientDriverBoolWrapper>(stateObject, bools.Count);
        var boolCopies = EnsureCount<ValueCopyBoolWrapper>(stateObject, bools.Count * 3);
        for (var i = 0; i < bools.Count; i++)
        {
            var target = bools[i];
            var outer = boolOuter[i].Data;
            outer.Interpolate = false;
            outer.Points.Clear();
            target.ResolveField(context, field => outer.Target = field);
            y.Drives.Add(outer.Progress_Element.Member);
            for (var row = 0; row < 3; row++)
            {
                var index = i * 3 + row;
                var rowValue = boolValues[index].Data;
                rowValue.Drives.Clear();
                var inner = boolRows[index].Data;
                inner.Interpolate = false;
                inner.Points.Clear();
                inner.Target = rowValue.Value_Element.Member;
                x.Drives.Add(inner.Progress_Element.Member);
                for (var column = 0; column < 3; column++)
                {
                    var point = inner.Points.Add();
                    point.Position = column - 1;
                    var sample = grid[row * 3 + column];
                    point.Value = sample.Bools.TryGetValue(target.Key, out var entry) ? entry.value : target.RestValue;
                }
                var outerPoint = outer.Points.Add();
                outerPoint.Position = row - 1;
                var copy = boolCopies[index].Data;
                copy.Source = rowValue.Value_Element.Member;
                copy.Target = outerPoint.Value_Element.Member;
                copy.WriteBack = false;
            }
        }

        var materials = MaterialSlots(grid);
        var materialRows = EnsureCount<ValueGradientDriverIntWrapper>(rows, materials.Count * 3);
        var materialValues = EnsureCount<ValueMultiDriverIntWrapper>(rows, materials.Count * 3);
        var materialOuter = EnsureCount<ValueGradientDriverIntWrapper>(stateObject, materials.Count);
        var materialCopies = EnsureCount<ValueCopyIntWrapper>(stateObject, materials.Count * 3);
        var materialMuxes = EnsureCount<AssetMultiplexerMaterialWrapper>(stateObject, materials.Count);
        for (var i = 0; i < materials.Count; i++)
        {
            var target = materials[i];
            var mux = materialMuxes[i].Data;
            mux.Assets.Clear();
            foreach (var sample in grid)
                mux.Assets.Add(GetMaterial(context, sample.ResolveMaterial(target)));
            target.ResolveSlot(context, slot => mux.Target_Element.Data = slot);
            var outer = materialOuter[i].Data;
            outer.Interpolate = false;
            outer.Points.Clear();
            outer.Target = mux.Index_Element.Member;
            y.Drives.Add(outer.Progress_Element.Member);
            for (var row = 0; row < 3; row++)
            {
                var index = i * 3 + row;
                var rowValue = materialValues[index].Data;
                rowValue.Drives.Clear();
                var inner = materialRows[index].Data;
                inner.Interpolate = false;
                inner.Points.Clear();
                inner.Target = rowValue.Value_Element.Member;
                x.Drives.Add(inner.Progress_Element.Member);
                for (var column = 0; column < 3; column++)
                {
                    var point = inner.Points.Add();
                    point.Position = column - 1;
                    point.Value = row * 3 + column;
                }
                var outerPoint = outer.Points.Add();
                outerPoint.Position = row - 1;
                var copy = materialCopies[index].Data;
                copy.Source = rowValue.Value_Element.Member;
                copy.Target = outerPoint.Value_Element.Member;
                copy.WriteBack = false;
            }
        }

        SetupSliderCanvas(panel, 0.24f);
        AddSlider(panel, "[Resonite] Horizontal", x.Value_Element.Member, defaults.x, -1, 1, 0.55f, 0.9f);
        AddSlider(panel, "[Resonite] Vertical", y.Value_Element.Member, defaults.y, -1, 1, 0.1f, 0.45f);
        return (x.Value_Element.Member, y.Value_Element.Member);
    }

    /// <summary>Sets both axes from one context menu action.</summary>
    public static void BuildAxisPreset(GameObject item, FrooxEngine.IField<float> x, FrooxEngine.IField<float> y, Vector2 position)
    {
        var setters = EnsureCount<ButtonValueSetFloatWrapper>(item, 2);
        setters[0].Data.TargetValue = x;
        setters[0].Data.SetValue = position.x;
        setters[1].Data.TargetValue = y;
        setters[1].Data.SetValue = position.y;
    }

    /// <summary>Opens or closes a draggable puppet panel from a context menu item.</summary>
    public static void BuildContinuousSliderToggle(GameObject item, GameObject panel)
    {
        panel.SetActive(false);
        var button = ConverterComponentHelper.GetOrAdd<FrooxEngine.ButtonToggleWrapper>(item).Data;
        button.TargetValue = new SlotActiveField(panel.transform);
    }

    /// <summary>
    /// Builds a menu item that sets a puppet to the given value
    /// </summary>
    public static void BuildPuppetStep(GameObject item, FrooxEngine.IField<float> puppetValue, float value)
    {
        var button = ConverterComponentHelper.GetOrAdd<ButtonValueSetFloatWrapper>(item).Data;
        button.TargetValue = puppetValue;
        button.SetValue = value;
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

    static FrooxEngine.IAssetProvider<FrooxEngine.Material> GetMaterial(IConversionContext context, Material material) =>
        material != null ? context.GetMaterial(material) : null;

    static List<MaterialSlotTarget> MaterialSlots(IEnumerable<ToggleState> states) =>
        states.SelectMany(s => s.Materials.Values).GroupBy(v => v.target.Key).Select(g => g.First().target).ToList();

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
