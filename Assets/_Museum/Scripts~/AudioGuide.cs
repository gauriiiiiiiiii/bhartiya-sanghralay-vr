using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The museum's audio guide. Selecting an object (tap on the phone, gaze or the viewer button in VR) plays its
/// narration, softly brightens the object's own spotlight and shows subtitles at the bottom of the view – like a
/// handheld museum audio guide with captions. Selecting the same object again stops it.
/// The UI is built at runtime: a screen overlay in phone mode, and a small caption strip below the line of sight in VR.
public class AudioGuide : MonoBehaviour
{
    public static AudioGuide Instance { get; private set; }

    public TMP_FontAsset font;
    public AudioSource source;
    public Color accent = new Color(0.86f, 0.62f, 0.28f);
    [Tooltip("Played once per session when this scene first opens (the lobby welcome / a gallery introduction)")]
    public ArtifactData intro;

    static readonly HashSet<string> introsPlayed = new HashSet<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => introsPlayed.Clear();   // also when domain reload is off in the Editor

    Artifact current;
    ArtifactData playing;
    readonly List<string> sentences = new List<string>();
    readonly List<float> sentenceEnd = new List<float>();     // cumulative fraction of the clip
    float hideAt = -1f, startedAt; bool everPlayed;

    class View { public GameObject root; public TMP_Text stop, title, caption, hint; public Image progress; public GameObject hintRoot; }
    View phone, vr;

    void Awake()
    {
        Instance = this;
        if (!source) { source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0f; }
        EnsureGlyphs(font);
        phone = BuildPhoneView();
        vr = BuildVrView();
        SetVisible(false);
    }

    void Start()
    {
        if (intro && intro.narration && introsPlayed.Add(intro.id)) Invoke(nameof(PlayIntro), 1.2f);
    }

    void PlayIntro() { if (!source.isPlaying) Play(intro, null); }

    // ---------------------------------------------------------------- public API
    public void Hover(Artifact a)
    {
        if (a == null || a.data == null || current == a) { ShowHint(null); return; }
        ShowHint((a.data.stop > 0 ? $"<b>{a.data.stop}</b>   " : "") + a.data.title + "   <color=#C9C2B6>– look to listen</color>");
    }

    public void Unhover(Artifact a) => ShowHint(null);

    public void Toggle(Artifact a)
    {
        if (a != null && current == a && source.isPlaying) { Stop(); return; }
        Play(a ? a.data : null, a);
    }

    public void Play(ArtifactData d, Artifact a)
    {
        if (d == null) return;
        if (current && current != a) current.SetHighlighted(false);
        current = a; playing = d;
        if (a) a.SetHighlighted(true);
        ShowHint(null);
        source.Stop();
        source.clip = d.narration;
        if (source.clip) { if (source.clip.loadState != AudioDataLoadState.Loaded) source.clip.LoadAudioData(); source.Play(); }
        startedAt = Time.time; everPlayed = false;
        Split(string.IsNullOrEmpty(d.text) ? d.title : d.text);
        foreach (var v in new[] { phone, vr })
        {
            if (v == null) continue;
            v.stop.text = d.stop > 0 ? d.stop.ToString() : "i";
            v.title.text = d.title;
            v.caption.text = sentences.Count > 0 ? sentences[0] : "";
            v.progress.fillAmount = 0f;
        }
        hideAt = source.clip ? -1f : Time.time + 8f;
        SetVisible(true);
    }

    public void Stop()
    {
        source.Stop();
        if (current) current.SetHighlighted(false);
        current = null; playing = null;
        SetVisible(false);
    }

    // ---------------------------------------------------------------- update
    void Update()
    {
        bool xr = MuseumApp.XRActive;
        if (phone != null && phone.root.activeSelf && xr) phone.root.SetActive(false);
        if (vr != null && vr.root.activeSelf && !xr) vr.root.SetActive(false);
        if (playing == null) return;
        if (phone != null && !xr && !phone.root.activeSelf) phone.root.SetActive(true);
        if (vr != null && xr && !vr.root.activeSelf) vr.root.SetActive(true);

        if (source.clip)
        {
            float f = source.clip.length > 0 ? source.time / source.clip.length : 1f;
            if (source.isPlaying) everPlayed = true;
            bool finished = !source.isPlaying && (everPlayed || Time.time - startedAt > 6f);
            if (!source.isPlaying && !everPlayed && source.clip.loadState == AudioDataLoadState.Loaded && Time.time - startedAt > 0.3f) source.Play();
            if (finished && hideAt < 0f) { hideAt = Time.time + 1.5f; f = 1f; if (current) current.SetHighlighted(false); }
            int k = 0; while (k < sentenceEnd.Count - 1 && f > sentenceEnd[k]) k++;
            foreach (var v in new[] { phone, vr })
            {
                if (v == null) continue;
                v.progress.fillAmount = f;
                if (k < sentences.Count) v.caption.text = sentences[k];
            }
        }
        if (hideAt > 0f && Time.time > hideAt) { hideAt = -1f; current = null; playing = null; SetVisible(false); }
    }

