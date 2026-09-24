using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Utility/ValueMultiplexer<float>")]
public class ValueMultiplexerFloatWrapper : FrooxEngine.ValueMultiplexerWrapper<float>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueMultiplexer<float>";
}
