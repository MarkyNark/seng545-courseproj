using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-step Demo 1 bootstrap for the team's existing SampleScene.
/// No Inspector wiring or scene edits are necessary for the keyboard prototype.
/// Unity invokes this method once when entering Play mode, after the scene loads.
/// </summary>
public static class Demo1AutoStart
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnSampleScene()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene")
            return;

        // If a manager was manually created previously, use it rather than
        // creating a duplicate controller in the same scene.
        if (Object.FindFirstObjectByType<Demo1Controller>() != null)
            return;

        GameObject manager = new GameObject("Demo1Manager (automatic, Play mode only)");
        manager.AddComponent<Demo1Controller>();
        Debug.Log("Demo1: Sorting controller automatically installed. " +
                  "Use WASD/EQ, hold SPACE to grab, release over a bin; press R to reset.");
    }
}
