#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using LocalTransformNode = FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.LocalTransform;
using SlerpNode = FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.Quaternions.Slerp_floatQ;

public class ComponentLifecycleRegressionTests
{
    [Test]
    public void EnsureOnRecreatesExternallyDestroyedRetainedReferenceOnSameHost() =>
        AssertEnsureOnRecovers(false);

    [Test]
    public void EnsureOnRecreatesExternallyDestroyedRetainedReferenceOnDifferentHost() =>
        AssertEnsureOnRecovers(true);

    static void AssertEnsureOnRecovers(bool changeHost)
    {
        using var scene = new PreviewSceneScope();
        var originalHost = new GameObject("Original owner");
        var target = changeHost ? new GameObject("Replacement owner") : originalHost;
        var unrelated = originalHost.AddResoniteComponent<ValueCopyIntWrapper>();
        var sameTypeUnowned = target.AddResoniteComponent<ValueCopyFloatWrapper>();
        sameTypeUnowned.Binding.WriteBack = true;
        ResRef<ValueCopyFloatWrapper> owned = default;
        ConverterComponentHelper.EnsureOn(ref owned, originalHost);
        owned.Binding.WriteBack = true;
        var oldContainer = owned.Container;
        var oldBinding = owned.Binding;
        Object.DestroyImmediate(oldContainer);

        // Keep the actual ResRef, rather than replacing it with default or querying the host again.
        Assert.IsTrue(ReferenceEquals(oldContainer, owned.Container));
        Assert.IsFalse(ReferenceEquals(null, owned.Container));
        Assert.IsTrue(owned.Container == null, "This fixture must exercise Unity's destroyed-object null semantics.");
        var returned = ConverterComponentHelper.EnsureOn(ref owned, target);
        Assert.IsTrue(owned.Container != null);
        Assert.AreSame(target, owned.Container.gameObject);
        Assert.AreSame(owned.Container, returned.Container);
        Assert.AreSame(owned.Binding, returned.Binding);
        Assert.AreNotSame(oldContainer, owned.Container);
        Assert.AreNotSame(oldBinding, owned.Binding);
        Assert.AreNotSame(sameTypeUnowned.Container, owned.Container, "EnsureOn must not adopt somebody else's component.");
        Assert.IsFalse(owned.Binding.WriteBack, "Recovery must initialize a fresh binding, not reuse zombie data.");
        Assert.IsTrue(sameTypeUnowned.Binding.WriteBack);

        var replacement = owned.Container;
        ConverterComponentHelper.EnsureOn(ref owned, target);
        Assert.AreSame(replacement, owned.Container, "The first update after recovery must reuse the replacement.");
        Assert.AreEqual(2, target.GetResoniteComponents<ValueCopyFloatWrapper>().Count);
        ConverterComponentHelper.EnsureOn(ref owned, null);
        Assert.IsTrue(owned == null);
        Assert.IsTrue(replacement == null);
        Assert.IsTrue(unrelated.Container != null);
        Assert.IsTrue(sameTypeUnowned.Container != null);
        Assert.AreEqual(1, target.GetResoniteComponents<ValueCopyFloatWrapper>().Count);
        if (changeHost)
            Assert.AreEqual(0, originalHost.GetResoniteComponents<ValueCopyFloatWrapper>().Count);
    }

    [Test]
    public void MixedGraphRebuildsRetainContainersBindingsMemberIdsAndExportedTargets()
    {
        using var fixture = new GraphFixture();
        var first = fixture.Build(2, 0);
        for (var pass = 1; pass <= 4; pass++)
        {
            var rebuilt = fixture.Build(2, pass);
            AssertRetained(first, rebuilt, 2);
        }
    }

    [Test]
    public void MixedGraphShrinkAndRegrowthRetainSurvivorsWithoutReusingRemovedIds()
    {
        using var fixture = new GraphFixture();
        var initial = fixture.Build(2, 0);
        var smaller = fixture.Build(1, 1);
        AssertRetained(initial, smaller, 1);
        var removed = initial.Values.Select(nodes => nodes[1]).ToArray();
        Assert.IsTrue(removed.All(node => node.Container == null), "End must destroy the unused tail of every binding-type pool.");
        var removedIds = new HashSet<string>(removed.SelectMany(node => node.AllIds));

        var regrown = fixture.Build(3, 2);
        AssertRetained(smaller, regrown, 1);
        var additions = regrown.Values.SelectMany(nodes => nodes.Skip(1)).ToArray();
        Assert.IsFalse(additions.SelectMany(node => node.AllIds).Any(removedIds.Contains),
            "Regrowth must allocate new bindings and members for destroyed nodes, not resurrect their export IDs.");
        Assert.IsFalse(additions.Any(node => removed.Any(old => ReferenceEquals(old.Binding, node.Binding))));
        AssertRetained(regrown, fixture.Build(3, 3), 3);

        var empty = fixture.Build(0, 4);
        Assert.IsEmpty(empty);
        Assert.IsTrue(regrown.Values.SelectMany(nodes => nodes).All(node => node.Container == null));
        var priorIds = new HashSet<string>(initial.Values.Concat(regrown.Values).SelectMany(nodes => nodes).SelectMany(node => node.AllIds));
        var restarted = fixture.Build(2, 5);
        Assert.IsFalse(restarted.Values.SelectMany(nodes => nodes).SelectMany(node => node.AllIds).Any(priorIds.Contains),
            "A fully cleared graph must not reconnect new nodes to any previously destroyed graph member.");
    }

