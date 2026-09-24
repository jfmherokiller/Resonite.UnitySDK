using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts VRCFury toggles into Resonite context menu toggles. The toggles are placed under an "Avatar Toggles"
/// item in the root context menu of the user wearing the avatar, following VRCFury's menu paths as submenus.
///
/// Supported actions:
/// - Object toggle (turn on / turn off / toggle) - drives the slot's active state
/// - Blendshape - drives the blendshape weight
/// Other actions (animation clips, materials...) are reported.
/// </summary>
public static class VRCFuryToggle
{
    public const string MENU_ROOT_NAME = "[Resonite] Avatar Toggles";

    struct ObjectAction
    {
        public Transform Object;
        public bool OnState;
    }

    struct BlendShapeAction
    {
        public SkinnedMeshRenderer Renderer;
        public int Index;
        public float OnWeight;
    }

    public static void Apply(Component target, object toggle, ref GameObject toggleObject, IConversionContext context,
        ConversionReporter report)
    {
        var avatarRoot = VRChatTypes.FindAvatarRoot(target.transform);
        var path = ReflectionAccessor.Get(toggle, "name", "");

        if (string.IsNullOrWhiteSpace(path))
            path = target.name;

        if (ReflectionAccessor.Get(toggle, "slider", false))
            report.Warning("slider", $"VRCFury toggle {path} is a slider, which is converted as a simple on/off toggle.", target);

        var objects = new List<ObjectAction>();
        var blendShapes = new List<BlendShapeAction>();

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

                    objects.Add(new ObjectAction()
                    {
                        Object = obj,
                        OnState = mode == "TurnOn" || (mode != "TurnOff" && !obj.gameObject.activeSelf),
                    });
                    break;

                case "BlendShapeAction":
                    CollectBlendShapes(avatarRoot, action, blendShapes);
                    break;

                default:
                    report.Warning(action.GetType().Name, $"VRCFury toggle {path} uses {action.GetType().Name}, which isn't converted.", target);
                    break;
            }
        }

        if (objects.Count == 0 && blendShapes.Count == 0)
        {
            GeneratedObjectHelper.DestroyWithEmptyParents(ref toggleObject);
            return;
        }

        // Build the menu hierarchy for the path
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var menu = EnsureMenu(avatarRoot, MENU_ROOT_NAME, "Avatar Toggles", isRoot: true);

        for (int i = 0; i < segments.Length - 1; i++)
            menu = EnsureMenu(menu.transform, "[Resonite] " + segments[i], segments[i], isRoot: false);

        var label = segments.Length > 0 ? segments[segments.Length - 1] : path;

        if (toggleObject == null || toggleObject.transform.parent != menu.transform)
        {
            GeneratedObjectHelper.Destroy(ref toggleObject);
            toggleObject = GeneratedObjectHelper.Create(menu.transform, "[Resonite] " + label);
        }

        SetupItem(toggleObject, label);

        // The toggle state, which the menu button flips. It drives the state of all the individual actions.
        var stateDriver = ConverterComponentHelper.GetOrAdd<ValueMultiDriverBoolWrapper>(toggleObject).Data;
        stateDriver.Value = ReflectionAccessor.Get(toggle, "defaultOn", false);
        stateDriver.Drives.Clear();

        var button = ConverterComponentHelper.GetOrAdd<FrooxEngine.ButtonToggleWrapper>(toggleObject).Data;
        button.TargetValue = stateDriver.Value_Element.Member;

        var objectDrivers = EnsureCount<BooleanValueDriverBoolWrapper>(toggleObject, objects.Count);

        for (int i = 0; i < objects.Count; i++)
        {
            var driver = objectDrivers[i].Data;

            driver.TrueValue = objects[i].OnState;
            driver.FalseValue = objects[i].Object.gameObject.activeSelf;
            driver.TargetField = new SlotActiveField(objects[i].Object);

            stateDriver.Drives.Add(driver.State_Element.Member);
        }

        var blendShapeDrivers = EnsureCount<BooleanValueDriverFloatWrapper>(toggleObject, blendShapes.Count);

        for (int i = 0; i < blendShapes.Count; i++)
        {
            var driver = blendShapeDrivers[i].Data;
            var action = blendShapes[i];

            driver.TrueValue = action.OnWeight;
            driver.FalseValue = BlendShapeFieldHelper.GetNormalizedWeight(action.Renderer.sharedMesh, action.Index,
                action.Renderer.GetBlendShapeWeight(action.Index));

            BlendShapeFieldHelper.RunWithField(context, action.Renderer, action.Index, field => driver.TargetField = field);

            stateDriver.Drives.Add(driver.State_Element.Member);
        }
    }

    static void CollectBlendShapes(Transform avatarRoot, object action, List<BlendShapeAction> blendShapes)
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
            if (renderer.sharedMesh == null)
                continue;

            var index = renderer.sharedMesh.GetBlendShapeIndex(name);

            if (index < 0)
                continue;

            blendShapes.Add(new BlendShapeAction()
            {
                Renderer = renderer,
                Index = index,
                OnWeight = BlendShapeFieldHelper.GetNormalizedWeight(renderer.sharedMesh, index, value),
            });
        }
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
