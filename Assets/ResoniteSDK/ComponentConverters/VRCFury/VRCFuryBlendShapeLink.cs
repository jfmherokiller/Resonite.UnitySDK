using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts VRCFury's Blend Shape Link. The linked renderers' blendshapes are driven by the matching blendshapes
/// on the base renderer using ValueCopy components, so e.g. clothing follows body shape changes.
/// </summary>
public static class VRCFuryBlendShapeLink
{
    public const string OBJECT_NAME = "[Resonite] BlendShape Link";

    struct Link
    {
        public SkinnedMeshRenderer Linked;
        public int BaseIndex;
        public int LinkedIndex;
    }

    public static void Apply(Component target, IEnumerable<object> features, ref GameObject container, IConversionContext context)
    {
        var links = new List<(SkinnedMeshRenderer baseRenderer, Link link)>();

        foreach (var feature in features)
            Collect(target, feature, links);

        if (links.Count == 0)
        {
            GeneratedObjectHelper.Destroy(ref container);
            return;
        }

        if (container == null)
            container = GeneratedObjectHelper.EnsureChild(target.transform, OBJECT_NAME);

        var copies = container.GetComponents<ValueCopyFloatWrapper>().ToList();

        while (copies.Count < links.Count)
            copies.Add(container.AddComponent<ValueCopyFloatWrapper>());

        while (copies.Count > links.Count)
        {
            UnityEngine.Object.DestroyImmediate(copies[copies.Count - 1]);
            copies.RemoveAt(copies.Count - 1);
        }

        for (int i = 0; i < links.Count; i++)
        {
            var copy = copies[i].Data;
            var (baseRenderer, link) = links[i];

            copy.WriteBack = false;

            BlendShapeFieldHelper.RunWithField(context, baseRenderer, link.BaseIndex, field => copy.Source = field);
            BlendShapeFieldHelper.RunWithField(context, link.Linked, link.LinkedIndex, field => copy.Target = field);
        }
    }

    static void Collect(Component target, object feature, List<(SkinnedMeshRenderer, Link)> links)
    {
        var avatarRoot = VRChatTypes.FindAvatarRoot(target.transform);
        var baseRenderer = FindBase(avatarRoot, ReflectionAccessor.Get(feature, "baseObj", ""));

        if (baseRenderer == null || baseRenderer.sharedMesh == null)
        {
            Debug.LogWarning($"VRCFury Blend Shape Link on {target.name} couldn't find the base renderer.", target);
            return;
        }

        var baseMesh = baseRenderer.sharedMesh;
        var includeAll = ReflectionAccessor.Get(feature, "includeAll", true);
        var exactMatch = ReflectionAccessor.Get(feature, "exactMatch", false);

        var excludes = new HashSet<string>(ReflectionAccessor.GetList(feature, "excludes")
            .Select(e => ReflectionAccessor.Get(e, "name", "")).Where(n => !string.IsNullOrEmpty(n)),
            StringComparer.OrdinalIgnoreCase);

        var includes = ReflectionAccessor.GetList(feature, "includes")
            .Select(i => (baseName: ReflectionAccessor.Get(i, "nameOnBase", ""), linkedName: ReflectionAccessor.Get(i, "nameOnLinked", "")))
            .Where(i => !string.IsNullOrEmpty(i.baseName) && !string.IsNullOrEmpty(i.linkedName))
            .ToList();

        foreach (var skin in ReflectionAccessor.GetList(feature, "linkSkins"))
        {
            var linked = ReflectionAccessor.GetObject<SkinnedMeshRenderer>(skin, "renderer");

            if (linked == null || linked.sharedMesh == null || linked == baseRenderer)
                continue;

            var linkedMesh = linked.sharedMesh;
            var linkedIndices = new HashSet<int>();

            void Add(int baseIndex, int linkedIndex)
            {
                if (baseIndex >= 0 && linkedIndex >= 0 && linkedIndices.Add(linkedIndex))
                    links.Add((baseRenderer, new Link() { Linked = linked, BaseIndex = baseIndex, LinkedIndex = linkedIndex }));
            }

            // Explicit mappings take precedence
            foreach (var (baseName, linkedName) in includes)
                Add(baseMesh.GetBlendShapeIndex(baseName), linkedMesh.GetBlendShapeIndex(linkedName));

            if (!includeAll)
                continue;

            for (int i = 0; i < linkedMesh.blendShapeCount; i++)
            {
                var name = linkedMesh.GetBlendShapeName(i);

                if (excludes.Contains(name))
                    continue;

                Add(FindBlendShape(baseMesh, name, exactMatch), i);
            }
        }
    }

    static int FindBlendShape(Mesh mesh, string name, bool exact)
    {
        var index = mesh.GetBlendShapeIndex(name);

        if (index >= 0 || exact)
            return index;

        for (int i = 0; i < mesh.blendShapeCount; i++)
            if (string.Equals(mesh.GetBlendShapeName(i), name, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }

    static SkinnedMeshRenderer FindBase(Transform avatarRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = "Body";

        var found = avatarRoot.Find(path);

        if (found == null)
        {
            // VRCFury also accepts just the object name
            var name = path.Split('/').Last();
            found = avatarRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        }

        return found != null ? found.GetComponent<SkinnedMeshRenderer>() : null;
    }
}
