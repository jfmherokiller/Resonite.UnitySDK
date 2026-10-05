using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueSource<int>")]
public class ProtoFluxValueSourceIntWrapper : FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueSourceWrapper<int>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueSource<int>";
}