    static void AssertRetained(Dictionary<Type, NodeSnapshot[]> before, Dictionary<Type, NodeSnapshot[]> after, int count)
    {
        CollectionAssert.AreEquivalent(before.Keys, after.Keys);
        foreach (var type in before.Keys)
        for (var i = 0; i < count; i++)
        {
            var old = before[type][i];
            var current = after[type][i];
            var label = type.Name + "[" + i + "]";
            Assert.AreSame(old.Container, current.Container, label + " container");
            Assert.AreSame(old.Binding, current.Binding, label + " binding");
            Assert.AreEqual(old.Id, current.Id, label + " binding ID");
            CollectionAssert.AreEquivalent(old.Members.Keys, current.Members.Keys, label);
            foreach (var name in old.Members.Keys)
                Assert.AreEqual(old.Members[name].ID, current.Members[name].ID, label + "." + name + " member ID");
        }
    }

    sealed class NodeSnapshot
    {
        public readonly ResoniteComponent Container;
        public readonly ResoniteObject Binding;
        public readonly string Id;
        public readonly Dictionary<string, ResoniteLink.Member> Members;
        public IEnumerable<string> AllIds => new[] { Id }.Concat(Members.Values.Select(member => member.ID));

        public NodeSnapshot(ResoniteComponent container, ExportContext context)
        {
            Container = container;
            Binding = container.Data;
            Id = context.GetIdOrAllocate((FrooxEngine.IWorldElement)Binding);
            Members = new Dictionary<string, ResoniteLink.Member>();
            Binding.CollectMembers(Members, context);
        }
    }

    sealed class GraphFixture : IDisposable
    {
        static readonly Type[] NodeTypes = {
            typeof(ProtoFluxValueInputFloatWrapper), typeof(ProtoFluxValueInputFloat3Wrapper),
            typeof(ProtoFluxValueInputFloatQWrapper), typeof(ProtoFluxValueInputBoolWrapper),
            typeof(ProtoFluxValueInputIntWrapper), typeof(LocalTransformNode), typeof(ProtoFluxSlotInputWrapper),
            typeof(ProtoFluxValueLerpFloat3Wrapper), typeof(SlerpNode),
            typeof(ProtoFluxFieldDriveFloat3Wrapper), typeof(ProtoFluxFieldDriveProxyFloat3Wrapper),
            typeof(ProtoFluxFieldDriveFloatQWrapper), typeof(ProtoFluxFieldDriveProxyFloatQWrapper)
        };
        readonly PreviewSceneScope scene = new PreviewSceneScope();
        readonly ExportContext context = new ExportContext();
        readonly GameObject host;
        readonly GameObject[] sources;
        readonly ProtoFluxValueInputFloat3Wrapper[] vectorTargets;
        readonly ProtoFluxValueInputFloatQWrapper[] rotationTargets;
        readonly ProtoFluxGraph graph;

        public GraphFixture()
        {
            try
            {
                host = new GameObject("Mixed graph");
                graph = new ProtoFluxGraph(host);
                sources = new[] { new GameObject("Source A"), new GameObject("Source B"), new GameObject("Source C") };
                // Keep external fields off the graph's exclusively owned host: End removes unused containers there.
                vectorTargets = sources.Select(source => source.AddResoniteComponent<ProtoFluxValueInputFloat3Wrapper>().Binding).ToArray();
                rotationTargets = sources.Select(source => source.AddResoniteComponent<ProtoFluxValueInputFloatQWrapper>().Binding).ToArray();
            }
            catch { scene.Dispose(); throw; }
        }

