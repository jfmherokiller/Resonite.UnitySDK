/// <summary>
/// Can be implemented by a component (usually a converter) to force the slot generated for its GameObject to be
/// active or inactive in Resonite, without modifying the active state of the object in Unity.
/// </summary>
public interface ISlotActiveOverride
{
    bool ForceSlotInactive { get; }

    /// <summary>
    /// Forces the slot active even if the GameObject is inactive in Unity (e.g. VRCFury's ObjectState "Activate").
    /// If some other override on the same slot also forces it inactive, ForceSlotInactive wins.
    /// </summary>
    bool ForceSlotActive { get; }
}
