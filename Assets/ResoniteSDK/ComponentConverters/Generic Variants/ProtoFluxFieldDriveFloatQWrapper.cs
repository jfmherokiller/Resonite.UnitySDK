using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component.
// The type name has been verified against Resonite through ResoniteLink.
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueFieldDrive<floatQ>")]
public class ProtoFluxFieldDriveFloatQWrapper : FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDriveWrapper<UnityEngine.Quaternion>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDrive<floatQ>";
}
