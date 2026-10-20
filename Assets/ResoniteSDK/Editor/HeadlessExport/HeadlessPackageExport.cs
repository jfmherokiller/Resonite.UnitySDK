using ResoniteLink;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Converts an avatar into a temporary headless Resonite world and exports it as a .resonitepackage.
/// The open scene, its asset cache and the Resonite SDK window's own connection are left untouched.
/// </summary>
public static class HeadlessPackageExport
{
    public static string WorkDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "ResoniteHeadless"));

    static string PluginSource
    {
        get
        {
            var script = AssetDatabase.FindAssets("HeadlessResoniteServer t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).First(p => p.EndsWith("/HeadlessResoniteServer.cs"));
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script), "HeadlessPlugin"));
        }
    }

    /// <summary>Runs the whole export. Call from the main thread; heavy waits run in the background.</summary>
    public static async Task<string> ExportAsync(GameObject avatar, string headless, string folder, bool includeVariants,
        Action<string> log)
    {
        if (avatar == null)
            throw new ArgumentException("Choose an avatar or object to export");
        if (!HeadlessResoniteServer.IsHeadless(headless))
            throw new DirectoryNotFoundException($"Not a Resonite Headless folder: {headless}");
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Choose an export folder");
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);

        var work = WorkDirectory;
        var source = PluginSource;
        var server = await Task.Run(async () =>
        {
            var bridge = HeadlessResoniteServer.BuildBridge(headless, source, work, log);
            return await HeadlessResoniteServer.StartAsync(headless, bridge, work, log, default);
        });
        try
        {
            log($"Converting {avatar.name} (Resonite {server.ResoniteVersion})...");
            var slot = ConvertCopy(avatar, server);
            log("Exporting package...");
            var name = avatar.name;
            var path = await Task.Run(() => server.ExportPackageAsync(slot, folder, name, includeVariants));
            log($"Exported {path} ({new FileInfo(path).Length / (1024f * 1024f):0.0} MB)");
            return path;
        }
        finally
        {
            await Task.Run(server.Dispose);
        }
    }

    /// <summary>Sends a copy of root through the normal scene conversion, inside a temporary scene; returns its slot ID.</summary>
    static string ConvertCopy(GameObject root, HeadlessResoniteServer server)
    {
        var source = SceneManager.GetActiveScene();
        var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var window = ScriptableObject.CreateInstance<ResoniteLinkWindow>();
        window.hideFlags = HideFlags.HideAndDontSave;
        window.ConvertSkybox = false;
        LinkInterface link = null;
        var target = new NewObjectSceneScope(temp);
        try
        {
            SceneManager.SetActiveScene(temp);
            var copy = UnityEngine.Object.Instantiate(root);
            copy.name = root.name;
            copy.SetActive(true);
            if (copy.scene != temp)
                throw new InvalidOperationException("Could not isolate the export copy from the open scene");

            // Never block the main thread on link tasks directly: their continuations need it.
            link = Task.Run(server.ConnectAsync).Result;
            var session = Task.Run(() => link.GetSessionData()).Result;
            if (!session.Success)
                throw new InvalidOperationException("ResoniteLink session failed: " + session.ErrorInfo);
            window.UseLink(link, session.UniqueSessionId, server.Port);

            var converter = new SceneConverter();
            converter.EnsureInitialized(window);
            converter.ConvertScene();
            if (converter.IsCorrupted || !link.IsConnected)
                throw new InvalidOperationException("Conversion failed; see the Console" +
                    (link.FailureException != null ? ": " + link.FailureException.Message : ""));
            return converter.GetTransformSlotId(copy.transform);
        }
        finally
        {
            target.Dispose();
            link?.Dispose();
            UnityEngine.Object.DestroyImmediate(window);
            if (source.IsValid())
                SceneManager.SetActiveScene(source);
            EditorSceneManager.CloseScene(temp, true);
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>The selected scene object's root, otherwise the only active avatar descriptor root.</summary>
    public static GameObject DefaultTarget()
    {
        var selected = Selection.activeGameObject;
        if (selected != null && selected.scene.IsValid() && !EditorUtility.IsPersistent(selected))
            return selected.transform.root.gameObject;
        var avatars = SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(g => g.activeInHierarchy && g.GetComponents<UnityEngine.Component>()
                .Any(c => c != null && c.GetType().Name == "VRCAvatarDescriptor"))
            .ToList();
        return avatars.Count == 1 ? avatars[0] : null;
    }
}
