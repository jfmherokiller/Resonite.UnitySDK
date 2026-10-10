using System.Collections.Generic;
using System.Linq;
using FrooxEngine.ProtoFlux;
using UnityEngine;

/// <summary>
/// Builds small ProtoFlux graphs out of node components on a host GameObject, for conversions that can't be done
/// with regular components (e.g. blending constraint sources).
///
/// Nodes are regular components, connected by referencing the outputs of other nodes. Nodes don't need visuals to run.
/// Things verified against Resonite through ResoniteLink:
/// - Multi-output nodes (e.g. LocalTransform) keep the IDs assigned to their output elements, so they can be referenced
/// - A field drive is a ValueFieldDrive node plus its FieldDriveBase.Proxy on the same slot, which targets the field.
///   The proxy must be sent in the same batch as the node - otherwise the node creates its own proxy with no target.
///   Both are always added to the host here, so the scene converter sends them together.
///
/// The graph is rebuilt on every update: call <see cref="Begin"/>, create the nodes in the same order, then call
/// <see cref="End"/>. Existing node components are reused in order, so realtime updates don't recreate them.
/// </summary>
public class ProtoFluxGraph
{
    readonly GameObject _host;
    readonly HashSet<Component> _used = new HashSet<Component>();
    readonly Dictionary<System.Type, int> _cursors = new Dictionary<System.Type, int>();

    public ProtoFluxGraph(GameObject host)
    {
        _host = host;
    }

    public void Begin()
    {
        _used.Clear();
        _cursors.Clear();
    }

    /// <summary>
    /// Removes any node components from previous updates that weren't used this time
    /// </summary>
    public void End()
    {
        foreach (var component in _host.GetComponents<ResoniteComponent>())
            if (!_used.Contains(component))
                Object.DestroyImmediate(component);
    }

    ResRef<T> Next<T>() where T : ResoniteObject, FrooxEngine.IWorldElement, new()
    {
        _cursors.TryGetValue(typeof(T), out var index);
        _cursors[typeof(T)] = index + 1;

        var existing = _host.GetComponents<ResoniteComponent>()
            .Where(container => container.Data is T).ToArray();
        var component = index < existing.Length
            ? new ResRef<T>(existing[index])
            : _host.AddResoniteComponent<T>();

        _used.Add(component.Container);

        return component;
    }

    #region INPUTS

    public INodeValueOutput<float> Constant(float value)
    {
        var node = Next<ProtoFluxValueInputFloatWrapper>().Binding;
        node.Value = value;
        return node;
    }

    public INodeValueOutput<Vector3> Constant(Vector3 value)
    {
        var node = Next<ProtoFluxValueInputFloat3Wrapper>().Binding;
        node.Value = value;
        return node;
    }

    public INodeValueOutput<Quaternion> Constant(Quaternion value)
    {
        var node = Next<ProtoFluxValueInputFloatQWrapper>().Binding;
        node.Value = value;
        return node;
    }

    public INodeValueOutput<bool> Constant(bool value)
    {
        var node = Next<ProtoFluxValueInputBoolWrapper>().Binding;
        node.Value = value;
        return node;
    }

    public INodeValueOutput<int> Constant(int value)
    {
        var node = Next<ProtoFluxValueInputIntWrapper>().Binding;
        node.Value = value;
        return node;
    }

    public INodeObjectOutput<FrooxEngine.Slot> Slot(Transform transform)
    {
        var node = Next<ProtoFluxSlotInputWrapper>().Binding;
        node.Target = transform.GetSlot();
        return node;
    }

    #endregion

    #region TRANSFORMS

    public (INodeValueOutput<Vector3> position, INodeValueOutput<Quaternion> rotation) LocalTransform(Transform transform)
    {
        var node = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.LocalTransform>().Binding;
        node.Instance = Slot(transform);
        return (node.LocalPosition, node.LocalRotation);
    }

    public INodeValueOutput<Vector3> LocalScale(Transform transform)
    {
        var node = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.LocalTransform>().Binding;
        node.Instance = Slot(transform);
        return node.LocalScale;
    }

    public INodeValueOutput<Vector3> GlobalScale(Transform transform)
    {
        var node = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.GlobalTransform>().Binding;
        node.Instance = Slot(transform);
        return node.GlobalScale;
    }

    public INodeValueOutput<Vector3> LocalScaleToGlobal(Transform transform, INodeValueOutput<Vector3> scale)
    {
        var node = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.LocalScaleToGlobal>().Binding;
        node.Instance = Slot(transform);
        node.LocalScale = scale;
        return node;
    }

    public INodeValueOutput<Vector3> GlobalScaleToLocal(Transform transform, INodeValueOutput<Vector3> scale)
    {
        var node = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.GlobalScaleToLocal>().Binding;
        node.Instance = Slot(transform);
        node.GlobalScale = scale;
        return node;
    }

    #endregion

    #region MATH

    public INodeValueOutput<Quaternion> Slerp(INodeValueOutput<Quaternion> from, INodeValueOutput<Quaternion> to, INodeValueOutput<float> lerp)
    {
        var node = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.Quaternions.Slerp_floatQ>().Binding;
        node.From = from;
        node.To = to;
        node.Lerp = lerp;
        return node;
    }

    public INodeValueOutput<Vector3> Lerp(INodeValueOutput<Vector3> from, INodeValueOutput<Vector3> to, INodeValueOutput<float> lerp)
    {
        var node = Next<ProtoFluxValueLerpFloat3Wrapper>().Binding;
        node.From = from;
        node.To = to;
        node.Lerp = lerp;
        return node;
    }