        public Dictionary<Type, NodeSnapshot[]> Build(int count, int pass)
        {
            graph.Begin();
            for (var i = 0; i < count; i++)
            {
                FrooxEngine.ProtoFlux.INodeValueOutput<float> weight;
                FrooxEngine.ProtoFlux.INodeValueOutput<Vector3> vector;
                FrooxEngine.ProtoFlux.INodeValueOutput<Quaternion> rotation;
                // Interleave different closed generic types in a different order on alternating updates.
                if (pass % 2 == 0)
                {
                    weight = graph.Constant(Weight(i, pass));
                    vector = graph.Constant(Vector(i, pass));
                    graph.Constant(pass % 2 == 0);
                    rotation = graph.Constant(Rotation(i, pass));
                    graph.Constant(pass + i);
                }
                else
                {
                    graph.Constant(pass + i);
                    rotation = graph.Constant(Rotation(i, pass));
                    graph.Constant(pass % 2 == 0);
                    vector = graph.Constant(Vector(i, pass));
                    weight = graph.Constant(Weight(i, pass));
                }
                var sourceIndex = (i + pass) % sources.Length;
                var local = graph.LocalTransform(sources[sourceIndex].transform);
                var position = graph.Lerp(local.position, vector, weight);
                var orientation = graph.Slerp(local.rotation, rotation, weight);
                var targetIndex = (sourceIndex + 1) % sources.Length;
                graph.Drive(position, vectorTargets[targetIndex].Value_Element.Member);
                graph.Drive(orientation, rotationTargets[targetIndex].Value_Element.Member);
            }
            graph.End();

            var containers = host.GetComponents<ResoniteComponent>();
            Assert.AreEqual(NodeTypes.Length * count, containers.Length, "Rebuilds must not accumulate containers or borrow a different binding type.");
            var exported = containers.Select(container => new NodeSnapshot(container, context))
                .GroupBy(node => node.Binding.GetType()).ToDictionary(group => group.Key, group => group.ToArray());
            if (count == 0) return exported;
            CollectionAssert.AreEquivalent(NodeTypes, exported.Keys);
            foreach (var nodes in exported.Values) Assert.AreEqual(count, nodes.Length);
            var ids = exported.Values.SelectMany(nodes => nodes).SelectMany(node => node.AllIds).ToArray();
            Assert.IsTrue(ids.All(id => !string.IsNullOrEmpty(id)), "Every exported binding and member must have an ID.");
            Assert.AreEqual(ids.Length, ids.Distinct().Count(), "Distinct bindings/members must never alias an export ID.");

            for (var i = 0; i < count; i++)
            {
                var sourceIndex = (i + pass) % sources.Length;
                var targetIndex = (sourceIndex + 1) % sources.Length;
                var local = exported[typeof(LocalTransformNode)][i];
                var slot = exported[typeof(ProtoFluxSlotInputWrapper)][i];
                var weight = exported[typeof(ProtoFluxValueInputFloatWrapper)][i];
                var vector = exported[typeof(ProtoFluxValueInputFloat3Wrapper)][i];
                var rotation = exported[typeof(ProtoFluxValueInputFloatQWrapper)][i];
                var lerp = exported[typeof(ProtoFluxValueLerpFloat3Wrapper)][i];
                var slerp = exported[typeof(SlerpNode)][i];
                var drive = exported[typeof(ProtoFluxFieldDriveFloat3Wrapper)][i];
                var rotationDrive = exported[typeof(ProtoFluxFieldDriveFloatQWrapper)][i];
                Assert.AreEqual(Weight(i, pass), ((ResoniteLink.Field)weight.Members["Value"]).BoxedValue);
                Assert.AreEqual(Vector(i, pass), ((ProtoFluxValueInputFloat3Wrapper)vector.Binding).Value);
                Assert.AreEqual(Rotation(i, pass), ((ProtoFluxValueInputFloatQWrapper)rotation.Binding).Value);
                Assert.AreEqual(pass % 2 == 0, ((ResoniteLink.Field)exported[typeof(ProtoFluxValueInputBoolWrapper)][i].Members["Value"]).BoxedValue);
                Assert.AreEqual(pass + i, ((ResoniteLink.Field)exported[typeof(ProtoFluxValueInputIntWrapper)][i].Members["Value"]).BoxedValue);
                AssertReference(slot, "Target", context.GetIdOrAllocate(sources[sourceIndex].transform.GetSlot()));
                AssertReference(local, "Instance", slot.Id);
                AssertReference(lerp, "From", local.Members["LocalPosition"].ID);
                AssertReference(lerp, "To", vector.Id);
                AssertReference(lerp, "Lerp", weight.Id);
                AssertReference(slerp, "From", local.Members["LocalRotation"].ID);
                AssertReference(slerp, "To", rotation.Id);
                AssertReference(slerp, "Lerp", weight.Id);
                AssertReference(drive, "Value", lerp.Id);
                AssertReference(rotationDrive, "Value", slerp.Id);
                var vectorMembers = vectorTargets[targetIndex].CollectMembers(context);
                var rotationMembers = rotationTargets[targetIndex].CollectMembers(context);
                AssertReference(exported[typeof(ProtoFluxFieldDriveProxyFloat3Wrapper)][i], "Node", drive.Id);
                AssertReference(exported[typeof(ProtoFluxFieldDriveProxyFloat3Wrapper)][i], "Drive", vectorMembers["Value"].ID);
                AssertReference(exported[typeof(ProtoFluxFieldDriveProxyFloatQWrapper)][i], "Node", rotationDrive.Id);
                AssertReference(exported[typeof(ProtoFluxFieldDriveProxyFloatQWrapper)][i], "Drive", rotationMembers["Value"].ID);
            }
            return exported;
        }

