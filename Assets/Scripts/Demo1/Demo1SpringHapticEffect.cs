using UnityEngine;

/// <summary>
/// Optional low-amplitude oscillating force cue for App B.
/// Default OFF. Requires the restored course OpenHaptics plugin and actual
/// Touch hardware. This approximates a bouncing cue; it is not a validated
/// spring-damper force model and must be tested/calibrated before a user study.
/// </summary>
public sealed class Demo1SpringHapticEffect : MonoBehaviour
{
    [Header("Physical Touch spring cue - keep off until hardware validation")]
    public bool enablePhysicalForce = false;
    [Range(0f, 0.15f)] public float maximumMagnitude = 0.04f;
    [Range(1f, 10f)] public float frequencyHz = 4f;

    private bool springHeld;
    public void SetSpringHeld(bool held) => springHeld = held;

#if SENG545_OPENHAPTICS
    private HapticPlugin device;
    private int effectId = -1;
    private bool effectPlaying;
    private string effectDeviceName;

    private void Update()
    {
        if (!enablePhysicalForce || !springHeld)
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

        if (effectDeviceName != null && effectDeviceName != device.configName)
        {
            StopForce();
            effectId = -1;
        }
        if (effectId < 0)
        {
            effectId = HapticPlugin.effects_assignEffect(device.configName);
            if (effectId < 0) return;
            effectDeviceName = device.configName;
        }

        // Alternating low-magnitude upward/downward resistance. Actual perceived
        // force depends on device coordinates and must be validated in the lab.
        float wave = Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * frequencyHz);
        Vector3 localUp = device.transform.InverseTransformDirection(Vector3.up).normalized;
        Vector3 signedDirection = wave < 0f ? -localUp : localUp;
        double[] point = { 0.0, 0.0, 0.0 };
        double[] direction = { signedDirection.x, signedDirection.y, signedDirection.z };
        HapticPlugin.effects_settings(device.configName, effectId,
            0.0, maximumMagnitude * Mathf.Abs(wave), 0.0, point, direction);
        HapticPlugin.effects_type(device.configName, effectId, 0); // CONSTANT, updated over time
        if (!effectPlaying)
        {
            HapticPlugin.effects_startEffect(device.configName, effectId);
            effectPlaying = true;
        }
    }

    private void StopForce()
    {
        if (!effectPlaying) return;
        effectPlaying = false;
        if (device != null && device.hHD >= 0 && effectId >= 0)
            HapticPlugin.effects_stopEffect(device.configName, effectId);
    }
    private void OnDisable() => StopForce();
    private void OnDestroy() => StopForce();
    private void OnApplicationQuit() => StopForce();
#endif
}
