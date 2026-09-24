using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueMultiDriver<int>")]
public class ValueMultiDriverIntWrapper : FrooxEngine.ValueMultiDriverWrapper<int>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueMultiDriver<int>";
}
