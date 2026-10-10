#if UNITY_EDITOR
using FrooxEngine;
using ResoniteLink;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts small fixtures with the real converters and writes converter-output bundles
/// (exact ResoniteLink batch + numeric runtime cases) for tests/runtime/harness.py.
/// </summary>
public static class ConverterOutputBundles
{
    public const int SchemaVersion = 1;

    // Add fixtures here; each converts a fresh isolated hierarchy and declares runtime cases.
    static readonly (string name, Action<BundleBuilder> build)[] Fixtures =
    {
        ("continuous-slider", ContinuousSlider),
    };

    public static string[] ExportAll(string directory)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        foreach (var (name, build) in Fixtures)
        {
            using var builder = new BundleBuilder(name);
            build(builder);
            var path = Path.Combine(directory, name + ".json");
            File.WriteAllText(path, builder.ToJson());
            written.Add(path);
        }
        return written.ToArray();
    }

    /// <summary>Batch-mode entry point: -executeMethod ConverterOutputBundles.ExportFromCommandLine -converterBundleDir DIR</summary>
    public static void ExportFromCommandLine()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-converterBundleDir");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Missing -converterBundleDir <directory>");
            foreach (var path in ExportAll(args[index + 1]))
                Debug.Log("CONVERTER_BUNDLE_WRITTEN: " + path);
            Debug.Log("CONVERTER_BUNDLES_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError("CONVERTER_BUNDLES_FAIL: " + ex);
            EditorApplication.Exit(1);
        }
    }

    // A radial puppet slider: the slider drives the puppet parameter and its handle anchors,
    // and the puppet writes back into the slider (e.g. preset buttons).
    static void ContinuousSlider(BundleBuilder bundle)
    {
        var state = bundle.Child("State");
        var puppet = state.AddResoniteComponent<ValueMultiDriverFloatWrapper>().Binding;
        var panel = bundle.Child("Panel");
        AvatarToggleBuilder.BuildContinuousSlider(panel, puppet.Value_Element.Member, 0.3f, 0f, 1f);
        bundle.Convert();

        var track = panel.transform.Find("[Resonite] Track").gameObject;
        var slider = track.GetResoniteComponent<ContinuousPuppetSliderWrapper>().Binding;
        var handle = track.transform.Find("[Resonite] Handle").gameObject
            .GetResoniteComponent<FrooxEngine.UIX.RectTransform>().Binding;

        // Native Slider<T>: horizontal handle anchor = (pow(clamp01(normalized), Power), 0) + AnchorOffset.
        void ExpectPosition(BundleBuilder.Case c, float value) => c
            .Check(slider, "Value", value)
            .Check(puppet, "Value", value)
            .Check(handle, "AnchorMin", new Vector2(value, 0.5f))
            .Check(handle, "AnchorMax", new Vector2(value, 0.5f));

        ExpectPosition(bundle.AddCase("initial value reaches puppet and handle"), 0.3f);
        ExpectPosition(bundle.AddCase("slider value drives puppet and handle").Set(slider, "Value", 0.75f), 0.75f);
        ExpectPosition(bundle.AddCase("puppet write-back moves slider").Set(puppet, "Value", 0.2f), 0.2f);
    }
}

