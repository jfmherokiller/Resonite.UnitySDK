using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueWrite<FrooxEngineContext, bool>")]
public class ProtoFluxValueWriteBoolWrapper : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueWriteWrapper<FrooxEngine.ProtoFlux.FrooxEngineContext, bool>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueWrite<[FrooxEngine]FrooxEngine.ProtoFlux.FrooxEngineContext,bool>";
}
