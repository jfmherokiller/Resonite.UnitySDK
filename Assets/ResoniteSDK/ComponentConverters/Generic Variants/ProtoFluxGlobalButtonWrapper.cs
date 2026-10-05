using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/GlobalReference<IButton>")]
public class ProtoFluxGlobalButtonWrapper : FrooxEngine.ProtoFlux.GlobalReferenceWrapper<FrooxEngine.IButton>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ProtoFlux.GlobalReference<[FrooxEngine]FrooxEngine.IButton>";
}
