using UnityEngine;

// Unity can't add generic components, so this is a concrete variant of the generic Resonite component
[AddComponentMenu("FrooxEngine/Common UI/Button Interactions/ButtonValueCycle<int>")]
public class ButtonValueCycleIntWrapper : FrooxEngine.ButtonValueCycleWrapper<int>
{
    public override string TypeName => "[FrooxEngine]FrooxEngine.ButtonValueCycle<int>";
}
