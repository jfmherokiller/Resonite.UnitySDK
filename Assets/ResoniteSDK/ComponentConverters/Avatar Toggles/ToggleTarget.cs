using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A property that an avatar toggle controls, e.g. the active state of an object or a blendshape weight.
/// The field is resolved lazily, since the component that owns it might not have been converted yet.
/// </summary>
public abstract class ToggleTarget<T>
{
    /// <summary>
    /// Identifies the controlled property, so the same property isn't driven by multiple toggles
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// Value of the property in the scene, used when the toggle doesn't set it
    /// </summary>
    public abstract T RestValue { get; }

    public abstract void ResolveField(IConversionContext context, Action<FrooxEngine.IField<T>> onResolved);
}

public class ObjectActiveTarget : ToggleTarget<bool>
{
    public readonly Transform Object;

    public ObjectActiveTarget(Transform obj) => Object = obj;

    public override string Key => $"active:{Object.GetInstanceID()}";
    public override bool RestValue => Object.gameObject.activeSelf;

    public override void ResolveField(IConversionContext context, Action<FrooxEngine.IField<bool>> onResolved) =>
        onResolved(new SlotActiveField(Object));
}

public class RendererEnabledTarget : ToggleTarget<bool>
{
    public readonly Renderer Renderer;

    public RendererEnabledTarget(Renderer renderer) => Renderer = renderer;

    public override string Key => $"renderer:{Renderer.GetInstanceID()}";
    public override bool RestValue => Renderer.enabled;

    public override void ResolveField(IConversionContext context, Action<FrooxEngine.IField<bool>> onResolved)
    {
        context.RunOnConverted(Renderer, () =>
        {
            FrooxEngine.IField<bool> field = null;

            var skinned = Renderer.GetComponent<FrooxEngine.SkinnedMeshRendererWrapper>();
            var mesh = Renderer.GetComponent<FrooxEngine.MeshRendererWrapper>();

            if (Renderer is SkinnedMeshRenderer && skinned != null)
                field = skinned.Data.Enabled_Element.Member;
            else if (mesh != null)
                field = mesh.Data.Enabled_Element.Member;

            if (field != null)
                onResolved(field);
        });
    }
}

/// <summary>
/// The Enabled state of a converted DynamicBoneChain (PhysBone / legacy Dynamic Bone), identified by its
/// source component (the VRCPhysBone or DynamicBone, whose converter owns the DynamicBoneChainWrapper).
/// </summary>
public class DynamicBoneChainEnabledTarget : ToggleTarget<bool>
{
    public readonly Component Source;

    public DynamicBoneChainEnabledTarget(Component source) => Source = source;

    public override string Key => $"dynamicbonechain:{Source.GetInstanceID()}";
    public override bool RestValue => true;

    public override void ResolveField(IConversionContext context, Action<FrooxEngine.IField<bool>> onResolved)
    {
        context.RunOnConverted(Source, () =>
        {
            var wrapper = Source.GetComponent<FrooxEngine.DynamicBoneChainWrapper>();

            if (wrapper != null)
                onResolved(wrapper.Data.Enabled_Element.Member);
        });
    }
}

public class BlendShapeTarget : ToggleTarget<float>
{
    public readonly SkinnedMeshRenderer Renderer;
    public readonly int Index;

    public BlendShapeTarget(SkinnedMeshRenderer renderer, int index)
    {
        Renderer = renderer;
        Index = index;
    }

    public override string Key => $"blendshape:{Renderer.GetInstanceID()}:{Index}";

    public override float RestValue =>
        BlendShapeFieldHelper.GetNormalizedWeight(Renderer.sharedMesh, Index, Renderer.GetBlendShapeWeight(Index));

    /// <summary>
    /// Converts Unity's blendshape weight (usually 0...100) into Resonite's normalized weight
    /// </summary>
    public float Normalize(float unityWeight) => BlendShapeFieldHelper.GetNormalizedWeight(Renderer.sharedMesh, Index, unityWeight);

    public override void ResolveField(IConversionContext context, Action<FrooxEngine.IField<float>> onResolved) =>
        BlendShapeFieldHelper.RunWithField(context, Renderer, Index, onResolved);
}

