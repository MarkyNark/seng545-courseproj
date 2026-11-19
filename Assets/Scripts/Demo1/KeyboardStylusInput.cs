using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard stand-in for the haptic stylus's 3D position and grab button.
/// This simulates the interaction workflow, not physical force sensations.
/// </summary>
public sealed class KeyboardStylusInput : MonoBehaviour, IStylusInput
{
    [Min(0.1f)] public float moveSpeed = 4f;
    [Min(1f)] public float sprintMultiplier = 2f;
    [Min(0.1f)] public float minimumHeight = -100f;
    public Camera referenceCamera;

    // Demo1Controller reduces this while holding an anomalously heavy canister.
    [System.NonSerialized] public float movementMultiplier = 1f;

    public bool IsAvailable => Keyboard.current != null;
    public Vector3 WorldPosition => transform.position;
    public bool GrabHeld => Keyboard.current != null && Keyboard.current.spaceKey.isPressed;

    private void Awake()
    {
        if (referenceCamera == null) referenceCamera = Camera.main;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (referenceCamera != null)
        {
            forward = Vector3.ProjectOnPlane(referenceCamera.transform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(referenceCamera.transform.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
        }

        float horizontal = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float depth = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        float vertical = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
        Vector3 motion = right * horizontal + forward * depth + Vector3.up * vertical;
        motion = Vector3.ClampMagnitude(motion, 1f);

        float sprint = (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) ? sprintMultiplier : 1f;
        Vector3 next = transform.position + motion * (moveSpeed * movementMultiplier * sprint * Time.deltaTime);
        next.y = Mathf.Max(next.y, minimumHeight);
        transform.position = next;
    }

    public void SetPosition(Vector3 position)
    {
        transform.position = position;
    }
}
