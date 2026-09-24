using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Helpers for reading serialized data from components whose types aren't available at compile time.
/// All the accessors are lenient - when a member is missing or has unexpected type, the fallback is returned.
/// </summary>
public static class ReflectionAccessor
{
    const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly Dictionary<(Type, string), MemberInfo> _members = new Dictionary<(Type, string), MemberInfo>();

    static MemberInfo FindMember(Type type, string name)
    {
        var key = (type, name);

        if (_members.TryGetValue(key, out var member))
            return member;

        // Private fields of base types aren't returned by GetField, so we need to walk the hierarchy
        for (var t = type; t != null && member == null; t = t.BaseType)
        {
            member = t.GetField(name, FLAGS | BindingFlags.DeclaredOnly);

            if (member == null)
                member = t.GetProperty(name, FLAGS | BindingFlags.DeclaredOnly);
        }

        _members.Add(key, member);

        return member;
    }

    public static bool Has(object obj, string name) => obj != null && FindMember(obj.GetType(), name) != null;

    public static object GetRaw(object obj, string name)
    {
        if (obj == null)
            return null;

        try
        {
            switch (FindMember(obj.GetType(), name))
            {
                case FieldInfo field:
                    return field.GetValue(obj);

                case PropertyInfo property when property.GetIndexParameters().Length == 0:
                    return property.GetValue(obj);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to read {name} from {obj.GetType().FullName}: {ex.Message}");
        }

        return null;
    }

    public static T Get<T>(object obj, string name, T fallback = default)
    {
        var value = GetRaw(obj, name);

        if (value is T typed)
            return typed;

        // Unity null objects pretend to be null, but don't pass the check above anyway, so this only handles
        // conversions between primitive types (e.g. enums to int)
        if (value != null && value is IConvertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                // Ignore, use fallback
            }
        }

        return fallback;
    }

    /// <summary>
    /// Reads an enum (or any integer) value as integer
    /// </summary>
    public static int GetInt(object obj, string name, int fallback = 0)
    {
        var value = GetRaw(obj, name);

        if (value == null)
            return fallback;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Reads an enum value as its name. This is more robust than comparing the numeric values, which can change
    /// between versions of the package.
    /// </summary>
    public static string GetEnumName(object obj, string name, string fallback = "")
    {
        var value = GetRaw(obj, name);

        return value is Enum ? value.ToString() : fallback;
    }

    /// <summary>
    /// Reads a Unity object reference. Destroyed/missing objects are returned as actual null.
    /// </summary>
    public static T GetObject<T>(object obj, string name) where T : UnityEngine.Object
    {
        var value = GetRaw(obj, name) as T;

        return value == null ? null : value;
    }

    /// <summary>
    /// Returns the Transform for a member that can be either a Transform, a Component or a GameObject reference
    /// </summary>
    public static Transform GetTransform(object obj, string name)
    {
        switch (GetRaw(obj, name))
        {
            case Component component when component != null:
                return component.transform;

            case GameObject gameObject when gameObject != null:
                return gameObject.transform;
        }

        return null;
    }

    /// <summary>
    /// Enumerates any list-like member. Null entries are preserved, so indices match the original collection.
    /// </summary>
    public static IEnumerable<object> GetList(object obj, string name)
    {
        if (!(GetRaw(obj, name) is IEnumerable enumerable))
            yield break;

        foreach (var item in enumerable)
            yield return item;
    }
}
