using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fast, non-destructive Demo 1 prototype for the existing SampleScene.
/// Uses either keyboard or the optional OpenHaptics stylus to drive identical
/// grab/sort behavior. Physical force output still needs separate hardware setup.
/// </summary>
public sealed class Demo1Controller : MonoBehaviour
{
    public enum InputMode { Keyboard, Haptics, Auto }

    [Header("Choose controller (can change during Play mode)")]
    public InputMode inputMode = InputMode.Auto;
    public KeyboardStylusInput keyboardInput;
    public OpenHapticsStylusInput hapticInput;
    private Demo1MassHapticEffect massFeedback;

    [Header("Existing scene objects (auto-found by name if empty)")]
    public DemoCanister[] canisters;
    public Transform binAStandard;
    public Transform binBReject;

    [Header("Demo tuning")]
    [Min(0.1f)] public float grabRange = 1f;
    [Min(0.1f)] public float binExtraRadius = 0.3f;
    [Range(0.1f, 1f)] public float anomalousKeyboardMovementRatio = 0.35f;
    public bool showDebugClassWhileHeld = true;
    public bool showCursor = true;

    private Transform cursorVisual;
    private DemoCanister held;
    private bool priorGrabHeld;
    private Vector3 holdOffset;
    private Vector3 initialCursorPosition;
    private int processed;
    private int correct;
    private string lastMessage = "Move the cursor to a box and hold SPACE to pick it up.";
    private bool initializationFailed;

    private void Awake()
    {
        FindSceneObjects();
        if (initializationFailed) return;

        if (keyboardInput == null)
            keyboardInput = gameObject.AddComponent<KeyboardStylusInput>();
        if (hapticInput == null)
            hapticInput = gameObject.AddComponent<OpenHapticsStylusInput>();
        massFeedback = gameObject.AddComponent<Demo1MassHapticEffect>();

        initialCursorPosition = canisters[0].VisibleBounds.center + Vector3.up * 0.2f;
        keyboardInput.SetPosition(initialCursorPosition);

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Demo Stylus Cursor (runtime only)";
        sphere.transform.localScale = Vector3.one * 0.22f;
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.cyan;
        cursorVisual = sphere.transform;
    }

    private void FindSceneObjects()
    {
        if (canisters == null || canisters.Length == 0)
        {
            List<DemoCanister> found = new List<DemoCanister>();
            // The three names below are taken directly from the existing scene.
            string[] names = { "Box", "Box (1)", "Box (2)" };
            foreach (string name in names)
            {
                GameObject obj = GameObject.Find(name);
                if (obj == null) { Debug.LogError("Demo1: Missing object " + name); continue; }
                DemoCanister canister = obj.GetComponent<DemoCanister>();
                if (canister == null) canister = obj.AddComponent<DemoCanister>();
                canister.anomalous = name == "Box (2)"; // 1 reject among 3 samples
                found.Add(canister);
            }
            canisters = found.ToArray();
        }

        if (binAStandard == null || binBReject == null)
        {
            GameObject first = GameObject.Find("bin (1)");
            GameObject second = GameObject.Find("bin (2)");
            if (first != null && second != null)
            {
                Camera view = Camera.main;
                Vector3 screenRight = view != null ? view.transform.right : Vector3.right;
                float a = Vector3.Dot(RendererBounds(first.transform).center, screenRight);
                float b = Vector3.Dot(RendererBounds(second.transform).center, screenRight);
                binAStandard = a <= b ? first.transform : second.transform;
                binBReject = a <= b ? second.transform : first.transform;
            }
        }

        if (canisters == null || canisters.Length == 0 ||
            binAStandard == null || binBReject == null)
        {
            initializationFailed = true;
            Debug.LogError("Demo1: Scene references missing. Assign 3 boxes and 2 bins in the Inspector.");
        }
    }

