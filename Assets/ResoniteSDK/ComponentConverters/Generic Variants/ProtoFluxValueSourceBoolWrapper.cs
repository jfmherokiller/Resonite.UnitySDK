using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/ProtoFlux/ValueSource<bool>")]
public class ProtoFluxValueSourceBoolWrapper : FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueSourceWrapper<bool>
{
    public override string TypeName => "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueSource<bool>";
}
