#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class FirstPersonHeadHideTests
{
    [Test]
    public void ToggleHidesOnlyUserViewWithoutChangingSourcePose()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var hide = f.Hide;
            Assert.AreEqual(Renderite.Shared.RenderingContext.UserView, hide.Binding.Context);
            Assert.AreEqual((Vector3?)Vector3.zero, hide.Binding.ScaleOverride);
            Assert.IsNull(hide.Binding.PositionOverride);
            Assert.IsNull(hide.Binding.RotationOverride);
            Assert.IsTrue(hide.Binding.Enabled);
            Assert.AreEqual(new Vector3(1, 2, 3), f.Head.localScale);
            Assert.IsTrue(f.Body.enabled);
            Assert.AreSame(hide.Binding.Enabled_Element.Member, f.Menu.GetResoniteComponent<FrooxEngine.ButtonToggle>().Binding.TargetValue);
            Assert.AreSame(f.Head, hide.Container.transform);
        }
    }

    [Test]
    public void ReservesSeparatePoseOverrideBeforeScaleOverride()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var overrides = f.Overrides;
            Assert.AreEqual(2, overrides.Length);
            Assert.AreNotSame(f.Hide.Container, overrides[0].Container);
            Assert.IsNull(overrides[0].Binding.ScaleOverride);
            Assert.IsNull(overrides[0].Binding.PositionOverride);
            Assert.IsNull(overrides[0].Binding.RotationOverride);
            Assert.AreEqual(0, f.Head.gameObject.GetResoniteComponents<FrooxEngine.CommonAvatar.AvatarUserViewHeadOverride>().Count);
        }
    }

    [Test]
    public void IncludesWholeBodyMeshAndReusesStableConfiguration()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var hide = f.Hide;
            Assert.AreEqual(1, hide.Binding.SkinnedMeshRenderers.Count);
            Assert.AreSame(f.Renderer.Binding, hide.Binding.SkinnedMeshRenderers.GetElement(0).Data);
            var menu = f.Menu;
            f.Apply(false);
            Assert.AreSame(hide.Container, f.Hide.Container);
            Assert.AreSame(menu, f.Menu);
            Assert.IsFalse(f.Hide.Binding.Enabled);
            Assert.AreEqual(2, f.Overrides.Length);
        }
    }

    [Test]
    public void RendererRemovalRecreatesOverrideWithoutNullOrStaleEntries()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var old = f.Hide;
            f.Body.bones = new Transform[0];
            f.Apply();
            Assert.IsTrue(old.Container == null);
            Assert.AreEqual(0, f.Hide.Binding.SkinnedMeshRenderers.Count);
            Assert.AreSame(f.Hide.Binding.Enabled_Element.Member, f.Menu.GetResoniteComponent<FrooxEngine.ButtonToggle>().Binding.TargetValue);
        }
    }

    [Test]
    public void MissingHeadCleansUpOnlyOwnedObjects()
    {
        using (var f = new Fixture())
        {
            var unrelated = f.Head.gameObject.AddComponent<BoxCollider>();
            f.Apply();
            f.ApplyTo(null);
            Assert.IsTrue(f.Hide.Container == null);
            Assert.IsTrue(f.Menu == null);
            Assert.IsTrue(unrelated != null);
        }
    }

    [Test]
    public void MissingPoseReservationIsRecreatedBeforeHideOverride()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            Object.DestroyImmediate(f.Overrides[0].Container);
            f.Apply();
            var overrides = f.Overrides;
            Assert.AreEqual(2, overrides.Length);
            Assert.IsNull(overrides[0].Binding.ScaleOverride);
            Assert.AreSame(f.Hide.Container, overrides[1].Container);
        }
    }

    [Test]
    public void ObsoleteDeferredResolutionCannotRecreateClearedToggle()
    {
        using (var f = new Fixture())
        {
            f.Body.GetComponent<SkinnedMeshRendererConverter>().Target = null;
            var context = new SceneConverter();
            f.ApplyTo(f.Head, true, context);
            var pending = ((System.Collections.Generic.Dictionary<Component, System.Collections.Generic.List<Action>>)
                typeof(SceneConverter).GetField("_deferedActions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context))
                .SelectMany(p => p.Value).ToArray();
            Assert.AreEqual(1, pending.Length);
            f.ApplyTo(null);
            foreach (var action in pending) action();
            Assert.IsTrue(f.Hide.Container == null);
            Assert.IsTrue(f.Menu == null);
        }
    }

    [Test]
    public void ClearingVisibilityPreservesNativePoseReservation()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var pose = f.Overrides[0];
            f.ApplyTo(null);
            Assert.IsTrue(pose.Container != null, "Native desktop instrumentation can own these fields after export.");
            f.Apply();
            Assert.AreSame(pose.Container, f.Overrides[0].Container);
        }
    }

    [Test]
    public void ExcludedAncestorDoesNotBlockRendererResolution()
    {
        using (var f = new Fixture())
        {
            var excluded = new GameObject("VF_UE_VRCFT");
            SceneManager.MoveGameObjectToScene(excluded, f.Head.gameObject.scene);
            excluded.transform.SetParent(f.Head.parent, false);
            f.Body.transform.SetParent(excluded.transform, false);
            f.Body.GetComponent<SkinnedMeshRendererConverter>().Target = null;
            f.Apply();
            Assert.IsTrue(f.Hide.Container != null, "Excluded subtree callbacks never complete.");
            Assert.AreEqual(0, f.Hide.Binding.SkinnedMeshRenderers.Count);
        }
    }

    [Test]
    public void AddsNoVrchatIllegalComponentsAndCleansOwnedFilterEntries()
    {
        using (var f = new Fixture())
        {
            var validation = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Validation.AvatarValidation")).FirstOrDefault(t => t != null);
            if (validation == null) Assert.Ignore("Requires the VRChat SDK.");
            var find = validation.GetMethod("FindIllegalComponents");
            var root = f.Head.parent.gameObject;
            var before = ((System.Collections.Generic.IEnumerable<Component>)find.Invoke(null, new object[] { root })).ToArray();
            f.Apply();
            var after = ((System.Collections.Generic.IEnumerable<Component>)find.Invoke(null, new object[] { root })).ToArray();
            Assert.IsEmpty(after.Except(before), "Head hiding must not add validation failures.");
            var old = f.Hide;
            f.ApplyTo(null);
            var filter = f.Head.GetComponent<ResoniteMemberFilter>();
            Assert.IsTrue(filter == null || filter.Entries.All(e => e.Component != null && e.Component != old.Container));
        }
    }

    [Test]
    public void NullableOverrideExportPreservesWireTypesValuesAndFieldIds()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var data = f.Hide.Binding;
            var context = new ExportContext();
            Assert.AreEqual("[FrooxEngine]FrooxEngine.RenderTransformOverride", f.Hide.Container.TypeName);
            foreach (var populated in new[] { false, true, false })
            {
                data.PositionOverride = populated ? (Vector3?)new Vector3(1, 2, 3) : null;
                data.RotationOverride = populated ? (Quaternion?)Quaternion.Euler(10, 20, 30) : null;
                data.ScaleOverride = populated ? (Vector3?)Vector3.zero : null;
                var members = f.Hide.Container.CollectData(context).Members;
                Assert.IsInstanceOf<ResoniteLink.Field_Nullable_float3>(members["PositionOverride"]);
                Assert.IsInstanceOf<ResoniteLink.Field_Nullable_floatQ>(members["RotationOverride"]);
                Assert.IsInstanceOf<ResoniteLink.Field_Nullable_float3>(members["ScaleOverride"]);
                Assert.AreEqual(data.PositionOverride?.ToResoniteLink(), ((ResoniteLink.Field_Nullable_float3)members["PositionOverride"]).Value);
                Assert.AreEqual(data.RotationOverride?.ToResoniteLink(), ((ResoniteLink.Field_Nullable_floatQ)members["RotationOverride"]).Value);
                Assert.AreEqual(data.ScaleOverride?.ToResoniteLink(), ((ResoniteLink.Field_Nullable_float3)members["ScaleOverride"]).Value);
                Assert.AreEqual(context.GetIdOrAllocate(data.PositionOverride_Element.Member), members["PositionOverride"].ID);
                Assert.AreEqual(context.GetIdOrAllocate(data.RotationOverride_Element.Member), members["RotationOverride"].ID);
                Assert.AreEqual(context.GetIdOrAllocate(data.ScaleOverride_Element.Member), members["ScaleOverride"].ID);
            }
        }
    }

    [Test]
    public void FilteredExportPreservesRendererAndToggleReferencesButNotNativePoseFields()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var context = new ExportContext();
            var hide = f.Hide.Container.CollectData(context);
            ResoniteMemberFilter.Apply(f.Hide.Container, hide);
            CollectionAssert.AreEquivalent(new[] { "persistent", "Enabled", "Context", "PositionOverride", "RotationOverride", "ScaleOverride", "SkinnedMeshRenderers" }, hide.Members.Keys);
            var renderers = (ResoniteLink.SyncList)hide.Members["SkinnedMeshRenderers"];
            Assert.AreEqual(1, renderers.Elements.Count);
            Assert.AreEqual(context.GetIdOrAllocate(f.Renderer.Binding), ((ResoniteLink.Reference)renderers.Elements[0]).TargetID);
            var toggle = f.Menu.GetResoniteComponent<FrooxEngine.ButtonToggle>().Container.CollectData(context);
            Assert.AreEqual(hide.Members["Enabled"].ID, ((ResoniteLink.Reference)toggle.Members["TargetValue"]).TargetID);
            var pose = f.Overrides[0].Container;
            var exportedPose = pose.CollectData(context);
            ResoniteMemberFilter.Apply(pose, exportedPose);
            CollectionAssert.AreEquivalent(new[] { "persistent", "Enabled", "Context" }, exportedPose.Members.Keys);
        }
    }

    [Test]
    public void ExistingBindingScaleOverrideIsNotOverwrittenOrDeleted()
    {
        using (var f = new Fixture())
        {
            var existing = f.Head.gameObject.AddResoniteComponent<FrooxEngine.RenderTransformOverride>();
            existing.Binding.Context = Renderite.Shared.RenderingContext.UserView;
            existing.Binding.ScaleOverride = Vector3.one;
            f.Apply();
            Assert.IsTrue(f.Hide.Container == null);
            Assert.IsTrue(f.Menu == null);
            Assert.IsTrue(existing.Container != null);
            Assert.AreEqual((Vector3?)Vector3.one, existing.Binding.ScaleOverride);
        }
    }

    [Test]
    public void DestroyedHideContainerIsRecreatedWithoutReplacingPoseReservation()
    {
        using (var f = new Fixture())
        {
            f.Apply();
            var pose = f.Overrides[0].Container;
            Object.DestroyImmediate(f.Hide.Container);
            f.Apply();
            Assert.IsTrue(f.Hide.Container != null);
            Assert.AreSame(pose, f.Overrides[0].Container);
            Assert.AreEqual(2, f.Overrides.Length);
            Assert.IsTrue(f.Head.GetComponent<ResoniteMemberFilter>().Entries.All(e => e.Component != null));
        }
    }

    sealed class ExportContext : IConversionContext
    {
        readonly System.Collections.Generic.Dictionary<FrooxEngine.IWorldElement, string> ids =
            new System.Collections.Generic.Dictionary<FrooxEngine.IWorldElement, string>();
        public string GetUniqueMessageId(string prefix) => prefix;
        public string GetTransformSlotId(Transform transform) => null;
        public string GetId(FrooxEngine.IWorldElement o) => o == null ? null : ids[o];
        public string GetIdOrAllocate(FrooxEngine.IWorldElement o) => GetIdOrAllocate(o, out _);
        public string GetIdOrAllocate(FrooxEngine.IWorldElement o, out bool allocated)
        {
            allocated = !ids.TryGetValue(o, out var id);
            if (allocated) ids.Add(o, id = "test_" + ids.Count);
            return id;
        }
        public void RemoveId(FrooxEngine.IWorldElement o) => ids.Remove(o);
        public FrooxEngine.IWorldElement TryResolveId(string id) => ids.FirstOrDefault(p => p.Value == id).Key;
        public void NotifyOfStaticMesh(Mesh combinedMesh, int materialStartIndex, Material[] materials) { }
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

    sealed class Fixture : IDisposable
    {
        readonly Scene scene;
        readonly GameObject root;
        readonly object helper;
        readonly Type type;
        public readonly Transform Head;
        public readonly SkinnedMeshRenderer Body;
        public readonly ResRef<FrooxEngine.SkinnedMeshRenderer> Renderer;
        public ResRef<FrooxEngine.RenderTransformOverride> Hide => (ResRef<FrooxEngine.RenderTransformOverride>)type.GetField("HideOverride").GetValue(helper);
        public ResRef<FrooxEngine.RenderTransformOverride>[] Overrides => Head.GetComponents<ResoniteComponent>()
            .Where(c => c.Data is FrooxEngine.RenderTransformOverride)
            .Select(c => new ResRef<FrooxEngine.RenderTransformOverride>(c)).ToArray();
        public GameObject Menu => (GameObject)type.GetField("MenuItem").GetValue(helper);

        public Fixture()
        {
            type = typeof(AnimatorConverter).Assembly.GetType("FirstPersonHeadHideToggle");
            Assert.IsNotNull(type, "The generalized first-person head toggle must exist.");
            helper = Activator.CreateInstance(type);
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Head hide fixture") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            Head = new GameObject("Head").transform;
            SceneManager.MoveGameObjectToScene(Head.gameObject, scene);
            Head.SetParent(root.transform, false);
            Head.localScale = new Vector3(1, 2, 3);
            var body = new GameObject("Whole Body");
            SceneManager.MoveGameObjectToScene(body, scene);
            body.transform.SetParent(root.transform, false);
            Body = body.AddComponent<SkinnedMeshRenderer>();
            Body.bones = new[] { Head };
            Renderer = body.AddResoniteComponent<FrooxEngine.SkinnedMeshRenderer>();
            body.AddComponent<SkinnedMeshRendererConverter>().Target = Body;
        }

        public void Apply(bool initial = true) => ApplyTo(Head, initial);
        public void ApplyTo(Transform head, bool initial = true, IConversionContext context = null) =>
            type.GetMethod("Apply").Invoke(helper, new object[] { root.transform, head, initial, context ?? new SceneConverter() });

        public void Dispose()
        {
            type.GetMethod("Clear").Invoke(helper, null);
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
