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
/// The values a toggle (or one option of a selector) sets
/// </summary>
public class ToggleState
{
    public readonly Dictionary<string, (ToggleTarget<bool> target, bool value)> Bools = new Dictionary<string, (ToggleTarget<bool>, bool)>();
    public readonly Dictionary<string, (ToggleTarget<float> target, float value)> Floats = new Dictionary<string, (ToggleTarget<float>, float)>();

    public bool IsEmpty => Bools.Count == 0 && Floats.Count == 0;

    public void Set(ToggleTarget<bool> target, bool value) => Bools[target.Key] = (target, value);
    public void Set(ToggleTarget<float> target, float value) => Floats[target.Key] = (target, value);

    public IEnumerable<string> Keys => Bools.Keys.Concat(Floats.Keys);

    public void Remove(string key)
    {
        Bools.Remove(key);
        Floats.Remove(key);
    }
}
