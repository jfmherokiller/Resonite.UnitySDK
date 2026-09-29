using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts VRCFury's "Apply During Upload": unconditionally applies a State's actions - the conceptual inverse of
/// DeleteDuringUpload (which we also convert), which unconditionally makes an object inactive instead. Reuses the
/// same action reader as <see cref="VRCFuryToggle"/>, and builds a permanently-on toggle (via
/// <see cref="AvatarToggleBuilder.BuildToggle"/>) with no menu item ever pointing at it, so it can never be
/// switched off, rather than inventing a separate "just write the value" mechanism - the driver components
/// AvatarToggleBuilder wires up are the only proven way this SDK has to set a field on an already-converted target.
/// </summary>
public static class VRCFuryApplyDuringUpload
{
    public static void Apply(Component target, object feature, ref GameObject stateObject, IConversionContext context,
        ConversionReporter report)
    {
        var avatarRoot = VRChatTypes.FindAvatarRoot(target.transform);
        var state = ReflectionAccessor.GetRaw(feature, "action");

        var on = new ToggleState();
        var unsupported = new HashSet<string>();

        VRCFuryToggle.ReadState(avatarRoot, state, on, unsupported, report, target, $"'Apply During Upload' on {target.name}");

        if (unsupported.Count > 0)
            report.Info("unsupported:applyDuringUpload", $"VRCFury 'Apply During Upload' on {target.name} also animates " +
                $"{string.Join(", ", unsupported)}, which isn't converted.", target);

        if (on.IsEmpty)
        {
            GeneratedObjectHelper.Destroy(ref stateObject);
            return;
        }

        if (stateObject == null)
            stateObject = GeneratedObjectHelper.EnsureChild(target.transform, "[Resonite] Apply During Upload");

        AvatarToggleBuilder.BuildToggle(stateObject, true, on, new ToggleState(), context);
    }
}
