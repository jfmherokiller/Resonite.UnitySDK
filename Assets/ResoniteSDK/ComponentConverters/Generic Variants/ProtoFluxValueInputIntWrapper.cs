using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueInput<int>")]
public class ProtoFluxValueInputIntWrapper : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInputWrapper<int>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<int>";
}
