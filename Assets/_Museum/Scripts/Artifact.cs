using UnityEngine;

/// A museum object (layer: Interactive) with a collider around it. Selecting it plays its audio-guide narration
/// with subtitles (AudioGuide); its own small spotlight brightens while it plays.
public class Artifact : MonoBehaviour, IGazeTarget
{
    public ArtifactData data;
    public Transform model;              // the object itself
    public Transform panelAnchor;        // legacy (info card); unused by the audio guide
    public TeleportPoint standPoint;     // floor point in front of this object (used for far selections)
    [Tooltip("Realtime spotlight that softly brightens while the narration plays (optional)")]
    public Light highlight;
    public float highlightIntensity = 1.2f;
    [Tooltip("0 = never rotates")]
    public float spinSpeed = 0f;
    public float hoverScale = 1f;
    [Tooltip("Selecting the object from further away than this first moves the visitor to its stand point")]
    public float walkUpDistance = 4f;

    /// Kept for compatibility with older scenes.
    public static Artifact Suppressed;

    bool lit;
    float level;

    void Awake()
    {
        if (!model) model = transform;
        if (highlight) { highlight.intensity = 0f; highlight.enabled = false; }
    }

    public void OnGazeEnter() { if (AudioGuide.Instance) AudioGuide.Instance.Hover(this); }
    public void OnGazeExit() { if (AudioGuide.Instance) AudioGuide.Instance.Unhover(this); }

    public void OnSelect()
    {
        var cam = Camera.main;
        if (standPoint && cam)
        {
            var d = standPoint.transform.position - cam.transform.position;
            d.y = 0f;
            if (d.magnitude > walkUpDistance) standPoint.Go();
        }
        if (AudioGuide.Instance) AudioGuide.Instance.Toggle(this);
        else if (InfoPanel.Instance) InfoPanel.Instance.Show(this);
    }

    public void SetHighlighted(bool on) => lit = on;
    public void SetSelected(bool value) => lit = value;     // legacy InfoPanel hook

    void Update()
    {
        if (highlight)
        {
            level = Mathf.MoveTowards(level, lit ? 1f : 0f, Time.deltaTime * 1.5f);
            highlight.enabled = level > 0.001f;
            highlight.intensity = highlightIntensity * Mathf.SmoothStep(0f, 1f, level);
        }
        if (lit && spinSpeed > 0f) model.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }
}
