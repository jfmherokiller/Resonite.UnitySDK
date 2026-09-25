using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueMultiDriver<float>")]
public class ValueMultiDriverFloatWrapper : FrooxEngine.ValueMultiDriverWrapper<float>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueMultiDriver<float>";
}
