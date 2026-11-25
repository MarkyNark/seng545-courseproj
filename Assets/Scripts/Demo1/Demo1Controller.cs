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
/// Placement and failed-drop motions are visual animations, not rigidbody physics.
/// </summary>
public sealed class Demo1Controller : MonoBehaviour
{
    public enum InputMode { Keyboard, Haptics, Auto }
    public enum ApplicationMode { MassA, SpringB }

    [Header("Select application before starting a trial (1=A, 2=B)")]
    public ApplicationMode applicationMode = ApplicationMode.MassA;

    [Header("App B keyboard-only spring cue (not physical haptic feedback)")]
    [Range(0f, 0.3f)] public float springKeyboardDisplacement = 0.12f;
    [Range(1f, 10f)] public float springKeyboardFrequencyHz = 4.5f;
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

    [Header("Scene references (auto-found if empty)")]
    public Transform binAStandard;
    public Transform binBReject;

    [Header("Spawn points (separate objects, found by Tag if not assigned)")]
    [Tooltip("The Box that samples are cloned from / appear on. Falls back to the object named 'Box'.")]
    public Transform boxSpawnPoint;
    public string boxTag = "Box";
    [Tooltip("Empty GameObject marking where the cyan cursor starts. Falls back to 0.2 above the Box.")]
    public Transform cursorSpawnPoint;
    public string cursorSpawnTag = "CursorSpawn";

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
    private Demo1SpringHapticEffect springFeedback;
    private float pickupTime;
    private Transform cursorVisual;
    private DemoCanister held;
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
        springFeedback = gameObject.GetComponent<Demo1SpringHapticEffect>();
        if (springFeedback == null)
            springFeedback = gameObject.AddComponent<Demo1SpringHapticEffect>();

        if (cursorSpawnPoint == null)
            cursorSpawnPoint = FindByTag(cursorSpawnTag);
        initialCursorPosition = cursorSpawnPoint != null
            ? cursorSpawnPoint.position
            : spawnCenter + Vector3.up * 0.2f;
        if (cursorSpawnPoint == null)
            Debug.LogWarning("Demo1: No object tagged '" + cursorSpawnTag +
                             "' found; cursor spawns 0.2 above the Box instead.");
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

    // Tag lookup that doesn't throw if the tag hasn't been created in Tags & Layers.
    private static Transform FindByTag(string tagName)
    {
        if (string.IsNullOrEmpty(tagName)) return null;
        try
        {
            GameObject found = GameObject.FindWithTag(tagName);
            return found != null ? found.transform : null;
        }
        catch (UnityException)
        {
            Debug.LogWarning("Demo1: Tag '" + tagName + "' is not defined. Add it under Tags & Layers.");
            return null;
        }
    }

    private bool SetupAuthoredScene()
    {
        string[] boxNames = { "Box", "Box (1)", "Box (2)" };
        foreach (string name in boxNames)
        {
            GameObject box = GameObject.Find(name);
            if (box != null) sourceBoxes.Add(box);
        }
        if (boxSpawnPoint == null)
            boxSpawnPoint = FindByTag(boxTag);
        GameObject primary = boxSpawnPoint != null ? boxSpawnPoint.gameObject : GameObject.Find("Box");
        if (primary == null && sourceBoxes.Count > 0)
            primary = sourceBoxes[0];
        GameObject firstBin = GameObject.Find("bin (1)");
        GameObject secondBin = GameObject.Find("bin (2)");
        if (primary == null || firstBin == null || secondBin == null)
            return false;

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

            // Never change experimental conditions during a trial or practice.
            if (state == TrialState.Ready)
            {
                if (kb.digit1Key.wasPressedThisFrame)
                {
                    applicationMode = ApplicationMode.MassA;
                    lastMessage = "App A selected. Press ENTER for trial or P for practice.";
                }
                else if (kb.digit2Key.wasPressedThisFrame)
                {
                    applicationMode = ApplicationMode.SpringB;
                    lastMessage = "App B selected. Press ENTER for trial or P for practice.";
                }
            }

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
            if (springFeedback != null) springFeedback.SetSpringHeld(false);
            return;
        }

