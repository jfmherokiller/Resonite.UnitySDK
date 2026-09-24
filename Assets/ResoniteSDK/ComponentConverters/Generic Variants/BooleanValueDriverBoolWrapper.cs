using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/BooleanValueDriver<bool>")]
public class BooleanValueDriverBoolWrapper : FrooxEngine.BooleanValueDriverWrapper<bool>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.BooleanValueDriver<bool>";
}
