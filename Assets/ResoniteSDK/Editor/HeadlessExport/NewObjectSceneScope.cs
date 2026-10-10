using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Routes new GameObjects (Instantiate, new GameObject) into a given scene until disposed.
/// Needed because the editor can pin new objects to the user's scene regardless of the active scene.
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
        _previous = (Scene)GetTarget.Invoke(null, null);
        SetTarget.Invoke(null, new object[] { scene });
    }

    public void Dispose()
    {
        if (_previous.IsValid())
            SetTarget.Invoke(null, new object[] { _previous });
        else
            ClearTarget.Invoke(null, null);
    }
}
