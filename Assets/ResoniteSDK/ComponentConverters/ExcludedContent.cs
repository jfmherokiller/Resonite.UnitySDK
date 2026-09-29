using UnityEngine;

/// <summary>
/// Named GameObjects whose whole subtree is excluded from conversion entirely - not just made inactive. For
/// third-party debug/companion tooling that has no purpose in Resonite, but would otherwise convert into real,
/// functional objects that are tedious to fully clean up afterward (e.g. VRCFT's face tracking debug companion
/// window: it links its own copy of the avatar's bones and blendshapes via VRCFury ArmatureLink/BlendShapeLink,
/// and adds its own menu toggle via a Full Controller, scattering generated objects across the whole avatar
/// hierarchy exactly like any other converted content would - simply leaving its own (inactive) debug mesh out
/// isn't enough, since everything it links elsewhere still gets created).
///
/// Note: this only prevents the excluded content from being converted on a fresh send. SceneConverter's removal
/// tracking only fires when the Unity-side object is actually destroyed, so if this content was already converted
/// before being excluded, the resulting Resonite objects won't be automatically cleaned up - that needs a manual
/// one-time removal, or starting from a fresh world/avatar instance.
/// </summary>
public static class ExcludedContent
{
    // VRCFT's (VRChat Face Tracking OSC) "Unified Expressions" debug companion window prefab.
    const string VRCFTDebugCompanion = "VF_UE_VRCFT";

    public static bool IsExcluded(Transform transform) => transform.name == VRCFTDebugCompanion;
}
