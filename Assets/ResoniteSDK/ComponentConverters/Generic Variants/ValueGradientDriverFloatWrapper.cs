using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueGradientDriver<float>")]
public class ValueGradientDriverFloatWrapper : FrooxEngine.ValueGradientDriverWrapper<float>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueGradientDriver<float>";
}
