using UnityEngine;

/// Put on each exhibit (layer: Interactive) with a collider around the model.
public class Artifact : MonoBehaviour, IGazeTarget
{
    public ArtifactData data;
    public Transform model;              // the mesh to highlight / spin
    public Transform panelAnchor;        // empty placed where the info panel should appear, facing the visitor
    public TeleportPoint standPoint;     // floor point in front of this exhibit (used by "Next" and far selections)
    [Tooltip("0 for boards and paintings")]
    public float spinSpeed = 20f;
    public float hoverScale = 1.06f;
    [Tooltip("Selecting the exhibit from further away than this first moves the visitor to its stand point")]
    public float walkUpDistance = 3f;

    /// The exhibit whose panel was just closed: gazing at it does not reopen it until the visitor looks away
    /// (the viewer button still works).
    public static Artifact Suppressed;

    bool selected;
    Vector3 baseScale;
    Quaternion baseRotation;

    void Awake()
    {
        if (!model) model = transform;
        baseScale = model.localScale;
        baseRotation = model.localRotation;
    }

    public void OnGazeEnter() => model.localScale = baseScale * hoverScale;
    public void OnGazeExit()
    {
        model.localScale = baseScale;
        if (Suppressed == this) Suppressed = null;
    }

    public void OnSelect()
    {
        if (Suppressed == this && GazeInteractor.SelectedByDwell) return;
        Suppressed = null;
        var cam = Camera.main;
        if (standPoint && cam)
        {
            var d = standPoint.transform.position - cam.transform.position;
            d.y = 0f;
            if (d.magnitude > walkUpDistance) standPoint.Go();
        }
        InfoPanel.Instance.Show(this);
    }

    public void SetSelected(bool value)
    {
        selected = value;
        if (!value) model.localRotation = baseRotation;
    }

    void Update()
    {
        if (selected && spinSpeed > 0f)
            model.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }
}
