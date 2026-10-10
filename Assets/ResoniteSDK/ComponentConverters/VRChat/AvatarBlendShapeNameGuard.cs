using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public sealed class AvatarBlendShapeNameGuard : AssetMessagePostProcessor, IEquatable<AvatarBlendShapeNameGuard>
{
    static readonly Regex RightToken = new Regex(@"(?<!\p{L})[rR](?!\p{L})", RegexOptions.CultureInvariant);
    [SerializeField] string[] explicitVisemes;

    AvatarBlendShapeNameGuard(HashSet<string> explicitVisemes) =>
        this.explicitVisemes = explicitVisemes.Where(n => !string.IsNullOrEmpty(n)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public static AssetMessagePostProcessor ForRenderer(SkinnedMeshRenderer renderer, ref AvatarBlendShapeNameGuard cached)
    {
        var root = VRChatTypes.FindAvatarRoot(renderer.transform);
        var descriptor = root.GetComponents<Component>()
            .FirstOrDefault(c => c != null && c.GetType().FullName == VRChatTypes.AvatarDescriptor);
        if (descriptor == null)
            return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (ReflectionAccessor.GetObject<SkinnedMeshRenderer>(descriptor, "VisemeSkinnedMesh") == renderer)
        {
            var mode = ReflectionAccessor.GetEnumName(descriptor, "lipSync", "Default");
            if (mode == "VisemeBlendShape" || mode == "Default")
                names.UnionWith(ReflectionAccessor.Get<string[]>(descriptor, "VisemeBlendShapes") ?? Array.Empty<string>());
            else if (mode == "JawFlapBlendShape")
                names.Add(ReflectionAccessor.Get(descriptor, "MouthOpenBlendShapeName", ""));
        }

        names.RemoveWhere(string.IsNullOrEmpty);
        if (cached == null || !names.SetEquals(cached.explicitVisemes))
            cached = new AvatarBlendShapeNameGuard(names);
        return cached;
    }

    public override void Process(ResoniteLink.Message message)
    {
        if (!(message is ResoniteLink.ImportMeshRawData mesh) || mesh.BlendShapes == null)
            return;

        var reserved = new HashSet<string>(mesh.BlendShapes.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < mesh.BlendShapes.Count; i++)
        {
            var shape = mesh.BlendShapes[i];
            var name = shape.Name;
            if (string.IsNullOrEmpty(name) || explicitVisemes.Contains(name)
                || name.Equals("r", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("vrc.v_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("v_", StringComparison.OrdinalIgnoreCase))
                continue;

            // Native DirectVisemeDriver treats isolated directional R as the RR phoneme.
            var expanded = RightToken.Replace(name, "Right");
            if (expanded == name)
                continue;

            var unique = expanded;
            int suffix = 0;
            while (!reserved.Add(unique))
                unique = expanded + "__SDK" + (++suffix).ToString(System.Globalization.CultureInfo.InvariantCulture);
            shape.Name = unique;
        }
    }

    public override bool HasChanged() => false;

    public bool Equals(AvatarBlendShapeNameGuard other) =>
        other != null && explicitVisemes.SequenceEqual(other.explicitVisemes, StringComparer.Ordinal);

    public override bool Equals(object other) => other is AvatarBlendShapeNameGuard guard && Equals(guard);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var name in explicitVisemes)
            hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(name));
        return hash;
    }
}
