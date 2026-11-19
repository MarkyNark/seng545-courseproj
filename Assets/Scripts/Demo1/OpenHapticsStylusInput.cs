using UnityEngine;

/// <summary>
/// Input-only adapter for the course OpenHaptics Unity plugin's HapticPlugin.
/// This script also builds without the plugin installed. The mass/force effect
/// must be implemented and tested separately on the actual Touch device.
/// </summary>
public sealed class OpenHapticsStylusInput : MonoBehaviour, IStylusInput
{
#if SENG545_OPENHAPTICS
    [Tooltip("Optional. If unassigned, searches the loaded scene for HapticPlugin.")]
    public HapticPlugin hapticDevice;
    [Min(0.01f)] public float worldScale = 1f;
    public Vector3 worldOffset = Vector3.zero;
    [Range(0, 3)] public int grabButtonIndex = 0;

    // The keyboard demo can start before the device prefab is present.
    // Once it is added in the scene, the adapter discovers it automatically.
    private HapticPlugin Device
    {
        get
        {
            if (hapticDevice == null)
                hapticDevice = Object.FindFirstObjectByType<HapticPlugin>();
            return hapticDevice;
        }
    }

    public bool IsAvailable => Device != null && Device.hHD >= 0;

    // The plugin already returns world space. Scaling/offset are for calibrating
    // its reach to the team's scene, not for generating haptic forces.
    public Vector3 WorldPosition => IsAvailable
        ? Device.stylusPositionWorld * worldScale + worldOffset
        : transform.position;

    public bool GrabHeld
    {
        get
        {
            if (!IsAvailable || Device.Buttons == null) return false;
            return grabButtonIndex >= 0 && grabButtonIndex < Device.Buttons.Length
                && Device.Buttons[grabButtonIndex] == 1;
        }
    }
#else
    // Keep the keyboard-only application compilable when no plugin or
    // native driver has been installed.
    public bool IsAvailable => false;
    public Vector3 WorldPosition => transform.position;
    public bool GrabHeld => false;
#endif
}
