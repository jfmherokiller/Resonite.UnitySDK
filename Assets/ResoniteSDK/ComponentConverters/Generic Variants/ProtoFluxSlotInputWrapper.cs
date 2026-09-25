using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component.
// The type name has been verified against Resonite through ResoniteLink.
[AddComponentMenu("FrooxEngine/ProtoFlux/RefObjectInput<Slot>")]
public class ProtoFluxSlotInputWrapper : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.RefObjectInputWrapper<FrooxEngine.Slot>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.RefObjectInput<[FrooxEngine]FrooxEngine.Slot>";
}
