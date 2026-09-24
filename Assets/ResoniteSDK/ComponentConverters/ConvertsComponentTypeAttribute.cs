using System;

/// <summary>
/// Registers a converter for Unity component types by their full type name, instead of by the generic argument
/// of <see cref="ResoniteComponentConverter{T}"/>.
///
/// This allows writing converters for components from third party packages (e.g. VRChat SDK or VRCFury) without
/// requiring those packages to be present at compile time. Converters using this should derive from
/// <see cref="ResoniteComponentConverter{T}"/> with <see cref="UnityEngine.Component"/> and read the data through
/// reflection (see <see cref="ReflectionAccessor"/>). Type names that can't be found are silently ignored.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class ConvertsComponentTypeAttribute : Attribute
{
    public string TypeName { get; private set; }

    public ConvertsComponentTypeAttribute(string typeName)
    {
        TypeName = typeName;
    }
}
