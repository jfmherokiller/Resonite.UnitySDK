using FrooxEngine;
using UnityEngine;

public class PoiyomiAssetCache
{
    public UnityEngine.Texture2D ShadowRampTexture;
#if UNITY_EDITOR
    public ColorSwizzler MetallicSwizzler = new();
#endif
    public PoiyomiXiexeTinter ShadowRampTinter = new();
}
