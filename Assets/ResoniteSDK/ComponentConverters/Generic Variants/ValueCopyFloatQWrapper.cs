using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueCopy<floatQ>")]
public class ValueCopyFloatQWrapper : FrooxEngine.ValueCopyWrapper<UnityEngine.Quaternion>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueCopy<floatQ>";
}
