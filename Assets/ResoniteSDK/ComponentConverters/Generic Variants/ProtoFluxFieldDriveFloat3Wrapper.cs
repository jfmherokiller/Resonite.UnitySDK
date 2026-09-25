using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component.
// The type name has been verified against Resonite through ResoniteLink.
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueFieldDrive<float3>")]
public class ProtoFluxFieldDriveFloat3Wrapper : FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDriveWrapper<UnityEngine.Vector3>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDrive<float3>";
}
