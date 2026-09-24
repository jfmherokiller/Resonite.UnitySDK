#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts the menus of VRCFury Full Controllers. VRCFury merges their menus, parameters and controllers into the
/// avatar when it's uploaded, so they're converted here the same way as the avatar's own expression menu
/// (see <see cref="VRCExpressionMenuToggles"/>).
///
/// Animated paths are resolved like VRCFury does: relative to the component's object (or the root object override),
/// after applying the binding rewrites, falling back to the avatar root.
/// </summary>
public static class VRCFuryFullController
{
    public static ExpressionMenuSource CreateSource(Component component, object feature)
    {
        var source = new ExpressionMenuSource();

        foreach (var entry in ReflectionAccessor.GetList(feature, "menus"))
        {
            var menu = ReflectionAccessor.GetObject<Object>(ReflectionAccessor.GetRaw(entry, "menu"), "objRef");

            if (menu != null)
                source.Menus.Add((menu, VRCFuryTypes.SplitMenuPath(ReflectionAccessor.Get(entry, "prefix", ""))));
        }

        foreach (var entry in ReflectionAccessor.GetList(feature, "prms"))
        {
            var parameters = ReflectionAccessor.GetObject<Object>(ReflectionAccessor.GetRaw(entry, "parameters"), "objRef");

            if (parameters != null)
                source.Parameters.Add(parameters);
        }

        foreach (var entry in ReflectionAccessor.GetList(feature, "controllers"))
        {
            if (ReflectionAccessor.GetEnumName(entry, "type") != "FX")
                continue;

            var controller = ReflectionAccessor.GetObject<RuntimeAnimatorController>(ReflectionAccessor.GetRaw(entry, "controller"), "objRef");

            if (controller != null)
                source.Controllers.Add(controller);
        }

        var avatarRoot = VRChatTypes.FindAvatarRoot(component.transform);
        var rootOverride = ReflectionAccessor.GetTransform(feature, "rootObjOverride");
        var baseObject = rootOverride != null ? rootOverride : component.transform;
        var rootBindingsApplyToAvatar = ReflectionAccessor.Get(feature, "rootBindingsApplyToAvatar", false);

        var rewrites = ReflectionAccessor.GetList(feature, "rewriteBindings")
            .Select(r => (from: ReflectionAccessor.Get(r, "from", ""), to: ReflectionAccessor.Get(r, "to", ""),
                delete: ReflectionAccessor.Get(r, "delete", false)))
            .ToList();

        source.ResolvePath = path =>
        {
            if (rootBindingsApplyToAvatar && string.IsNullOrEmpty(path))
                return avatarRoot;

            path = RewritePath(path, rewrites);

            // Deleted binding
            if (path == null)
                return null;

            // Absolute path from the avatar root
            if (path.StartsWith("/"))
                return Find(avatarRoot, path.Substring(1));

            var found = Find(baseObject, path);

            return found != null ? found : Find(avatarRoot, path);
        };

        return source;
    }

    static Transform Find(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);

    /// <summary>
    /// Applies the binding rewrites, following VRCFury's AnimationBindingUtils.RewriteRelativePath.
    /// Returns null if the binding is deleted.
    /// </summary>
    static string RewritePath(string path, List<(string from, string to, bool delete)> rewrites)
    {
        string Append(string to, string suffix)
        {
            if (to == "" || suffix.StartsWith("/")) return suffix;
            if (suffix == "") return to;
            return to == "/" ? "/" + suffix : to + "/" + suffix;
        }

        foreach (var rewrite in rewrites)
        {
            var from = rewrite.from ?? "";
            var to = rewrite.to ?? "";

            while (from.Length > 1 && from.EndsWith("/"))
                from = from.Substring(0, from.Length - 1);

            while (to.Length > 1 && to.EndsWith("/"))
                to = to.Substring(0, to.Length - 1);

            if (from == "")
                path = Append(to, path);
            else if (path.StartsWith(from + "/"))
                path = Append(to, path.Substring(from.Length + 1));
            else if (path == from)
                path = to;
            else
                continue;

            if (rewrite.delete)
                return null;
        }

        return path;
    }
}
#endif
