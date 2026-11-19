using UnityEngine;

/// <summary>
/// Runtime-only physics setup for the team's imported OBJ scene.
/// Adds static concave colliders to its environment, and a BoxCollider plus
/// non-kinematic Rigidbody to each canister. No scene/prefab edits needed.
/// </summary>
public static class Demo1PhysicsSetup
{
    public static void AddEnvironmentColliders()
    {
        string[] roots = { "room", "conveyor", "bin (1)", "bin (2)" };
        foreach (string rootName in roots)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null)
            {
                Debug.LogWarning("Demo1 V5: Could not locate physics environment object " + rootName);
                continue;
            }
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponent<Collider>() != null) continue;
                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false; // Static geometry can be concave/open.
            }
        }
    }

    public static void AddSampleBody(DemoCanister sample)
    {
        // Imported meshes have significant local offsets. Construct the
        // collider from the combined rendered world bounds instead of guessing
        // from the sample GameObject's transform position.
        Bounds world = sample.VisibleBounds;
        Vector3 localMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 localMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = world.center + Vector3.Scale(world.extents, new Vector3(x, y, z));
            Vector3 local = sample.transform.InverseTransformPoint(corner);
            localMin = Vector3.Min(localMin, local);
            localMax = Vector3.Max(localMax, local);
        }

        BoxCollider box = sample.GetComponent<BoxCollider>();
        if (box == null) box = sample.gameObject.AddComponent<BoxCollider>();
        box.center = (localMin + localMax) * 0.5f;
        box.size = localMax - localMin;
        box.isTrigger = false;

        Rigidbody body = sample.GetComponent<Rigidbody>();
        if (body == null) body = sample.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.mass = 1f; // Same simulated mechanics for all classes; cue is external.
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    public static void Freeze(DemoCanister sample)
    {
        Rigidbody body = sample != null ? sample.GetComponent<Rigidbody>() : null;
        if (body == null) return;
        body.isKinematic = true;
        body.useGravity = false;
    }

    public static void ReleaseAtRest(DemoCanister sample)
    {
        Rigidbody body = sample != null ? sample.GetComponent<Rigidbody>() : null;
        if (body == null) return;
        body.isKinematic = true;
        body.useGravity = false;
        Physics.SyncTransforms();
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = true;
    }

    public static void BeginGrab(Rigidbody body)
    {
        body.useGravity = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    public static void EndGrab(Rigidbody body)
    {
        if (body == null) return;
        body.linearVelocity = Vector3.zero;
        body.useGravity = true;
    }
}
