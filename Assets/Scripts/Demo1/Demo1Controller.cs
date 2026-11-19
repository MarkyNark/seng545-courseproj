using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mass-based canister sorting for ENSF 545 Demo 1.
///
/// The same sorting rules work with either IStylusInput implementation.
/// This keyboard-completion version leaves OpenHaptics scripts unchanged.
/// It creates ten identical-looking samples from the team's existing Box mesh.
/// Only one sample is presented at a time to avoid changing the authored scene.
///
/// Keyboard resistance is a SUBSTITUTE cue, not a physical haptic force.
/// V5 uses real Rigidbody/Collider physics for carried and released canisters.
/// Scoring still uses the selected bin's horizontal footprint for demo reliability.
/// </summary>
public sealed class Demo1Controller : MonoBehaviour
{
    public enum InputMode { Keyboard, Haptics, Auto }
    private enum TrialState { Ready, Practice, Trial, Complete }

    [Header("Input adapter - Keyboard is the safe default for local testing")]
    public InputMode inputMode = InputMode.Keyboard;
    public KeyboardStylusInput keyboardInput;
    public OpenHapticsStylusInput hapticInput;

    [Header("Trial settings")]
    [Min(1)] public int trialSampleCount = 10;
    [Min(5f)] public float practiceSeconds = 60f;
    [Min(0.1f)] public float grabRange = 1f;
    [Min(0.1f)] public float binExtraRadius = 0.3f;
    [Range(0.1f, 1f)] public float anomalousKeyboardMovementRatio = 0.35f;
    [Min(0.05f)] public float placementAnimationSeconds = 0.35f;
    public bool showCursor = true;
    public bool showDebugClassWhileHeld = false;

    [Header("V5 collision-aware movement")]
    [Min(1f)] public float heldFollowResponsiveness = 18f;
    [Min(0.5f)] public float heldMaxSpeed = 9f;
    [Min(0.1f)] public float releasePhysicsSeconds = 0.8f;

    [Header("Scene references (auto-found if empty)")]
    public Transform binAStandard;
    public Transform binBReject;

    /// <summary>One completed placement in a timed mass-sorting trial.</summary>
    public struct SortRecord
    {
        public int CanisterNumber;
        public bool WasAnomalous;
        public bool PlacedInRejectBin;
        public bool Correct;
        public int DropsBeforeSort;
        public float ElapsedSeconds;
    }

    private readonly List<DemoCanister> samples = new List<DemoCanister>();
    private readonly List<SortRecord> trialRecords = new List<SortRecord>();
    private readonly List<GameObject> sourceBoxes = new List<GameObject>();
    private Demo1MassHapticEffect massFeedback;
    private Transform cursorVisual;
    private DemoCanister held;
    private Rigidbody heldBody;
    private Vector3 desiredHeldCenter;
    private DemoCanister currentSample;
    private Vector3 holdOffset;
    private Vector3 spawnCenter;
    private Vector3 initialCursorPosition;
    private bool priorGrabHeld;
    private bool transitionInProgress;
    private bool initializationFailed;
    private bool[] classification;
    private TrialState state = TrialState.Ready;
    private float stateStartTime;
    private float finalTrialSeconds;
    private int processed;
    private int correct;
    private int drops;
    private int currentSampleDrops;
    private int practicePlacements;
    private string lastMessage = "Press ENTER for ten samples, or P for 60 seconds of practice.";
    private string exportedCsvPath;
    private string sessionId;

    private void Awake()
    {
        if (!SetupAuthoredScene())
        {
            initializationFailed = true;
            Debug.LogError("Demo1 V4: Existing Box and two bin objects are required in SampleScene.");
            return;
        }

        if (keyboardInput == null)
            keyboardInput = gameObject.AddComponent<KeyboardStylusInput>();
        if (hapticInput == null)
            hapticInput = gameObject.AddComponent<OpenHapticsStylusInput>();
        massFeedback = gameObject.GetComponent<Demo1MassHapticEffect>();
        if (massFeedback == null)
            massFeedback = gameObject.AddComponent<Demo1MassHapticEffect>();

        initialCursorPosition = spawnCenter + Vector3.up * 0.2f;
        keyboardInput.SetPosition(initialCursorPosition);

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Demo Stylus Cursor (runtime only)";
        sphere.transform.localScale = Vector3.one * 0.22f;
        Collider cursorCollider = sphere.GetComponent<Collider>();
        if (cursorCollider != null) Destroy(cursorCollider);
        Renderer sphereRenderer = sphere.GetComponent<Renderer>();
        if (sphereRenderer != null) sphereRenderer.material.color = Color.cyan;
        cursorVisual = sphere.transform;

        // Ready state deliberately presents no samples until the user starts.
        HideAllSamples();
    }

