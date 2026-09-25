using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueGradientDriver<bool>")]
public class ValueGradientDriverBoolWrapper : FrooxEngine.ValueGradientDriverWrapper<bool>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueGradientDriver<bool>";
}