        void AssertReference(NodeSnapshot node, string member, string expectedId)
        {
            Assert.IsNotEmpty(expectedId);
            Assert.IsInstanceOf<ResoniteLink.Reference>(node.Members[member]);
            var reference = (ResoniteLink.Reference)node.Members[member];
            Assert.AreEqual(expectedId, reference.TargetID, node.Binding.GetType().Name + "." + member);
            Assert.IsNotNull(context.TryResolveId(reference.TargetID));
        }

        static float Weight(int i, int pass) => 0.1f * (i + 1) + 0.01f * pass;
        static Vector3 Vector(int i, int pass) => new Vector3(i + pass, pass, i + 1);
        static Quaternion Rotation(int i, int pass) => Quaternion.Euler(i * 10, pass * 15, 0);
        public void Dispose() => scene.Dispose();
    }

    sealed class ExportContext : IConversionContext
    {
        readonly Dictionary<FrooxEngine.IWorldElement, string> ids = new Dictionary<FrooxEngine.IWorldElement, string>();
        int nextId;
        public string GetUniqueMessageId(string prefix) => prefix + "_" + nextId++;
        public string GetTransformSlotId(Transform transform) => transform == null ? null : GetIdOrAllocate(transform.GetSlot());
        public string GetId(FrooxEngine.IWorldElement element) => element != null && ids.TryGetValue(element, out var id) ? id : null;
        public string GetIdOrAllocate(FrooxEngine.IWorldElement element) => GetIdOrAllocate(element, out _);
        public string GetIdOrAllocate(FrooxEngine.IWorldElement element, out bool allocated)
        {
            allocated = false;
            if (element == null) return null;
            if (ids.TryGetValue(element, out var id)) return id;
            allocated = true;
            ids.Add(element, id = "lifecycle_" + nextId++);
            return id;
        }
        public void RemoveId(FrooxEngine.IWorldElement element) { if (element != null) ids.Remove(element); }
        public FrooxEngine.IWorldElement TryResolveId(string id) => ids.FirstOrDefault(pair => pair.Value == id).Key;
        public void NotifyOfStaticMesh(Mesh mesh, int materialStartIndex, Material[] materials) { }
        public void RunOnConverted(Component component, Action action) => action();
        public FrooxEngine.IAssetProvider<FrooxEngine.Mesh> GetMesh(Mesh mesh, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.ITexture> GetITexture(Texture texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.ITexture2D> GetITexture2D(Texture texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Texture2D> GetTexture2D(Texture2D texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Cubemap> GetCubemap(Cubemap cubemap, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.AudioClip> GetAudioClip(AudioClip audioClip, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Material> GetMaterial(Material material) => null;
        public System.Threading.Tasks.Task<ResoniteLink.MethodResult> CallMethod(ResoniteLink.CallSyncMethod request) => null;
        public System.Threading.Tasks.Task<ResoniteLink.MethodResult> CallMethod(ResoniteLink.CallStaticSyncMethod request) => null;
    }

    sealed class PreviewSceneScope : IDisposable
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        static readonly MethodInfo GetTarget = typeof(EditorSceneManager).GetMethod("GetTargetSceneForNewGameObjects", Flags);
        static readonly MethodInfo SetTarget = typeof(EditorSceneManager).GetMethod("SetTargetSceneForNewGameObjects", Flags, null, new[] { typeof(Scene) }, null);
        static readonly MethodInfo ClearTarget = typeof(EditorSceneManager).GetMethod("ClearTargetSceneForNewGameObjects", Flags);
        readonly Scene previousTarget;
        readonly Scene preview;

        public PreviewSceneScope()
        {
            if (GetTarget == null || SetTarget == null || ClearTarget == null)
                throw new NotSupportedException("The editor must support isolated GameObject creation for these fixtures.");
            previousTarget = (Scene)GetTarget.Invoke(null, null);
            preview = EditorSceneManager.NewPreviewScene();
            try { SetTarget.Invoke(null, new object[] { preview }); }
            catch { EditorSceneManager.ClosePreviewScene(preview); throw; }
        }

        public void Dispose()
        {
            try
            {
                if (previousTarget.IsValid()) SetTarget.Invoke(null, new object[] { previousTarget });
                else ClearTarget.Invoke(null, null);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
#endif
