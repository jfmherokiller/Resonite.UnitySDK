using System;
using UnityEngine;

/// <summary>
/// Reference to a field of the slot generated for a Transform (e.g. its Active state or local Position).
/// This lets Resonite components reference or drive the fields of converted slots. The scene converter
/// resolves these to the IDs of the slot's fields.
/// </summary>
[Serializable]
public abstract class SlotFieldReference
{
    public Transform Transform;

    protected SlotFieldReference(Transform transform)
    {
        Transform = transform;
    }

    /// <summary>
    /// Returns the ID of the referenced field on the ResoniteLink slot
    /// </summary>
    public abstract string GetFieldId(ResoniteLink.Slot slot);

    public override bool Equals(object obj) => obj != null && obj.GetType() == GetType() && ((SlotFieldReference)obj).Transform == Transform;
    public override int GetHashCode() => (Transform == null ? 0 : Transform.GetHashCode()) ^ GetType().GetHashCode();
}

[Serializable]
public class SlotActiveField : SlotFieldReference, FrooxEngine.IField<bool>
{
    public SlotActiveField(Transform transform) : base(transform) { }
    public override string GetFieldId(ResoniteLink.Slot slot) => slot.IsActive.ID;
}

[Serializable]
public class SlotPositionField : SlotFieldReference, FrooxEngine.IField<Vector3>
{
    public SlotPositionField(Transform transform) : base(transform) { }
    public override string GetFieldId(ResoniteLink.Slot slot) => slot.Position.ID;
}

[Serializable]
public class SlotRotationField : SlotFieldReference, FrooxEngine.IField<Quaternion>
{
    public SlotRotationField(Transform transform) : base(transform) { }
    public override string GetFieldId(ResoniteLink.Slot slot) => slot.Rotation.ID;
}
