using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/GlobalReference<IValue<int>>")]
public class ProtoFluxGlobalValueIntWrapper : FrooxEngine.ProtoFlux.GlobalReferenceWrapper<FrooxEngine.IValue<int>>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ProtoFlux.GlobalReference<[FrooxEngine]FrooxEngine.IValue<int>>";
}