    private static Bounds RendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        bool found = false;
        Bounds result = new Bounds(root.position, Vector3.one);
        foreach (Renderer item in renderers)
        {
            if (!item.enabled) continue;
            if (!found) { result = item.bounds; found = true; }
            else result.Encapsulate(item.bounds);
        }
        return result;
    }

    private IStylusInput SelectedInput()
    {
        if (inputMode != InputMode.Keyboard && hapticInput != null && hapticInput.IsAvailable)
            return hapticInput;
        return keyboardInput;
    }

    private void Update()
    {
        if (initializationFailed || keyboardInput == null) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame) ResetDemo();
            if (keyboard.tabKey.wasPressedThisFrame)
                inputMode = inputMode == InputMode.Keyboard ? InputMode.Haptics : InputMode.Keyboard;
        }

        // Apply keyboard motion before sampling either input source, so the
        // stylus and the grabbed mesh see the same updated position.
        keyboardInput.SimulateFrame(Time.deltaTime);

        IStylusInput source = SelectedInput();
        if (source == null || !source.IsAvailable) return;
        Vector3 cursor = source.WorldPosition;

        if (cursorVisual != null)
        {
            cursorVisual.gameObject.SetActive(showCursor);
            cursorVisual.position = cursor;
        }

        bool grab = source.GrabHeld;
        bool press = grab && !priorGrabHeld;
        bool release = !grab && priorGrabHeld;
        priorGrabHeld = grab;

        if (held == null && press) TryGrab(cursor);
        if (held != null)
        {
            // Move the entire mesh assembly; keep the grab offset so it doesn't jump.
            held.ShiftVisualCenterTo(cursor + holdOffset);
            if (release) Release(cursor);
        }

        // Keyboard feedback is movement resistance ONLY, not actual haptic force.
        keyboardInput.movementMultiplier = held != null && held.anomalous
            ? anomalousKeyboardMovementRatio : 1f;

        // This is independent of keyboard movement slowdown. The actual Touch
        // device produces a modest downward force ONLY while a heavy sample
        // is held, if the operator has explicitly enabled force output.
        if (massFeedback != null)
            massFeedback.SetHeavyHeld(source == hapticInput && held != null && held.anomalous);
    }

    private void TryGrab(Vector3 cursor)
    {
        DemoCanister closest = null;
        float distance = float.PositiveInfinity;
        foreach (DemoCanister canister in canisters)
        {
            if (canister == null || canister.Sorted) continue;
            Bounds bounds = canister.VisibleBounds;
            float d = Vector3.Distance(bounds.ClosestPoint(cursor), cursor);
            if (d < distance) { closest = canister; distance = d; }
        }

        if (closest == null || distance > grabRange)
        {
            lastMessage = "No box within reach. Move the cyan cursor closer.";
            return;
        }

        held = closest;
        holdOffset = closest.VisibleBounds.center - cursor;
        lastMessage = "Holding a canister. Move to a bin; release SPACE to drop.";
    }

    private void Release(Vector3 cursor)
    {
        Bounds a = RendererBounds(binAStandard);
        Bounds b = RendererBounds(binBReject);
        float horizontalA = HorizontalDistance(cursor, a.center);
        float horizontalB = HorizontalDistance(cursor, b.center);
        bool nearA = horizontalA <= BinRadius(a);
        bool nearB = horizontalB <= BinRadius(b);
        if (nearA && nearB)
        {
            nearA = horizontalA <= horizontalB;
            nearB = !nearA;
        }

        if (!nearA && !nearB)
        {
            lastMessage = "Released outside a bin. Pick the canister up again.";
            held = null;
            return;
        }

        bool placedInA = nearA;
        bool rightBin = placedInA != held.anomalous;
        Bounds chosen = placedInA ? a : b;

        // Reposition the rendered container inside its selected bin.
        float top = chosen.max.y;
        Vector3 landingCenter = new Vector3(chosen.center.x,
            top - Mathf.Min(held.VisibleBounds.extents.y, 0.3f), chosen.center.z);
        held.ShiftVisualCenterTo(landingCenter);
        held.MarkSorted();
        processed++;
        if (rightBin) correct++;
        lastMessage = rightBin ? "CORRECT!" : "Incorrect bin. Try another canister.";
        held = null;
    }

    private static float HorizontalDistance(Vector3 one, Vector3 two)
    {
        return Vector2.Distance(new Vector2(one.x, one.z), new Vector2(two.x, two.z));
    }

    private float BinRadius(Bounds bounds)
    {
        return Mathf.Max(bounds.extents.x, bounds.extents.z) + binExtraRadius;
    }

    private void ResetDemo()
    {
        held = null;
        priorGrabHeld = false;
        processed = 0;
        correct = 0;
        foreach (DemoCanister canister in canisters)
            if (canister != null) canister.ResetCanister();
        keyboardInput.SetPosition(initialCursorPosition);
        keyboardInput.movementMultiplier = 1f;
        lastMessage = "Reset complete. Start with a canister on the conveyor.";
    }

    private void OnGUI()
    {
        if (initializationFailed) return;

        // At the repo's default QHD Game resolution, the Game tab is often
        // zoomed out. Scale the HUD so messages stay legible while testing.
        Matrix4x4 previousMatrix = GUI.matrix;
        float hudScale = Mathf.Clamp(Screen.width / 1100f, 1f, 3f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(hudScale, hudScale, 1f));
        GUI.Box(new Rect(8, 8, 610, 142), "Demo 1 | Mass-Based Identification");
        string sourceName = SelectedInput() == hapticInput ? "Haptic device" : "Keyboard simulator";
        GUI.Label(new Rect(22, 32, 580, 22), "Control: " + sourceName + " | Requested: " + inputMode);
        GUI.Label(new Rect(22, 52, 580, 22), "W/A/S/D: move | E/Q: up/down | SPACE: hold to grab | SHIFT: faster");
        GUI.Label(new Rect(22, 72, 580, 22), "TAB: switch input | R: reset | Left bin: standard | Right bin: reject");
        GUI.Label(new Rect(22, 92, 580, 22), "Sorted: " + processed + "/" + canisters.Length + " | Correct: " + correct + " | " + lastMessage);
        if (held != null && showDebugClassWhileHeld)
            GUI.Label(new Rect(22, 112, 580, 22), "DEVELOPER ONLY: " + (held.anomalous ? "HEAVY / ANOMALOUS" : "NORMAL"));
        else
            GUI.Label(new Rect(22, 112, 580, 22), "Keyboard movement resistance is a test cue, NOT physical force.");
        GUI.matrix = previousMatrix;
    }
}
