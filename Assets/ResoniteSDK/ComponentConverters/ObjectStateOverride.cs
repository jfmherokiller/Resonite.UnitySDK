using UnityEngine;

/// <summary>
/// Forces the slot generated for this GameObject to be active or inactive in Resonite, regardless of its actual
/// state in Unity. Added to arbitrary target GameObjects (not necessarily the feature's own) by converters like
/// VRCFuryConverter's ObjectState handling.
/// </summary>
[AddComponentMenu("")]
public class ObjectStateOverride : MonoBehaviour, ISlotActiveOverride
{
    public bool Active = true;

    public bool ForceSlotInactive => !Active;
    public bool ForceSlotActive => Active;
}
