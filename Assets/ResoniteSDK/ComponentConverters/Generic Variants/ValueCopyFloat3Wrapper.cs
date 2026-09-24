using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Transform/Drivers/ValueCopy<float3>")]
public class ValueCopyFloat3Wrapper : FrooxEngine.ValueCopyWrapper<UnityEngine.Vector3>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueCopy<float3>";
}
