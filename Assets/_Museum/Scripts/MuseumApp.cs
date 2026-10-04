using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

/// App-wide state: viewer mode (Cardboard VR or phone-only), XR start/stop and scene switching.
/// The app starts in the Menu scene (2D, touch). Choosing a wing starts Cardboard XR (VR mode) or not (phone mode).
public class MuseumApp : MonoBehaviour
{
    public const string MenuScene = "Menu";
    const string ModeKey = "museum.vrMode";

    static MuseumApp instance;

    /// Viewer mode chosen on the menu (remembered between launches).
    public static bool VRMode
    {
        get => PlayerPrefs.GetInt(ModeKey, 1) == 1;
        set { PlayerPrefs.SetInt(ModeKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// True while the Cardboard XR display is running (split-screen stereo).
    public static bool XRActive
    {
        get
        {
            var s = XRGeneralSettings.Instance;
            return s != null && s.Manager != null && s.Manager.isInitializationComplete && s.Manager.activeLoader != null;
        }
    }

    static bool busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Application.targetFrameRate = 60;              // phones default to 30 fps
        QualitySettings.vSyncCount = 0;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    static MuseumApp Runner
    {
        get
        {
            if (instance) return instance;
            var go = new GameObject("MuseumApp");
            DontDestroyOnLoad(go);
            return instance = go.AddComponent<MuseumApp>();
        }
    }

    /// Opens a museum scene in the current viewer mode.
    public static void Enter(string sceneName)
    {
        if (!busy) Runner.StartCoroutine(Runner.DoEnter(sceneName, VRMode));
    }

    /// Leaves VR (if running) and returns to the menu.
    public static void BackToMenu()
    {
        if (!busy) Runner.StartCoroutine(Runner.DoEnter(MenuScene, false));
    }

    IEnumerator DoEnter(string sceneName, bool vr)
    {
        busy = true;
        if (vr && !XRActive) yield return StartXR();
        else if (!vr && XRActive) StopXR();
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (op != null && !op.isDone) yield return null;
        busy = false;
    }

    static IEnumerator StartXR()
    {
        var s = XRGeneralSettings.Instance;
        if (s == null || s.Manager == null) yield break;
        yield return s.Manager.InitializeLoader();
        if (s.Manager.activeLoader == null) { Debug.LogWarning("[Museum] Cardboard XR could not start; using phone mode."); yield break; }
        s.Manager.StartSubsystems();
    }

    static void StopXR()
    {
        var s = XRGeneralSettings.Instance;
        if (s == null || s.Manager == null || !s.Manager.isInitializationComplete) return;
        s.Manager.StopSubsystems();
        s.Manager.DeinitializeLoader();
    }

    void OnApplicationQuit() => StopXR();
}
