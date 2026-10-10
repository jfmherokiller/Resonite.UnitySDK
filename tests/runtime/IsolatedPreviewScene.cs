#if UNITY_EDITOR
using System;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>Routes new GameObjects into a disposable preview scene so fixtures never dirty the open scene.</summary>
public sealed class IsolatedPreviewScene : IDisposable
{
    readonly NewObjectSceneScope _target;

    public Scene Scene { get; }

    public IsolatedPreviewScene()
    {
        Scene = EditorSceneManager.NewPreviewScene();
        try
        {
            _target = new NewObjectSceneScope(Scene);
        }
        catch
        {
            EditorSceneManager.ClosePreviewScene(Scene);
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            _target.Dispose();
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(Scene);
        }
    }
}
#endif
