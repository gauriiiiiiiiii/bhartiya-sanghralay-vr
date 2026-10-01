using UnityEngine;
#if !UNITY_EDITOR
using Google.XR.Cardboard;
#endif

/// Cardboard housekeeping on the Player: keeps the screen awake, scans the viewer QR code on first
/// launch, handles the gear (re-scan) and close buttons, and recentres when the button is held.
public class MuseumCardboardRuntime : MonoBehaviour
{
    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if !UNITY_EDITOR
        Screen.brightness = 1f;
        if (!Api.HasDeviceParams()) Api.ScanDeviceParams();
#endif
    }

    void Update()
    {
#if !UNITY_EDITOR
        if (Api.IsGearButtonPressed) Api.ScanDeviceParams();
        if (Api.IsCloseButtonPressed) Application.Quit();
        if (Api.IsTriggerHeldPressed) Api.Recenter();
        if (Api.HasNewDeviceParams()) Api.ReloadDeviceParams();
        Api.UpdateScreenParams();
#endif
    }
}
