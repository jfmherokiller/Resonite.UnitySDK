using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueInput<bool>")]
public class ProtoFluxValueInputBoolWrapper : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInputWrapper<bool>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<bool>";
}
