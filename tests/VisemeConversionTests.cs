#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class VisemeConversionTests
{
    static readonly string[] SharedShapes = {
        "aa", "ou", "ou", "oh", "E", "aa", "ih", "ih", "E", "E", "aa", "E", "ih", "oh", "ou"
    };

    [Test]
    public void SilenceDoesNotDriveASharedSpeechShape()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Convert();
            CollectionAssert.DoesNotContain(fixture.SourcesFor("aa"), FrooxEngine.Viseme.Silence,
                "Silence must not drive a shape also used for speech.");
        }
    }

    [Test]
    public void SharedSpeechChannelsArePreserved()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Convert();
            CollectionAssert.AreEquivalent(new[] { FrooxEngine.Viseme.kk, FrooxEngine.Viseme.aa }, fixture.SourcesFor("aa"));
            CollectionAssert.AreEquivalent(new[] { FrooxEngine.Viseme.PP, FrooxEngine.Viseme.FF, FrooxEngine.Viseme.ou }, fixture.SourcesFor("ou"));
            CollectionAssert.AreEquivalent(new[] { FrooxEngine.Viseme.TH, FrooxEngine.Viseme.oh }, fixture.SourcesFor("oh"));
            CollectionAssert.AreEquivalent(new[] { FrooxEngine.Viseme.DD, FrooxEngine.Viseme.nn, FrooxEngine.Viseme.RR, FrooxEngine.Viseme.E }, fixture.SourcesFor("E"));
            CollectionAssert.AreEquivalent(new[] { FrooxEngine.Viseme.CH, FrooxEngine.Viseme.SS, FrooxEngine.Viseme.ih }, fixture.SourcesFor("ih"));
        }
    }

    [Test]
    public void DedicatedSilenceAndUniqueSpeechShapesArePreserved()
    {
        var names = Enum.GetValues(typeof(FrooxEngine.Viseme)).Cast<FrooxEngine.Viseme>()
            .Where(v => (int)v < 15).Select(v => "shape_" + v).ToArray();
        using (var fixture = new Fixture(names))
        {
            fixture.Convert();
            for (int i = 0; i < names.Length; i++)
                CollectionAssert.AreEqual(new[] { (FrooxEngine.Viseme)i }, fixture.SourcesFor(names[i]));
            Assert.AreEqual(15, fixture.Dynamic.Drivers.Count);
        }
    }

    [Test]
    public void SharedShapesHaveOneWriterAndFullStrength()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Convert();
            var driver = fixture.Dynamic;
            Assert.AreEqual(5, driver.Drivers.Count);
            Assert.AreEqual(5, driver.Drivers.Data.Select(d => d.Target).Distinct().Count());
            Assert.IsTrue(driver.Drivers.Data.All(d => d.Target != null));
            Assert.AreEqual(14, driver.Drivers.Data.Sum(d => d.Sources.Count));
            Assert.AreEqual(1f, driver.StrengthMultiplier);
            Assert.IsTrue(driver.Drivers.Data.SelectMany(d => d.Sources.Data).All(s => s.Influence == 1f));
            Assert.IsNotNull(driver.Source);
            var host = fixture.Host;
            var assigner = host.GetResoniteComponent<FrooxEngine.CommonAvatar.AvatarVoiceSourceAssigner>().Binding;
            Assert.AreSame(host.GetResoniteComponent<FrooxEngine.VisemeAnalyzer>().Binding.Source_Element.Member, assigner.TargetReference);
            var filter = host.GetComponent<ResoniteMemberFilter>();
            CollectionAssert.Contains(filter.Entries.Single(e => e.Component == host.GetResoniteComponent<FrooxEngine.DynamicVisemeDriver>().Container).Members, "Drivers");
            CollectionAssert.Contains(filter.Entries.Single(e => e.Component == host.GetResoniteComponent<FrooxEngine.VisemeAnalyzer>().Container).Members, "Source");
        }
    }

    [Test]
    public void RepeatedConversionReusesDriversAndDisablesOldSources()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Convert();
            var driver = fixture.Dynamic;
            var first = driver.Drivers.GetElement(0);
            var source = first.Sources.GetElement(0);
            fixture.Convert();
            Assert.AreSame(driver, fixture.Dynamic);
            Assert.AreSame(first, driver.Drivers.GetElement(0));
            Assert.AreSame(source, first.Sources.GetElement(0));
            var replacement = new string[15];
            replacement[10] = "E";
            fixture.SetShapes(replacement);
            fixture.Convert();
            Assert.AreEqual(1, driver.Drivers.Data.Count(d => d.Target != null));
            Assert.AreEqual(1, first.Sources.Data.Count(s => s.Influence != 0f));
            CollectionAssert.AreEqual(new[] { FrooxEngine.Viseme.aa }, fixture.SourcesFor("E"));
            Assert.IsEmpty(fixture.SourcesFor("aa"));
        }
    }

    [Test]
    public void ConversionRemovesLegacyDirectDriver()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Convert();
            fixture.Host.AddResoniteComponent<FrooxEngine.DirectVisemeDriver>();
            fixture.Convert();
            Assert.AreEqual(0, fixture.Host.GetResoniteComponents<FrooxEngine.DirectVisemeDriver>().Count);
            Assert.AreEqual(1, fixture.Host.GetResoniteComponents<FrooxEngine.DynamicVisemeDriver>().Count);
        }
    }

    [Test]
    public void MissingShapesRemoveTheGeneratedDriver()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Convert();
            fixture.SetShapes(Enumerable.Repeat("missing", 15).ToArray());
            fixture.Convert();
            Assert.IsTrue(fixture.Host == null);
        }
    }

    [Test]
    public void JawFlapUsesSmoothedVolumeInsteadOfOnePhoneme()
    {
        using (var fixture = new Fixture(new[] { "jaw" }))
        {
            fixture.SetMode("JawFlapBlendShape", "jaw");
            var context = new ExportContext();
            fixture.Convert(context);
            var meter = fixture.Host.GetResoniteComponent<FrooxEngine.VolumeMeter>();
            Assert.IsNotNull(meter.Binding);
            var copy = fixture.Host.GetResoniteComponent<ValueCopyFloatWrapper>();
            var assigner = fixture.Host.GetResoniteComponent<FrooxEngine.CommonAvatar.AvatarVoiceSourceAssigner>();
            Assert.AreEqual(0.8f, meter.Binding.Smoothing);
            Assert.AreEqual(0.5f, meter.Binding.Power);
            Assert.AreEqual(FrooxEngine.VolumeMeterMethod.Magnitude, meter.Binding.Method);
            Assert.AreSame(meter.Binding.Volume_Element.Member, copy.Binding.Source);
            Assert.AreSame(fixture.Renderer.Binding.BlendShapeWeights.GetElement(0).Member, copy.Binding.Target);
            Assert.IsFalse(copy.Binding.WriteBack);
            Assert.AreSame(meter.Binding.Source_Element.Member, assigner.Binding.TargetReference);
            Assert.IsEmpty(fixture.Host.GetResoniteComponents<FrooxEngine.DynamicVisemeDriver>());
            var meterData = meter.Container.CollectData(context);
            ResoniteMemberFilter.Apply(meter.Container, meterData);
            var copyData = copy.Container.CollectData(context);
            ResoniteMemberFilter.Apply(copy.Container, copyData);
            Assert.AreEqual(meterData.Members["Volume"].ID, ((ResoniteLink.Reference)copyData.Members["Source"]).TargetID);
            Assert.IsTrue(meterData.Members.ContainsKey("Source"));
            fixture.Convert(context);
            Assert.AreSame(meter.Binding, fixture.Host.GetResoniteComponent<FrooxEngine.VolumeMeter>().Binding);
            Assert.AreSame(copy.Binding, fixture.Host.GetResoniteComponent<ValueCopyFloatWrapper>().Binding);
            Assert.AreEqual(1, fixture.Host.GetResoniteComponents<ValueCopyFloatWrapper>().Count);
        }
    }

    [Test]
    public void JawFlapModeChangesRemoveCompetingDriversAndMissingShapes()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Convert();
            fixture.SetMode("JawFlapBlendShape", "aa");
            fixture.Convert();
            Assert.IsEmpty(fixture.Host.GetResoniteComponents<FrooxEngine.DynamicVisemeDriver>());
            Assert.IsEmpty(fixture.Host.GetResoniteComponents<FrooxEngine.VisemeAnalyzer>());
            Assert.AreEqual(1, fixture.Host.GetResoniteComponents<FrooxEngine.VolumeMeter>().Count);
            fixture.SetMode("VisemeBlendShape");
            fixture.Convert();
            Assert.IsEmpty(fixture.Host.GetResoniteComponents<FrooxEngine.VolumeMeter>());
            Assert.IsEmpty(fixture.Host.GetResoniteComponents<ValueCopyFloatWrapper>());
            Assert.AreEqual(1, fixture.Host.GetResoniteComponents<FrooxEngine.DynamicVisemeDriver>().Count);
            fixture.SetMode("JawFlapBlendShape", "missing");
            fixture.Convert();
            Assert.IsTrue(fixture.Host == null);
        }
    }

    [Test]
    public void JawFlapIgnoresObsoleteDeferredTargets()
    {
        using (var fixture = new Fixture(new[] { "jaw", "replacement" }))
        {
            fixture.SetMode("JawFlapBlendShape", "jaw");
            var rendererConverter = fixture.Renderer.Container.GetComponent<SkinnedMeshRendererConverter>();
            rendererConverter.Target = null;
            var context = new SceneConverter();
            fixture.Convert(context);
            var actions = ((System.Collections.Generic.Dictionary<Component, System.Collections.Generic.List<Action>>)
                typeof(SceneConverter).GetField("_deferedActions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context))
                .SelectMany(pair => pair.Value).ToArray();
            Assert.AreEqual(1, actions.Length);
            rendererConverter.Target = fixture.Renderer.Container.GetComponent<SkinnedMeshRenderer>();
            fixture.SetMode("JawFlapBlendShape", "replacement");
            fixture.Convert();
            foreach (var action in actions) action();
            var copy = fixture.Host.GetResoniteComponent<ValueCopyFloatWrapper>().Binding;
            Assert.IsNotNull(copy);
            Assert.AreSame(fixture.Renderer.Binding.BlendShapeWeights.GetElement(1).Member, copy.Target);
        }
    }

    [Test]
    public void DefaultModeFindsConventionalShapes()
    {
        using (var fixture = new Fixture(new[] { "vrc.v_sil", "vrc.v_aa" }))
        {
            fixture.SetShapes(new string[15]);
            fixture.SetMode("Default");
            fixture.Convert();
            CollectionAssert.AreEqual(new[] { FrooxEngine.Viseme.Silence }, fixture.SourcesFor("vrc.v_sil"));
            CollectionAssert.AreEqual(new[] { FrooxEngine.Viseme.aa }, fixture.SourcesFor("vrc.v_aa"));
        }
    }

    [Test]
    public void ObsoleteDeferredCallbacksCannotRetargetCurrentDrivers()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            fixture.Renderer.Container.GetComponent<SkinnedMeshRendererConverter>().Target = null;
            var context = new SceneConverter();
            fixture.Convert(context);
            var actions = ((System.Collections.Generic.Dictionary<Component, System.Collections.Generic.List<Action>>)
                typeof(SceneConverter).GetField("_deferedActions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context))
                .SelectMany(pair => pair.Value).ToArray();
            Assert.AreEqual(5, actions.Length);
            var replacement = new string[15];
            replacement[10] = "E";
            fixture.SetShapes(replacement);
            fixture.Renderer.Container.GetComponent<SkinnedMeshRendererConverter>().Target = fixture.Renderer.Container.GetComponent<SkinnedMeshRenderer>();
            fixture.Convert();
            foreach (var action in actions) action();
            CollectionAssert.AreEqual(new[] { FrooxEngine.Viseme.aa }, fixture.SourcesFor("E"));
            Assert.IsEmpty(fixture.SourcesFor("aa"));
        }
    }

    [Test]
    public void ShrinkingMappingsExplicitlyNeutralizesEveryExportedEntry()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            var context = new ExportContext();
            fixture.Convert(context);
            var before = ExportDrivers(fixture.Dynamic, context);
            var replacement = new string[15];
            replacement[10] = "E";
            fixture.SetShapes(replacement);
            fixture.Convert(context);
            var after = ExportDrivers(fixture.Dynamic, context);
            CollectionAssert.AreEqual(before.Elements.Select(e => e.ID), after.Elements.Select(e => e.ID),
                "ResoniteLink does not truncate omitted trailing list entries.");
            for (int i = 0; i < before.Elements.Count; i++)
            {
                var oldEntry = (ResoniteLink.SyncObject)before.Elements[i];
                var entry = (ResoniteLink.SyncObject)after.Elements[i];
                var oldSources = (ResoniteLink.SyncList)oldEntry.Members["Sources"];
                var sources = (ResoniteLink.SyncList)entry.Members["Sources"];
                CollectionAssert.AreEqual(oldSources.Elements.Select(e => e.ID), sources.Elements.Select(e => e.ID));
                var target = (ResoniteLink.Reference)entry.Members["Target"];
                if (i == 0)
                    Assert.AreEqual(context.GetIdOrAllocate(fixture.Renderer.Binding.BlendShapeWeights.GetElement(3).Member), target.TargetID);
                else
                    Assert.IsTrue(string.IsNullOrEmpty(target.TargetID), "Unused targets must explicitly disconnect.");
                for (int j = 0; j < sources.Elements.Count; j++)
                {
                    var source = (ResoniteLink.SyncObject)sources.Elements[j];
                    Assert.AreEqual(i == 0 && j == 0 ? 1f : 0f,
                        ((ResoniteLink.Field)source.Members["Influence"]).BoxedValue);
                }
            }
            fixture.SetShapes(SharedShapes);
            fixture.Convert(context);
            CollectionAssert.AreEquivalent(new[] { FrooxEngine.Viseme.kk, FrooxEngine.Viseme.aa }, fixture.SourcesFor("aa"));
            Assert.AreEqual(14, fixture.Dynamic.Drivers.Data.Sum(d => d.Sources.Data.Count(s => s.Influence == 1f)));
        }
    }

    [Test]
    public void ExportedBindingsReferenceTheAnalyzerAndItsExportedSourceField()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            var context = new ExportContext();
            fixture.Convert(context);
            var analyzer = fixture.Host.GetResoniteComponent<FrooxEngine.VisemeAnalyzer>();
            var driver = fixture.Host.GetResoniteComponent<FrooxEngine.DynamicVisemeDriver>();
            var assigner = fixture.Host.GetResoniteComponent<FrooxEngine.CommonAvatar.AvatarVoiceSourceAssigner>();
            Assert.AreSame(fixture.Dynamic, driver.Container.Data);
            Assert.AreEqual("[FrooxEngine]FrooxEngine.DynamicVisemeDriver", driver.Container.TypeName);
            var analyzerData = analyzer.Container.CollectData(context);
            ResoniteMemberFilter.Apply(analyzer.Container, analyzerData);
            var driverData = driver.Container.CollectData(context);
            ResoniteMemberFilter.Apply(driver.Container, driverData);
            var assignerData = assigner.Container.CollectData(context);
            Assert.AreEqual(analyzerData.ID, ((ResoniteLink.Reference)driverData.Members["Source"]).TargetID);
            Assert.AreEqual(analyzerData.Members["Source"].ID, ((ResoniteLink.Reference)assignerData.Members["TargetReference"]).TargetID);
            var entries = (ResoniteLink.SyncList)driverData.Members["Drivers"];
            Assert.IsTrue(entries.Elements.Cast<ResoniteLink.SyncObject>().All(e =>
                context.TryResolveId(((ResoniteLink.Reference)e.Members["Target"]).TargetID) is FrooxEngine.IField<float>));
        }
    }

    [Test]
    public void BlendShapeHelperReusesFieldsAndRecoversOwnedContainers()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            var renderer = fixture.Renderer.Container.GetComponent<SkinnedMeshRenderer>();
            var first = BlendShapeFieldHelper.GetField(renderer, 0);
            Assert.AreSame(first, BlendShapeFieldHelper.GetField(renderer, 0));
            Assert.AreSame(first, fixture.Renderer.Binding.BlendShapeWeights.GetElement(0).Member);
            Assert.IsNull(BlendShapeFieldHelper.GetField(renderer, -1));
            Object.DestroyImmediate(fixture.Renderer.Container);
            var converter = renderer.GetComponent<SkinnedMeshRendererConverter>();
            var recovered = BlendShapeFieldHelper.GetField(renderer, 0);
            Assert.IsNotNull(recovered);
            Assert.AreNotSame(first, recovered);
            Assert.AreSame(converter.Binding.BlendShapeWeights.GetElement(0).Member, recovered);
            Object.DestroyImmediate(converter);
            Assert.IsNull(BlendShapeFieldHelper.GetField(renderer, 0));
            Assert.IsNull(BlendShapeFieldHelper.GetField(null, 0));
        }
    }

    [Test]
    public void BodySideLabelsCannotBecomeAutomaticSpeechTargets()
    {
        using (var fixture = new Fixture(SharedShapes))
        {
            var body = new GameObject("Body shrink shapes");
            SceneManager.MoveGameObjectToScene(body, fixture.Root.scene);
            body.transform.SetParent(fixture.Root.transform, false);
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                var names = new[] { "Foot.R", "Foot.Right", "UpperArm.R", "Blink.R", "Foot.L", "Chest" };
                foreach (var name in names)
                    mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.right, Vector3.zero, Vector3.zero }, null, null);
                var renderer = body.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                var converter = body.AddComponent<SkinnedMeshRendererConverter>();
                converter.Initialize(renderer);
                var context = new ExportContext();
                converter.UpdateConversion(context);
                var processor = context.MeshPostProcessor;
                var payload = MeshConverter.ConvertMesh(mesh);
                processor?.Process(payload);
                var exported = payload.BlendShapes.Select(s => s.Name).ToArray();
                Assert.IsFalse(exported.Any(n => System.Text.RegularExpressions.Regex.Split(n.ToLowerInvariant(), @"[^\p{L}]+").Contains("r")),
                    "Native DirectVisemeDriver matches isolated r to RR, including Foot.R.");
                Assert.AreEqual(names.Length, exported.Distinct().Count(), "Expanded labels must not collide.");
                Assert.AreEqual("Foot.Right", exported[1]);
                Assert.AreEqual("UpperArm.Right", exported[2]);
                Assert.AreEqual("Blink.Right", exported[3]);
                Assert.AreEqual("Foot.L", exported[4]);
                Assert.AreEqual("Chest", exported[5]);
                CollectionAssert.AreEqual(names, Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName));
                Assert.AreEqual(names.Length, converter.Binding.BlendShapeWeights.Count);
                for (int i = 0; i < names.Length; i++)
                {
                    Assert.AreEqual(100, payload.BlendShapes[i].Frames[0].Position);
                    Assert.AreEqual(1f, payload.BlendShapes[i].Frames[0].PositionDeltas[0].x);
                }
                converter.UpdateConversion(context);
                Assert.AreSame(processor, context.MeshPostProcessor, "Repeated conversion must reuse its asset cache key.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }

    [Test]
    public void ExplicitSpeechNamesAndWeightReferencesSurviveNameProtection()
    {
        var names = new[] { "Foot.R", "vrc.v_r", "r" };
        using (var fixture = new Fixture(names))
        {
            var converter = fixture.Renderer.Container.GetComponent<SkinnedMeshRendererConverter>();
            converter.Container = fixture.Renderer.Container;
            var context = new ExportContext();
            converter.UpdateConversion(context);
            var firstProcessor = context.MeshPostProcessor;
            var mesh = ((SkinnedMeshRenderer)converter.Target).sharedMesh;
            var payload = MeshConverter.ConvertMesh(mesh);
            firstProcessor.Process(payload);
            CollectionAssert.AreEqual(names, payload.BlendShapes.Select(s => s.Name));
            fixture.Convert(context);
            for (int i = 0; i < names.Length; i++)
                CollectionAssert.AreEqual(new[] { (FrooxEngine.Viseme)i }, fixture.SourcesFor(names[i]));
            var fields = fixture.Renderer.Binding.BlendShapeWeights.Data.ToArray();
            converter.UpdateConversion(context);
            Assert.AreSame(firstProcessor, context.MeshPostProcessor);

            var replacement = new string[15];
            replacement[10] = "r";
            fixture.SetShapes(replacement);
            converter.UpdateConversion(context);
            Assert.AreNotSame(firstProcessor, context.MeshPostProcessor, "Changed protection needs a new asset cache key.");
            payload = MeshConverter.ConvertMesh(mesh);
            context.MeshPostProcessor.Process(payload);
            CollectionAssert.AreEqual(new[] { "Foot.Right", "vrc.v_r", "r" }, payload.BlendShapes.Select(s => s.Name));
            context.MeshPostProcessor.Process(payload);
            CollectionAssert.AreEqual(new[] { "Foot.Right", "vrc.v_r", "r" }, payload.BlendShapes.Select(s => s.Name));
            CollectionAssert.AreEqual(names, Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName));
            for (int i = 0; i < fields.Length; i++)
                Assert.AreSame(fields[i], fixture.Renderer.Binding.BlendShapeWeights.Data[i]);
        }
    }

    [Test]
    public void NonAvatarMeshesDoNotReceiveAvatarNameProtection()
    {
        using (var fixture = new Fixture(new[] { "Foot.R" }))
        {
            var descriptor = fixture.Root.GetComponents<Component>().Single(c => c.GetType().FullName == VRChatTypes.AvatarDescriptor);
            Object.DestroyImmediate(descriptor);
            var converter = fixture.Renderer.Container.GetComponent<SkinnedMeshRendererConverter>();
            converter.Container = fixture.Renderer.Container;
            var context = new ExportContext();
            converter.UpdateConversion(context);
            Assert.IsNull(context.MeshPostProcessor);
            Assert.AreEqual("Foot.R", ((SkinnedMeshRenderer)converter.Target).sharedMesh.GetBlendShapeName(0));
        }
    }

    static ResoniteLink.SyncList ExportDrivers(FrooxEngine.DynamicVisemeDriver driver, IConversionContext context)
    {
        var members = new System.Collections.Generic.Dictionary<string, ResoniteLink.Member>();
        driver.CollectMembers(members, context);
        return (ResoniteLink.SyncList)members["Drivers"];
    }

    sealed class ExportContext : IConversionContext
    {
        public AssetMessagePostProcessor MeshPostProcessor;
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
        public FrooxEngine.IAssetProvider<FrooxEngine.Mesh> GetMesh(Mesh mesh, AssetMessagePostProcessor p = null)
        {
            MeshPostProcessor = p;
            return null;
        }
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
        readonly Scene preview;
        readonly GameObject root;
        readonly Mesh mesh;
        readonly Component descriptor;
        readonly VRCAvatarDescriptorConverter converter;
        public GameObject Root => root;
        public readonly ResRef<FrooxEngine.SkinnedMeshRenderer> Renderer;
        public GameObject Host { get { return converter.Visemes; } }
        public FrooxEngine.DynamicVisemeDriver Dynamic { get { return Host.GetResoniteComponent<FrooxEngine.DynamicVisemeDriver>().Binding; } }
        public void SetShapes(string[] shapes) { descriptor.GetType().GetField("VisemeBlendShapes").SetValue(descriptor, shapes); }
        public FrooxEngine.Viseme[] SourcesFor(string shape)
        {
            var field = Renderer.Binding.BlendShapeWeights.GetElement(mesh.GetBlendShapeIndex(shape)).Member;
            var dynamic = converter.Visemes.GetResoniteComponent<FrooxEngine.DynamicVisemeDriver>();
            if (dynamic.Container != null)
                return dynamic.Binding.Drivers.Data.Where(d => Equals(d.Target, field))
                    .SelectMany(d => d.Sources.Data).Where(s => s.Influence != 0f).Select(s => s.Viseme).ToArray();
            var direct = converter.Visemes.GetResoniteComponent<FrooxEngine.DirectVisemeDriver>().Binding;
            return Enum.GetValues(typeof(FrooxEngine.Viseme)).Cast<FrooxEngine.Viseme>()
                .Where(v => (int)v < 15 && Equals(direct.GetType().GetProperty(v.ToString()).GetValue(direct), field)).ToArray();
        }

        public Fixture(string[] shapes)
        {
            var descriptorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"))
                .FirstOrDefault(t => t != null);
            if (descriptorType == null) Assert.Ignore("This integration fixture requires the VRChat Avatars SDK.");
            preview = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Viseme conversion fixture") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, preview);
            var body = new GameObject("Body");
            SceneManager.MoveGameObjectToScene(body, preview);
            body.transform.SetParent(root.transform, false);
            mesh = new Mesh { name = "Synthetic visemes", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            foreach (var name in shapes.Where(s => !string.IsNullOrEmpty(s)).Distinct())
                mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.zero, Vector3.zero, Vector3.down }, null, null);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            Renderer = body.AddResoniteComponent<FrooxEngine.SkinnedMeshRenderer>();
            var rendererConverter = body.AddComponent<SkinnedMeshRendererConverter>();
            rendererConverter.Target = renderer;
            rendererConverter.Container = Renderer.Container;
            descriptor = root.AddComponent(descriptorType);
            var mode = descriptorType.GetField("lipSync");
            mode.SetValue(descriptor, Enum.Parse(mode.FieldType, "VisemeBlendShape"));
            descriptorType.GetField("VisemeSkinnedMesh").SetValue(descriptor, renderer);
            descriptorType.GetField("VisemeBlendShapes").SetValue(descriptor, shapes.ToArray());
            converter = root.AddComponent<VRCAvatarDescriptorConverter>();
        }

        public void SetMode(string mode, string mouth = "")
        {
            var field = descriptor.GetType().GetField("lipSync");
            field.SetValue(descriptor, Enum.Parse(field.FieldType, mode));
            descriptor.GetType().GetField("MouthOpenBlendShapeName").SetValue(descriptor, mouth);
        }

        public void Convert(IConversionContext context = null)
        {
            typeof(VRCAvatarDescriptorConverter).GetMethod("SetupVisemes", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(converter, new object[] { descriptor, context ?? new SceneConverter() });
        }

        public void Dispose()
        {
            foreach (var component in root.GetComponentsInChildren<ResoniteComponentConverter>(true))
                if (component != null)
                    Object.DestroyImmediate(component);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
#endif
