using UnityEngine;

/// Screen overlay for phone mode: a Menu button and a hint. Hidden while the Cardboard display is running.
public class PhoneHUD : MonoBehaviour
{
    public GameObject root;
    public GameObject hint;
    float hintTime = 6f;

    void Update()
    {
        bool show = !MuseumApp.XRActive;
        if (root && root.activeSelf != show) root.SetActive(show);
        if (hint && hint.activeSelf && (hintTime -= Time.deltaTime) < 0f) hint.SetActive(false);
    }

    public void GoMenu() => MuseumApp.BackToMenu();
}