/// <summary>Builds one isolated fixture, captures its converted operations and records runtime cases.</summary>
public sealed class BundleBuilder : IDisposable
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly MethodInfo UpdateConversions = typeof(SceneConverter).GetMethod("UpdateComponentConversions", Private);
    static readonly MethodInfo ConvertHierarchy = typeof(SceneConverter).GetMethod("ConvertHierarchy", Private);

    readonly IsolatedPreviewScene scene = new IsolatedPreviewScene();
    readonly ResoniteLinkWindow window = ScriptableObject.CreateInstance<ResoniteLinkWindow>();
    readonly SceneConverter converter = new SceneConverter();
    readonly List<Case> cases = new List<Case>();
    List<DataModelOperation> operations;

    public string Name { get; }
    public GameObject Root { get; }

    public BundleBuilder(string name)
    {
        if (UpdateConversions == null || ConvertHierarchy == null)
            throw new NotSupportedException("SceneConverter conversion internals changed; update BundleBuilder.");
        Name = name;
        window.hideFlags = HideFlags.HideAndDontSave;
        converter.EnsureInitialized(window);
        Root = new GameObject("ConverterHarness_" + name);
    }

    public GameObject Child(string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(Root.transform, false);
        return child;
    }

    /// <summary>Runs the same converter and payload path as Send Current Scene, without sending.</summary>
    public void Convert()
    {
        if (operations != null)
            throw new InvalidOperationException("Fixture already converted");
        if (Root.GetComponentsInChildren<IConversionPostProcessor>(true).Any())
            throw new NotSupportedException($"{Name}: conversion post-processors need a live link");
        try
        {
            UpdateConversions.Invoke(converter, new object[] { Root.transform });
            operations = new List<DataModelOperation>();
            ConvertHierarchy.Invoke(converter, new object[] { Root.transform, operations });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
        {
            throw new NotSupportedException($"{Name}: fixture needs asset conversion, which bundles do not capture yet", ex.InnerException);
        }
        if (Root.GetComponentsInChildren<ResoniteComponent>(true).Any(c => c.Data is IComponentConversionPostProcessor))
            throw new NotSupportedException($"{Name}: component post-processors need a live link");
    }

    public string Id(object element) =>
        converter.GetId((IWorldElement)element) ?? throw new InvalidOperationException($"No exported ID for {element}");

    public Case AddCase(string name)
    {
        if (operations == null)
            throw new InvalidOperationException("Convert the fixture before declaring cases");
        var c = new Case(this, name);
        cases.Add(c);
        return c;
    }

    public string ToJson()
    {
        if (operations == null)
            throw new InvalidOperationException($"{Name} was never converted");
        var batch = new DataModelOperationBatch { Operations = operations.ToList<Message>() };
        // Serialize through the polymorphic base, as on the wire, so "$type" is emitted.
        using var batchJson = JsonDocument.Parse(JsonSerializer.Serialize<Message>(batch, LinkInterface.SerializationOptions));
        using var casesJson = JsonDocument.Parse(JsonSerializer.Serialize(cases.Select(c => c.Data).ToList()));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", ConverterOutputBundles.SchemaVersion);
            writer.WriteString("name", Name);
            writer.WriteString("generator", "Resonite.UnitySDK tests/runtime/ConverterOutputBundles.cs");
            writer.WriteString("unityVersion", Application.unityVersion);
            writer.WriteString("rootId", converter.GetTransformSlotId(Root.transform));
            writer.WritePropertyName("batch");
            batchJson.RootElement.WriteTo(writer);
            writer.WritePropertyName("cases");
            casesJson.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Dispose()
    {
        UnityEngine.Object.DestroyImmediate(Root);
        UnityEngine.Object.DestroyImmediate(window);
        scene.Dispose();
    }

    public sealed class Case
    {
        readonly BundleBuilder bundle;
        readonly List<Dictionary<string, object>> updates = new List<Dictionary<string, object>>();
        readonly List<Dictionary<string, object>> checks = new List<Dictionary<string, object>>();

        internal Case(BundleBuilder bundle, string name)
        {
            this.bundle = bundle;
            Data = new Dictionary<string, object> { ["name"] = name, ["updates"] = updates, ["checks"] = checks };
        }

        internal Dictionary<string, object> Data { get; }

        public Case Set(object component, string member, object value)
        {
            updates.Add(new Dictionary<string, object>
            {
                ["kind"] = "component",
                ["id"] = bundle.Id(component),
                ["members"] = new Dictionary<string, object> { [member] = Typed(value) },
            });
            return this;
        }

        public Case Check(object component, string member, object expected, double tolerance = 1e-4)
        {
            checks.Add(new Dictionary<string, object>
            {
                ["kind"] = "component",
                ["id"] = bundle.Id(component),
                ["member"] = member,
                ["expected"] = Plain(expected),
                ["absoluteTolerance"] = tolerance,
            });
            return this;
        }

        static object Plain(object value) => value switch
        {
            Vector2 v => new Dictionary<string, object> { ["x"] = v.x, ["y"] = v.y },
            Vector3 v => new Dictionary<string, object> { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z },
            float or bool or int or string => value,
            _ => throw new NotSupportedException($"Unsupported case value {value?.GetType()}"),
        };

        static object Typed(object value) => new Dictionary<string, object>
        {
            ["$type"] = value switch
            {
                float => "float",
                bool => "bool",
                int => "int",
                string => "string",
                Vector2 => "float2",
                Vector3 => "float3",
                _ => throw new NotSupportedException($"Unsupported update value {value?.GetType()}"),
            },
            ["value"] = Plain(value),
        };
    }
}
#endif