        if (held == null && press) TryGrab(cursor);
        if (held != null)
        {
            Vector3 desiredCenter = cursor + holdOffset;
            if (applicationMode == ApplicationMode.SpringB && held.anomalous && source == keyboardInput)
            {
                // Development-only visible/kinematic proxy for spring feedback.
                // A keyboard cannot reproduce physical forces; this should not
                // be used to collect real haptic participant responses.
                float elapsed = Time.unscaledTime - pickupTime;
                float wobble = springKeyboardDisplacement *
                    Mathf.Sin(elapsed * 2f * Mathf.PI * springKeyboardFrequencyHz);
                desiredCenter += Camera.main != null
                    ? Camera.main.transform.right * wobble
                    : Vector3.right * wobble;
            }
            held.ShiftVisualCenterTo(desiredCenter);
            if (release) Release(cursor);
        }

        keyboardInput.movementMultiplier =
            applicationMode == ApplicationMode.MassA && held != null && held.anomalous
                ? anomalousKeyboardMovementRatio : 1f;

        bool physicalAnomalyHeld = source == hapticInput && held != null && held.anomalous;
        if (massFeedback != null)
            massFeedback.SetHeavyHeld(physicalAnomalyHeld && applicationMode == ApplicationMode.MassA);
        if (springFeedback != null)
            springFeedback.SetSpringHeld(physicalAnomalyHeld && applicationMode == ApplicationMode.SpringB);
    }

    private void StartPractice()
    {
        BeginSession(TrialState.Practice);
        lastMessage = applicationMode == ApplicationMode.MassA
            ? "Practice: distinguish heavy versus normal samples."
            : "Practice: distinguish bouncing versus smooth samples.";
    }

    private void StartTrial()
    {
        BeginSession(TrialState.Trial);
        sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        lastMessage = applicationMode == ApplicationMode.MassA
            ? "App A timed trial: sort ten normal or heavy samples."
            : "App B timed trial: sort ten smooth or bouncy samples.";
    }

    private void BeginSession(TrialState newState)
    {
        StopAllCoroutines();
        transitionInProgress = false;
        held = null;
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
        if (springFeedback != null) springFeedback.SetSpringHeld(false);
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
        currentSampleDrops = 0;
        keyboardInput.SetPosition(initialCursorPosition);
        keyboardInput.movementMultiplier = 1f;
        priorGrabHeld = true; // Require Space to be released before the next pickup.
    }

    private void HideAllSamples()
    {
        foreach (DemoCanister sample in samples)
            if (sample != null) sample.gameObject.SetActive(false);
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
        pickupTime = Time.unscaledTime;
        holdOffset = currentSample.VisibleBounds.center - cursor;
        lastMessage = "Holding a sample. LEFT bin = standard; RIGHT bin = reject.";
    }

    private void Release(Vector3 cursor)
    {
        DemoCanister droppedSample = held;
        held = null;
        if (massFeedback != null) massFeedback.SetHeavyHeld(false);
        if (springFeedback != null) springFeedback.SetSpringHeld(false);

        Bounds standard = RendererBounds(binAStandard);
        Bounds reject = RendererBounds(binBReject);
        float distanceA = HorizontalDistance(cursor, standard.center);
        float distanceB = HorizontalDistance(cursor, reject.center);
        bool nearA = distanceA <= BinRadius(standard);
        bool nearB = distanceB <= BinRadius(reject);
        if (nearA && nearB) { nearA = distanceA <= distanceB; nearB = !nearA; }

        if (!nearA && !nearB)
        {
            drops++;
            currentSampleDrops++;
            lastMessage = "Dropped outside a bin. Sample returns to conveyor; try again.";
            StartCoroutine(AnimateTo(droppedSample, spawnCenter, false, false));
            return;
        }

        bool placedInReject = nearB;
        bool correctPlacement = placedInReject == droppedSample.anomalous;
        Bounds chosenBin = placedInReject ? reject : standard;
        Vector3 finalCenter = new Vector3(chosenBin.center.x,
            chosenBin.max.y - Mathf.Min(droppedSample.VisibleBounds.extents.y, 0.3f),
            chosenBin.center.z);

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
        lastMessage = correctPlacement ? "CORRECT: sample classified." : "INCORRECT: sample placed in the wrong bin.";
        StartCoroutine(AnimateTo(droppedSample, finalCenter, true, true));
    }

    private IEnumerator AnimateTo(DemoCanister sample, Vector3 endCenter, bool sorted, bool moveOn)
    {
        transitionInProgress = true;
        Vector3 startCenter = sample.VisibleBounds.center;
        float duration = Mathf.Max(0.05f, placementAnimationSeconds);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            sample.ShiftVisualCenterTo(Vector3.Lerp(startCenter, endCenter, t));
            yield return null;
        }
        sample.ShiftVisualCenterTo(endCenter);
        if (sorted)
        {
            sample.gameObject.SetActive(false);
            currentSample = null;
        }
        transitionInProgress = false;
        if (moveOn) ShowNextSample();
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
        if (springFeedback != null) springFeedback.SetSpringHeld(false);
        float accuracy = processed > 0 ? (100f * correct / processed) : 0f;
        lastMessage = string.Format("FINISHED: {0}/{1} correct ({2:0}%), {3} drops, {4:0.0} seconds.",
            correct, processed, accuracy, drops, finalTrialSeconds);
        Debug.Log("Demo1 V4: " + lastMessage);
        bool touchWasSelected = SelectedInput() == hapticInput && hapticInput != null && hapticInput.IsAvailable;
        string trialMode = applicationMode == ApplicationMode.MassA ? "AppA_Mass" : "AppB_Spring";
        string inputSource = touchWasSelected ? "_Touch" : "_Keyboard";
        exportedCsvPath = Demo1SessionLogger.SaveTrial(
            trialMode + inputSource, sessionId, trialRecords, correct, drops, finalTrialSeconds);
    }

    private void ResetToReady()
    {
        StopAllCoroutines();
        held = null;
        priorGrabHeld = false;
        transitionInProgress = false;
        if (massFeedback != null) massFeedback.SetHeavyHeld(false);
        if (springFeedback != null) springFeedback.SetSpringHeld(false);
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
        if (springFeedback != null) springFeedback.SetSpringHeld(false);
    }

    private void OnGUI()
    {
        if (initializationFailed) return;
        Matrix4x4 oldMatrix = GUI.matrix;
        float scale = Mathf.Clamp(Screen.width / 1100f, 1f, 3f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        string appTitle = applicationMode == ApplicationMode.MassA
            ? "APP A | Mass-Based Identification" : "APP B | Bouncy/Spring Identification";
        GUI.Box(new Rect(8, 8, 650, 188), appTitle + " (keyboard simulator)");
        string sourceName = SelectedInput() == hapticInput ? "Physical Touch" : "Keyboard";
        GUI.Label(new Rect(22, 31, 630, 22), "Mode: " + state + " | Input: " + sourceName + " | Left: STANDARD | Right: REJECT");
        GUI.Label(new Rect(22, 53, 630, 22), "WASD move | Q/E up/down | Hold SPACE grab | SHIFT fast | R reset");
        GUI.Label(new Rect(22, 75, 630, 22), "ENTER trial | P practice | 1=Mass A, 2=Spring B (when ready) | TAB input");

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
            metrics = "READY | 10 identical-looking samples | 5 standard + 5 anomalous";

        GUI.Label(new Rect(22, 97, 630, 22), metrics);
        GUI.Label(new Rect(22, 119, 630, 22), lastMessage);
        if (showDebugClassWhileHeld && currentSample != null)
            GUI.Label(new Rect(22, 141, 630, 22), "DEVELOPER ONLY — current: " +
                (currentSample.anomalous
                    ? (applicationMode == ApplicationMode.MassA ? "HEAVY / REJECT" : "BOUNCY / REJECT")
                    : "STANDARD"));
        else
            GUI.Label(new Rect(22, 141, 630, 22),
                applicationMode == ApplicationMode.MassA
                    ? "Mass keyboard cue: heavy samples move slower."
                    : "Spring keyboard cue: bouncy samples oscillate while grabbed (visual proxy).");
        if (!string.IsNullOrEmpty(exportedCsvPath))
            GUI.Label(new Rect(22, 163, 630, 22), "CSV saved. Full location printed in Unity Console.");
        GUI.matrix = oldMatrix;
    }
}