    void Split(string text)
    {
        sentences.Clear(); sentenceEnd.Clear();
        var parts = Regex.Split(text.Trim(), @"(?<=[.!?…])\s+(?=[A-Z'‘“(0-9])");
        // keep captions short: merge very short sentences, split very long ones at a comma
        var list = new List<string>();
        foreach (var p in parts)
        {
            if (p.Length > MaxCaption) { foreach (var piece in Chop(p)) list.Add(piece); continue; }
            if (list.Count > 0 && list[list.Count - 1].Length < 40 && list[list.Count - 1].Length + p.Length < MaxCaption) list[list.Count - 1] += " " + p; else list.Add(p);
        }
        float total = 0; foreach (var s in list) total += s.Length + 12;     // +12: pause after each sentence
        float acc = 0;
        foreach (var s in list) { acc += s.Length + 12; sentences.Add(s); sentenceEnd.Add(acc / Mathf.Max(1, total)); }
    }

    // a caption chunk must fit three lines of the bar at CaptionSize (~55 characters per line in the VR strip)
    const int MaxCaption = 150;

    /// Splits a long sentence at commas, semicolons or dashes (else at a space) into pieces of at most MaxCaption.
    static List<string> Chop(string p)
    {
        var res = new List<string>();
        while (p.Length > MaxCaption)
        {
            int cut = -1;
            foreach (var sep in new[] { "; ", ", ", " – ", " — " })
            {
                int i = p.LastIndexOf(sep, MaxCaption, StringComparison.Ordinal);
                if (i > MaxCaption / 3 && i > cut) cut = i + sep.Length - 1;
            }
            if (cut < 0) cut = p.LastIndexOf(' ', MaxCaption);
            if (cut <= 0) break;
            res.Add(p.Substring(0, cut + 1).Trim()); p = p.Substring(cut + 1).Trim();
        }
        if (p.Length > 0) res.Add(p);
        return res;
    }

    void ShowHint(string text)
    {
        foreach (var v in new[] { vr })
        {
            if (v == null) continue;
            bool show = !string.IsNullOrEmpty(text) && MuseumApp.XRActive;
            v.hintRoot.SetActive(show);
            if (show) v.hint.text = text;
            if (show && !v.root.activeSelf) v.root.SetActive(true);
            if (!show && playing == null && v.root.activeSelf) v.root.SetActive(false);
        }
    }

    void SetVisible(bool on)
    {
        bool xr = MuseumApp.XRActive;
        if (phone != null) { phone.root.SetActive(on && !xr); }
        if (vr != null)
        {
            vr.root.SetActive(on && xr);
            vr.hintRoot.SetActive(false);
            vr.root.transform.Find("Bar")?.gameObject.SetActive(on);
        }
    }

    /// Dynamic font atlases are cleared for builds and refilled on demand; TMP looks up the ellipsis glyph without
    /// adding it, so put the punctuation the captions use into the atlas up front.
    public static void EnsureGlyphs(TMP_FontAsset f)
    {
        if (f && f.atlasPopulationMode != AtlasPopulationMode.Static) f.TryAddCharacters("\u2026\u00B7\u2013\u2014\u2018\u2019\u201C\u201D\u00D7\u20B9", out _);
    }

    static bool HasEllipsis(TMP_FontAsset f) => f && f.HasCharacter('\u2026', true, true);