/// <summary>
/// One material slot of a renderer. Toggles swap the slot's material (material swaps), or a variant of it with some
/// properties changed (material property animations, see <see cref="MaterialVariants"/>).
/// </summary>
public class MaterialSlotTarget
{
    public readonly Renderer Renderer;
    public readonly int Slot;

    public MaterialSlotTarget(Renderer renderer, int slot)
    {
        Renderer = renderer;
        Slot = slot;
    }

    public string Key => $"material:{Renderer.GetInstanceID()}:{Slot}";

    public Material RestValue => Slot < Renderer.sharedMaterials.Length ? Renderer.sharedMaterials[Slot] : null;

    /// <summary>
    /// Resolves the slot's material reference on the converted renderer
    /// </summary>
    public void ResolveSlot(IConversionContext context, Action<FrooxEngine.AssetRef<FrooxEngine.Material>> onResolved)
    {
        context.RunOnConverted(Renderer, () =>
        {
            FrooxEngine.MeshRenderer data = null;

            if (Renderer is SkinnedMeshRenderer)
                data = Renderer.GetComponent<FrooxEngine.SkinnedMeshRendererWrapper>()?.Data;
            else
                data = Renderer.GetComponent<FrooxEngine.MeshRendererWrapper>()?.Data;

            if (data != null && Slot < data.Materials.Count)
                onResolved(data.Materials.GetElement(Slot).Member);
        });
    }
}

/// <summary>
/// What a toggle state does to a material slot: an optional swapped material, with optional property overrides
/// (shader property name as animated, e.g. "_Color.r" or "_EmissionStrength")
/// </summary>
public class MaterialSlotState
{
    public Material Material;
    public readonly Dictionary<string, float> Properties = new Dictionary<string, float>();

    /// <summary>
    /// The Unity material this state shows in the slot
    /// </summary>
    public Material Resolve(MaterialSlotTarget target) => MaterialVariants.Get(Material != null ? Material : target.RestValue, Properties);
}

/// <summary>
/// The values a toggle (or one option of a selector) sets
/// </summary>
public class ToggleState
{
    public readonly Dictionary<string, (ToggleTarget<bool> target, bool value)> Bools = new Dictionary<string, (ToggleTarget<bool>, bool)>();
    public readonly Dictionary<string, (ToggleTarget<float> target, float value)> Floats = new Dictionary<string, (ToggleTarget<float>, float)>();
    public readonly Dictionary<string, (MaterialSlotTarget target, MaterialSlotState value)> Materials =
        new Dictionary<string, (MaterialSlotTarget, MaterialSlotState)>();

    public bool IsEmpty => Bools.Count == 0 && Floats.Count == 0 && Materials.Count == 0;

    public void Set(ToggleTarget<bool> target, bool value) => Bools[target.Key] = (target, value);
    public void Set(ToggleTarget<float> target, float value) => Floats[target.Key] = (target, value);

    public void SetMaterial(MaterialSlotTarget target, Material material) => GetMaterialState(target).Material = material;

    public void SetMaterialProperty(MaterialSlotTarget target, string property, float value) =>
        GetMaterialState(target).Properties[property] = value;

    public void Set(MaterialSlotTarget target, MaterialSlotState value) => Materials[target.Key] = (target, value);

    /// <summary>
    /// The Unity material this state shows in the slot, or its rest material if the state doesn't change it
    /// </summary>
    public Material ResolveMaterial(MaterialSlotTarget target) =>
        Materials.TryGetValue(target.Key, out var entry) ? entry.value.Resolve(target) : target.RestValue;

    MaterialSlotState GetMaterialState(MaterialSlotTarget target)
    {
        if (!Materials.TryGetValue(target.Key, out var entry))
            Materials[target.Key] = entry = (target, new MaterialSlotState());

        return entry.value;
    }

    public IEnumerable<string> Keys => Bools.Keys.Concat(Floats.Keys).Concat(Materials.Keys);

    public void Remove(string key)
    {
        Bools.Remove(key);
        Floats.Remove(key);
        Materials.Remove(key);
    }
}
