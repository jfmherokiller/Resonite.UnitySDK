using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueWrite<FrooxEngineContext, int>")]
public class ProtoFluxValueWriteIntWrapper : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueWriteWrapper<FrooxEngine.ProtoFlux.FrooxEngineContext, int>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueWrite<[FrooxEngine]FrooxEngine.ProtoFlux.FrooxEngineContext,int>";
}
