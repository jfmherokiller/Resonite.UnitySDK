#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Finds out what an animator controller does for a given parameter value, e.g. which objects a menu toggle turns on.
///
/// Every transition that has conditions on the parameter, all of which are satisfied by the value, is followed to its
/// destination state. The animation clips of those states are then read into a <see cref="ToggleState"/>.
/// Supported animated properties are object active state, renderer enabled state, blendshape weights, material
/// properties (as material variants, see <see cref="MaterialVariants"/>) and material swaps.
/// Float parameters (e.g. radial and axis puppets) can be sampled with <see cref="SampleFloat"/>, which also handles 1D/2D
/// blend trees and motion time.
/// </summary>
public static class AnimatorToggleAnalyzer
{
    /// <summary>
    /// Collects the clips that play when the parameter has the given value
    /// </summary>
    public static List<AnimationClip> FindClips(RuntimeAnimatorController runtimeController, string parameter, float value,
        ICollection<string> unsupported)
    {
        var clips = new List<AnimationClip>();
        var controller = runtimeController as AnimatorController;
        var overrides = runtimeController as AnimatorOverrideController;

        if (overrides != null)
            controller = overrides.runtimeAnimatorController as AnimatorController;

        if (controller == null || string.IsNullOrEmpty(parameter))
            return clips;

        var controllerParameter = controller.parameters.FirstOrDefault(p => p.name == parameter);

        if (controllerParameter == null)
            return clips;

        foreach (var layer in controller.layers)
            foreach (var state in FindStates(layer.stateMachine, parameter, controllerParameter.type, value))
                CollectClips(state.motion, overrides, clips, unsupported);

        return clips.Distinct().ToList();
    }

    /// <summary>
    /// Collects the clips that play in any of the controllers when the parameter has the given value
    /// </summary>
    public static List<AnimationClip> FindClips(IEnumerable<RuntimeAnimatorController> controllers, string parameter, float value,
        ICollection<string> unsupported) =>
        controllers.SelectMany(c => FindClips(c, parameter, value, unsupported)).Distinct().ToList();

    /// <summary>
    /// Reads the values the clips set (at their first keyframe) into the state. Paths are relative to the root.
    /// </summary>
    public static void ReadClips(IEnumerable<AnimationClip> clips, Transform root, ToggleState state, ICollection<string> unsupported) =>
        ReadClips(clips, path => string.IsNullOrEmpty(path) ? root : root.Find(path), state, unsupported);

    /// <summary>
    /// Reads the values the clips set (at their first keyframe) into the state.
    /// </summary>
    /// <param name="resolvePath">Resolves the animated path to a transform, or null if it doesn't exist</param>
    /// <param name="normalizedTime">Evaluates the clips at this point (0...1 of their length) instead of the first keyframe</param>
    public static void ReadClips(IEnumerable<AnimationClip> clips, Func<string, Transform> resolvePath, ToggleState state,
        ICollection<string> unsupported, float? normalizedTime = null)
    {
        foreach (var clip in clips)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);

                if (curve == null || curve.length == 0)
                    continue;

                var value = normalizedTime.HasValue ? curve.Evaluate(normalizedTime.Value * clip.length) : curve.keys[0].value;
                var target = resolvePath(binding.path);

                if (target == null)
                    continue;

                if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                    state.Set(new ObjectActiveTarget(target), value > 0.5f);
                else if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape."))
                {
                    var renderer = target.GetComponent<SkinnedMeshRenderer>();
                    var index = renderer != null && renderer.sharedMesh != null
                        ? renderer.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring("blendShape.".Length)) : -1;

