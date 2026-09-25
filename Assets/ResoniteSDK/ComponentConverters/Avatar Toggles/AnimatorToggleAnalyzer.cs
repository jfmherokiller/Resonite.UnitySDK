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
/// Supported animated properties are object active state, renderer enabled state and blendshape weights.
/// Float parameters (e.g. radial puppets) can be sampled with <see cref="SampleFloat"/>, which also handles 1D blend trees
/// and motion time.
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
                else
                    unsupported.Add($"{binding.type.Name}.{binding.propertyName}");
            }

            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
                unsupported.Add("material/object swaps");
        }
    }

    /// <summary>
    /// Reads what the controllers do when a float parameter has the given value (e.g. a radial puppet). In addition to
    /// transitions (e.g. Greater / Less conditions), this includes states whose motion depends on the parameter directly:
    /// - 1D blend trees blended by the parameter, interpolated between their children like Unity does
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
                    if (animatorState.motion is BlendTree tree && tree.blendParameter == parameter)
                        SampleBlendTree(tree, value, overrides, resolvePath, state, unsupported);
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

    static void SampleBlendTree(BlendTree tree, float value, AnimatorOverrideController overrides, Func<string, Transform> resolvePath,
        ToggleState state, ICollection<string> unsupported)
    {
        if (tree.blendType != BlendTreeType.Simple1D)
        {
            unsupported.Add("2D/direct blend trees");
            return;
        }

        var children = tree.children.Where(c => c.motion != null).OrderBy(c => c.threshold).ToList();

        if (children.Count == 0)
            return;

        // Find the two children around the value
        var upper = children.FindIndex(c => c.threshold >= value);
        var a = upper <= 0 ? children[0] : children[upper - 1];
        var b = upper < 0 ? children[children.Count - 1] : children[upper];
        var t = Mathf.Approximately(a.threshold, b.threshold) ? 0 : Mathf.Clamp01((value - a.threshold) / (b.threshold - a.threshold));

        ToggleState Read(ChildMotion child)
        {
            var childState = new ToggleState();

            if (child.motion is AnimationClip clip)
                ReadClips(new[] { Override(clip, overrides) }, resolvePath, childState, unsupported);
            else
                unsupported.Add("nested blend trees");

            return childState;
        }

        var stateA = Read(a);
        var stateB = t > 0 ? Read(b) : stateA;

        foreach (var key in stateA.Keys.Concat(stateB.Keys).Distinct().ToList())
        {
            if (stateA.Floats.ContainsKey(key) || stateB.Floats.ContainsKey(key))
            {
                var target = stateA.Floats.TryGetValue(key, out var fa) ? fa.target : stateB.Floats[key].target;
                var valueA = stateA.Floats.TryGetValue(key, out fa) ? fa.value : target.RestValue;
                var valueB = stateB.Floats.TryGetValue(key, out var fb) ? fb.value : target.RestValue;

                state.Set(target, Mathf.Lerp(valueA, valueB, t));
            }
            else
            {
                // On/off properties switch at the midpoint
                var source = t < 0.5f ? stateA : stateB;
                var target = stateA.Bools.TryGetValue(key, out var ba) ? ba.target : stateB.Bools[key].target;

                state.Set(target, source.Bools.TryGetValue(key, out var chosen) ? chosen.value : target.RestValue);
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
