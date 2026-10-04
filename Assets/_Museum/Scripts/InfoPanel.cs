using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// World-space canvas (about 0.9 m x 0.6 m) showing the selected artifact's info and playing its narration.
/// Wire the Play/Pause, Close, Next and Language GazeButtons to the public methods below.
public class InfoPanel : MonoBehaviour
{
    public static InfoPanel Instance { get; private set; }

    [Header("UI")]
    public GameObject root;
    public TMP_Text titleText;
    public TMP_Text eraText;
    public TMP_Text bodyText;
    public Image picture;
    public Image marathiRenderedImage;   // shows ArtifactData.panelMrRendered when present
    public TMP_Text playLabel;
    public TMP_Text languageLabel;

    [Header("Fonts")]
    public TMP_FontAsset latinFont;       // e.g. Noto Sans
    public TMP_FontAsset devanagariFont;  // e.g. Noto Sans Devanagari

    [Header("Audio")]
    public AudioSource narration;         // 2D AudioSource (spatialBlend 0)
    public AudioSource ambience;          // looping background
    public float ambienceNormal = 0.15f;
    public float ambienceDucked = 0.04f;

    [Header("Guided tour order (A01..A16)")]
    public Artifact[] tour;

    Artifact current;

    void Awake()
    {
        Instance = this;
        root.SetActive(false);
        MuseumSettings.LanguageChanged += OnLanguageChanged;
    }

    void OnDestroy() => MuseumSettings.LanguageChanged -= OnLanguageChanged;

    public void Show(Artifact a)
    {
        if (current && current != a) current.SetSelected(false);
        current = a;
        a.SetSelected(true);

        if (a.panelAnchor) transform.SetPositionAndRotation(a.panelAnchor.position, a.panelAnchor.rotation);
        root.SetActive(true);
        Refresh();
        Play();
    }

    void Refresh()
    {
        if (!current) return;
        var d = current.data;
        bool mr = MuseumSettings.Language == Lang.Mr;
        bool useRendered = mr && d.panelMrRendered;

        var font = mr && devanagariFont ? devanagariFont : latinFont;
        if (font) titleText.font = bodyText.font = font;
        titleText.text = mr ? d.titleMr : d.titleEn;
        eraText.text = d.era;

        string body = mr ? d.panelMr : d.panelEn;
        if (d.shownEnlarged) body += mr ? "\n\n(मोठ्या आकारात दाखवले आहे)" : "\n\n(Shown enlarged)";
        bodyText.text = body;

        titleText.gameObject.SetActive(!useRendered);
        eraText.gameObject.SetActive(!useRendered);   // the rendered image includes the era line
        bodyText.gameObject.SetActive(!useRendered);
        if (marathiRenderedImage)
        {
            marathiRenderedImage.gameObject.SetActive(useRendered);
            marathiRenderedImage.sprite = d.panelMrRendered;
        }

        picture.sprite = d.image;
        picture.enabled = d.image;
        if (languageLabel) languageLabel.text = mr ? "English" : (devanagariFont ? "मराठी" : "Marathi");
    }

    AudioClip CurrentClip()
    {
        var d = current.data;
        return MuseumSettings.Language == Lang.Mr && d.narrationMr ? d.narrationMr : d.narrationEn;
    }

    public void Play()
    {
        if (!current) return;
        narration.Stop();
        narration.clip = CurrentClip();
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

    public void ToggleLanguage() => MuseumSettings.ToggleLanguage();

    void OnLanguageChanged()
    {
        bool wasPlaying = narration.isPlaying;
        Refresh();
        if (wasPlaying) Play(); else { narration.Stop(); SetPlayLabel(false); }
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
        if (current && !narration.isPlaying && narration.time == 0f && playLabel && playLabel.text == "Pause")
            SetPlayLabel(false);
    }
}
