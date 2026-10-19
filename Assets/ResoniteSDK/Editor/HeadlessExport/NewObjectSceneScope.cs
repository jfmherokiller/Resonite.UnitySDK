using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Routes new GameObjects (Instantiate, new GameObject) into a given scene until disposed.
/// Needed because Instantiate of a scene object otherwise lands in the original's scene.
/// </summary>
public sealed class NewObjectSceneScope : IDisposable
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly MethodInfo GetTarget = typeof(EditorSceneManager).GetMethod("GetTargetSceneForNewGameObjects", Flags);
    static readonly MethodInfo SetTarget = typeof(EditorSceneManager)
        .GetMethod("SetTargetSceneForNewGameObjects", Flags, null, new[] { typeof(Scene) }, null);
    static readonly MethodInfo ClearTarget = typeof(EditorSceneManager).GetMethod("ClearTargetSceneForNewGameObjects", Flags);

    readonly Scene _previous;

    public NewObjectSceneScope(Scene scene)
    {
        if (GetTarget == null || SetTarget == null || ClearTarget == null)
            throw new NotSupportedException("This editor cannot route new GameObjects into another scene.");
        // Without an override the getter reports the active scene; re-pinning that on dispose leaves a stale
        // target once another scene is opened, and Unity crashes creating the next GameObject.
        var current = (Scene)GetTarget.Invoke(null, null);
        _previous = current.IsValid() && current != SceneManager.GetActiveScene() ? current : default;
        SetTarget.Invoke(null, new object[] { scene });
    }

    public void Dispose()
    {
        if (_previous.IsValid() && _previous.isLoaded)
            SetTarget.Invoke(null, new object[] { _previous });
        else
            ClearTarget.Invoke(null, null);
    }
}
