using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Common UI/Button Interactions/ButtonValueSet<int>")]
public class ButtonValueSetIntWrapper : FrooxEngine.ButtonValueSetWrapper<int>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ButtonValueSet<int>";
}
