using UnityEngine;
#if !UNITY_EDITOR
using Google.XR.Cardboard;
#endif

/// Cardboard housekeeping on the Player (VR mode only): scans the viewer QR code on first launch, handles the
/// gear (re-scan) and close (back to menu) buttons, and recentres when the viewer button is held.
public class MuseumCardboardRuntime : MonoBehaviour
{
    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if !UNITY_EDITOR
        if (!MuseumApp.XRActive) return;
        Screen.brightness = 1f;
        if (!Api.HasDeviceParams()) Api.ScanDeviceParams();
#endif
    }

    void Update()
    {
#if !UNITY_EDITOR
        if (!MuseumApp.XRActive) return;
        if (Api.IsGearButtonPressed) Api.ScanDeviceParams();
        if (Api.IsCloseButtonPressed) MuseumApp.BackToMenu();
        if (Api.IsTriggerHeldPressed) Api.Recenter();
        if (Api.HasNewDeviceParams()) Api.ReloadDeviceParams();
        Api.UpdateScreenParams();
#endif
    }
}
