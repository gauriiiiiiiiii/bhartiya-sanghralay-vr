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
    public Color selectedText = Color.white;
    public Color unselectedText = new Color(1f, 1f, 1f, 0.7f);

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
        if (vrLabel) vrLabel.color = vr ? selectedText : unselectedText;
        if (phoneLabel) phoneLabel.color = vr ? unselectedText : selectedText;
        if (modeHint)
            modeHint.text = vr
                ? "Cardboard: choose a gallery, then put the phone in the viewer. Look at something and press the viewer button to select it. Press X to return here."
                : "Phone: drag to look around, tap a circle on the floor to walk, tap an exhibit to hear its audio guide. Tap Menu to return here.";
    }
}
