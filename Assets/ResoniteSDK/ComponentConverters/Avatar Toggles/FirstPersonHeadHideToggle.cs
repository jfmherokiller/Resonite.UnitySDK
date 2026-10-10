using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

[Serializable]
public sealed class FirstPersonHeadHideToggle
{
    public GameObject MenuItem;
    public ResRef<FrooxEngine.RenderTransformOverride> PoseOverride;
    public ResRef<FrooxEngine.RenderTransformOverride> HideOverride;
    [NonSerialized] int generation;

    public void Apply(Transform avatarRoot, Transform head, bool defaultHidden, IConversionContext context)
    {
        if (head == null || !Included(head, avatarRoot))
        {
            Clear();
            return;
        }

        if ((HideOverride.Container != null && HideOverride.Container.transform != head)
            || (PoseOverride.Container != null && PoseOverride.Container.transform != head))
        {
            Clear();
            PoseOverride = default;
        }

        var conflicts = head.gameObject.GetResoniteComponents<FrooxEngine.RenderTransformOverride>()
            .Any(r => r != HideOverride.Binding && r.Context == Renderite.Shared.RenderingContext.UserView
                && r.ScaleOverride.HasValue);
        if (conflicts)
        {
            Clear();
            Debug.LogWarning("Cannot add Hide Head: the head already has a UserView scale override.", head);
            return;
        }

        var current = ++generation;
        var sources = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => Included(r.transform, avatarRoot) && r.bones.Any(b => b != null &&
                (b == head || b.IsChildOf(head) || head.IsChildOf(b)))).ToArray();
        var renderers = new FrooxEngine.SkinnedMeshRenderer[sources.Length];
        var remaining = sources.Length;

        if (remaining == 0)
            Configure(avatarRoot, head, defaultHidden, renderers);

        for (int i = 0; i < sources.Length; i++)
        {
            var index = i;
            var source = sources[i];
            context.RunOnConverted(source, () =>
            {
                if (current != generation || head == null || avatarRoot == null)
                    return;
                renderers[index] = source != null ? source.gameObject.GetResoniteComponent<FrooxEngine.SkinnedMeshRenderer>().Binding : null;
                if (--remaining == 0)
                    Configure(avatarRoot, head, defaultHidden, renderers.Where(r => r != null).ToArray());
            });
        }
    }

    void Configure(Transform avatarRoot, Transform head, bool defaultHidden, FrooxEngine.SkinnedMeshRenderer[] renderers)
    {
        // Reserve the first override for native desktop pose instrumentation, which disables itself in VR.
        if (PoseOverride.Container == null)
        {
            RemoveHideOverride();
            PoseOverride = AddOverride(head.gameObject);
            PoseOverride.Container.hideFlags = HideFlags.DontSave;
            PoseOverride.Binding.Enabled = false;
            PoseOverride.Binding.Context = Renderite.Shared.RenderingContext.UserView;
            SetFilter(PoseOverride.Container, "Context");
        }

        // Recreate on topology changes: ResoniteLink does not truncate lists and null renderer entries are unsafe.
        if (HideOverride.Container != null && !HideOverride.Binding.SkinnedMeshRenderers.Data.Select(r => r.Data).SequenceEqual(renderers))
            RemoveHideOverride();
        if (HideOverride.Container == null)
        {
            HideOverride = AddOverride(head.gameObject);
            HideOverride.Container.hideFlags = HideFlags.DontSave;
            foreach (var renderer in renderers)
                HideOverride.Binding.SkinnedMeshRenderers.Add(renderer);
        }

        var data = HideOverride.Binding;
        data.Enabled = defaultHidden;
        data.Context = Renderite.Shared.RenderingContext.UserView;
        data.PositionOverride = null;
        data.RotationOverride = null;
        data.ScaleOverride = Vector3.zero;
        SetFilter(HideOverride.Container, "Context", "PositionOverride", "RotationOverride", "ScaleOverride", "SkinnedMeshRenderers");

        var menu = AvatarToggleBuilder.EnsureMenu(avatarRoot, Array.Empty<string>());
        menu.tag = "EditorOnly";
        AvatarToggleBuilder.EnsureItem(ref MenuItem, menu, "Hide Head (First Person)");
        AvatarToggleBuilder.BuildToggleLink(MenuItem, data.Enabled_Element.Member);
    }

    public void Clear()
    {
        generation++;
        GeneratedObjectHelper.DestroyWithEmptyParents(ref MenuItem);
        RemoveHideOverride();
        // Native desktop instrumentation may own the reserved pose fields after export.
    }

    void RemoveHideOverride()
    {
        if (HideOverride.Container != null)
        {
            var filter = HideOverride.Container.GetComponent<ResoniteMemberFilter>();
            if (filter != null)
            {
                filter.Entries.RemoveAll(e => e == null || e.Component == null || e.Component == HideOverride.Container);
                if (filter.Entries.Count == 0 && (filter.hideFlags & HideFlags.DontSave) != 0)
                    Object.DestroyImmediate(filter);
            }
            Object.DestroyImmediate(HideOverride.Container);
        }
        HideOverride = default;
    }

    static ResRef<FrooxEngine.RenderTransformOverride> AddOverride(GameObject target)
    {
        var container = target.AddComponent<FirstPersonHeadRenderOverride>();
        container.Initialize<FirstPersonHeadRenderOverrideBinding>();
        return new ResRef<FrooxEngine.RenderTransformOverride>(container);
    }

    static void SetFilter(ResoniteComponent component, params string[] members)
    {
        var existing = component.GetComponent<ResoniteMemberFilter>();
        ResoniteMemberFilter.Set(component, members);
        if (existing == null)
            component.GetComponent<ResoniteMemberFilter>().hideFlags |= HideFlags.DontSave;
    }

    static bool Included(Transform transform, Transform root)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (ExcludedContent.IsExcluded(current)) return false;
            if (current == root) return true;
        }
        return false;
    }
}
