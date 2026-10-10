using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts the VRChat avatar descriptor:
/// - Lip sync: phonemes use a viseme analyzer; jaw flap uses smoothed voice volume.
/// - Expression menu: toggles are converted into Resonite context menu toggles (see <see cref="VRCExpressionMenuToggles"/>)
///
/// The Resonite avatar itself is set up with the Avatar Setup Wizard, which uses the VRChat view position
/// when it finds this descriptor. Eye look and blinking are handled by Resonite's avatar creator ("Eye Setup").
/// </summary>
[ConvertsComponentType(VRChatTypes.AvatarDescriptor)]
public class VRCAvatarDescriptorConverter : ResoniteComponentConverter<Component>
{
    const string VISEMES_NAME = "[Resonite] Visemes";

    // Order of VRChat's VisemeBlendShapes array
    static readonly FrooxEngine.Viseme[] VISEMES =
    {
        FrooxEngine.Viseme.Silence, FrooxEngine.Viseme.PP, FrooxEngine.Viseme.FF,
        FrooxEngine.Viseme.TH, FrooxEngine.Viseme.DD, FrooxEngine.Viseme.kk,
        FrooxEngine.Viseme.CH, FrooxEngine.Viseme.SS, FrooxEngine.Viseme.nn,
        FrooxEngine.Viseme.RR, FrooxEngine.Viseme.aa, FrooxEngine.Viseme.E,
        FrooxEngine.Viseme.ih, FrooxEngine.Viseme.oh, FrooxEngine.Viseme.ou,
    };

    // Blendshape names VRChat detects automatically in the Default lip sync mode
    static readonly string[] VRC_VISEME_NAMES =
        { "sil", "pp", "ff", "th", "dd", "kk", "ch", "ss", "nn", "rr", "aa", "e", "ih", "oh", "ou" };


    [Tooltip("Drive the viseme blendshapes from the voice in Resonite. Disable if you set up visemes differently.")]
    public bool ConvertVisemes = true;

    [Tooltip("Convert the expression menu toggles into Resonite context menu toggles.")]
    public bool ConvertExpressionMenu = true;

    [Tooltip("Add a \"Disable PhysBones\" avatar toggle covering every VRC PhysBone / Dynamic Bone on the avatar.")]
    public bool ConvertPhysBoneDisableToggle = true;

    public GameObject Visemes;
    public GameObject PhysBonesToggle;
    public List<GameObject> MenuItems = new List<GameObject>();
    public List<GameObject> MenuSelectors = new List<GameObject>();

    readonly ConversionReporter _report = new ConversionReporter();
    int _visemeGeneration;

    protected override void Initialize(Component target)
    {
        if (target.GetComponent<ResoniteBipedAvatarDescriptor>() == null)
            Debug.Log($"VRChat avatar {target.name} doesn't have Resonite avatar setup yet. Use Resonite SDK > Avatar Setup Wizard " +
                $"to set it up - it will use the VRChat view position.", target);
    }

    protected override void UpdateConversion(Component target, IConversionContext context)
    {
        if (ConvertVisemes)
            SetupVisemes(target, context);
        else
        {
            _visemeGeneration++;
            GeneratedObjectHelper.Destroy(ref Visemes);
        }

        if (ConvertPhysBoneDisableToggle)
            PhysBoneDisableToggle.Apply(target.transform, ref PhysBonesToggle, context);
        else
            GeneratedObjectHelper.DestroyWithEmptyParents(ref PhysBonesToggle);

#if UNITY_EDITOR
        var menu = ConvertExpressionMenu ? VRCExpressionMenuToggles.FromDescriptor(target) : null;

        if (menu != null)
            VRCExpressionMenuToggles.Apply(target, new[] { menu }, target.transform, MenuItems, MenuSelectors, context, _report);
        else
            VRCExpressionMenuToggles.Clear(MenuItems, MenuSelectors);
#endif
    }

