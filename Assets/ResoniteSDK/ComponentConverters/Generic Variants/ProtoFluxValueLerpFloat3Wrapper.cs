using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component.
// The type name has been verified against Resonite through ResoniteLink.
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueLerp<float3>")]
public class ProtoFluxValueLerpFloat3Wrapper : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.ValueLerpWrapper<UnityEngine.Vector3>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.ValueLerp<float3>";
}