                    if (index >= 0)
                    {
                        var blendShape = new BlendShapeTarget(renderer, index);
                        state.Set(blendShape, blendShape.Normalize(value));
                    }
                }
                else if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled")
                {
                    if (target.GetComponent(binding.type) is Renderer renderer)
                        state.Set(new RendererEnabledTarget(renderer), value > 0.5f);
                }
                else if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith(MATERIAL_PREFIX))
                {
                    // Material property curves apply to every material of the renderer that has the property
                    var property = binding.propertyName.Substring(MATERIAL_PREFIX.Length);
                    var applied = false;

                    if (target.GetComponent(binding.type) is Renderer renderer)
                        for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                            if (MaterialVariants.Has(renderer.sharedMaterials[slot], property))
                            {
                                state.SetMaterialProperty(new MaterialSlotTarget(renderer, slot), property, value);
                                applied = true;
                            }

                    if (!applied)
                        unsupported.Add($"material property {property}");
                }
                else
                    unsupported.Add($"{binding.type.Name}.{binding.propertyName}");
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var target = resolvePath(binding.path);
                var slot = MaterialSlotIndex(binding.propertyName);

                if (keys == null || keys.Length == 0 || target == null)
                    continue;

                if (slot >= 0 && typeof(Renderer).IsAssignableFrom(binding.type) && target.GetComponent(binding.type) is Renderer renderer
                    && slot < renderer.sharedMaterials.Length)
                {
                    // The last key at or before the sampled time, like Unity's constant object curves
                    var time = normalizedTime.HasValue ? normalizedTime.Value * clip.length : keys[0].time;
                    var key = keys.Where(k => k.time <= time).DefaultIfEmpty(keys[0]).Last();

                    if (key.value is Material material)
                        state.SetMaterial(new MaterialSlotTarget(renderer, slot), material);
                }
                else
                    unsupported.Add($"{binding.type.Name}.{binding.propertyName} (object swap)");
            }
        }
    }

    const string MATERIAL_PREFIX = "material.";
    const string MATERIAL_SLOT_PREFIX = "m_Materials.Array.data[";

    /// <summary>
    /// Material slot of a material swap binding ("m_Materials.Array.data[1]"), or -1
    /// </summary>
    static int MaterialSlotIndex(string propertyName) =>
        propertyName.StartsWith(MATERIAL_SLOT_PREFIX) && propertyName.EndsWith("]")
            && int.TryParse(propertyName.Substring(MATERIAL_SLOT_PREFIX.Length, propertyName.Length - MATERIAL_SLOT_PREFIX.Length - 1), out var slot)
            ? slot : -1;

    /// <summary>
    /// Reads what the controllers do when a float parameter has the given value (e.g. a radial puppet). In addition to
    /// transitions (e.g. Greater / Less conditions), this includes states whose motion depends on the parameter directly:
    /// - 1D blend trees blended by the parameter, interpolated between their children like Unity does
    /// - 2D blend trees using the parameter on either axis (other axis at 0, approximated weights)
    /// - Motion time driven by the parameter, evaluating the clip at that point
    /// </summary>
    public static ToggleState SampleFloat(IEnumerable<RuntimeAnimatorController> controllers, string parameter, float value,
        Func<string, Transform> resolvePath, ICollection<string> unsupported)
    {
        var state = new ToggleState();

        foreach (var runtimeController in controllers)
        {
            var controller = runtimeController as AnimatorController;
            var overrides = runtimeController as AnimatorOverrideController;

            if (overrides != null)
                controller = overrides.runtimeAnimatorController as AnimatorController;

            var controllerParameter = controller != null ? controller.parameters.FirstOrDefault(p => p.name == parameter) : null;

            if (controllerParameter == null)
                continue;

            foreach (var layer in controller.layers)
            {
                var reached = new HashSet<AnimatorState>(FindStates(layer.stateMachine, parameter, controllerParameter.type, value));

                foreach (var animatorState in AllStates(layer.stateMachine))
                {
                    if (animatorState.motion is BlendTree tree && (tree.blendParameter == parameter
                        || (tree.blendType != BlendTreeType.Simple1D && tree.blendType != BlendTreeType.Direct && tree.blendParameterY == parameter)))
                        SampleBlendTree(tree, parameter, value, overrides, resolvePath, state, unsupported);
                    else if (animatorState.timeParameterActive && animatorState.timeParameter == parameter
                        && animatorState.motion is AnimationClip timeClip)
                        ReadClips(new[] { Override(timeClip, overrides) }, resolvePath, state, unsupported, Mathf.Clamp01(value));
                    else if (reached.Contains(animatorState))
                    {
                        var clips = new List<AnimationClip>();
                        CollectClips(animatorState.motion, overrides, clips, unsupported);
                        ReadClips(clips, resolvePath, state, unsupported);
                    }
                }
            }
        }

        return state;
    }

    static void SampleBlendTree(BlendTree tree, string parameter, float value, AnimatorOverrideController overrides,
        Func<string, Transform> resolvePath, ToggleState state, ICollection<string> unsupported)
    {
        var children = tree.children.Where(c => c.motion != null).ToList();

        if (children.Count == 0)
            return;

        List<(ChildMotion child, float weight)> weights;

        if (tree.blendType == BlendTreeType.Simple1D)
        {
            var sorted = children.OrderBy(c => c.threshold).ToList();

            // The two children around the value
            var upper = sorted.FindIndex(c => c.threshold >= value);
            var a = upper <= 0 ? sorted[0] : sorted[upper - 1];
            var b = upper < 0 ? sorted[sorted.Count - 1] : sorted[upper];
            var t = Mathf.Approximately(a.threshold, b.threshold) ? 0 : Mathf.Clamp01((value - a.threshold) / (b.threshold - a.threshold));

            weights = new List<(ChildMotion, float)> { (a, 1 - t), (b, t) };
        }
        else if (tree.blendType != BlendTreeType.Direct)
        {
            // 2D: the other axis is at rest (0), like an axis puppet moved along one axis only
            var point = tree.blendParameter == parameter ? new Vector2(value, 0) : new Vector2(0, value);
            weights = Weights2D(children, point);
        }
        else
        {
            unsupported.Add("direct blend trees");
            return;
        }

        var states = weights.Where(w => w.weight > 0.0001f).Select(w =>
        {
            var childState = new ToggleState();

            if (w.child.motion is AnimationClip clip)
                ReadClips(new[] { Override(clip, overrides) }, resolvePath, childState, unsupported);
            else
                unsupported.Add("nested blend trees");

            return (childState, w.weight);
        }).ToList();

        Blend(states, state);
    }

    /// <summary>
    /// Approximates Unity's 2D blending with inverse distance weights (exact at the children's positions)
    /// </summary>
    static List<(ChildMotion child, float weight)> Weights2D(List<ChildMotion> children, Vector2 point)
    {
        var exact = children.FindIndex(c => (c.position - point).sqrMagnitude < 0.000001f);

        if (exact >= 0)
            return new List<(ChildMotion, float)> { (children[exact], 1) };

        var inverse = children.Select(c => (c, 1 / (c.position - point).sqrMagnitude)).ToList();
        var total = inverse.Sum(w => w.Item2);

        return inverse.Select(w => (w.c, w.Item2 / total)).ToList();
    }

    /// <summary>
    /// Weighted blend of the states into the target. Values are interpolated, on/off properties and material swaps
    /// come from the state with the highest weight. Properties missing from a state use their rest value.
    /// </summary>
    static void Blend(List<(ToggleState state, float weight)> states, ToggleState target)
    {
        if (states.Count == 0)
            return;

        var total = states.Sum(s => s.weight);
        var dominant = states.OrderByDescending(s => s.weight).First().state;

        foreach (var key in states.SelectMany(s => s.state.Keys).Distinct().ToList())
        {
            var materials = states.Where(s => s.state.Materials.ContainsKey(key)).ToList();
            var floats = states.Where(s => s.state.Floats.ContainsKey(key)).ToList();

            if (materials.Count > 0)
            {
                var slot = materials[0].state.Materials[key].target;
                var rest = slot.RestValue;
                var blended = new MaterialSlotState
                {
                    Material = dominant.Materials.TryGetValue(key, out var d) ? d.value.Material : null,
                };

                MaterialSlotState Of(ToggleState s) => s.Materials.TryGetValue(key, out var m) ? m.value : null;

                foreach (var property in materials.SelectMany(s => s.state.Materials[key].value.Properties.Keys).Distinct())
                    blended.Properties[property] = states.Sum(s =>
                    {
                        var m = Of(s.state);
                        return s.weight * (m != null && m.Properties.TryGetValue(property, out var v) ? v : MaterialVariants.GetValue(rest, property));
                    }) / total;

                target.Set(slot, blended);
            }
            else if (floats.Count > 0)
            {
                var floatTarget = floats[0].state.Floats[key].target;
                target.Set(floatTarget, states.Sum(s => s.weight *
                    (s.state.Floats.TryGetValue(key, out var f) ? f.value : floatTarget.RestValue)) / total);
            }
            else
            {
                var boolTarget = states.First(s => s.state.Bools.ContainsKey(key)).state.Bools[key].target;
                target.Set(boolTarget, dominant.Bools.TryGetValue(key, out var b) ? b.value : boolTarget.RestValue);
            }
        }
    }

    static AnimationClip Override(AnimationClip clip, AnimatorOverrideController overrides)
    {
        var overridden = overrides != null ? overrides[clip] : null;
        return overridden != null ? overridden : clip;
    }

    static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
    {
        foreach (var child in machine.states)
            yield return child.state;

        foreach (var child in machine.stateMachines)
            foreach (var nested in AllStates(child.stateMachine))
                yield return nested;
    }

    static IEnumerable<AnimatorState> FindStates(AnimatorStateMachine stateMachine, string parameter, AnimatorControllerParameterType type,
        float value)
    {
        var states = new HashSet<AnimatorState>();

        void Follow(AnimatorTransitionBase transition)
        {
            var conditions = transition.conditions.Where(c => c.parameter == parameter).ToList();

            if (conditions.Count == 0 || !conditions.All(c => IsSatisfied(c, type, value)))
                return;

            if (transition.destinationState != null)
                states.Add(transition.destinationState);
            else if (transition.destinationStateMachine != null && transition.destinationStateMachine.defaultState != null)
                states.Add(transition.destinationStateMachine.defaultState);
        }

        void Visit(AnimatorStateMachine machine)
        {
            foreach (var transition in machine.anyStateTransitions)
                Follow(transition);

            foreach (var transition in machine.entryTransitions)
                Follow(transition);

            foreach (var child in machine.states)
                foreach (var transition in child.state.transitions)
                    Follow(transition);

            foreach (var child in machine.stateMachines)
            {
                foreach (var transition in machine.GetStateMachineTransitions(child.stateMachine))
                    Follow(transition);

                Visit(child.stateMachine);
            }
        }

        Visit(stateMachine);

        return states;
    }

    static bool IsSatisfied(AnimatorCondition condition, AnimatorControllerParameterType type, float value)
    {
        switch (condition.mode)
        {
            case AnimatorConditionMode.If: return value != 0;
            case AnimatorConditionMode.IfNot: return value == 0;
            case AnimatorConditionMode.Greater: return value > condition.threshold;
            case AnimatorConditionMode.Less: return value < condition.threshold;

            case AnimatorConditionMode.Equals:
                return type == AnimatorControllerParameterType.Int
                    ? Mathf.RoundToInt(value) == Mathf.RoundToInt(condition.threshold)
                    : Mathf.Approximately(value, condition.threshold);

            case AnimatorConditionMode.NotEqual:
                return type == AnimatorControllerParameterType.Int
                    ? Mathf.RoundToInt(value) != Mathf.RoundToInt(condition.threshold)
                    : !Mathf.Approximately(value, condition.threshold);
        }

        return false;
    }

    static void CollectClips(Motion motion, AnimatorOverrideController overrides, List<AnimationClip> clips, ICollection<string> unsupported)
    {
        switch (motion)
        {
            case AnimationClip clip:
                clips.Add(Override(clip, overrides));
                break;

            case BlendTree _:
                unsupported.Add("blend trees");
                break;
        }
    }
}
#endif