    // ---------------------------------------------------------------- UI construction
    TMP_Text Text(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, float size, Color c, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform; rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = align; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    Image Box(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform; rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
        var i = go.AddComponent<Image>(); i.color = c; i.raycastTarget = false;
        return i;
    }

    // Text sizes are chosen for a phone: in phone mode the canvas is 1080 units tall, so 44 units is ~2.6 mm on a
    // 6.1-inch iPhone held sideways (about iOS body text). In VR the strip is 1.3 m away and 46 units at 0.0009 m per
    // unit gives a cap height of ~1.4°, comfortably above the ~0.8° a Cardboard viewer can resolve.
    const float TitleSize = 46f, CaptionSize = 44f, BadgeSize = 92f;

    View Fill(Transform bar, float scale)
    {
        var v = new View();
        Box(bar, "Back", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.05f, 0.05f, 0.05f, 0.86f));
        float pad = 20f, b = BadgeSize;
        var badge = Box(bar, "Badge", new Vector2(0, 1), new Vector2(0, 1), new Vector2(pad, -pad - b), new Vector2(pad + b, -pad), accent);
        v.stop = Text(badge.transform, "Stop", Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4), 40, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        v.stop.enableAutoSizing = true; v.stop.fontSizeMin = 26; v.stop.fontSizeMax = 40;
        float left = pad * 2 + b;
        v.title = Text(bar, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(left, -pad - TitleSize * 1.3f), new Vector2(-pad, -pad + 4), TitleSize, new Color(0.97f, 0.86f, 0.64f), FontStyles.Bold, TextAlignmentOptions.TopLeft);
        v.title.textWrappingMode = TextWrappingModes.NoWrap; v.title.overflowMode = HasEllipsis(font) ? TextOverflowModes.Ellipsis : TextOverflowModes.Truncate;
        v.caption = Text(bar, "Caption", Vector2.zero, Vector2.one, new Vector2(left, pad + 6), new Vector2(-pad, -pad - TitleSize * 1.35f), CaptionSize, new Color(0.98f, 0.97f, 0.94f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
        v.caption.enableAutoSizing = true; v.caption.fontSizeMin = CaptionSize * 0.85f; v.caption.fontSizeMax = CaptionSize;
        v.caption.lineSpacing = -6f;
        var track = Box(bar, "Track", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 8), new Color(1, 1, 1, 0.15f));
        v.progress = Box(track.transform, "Progress", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, accent);
        v.progress.type = Image.Type.Filled; v.progress.fillMethod = Image.FillMethod.Horizontal; v.progress.fillAmount = 0f;
        v.progress.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        return v;
    }

    View BuildPhoneView()
    {
        var go = new GameObject("AudioGuideUI", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
        var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
        go.AddComponent<GraphicRaycaster>();
        var bar = new GameObject("Bar", typeof(RectTransform)); bar.transform.SetParent(go.transform, false);
        var rt = (RectTransform)bar.transform; rt.anchorMin = new Vector2(0.07f, 0); rt.anchorMax = new Vector2(0.93f, 0);
        // 1080 units tall; a 19.5:9 phone is ~2340 wide. The bar spans most of the width and holds a title and
        // three caption lines, leaving the upper two thirds of the screen for the exhibit.
        rt.pivot = new Vector2(0.5f, 0); rt.sizeDelta = new Vector2(0, 290); rt.anchoredPosition = new Vector2(0, 24);
        var v = Fill(bar.transform, 1f);
        // stop button
        var btnGo = new GameObject("StopButton", typeof(RectTransform)); btnGo.transform.SetParent(bar.transform, false);
        var brt = (RectTransform)btnGo.transform; brt.anchorMin = brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(1, 1);
        brt.sizeDelta = new Vector2(96, 96); brt.anchoredPosition = new Vector2(-12, -12);   // ≥ 44 pt touch target
        var img = btnGo.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0.12f);
        var btn = btnGo.AddComponent<Button>(); btn.onClick.AddListener(Stop);
        Text(btnGo.transform, "X", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 56, Color.white, FontStyles.Bold, TextAlignmentOptions.Center).text = "×";
        v.title.rectTransform.offsetMax = new Vector2(-124, v.title.rectTransform.offsetMax.y);
        v.root = go; v.hintRoot = new GameObject("NoHint"); v.hintRoot.transform.SetParent(go.transform, false);
        return v;
    }

    View BuildVrView()
    {
        var cam = Camera.main; if (!cam) return null;
        var go = new GameObject("AudioGuideVR", typeof(RectTransform));
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0, -0.42f, 1.3f);     // ~18° below the line of sight
        go.transform.localRotation = Quaternion.Euler(18, 0, 0);
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 20;
        var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(1200, 400); rt.localScale = Vector3.one * 0.0009f;     // 1.08 m wide ≈ 45°
        var bar = new GameObject("Bar", typeof(RectTransform)); bar.transform.SetParent(go.transform, false);
        var brt = (RectTransform)bar.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(0.5f, 0);
        brt.sizeDelta = new Vector2(0, 290); brt.anchoredPosition = Vector2.zero;
        var v = Fill(bar.transform, 1f);
        var hint = new GameObject("Hint", typeof(RectTransform)); hint.transform.SetParent(go.transform, false);
        var hrt = (RectTransform)hint.transform; hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 84); hrt.anchoredPosition = new Vector2(0, 0);
        Box(hint.transform, "Back", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.06f, 0.06f, 0.06f, 0.7f));
        v.hint = Text(hint.transform, "Text", Vector2.zero, Vector2.one, new Vector2(20, 6), new Vector2(-20, -6), 44, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);
        v.hint.textWrappingMode = TextWrappingModes.NoWrap; v.hint.enableAutoSizing = true; v.hint.fontSizeMin = 34; v.hint.fontSizeMax = 44;
        v.hintRoot = hint; v.root = go;
        return v;
    }
}
