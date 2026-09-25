using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Common UI/Button Interactions/ButtonValueSet<float>")]
public class ButtonValueSetFloatWrapper : FrooxEngine.ButtonValueSetWrapper<float>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ButtonValueSet<float>";
}
