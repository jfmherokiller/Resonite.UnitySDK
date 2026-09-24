using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/BooleanValueDriver<float>")]
public class BooleanValueDriverFloatWrapper : FrooxEngine.BooleanValueDriverWrapper<float>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.BooleanValueDriver<float>";
}
