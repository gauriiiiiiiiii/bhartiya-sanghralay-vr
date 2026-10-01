using UnityEngine;

/// Glowing floor disc (layer: Teleport) with a collider. Its blue Z arrow = direction the visitor faces on arrival.
public class TeleportPoint : MonoBehaviour, IGazeTarget
{
    public Renderer disc;
    public Color idleColor = new Color(1f, 0.55f, 0.1f, 0.45f);   // saffron
    public Color hoverColor = new Color(1f, 0.8f, 0.2f, 1f);
    public bool faceForwardOnArrival = true;

    void Start() => SetColor(idleColor);

    public void OnGazeEnter() => SetColor(hoverColor);
    public void OnGazeExit() => SetColor(idleColor);

    public void OnSelect() => Go();

    public void Go()
    {
        float? yaw = faceForwardOnArrival ? transform.eulerAngles.y : (float?)null;
        PlayerTeleporter.Instance.TeleportTo(transform.position, yaw);
    }

    void SetColor(Color c)
    {
        if (disc) disc.material.color = c;
    }
}