    private bool SetupAuthoredScene()
    {
        string[] boxNames = { "Box", "Box (1)", "Box (2)" };
        foreach (string name in boxNames)
        {
            GameObject box = GameObject.Find(name);
            if (box != null) sourceBoxes.Add(box);
        }
        GameObject primary = GameObject.Find("Box");
        if (primary == null && sourceBoxes.Count > 0)
            primary = sourceBoxes[0];
        GameObject firstBin = GameObject.Find("bin (1)");
        GameObject secondBin = GameObject.Find("bin (2)");
        if (primary == null || firstBin == null || secondBin == null)
            return false;

        // Static triangle-mesh colliders preserve the imported floor, wall,
        // conveyor, and open-bin shapes. They are created at runtime only.
        Demo1PhysicsSetup.AddEnvironmentColliders();

        if (binAStandard == null || binBReject == null)
        {
            Vector3 screenRight = Camera.main != null ? Camera.main.transform.right : Vector3.right;
            float first = Vector3.Dot(RendererBounds(firstBin.transform).center, screenRight);
            float second = Vector3.Dot(RendererBounds(secondBin.transform).center, screenRight);
            binAStandard = first <= second ? firstBin.transform : secondBin.transform;
            binBReject = first <= second ? secondBin.transform : firstBin.transform;
        }

        spawnCenter = RendererBounds(primary.transform).center;
        trialSampleCount = Mathf.Clamp(trialSampleCount, 1, 100);

        // Use the very same mesh/material for every sample: classification is
        // not leaked through a different model, colour or lid geometry.
        for (int i = 0; i < trialSampleCount; i++)
        {
            GameObject sampleObject = Instantiate(primary, primary.transform.parent);
            sampleObject.name = "Demo1 sample " + (i + 1);
            sampleObject.transform.SetPositionAndRotation(primary.transform.position, primary.transform.rotation);
            sampleObject.transform.localScale = primary.transform.localScale;
            DemoCanister sample = sampleObject.GetComponent<DemoCanister>();
            if (sample == null) sample = sampleObject.AddComponent<DemoCanister>();
            Demo1PhysicsSetup.AddSampleBody(sample);
            // Existing DemoCanister records its original transform in Awake.
            samples.Add(sample);
            sampleObject.SetActive(false);
        }

        // Keep the authored originals in the scene, but remove them from play.
        // They return automatically when exiting Play mode.
        foreach (GameObject box in sourceBoxes)
            box.SetActive(false);
        classification = new bool[samples.Count];
        return samples.Count != 0;
    }

