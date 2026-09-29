using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// Editor asset database access that also compiles in player builds (e.g. Android/Quest avatar uploads),
/// where UnityEditor isn't referenced. Outside the editor everything behaves as a procedural, non-persistent asset.
public static class EditorAssetUtility
{
    public static string GetAssetPath(Object asset)
    {
#if UNITY_EDITOR
        return AssetDatabase.GetAssetPath(asset);
#else
        return null;
#endif
    }

    /// Importer timestamp of the asset at path, or null if it has no importer.
    public static ulong? GetImporterTimestamp(string assetPath)
    {
#if UNITY_EDITOR
        return AssetImporter.GetAtPath(assetPath)?.assetTimeStamp;
#else
        return null;
#endif
    }

    /// TextureImporter max size of a texture asset, or null if it isn't an imported texture.
    public static int? GetTextureMaxSize(Object texture)
    {
#if UNITY_EDITOR
        var path = AssetDatabase.GetAssetPath(texture);
        if (!string.IsNullOrWhiteSpace(path) && AssetImporter.GetAtPath(path) is TextureImporter importer)
            return importer.maxTextureSize;
#endif
        return null;
    }

    public static bool IsPersistent(Object asset)
    {
#if UNITY_EDITOR
        return EditorUtility.IsPersistent(asset);
#else
        return false;
#endif
    }

    public static T LoadAssetByGuid<T>(string guid) where T : Object
    {
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
#else
        return null;
#endif
    }
}
