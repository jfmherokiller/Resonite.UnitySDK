#if UNITY_EDITOR
using System.Collections.Generic;
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
            suite.FourAxisDiagonalDrivesUpAndRightWithoutDrivingDownOrLeft();
            suite.TwoAxisDiagonalDrivesBothParameters();
            UnityEngine.Debug.Log("EXPRESSION_PUPPET_TESTS_PASS");
            EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("EXPRESSION_PUPPET_TESTS_FAIL: " + ex);
            EditorApplication.Exit(1);
        }
    }

    public enum FakeControlType { TwoAxisPuppet }
    public class FakeParameter { public string name; }
    public class FakeControl
    {
        public string name;
        public FakeControlType type;
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
