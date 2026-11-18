//#define POSTPROCESSING
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using AS3DPlugin;
#if POSTPROCESSING
using UnityEngine.Rendering.PostProcessing;
#endif
/// <summary>
/// Simple demo script 
/// </summary>
public class DemoLogic : MonoBehaviour
{
    public Transform Cam;
    public Transform VirtualScreen;
    public bool UsePostProcess = false;
#if POSTPROCESSING
    [Tooltip("Assign PostProcessResources.asset here, it's under postprocessing package path")]
    public PostProcessResources postProcessResources;
#endif
    public int otherSceneIdx = 0;
    private Vector3 VirtualScreenSize = Vector3.one;
    private float FOVScale = 1;
    public void SwitchSceneDemo()
    {
        SceneManager.LoadScene(otherSceneIdx, LoadSceneMode.Single);
    }
    public void Quit()
    {
        if (Cam.GetComponent<StereoCam>() != null)
        {
            Cam.GetComponent<StereoCam>().Quit();
        }
    }
    public void MinimizeWindow()
    {
        if (Cam.GetComponent<StereoCam>() != null)
        {
            Cam.GetComponent<StereoCam>().MinimizeWindow();
        }
    }

    private void Awake()
    {
#if UNITY_EDITOR
#if POSTPROCESSING
        if (UsePostProcess && postProcessResources == null)
        {
            string[] guids = AssetDatabase.FindAssets("PostProcessResources");
            if (guids.Length > 0)
            {
                // Get the first asset's path
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);

                // Load the asset at the path as a PostProcessResources object
                postProcessResources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(path);
            }
            else
            {
                print("Please add PostProcess Package to see it running in stereoscopic modes, " +
                    "using FXAA with postprocess for MockHMD mode is recommended. You also need to create a layer named eg. as \"PostProcess\"");
            }
        }
#endif
#else
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#endif
#if POSTPROCESSING
        if (UsePostProcess && postProcessResources != null)
        {
            var postProcessLayer = Cam.gameObject.AddComponent<PostProcessLayer>();
            postProcessLayer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
            postProcessLayer.volumeLayer = LayerMask.GetMask("PostProcessing");
            postProcessLayer.volumeTrigger = Camera.main.transform;
            var postProcessingGlobalVolume = Resources.Load("Prefabs/PostProcessingGlobalVolume") as GameObject;
            postProcessLayer.Init(postProcessResources);
            if (postProcessingGlobalVolume)
                 Instantiate(postProcessingGlobalVolume);
        }
#endif
        //Cam.GetComponent<StereoCam>().HoldBeforeSwitch3DOn = true;    //Do this if you don't want start in 3D
    }
    private void Start()
    {
        if (VirtualScreen != null)
            VirtualScreenSize = VirtualScreen.localScale;
    }
    private void Update()
    {
        //if (Input.GetKeyDown(KeyCode.Space))                      //instead, use some trigger to WOW your viewer
        //    Cam.GetComponent<StereoCam>().HoldBeforeSwitch3DOn = false;
        if (Input.GetKeyDown(KeyCode.T))
        {
            SwitchSceneDemo();
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Quit();
        }
        if (Input.GetKeyDown(KeyCode.U))
        {
            FOVScale /= 1.2f;
            Cam.GetComponent<StereoCam>().AdjustFOVScale(FOVScale);
        }
        if (Input.GetKeyDown(KeyCode.J))
        {
            FOVScale *= 1.2f;
            Cam.GetComponent<StereoCam>().AdjustFOVScale(FOVScale);
        }
        if (Input.GetKeyDown(KeyCode.M))
        {
            FOVScale = 1;
            Cam.GetComponent<StereoCam>().AdjustFOVScale(FOVScale);
        }
        if (Input.GetKeyDown(KeyCode.F))
        {
            Cam.GetComponent<StereoCam>().SwitchFrustumSyncEnable();
        }
        //Implementing Calibration can benifit further/closer view experience
        if (Input.GetKeyDown(KeyCode.C))
        {
            Cam.GetComponent<StereoCam>().CalibRecenter(null);
        }
        if (Input.GetKeyDown(KeyCode.V))
        {
            Cam.GetComponent<StereoCam>().CalibRecenterClear(null);
        }
        //demo dynamic changing virtual screen sizes, eg, need a correct "zooming" operation in 3d
        if (Input.GetKeyDown(KeyCode.Y))
        {
            if (VirtualScreen != null)
            {
                VirtualScreen.localScale /= 1.2f;
                Cam.GetComponent<StereoCam>().ReSetVirtualScreenWidth(VirtualScreen.localScale.x);
            }
        }
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (VirtualScreen != null)
            {
                VirtualScreen.localScale *= 1.2f;
                Cam.GetComponent<StereoCam>().ReSetVirtualScreenWidth(VirtualScreen.localScale.x);
            }
        }
        if (Input.GetKeyDown(KeyCode.N))
        {
            if (VirtualScreen != null)
            {
                VirtualScreen.localScale = VirtualScreenSize;
                Cam.GetComponent<StereoCam>().ReSetVirtualScreenWidth(VirtualScreen.localScale.x);
            }
        }
    }
}
