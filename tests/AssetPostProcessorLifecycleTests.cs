#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class AssetPostProcessorLifecycleTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void GuardedAssetSurvivesSerializedCacheReconstruction()
    {
        using (var fixture = new Fixture())
        {
            var guard = fixture.Guard();
            var original = fixture.AddAsset(guard);
            var restored = fixture.NewObject("Restored asset").AddComponent<MeshConverter>();
            // EditorJsonUtility drops preview-scene object references; JsonUtility retains them.
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(original), restored);
            Assert.IsNotNull(restored.PostProcessor, "Reload must not collapse a guarded asset into the unprocessed cache key.");
            Assert.AreEqual(new AssetMap<Mesh>(fixture.Mesh, guard), new AssetMap<Mesh>(fixture.Mesh, restored.PostProcessor));
            Assert.AreEqual(new AssetMap<Mesh>(fixture.Mesh, guard).GetHashCode(), new AssetMap<Mesh>(fixture.Mesh, restored.PostProcessor).GetHashCode());
            Object.DestroyImmediate(original);
            var protectedGuard = fixture.Guard("Foot.R");
            var protectedOriginal = fixture.AddAsset(protectedGuard);
            var protectedRestored = fixture.NewObject("Restored protected variant").AddComponent<MeshConverter>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(protectedOriginal), protectedRestored);
            Object.DestroyImmediate(protectedOriginal);
            var manager = fixture.Manager();
            fixture.Scan(manager);
            Assert.IsTrue(manager.HasMesh(fixture.Mesh, fixture.Guard()));
            Assert.AreSame(restored.Provider.Binding, manager.GetMesh(fixture.Mesh, fixture.Guard()));
            Assert.AreSame(protectedRestored.Provider.Binding, manager.GetMesh(fixture.Mesh, fixture.Guard("Foot.R")));
            Assert.AreNotSame(restored.Provider.Binding, protectedRestored.Provider.Binding);
            var payload = MeshConverter.ConvertMesh(fixture.Mesh);
            restored.PostProcessor.Process(payload);
            Assert.AreEqual("Foot.Right", payload.BlendShapes[0].Name);
            payload = MeshConverter.ConvertMesh(fixture.Mesh);
            protectedRestored.PostProcessor.Process(payload);
            Assert.AreEqual("Foot.R", payload.BlendShapes[0].Name);
        }
    }

    [Test]
    public void SharedMeshPolicyReusesAssetAcrossIndependentRenderers()
    {
        using (var fixture = new Fixture())
        {
            var manager = fixture.Manager();
            var first = manager.GetMesh(fixture.Mesh, fixture.Guard());
            var second = manager.GetMesh(fixture.Mesh, fixture.Guard());
            Assert.AreSame(first, second, "Equivalent immutable guards must share a cache key, including after reload.");
        }
    }

    [Test]
    public void LiveMeshChangeQueuesEveryExistingVariantWithoutCreatingARawVariant()
    {
        using (var fixture = new Fixture())
        {
            var manager = fixture.Manager();
            var guarded = manager.GetMesh(fixture.Mesh, fixture.Guard());
            var other = manager.GetMesh(fixture.Mesh, new AssetMessageDelegatePostProcessor(m => { }));
            fixture.Queue(manager).Clear();
            fixture.UpdatedRoots(manager).Clear();
            manager.BeginConversion();
            var refresh = typeof(AssetConversionManager).GetMethod("RefreshAssetVariants");
            Assert.IsNotNull(refresh, "Live updates need a variant-aware asset refresh entry point.");
            refresh.Invoke(manager, new object[] { fixture.Mesh });
            CollectionAssert.AreEquivalent(new[] { guarded, other }, fixture.Queue(manager).Cast<MeshConverter>().Select(c => c.Provider.Binding));
            Assert.IsFalse(manager.HasMesh(fixture.Mesh), "Refreshing guarded variants must not create an unprocessed asset.");
            refresh.Invoke(manager, new object[] { fixture.Mesh });
            Assert.AreEqual(2, fixture.Queue(manager).Count, "One batch should not enqueue a variant twice.");
        }
    }

    [Test]
    public void LiveUpdatesIncludeTextureCubemapAndAudioVariants()
    {
        using (var fixture = new Fixture())
        {
            var texture = new Texture2D(2, 2);
            var cubemap = new Cubemap(2, TextureFormat.RGBA32, false);
            var clip = AudioClip.Create("Synthetic clip", 16, 1, 8000, false);
            try
            {
                var manager = fixture.Manager();
                var processor = new AssetMessageDelegatePostProcessor(m => { });
                manager.GetTexture2D(texture);
                manager.GetTexture2D(texture, processor);
                manager.GetCubemap(cubemap);
                manager.GetCubemap(cubemap, processor);
                manager.GetAudioClip(clip);
                manager.GetAudioClip(clip, processor);
                var refresh = typeof(AssetConversionManager).GetMethod("RefreshAssetVariants");
                Assert.IsNotNull(refresh);
                foreach (var asset in new Object[] { texture, cubemap, clip })
                {
                    fixture.Queue(manager).Clear();
                    fixture.UpdatedRoots(manager).Clear();
                    refresh.Invoke(manager, new object[] { asset });
                    Assert.AreEqual(2, fixture.Queue(manager).Count);
                    Assert.AreEqual(2, fixture.UpdatedRoots(manager).Count);
                    Assert.AreEqual(1, fixture.Queue(manager).Count(c => c.PostProcessor == null));
                    Assert.AreEqual(1, fixture.Queue(manager).Count(c => c.PostProcessor == processor));
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(cubemap);
                Object.DestroyImmediate(clip);
            }
        }
    }

    [Test]
    public void LostNonserializableProcessorIsNotReusedAsAnUnprocessedAsset()
    {
        using (var fixture = new Fixture())
        {
            var original = fixture.AddAsset(new AssetMessageDelegatePostProcessor(m => { }));
            var restored = fixture.NewObject("Lost delegate asset").AddComponent<MeshConverter>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(original), restored);
            Object.DestroyImmediate(original);
            var raw = fixture.AddAsset(null);
            var manager = fixture.Manager();
            Assert.DoesNotThrow(() => fixture.Scan(manager));
            Assert.AreSame(raw.Provider.Binding, manager.GetMesh(fixture.Mesh));
            Assert.IsTrue(restored == null, "Discard only the generated cache entry whose processing recipe was lost.");
            Assert.IsTrue(fixture.Mesh != null);
        }
    }

    [Test]
    public void LegacyLoneVariantCannotBeReusedAsRawAsset()
    {
        using (var fixture = new Fixture())
        {
            var legacy = fixture.AddLegacyAsset();
            var oldProvider = legacy.Provider.Binding;
            var manager = fixture.Manager();
            fixture.Scan(manager);
            Assert.IsFalse(manager.HasMesh(fixture.Mesh), "A legacy entry has no trustworthy raw/processed identity.");
            Assert.IsTrue(legacy == null);
            var replacement = manager.GetMesh(fixture.Mesh);
            Assert.AreNotSame(oldProvider, replacement);
            var reconstructed = fixture.Manager();
            fixture.Scan(reconstructed);
            Assert.AreSame(replacement, reconstructed.GetMesh(fixture.Mesh), "Rebuilt raw entries must remain reusable.");
            Assert.AreEqual("Foot.R", fixture.Mesh.GetBlendShapeName(0));
        }
    }

    [Test]
    public void LegacyDuplicateVariantsAreDiscardedWithoutCollidingWithKnownEntries()
    {
        using (var fixture = new Fixture())
        {
            var first = fixture.AddLegacyAsset();
            var second = fixture.AddLegacyAsset();
            var raw = fixture.AddAsset(null);
            var guarded = fixture.AddAsset(fixture.Guard());
            var unrelated = fixture.NewObject("Unrelated child");
            var manager = fixture.Manager();
            Assert.DoesNotThrow(() => fixture.Scan(manager));
            Assert.IsTrue(first == null && second == null);
            Assert.AreSame(raw.Provider.Binding, manager.GetMesh(fixture.Mesh));
            Assert.AreSame(guarded.Provider.Binding, manager.GetMesh(fixture.Mesh, fixture.Guard()));
            Assert.IsTrue(unrelated != null && fixture.Mesh != null);
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly Scene scene = EditorSceneManager.NewPreviewScene();
        readonly GameObject root;
        public readonly Mesh Mesh;
        public Fixture()
        {
            root = new GameObject("Asset lifecycle fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            Mesh = new Mesh { name = "Synthetic guarded mesh" };
            Mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            Mesh.triangles = new[] { 0, 1, 2 };
            Mesh.AddBlendShapeFrame("Foot.R", 100, new[] { Vector3.right, Vector3.zero, Vector3.zero }, null, null);
            var descriptorType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(VRChatTypes.AvatarDescriptor)).First(t => t != null);
            root.AddComponent(descriptorType);
        }
        public GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(root.transform, false);
            return go;
        }
        public AssetMessagePostProcessor Guard(params string[] explicitNames)
        {
            var renderer = NewObject("Renderer").AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = Mesh;
            var descriptor = root.GetComponents<Component>().First(c => c.GetType().FullName == VRChatTypes.AvatarDescriptor);
            var type = descriptor.GetType();
            type.GetField("VisemeSkinnedMesh").SetValue(descriptor, explicitNames.Length > 0 ? renderer : null);
            type.GetField("VisemeBlendShapes").SetValue(descriptor, explicitNames);
            var mode = type.GetField("lipSync");
            mode.SetValue(descriptor, Enum.Parse(mode.FieldType, "VisemeBlendShape"));
            AvatarBlendShapeNameGuard cached = null;
            return AvatarBlendShapeNameGuard.ForRenderer(renderer, ref cached);
        }
        public MeshConverter AddAsset(AssetMessagePostProcessor processor)
        {
            var asset = NewObject("Asset").AddComponent<MeshConverter>();
            asset.Initialize(Mesh, processor);
            return asset;
        }
        public MeshConverter AddLegacyAsset()
        {
            var go = NewObject("Legacy asset");
            var asset = go.AddComponent<MeshConverter>();
            go.AddResoniteComponent<FrooxEngine.StaticMesh>();
            // Old payloads contain neither processing metadata nor a serialized provider container.
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Source = new { instanceID = Mesh.GetInstanceID() },
                LastTimestamp = 0,
                Provider = new { }
            });
            JsonUtility.FromJsonOverwrite(json, asset);
            Assert.AreSame(Mesh, asset.Source);
            Assert.IsTrue(asset.Provider.Container != null);
            Assert.IsNull(asset.PostProcessor);
            return asset;
        }
        public AssetConversionManager Manager()
        {
            var manager = (AssetConversionManager)FormatterServices.GetUninitializedObject(typeof(AssetConversionManager));
            typeof(AssetConversionManager).GetProperty("AssetsRoot").SetValue(manager, root.transform);
            foreach (var field in typeof(AssetConversionManager).GetFields(Private).Where(f => f.FieldType.IsGenericType))
                field.SetValue(manager, Activator.CreateInstance(field.FieldType));
            return manager;
        }
        public void Scan(AssetConversionManager manager)
        {
            var scan = typeof(AssetConversionManager).GetMethod("ScanConverters", Private).MakeGenericMethod(
                typeof(FrooxEngine.StaticMesh), typeof(Mesh), typeof(FrooxEngine.Mesh), typeof(MeshConverter));
            scan.Invoke(manager, new[] { typeof(AssetConversionManager).GetField("_meshes", Private).GetValue(manager) });
        }
        public Queue<AssetConverter> Queue(AssetConversionManager manager) =>
            (Queue<AssetConverter>)typeof(AssetConversionManager).GetField("_scheduledConversions", Private).GetValue(manager);
        public HashSet<Transform> UpdatedRoots(AssetConversionManager manager) =>
            (HashSet<Transform>)typeof(AssetConversionManager).GetField("_updatedAssetProviderRoots", Private).GetValue(manager);
        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(Mesh);
        }
    }
}
#endif
