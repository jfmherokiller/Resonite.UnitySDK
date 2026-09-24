using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueCopy<float>")]
public class ValueCopyFloatWrapper : FrooxEngine.ValueCopyWrapper<float>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueCopy<float>";
}
