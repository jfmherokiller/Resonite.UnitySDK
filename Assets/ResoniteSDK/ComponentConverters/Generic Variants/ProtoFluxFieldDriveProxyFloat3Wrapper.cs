using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component.
// The type name has been verified against Resonite through ResoniteLink.
[AddComponentMenu("FrooxEngine/ProtoFlux/FieldDriveBase<float3>.Proxy")]
public class ProtoFluxFieldDriveProxyFloat3Wrapper : FrooxEngine.ProtoFlux.CoreNodes.FieldDriveBase<UnityEngine.Vector3>.ProxyWrapper
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ProtoFlux.CoreNodes.FieldDriveBase<float3>+Proxy";
}
