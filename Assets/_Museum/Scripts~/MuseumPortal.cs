using UnityEngine;

/// A glowing gateway (layer: Interactive) that loads another museum scene: a wing or the lobby.
public class MuseumPortal : MonoBehaviour, IGazeTarget
{
    [Tooltip("Scene name, as listed in Build Settings (e.g. Lobby, Wing1)")]
    public string sceneName;
    [Tooltip("The visitor arrives at the GameObject 'Arrive_<tag>' in that scene, e.g. Entry or W3")]
    public string arrivalTag = "Entry";
    public Renderer glow;
    public Color idleColor = new Color(1f, 0.6f, 0.2f);
    public float idleIntensity = 1.2f, hoverIntensity = 3f;

    Material mat;

    void Start()
    {
        if (glow) mat = glow.material;
        SetGlow(idleIntensity);
    }

    public void OnGazeEnter() => SetGlow(hoverIntensity);
    public void OnGazeExit() => SetGlow(idleIntensity);
    public void OnSelect()
    {
        if (PlayerTeleporter.Instance) PlayerTeleporter.Instance.LoadScene(sceneName, arrivalTag);
    }

    void SetGlow(float k)
    {
        if (!mat) return;
        mat.SetColor("_BaseColor", idleColor * 0.6f);
        mat.SetColor("_EmissionColor", idleColor * k);
    }
}
