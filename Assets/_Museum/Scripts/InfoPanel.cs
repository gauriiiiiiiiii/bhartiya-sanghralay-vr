using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// World-space canvas (about 0.9 m x 0.6 m) showing the selected exhibit and playing its narration.
/// The Play/Pause, Close and Next GazeButtons call the public methods below.
public class InfoPanel : MonoBehaviour
{
    public static InfoPanel Instance { get; private set; }

    [Header("UI")]
    public GameObject root;
    public TMP_Text titleText;
    public TMP_Text eraText;
    public TMP_Text bodyText;
    public Image picture;
    public TMP_Text playLabel;

    [Header("Audio")]
    public AudioSource narration;         // 2D AudioSource (spatialBlend 0)
    public AudioSource ambience;          // optional looping background
    public float ambienceNormal = 0.15f;
    public float ambienceDucked = 0.04f;

    [Header("Guided tour order for this scene")]
    public Artifact[] tour;

    Artifact current;
    Vector3 baseScale;
    float bodyLeftWithPicture = -1f;

    void Awake()
    {
        Instance = this;
        root.SetActive(false);
    }

    public void Show(Artifact a)
    {
        if (current && current != a) current.SetSelected(false);
        current = a;
        a.SetSelected(true);

        if (a.panelAnchor) transform.SetPositionAndRotation(a.panelAnchor.position, a.panelAnchor.rotation);
        root.SetActive(true);
        StopCoroutine(nameof(Pop));
        StartCoroutine(nameof(Pop));
        Refresh();
        Play();
    }

    System.Collections.IEnumerator Pop()      // small scale-in when the panel opens
    {
        var t = root.transform;
        if (baseScale == Vector3.zero) baseScale = t.localScale;   // the canvas is authored at 0.001 scale
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.18f)
        {
            t.localScale = baseScale * Mathf.Lerp(0.85f, 1f, 1f - (1f - k) * (1f - k));
            yield return null;
        }
        t.localScale = baseScale;
    }

    void Refresh()
    {
        if (!current) return;
        var d = current.data;
        titleText.text = d.title;
        eraText.text = d.era;
        bodyText.text = d.shownEnlarged ? d.text + "\n\n(Shown enlarged)" : d.text;

        bool hasPicture = d.image;
        picture.sprite = d.image;
        picture.enabled = hasPicture;
        var rt = bodyText.rectTransform;          // use the full width when there is no picture
        if (bodyLeftWithPicture < 0f) bodyLeftWithPicture = rt.offsetMin.x;
        rt.offsetMin = new Vector2(hasPicture ? bodyLeftWithPicture : 30f, rt.offsetMin.y);
    }

    public void Play()
    {
        if (!current) return;
        narration.Stop();
        narration.clip = current.data.narration;
        if (!narration.clip) { SetPlayLabel(false); return; }
        narration.Play();
        SetPlayLabel(true);
    }

    public void TogglePlay()
    {
        if (narration.isPlaying) { narration.Pause(); SetPlayLabel(false); }
        else if (narration.clip && narration.time > 0f) { narration.UnPause(); SetPlayLabel(true); }
        else Play();
    }

    public void Close()
    {
        narration.Stop();
        root.SetActive(false);
        if (current) current.SetSelected(false);
        current = null;
    }

    public void Next()
    {
        if (!current || tour == null || tour.Length == 0) return;
        int i = System.Array.IndexOf(tour, current);
        var next = tour[(i + 1) % tour.Length];
        if (next.standPoint) next.standPoint.Go();
        Show(next);
    }

    void SetPlayLabel(bool playing)
    {
        if (playLabel) playLabel.text = playing ? "Pause" : "Play";
    }

    void Update()
    {
        if (ambience)
        {
            float target = narration.isPlaying ? ambienceDucked : ambienceNormal;
            ambience.volume = Mathf.MoveTowards(ambience.volume, target, Time.deltaTime * 0.5f);
        }
        // Narration finished on its own
        if (current && narration.clip && !narration.isPlaying && narration.time == 0f && playLabel && playLabel.text == "Pause")
            SetPlayLabel(false);
    }
}