    private static Bounds RendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds result = new Bounds(root.position, Vector3.zero);
        bool hasRenderer = false;
        foreach (Renderer item in renderers)
        {
            if (!item.enabled) continue;
            if (!hasRenderer) { result = item.bounds; hasRenderer = true; }
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

    // Apply motion in the physics step so wall/floor/bin collisions can respond.
    // Do not set the held canister's Transform position every Update.
    private void FixedUpdate()
    {
        if (held == null || heldBody == null || transitionInProgress) return;
        Vector3 error = desiredHeldCenter - held.VisibleBounds.center;
        Vector3 velocity = Vector3.ClampMagnitude(
            error * heldFollowResponsiveness, heldMaxSpeed);
        heldBody.linearVelocity = velocity;
        heldBody.angularVelocity = Vector3.zero;
    }

    private void Update()
    {
        if (initializationFailed || keyboardInput == null) return;
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.rKey.wasPressedThisFrame) ResetToReady();
            if (kb.dKey.wasPressedThisFrame &&
                (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
                showDebugClassWhileHeld = !showDebugClassWhileHeld;
            if (kb.tabKey.wasPressedThisFrame)
                inputMode = inputMode == InputMode.Keyboard ? InputMode.Auto : InputMode.Keyboard;

            if (!transitionInProgress &&
                (state == TrialState.Ready || state == TrialState.Complete))
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                    StartTrial();
                else if (kb.pKey.wasPressedThisFrame)
                    StartPractice();
            }
        }

        keyboardInput.SimulateFrame(Time.deltaTime);
        IStylusInput source = SelectedInput();
        if (source == null || !source.IsAvailable) return;
        Vector3 cursor = source.WorldPosition;
        if (cursorVisual != null)
        {
            cursorVisual.gameObject.SetActive(showCursor);
            cursorVisual.position = cursor;
        }

        if (state == TrialState.Practice &&
            Time.unscaledTime - stateStartTime >= practiceSeconds)
        {
            ResetToReady();
            lastMessage = "Practice finished. Press ENTER to begin the timed trial.";
        }

        bool grab = source.GrabHeld;
        bool press = grab && !priorGrabHeld;
        bool release = !grab && priorGrabHeld;
        priorGrabHeld = grab;

        bool running = state == TrialState.Practice || state == TrialState.Trial;
        if (!running || transitionInProgress)
        {
            keyboardInput.movementMultiplier = 1f;
            if (massFeedback != null) massFeedback.SetHeavyHeld(false);
            return;
        }

        if (held == null && press) TryGrab(cursor);
        if (held != null)
        {
            desiredHeldCenter = cursor + holdOffset;
            if (release) Release();
        }

        keyboardInput.movementMultiplier = held != null && held.anomalous
            ? anomalousKeyboardMovementRatio : 1f;
        if (massFeedback != null)
            massFeedback.SetHeavyHeld(source == hapticInput && held != null && held.anomalous);
    }

    private void StartPractice()
    {
        BeginSession(TrialState.Practice);
        lastMessage = "Practice running: feel the virtual heavy cue and sort samples.";
    }

    private void StartTrial()
    {
        BeginSession(TrialState.Trial);
        sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        lastMessage = "Timed trial started. Sort all ten visually identical samples.";
    }

    private void BeginSession(TrialState newState)
    {
        StopAllCoroutines();
        transitionInProgress = false;
        held = null;
        heldBody = null;
        priorGrabHeld = false;
        processed = 0;
        correct = 0;
        drops = 0;
        practicePlacements = 0;
        currentSampleDrops = 0;
        finalTrialSeconds = 0;
        exportedCsvPath = null;
        trialRecords.Clear();
        if (massFeedback != null) massFeedback.SetHeavyHeld(false);
        keyboardInput.movementMultiplier = 1f;
        keyboardInput.SetPosition(initialCursorPosition);
        HideAllSamples();

        // Exactly half of the ten objects are anomalous by default.
        // Shuffle positions to avoid a fixed predictable pattern.
        int anomalousCount = samples.Count / 2;
        for (int i = 0; i < classification.Length; i++)
            classification[i] = i < anomalousCount;
        for (int i = 0; i < classification.Length; i++)
        {
            int swapIndex = UnityEngine.Random.Range(i, classification.Length);
            bool value = classification[i];
            classification[i] = classification[swapIndex];
            classification[swapIndex] = value;
        }

        state = newState;
        stateStartTime = Time.unscaledTime;
        ShowNextSample();
    }

    private void ShowNextSample()
    {
        if (state == TrialState.Trial && processed >= samples.Count)
        {
            CompleteTrial();
            return;
        }
        if (state != TrialState.Trial && state != TrialState.Practice)
            return;

        HideAllSamples();
        int index = state == TrialState.Trial ? processed : practicePlacements % samples.Count;
        currentSample = samples[index];
        currentSample.ResetCanister();
        currentSample.anomalous = state == TrialState.Trial
            ? classification[index]
            : practicePlacements % 2 == 1;
        currentSample.gameObject.SetActive(true);
        currentSample.ShiftVisualCenterTo(spawnCenter);
        Demo1PhysicsSetup.ReleaseAtRest(currentSample);
        currentSampleDrops = 0;
        keyboardInput.SetPosition(initialCursorPosition);
        keyboardInput.movementMultiplier = 1f;
        priorGrabHeld = true; // Require Space to be released before the next pickup.
    }

    private void HideAllSamples()
    {
        foreach (DemoCanister sample in samples)
            if (sample != null)
            {
                Demo1PhysicsSetup.Freeze(sample);
                sample.gameObject.SetActive(false);
            }
        currentSample = null;
    }

