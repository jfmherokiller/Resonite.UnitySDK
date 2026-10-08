#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class ExpressionPuppetAnalyzerTests
{
    public static void RunFromCommandLine()
    {
        var suite = new ExpressionPuppetAnalyzerTests();
        try
        {
            suite.TwoAxisDiagonalSamplesBothParameters();
            suite.AxisControlIsCollectedAsOneSharedControl();
            suite.ButtonGraphConnectsPressAndRelease();
            suite.ContinuousSliderDrivesPuppetValue();
            suite.ContinuousSliderRebuildReusesItsCopy();
            suite.MenuItemOpensContinuousSlider();
            suite.RadialMenuOffersDraggableSlider();
            suite.ContinuousAxisPanelBlendsBothParameters();
            suite.ContinuousAxisPanelLabelsBothSliders();
            suite.ContinuousAxisBoolSwitchesFromBothParameters();
            suite.ContinuousAxisMaterialSwitchesFromBothParameters();
            suite.TwoAxisMenuOffersContinuousControls();
            suite.FourAxisOpposingDefaultsAreReportedAsApproximate();
            suite.FourAxisDefaultOnlyAnimationIsReported();
            suite.FourAxisDiagonalDrivesUpAndRightWithoutDrivingDownOrLeft();
            suite.TwoAxisDiagonalDrivesBothParameters();
            suite.FractionalFloatOptionsCreateDistinctSelector();
            suite.NearbyFloatOptionsCreateDistinctSelector();
            suite.MToonPannersAreRemovedWhenScrollStops();
            suite.LilToonDisabledReflectionClearsShinyFields();
            suite.LilToonReenabledReflectionRestoresHighlights();
            suite.LilToonReportsDroppedSecondaryNormalLayer();
            suite.LilToonReportsIgnoredSecondaryNormalStrengthMask();
            suite.LilToonReportsBothNormalOmissionsWithSecondaryMask();
            suite.LilToonDoesNotWarnForInactiveSecondaryNormal();
            suite.LilToonUsesSecondaryNormalWhenPrimaryTextureMissing();
            suite.ScaleConstraintBlendsBothSourcesAtPartialWeight();
            suite.LocalSpaceScaleConstraintUsesLocalSourceAtFullWeight();
            UnityEngine.Debug.Log("EXPRESSION_PUPPET_TESTS_PASS");
            EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("EXPRESSION_PUPPET_TESTS_FAIL: " + ex);
            EditorApplication.Exit(1);
        }
    }

    public enum FakeControlType { TwoAxisPuppet, FourAxisPuppet, RadialPuppet, Toggle, Button }
    public enum FakeValueType { Float }
    public class FakeParameter { public string name; public FakeValueType valueType; public float defaultValue; }
    public class FakeParameterAsset { public FakeParameter[] parameters; }
    public class FakeControl
    {
        public string name;
        public FakeControlType type;
        public FakeParameter parameter;
        public float value;
        public FakeParameter[] subParameters;
    }
    public class FakeMenu : ScriptableObject { public List<FakeControl> controls = new List<FakeControl>(); }

    [Test]
    public void AxisControlIsCollectedAsOneSharedControl()
    {
        var menu = ScriptableObject.CreateInstance<FakeMenu>();
        var owner = new GameObject("Test Owner");
        try
        {
            menu.controls.Add(new FakeControl { name = "Aim", type = FakeControlType.TwoAxisPuppet,
                subParameters = new[] { new FakeParameter { name = "Horizontal" }, new FakeParameter { name = "Vertical" } } });
            var controlType = typeof(VRCExpressionMenuToggles).GetNestedType("MenuControl",
                System.Reflection.BindingFlags.NonPublic);
            var listType = typeof(List<>).MakeGenericType(controlType);
            var controls = (System.Collections.IList)System.Activator.CreateInstance(listType);
            var method = typeof(VRCExpressionMenuToggles).GetMethod("CollectControls",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method.Invoke(null, new object[] { menu, new string[0], new HashSet<object>(), controls,
                owner.transform, new ConversionReporter() });

            Assert.AreEqual(1, controls.Count, "Both axes must be one control, not independent drivers for the same field");
            var parameters = (string[])controlType.GetField("AxisParameters").GetValue(controls[0]);
            CollectionAssert.AreEqual(new[] { "Horizontal", "Vertical" }, parameters);
        }
        finally
        {
            Object.DestroyImmediate(menu);
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void ButtonGraphConnectsPressAndRelease()
    {
        var item = new GameObject("Button Test");
        try
        {
            var source = item.AddComponent<FrooxEngine.ContextMenuItemSourceWrapper>().Data;
            var value = item.AddComponent<ValueMultiDriverBoolWrapper>().Data.Value_Element.Member;
            AvatarToggleBuilder.BuildButtonLink(item, value);
            var events = item.GetComponentInChildren<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Interaction.ButtonEventsWrapper>();
            Assert.IsNotNull(events);
            var global = item.GetComponentInChildren<ProtoFluxGlobalButtonWrapper>();
            Assert.AreSame(source, global.Data.Reference);
            Assert.AreSame(global.Data, events.Data.Button);
            Assert.AreNotSame(events.Data.Pressed, events.Data.Released);
            var writes = item.GetComponentsInChildren<ProtoFluxValueWriteBoolWrapper>();
            Assert.AreEqual(2, writes.Length);
            Assert.IsTrue(System.Array.Exists(writes, w => ReferenceEquals(w.Data, events.Data.Pressed)));
            Assert.IsTrue(System.Array.Exists(writes, w => ReferenceEquals(w.Data, events.Data.Released)));
        }
        finally { Object.DestroyImmediate(item); }
    }

    [Test]
    public void ContinuousSliderDrivesPuppetValue()
    {
        var panel = new GameObject("Continuous Puppet");
        try
        {
            var value = panel.AddComponent<ValueMultiDriverFloatWrapper>().Data.Value_Element.Member;
            var method = typeof(AvatarToggleBuilder).GetMethod("BuildContinuousSlider");
            Assert.IsNotNull(method, "Radial puppets need a draggable slider, not only discrete menu steps");
            method.Invoke(null, new object[] { panel, value, 0.4f, 0f, 1f });
            var slider = panel.GetComponentInChildren<FrooxEngine.UIX.SliderWrapper<float>>();
            Assert.IsNotNull(slider);
            Assert.AreEqual(0.4f, slider.Data.Value);
            Assert.AreEqual(0f, slider.Data.Min);
            Assert.AreEqual(1f, slider.Data.Max);
            var copy = panel.GetComponentInChildren<ValueCopyFloatWrapper>();
            Assert.IsNotNull(copy);
            Assert.AreSame(slider.Data.Value_Element.Member, copy.Data.Source);
            Assert.AreSame(value, copy.Data.Target);
            Assert.IsNotNull(panel.GetComponent<FrooxEngine.UIX.CanvasWrapper>());
        }
        finally { Object.DestroyImmediate(panel); }
    }

    [Test]
    public void ContinuousSliderRebuildReusesItsCopy()
    {
        var panel = new GameObject("Slider Panel");
        try
        {
            var state = panel.AddComponent<ValueMultiDriverFloatWrapper>().Data.Value_Element.Member;
            AvatarToggleBuilder.BuildContinuousSlider(panel, state, 0.3f, 0, 1);
            AvatarToggleBuilder.BuildContinuousSlider(panel, state, 0.3f, 0, 1);
            Assert.AreEqual(1, panel.GetComponentsInChildren<ValueCopyFloatWrapper>().Length,
                "A realtime update must not add another driver to the same field");
        }
        finally { Object.DestroyImmediate(panel); }
    }

    [Test]
    public void MenuItemOpensContinuousSlider()
    {
        var menu = new GameObject("Menu Item");
        var panel = new GameObject("Slider Panel");
        try
        {
            menu.AddComponent<FrooxEngine.ContextMenuItemSourceWrapper>();
            var method = typeof(AvatarToggleBuilder).GetMethod("BuildContinuousSliderToggle");
            Assert.IsNotNull(method, "The draggable slider must be reachable from the expression menu");
            method.Invoke(null, new object[] { menu, panel });
            Assert.IsFalse(panel.activeSelf);
            var toggle = menu.GetComponent<FrooxEngine.ButtonToggleWrapper>();
            Assert.IsNotNull(toggle);
            Assert.AreEqual(new SlotActiveField(panel.transform), toggle.Data.TargetValue);
        }
        finally { Object.DestroyImmediate(menu); Object.DestroyImmediate(panel); }
    }

    [Test]
    public void RadialMenuOffersDraggableSlider()
    {
        var root = new GameObject("Avatar");
        var target = new GameObject("Target");
        target.transform.SetParent(root.transform);
        var menu = ScriptableObject.CreateInstance<FakeMenu>();
        var controller = new AnimatorController();
        var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "Dial" };
        var clip = new AnimationClip();
        var items = new List<GameObject>();
        var selectors = new List<GameObject>();
        try
        {
            controller.AddParameter("Dial", AnimatorControllerParameterType.Float);
            controller.AddLayer("FX");
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Target", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 1, 1));
            tree.AddChild(clip, 0);
            tree.AddChild(clip, 1);
            controller.layers[0].stateMachine.AddState("Pose").motion = tree;
            menu.controls.Add(new FakeControl { name = "Dial", type = FakeControlType.RadialPuppet,
                subParameters = new[] { new FakeParameter { name = "Dial" } } });
            var source = new ExpressionMenuSource { ResolvePath = path => path == "Target" ? target.transform : root.transform };
            source.Menus.Add((menu, new string[0]));
            source.Parameters.Add(new FakeParameterAsset { parameters = new[] {
                new FakeParameter { name = "Dial", valueType = FakeValueType.Float, defaultValue = 0.4f } } });
            source.Controllers.Add(controller);
            VRCExpressionMenuToggles.Apply(root.transform, new[] { source }, root.transform, items, selectors,
                null, new ConversionReporter());

            var panel = selectors.Find(s => s.GetComponent<FrooxEngine.UIX.CanvasWrapper>() != null);
            Assert.IsNotNull(panel, "Radial control must generate a continuous UIX panel");
            Assert.IsFalse(panel.activeSelf);
            Assert.IsTrue(items.Exists(item => item.name.Contains("Drag") &&
                Equals(item.GetComponent<FrooxEngine.ButtonToggleWrapper>()?.Data.TargetValue,
                    new SlotActiveField(panel.transform))));
        }
        finally
        {
            VRCExpressionMenuToggles.Clear(items, selectors);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(tree);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(menu);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void FractionalFloatOptionsCreateDistinctSelector() =>
        AssertFloatOptionsCreateDistinctSelector(new[] { 0.1f, 0.2f, 0.3f }, 0.2f);

    [Test]
    public void NearbyFloatOptionsCreateDistinctSelector() =>
        AssertFloatOptionsCreateDistinctSelector(new[] { 0.49f, 0.4900001f }, 0.4900001f);

    void AssertFloatOptionsCreateDistinctSelector(float[] values, float defaultValue)
    {
        var root = new GameObject("Avatar");
        var target = new GameObject("Target");
        target.transform.SetParent(root.transform);
        var menu = ScriptableObject.CreateInstance<FakeMenu>();
        var controller = new AnimatorController();
        var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "Style", useAutomaticThresholds = false };
        var clips = new List<AnimationClip>();
        var items = new List<GameObject>();
        var selectors = new List<GameObject>();
        try
        {
            controller.AddParameter("Style", AnimatorControllerParameterType.Float);
            controller.AddLayer("FX");
            foreach (var value in values)
            {
                var clip = new AnimationClip();
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Target", typeof(GameObject), "m_IsActive"),
                    AnimationCurve.Constant(0, 1, clips.Count % 2));
                clips.Add(clip);
                tree.AddChild(clip, value);
                menu.controls.Add(new FakeControl { name = "Style " + value, type = FakeControlType.Toggle,
                    parameter = new FakeParameter { name = "Style" }, value = value });
            }
            CollectionAssert.AreEqual(values, tree.children.Select(c => c.threshold).ToArray());
            controller.layers[0].stateMachine.AddState("Pose").motion = tree;
            for (var index = 0; index < values.Length; index++)
            {
                var sampled = AnimatorToggleAnalyzer.SampleFloat(new[] { controller }, "Style", values[index],
                    path => path == "Target" ? target.transform : root.transform, new HashSet<string>());
                Assert.AreEqual(index % 2 == 1, sampled.Bools[$"active:{target.transform.GetInstanceID()}"].value,
                    "Nearby Float thresholds must select their own animated state");
            }
            var source = new ExpressionMenuSource { ResolvePath = path => path == "Target" ? target.transform : root.transform };
            source.Menus.Add((menu, new string[0]));
            source.Parameters.Add(new FakeParameterAsset { parameters = new[] {
                new FakeParameter { name = "Style", valueType = FakeValueType.Float, defaultValue = defaultValue } } });
            source.Controllers.Add(controller);
            VRCExpressionMenuToggles.Apply(root.transform, new[] { source }, root.transform, items, selectors,
                null, new ConversionReporter());
            var selector = selectors.Find(s => s.name.Contains("Selector Style"));
            Assert.IsNotNull(selector, "Distinct Float options must not collapse into one toggle");
            Assert.AreEqual(System.Array.IndexOf(values.OrderBy(v => v).ToArray(), defaultValue) + 1,
                selector.GetComponent<ValueMultiDriverIntWrapper>().Data.Value,
                "The nonzero default must retain its own selector index after the off state");
            var cycles = items.Select(i => i.GetComponent<ButtonValueCycleIntWrapper>())
                .Where(c => c != null).ToArray();
            Assert.AreEqual(values.Length, cycles.Length);
            Assert.AreEqual(values.Length, cycles.Select(c => c.Data.Values[0]).Distinct().Count(),
                "Each authored Float option needs a distinct selector index");
        }
        finally
        {
            VRCExpressionMenuToggles.Clear(items, selectors);
            foreach (var clip in clips) Object.DestroyImmediate(clip);
            Object.DestroyImmediate(tree);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(menu);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MToonPannersAreRemovedWhenScrollStops()
    {
        var root = new GameObject("MToon lifecycle");
        var material = new Material(Shader.Find("Standard"));
        try
        {
            var xiexe = root.AddComponent<FrooxEngine.XiexeToonMaterialWrapper>().Data;
            xiexe.MainTexture = null;
            xiexe.NormalMap = null;
            xiexe.EmissionMap = null;
            xiexe.OcclusionMap = null;
            xiexe.ShadowRampMask = null;
            foreach (var type in new[] { typeof(MToonConverter), typeof(MToon10Converter) })
            foreach (var hasScroll in new[] { false, true })
            {
                var speedProperty = type == typeof(MToonConverter) ? "_UvAnimScrollX" : "_UvAnimScrollXSpeed";
                material.SetFloat(speedProperty, hasScroll ? 1f : 0f);
                var converter = root.AddComponent(type);
                var fields = type == typeof(MToonConverter)
                    ? new[] { "MainTexturePanner", "NormalMapPanner", "EmissionMapPanner", "OcclusionMapPanner", "ShadowRampMaskPanner" }
                    : new[] { "MainTexturePanner", "NormalMapPanner", "EmissionMapPanner", "ShadowRampMaskPanner" };
                foreach (var fieldName in fields)
                {
                    var panner = root.AddComponent<FrooxEngine.Panner2DWrapper>();
                    panner.Data.Enabled = true;
                    type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .SetValue(converter, panner);
                }
                var method = type.GetMethod("UpdateUvScrollPanners",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method.Invoke(converter, new object[] { material, xiexe });
                Assert.AreEqual(0, root.GetComponents<FrooxEngine.Panner2DWrapper>().Length,
                    type.Name + " left stale panners when scrolling or their target texture disappeared (scroll=" + hasScroll + ")");
                Object.DestroyImmediate(converter);
            }
        }
        finally { Object.DestroyImmediate(material); Object.DestroyImmediate(root); }
    }

    [Test]
    public void LocalSpaceScaleConstraintUsesLocalSourceAtFullWeight()
    {
        var root = new GameObject("Local scale test");
        var source = new GameObject("Local source");
        var target = new GameObject("Local target");
        source.transform.SetParent(root.transform);
        target.transform.SetParent(root.transform);
        try
        {
            var component = target.AddComponent<UnityEngine.Animations.ScaleConstraint>();
            var converter = target.AddComponent<ScaleConstraintConverter>();
            converter.Initialize(component);
            var data = new ConstraintData { Kind = ConstraintKind.Scale, Target = target.transform,
                SolveInLocalSpace = true, GlobalWeight = 1 };
            data.Sources.Add(new ConstraintSourceData { Source = source.transform, Weight = 1 });
            var method = typeof(ConstraintConverterBase<UnityEngine.Animations.ScaleConstraint>)
                .GetMethod("Convert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Invoke(converter, new object[] { data, data.Sources, "Local scale test" });
            Assert.IsNotNull(converter.BlendGraph, "Local-space scale must not switch to global copying at full weight");
            Assert.IsNull(converter.CopyScale);
            Assert.AreEqual(0, converter.BlendGraph.GetComponents<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.GlobalTransformWrapper>().Length);
            Assert.AreEqual(1, converter.BlendGraph.GetComponents<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.LocalTransformWrapper>().Length);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void ScaleConstraintBlendsBothSourcesAtPartialWeight()
    {
        var root = new GameObject("Scale blend test");
        var first = new GameObject("First source");
        var second = new GameObject("Second source");
        var target = new GameObject("Constrained scale");
        first.transform.SetParent(root.transform);
        second.transform.SetParent(root.transform);
        target.transform.SetParent(root.transform);
        root.transform.localScale = new Vector3(2, 3, 4);
        target.transform.localRotation = Quaternion.Euler(0, 45, 0);
        target.transform.localScale = new Vector3(1.5f, 1, 1);
        first.transform.localScale = new Vector3(2, 1, 3);
        second.transform.localScale = new Vector3(4, 3, 1);
        try
        {
            var constraint = target.AddComponent<UnityEngine.Animations.ScaleConstraint>();
            constraint.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = first.transform, weight = 0.25f });
            constraint.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = second.transform, weight = 0.5f });
            constraint.scaleAtRest = new Vector3(0.5f, 2, 3);
            constraint.weight = 0.5f;
            constraint.constraintActive = true;
            var converter = target.AddComponent<ScaleConstraintConverter>();
            converter.Initialize(constraint);
            converter.UpdateConversion(new ImmediateContext());
            Assert.IsNotNull(converter.BlendGraph, "A multi-source scale must not copy just one source at full strength");
            Assert.IsNull(converter.CopyScale);
            Assert.AreEqual(0, converter.BlendFollowers.Count, "Global scale must not be sampled from unrotated followers");
            Assert.AreEqual(2, converter.BlendGraph.GetComponents<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.GlobalTransformWrapper>().Length);
            var restToGlobal = converter.BlendGraph.GetComponent<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.LocalScaleToGlobalWrapper>();
            var resultToLocal = converter.BlendGraph.GetComponent<FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform.GlobalScaleToLocalWrapper>();
            Assert.IsNotNull(restToGlobal, "The authored rest scale must be converted to global scale");
            Assert.IsNotNull(resultToLocal, "The blended global scale must be converted for the rotated target");
            var restBlend = converter.BlendGraph.GetComponents<ProtoFluxValueLerpFloat3Wrapper>()
                .Single(node => ReferenceEquals(node.Data, resultToLocal.Data.GlobalScale));
            Assert.AreSame(restToGlobal.Data, restBlend.Data.From,
                "The authored rest scale must feed the global-space blend");
            Assert.AreSame(converter.BlendGraph.GetComponent<ProtoFluxValueInputFloat3Wrapper>().Data,
                restToGlobal.Data.LocalScale);
            Assert.AreSame(target.transform.GetSlot(),
                ((ProtoFluxSlotInputWrapper)converter.BlendGraph.GetComponents<ProtoFluxSlotInputWrapper>()
                    .Single(s => ReferenceEquals(s.Data, resultToLocal.Data.Instance))).Data.Target);
            var drive = converter.BlendGraph.GetComponent<ProtoFluxFieldDriveFloat3Wrapper>();
            var proxy = converter.BlendGraph.GetComponent<ProtoFluxFieldDriveProxyFloat3Wrapper>();
            Assert.IsNotNull(drive);
            Assert.AreSame(drive.Data, proxy.Data.Node);
            Assert.IsInstanceOf<SlotScaleField>(proxy.Data.Drive);
            var field = (SlotScaleField)proxy.Data.Drive;
            Assert.AreSame(target.transform, field.Transform);
            Assert.AreEqual("scale-test", field.GetFieldId(new ResoniteLink.Slot
                { Scale = new ResoniteLink.Field_float3 { ID = "scale-test" } }));
            var restInputs = converter.BlendGraph.GetComponents<ProtoFluxValueInputFloat3Wrapper>()
                .Select(input => input.Data.Value).ToArray();
            CollectionAssert.Contains(restInputs, constraint.scaleAtRest,
                "The authored rest scale must be used even when the target's current scale differs");
            var weights = converter.BlendGraph.GetComponents<ProtoFluxValueInputFloatWrapper>()
                .Select(input => input.Data.Value).ToArray();
            CollectionAssert.Contains(weights, 2f / 3f, "Both sources must contribute their relative weights");
            CollectionAssert.Contains(weights, 0.375f, "Source sum and global weight must blend with rest scale");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void LilToonDisabledReflectionClearsShinyFields()
    {
        var root = new GameObject("Unlit lilToon reflections");
        var material = new Material(Shader.Find("lilToon"));
        try
        {
            material.SetFloat("_UseReflection", 0);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", 1);
            material.SetFloat("_UseShadow", 0);
            material.SetFloat("_UseEmission", 0);
            var xiexe = root.AddComponent<FrooxEngine.XiexeToonMaterialWrapper>().Data;
            xiexe.Reflectivity = 1;
            xiexe.Metallic = 0.8f;
            xiexe.Glossiness = 0.9f;
            xiexe.SpecularIntensity = 80;
            new LilToonXiexeConverter(xiexe, material, new ImmediateContext(), new LilToonAssetCache())
                .UpdateConversion();
            Assert.AreEqual(0, xiexe.Reflectivity, "Disabled lilToon reflection must not inherit Xiexe's reflective default");
            Assert.AreEqual(0, xiexe.Metallic);
            Assert.AreEqual(0, xiexe.Glossiness);
            Assert.AreEqual(0, xiexe.SpecularIntensity);
            Assert.IsNull(xiexe.MetallicGlossMap);
        }
        finally
        {
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void LilToonReenabledReflectionRestoresHighlights()
    {
        var root = new GameObject("Toggle lilToon reflections");
        var material = new Material(Shader.Find("lilToon"));
        try
        {
            material.SetFloat("_UseReflection", 0);
            material.SetFloat("_UseShadow", 0);
            material.SetFloat("_UseEmission", 0);
            var xiexe = root.AddComponent<FrooxEngine.XiexeToonMaterialWrapper>().Data;
            var converter = new LilToonXiexeConverter(xiexe, material, new ImmediateContext(), new LilToonAssetCache());
            converter.UpdateConversion();
            Assert.AreEqual(0, xiexe.Reflectivity);

            material.SetFloat("_UseReflection", 1);
            material.SetFloat("_ApplyReflection", 1);
            material.SetFloat("_ApplySpecular", 1);
            material.SetFloat("_Reflectance", 0.35f);
            material.SetFloat("_Metallic", 0.2f);
            material.SetFloat("_Smoothness", 0.6f);
            converter.UpdateConversion();
            Assert.That(xiexe.Reflectivity, Is.EqualTo(0.35f).Within(1e-5f),
                "Enabled reflection should follow lilToon's reflectance");
            Assert.That(xiexe.Metallic, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(xiexe.Glossiness, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.Greater(xiexe.SpecularIntensity, 0);
        }
        finally
        {
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void LilToonReportsDroppedSecondaryNormalLayer() =>
        AssertLilToonNormalWarning(true, false, "secondary normal map");

    [Test]
    public void LilToonReportsIgnoredSecondaryNormalStrengthMask() =>
        AssertLilToonNormalWarning(false, true, "strength mask");

    [Test]
    public void LilToonReportsBothNormalOmissionsWithSecondaryMask() =>
        AssertLilToonNormalWarning(true, true, "secondary normal strength mask");

    [Test]
    public void LilToonDoesNotWarnForInactiveSecondaryNormal() =>
        AssertLilToonNormalWarning(true, true, null, false);

    void AssertLilToonNormalWarning(bool usePrimary, bool useMask, string expected, bool useSecondary = true)
    {
        var root = new GameObject("Normal layers");
        var material = new Material(Shader.Find("lilToon"));
        var primary = new Texture2D(2, 2);
        var secondary = new Texture2D(2, 2);
        var mask = new Texture2D(2, 2);
        var warnings = new List<string>();
        Application.LogCallback onLog = (message, trace, type) => {
            if (type == LogType.Warning) warnings.Add(message);
        };
        Application.logMessageReceived += onLog;
        try
        {
            material.SetFloat("_UseBumpMap", usePrimary ? 1 : 0);
            material.SetFloat("_UseBump2ndMap", useSecondary ? 1 : 0);
            material.SetTexture("_BumpMap", primary);
            material.SetTexture("_Bump2ndMap", secondary);
            if (useMask) material.SetTexture("_Bump2ndScaleMask", mask);
            var context = new ImmediateContext();
            var converter = new LilToonXiexeConverter(root.AddComponent<FrooxEngine.XiexeToonMaterialWrapper>().Data,
                material, context, new LilToonAssetCache());
            typeof(LilToonXiexeConverter).GetMethod("UpdateNormal",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(converter, null);
            Assert.AreSame(usePrimary ? primary : secondary, context.LastTexture2D);
            var relevant = warnings.Where(w => w.Contains(material.name) && w.Contains("lilToon secondary normal")).ToArray();
            if (expected == null)
                Assert.IsEmpty(relevant, "Inactive secondary layers must not report a conversion loss");
            else
                Assert.IsTrue(relevant.Any(w => w.Contains(expected)),
                    "Unsupported lilToon normal layers or masks must be reported for the affected material");
            Assert.AreEqual(usePrimary && useSecondary,
                relevant.Any(w => w.Contains("secondary normal map on " + material.name)));
            Assert.AreEqual(useMask && useSecondary,
                relevant.Any(w => w.Contains("secondary normal strength mask on " + material.name)));
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            Object.DestroyImmediate(mask);
            Object.DestroyImmediate(primary);
            Object.DestroyImmediate(secondary);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void LilToonUsesSecondaryNormalWhenPrimaryTextureMissing()
    {
        var root = new GameObject("Normal fallback");
        var material = new Material(Shader.Find("lilToon"));
        var secondary = new Texture2D(2, 2);
        try
        {
            material.SetFloat("_UseBumpMap", 1);
            material.SetFloat("_UseBump2ndMap", 1);
            material.SetFloat("_Bump2ndScale", 0.75f);
            material.SetTexture("_Bump2ndMap", secondary);
            var context = new ImmediateContext();
            var xiexe = root.AddComponent<FrooxEngine.XiexeToonMaterialWrapper>().Data;
            var converter = new LilToonXiexeConverter(xiexe, material, context, new LilToonAssetCache());
            typeof(LilToonXiexeConverter).GetMethod("UpdateNormal",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(converter, null);
            Assert.AreSame(secondary, context.LastTexture2D,
                "A missing primary texture must not suppress an available secondary normal map");
            Assert.AreEqual(0.75f, xiexe.NormalScale);
        }
        finally
        {
            Object.DestroyImmediate(secondary);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);
        }
    }

    class FloatFieldTarget : ToggleTarget<float>
    {
        readonly FrooxEngine.IField<float> _field;
        public FloatFieldTarget(FrooxEngine.IField<float> field) => _field = field;
        public override string Key => "float:grid";
        public override float RestValue => 0;
        public override void ResolveField(IConversionContext context, System.Action<FrooxEngine.IField<float>> resolved) => resolved(_field);
    }

    [Test]
    public void ContinuousAxisPanelBlendsBothParameters()
    {
        var state = new GameObject("Continuous Axes");
        var panel = new GameObject("Axis Panel");
        var targetObject = new GameObject("Animated Field");
        try
        {
            var target = new FloatFieldTarget(targetObject.AddComponent<ValueMultiDriverFloatWrapper>().Data.Value_Element.Member);
            var grid = new List<ToggleState>();
            foreach (var y in new[] { -1f, 0f, 1f })
                foreach (var x in new[] { -1f, 0f, 1f })
                {
                    var sample = new ToggleState();
                    sample.Set(target, (x + 1) * (y + 1) * 0.25f);
                    grid.Add(sample);
                }
            var method = typeof(AvatarToggleBuilder).GetMethod("BuildContinuousAxes");
            Assert.IsNotNull(method, "Axis puppets need two live sliders and a 2D state driver");
            method.Invoke(null, new object[] { state, panel, Vector2.zero, grid, null });
            Assert.AreEqual(2, panel.GetComponentsInChildren<FrooxEngine.UIX.SliderWrapper<float>>().Length);
            Assert.AreEqual(1, state.GetComponents<ValueGradientDriverFloatWrapper>().Length,
                "The Y gradient should combine the three X rows into the target field");
            Assert.AreEqual(3, state.GetComponentsInChildren<ValueCopyFloatWrapper>().Length);
        }
        finally { Object.DestroyImmediate(panel); Object.DestroyImmediate(state); Object.DestroyImmediate(targetObject); }
    }

    [Test]
    public void ContinuousAxisPanelLabelsBothSliders()
    {
        var state = new GameObject("Axis State");
        var panel = new GameObject("Axis Panel");
        try
        {
            var grid = new List<ToggleState>();
            for (var i = 0; i < 9; i++) grid.Add(new ToggleState());
            AvatarToggleBuilder.BuildContinuousAxes(state, panel, Vector2.zero, grid, null);
            var labels = panel.GetComponentsInChildren<FrooxEngine.UIX.TextWrapper>()
                .Select(t => t.Data.Content).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Horizontal", "Vertical" }, labels);
        }
        finally { Object.DestroyImmediate(panel); Object.DestroyImmediate(state); }
    }

    [Test]
    public void ContinuousAxisBoolSwitchesFromBothParameters()
    {
        var state = new GameObject("Axis State");
        var panel = new GameObject("Axis Panel");
        var target = new GameObject("Target");
        try
        {
            var field = new ObjectActiveTarget(target.transform);
            var grid = new List<ToggleState>();
            foreach (var y in new[] { -1f, 0f, 1f })
                foreach (var x in new[] { -1f, 0f, 1f })
                {
                    var sample = new ToggleState();
                    sample.Set(field, x > 0 && y > 0);
                    grid.Add(sample);
                }
            AvatarToggleBuilder.BuildContinuousAxes(state, panel, Vector2.zero, grid, null);
            var outer = state.GetComponent<ValueGradientDriverBoolWrapper>();
            Assert.IsNotNull(outer, "Boolean animation must follow both live axes");
            Assert.IsFalse(outer.Data.Interpolate);
            Assert.AreEqual(new SlotActiveField(target.transform), outer.Data.Target);
            Assert.AreEqual(3, state.GetComponents<FrooxEngine.ValueCopyWrapper<bool>>().Length);
        }
        finally { Object.DestroyImmediate(target); Object.DestroyImmediate(panel); Object.DestroyImmediate(state); }
    }

    class ImmediateContext : IConversionContext
    {
        public Texture LastTexture2D;
        public string GetUniqueMessageId(string prefix) => prefix;
        public string GetTransformSlotId(Transform transform) => null;
        public string GetId(FrooxEngine.IWorldElement o) => null;
        public string GetIdOrAllocate(FrooxEngine.IWorldElement o) => null;
        public string GetIdOrAllocate(FrooxEngine.IWorldElement o, out bool allocated) { allocated = false; return null; }
        public void RemoveId(FrooxEngine.IWorldElement o) { }
        public FrooxEngine.IWorldElement TryResolveId(string id) => null;
        public void RunOnConverted(Component component, System.Action action) => action();
        public FrooxEngine.IAssetProvider<FrooxEngine.Mesh> GetMesh(Mesh mesh, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.ITexture> GetITexture(Texture texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.ITexture2D> GetITexture2D(Texture texture, AssetMessagePostProcessor p = null) { LastTexture2D = texture; return null; }
        public FrooxEngine.IAssetProvider<FrooxEngine.Texture2D> GetTexture2D(Texture2D texture, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Cubemap> GetCubemap(Cubemap cubemap, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.AudioClip> GetAudioClip(AudioClip audioClip, AssetMessagePostProcessor p = null) => null;
        public FrooxEngine.IAssetProvider<FrooxEngine.Material> GetMaterial(Material material) => null;
        public System.Threading.Tasks.Task<ResoniteLink.MethodResult> CallMethod(ResoniteLink.CallSyncMethod request) => null;
        public System.Threading.Tasks.Task<ResoniteLink.MethodResult> CallMethod(ResoniteLink.CallStaticSyncMethod request) => null;
    }

    [Test]
    public void ContinuousAxisMaterialSwitchesFromBothParameters()
    {
        var state = new GameObject("Axis State");
        var panel = new GameObject("Axis Panel");
        var target = new GameObject("Target");
        try
        {
            var renderer = target.AddComponent<MeshRenderer>();
            renderer.gameObject.AddComponent<FrooxEngine.MeshRendererWrapper>().Data.Materials.Add();
            var slot = new MaterialSlotTarget(renderer, 0);
            var grid = new List<ToggleState>();
            for (var i = 0; i < 9; i++)
            {
                var sample = new ToggleState();
                sample.SetMaterial(slot, null);
                grid.Add(sample);
            }
            AvatarToggleBuilder.BuildContinuousAxes(state, panel, Vector2.zero, grid, new ImmediateContext());
            Assert.IsNotNull(state.GetComponent<ValueGradientDriverIntWrapper>(),
                "Material swaps must switch at 2D grid points while dragging");
            Assert.AreEqual(1, state.GetComponents<AssetMultiplexerMaterialWrapper>().Length);
            Assert.AreEqual(3, state.GetComponents<FrooxEngine.ValueCopyWrapper<int>>().Length);
        }
        finally { Object.DestroyImmediate(target); Object.DestroyImmediate(panel); Object.DestroyImmediate(state); }
    }

    [Test]
    public void TwoAxisMenuOffersContinuousControls()
    {
        var root = new GameObject("Avatar");
        var target = new GameObject("Target");
        target.transform.SetParent(root.transform);
        var menu = ScriptableObject.CreateInstance<FakeMenu>();
        var controller = new AnimatorController();
        var tree = new BlendTree { blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "Horizontal", blendParameterY = "Vertical" };
        var clip = new AnimationClip();
        var items = new List<GameObject>();
        var selectors = new List<GameObject>();
        try
        {
            controller.AddParameter("Horizontal", AnimatorControllerParameterType.Float);
            controller.AddParameter("Vertical", AnimatorControllerParameterType.Float);
            controller.AddLayer("FX");
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Target", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 1, 1));
            tree.AddChild(clip, Vector2.zero);
            tree.AddChild(clip, Vector2.one);
            controller.layers[0].stateMachine.AddState("Pose").motion = tree;
            menu.controls.Add(new FakeControl { name = "Aim", type = FakeControlType.TwoAxisPuppet,
                subParameters = new[] { new FakeParameter { name = "Horizontal" }, new FakeParameter { name = "Vertical" } } });
            var source = new ExpressionMenuSource { ResolvePath = path => path == "Target" ? target.transform : root.transform };
            source.Menus.Add((menu, new string[0]));
            source.Parameters.Add(new FakeParameterAsset { parameters = new[] {
                new FakeParameter { name = "Horizontal", valueType = FakeValueType.Float },
                new FakeParameter { name = "Vertical", valueType = FakeValueType.Float } } });
            source.Controllers.Add(controller);
            VRCExpressionMenuToggles.Apply(root.transform, new[] { source }, root.transform, items, selectors,
                null, new ConversionReporter());
            var panel = selectors.Find(s => s.GetComponent<FrooxEngine.UIX.CanvasWrapper>() != null);
            Assert.IsNotNull(panel, "2D puppets need a draggable X/Y panel");
            Assert.AreEqual(2, panel.GetComponentsInChildren<FrooxEngine.UIX.SliderWrapper<float>>(true).Length);
            Assert.IsFalse(panel.activeSelf);
            Assert.IsFalse(selectors.Exists(s => s.GetComponent<ValueMultiDriverIntWrapper>() != null),
                "The discrete selector must not compete to drive the same properties");
            Assert.IsTrue(items.Exists(item => item.name.Contains("Drag") &&
                Equals(item.GetComponent<FrooxEngine.ButtonToggleWrapper>()?.Data.TargetValue,
                    new SlotActiveField(panel.transform))));
        }
        finally
        {
            VRCExpressionMenuToggles.Clear(items, selectors);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(tree);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(menu);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void FourAxisOpposingDefaultsAreReportedAsApproximate()
    {
        var (warnings, items) = RunFourAxisOpposingDefaults(false);
        Assert.IsTrue(warnings.Any(message => message.Contains("opposing") && message.Contains("default")));
        Assert.IsTrue(warnings.Any(message => message.Contains("default-only") && message.Contains("omitted")),
            "Report animated properties that are present only at the original four-axis default");
        Assert.IsTrue(items.Any(name => name.Contains("Default (approx.)")));
    }

    [Test]
    public void FourAxisDefaultOnlyAnimationIsReported()
    {
        var (warnings, items) = RunFourAxisOpposingDefaults(true);
        Assert.IsEmpty(items, "A default-only four-parameter state is not representable with two sliders");
        Assert.IsTrue(warnings.Any(message => message.Contains("opposing") && message.Contains("default")),
            "Do not silently discard animation that exists only at opposing four-axis defaults");
    }

    static (List<string> warnings, List<string> itemNames) RunFourAxisOpposingDefaults(bool defaultOnly)
    {
        var root = new GameObject("Avatar");
        var target = new GameObject("Target");
        target.transform.SetParent(root.transform);
        var other = new GameObject("Other");
        other.transform.SetParent(root.transform);
        var menu = ScriptableObject.CreateInstance<FakeMenu>();
        var controller = new AnimatorController();
        var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "Up" };
        var clip = new AnimationClip();
        var activeClip = new AnimationClip();
        var otherClip = new AnimationClip();
        var items = new List<GameObject>();
        var selectors = new List<GameObject>();
        var warnings = new List<string>();
        Application.LogCallback onLog = (message, trace, type) => {
            if (type == LogType.Warning) warnings.Add(message);
        };
        Application.logMessageReceived += onLog;
        try
        {
            var names = new[] { "Up", "Right", "Down", "Left" };
            foreach (var name in names) controller.AddParameter(name, AnimatorControllerParameterType.Float);
            controller.AddLayer("FX");
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Target", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 1, 0));
            AnimationUtility.SetEditorCurve(activeClip, EditorCurveBinding.FloatCurve("Target", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 1, 1));
            if (defaultOnly)
            {
                var pose = controller.layers[0].stateMachine.AddState("Pose");
                pose.motion = clip;
                var transition = controller.layers[0].stateMachine.AddAnyStateTransition(pose);
                transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Up");
                transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Down");
            }
            else
            {
                tree.AddChild(clip, 0);
                tree.AddChild(activeClip, 1);
                var machine = controller.layers[0].stateMachine;
                machine.AddState("Pose").motion = tree;
                AnimationUtility.SetEditorCurve(otherClip, EditorCurveBinding.FloatCurve("Other", typeof(GameObject), "m_IsActive"),
                    AnimationCurve.Constant(0, 1, 0));
                var otherState = machine.AddState("Opposing Default Only");
                otherState.motion = otherClip;
                var transition = machine.AddAnyStateTransition(otherState);
                transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Up");
                transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Down");
            }
            menu.controls.Add(new FakeControl { name = "Direction", type = FakeControlType.FourAxisPuppet,
                subParameters = names.Select(name => new FakeParameter { name = name }).ToArray() });
            var source = new ExpressionMenuSource { ResolvePath = path => path == "Target" ? target.transform :
                path == "Other" ? other.transform : root.transform };
            source.Menus.Add((menu, new string[0]));
            source.Parameters.Add(new FakeParameterAsset { parameters = names.Select(name => new FakeParameter {
                name = name, valueType = FakeValueType.Float, defaultValue = name == "Up" || name == "Down" ? 1 : 0
            }).ToArray() });
            source.Controllers.Add(controller);
            VRCExpressionMenuToggles.Apply(root.transform, new[] { source }, root.transform, items, selectors,
                null, new ConversionReporter());
            return (warnings, items.Select(item => item.name).ToList());
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            VRCExpressionMenuToggles.Clear(items, selectors);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(activeClip);
            Object.DestroyImmediate(otherClip);
            Object.DestroyImmediate(tree);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(menu);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void FourAxisDiagonalDrivesUpAndRightWithoutDrivingDownOrLeft()
    {
        var method = typeof(VRCExpressionMenuToggles).GetMethod("AxisValues",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);
        var result = (Dictionary<string, float>)method.Invoke(null,
            new object[] { new[] { "Up", "Right", "Down", "Left" }, new Vector2(1, 1) });
        Assert.AreEqual(1, result["Up"]);
        Assert.AreEqual(1, result["Right"]);
        Assert.AreEqual(0, result["Down"]);
        Assert.AreEqual(0, result["Left"]);
    }

    [Test]
    public void TwoAxisDiagonalDrivesBothParameters()
    {
        var method = typeof(VRCExpressionMenuToggles).GetMethod("AxisValues",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);
        var result = (Dictionary<string, float>)method.Invoke(null,
            new object[] { new[] { "Horizontal", "Vertical" }, new Vector2(-1, 1) });
        Assert.AreEqual(-1, result["Horizontal"]);
        Assert.AreEqual(1, result["Vertical"]);
    }

    [Test]
    public void TwoAxisDiagonalSamplesBothParameters()
    {
        var root = new GameObject("Root");
        var target = new GameObject("Target");
        target.transform.SetParent(root.transform);
        var controller = new AnimatorController();
        var tree = new BlendTree { blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "Horizontal", blendParameterY = "Vertical" };
        var clips = new List<AnimationClip>();

        try
        {
            controller.AddParameter("Horizontal", AnimatorControllerParameterType.Float);
            controller.AddParameter("Vertical", AnimatorControllerParameterType.Float);
            controller.AddLayer("FX");

            AnimationClip Clip(bool active)
            {
                var clip = new AnimationClip();
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Target", typeof(GameObject), "m_IsActive"),
                    AnimationCurve.Constant(0, 1, active ? 1 : 0));
                clips.Add(clip);
                return clip;
            }

            tree.AddChild(Clip(false), Vector2.zero);
            tree.AddChild(Clip(false), Vector2.right);
            tree.AddChild(Clip(true), Vector2.one);
            controller.layers[0].stateMachine.AddState("Pose").motion = tree;

            var state = AnimatorToggleAnalyzer.SampleParameters(new[] { controller },
                new Dictionary<string, float> { ["Horizontal"] = 1, ["Vertical"] = 1 },
                path => path == "Target" ? target.transform : root.transform, new HashSet<string>());

            Assert.IsTrue(state.Bools[$"active:{target.transform.GetInstanceID()}"].value);
        }
        finally
        {
            foreach (var clip in clips) Object.DestroyImmediate(clip);
            Object.DestroyImmediate(tree);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(root);
        }
    }
}
#endif
