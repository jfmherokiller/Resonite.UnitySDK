using System.Collections.Generic;

/// <summary>
/// The generated bindings always send all members of a component. ResoniteLink leaves any omitted members at their
/// Resonite defaults, but members that are sent are applied - so any member the converter didn't set would be
/// overwritten with C# default (0/null). This also clears internal drives (members starting with underscore) that
/// many components set up for themselves when they're created (e.g. VirtualParent driving its slot's transform).
///
/// Partial wrappers use this to only send the members that the converter explicitly assigns.
/// </summary>
public static class PartialMembers
{
    static readonly string[] BASE_MEMBERS = { "persistent", "Enabled" };

    public static ResoniteLink.Component Filter(ResoniteLink.Component component, ICollection<string> keep)
    {
        if (component?.Members == null)
            return component;

        var remove = new List<string>();

        foreach (var name in component.Members.Keys)
            if (!(keep?.Contains(name) ?? false) && System.Array.IndexOf(BASE_MEMBERS, name) < 0)
                remove.Add(name);

        foreach (var name in remove)
            component.Members.Remove(name);

        return component;
    }

    /// <summary>
    /// Removes internal members (starting with underscore) from all sync objects in given list member.
    /// Used for lists of sync objects that contain internal drives (e.g. DynamicBoneChain bones).
    /// </summary>
    public static void StripInternalFromListElements(ResoniteLink.Component component, string listName, params string[] alsoRemove)
    {
        if (component?.Members == null || !component.Members.TryGetValue(listName, out var member))
            return;

        if (!(member is ResoniteLink.SyncList list) || list.Elements == null)
            return;

        foreach (var element in list.Elements)
        {
            if (!(element is ResoniteLink.SyncObject syncObject) || syncObject.Members == null)
                continue;

            var remove = new List<string>();

            foreach (var name in syncObject.Members.Keys)
                if (name.StartsWith("_") || System.Array.IndexOf(alsoRemove, name) >= 0)
                    remove.Add(name);

            foreach (var name in remove)
                syncObject.Members.Remove(name);
        }
    }
}
