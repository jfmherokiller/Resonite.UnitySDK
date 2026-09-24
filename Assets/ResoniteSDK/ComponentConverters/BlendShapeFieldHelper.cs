using System;
using UnityEngine;

/// <summary>
/// Gives access to the blendshape weight fields of converted SkinnedMeshRenderers, so they can be referenced
/// or driven by other Resonite components (visemes, blendshape links, toggles...)
/// </summary>
public static class BlendShapeFieldHelper
{
    /// <summary>
    /// Runs the action with the blendshape weight field once the renderer has been converted.
    /// Does nothing if the blendshape doesn't exist.
    /// </summary>
    public static void RunWithField(IConversionContext context, SkinnedMeshRenderer renderer, string blendShape,
        Action<FrooxEngine.IField<float>> action)
    {
        if (renderer == null || renderer.sharedMesh == null || string.IsNullOrEmpty(blendShape))
            return;

        var index = renderer.sharedMesh.GetBlendShapeIndex(blendShape);

        if (index < 0)
            return;

        RunWithField(context, renderer, index, action);
    }

    public static void RunWithField(IConversionContext context, SkinnedMeshRenderer renderer, int index,
        Action<FrooxEngine.IField<float>> action)
    {
        if (renderer == null || renderer.sharedMesh == null || index < 0 || index >= renderer.sharedMesh.blendShapeCount)
            return;

        context.RunOnConverted(renderer, () =>
        {
            var field = GetField(renderer, index);

            if (field != null)
                action(field);
        });
    }

    /// <summary>
    /// Returns the field for the blendshape weight on an already converted renderer
    /// </summary>
    public static FrooxEngine.IField<float> GetField(SkinnedMeshRenderer renderer, int index)
    {
        var wrapper = renderer.GetComponent<FrooxEngine.SkinnedMeshRendererWrapper>();

        if (wrapper == null)
            return null;

        var weights = wrapper.Data.BlendShapeWeights;

        // The renderer might've not been updated yet. The list elements are reused on update,
        // so we can create them ahead of time.
        while (weights.Count <= index)
            weights.Add(0);

        return weights.GetElement(index).Member;
    }

    /// <summary>
    /// Current weight of the blendshape in Resonite's normalized range
    /// </summary>
    public static float GetNormalizedWeight(SkinnedMeshRenderer renderer, int index, float unityWeight)
    {
        var mesh = renderer.sharedMesh;
        var frameWeight = mesh.GetBlendShapeFrameWeight(index, mesh.GetBlendShapeFrameCount(index) - 1);

        return Mathf.Approximately(frameWeight, 0) ? 0 : unityWeight / frameWeight;
    }
}
