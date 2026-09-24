using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts the VRChat avatar descriptor:
/// - Lip sync: viseme blendshapes (or jaw flap blendshape) are driven by Resonite's viseme analyzer from the voice
///   of the user wearing the avatar.
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
    static readonly Action<FrooxEngine.DirectVisemeDriver, FrooxEngine.IField<float>>[] VISEME_SETTERS =
    {
        (d, f) => d.Silence = f,
        (d, f) => d.PP = f,
        (d, f) => d.FF = f,
        (d, f) => d.TH = f,
        (d, f) => d.DD = f,
        (d, f) => d.kk = f,
        (d, f) => d.CH = f,
        (d, f) => d.SS = f,
        (d, f) => d.nn = f,
        (d, f) => d.RR = f,
        (d, f) => d.aa = f,
        (d, f) => d.E = f,
        (d, f) => d.ih = f,
        (d, f) => d.oh = f,
        (d, f) => d.ou = f,
    };

    static readonly string[] VISEME_MEMBERS =
        { "Silence", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };

    // Blendshape names VRChat detects automatically in the Default lip sync mode
    static readonly string[] VRC_VISEME_NAMES =
        { "sil", "pp", "ff", "th", "dd", "kk", "ch", "ss", "nn", "rr", "aa", "e", "ih", "oh", "ou" };

    const int VISEME_AA = 10;

    [Tooltip("Drive the viseme blendshapes from the voice in Resonite. Disable if you set up visemes differently.")]
    public bool ConvertVisemes = true;

    [Tooltip("Convert the expression menu toggles into Resonite context menu toggles.")]
    public bool ConvertExpressionMenu = true;

    public GameObject Visemes;
    public List<GameObject> MenuItems = new List<GameObject>();
    public List<GameObject> MenuSelectors = new List<GameObject>();

    readonly ConversionReporter _report = new ConversionReporter();

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
            GeneratedObjectHelper.Destroy(ref Visemes);

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
        var mesh = ReflectionAccessor.GetObject<SkinnedMeshRenderer>(target, "VisemeSkinnedMesh");
        var mode = ReflectionAccessor.GetEnumName(target, "lipSync", "Default");

        // Blendshape name for each viseme (in VRChat order)
        var shapes = new string[VISEME_SETTERS.Length];

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
            else if (mode == "JawFlapBlendShape")
            {
                // Closest approximation - open the mouth on the "aa" viseme
                shapes[VISEME_AA] = ReflectionAccessor.Get(target, "MouthOpenBlendShapeName", "");
            }
        }

        // Blendshape index for each viseme. The same blendshape can't be driven twice, so only the first viseme gets it.
        var indices = new int[shapes.Length];
        var used = new HashSet<int>();

        for (int i = 0; i < shapes.Length; i++)
        {
            indices[i] = string.IsNullOrEmpty(shapes[i]) ? -1 : mesh.sharedMesh.GetBlendShapeIndex(shapes[i]);

            if (indices[i] >= 0 && !used.Add(indices[i]))
                indices[i] = -1;
        }

        if (used.Count == 0)
        {
            GeneratedObjectHelper.Destroy(ref Visemes);
            return;
        }

        if (Visemes == null)
            Visemes = GeneratedObjectHelper.EnsureChild(target.transform, VISEMES_NAME);

        var analyzer = ConverterComponentHelper.GetOrAdd<FrooxEngine.VisemeAnalyzerWrapper>(Visemes);
        var assigner = ConverterComponentHelper.GetOrAdd<FrooxEngine.CommonAvatar.AvatarVoiceSourceAssignerWrapper>(Visemes);
        var driver = ConverterComponentHelper.GetOrAdd<FrooxEngine.DirectVisemeDriverWrapper>(Visemes);

        // Everything on the analyzer stays at defaults - the source is assigned when the avatar is equipped
        ResoniteMemberFilter.Set(analyzer);

        // When the avatar is equipped, this will feed the user's voice into the analyzer
        assigner.Data.TargetReference = analyzer.Data.Source_Element.Member;

        driver.Data.Source = analyzer.Data;

        var members = new List<string> { "Source" };

        for (int i = 0; i < shapes.Length; i++)
        {
            if (indices[i] < 0)
                continue;

            members.Add(VISEME_MEMBERS[i]);

            var setter = VISEME_SETTERS[i];
            var driverData = driver.Data;

            BlendShapeFieldHelper.RunWithField(context, mesh, indices[i], field => setter(driverData, field));
        }

        ResoniteMemberFilter.Set(driver, members.ToArray());
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
        GeneratedObjectHelper.Destroy(ref Visemes);

#if UNITY_EDITOR
        VRCExpressionMenuToggles.Clear(MenuItems, MenuSelectors);
#endif
    }
}
