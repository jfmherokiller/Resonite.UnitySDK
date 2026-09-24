using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Utility/ValueMultiplexer<bool>")]
public class ValueMultiplexerBoolWrapper : FrooxEngine.ValueMultiplexerWrapper<bool>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ValueMultiplexer<bool>";
}
