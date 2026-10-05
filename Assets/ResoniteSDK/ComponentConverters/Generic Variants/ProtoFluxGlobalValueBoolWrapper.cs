using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/GlobalReference<IValue<bool>>")]
public class ProtoFluxGlobalValueBoolWrapper : FrooxEngine.ProtoFlux.GlobalReferenceWrapper<FrooxEngine.IValue<bool>>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ProtoFlux.GlobalReference<[FrooxEngine]FrooxEngine.IValue<bool>>";
}
