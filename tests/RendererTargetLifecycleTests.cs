#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class RendererTargetLifecycleTests
{
    [Test]
    public void BlendShapeTargetRecoversBeforeExistingRendererUpdate()
    {
        using var fixture = new Fixture();
        var converter = fixture.Host.AddComponent<SkinnedMeshRendererConverter>();
        converter.Target = fixture.Renderer;
        FrooxEngine.IField<float> field = null;
        new BlendShapeTarget(fixture.Renderer, 0).ResolveField(fixture.Context, f => field = f);
        Assert.IsNotNull(field, "An existing converter can be present before its lost binding is recreated.");
        Assert.AreSame(converter.Binding.BlendShapeWeights.GetElement(0).Member, field);
        converter.Binding.SetFrom(fixture.Renderer, fixture.Context);
        Assert.AreSame(converter.Binding.BlendShapeWeights.GetElement(0).Member, field);
        Assert.AreEqual(1, fixture.Host.GetComponents<ResoniteComponent>().Length);
    }

    [Test]
    public void MaterialTargetAllocatesSlotBeforeFreshRendererUpdate()
    {
        using var fixture = new Fixture();
        FrooxEngine.AssetRef<FrooxEngine.Material> field = null;
        new MaterialSlotTarget(fixture.Renderer, 0).ResolveSlot(fixture.Context, f => field = f);
        Assert.IsNull(field);
        var converter = fixture.Host.AddComponent<SkinnedMeshRendererConverter>();
        converter.Initialize(fixture.Renderer);
        fixture.Context.CompleteInitialization(fixture.Renderer);
        Assert.IsNotNull(field, "Deferred callbacks run after Initialize but before SetFrom populates materials.");
        Assert.AreSame(converter.Binding.Materials.GetElement(0).Member, field);
        converter.Binding.SetFrom(fixture.Renderer, fixture.Context);
        Assert.AreSame(converter.Binding.Materials.GetElement(0).Member, field);
    }

    [Test]
    public void MaterialAndEnabledTargetsRecoverDestroyedStaticRendererBinding()
    {
        using var fixture = new Fixture();
        Object.DestroyImmediate(fixture.Renderer);
        var source = fixture.Host.AddComponent<MeshRenderer>();
        source.sharedMaterials = new Material[1];
        var converter = fixture.Host.AddComponent<MeshRendererConverter>();
        converter.Initialize(source);
        Object.DestroyImmediate(converter.Container);
        FrooxEngine.AssetRef<FrooxEngine.Material> material = null;
        new MaterialSlotTarget(source, 0).ResolveSlot(fixture.Context, f => material = f);
        FrooxEngine.IField<bool> enabled = null;
        new RendererEnabledTarget(source).ResolveField(fixture.Context, f => enabled = f);
        Assert.IsNotNull(material);
        Assert.AreSame(converter.Binding.Materials.GetElement(0).Member, material);
        Assert.AreSame(converter.Binding.Enabled_Element.Member, enabled);
        converter.Binding.SetFrom(source, fixture.Context);
        Assert.AreSame(converter.Binding.Materials.GetElement(0).Member, material);
        Assert.AreEqual(1, fixture.Host.GetComponents<ResoniteComponent>().Length);
    }

    [Test]
    public void InvalidMaterialSlotsDoNotAllocateOrResolve()
    {
        using var fixture = new Fixture();
        var converter = fixture.Host.AddComponent<SkinnedMeshRendererConverter>();
        converter.Initialize(fixture.Renderer);
        var called = false;
        new MaterialSlotTarget(fixture.Renderer, -1).ResolveSlot(fixture.Context, f => called = true);
        new MaterialSlotTarget(fixture.Renderer, 1).ResolveSlot(fixture.Context, f => called = true);
        Assert.IsFalse(called);
        Assert.AreEqual(0, converter.Binding.Materials.Count);
    }

    sealed class Fixture : IDisposable
    {
        readonly Scene scene = EditorSceneManager.NewPreviewScene();
        readonly Mesh mesh;
        public readonly GameObject Host;
        public readonly SkinnedMeshRenderer Renderer;
        public readonly TestContext Context = new TestContext();
        public Fixture()
        {
            Host = new GameObject("Renderer target fixture");
            SceneManager.MoveGameObjectToScene(Host, scene);
            mesh = new Mesh { name = "Synthetic target" };
            mesh.vertices = new[] { Vector3.zero };
            mesh.AddBlendShapeFrame("EXTRA Fat", 100, new[] { Vector3.right }, null, null);
            Renderer = Host.AddComponent<SkinnedMeshRenderer>();
            Renderer.sharedMesh = mesh;
            Renderer.sharedMaterials = new Material[1];
        }
        public void Dispose()
        {
            foreach (var converter in Host.GetComponents<ResoniteComponentConverter>())
                Object.DestroyImmediate(converter);
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(mesh);
        }
    }

    sealed class TestContext : IConversionContext
    {
        readonly SceneConverter scene = (SceneConverter)FormatterServices.GetUninitializedObject(typeof(SceneConverter));
        readonly Dictionary<Component, List<Action>> pending = new Dictionary<Component, List<Action>>();
        public TestContext() => typeof(SceneConverter).GetField("_deferedActions", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(scene, pending);
        public void RunOnConverted(Component component, Action action) => scene.RunOnConverted(component, action);
        public void CompleteInitialization(Component component)
        {
            foreach (var action in pending[component]) action();
            pending.Remove(component);
        }
        public string GetUniqueMessageId(string prefix) => throw new NotSupportedException();
        public string GetTransformSlotId(Transform transform) => throw new NotSupportedException();
        public string GetId(FrooxEngine.IWorldElement element) => throw new NotSupportedException();
        public string GetIdOrAllocate(FrooxEngine.IWorldElement element) => throw new NotSupportedException();
        public string GetIdOrAllocate(FrooxEngine.IWorldElement element, out bool allocated) => throw new NotSupportedException();
        public void RemoveId(FrooxEngine.IWorldElement element) => throw new NotSupportedException();
        public FrooxEngine.IWorldElement TryResolveId(string id) => throw new NotSupportedException();
        public void NotifyOfStaticMesh(Mesh mesh, int index, Material[] materials) { }
        public FrooxEngine.IAssetProvider<FrooxEngine.Mesh> GetMesh(Mesh mesh, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.ITexture> GetITexture(Texture texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.ITexture2D> GetITexture2D(Texture texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Texture2D> GetTexture2D(Texture2D texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Cubemap> GetCubemap(Cubemap cubemap, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.AudioClip> GetAudioClip(AudioClip clip, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Material> GetMaterial(Material material) => null;
        public System.Threading.Tasks.Task<ResoniteLink.MethodResult> CallMethod(ResoniteLink.CallSyncMethod request) => null;
        public System.Threading.Tasks.Task<ResoniteLink.MethodResult> CallMethod(ResoniteLink.CallStaticSyncMethod request) => null;
    }
}
#endif
