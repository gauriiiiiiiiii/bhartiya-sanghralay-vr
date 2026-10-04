using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The 2D start screen: pick the viewer mode, then tap a wing card to enter it.
public class MainMenu : MonoBehaviour
{
    [Header("Mode pills")]
    public Image vrPill;
    public Image phonePill;
    public TMP_Text vrLabel;
    public TMP_Text phoneLabel;
    public TMP_Text modeHint;
    public Color selected = new Color(0.94f, 0.52f, 0.1f);
    public Color unselected = new Color(1f, 1f, 1f, 0.08f);

    void Start()
    {
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Refresh();
    }

    public void SetVR(bool vr)
    {
        MuseumApp.VRMode = vr;
        Refresh();
    }

    public void Enter(string scene) => MuseumApp.Enter(scene);

    void Refresh()
    {
        bool vr = MuseumApp.VRMode;
        if (vrPill) vrPill.color = vr ? selected : unselected;
        if (phonePill) phonePill.color = vr ? unselected : selected;
        if (vrLabel) vrLabel.color = vr ? Color.white : new Color(1f, 1f, 1f, 0.7f);
        if (phoneLabel) phoneLabel.color = vr ? new Color(1f, 1f, 1f, 0.7f) : Color.white;
        if (modeHint)
            modeHint.text = vr
                ? "Cardboard viewer: after you tap a wing the screen splits in two. Put the phone in the viewer, look at things and press the viewer button (or keep looking) to select. Press the X to come back here."
                : "Phone only: drag to look around, tap a glowing floor circle to walk there, tap a board to read and hear it. Tap Menu to come back here.";
    }
}
