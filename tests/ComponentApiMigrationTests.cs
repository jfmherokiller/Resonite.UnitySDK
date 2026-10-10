#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class ComponentApiMigrationTests
{
    static readonly Dictionary<Type, string> VariantTypes = new Dictionary<Type, string>
    {
        { typeof(AssetMultiplexerMaterialWrapper), "[FrooxEngine]FrooxEngine.AssetMultiplexer<[FrooxEngine]FrooxEngine.Material>" },
        { typeof(BooleanAssetDriverMaterialWrapper), "[FrooxEngine]FrooxEngine.BooleanAssetDriver<[FrooxEngine]FrooxEngine.Material>" },
        { typeof(BooleanValueDriverBoolWrapper), "[FrooxEngine]FrooxEngine.BooleanValueDriver<bool>" },
        { typeof(BooleanValueDriverFloatWrapper), "[FrooxEngine]FrooxEngine.BooleanValueDriver<float>" },
        { typeof(ButtonValueCycleIntWrapper), "[FrooxEngine]FrooxEngine.ButtonValueCycle<int>" },
        { typeof(ButtonValueSetFloatWrapper), "[FrooxEngine]FrooxEngine.ButtonValueSet<float>" },
        { typeof(ButtonValueSetIntWrapper), "[FrooxEngine]FrooxEngine.ButtonValueSet<int>" },
        { typeof(ContinuousPuppetSliderWrapper), "[FrooxEngine]FrooxEngine.UIX.Slider<float>" },
        { typeof(ProtoFluxFieldDriveFloat3Wrapper), "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDrive<float3>" },
        { typeof(ProtoFluxFieldDriveFloatQWrapper), "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueFieldDrive<floatQ>" },
        { typeof(ProtoFluxFieldDriveProxyFloat3Wrapper), "[FrooxEngine]FrooxEngine.ProtoFlux.CoreNodes.FieldDriveBase<float3>+Proxy" },
        { typeof(ProtoFluxFieldDriveProxyFloatQWrapper), "[FrooxEngine]FrooxEngine.ProtoFlux.CoreNodes.FieldDriveBase<floatQ>+Proxy" },
        { typeof(ProtoFluxGlobalButtonWrapper), "[FrooxEngine]FrooxEngine.ProtoFlux.GlobalReference<[FrooxEngine]FrooxEngine.IButton>" },
        { typeof(ProtoFluxGlobalValueBoolWrapper), "[FrooxEngine]FrooxEngine.ProtoFlux.GlobalReference<[FrooxEngine]FrooxEngine.IValue<bool>>" },
        { typeof(ProtoFluxGlobalValueIntWrapper), "[FrooxEngine]FrooxEngine.ProtoFlux.GlobalReference<[FrooxEngine]FrooxEngine.IValue<int>>" },
        { typeof(ProtoFluxSlotInputWrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.RefObjectInput<[FrooxEngine]FrooxEngine.Slot>" },
        { typeof(ProtoFluxValueInputBoolWrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<bool>" },
        { typeof(ProtoFluxValueInputFloat3Wrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<float3>" },
        { typeof(ProtoFluxValueInputFloatQWrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<floatQ>" },
        { typeof(ProtoFluxValueInputFloatWrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<float>" },
        { typeof(ProtoFluxValueInputIntWrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueInput<int>" },
        { typeof(ProtoFluxValueLerpFloat3Wrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.ValueLerp<float3>" },
        { typeof(ProtoFluxValueSourceBoolWrapper), "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueSource<bool>" },
        { typeof(ProtoFluxValueSourceIntWrapper), "[ProtoFluxBindings]FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ValueSource<int>" },
        { typeof(ProtoFluxValueWriteBoolWrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueWrite<[FrooxEngine]FrooxEngine.ProtoFlux.FrooxEngineContext,bool>" },
        { typeof(ProtoFluxValueWriteIntWrapper), "[ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ValueWrite<[FrooxEngine]FrooxEngine.ProtoFlux.FrooxEngineContext,int>" },
        { typeof(ValueCopyBoolWrapper), "[FrooxEngine]FrooxEngine.ValueCopy<bool>" },
        { typeof(ValueCopyFloat3Wrapper), "[FrooxEngine]FrooxEngine.ValueCopy<float3>" },
        { typeof(ValueCopyFloatQWrapper), "[FrooxEngine]FrooxEngine.ValueCopy<floatQ>" },
        { typeof(ValueCopyFloatWrapper), "[FrooxEngine]FrooxEngine.ValueCopy<float>" },
        { typeof(ValueCopyIntWrapper), "[FrooxEngine]FrooxEngine.ValueCopy<int>" },
        { typeof(ValueGradientDriverBoolWrapper), "[FrooxEngine]FrooxEngine.ValueGradientDriver<bool>" },
        { typeof(ValueGradientDriverFloatWrapper), "[FrooxEngine]FrooxEngine.ValueGradientDriver<float>" },
        { typeof(ValueGradientDriverIntWrapper), "[FrooxEngine]FrooxEngine.ValueGradientDriver<int>" },
        { typeof(ValueMultiDriverBoolWrapper), "[FrooxEngine]FrooxEngine.ValueMultiDriver<bool>" },
        { typeof(ValueMultiDriverFloatWrapper), "[FrooxEngine]FrooxEngine.ValueMultiDriver<float>" },
        { typeof(ValueMultiDriverIntWrapper), "[FrooxEngine]FrooxEngine.ValueMultiDriver<int>" },
        { typeof(ValueMultiplexerBoolWrapper), "[FrooxEngine]FrooxEngine.ValueMultiplexer<bool>" },
        { typeof(ValueMultiplexerFloatWrapper), "[FrooxEngine]FrooxEngine.ValueMultiplexer<float>" },
    };

    [Test]
    public void GenericVariantsKeepClosedNativeTypeNamesAndSerialize()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var host = new GameObject("Binding variants");
            SceneManager.MoveGameObjectToScene(host, scene);
            foreach (var pair in VariantTypes)
            {
                Assert.IsTrue(pair.Key.IsSerializable, pair.Key.Name);
                Assert.IsTrue(typeof(ResoniteObject).IsAssignableFrom(pair.Key), pair.Key.Name);
                var container = host.AddComponent<ResoniteComponent>();
                container.Data = (ResoniteObject)Activator.CreateInstance(pair.Key);
                Assert.AreEqual(pair.Value, container.TypeName, pair.Key.Name);
                var restored = host.AddComponent<ResoniteComponent>();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(container), restored);
                Assert.AreEqual(pair.Key, restored.Data.GetType(), pair.Key.Name);
                Assert.AreEqual(pair.Value, restored.TypeName, pair.Key.Name);
                Object.DestroyImmediate(restored);
                Object.DestroyImmediate(container);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void HelpersReuseBindingsAndRecreateDestroyedContainers()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var host = new GameObject("Helpers");
            SceneManager.MoveGameObjectToScene(host, scene);
            var first = ConverterComponentHelper.GetOrAdd<ValueCopyFloatWrapper>(host);
            first.Binding.WriteBack = true;
            var second = ConverterComponentHelper.GetOrAdd<ValueCopyFloatWrapper>(host);
            Assert.AreSame(first.Container, second.Container);
            Assert.IsTrue(second.Binding.WriteBack);
            var integer = ConverterComponentHelper.GetOrAdd<ValueCopyIntWrapper>(host);
            Assert.AreNotSame(first.Container, integer.Container);
            Object.DestroyImmediate(first.Container);
            var recreated = ConverterComponentHelper.GetOrAdd<ValueCopyFloatWrapper>(host);
            Assert.IsNotNull(recreated.Binding);
            Assert.IsFalse(recreated.Binding.WriteBack);
            ConverterComponentHelper.Remove(ref recreated);
            Assert.IsTrue(recreated == null);
            Assert.IsNotNull(integer.Binding);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void SingleComponentConverterRecoversMissingUpgradeReference() => AssertConverterRecovers(false);

    [Test]
    public void SingleComponentConverterRecoversDestroyedContainer() => AssertConverterRecovers(true);

    static void AssertConverterRecovers(bool destroyContainer)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var host = new GameObject("Upgrade recovery");
            SceneManager.MoveGameObjectToScene(host, scene);
            var source = host.AddComponent<BoxCollider>();
            source.size = new Vector3(2, 3, 4);
            var converter = host.AddComponent<BoxColliderConverter>();
            if (destroyContainer)
            {
                converter.Initialize(source);
                Object.DestroyImmediate(converter.Container);
            }
            else
                converter.Target = source;

            converter.UpdateConversion(null);
            Assert.IsTrue(converter.Container != null);
            Assert.AreEqual(source.size, converter.Binding.Size);
            var container = converter.Container;
            converter.UpdateConversion(null);
            Assert.AreSame(container, converter.Container);
            Assert.AreEqual(1, host.GetComponents<ResoniteComponent>().Length);
            Object.DestroyImmediate(converter);
            Assert.IsTrue(container == null);
            Assert.IsTrue(source != null);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void EnsureOnMovesAndRemovesOnlyItsOwnedContainer()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var first = new GameObject("First");
            var second = new GameObject("Second");
            SceneManager.MoveGameObjectToScene(first, scene);
            SceneManager.MoveGameObjectToScene(second, scene);
            ResRef<FrooxEngine.VirtualParent> owned = default;
            ConverterComponentHelper.EnsureOn(ref owned, first);
            var original = owned.Container;
            ConverterComponentHelper.EnsureOn(ref owned, first);
            Assert.AreSame(original, owned.Container);
            var unrelated = first.AddResoniteComponent<FrooxEngine.LookAt>();
            ConverterComponentHelper.EnsureOn(ref owned, second);
            Assert.IsTrue(original == null);
            Assert.AreSame(second, owned.Container.gameObject);
            Assert.IsNotNull(unrelated.Binding);
            ConverterComponentHelper.EnsureOn(ref owned, null);
            Assert.IsTrue(owned == null);
            Assert.AreEqual(0, second.GetComponents<ResoniteComponent>().Length);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
#endif