    void SetupVisemes(Component target, IConversionContext context)
    {
        var generation = ++_visemeGeneration;
        var mesh = ReflectionAccessor.GetObject<SkinnedMeshRenderer>(target, "VisemeSkinnedMesh");
        var mode = ReflectionAccessor.GetEnumName(target, "lipSync", "Default");

        if (mode == "JawFlapBlendShape")
        {
            SetupJawFlap(target, mesh, context, generation);
            return;
        }

        // Blendshape name for each viseme (in VRChat order)
        var shapes = new string[VISEMES.Length];

        if (mesh != null && mesh.sharedMesh != null)
        {
            if (mode == "VisemeBlendShape" || mode == "Default")
            {
                var configured = ReflectionAccessor.Get<string[]>(target, "VisemeBlendShapes");

                for (int i = 0; i < shapes.Length; i++)
                {
                    if (configured != null && i < configured.Length && !string.IsNullOrEmpty(configured[i]))
                        shapes[i] = configured[i];
                    else if (mode == "Default")
                        shapes[i] = FindDefaultViseme(mesh.sharedMesh, VRC_VISEME_NAMES[i]);
                }
            }

        }

        var indices = new int[shapes.Length];
        var groups = new SortedDictionary<int, List<FrooxEngine.Viseme>>();

        for (int i = 0; i < shapes.Length; i++)
            indices[i] = string.IsNullOrEmpty(shapes[i]) ? -1 : mesh.sharedMesh.GetBlendShapeIndex(shapes[i]);

        // A shape shared with speech is not a neutral silence pose.
        if (indices[0] >= 0 && Array.IndexOf(indices, indices[0], 1) >= 0)
            indices[0] = -1;

        for (int i = 0; i < shapes.Length; i++)
        {
            if (indices[i] < 0)
                continue;
            if (!groups.TryGetValue(indices[i], out var sources))
                groups.Add(indices[i], sources = new List<FrooxEngine.Viseme>());
            sources.Add(VISEMES[i]);
        }

        if (groups.Count == 0)
        {
            GeneratedObjectHelper.Destroy(ref Visemes);
            return;
        }

        if (Visemes == null)
            Visemes = GeneratedObjectHelper.EnsureChild(target.transform, VISEMES_NAME);

        var analyzer = ConverterComponentHelper.GetOrAdd<FrooxEngine.VisemeAnalyzer>(Visemes);
        var assigner = ConverterComponentHelper.GetOrAdd<FrooxEngine.CommonAvatar.AvatarVoiceSourceAssigner>(Visemes);
        RemoveUnusedVoiceComponents(false);
        var driver = ConverterComponentHelper.GetOrAdd<FrooxEngine.DynamicVisemeDriver>(Visemes);

        // Everything on the analyzer stays at defaults - the source is assigned when the avatar is equipped.
        // "Source" must still be included (even though we don't set its value) because the assigner below
        // references it by ID - filtering it out entirely leaves that reference dangling and Resonite can't
        // resolve it.
        ResoniteMemberFilter.Set(analyzer.Container, "Source");

        // When the avatar is equipped, this will feed the user's voice into the analyzer
        assigner.Binding.TargetReference = analyzer.Binding.Source_Element.Member;

        driver.Binding.Source = analyzer.Binding;
        driver.Binding.StrengthMultiplier = 1f;
        var drivers = driver.Binding.Drivers;
        // ResoniteLink does not truncate trailing list entries; explicitly neutralize them.
        foreach (var entry in drivers.Data)
        {
            entry.Target = null;
            foreach (var source in entry.Sources.Data)
                source.Influence = 0f;
        }

        var groupIndex = 0;
        foreach (var group in groups)
        {
            var entry = groupIndex < drivers.Count ? drivers.GetElement(groupIndex) : drivers.Add();
            groupIndex++;
            entry.Target = null;
            for (int i = 0; i < group.Value.Count; i++)
            {
                var source = i < entry.Sources.Count ? entry.Sources.GetElement(i) : entry.Sources.Add();
                source.Viseme = group.Value[i];
                source.Influence = 1f;
            }

            // Native weighted sum: one writer per shape, without weakening shared phonemes.
            BlendShapeFieldHelper.RunWithField(context, mesh, group.Key, field =>
            {
                if (driver.Container != null && generation == _visemeGeneration)
                    entry.Target = field;
            });
        }

        ResoniteMemberFilter.Set(driver.Container, "Source", "StrengthMultiplier", "Drivers");
    }

    void SetupJawFlap(Component target, SkinnedMeshRenderer mesh, IConversionContext context, int generation)
    {
        var shape = ReflectionAccessor.Get(target, "MouthOpenBlendShapeName", "");
        var index = mesh != null && mesh.sharedMesh != null && !string.IsNullOrEmpty(shape)
            ? mesh.sharedMesh.GetBlendShapeIndex(shape) : -1;
        if (index < 0)
        {
            GeneratedObjectHelper.Destroy(ref Visemes);
            return;
        }

        if (Visemes == null)
            Visemes = GeneratedObjectHelper.EnsureChild(target.transform, VISEMES_NAME);
        RemoveUnusedVoiceComponents(true);
        var meter = ConverterComponentHelper.GetOrAdd<FrooxEngine.VolumeMeter>(Visemes);
        var assigner = ConverterComponentHelper.GetOrAdd<FrooxEngine.CommonAvatar.AvatarVoiceSourceAssigner>(Visemes);
        var copy = ConverterComponentHelper.GetOrAdd<ValueCopyFloatWrapper>(Visemes);
        meter.Binding.Smoothing = 0.8f;
        meter.Binding.Power = 0.5f;
        meter.Binding.Method = FrooxEngine.VolumeMeterMethod.Magnitude;
        assigner.Binding.TargetReference = meter.Binding.Source_Element.Member;
        copy.Binding.Source = meter.Binding.Volume_Element.Member;
        copy.Binding.Target = null;
        copy.Binding.WriteBack = false;
        BlendShapeFieldHelper.RunWithField(context, mesh, index, field =>
        {
            if (copy.Container != null && generation == _visemeGeneration)
                copy.Binding.Target = field;
        });

        // Export the raw output's ID even though ResoniteLink does not write its value.
        ResoniteMemberFilter.Set(meter.Container, "Source", "Smoothing", "Power", "Method", "Volume");
        ResoniteMemberFilter.Set(assigner.Container, "TargetReference");
        ResoniteMemberFilter.Set(copy.Container, "Source", "Target", "WriteBack");
    }

    void RemoveUnusedVoiceComponents(bool jawFlap)
    {
        foreach (var component in Visemes.GetComponents<ResoniteComponent>())
            if (component.Data is FrooxEngine.DirectVisemeDriver || (jawFlap
                ? component.Data is FrooxEngine.VisemeAnalyzer || component.Data is FrooxEngine.DynamicVisemeDriver
                : component.Data is FrooxEngine.VolumeMeter || component.Data is ValueCopyFloatWrapper))
                DestroyImmediate(component);
    }

    static string FindDefaultViseme(Mesh mesh, string viseme)
    {
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            var name = mesh.GetBlendShapeName(i);

            if (string.Equals(name, "vrc.v_" + viseme, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "v_" + viseme, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }

    protected override void Cleanup()
    {
        _visemeGeneration++;
        GeneratedObjectHelper.Destroy(ref Visemes);

#if UNITY_EDITOR
        VRCExpressionMenuToggles.Clear(MenuItems, MenuSelectors);
#endif
    }
}
