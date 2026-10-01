using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// A button on the world-space info panel that works by gaze.
/// Needs a BoxCollider sized to the button, on the Interactive layer.
public class GazeButton : MonoBehaviour, IGazeTarget
{
    public UnityEvent onSelect;
    public Image background;
    public Color normalColor = new Color(0.25f, 0.12f, 0.05f, 0.9f);
    public Color hoverColor = new Color(0.95f, 0.55f, 0.1f, 1f);

    void OnEnable() { if (background) background.color = normalColor; }

    public void OnGazeEnter() { if (background) background.color = hoverColor; }
    public void OnGazeExit() { if (background) background.color = normalColor; }
    public void OnSelect() => onSelect.Invoke();
}
