using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component.
// The type name has been verified against Resonite through ResoniteLink.
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueInput<float3>")]
public class ProtoFluxValueInputFloat3Wrapper : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInputWrapper<UnityEngine.Vector3>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<float3>";
}