    private void TryGrab(Vector3 cursor)
    {
        if (currentSample == null || currentSample.Sorted) return;
        Bounds bounds = currentSample.VisibleBounds;
        float distance = Vector3.Distance(bounds.ClosestPoint(cursor), cursor);
        if (distance > grabRange)
        {
            lastMessage = "Move the cyan cursor closer before grabbing.";
            return;
        }
        held = currentSample;
        heldBody = held.GetComponent<Rigidbody>();
        if (heldBody == null)
        {
            Debug.LogError("Demo1 V5: Missing Rigidbody on sample.");
            held = null;
            return;
        }
        Demo1PhysicsSetup.BeginGrab(heldBody);
        holdOffset = currentSample.VisibleBounds.center - cursor;
        desiredHeldCenter = currentSample.VisibleBounds.center;
        lastMessage = "Holding a sample. Physics prevents passing through solid objects.";
    }

    private void Release()
    {
        DemoCanister droppedSample = held;
        Rigidbody releasedBody = heldBody;
        held = null;
        heldBody = null;
        if (massFeedback != null) massFeedback.SetHeavyHeld(false);
        Demo1PhysicsSetup.EndGrab(releasedBody);

        // Score where the actual canister was released, NOT where the
        // unconstrained stylus cursor went through a wall.
        Vector3 objectCenter = droppedSample.VisibleBounds.center;
        Bounds standard = RendererBounds(binAStandard);
        Bounds reject = RendererBounds(binBReject);
        float distanceA = HorizontalDistance(objectCenter, standard.center);
        float distanceB = HorizontalDistance(objectCenter, reject.center);
        bool nearA = distanceA <= BinRadius(standard);
        bool nearB = distanceB <= BinRadius(reject);
        if (nearA && nearB) { nearA = distanceA <= distanceB; nearB = !nearA; }

        if (!nearA && !nearB)
        {
            drops++;
            currentSampleDrops++;
            lastMessage = "Dropped outside a bin. Gravity applies, then the sample resets.";
            StartCoroutine(WaitForPhysicsRelease(droppedSample, false));
            return;
        }

        bool placedInReject = nearB;
        bool correctPlacement = placedInReject == droppedSample.anomalous;
        if (state == TrialState.Trial)
        {
            processed++;
            if (correctPlacement) correct++;
            trialRecords.Add(new SortRecord
            {
                CanisterNumber = processed,
                WasAnomalous = droppedSample.anomalous,
                PlacedInRejectBin = placedInReject,
                Correct = correctPlacement,
                DropsBeforeSort = currentSampleDrops,
                ElapsedSeconds = Time.unscaledTime - stateStartTime
            });
        }
        else
        {
            practicePlacements++;
        }
        droppedSample.MarkSorted();
        lastMessage = correctPlacement ? "CORRECT: sample classified." : "INCORRECT: wrong bin.";
        StartCoroutine(WaitForPhysicsRelease(droppedSample, true));
    }

    // Leave the Rigidbody free to fall/collide before advancing or recovering.
    // This replaces the older transform-based animations that passed through walls.
    private IEnumerator WaitForPhysicsRelease(DemoCanister sample, bool sorted)
    {
        transitionInProgress = true;
        yield return new WaitForSeconds(releasePhysicsSeconds);
        if (sample == null) yield break;

        if (sorted)
        {
            sample.gameObject.SetActive(false);
            currentSample = null;
            transitionInProgress = false;
            ShowNextSample();
        }
        else
        {
            // Keep the same sample and its accumulated drop count.
            bool wasHeavy = sample.anomalous;
            Demo1PhysicsSetup.Freeze(sample);
            sample.ResetCanister();
            sample.anomalous = wasHeavy;
            sample.gameObject.SetActive(true);
            sample.ShiftVisualCenterTo(spawnCenter);
            Demo1PhysicsSetup.ReleaseAtRest(sample);
            keyboardInput.SetPosition(initialCursorPosition);
            keyboardInput.movementMultiplier = 1f;
            priorGrabHeld = true; // Require a fresh button press.
            transitionInProgress = false;
        }
    }

    private static float HorizontalDistance(Vector3 one, Vector3 two)
    {
        return Vector2.Distance(new Vector2(one.x, one.z), new Vector2(two.x, two.z));
    }

    private float BinRadius(Bounds bin)
    {
        return Mathf.Max(bin.extents.x, bin.extents.z) + binExtraRadius;
    }

