using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts supported VRCFury features. VRCFury components hold one feature each (or a list of them in older
/// versions), which are applied by VRCFury when the avatar is built. Since that build never happens when converting
/// to Resonite, the relevant features are translated here instead:
///
/// - Armature Link: prop/clothing bones are linked to the avatar bones using VirtualParent, so they follow
///   the avatar without modifying the Unity hierarchy.
/// - Bone Constraint (legacy): same as a non-recursive Armature Link.
/// - Delete During Upload: the object is made inactive in Resonite.
/// - Blend Shape Link: linked blendshapes are driven from the base mesh (see <see cref="VRCFuryBlendShapeLink"/>).
/// - Toggle: converted into context menu toggles (see <see cref="VRCFuryToggle"/>).
///
/// Other features (full controllers, gestures, menus...) rely on VRChat's animator and menu systems and are reported.
/// All VRCFury types are internal, so the data is read through reflection.
/// </summary>
[ConvertsComponentType(VRCFuryTypes.VRCFury)]
public class VRCFuryConverter : ResoniteComponentConverter<Component>, ISlotActiveOverride
{
    public List<FrooxEngine.VirtualParentWrapper> Links = new List<FrooxEngine.VirtualParentWrapper>();

    // Generated helper objects (not saved with the scene)
    public GameObject BlendShapeLinkObject;
    public List<GameObject> ToggleObjects = new List<GameObject>();

    readonly ConversionReporter _report = new ConversionReporter();

    // Evaluated on demand, so it's correct even when the slot is updated without the converter running
    public bool ForceSlotInactive => Target != null && GetFeatures(Target).Any(f => f.GetType().Name == "DeleteDuringUpload");

    struct LinkRequest
    {
        public Transform Prop;
        public Transform Target;
        public bool AlignPosition;
        public bool AlignRotation;
    }

    protected override void UpdateConversion(Component target, IConversionContext context)
    {
        var links = new List<LinkRequest>();
        var blendShapeLinks = new List<object>();
        var toggles = new List<object>();

        foreach (var feature in GetFeatures(target))
        {
            switch (feature.GetType().Name)
            {
                case "ArmatureLink":
                    ReadArmatureLink(target, feature, links);
                    break;

                case "BoneConstraint":
                    ReadBoneConstraint(target, feature, links);
                    break;

                case "DeleteDuringUpload":
                    // Handled by ForceSlotInactive
                    break;

                case "BlendShapeLink":
                    blendShapeLinks.Add(feature);
                    break;

                case "Toggle":
                    toggles.Add(feature);
                    break;

                default:
                    _report.Info(feature.GetType().Name, $"VRCFury feature {feature.GetType().Name} on {target.name} is not " +
                        $"converted to Resonite.", target);
                    break;
            }
        }

        ApplyLinks(links);

        VRCFuryBlendShapeLink.Apply(target, blendShapeLinks, ref BlendShapeLinkObject, context);
        ApplyToggles(target, toggles, context);
    }

    void ApplyToggles(Component target, List<object> toggles, IConversionContext context)
    {
        if (ToggleObjects == null)
            ToggleObjects = new List<GameObject>();

        while (ToggleObjects.Count < toggles.Count)
            ToggleObjects.Add(null);

        for (int i = 0; i < toggles.Count; i++)
        {
            var toggleObject = ToggleObjects[i];
            VRCFuryToggle.Apply(target, toggles[i], ref toggleObject, context, _report);
            ToggleObjects[i] = toggleObject;
        }

        // Remove toggles which no longer exist
        for (int i = ToggleObjects.Count - 1; i >= toggles.Count; i--)
        {
            var toggleObject = ToggleObjects[i];
            GeneratedObjectHelper.DestroyWithEmptyParents(ref toggleObject);
            ToggleObjects.RemoveAt(i);
        }
    }

    static IEnumerable<object> GetFeatures(Component target)
    {
        var content = ReflectionAccessor.GetRaw(target, "content");

        if (content != null)
            yield return content;

        // Older VRCFury versions stored a list of features, before they were migrated to one per component
        var config = ReflectionAccessor.GetRaw(target, "config");

        foreach (var feature in ReflectionAccessor.GetList(config, "features"))
            if (feature != null)
                yield return feature;
    }

    #region ARMATURE LINK

    void ReadArmatureLink(Component target, object feature, List<LinkRequest> links)
    {
        var prop = ReflectionAccessor.GetTransform(feature, "propBone");

        if (prop == null)
        {
            _report.Warning("nopropbone", $"VRCFury Armature Link on {target.name} has no prop bone set.", target);
            return;
        }

        var avatarRoot = VRChatTypes.FindAvatarRoot(target.transform);
        Transform linkTarget = null;

        foreach (var linkTo in ReflectionAccessor.GetList(feature, "linkTo"))
        {
            linkTarget = ResolveLinkTo(avatarRoot, linkTo);

            if (linkTarget != null)
                break;
        }

        if (linkTarget == null)
        {
            _report.Warning("notarget", $"VRCFury Armature Link on {target.name} couldn't find the bone to link to on the avatar.", target);
            return;
        }

        if (linkTarget == prop || linkTarget.IsChildOf(prop))
            return;

        var recursive = ReflectionAccessor.Get(feature, "recursive", false);
        var alignPosition = ReflectionAccessor.Get(feature, "alignPosition", false);
        var alignRotation = ReflectionAccessor.Get(feature, "alignRotation", false);

        links.Add(new LinkRequest()
        {
            Prop = prop,
            Target = linkTarget,
            AlignPosition = alignPosition,
            AlignRotation = alignRotation,
        });

        if (!recursive)
            return;

        var suffix = ReflectionAccessor.Get(feature, "removeBoneSuffix", "");
        var naming = BoneNaming.Detect(prop.name, linkTarget.name, suffix);

        MatchChildren(prop, linkTarget, naming, alignPosition, alignRotation, links);
    }

    static Transform ResolveLinkTo(Transform avatarRoot, object linkTo)
    {
        Transform result;

        if (ReflectionAccessor.Get(linkTo, "useBone", true))
        {
            var animator = avatarRoot.GetComponent<Animator>();

            if (animator == null || !animator.isHuman)
                return null;

            var bone = (HumanBodyBones)ReflectionAccessor.GetInt(linkTo, "bone");

            if (bone < 0 || bone >= HumanBodyBones.LastBone)
                return null;

            result = animator.GetBoneTransform(bone);
        }
        else if (ReflectionAccessor.Get(linkTo, "useObj", false))
            result = ReflectionAccessor.GetTransform(linkTo, "obj");
        else
            result = avatarRoot;

        if (result == null)
            return null;

        var offset = ReflectionAccessor.Get(linkTo, "offset", "");

        if (!string.IsNullOrWhiteSpace(offset))
            result = result.Find(offset.Trim().Trim('/'));

        return result;
    }

    static void MatchChildren(Transform prop, Transform avatar, BoneNaming naming,
        bool alignPosition, bool alignRotation, List<LinkRequest> links)
    {
        for (int i = 0; i < prop.childCount; i++)
        {
            var propChild = prop.GetChild(i);
            var name = naming.Normalize(propChild.name);

            Transform match = null;

            for (int a = 0; a < avatar.childCount && match == null; a++)
            {
                var avatarChild = avatar.GetChild(a);

                if (string.Equals(avatarChild.name, name, StringComparison.OrdinalIgnoreCase))
                    match = avatarChild;
            }

            // Bones without match are left as they are - they'll follow the matched parent bone
            if (match == null)
                continue;

            links.Add(new LinkRequest()
            {
                Prop = propChild,
                Target = match,
                AlignPosition = alignPosition,
                AlignRotation = alignRotation,
            });

            MatchChildren(propChild, match, naming, alignPosition, alignRotation, links);
        }
    }

    /// <summary>
    /// Clothing bones are often named the same as the avatar bones, but with extra prefix or suffix (e.g. "Hips.001"
    /// or "Shirt_Hips"). This detects it from the root bones, unless it's explicitly specified.
    /// </summary>
    class BoneNaming
    {
        string _prefix = "";
        string _suffix = "";

        public static BoneNaming Detect(string propName, string avatarName, string explicitSuffix)
        {
            var naming = new BoneNaming();

            if (!string.IsNullOrEmpty(explicitSuffix))
            {
                naming._suffix = explicitSuffix;
                return naming;
            }

            if (propName.Length > avatarName.Length)
            {
                if (propName.StartsWith(avatarName, StringComparison.OrdinalIgnoreCase))
                    naming._suffix = propName.Substring(avatarName.Length);
                else if (propName.EndsWith(avatarName, StringComparison.OrdinalIgnoreCase))
                    naming._prefix = propName.Substring(0, propName.Length - avatarName.Length);
            }

            return naming;
        }

        public string Normalize(string name)
        {
            if (_suffix.Length > 0)
                name = name.Replace(_suffix, "");

            if (_prefix.Length > 0 && name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(_prefix.Length);

            return name;
        }
    }

    #endregion

    void ReadBoneConstraint(Component target, object feature, List<LinkRequest> links)
    {
        var obj = ReflectionAccessor.GetTransform(feature, "obj");
        var animator = VRChatTypes.FindAvatarRoot(target.transform).GetComponent<Animator>();

        if (obj == null || animator == null || !animator.isHuman)
            return;

        var bone = animator.GetBoneTransform((HumanBodyBones)ReflectionAccessor.GetInt(feature, "bone"));

        if (bone == null || bone == obj || bone.IsChildOf(obj))
            return;

        links.Add(new LinkRequest() { Prop = obj, Target = bone });
    }

    void ApplyLinks(List<LinkRequest> requests)
    {
        // Remove links for props which are no longer linked
        var props = new HashSet<GameObject>(requests.Select(r => r.Prop.gameObject));

        for (int i = Links.Count - 1; i >= 0; i--)
        {
            if (Links[i] != null && props.Contains(Links[i].gameObject))
                continue;

            if (Links[i] != null)
                DestroyImmediate(Links[i]);

            Links.RemoveAt(i);
        }

        foreach (var request in requests)
        {
            var wrapper = Links.FirstOrDefault(l => l.gameObject == request.Prop.gameObject);

            if (wrapper == null)
            {
                wrapper = request.Prop.gameObject.AddComponent<FrooxEngine.VirtualParentWrapper>();
                Links.Add(wrapper);
            }

            ResoniteMemberFilter.Set(wrapper, "OverrideParent", "LocalPosition", "LocalRotation", "LocalScale");

            var parent = wrapper.Data;
            var prop = request.Prop;
            var target = request.Target;

            parent.OverrideParent = target.GetSlot();

            // Unless aligned, keep the prop where it currently is relative to the bone it's linked to
            parent.LocalPosition = request.AlignPosition ? Vector3.zero : target.InverseTransformPoint(prop.position);
            parent.LocalRotation = request.AlignRotation ? Quaternion.identity : Quaternion.Inverse(target.rotation) * prop.rotation;
            parent.LocalScale = ConverterComponentHelper.SafeDivide(prop.lossyScale, target.lossyScale);
        }
    }

    protected override void Cleanup()
    {
        foreach (var link in Links)
            if (link != null)
                DestroyImmediate(link);

        Links.Clear();

        GeneratedObjectHelper.Destroy(ref BlendShapeLinkObject);

        if (ToggleObjects != null)
            for (int i = 0; i < ToggleObjects.Count; i++)
            {
                var toggleObject = ToggleObjects[i];
                GeneratedObjectHelper.DestroyWithEmptyParents(ref toggleObject);
            }

        ToggleObjects?.Clear();
    }
}
