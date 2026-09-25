using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component.
// The type name has been verified against Resonite through ResoniteLink.
[AddComponentMenu("FrooxEngine/ProtoFlux/FieldDriveBase<floatQ>.Proxy")]
public class ProtoFluxFieldDriveProxyFloatQWrapper : FrooxEngine.ProtoFlux.CoreNodes.FieldDriveBase<UnityEngine.Quaternion>.ProxyWrapper
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ProtoFlux.CoreNodes.FieldDriveBase<floatQ>+Proxy";
}
