using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueGradientDriver<int>")]
public class ValueGradientDriverIntWrapper : FrooxEngine.ValueGradientDriverWrapper<int>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueGradientDriver<int>";
}