    private void CompleteTrial()
    {
        finalTrialSeconds = Time.unscaledTime - stateStartTime;
        state = TrialState.Complete;
        HideAllSamples();
        keyboardInput.movementMultiplier = 1f;
        if (massFeedback != null) massFeedback.SetHeavyHeld(false);
        float accuracy = processed > 0 ? (100f * correct / processed) : 0f;
        lastMessage = string.Format("FINISHED: {0}/{1} correct ({2:0}%), {3} drops, {4:0.0} seconds.",
            correct, processed, accuracy, drops, finalTrialSeconds);
        Debug.Log("Demo1 V4: " + lastMessage);
        exportedCsvPath = Demo1SessionLogger.SaveMassTrial(sessionId,
            trialRecords, correct, drops, finalTrialSeconds);
    }

    private void ResetToReady()
    {
        StopAllCoroutines();
        held = null;
        heldBody = null;
        priorGrabHeld = false;
        transitionInProgress = false;
        if (massFeedback != null) massFeedback.SetHeavyHeld(false);
        if (keyboardInput != null)
        {
            keyboardInput.movementMultiplier = 1f;
            keyboardInput.SetPosition(initialCursorPosition);
        }
        HideAllSamples();
        processed = 0;
        correct = 0;
        drops = 0;
        practicePlacements = 0;
        currentSampleDrops = 0;
        finalTrialSeconds = 0;
        exportedCsvPath = null;
        trialRecords.Clear();
        state = TrialState.Ready;
        lastMessage = "Ready. ENTER starts a 10-sample trial; P starts 60-second practice.";
    }

    private void OnDisable()
    {
        if (massFeedback != null) massFeedback.SetHeavyHeld(false);
    }

    private void OnGUI()
    {
        if (initializationFailed) return;
        Matrix4x4 oldMatrix = GUI.matrix;
        float scale = Mathf.Clamp(Screen.width / 1100f, 1f, 3f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        GUI.Box(new Rect(8, 8, 650, 188), "APP A | Mass-Based Identification (V5 physics)");
        string sourceName = SelectedInput() == hapticInput ? "Physical Touch" : "Keyboard";
        GUI.Label(new Rect(22, 31, 630, 22), "Mode: " + state + " | Input: " + sourceName + " | Left: STANDARD | Right: REJECT");
        GUI.Label(new Rect(22, 53, 630, 22), "WASD move | Q/E up/down | Hold SPACE grab | SHIFT fast | R reset");
        GUI.Label(new Rect(22, 75, 630, 22), "ENTER timed trial | P 60s practice | TAB toggle input | Ctrl+D debug class");

        string metrics;
        if (state == TrialState.Trial)
            metrics = string.Format("Sample: {0}/{1} | Correct: {2}/{3} | Drops: {4} | Time: {5:0.0}s",
                Mathf.Min(processed + 1, samples.Count), samples.Count, correct, processed, drops,
                Time.unscaledTime - stateStartTime);
        else if (state == TrialState.Practice)
            metrics = string.Format("Practice: {0:0}s left | Completed samples: {1} | Drops: {2}",
                Mathf.Max(0f, practiceSeconds - (Time.unscaledTime - stateStartTime)), practicePlacements, drops);
        else if (state == TrialState.Complete)
            metrics = string.Format("FINISHED | Correct: {0}/{1} | Accuracy: {2:0}% | Drops: {3} | Time: {4:0.0}s",
                correct, processed, processed > 0 ? 100f * correct / processed : 0f, drops, finalTrialSeconds);
        else
            metrics = "READY | 10 identical-looking samples | 5 standard + 5 heavy";

        GUI.Label(new Rect(22, 97, 630, 22), metrics);
        GUI.Label(new Rect(22, 119, 630, 22), lastMessage);
        if (showDebugClassWhileHeld && currentSample != null)
            GUI.Label(new Rect(22, 141, 630, 22), "DEVELOPER ONLY — current: " +
                (currentSample.anomalous ? "HEAVY / REJECT" : "NORMAL / STANDARD"));
        else
            GUI.Label(new Rect(22, 141, 630, 22), "Boxes collide with solids and fall under gravity; mass cue remains keyboard-only.");
        if (!string.IsNullOrEmpty(exportedCsvPath))
            GUI.Label(new Rect(22, 163, 630, 22), "CSV saved. Full location printed in Unity Console.");
        GUI.matrix = oldMatrix;
    }
}
