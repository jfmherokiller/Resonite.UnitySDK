using System;
using UnityEngine;

/// <summary>
/// Reference to the Active field of the slot generated for a Transform. This lets Resonite components reference
/// or drive the active state of converted objects (e.g. toggles). The scene converter resolves it to the ID of
/// the slot's field.
/// </summary>
[Serializable]
public class SlotActiveField : FrooxEngine.IField<bool>
{
    public Transform Transform;

    public SlotActiveField(Transform transform)
    {
        Transform = transform;
    }

    public override bool Equals(object obj) => obj is SlotActiveField other && other.Transform == Transform;
    public override int GetHashCode() => Transform == null ? 0 : Transform.GetHashCode();
}
