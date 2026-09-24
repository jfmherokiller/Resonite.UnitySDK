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
    public static void ReadClips(IEnumerable<AnimationClip> clips, Func<string, Transform> resolvePath, ToggleState state,
        ICollection<string> unsupported)
    {
        foreach (var clip in clips)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);

                if (curve == null || curve.length == 0)
                    continue;

                var value = curve.keys[0].value;
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
                var overridden = overrides != null ? overrides[clip] : null;
                clips.Add(overridden != null ? overridden : clip);
                break;

            case BlendTree _:
                unsupported.Add("blend trees");
                break;
        }
    }
}
#endif
