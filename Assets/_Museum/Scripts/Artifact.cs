using UnityEngine;

/// Put on each exhibit (layer: Interactive) with a collider around the model.
public class Artifact : MonoBehaviour, IGazeTarget
{
    public ArtifactData data;
    public Transform model;              // the mesh to highlight / spin
    public Transform panelAnchor;        // empty placed where the info panel should appear, facing the visitor
    public TeleportPoint standPoint;     // floor point in front of this exhibit (used by "Next")
    [Tooltip("0 for paintings and maps")]
    public float spinSpeed = 20f;
    public float hoverScale = 1.06f;

    bool selected;
    Vector3 baseScale;

    void Awake()
    {
        if (!model) model = transform;
        baseScale = model.localScale;
    }

    public void OnGazeEnter() => model.localScale = baseScale * hoverScale;
    public void OnGazeExit() => model.localScale = baseScale;
    public void OnSelect() => InfoPanel.Instance.Show(this);

    public void SetSelected(bool value) => selected = value;

    void Update()
    {
        if (selected && spinSpeed > 0f)
            model.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }
}
