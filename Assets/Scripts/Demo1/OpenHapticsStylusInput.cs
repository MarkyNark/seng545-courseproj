using UnityEngine;

/// <summary>
/// Optional adapter for the historical OpenHaptics HapticPlugin script.
/// The project compiles WITHOUT that plugin. After restoring and validating it,
/// add SENG545_OPENHAPTICS in Player Settings > Scripting Define Symbols.
/// The gameplay controller does not need to be changed.
/// </summary>
public sealed class OpenHapticsStylusInput : MonoBehaviour, IStylusInput
{
#if SENG545_OPENHAPTICS
    public HapticPlugin hapticDevice;
    public float worldScale = 1f;
    public Vector3 worldOffset = Vector3.zero;
    public int grabButtonIndex = 0;

    public bool IsAvailable => hapticDevice != null && hapticDevice.hHD >= 0;

    public Vector3 WorldPosition => IsAvailable
        ? hapticDevice.stylusPositionWorld * worldScale + worldOffset
        : transform.position;

    public bool GrabHeld
    {
        get
        {
            if (!IsAvailable || hapticDevice.Buttons == null) return false;
            return grabButtonIndex >= 0 && grabButtonIndex < hapticDevice.Buttons.Length
                && hapticDevice.Buttons[grabButtonIndex] == 1;
        }
    }
#else
    // Stub implementation keeps the keyboard demo runnable with no native DLL.
    public bool IsAvailable => false;
    public Vector3 WorldPosition => transform.position;
    public bool GrabHeld => false;
#endif
}
