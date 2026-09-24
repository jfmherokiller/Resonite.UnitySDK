using UnityEngine;

/// <summary>
/// Helpers for converters that need to manage Resonite components on GameObjects other than their own
/// (e.g. a constraint that targets another transform)
/// </summary>
public static class ConverterComponentHelper
{
    /// <summary>
    /// Makes sure the given wrapper exists on the target GameObject. If it exists on a different GameObject
    /// (e.g. because the target has changed), it's moved over. Passing null target removes the wrapper.
    /// </summary>
    public static TWrapper EnsureOn<TWrapper>(ref TWrapper wrapper, GameObject target)
        where TWrapper : Component
    {
        if (wrapper != null && wrapper.gameObject != target)
            Remove(ref wrapper);

        if (wrapper == null && target != null)
            wrapper = target.AddComponent<TWrapper>();

        return wrapper;
    }

    /// <summary>
    /// Returns the existing component of given type on the GameObject, or adds a new one
    /// </summary>
    public static T GetOrAdd<T>(GameObject obj) where T : Component
    {
        var component = obj.GetComponent<T>();

        if (component == null)
            component = obj.AddComponent<T>();

        return component;
    }

    public static void Remove<T>(ref T component)
        where T : Component
    {
        if (component != null)
            Object.DestroyImmediate(component);

        component = null;
    }

    public static Vector3 SafeDivide(Vector3 a, Vector3 b)
    {
        return new Vector3(
            Mathf.Approximately(b.x, 0) ? 1 : a.x / b.x,
            Mathf.Approximately(b.y, 0) ? 1 : a.y / b.y,
            Mathf.Approximately(b.z, 0) ? 1 : a.z / b.z);
    }
}
