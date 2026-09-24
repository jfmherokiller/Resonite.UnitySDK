using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueMultiDriver<bool>")]
public class ValueMultiDriverBoolWrapper : FrooxEngine.ValueMultiDriverWrapper<bool>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueMultiDriver<bool>";
}
