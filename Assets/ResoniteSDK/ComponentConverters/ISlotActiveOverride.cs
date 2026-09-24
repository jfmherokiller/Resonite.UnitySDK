/// <summary>
/// Can be implemented by a component (usually a converter) to force the slot generated for its GameObject to be
/// inactive in Resonite, without modifying the active state of the object in Unity.
/// </summary>
public interface ISlotActiveOverride
{
    bool ForceSlotInactive { get; }
}
