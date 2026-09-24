using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds Resonite dynamic bone sphere colliders approximating a sphere or capsule shape.
///
/// Resonite only has sphere colliders for dynamic bones, which are positioned by their slot. The builder
/// generates helper objects (not saved with the scene) under the given root: a container placed at the collider's
/// offset, with one child per sphere. Capsules (along the local Y axis) are approximated by overlapping spheres.
/// </summary>
[Serializable]
public class DynamicBoneColliderBuilder
{
    public GameObject Container;
    public List<FrooxEngine.DynamicBoneSphereColliderWrapper> Spheres = new List<FrooxEngine.DynamicBoneSphereColliderWrapper>();

    public bool Exists => Container != null;

    /// <param name="height">Total height of the capsule, including the caps. Use 0 for a sphere.</param>
    public void Build(string name, Transform root, Vector3 position, Quaternion rotation, float radius, float height, bool enabled)
    {
        radius = Mathf.Max(0, radius);

        // Sphere offsets along the local Y axis
        var offsets = new List<float>();
        var halfLength = Mathf.Max(0, height * 0.5f - radius);

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
            Container = GeneratedObjectHelper.Create(root, name);

        if (Container.transform.parent != root)
            Container.transform.SetParent(root, false);

        GeneratedObjectHelper.SetLocalPose(Container.transform, position, rotation);

        Spheres.RemoveAll(s => s == null);

        while (Spheres.Count < offsets.Count)
        {
            var sphere = GeneratedObjectHelper.Create(Container.transform, $"Sphere {Spheres.Count}");
            Spheres.Add(sphere.AddComponent<FrooxEngine.DynamicBoneSphereColliderWrapper>());
        }

        while (Spheres.Count > offsets.Count)
        {
            UnityEngine.Object.DestroyImmediate(Spheres[Spheres.Count - 1].gameObject);
            Spheres.RemoveAt(Spheres.Count - 1);
        }

        for (int i = 0; i < offsets.Count; i++)
        {
            GeneratedObjectHelper.SetLocalPose(Spheres[i].transform, new Vector3(0, offsets[i], 0), Quaternion.identity);

            var data = Spheres[i].Data;

            data.Enabled = enabled;
            data.Radius = radius;
        }
    }

    public IEnumerable<FrooxEngine.DynamicBoneSphereCollider> GetColliders()
    {
        foreach (var sphere in Spheres)
            if (sphere != null)
                yield return sphere.Data;
    }

    public void Clear()
    {
        Spheres.Clear();
        GeneratedObjectHelper.Destroy(ref Container);
    }
}

/// <summary>
/// Implemented by converters which produce dynamic bone colliders
/// </summary>
public interface IDynamicBoneColliderSource
{
    IEnumerable<FrooxEngine.DynamicBoneSphereCollider> GetColliders();
}