    /// <summary>
    /// Combines the axes of two vectors - the axes where <paramref name="useA"/> is true come from <paramref name="a"/>
    /// </summary>
    public INodeValueOutput<Vector3> SelectAxes(INodeValueOutput<Vector3> a, INodeValueOutput<Vector3> b, bool x, bool y, bool z)
    {
        var unpackA = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators.Unpack_Float3>().Binding;
        var unpackB = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators.Unpack_Float3>().Binding;
        var pack = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators.Pack_Float3>().Binding;

        unpackA.V = a;
        unpackB.V = b;

        pack.X = x ? unpackA.X : unpackB.X;
        pack.Y = y ? unpackA.Y : unpackB.Y;
        pack.Z = z ? unpackA.Z : unpackB.Z;

        return pack;
    }

    /// <summary>
    /// Weighted average of the values (by chaining lerps), blended from the rest value by the strength
    /// </summary>
    public INodeValueOutput<Vector3> WeightedBlend(Vector3 rest, IReadOnlyList<(INodeValueOutput<Vector3> value, float weight)> values,
        float strength) => WeightedBlend(Constant(rest), values, strength);

    public INodeValueOutput<Vector3> WeightedBlend(INodeValueOutput<Vector3> rest,
        IReadOnlyList<(INodeValueOutput<Vector3> value, float weight)> values, float strength)
    {
        INodeValueOutput<Vector3> result = null;
        var total = 0f;

        foreach (var (value, weight) in values)
        {
            result = result == null ? value : Lerp(result, value, Constant(weight / (total + weight)));
            total += weight;
        }

        return strength < 0.999f ? Lerp(rest, result, Constant(Mathf.Clamp01(strength))) : result;
    }

    /// <summary>
    /// Weighted average of the rotations (by chaining slerps), blended from the rest rotation by the strength
    /// </summary>
    public INodeValueOutput<Quaternion> WeightedBlend(Quaternion rest, IReadOnlyList<(INodeValueOutput<Quaternion> value, float weight)> values,
        float strength)
    {
        INodeValueOutput<Quaternion> result = null;
        var total = 0f;

        foreach (var (value, weight) in values)
        {
            result = result == null ? value : Slerp(result, value, Constant(weight / (total + weight)));
            total += weight;
        }

        return strength < 0.999f ? Slerp(Constant(rest), result, Constant(Mathf.Clamp01(strength))) : result;
    }

    #endregion

    #region ACTIONS

    /// <summary>
    /// Writes the value into the field when the returned operation is triggered
    /// </summary>
    public ISyncNodeOperation Write(FrooxEngine.IField<bool> field, bool value) =>
        Write<bool, ProtoFluxGlobalValueBoolWrapper, ProtoFluxValueSourceBoolWrapper, ProtoFluxValueWriteBoolWrapper>(field, Constant(value));

    public ISyncNodeOperation Write(FrooxEngine.IField<int> field, int value) =>
        Write<int, ProtoFluxGlobalValueIntWrapper, ProtoFluxValueSourceIntWrapper, ProtoFluxValueWriteIntWrapper>(field, Constant(value));

    // The field is referenced through a global + ValueSource, like a Source node dropped onto a Write in game
    ISyncNodeOperation Write<T, TGlobal, TSource, TWrite>(FrooxEngine.IField<T> field, INodeValueOutput<T> value)
        where T : struct
        where TGlobal : FrooxEngine.ProtoFlux.GlobalReference<FrooxEngine.IValue<T>>, new()
        where TSource : FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueSource<T>, new()
        where TWrite : FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueWrite<FrooxEngineContext, T>, new()
    {
        var global = Next<TGlobal>().Binding;
        var source = Next<TSource>().Binding;
        var write = Next<TWrite>().Binding;

        global.Reference = field;
        source.Source = global;
        write.Variable = source;
        write.Value = value;

        return write;
    }

    /// <summary>
    /// Triggers the operations when the button (e.g. a context menu item) is pressed / released
    /// </summary>
    public void ButtonEvents(FrooxEngine.IButton button, ISyncNodeOperation pressed, ISyncNodeOperation released)
    {
        var global = Next<ProtoFluxGlobalButtonWrapper>().Binding;
        var node = Next<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Interaction.ButtonEvents>().Binding;

        global.Reference = button;
        node.Button = global;
        node.Pressed = pressed;
        node.Released = released;
    }

    #endregion

    #region DRIVES

    public void Drive(INodeValueOutput<Vector3> value, FrooxEngine.IField<Vector3> field)
    {
        var drive = Next<ProtoFluxFieldDriveFloat3Wrapper>().Binding;
        var proxy = Next<ProtoFluxFieldDriveProxyFloat3Wrapper>().Binding;

        drive.Value = value;
        proxy.Node = drive;
        proxy.Drive = field;
    }

    public void Drive(INodeValueOutput<Quaternion> value, FrooxEngine.IField<Quaternion> field)
    {
        var drive = Next<ProtoFluxFieldDriveFloatQWrapper>().Binding;
        var proxy = Next<ProtoFluxFieldDriveProxyFloatQWrapper>().Binding;

        drive.Value = value;
        proxy.Node = drive;
        proxy.Drive = field;
    }

    #endregion
}
