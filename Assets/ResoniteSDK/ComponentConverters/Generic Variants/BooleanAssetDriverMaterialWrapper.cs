using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Assets/Utility/BooleanAssetDriver<Material>")]
public class BooleanAssetDriverMaterialWrapper : FrooxEngine.BooleanAssetDriverWrapper<FrooxEngine.Material>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.BooleanAssetDriver<[FrooxEngine]FrooxEngine.Material>";
}
