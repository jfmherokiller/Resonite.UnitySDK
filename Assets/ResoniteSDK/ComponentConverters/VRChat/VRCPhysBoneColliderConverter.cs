using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts VRChat PhysBone colliders into Resonite dynamic bone sphere colliders.
///
/// Resonite only has sphere colliders for dynamic bones, which are positioned by their slot. Because of this,
/// the converter generates helper objects (not saved with the scene) under the collider's root transform:
/// - Sphere is converted into a single sphere collider
/// - Capsule is approximated by a row of overlapping spheres
/// - Plane and "inside bounds" colliders are not supported
///
/// The colliders are referenced by <see cref="VRCPhysBoneConverter"/> for any PhysBones that use them.
/// </summary>
[ConvertsComponentType(VRChatTypes.PhysBoneCollider)]
public class VRCPhysBoneColliderConverter : ResoniteComponentConverter<Component>
{
    const string CONTAINER_NAME = "[Resonite] PhysBone Collider";

    const int SHAPE_SPHERE = 0;
    const int SHAPE_CAPSULE = 1;

    public GameObject Container;
    public List<FrooxEngine.DynamicBoneSphereColliderWrapper> Spheres = new List<FrooxEngine.DynamicBoneSphereColliderWrapper>();

    bool _reported;

    protected override void UpdateConversion(Component target, IConversionContext context) => Rebuild();

    /// <summary>
    /// Returns all the generated colliders. This will build them if they don't exist yet.
    /// </summary>
    public IEnumerable<FrooxEngine.DynamicBoneSphereCollider> GetColliders()
    {
        if (Container == null)
            Rebuild();

        foreach (var sphere in Spheres)
            if (sphere != null)
                yield return sphere.Data;
    }

    void Rebuild()
    {
        var target = Target;

        if (target == null)
            return;

        var shape = ReflectionAccessor.GetInt(target, "shapeType");
        var insideBounds = ReflectionAccessor.Get(target, "insideBounds", false);

        if ((shape != SHAPE_SPHERE && shape != SHAPE_CAPSULE) || insideBounds)
        {
            if (!_reported)
                Debug.LogWarning($"PhysBone collider on {target.name} uses shape or mode that's not supported by Resonite " +
                    $"(only non-inverted sphere and capsule colliders are converted).", target);

            _reported = true;
            RemoveGenerated();
            return;
        }

        var root = ReflectionAccessor.GetTransform(target, "rootTransform");

        if (root == null)
            root = target.transform;

        var radius = Mathf.Max(0, ReflectionAccessor.Get(target, "radius", 0.5f));
        var height = ReflectionAccessor.Get(target, "height", 2f);

        // Sphere offsets along the local Y axis
        var offsets = new List<float>();

        // VRChat's capsule height is the total height, including the caps
        var halfLength = shape == SHAPE_CAPSULE ? Mathf.Max(0, height * 0.5f - radius) : 0;

        if (halfLength <= 0 || radius <= 0)
            offsets.Add(0);
        else
        {
            // Space the spheres so they overlap by at least half the radius
            var count = Mathf.Clamp(Mathf.CeilToInt(halfLength * 2 / radius) + 1, 2, 16);

            for (int i = 0; i < count; i++)
                offsets.Add(Mathf.Lerp(-halfLength, halfLength, i / (float)(count - 1)));
        }

        if (Container == null)
        {
            Container = new GameObject(CONTAINER_NAME);
            Container.hideFlags = HideFlags.DontSave;
        }

        if (Container.transform.parent != root)
            Container.transform.SetParent(root, false);

        SetLocal(Container.transform,
            ReflectionAccessor.Get(target, "position", Vector3.zero),
            ReflectionAccessor.Get(target, "rotation", Quaternion.identity));

        Spheres.RemoveAll(s => s == null);

        while (Spheres.Count < offsets.Count)
        {
            var sphere = new GameObject($"Sphere {Spheres.Count}");
            sphere.hideFlags = HideFlags.DontSave;
            sphere.transform.SetParent(Container.transform, false);

            Spheres.Add(sphere.AddComponent<FrooxEngine.DynamicBoneSphereColliderWrapper>());
        }

        while (Spheres.Count > offsets.Count)
        {
            DestroyImmediate(Spheres[Spheres.Count - 1].gameObject);
            Spheres.RemoveAt(Spheres.Count - 1);
        }

        var enabled = !(target is Behaviour behaviour) || behaviour.enabled;

        for (int i = 0; i < offsets.Count; i++)
        {
            SetLocal(Spheres[i].transform, new Vector3(0, offsets[i], 0), Quaternion.identity);

            var data = Spheres[i].Data;

            data.persistent = true;
            data.Enabled = enabled;
            data.Radius = radius;
        }
    }

    // Only assign when changed, so we don't generate needless change events in realtime mode
    static void SetLocal(Transform transform, Vector3 position, Quaternion rotation)
    {
        if (transform.localPosition != position)
            transform.localPosition = position;

        if (transform.localRotation != rotation)
            transform.localRotation = rotation;
    }

    void RemoveGenerated()
    {
        Spheres.Clear();

        if (Container != null)
            DestroyImmediate(Container);

        Container = null;
    }

    protected override void Cleanup() => RemoveGenerated();
}
