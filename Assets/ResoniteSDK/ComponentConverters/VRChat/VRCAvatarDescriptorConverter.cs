using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts the VRChat avatar descriptor:
/// - If the avatar doesn't have <see cref="ResoniteBipedAvatarDescriptor"/> yet, it's added automatically with the
///   viewpoint placed at the VRChat view position. Existing descriptors are never modified.
/// - Viseme blendshapes (or jaw flap blendshape) are driven by Resonite's viseme analyzer from the user's voice.
///
/// Eye look and blinking are left to Resonite's avatar creator (see "Setup Eyes" on the Resonite descriptor).
/// </summary>
[ConvertsComponentType(VRChatTypes.AvatarDescriptor)]
public class VRCAvatarDescriptorConverter : ResoniteComponentConverter<Component>
{
    const string VISEMES_NAME = "[Resonite] Visemes";

    // VRChat's LipSyncStyle enum
    const int LIPSYNC_DEFAULT = 0;
    const int LIPSYNC_JAW_FLAP_BLENDSHAPE = 2;
    const int LIPSYNC_VISEME_BLENDSHAPE = 3;

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

    [Tooltip("Drive the viseme blendshapes from the voice in Resonite. Disable if you set up visemes differently.")]
    public bool ConvertVisemes = true;

    [NonSerialized]
    GameObject _visemes;

    protected override void Initialize(Component target)
    {
        if (target.GetComponent<ResoniteBipedAvatarDescriptor>() != null)
            return;

        var animator = target.GetComponent<Animator>();

        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogWarning($"VRChat avatar {target.name} doesn't have a humanoid Animator. " +
                $"Resonite avatar can't be set up automatically.", target);
            return;
        }

        // This will generate the references and position them based on the humanoid rig
        var descriptor = target.gameObject.AddComponent<ResoniteBipedAvatarDescriptor>();
        descriptor.EnsureReferencesExist();

        // VRChat's view position is more reliable than the one estimated from the eye bones, since the creator set it
        var viewPosition = ReflectionAccessor.Get(target, "ViewPosition", Vector3.zero);

        if (viewPosition != Vector3.zero && descriptor.ViewpointReference != null)
            descriptor.ViewpointReference.position = target.transform.TransformPoint(viewPosition);

        Debug.Log($"Added ResoniteBipedAvatarDescriptor to VRChat avatar {target.name}. " +
            $"Check the generated references before saving the avatar.", target);
    }

    protected override void UpdateConversion(Component target, IConversionContext context)
    {
        if (ConvertVisemes)
            SetupVisemes(target, context);
        else
            GeneratedObjectHelper.Destroy(ref _visemes);
    }

    void SetupVisemes(Component target, IConversionContext context)
    {
        var mesh = ReflectionAccessor.GetObject<SkinnedMeshRenderer>(target, "VisemeSkinnedMesh");
        var mode = ReflectionAccessor.GetInt(target, "lipSync");

        // Blendshape name for each viseme (in VRChat order)
        var shapes = new string[VISEME_SETTERS.Length];

        if (mesh != null && mesh.sharedMesh != null)
        {
            if (mode == LIPSYNC_VISEME_BLENDSHAPE || mode == LIPSYNC_DEFAULT)
            {
                var configured = ReflectionAccessor.Get<string[]>(target, "VisemeBlendShapes");

                for (int i = 0; i < shapes.Length; i++)
                {
                    if (configured != null && i < configured.Length && !string.IsNullOrEmpty(configured[i]))
                        shapes[i] = configured[i];
                    else if (mode == LIPSYNC_DEFAULT)
                        shapes[i] = FindDefaultViseme(mesh.sharedMesh, VRC_VISEME_NAMES[i]);
                }
            }
            else if (mode == LIPSYNC_JAW_FLAP_BLENDSHAPE)
            {
                // Closest approximation - open the mouth on the "aa" viseme
                shapes[10] = ReflectionAccessor.Get(target, "MouthOpenBlendShapeName", "");
            }
        }

        var any = false;

        foreach (var shape in shapes)
            if (!string.IsNullOrEmpty(shape) && mesh.sharedMesh.GetBlendShapeIndex(shape) >= 0)
                any = true;

        if (!any)
        {
            GeneratedObjectHelper.Destroy(ref _visemes);
            return;
        }

        if (_visemes == null)
            _visemes = GeneratedObjectHelper.EnsureChild(target.transform, VISEMES_NAME);

        var analyzer = GeneratedObjectHelper.EnsureComponent<PartialVisemeAnalyzerWrapper>(_visemes);
        var assigner = GeneratedObjectHelper.EnsureComponent<FrooxEngine.CommonAvatar.AvatarVoiceSourceAssignerWrapper>(_visemes);
        var driver = GeneratedObjectHelper.EnsureComponent<PartialDirectVisemeDriverWrapper>(_visemes);

        analyzer.Data.persistent = true;
        analyzer.Data.Enabled = true;

        // When the avatar is equipped, this will feed the user's voice into the analyzer
        assigner.Data.persistent = true;
        assigner.Data.Enabled = true;
        assigner.Data.TargetReference = analyzer.Data.Source_Element.Member;

        driver.Data.persistent = true;
        driver.Data.Enabled = true;
        driver.Data.Source = analyzer.Data;

        var members = new List<string> { "Source" };

        // The same blendshape can't be driven twice, so only the first viseme using it gets it
        var used = new HashSet<int>();

        for (int i = 0; i < shapes.Length; i++)
        {
            if (string.IsNullOrEmpty(shapes[i]))
                continue;

            var index = mesh.sharedMesh.GetBlendShapeIndex(shapes[i]);

            if (index < 0 || !used.Add(index))
                continue;

            members.Add(VISEME_MEMBERS[i]);

            var setter = VISEME_SETTERS[i];
            var driverData = driver.Data;

            BlendShapeFieldHelper.RunWithField(context, mesh, index, field => setter(driverData, field));
        }

        driver.Members = members;
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
        // We intentionally keep the generated ResoniteBipedAvatarDescriptor, since user might have adjusted it
        GeneratedObjectHelper.Destroy(ref _visemes);
    }
}
