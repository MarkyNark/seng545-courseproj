using UnityEngine;

/// <summary>
/// Optional OpenHaptics mass cue for Demo 1. The keyboard sorting prototype
/// works without the OpenHaptics SDK or a connected device.
///
/// The native effect is a modest downward force, ONLY while a heavy canister
/// is held with the physical stylus. It is a demo cue, not a calibrated mass.
/// </summary>
public sealed class Demo1MassHapticEffect : MonoBehaviour
{
    [Header("Physical force (opt-in on lab workstation)")]
    public bool enablePhysicalForce = false;

    // Begin low; never increase without a TA supervising a physical test.
    [Range(0f, 0.25f)] public float heavyMagnitude = 0.08f;

    private bool heavyHeld;

    public void SetHeavyHeld(bool isHeavyHeld)
    {
        heavyHeld = isHeavyHeld;
    }

#if SENG545_OPENHAPTICS
    private HapticPlugin device;
    private int effectId = -1;
    private bool effectPlaying;
    private string effectDeviceName;

    private void Update()
    {
        // Never allocate or run a force unless BOTH an actual device and an
        // explicitly opted-in heavy-canister grab are present.
        if (!enablePhysicalForce || !heavyHeld)
        {
            StopForce();
            return;
        }

        if (device == null)
            device = Object.FindFirstObjectByType<HapticPlugin>();

        if (device == null || device.hHD < 0 || device.isInSafetyMode())
        {
            StopForce();
            return;
        }

        // A Unity scene can reconnect or replace a device. Don't reuse an
        // OpenHaptics effect ID allocated for another configuration.
        if (effectDeviceName != null && effectDeviceName != device.configName)
        {
            StopForce();
            effectId = -1;
        }

        if (effectId < 0)
        {
            effectId = HapticPlugin.effects_assignEffect(device.configName);
            if (effectId < 0)
                return;
            effectDeviceName = device.configName;
        }

        // OpenHaptics expects a direction in device-local coordinates.
        Vector3 localDown = device.transform.InverseTransformDirection(Vector3.down).normalized;
        double[] point = { 0.0, 0.0, 0.0 }; // Unused by CONSTANT effects.
        double[] direction = { localDown.x, localDown.y, localDown.z };

        HapticPlugin.effects_settings(device.configName, effectId,
            0.0, heavyMagnitude, 0.0, point, direction);
        HapticPlugin.effects_type(device.configName, effectId, 0); // 0 = CONSTANT

        if (!effectPlaying)
        {
            HapticPlugin.effects_startEffect(device.configName, effectId);
            effectPlaying = true;
        }
    }

    private void StopForce()
    {
        if (!effectPlaying)
            return;

        effectPlaying = false;
        if (device != null && device.hHD >= 0 && effectId >= 0)
            HapticPlugin.effects_stopEffect(device.configName, effectId);
    }

    private void OnDisable() => StopForce();
    private void OnApplicationQuit() => StopForce();
    private void OnDestroy() => StopForce();
#endif
}
