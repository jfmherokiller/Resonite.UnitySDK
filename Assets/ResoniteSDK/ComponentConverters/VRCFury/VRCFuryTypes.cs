using System.Linq;

/// <summary>
/// Full type names of VRCFury components. VRCFury types are internal, so they can only be accessed by name.
/// </summary>
public static class VRCFuryTypes
{
    public const string VRCFury = "VF.Model.VRCFury";
    public const string GlobalCollider = "VF.Component.VRCFuryGlobalCollider";

    /// <summary>
    /// Splits a VRCFury menu path into its parts. "\/" can be used for a slash within a name.
    /// </summary>
    public static string[] SplitMenuPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new string[0];

        return path
            .Replace("\\/", "\u0000")
            .Split('/')
            .Select(s => s.Replace("\u0000", "/"))
            .Where(s => s.Length > 0)
            .ToArray();
    }
}
