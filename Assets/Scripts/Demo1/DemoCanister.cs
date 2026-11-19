using UnityEngine;

/// <summary>One originally identical-looking sorting object and its hidden class.</summary>
public sealed class DemoCanister : MonoBehaviour
{
    public bool anomalous;
    public bool Sorted { get; private set; }

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool initialActive;

    private void Awake()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialActive = gameObject.activeSelf;
    }

    // The OBJ models may have baked-in offsets. Renderer bounds are more reliable
    // for interaction than the root Transform coordinates in this repository.
    public Bounds VisibleBounds
    {
        get
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            bool found = false;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }
    }

    public void ShiftVisualCenterTo(Vector3 targetCenter)
    {
        transform.position += targetCenter - VisibleBounds.center;
    }

    public void MarkSorted()
    {
        Sorted = true;
    }

    public void ResetCanister()
    {
        gameObject.SetActive(initialActive);
        transform.SetPositionAndRotation(initialPosition, initialRotation);
        Sorted = false;
    }
}
