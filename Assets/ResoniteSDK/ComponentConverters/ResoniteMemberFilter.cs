using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Limits which members of Resonite components are sent to Resonite.
///
/// The bindings always send all members of a component. ResoniteLink leaves any omitted members at their Resonite
/// defaults, but members that are sent are applied - so any member the converter didn't set would be overwritten
/// with C# default (0/null). This also clears internal drives (members starting with underscore) that many
/// components set up for themselves when they're created (e.g. VirtualParent driving its slot's transform).
///
/// Converters register the members they assign with <see cref="Set"/> and the scene converter applies the filter
/// with <see cref="Apply"/> when collecting the component data. The filter lives on the same GameObject as the
/// filtered components, so it works for any ResoniteComponent without needing specialized wrapper types.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public class ResoniteMemberFilter : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public ResoniteComponent Component;

        /// <summary>
        /// Members to send. Base component members (persistent, Enabled) are always sent.
        /// </summary>
        public List<string> Members = new List<string>();

        /// <summary>
        /// List members containing sync objects with internal drives (e.g. DynamicBoneChain.Bones).
        /// Members starting with underscore are removed from their elements.
        /// </summary>
        public List<string> StripInternalFromLists = new List<string>();

        /// <summary>
        /// Additional members to remove from the elements of <see cref="StripInternalFromLists"/>
        /// </summary>
        public List<string> StripFromListElements = new List<string>();
    }

    static readonly string[] BASE_MEMBERS = { "persistent", "Enabled" };

    public List<Entry> Entries = new List<Entry>();

    /// <summary>
    /// Registers the members that will be sent for the component. Everything else stays at Resonite's defaults.
    /// </summary>
    public static Entry Set(ResoniteComponent component, params string[] members)
    {
        var filter = component.GetComponent<ResoniteMemberFilter>();

        if (filter == null)
        {
            filter = component.gameObject.AddComponent<ResoniteMemberFilter>();
            filter.hideFlags = HideFlags.HideInInspector;
        }

        filter.Entries.RemoveAll(e => e == null || e.Component == null);

        var entry = filter.Entries.FirstOrDefault(e => e.Component == component);

        if (entry == null)
        {
            entry = new Entry() { Component = component };
            filter.Entries.Add(entry);
        }

        entry.Members = members.ToList();
        entry.StripInternalFromLists.Clear();
        entry.StripFromListElements.Clear();

        return entry;
    }

    /// <summary>
    /// Applies the registered filter (if any) to the collected component data
    /// </summary>
    public static void Apply(ResoniteComponent component, ResoniteLink.Component data)
    {
        if (data?.Members == null)
            return;

        var filter = component.GetComponent<ResoniteMemberFilter>();
        var entry = filter != null ? filter.Entries.FirstOrDefault(e => e != null && e.Component == component) : null;

        if (entry == null)
            return;

        foreach (var name in data.Members.Keys.ToList())
            if (!entry.Members.Contains(name) && Array.IndexOf(BASE_MEMBERS, name) < 0)
                data.Members.Remove(name);

        foreach (var listName in entry.StripInternalFromLists)
            StripListElements(data, listName, entry.StripFromListElements);
    }

    static void StripListElements(ResoniteLink.Component data, string listName, List<string> alsoRemove)
    {
        if (!data.Members.TryGetValue(listName, out var member) || !(member is ResoniteLink.SyncList list) || list.Elements == null)
            return;

        foreach (var element in list.Elements)
        {
            if (!(element is ResoniteLink.SyncObject syncObject) || syncObject.Members == null)
                continue;

            foreach (var name in syncObject.Members.Keys.ToList())
                if (name.StartsWith("_") || alsoRemove.Contains(name))
                    syncObject.Members.Remove(name);
        }
    }
}
