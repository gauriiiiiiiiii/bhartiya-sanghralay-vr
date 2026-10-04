using UnityEngine;

/// Glowing floor disc (layer: Teleport) with a collider. Its blue Z arrow = direction the visitor faces on arrival.
public class TeleportPoint : MonoBehaviour, IGazeTarget
{
    public Renderer disc;
    public Color idleColor = new Color(1f, 0.55f, 0.1f, 0.45f);   // saffron
    public Color hoverColor = new Color(1f, 0.8f, 0.2f, 1f);
    public bool faceForwardOnArrival = true;
    [Tooltip("Optional: teleport here instead of to this point (e.g. Exit -> Entrance).")]
    public Transform destination;

    void Start() => SetColor(idleColor);

    public void OnGazeEnter() => SetColor(hoverColor);
    public void OnGazeExit() => SetColor(idleColor);

    public void OnSelect() => Go();

    public void Go()
    {
        var t = destination ? destination : transform;
        float? yaw = faceForwardOnArrival ? t.eulerAngles.y : (float?)null;
        PlayerTeleporter.Instance.TeleportTo(t.position, yaw);
    }

    void SetColor(Color c)
    {
        if (disc) disc.material.color = c;
    }
}
