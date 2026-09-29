using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Assets/Utility/AssetMultiplexer<Material>")]
public class AssetMultiplexerMaterialWrapper : FrooxEngine.AssetMultiplexerWrapper<FrooxEngine.Material>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.AssetMultiplexer<[FrooxEngine]FrooxEngine.Material>";
}
