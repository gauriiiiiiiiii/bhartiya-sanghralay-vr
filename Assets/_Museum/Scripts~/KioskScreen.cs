using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// Interactive screen standing in each section hall. Four tabs, all usable by gaze (VR) or tap (phone):
///   Quiz      three questions on the section, with explanations and a score
///   Timeline  the section's exhibits in date order; selecting one walks you to it and opens its panel
///   Map       where the places in this section are, on a map of India
///   Photos    slideshow of the section's photos with credits
/// The UI is built at runtime from the data the scene builder stores on this component.
[RequireComponent(typeof(Canvas))]
public class KioskScreen : MonoBehaviour
{
    [System.Serializable] public class Question { public string text; public string[] options; public int answer; public string why; }
    [System.Serializable] public class Place { public string name; public float u, v; }
    [System.Serializable] public class Entry
    {
        public int year; public string label, title, credit;
        public Artifact target; public Sprite photo; public Place[] places;
    }

    public TMP_FontAsset font;
    public Color accent = new Color(0.95f, 0.55f, 0.1f);
    public Sprite mapSprite;
    public string wingId, sectionTitle, period;
    public List<Question> questions = new List<Question>();
    public List<Entry> entries = new List<Entry>();

    /// Best quiz score per section id, for the whole session.
    public static readonly Dictionary<string, int> BestScores = new Dictionary<string, int>();

    const float W = 1240, H = 840;
    static readonly Color Bg = new Color(0.06f, 0.06f, 0.065f, 0.98f), Panel = new Color(0.15f, 0.15f, 0.16f, 1f), Text = new Color(0.96f, 0.95f, 0.92f);
    static readonly Color Good = new Color(0.2f, 0.55f, 0.25f, 1f), Bad = new Color(0.6f, 0.16f, 0.12f, 1f);

    RectTransform root;
    readonly GameObject[] pages = new GameObject[4];
    readonly GazeButton[] tabs = new GazeButton[4];
    Sprite dot;
    int interactiveLayer;

    // quiz state
    int qIndex, score; bool answered;
    TMP_Text qText, qFeedback, qScore; GazeButton[] qOptions; TMP_Text[] qOptionLabels; GazeButton qNext; TMP_Text qNextLabel;
    // timeline state
    int tPage; readonly List<GameObject> tItems = new List<GameObject>(); TMP_Text tPageLabel; List<Entry> sorted;
    // map state
    TMP_Text mInfo; Entry mSelected; GazeButton mGo;
    // photos state
    int pIndex; float pTimer; Image pImage; TMP_Text pCaption, pCredit, pCounter; List<Entry> withPhotos;

    void Awake()
    {
        interactiveLayer = LayerMask.NameToLayer("Interactive");
        if (interactiveLayer < 0) interactiveLayer = 0;
        root = (RectTransform)transform;
        dot = MakeDot();
        sorted = entries.Where(e => e.target).OrderBy(e => e.year).ToList();
        withPhotos = entries.Where(e => e.photo).ToList();
        Build();
        ShowPage(0);
    }

    // ---------------------------------------------------------------- UI helpers
    RectTransform R(string name, Transform parent, float x, float y, float w, float h)
    {   // x, y = top-left in canvas pixels (y down from the top)
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    Image Img(string name, Transform parent, float x, float y, float w, float h, Color c)
    {
        var i = R(name, parent, x, y, w, h).gameObject.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
    }

    TMP_Text Txt(string name, Transform parent, float x, float y, float w, float h, string text, float size, Color c,
                 TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
    {
        var t = R(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style; t.raycastTarget = false;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = size * 0.55f; t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    GazeButton Btn(string name, Transform parent, float x, float y, float w, float h, string label, float size, UnityAction action, out TMP_Text text)
    {
        var bg = Img(name, parent, x, y, w, h, Panel);
        bg.gameObject.layer = interactiveLayer;
        var col = bg.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, 20f); col.center = new Vector3(w / 2, -h / 2, 0);
        var gb = bg.gameObject.AddComponent<GazeButton>();
        gb.background = bg; gb.normalColor = Panel; gb.hoverColor = accent;
        gb.onSelect = new UnityEvent(); gb.onSelect.AddListener(action);
        text = Txt("Label", bg.transform, 12, 6, w - 24, h - 12, label, size, Text, TextAlignmentOptions.Center, FontStyles.Bold);
        return gb;
    }

    static void SetColor(GazeButton b, Color c) { b.normalColor = c; if (b.background) b.background.color = c; }

    Sprite MakeDot()
    {
        const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
            tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 8f)));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    // ---------------------------------------------------------------- layout
    void Build()
    {
        Img("Background", root, 0, 0, W, H, Bg);
        Img("AccentBar", root, 0, 0, W, 10, accent);
        Txt("Title", root, 30, 22, W - 60, 56, sectionTitle, 44, new Color(1f, 0.84f, 0.52f), TextAlignmentOptions.TopLeft, FontStyles.Bold);
        Txt("Period", root, 30, 80, W - 60, 34, period, 26, new Color(0.85f, 0.78f, 0.68f), TextAlignmentOptions.TopLeft, FontStyles.Italic);
        string[] names = { "Quiz", "Timeline", "Map", "Photos" };
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            tabs[i] = Btn("Tab_" + names[i], root, 30 + i * 300, 126, 282, 66, names[i], 32, () => ShowPage(k), out _);
            pages[i] = R("Page_" + names[i], root, 30, 210, W - 60, H - 240).gameObject;
        }
        BuildQuiz(pages[0].transform); BuildTimeline(pages[1].transform); BuildMap(pages[2].transform); BuildPhotos(pages[3].transform);
    }

    void ShowPage(int i)
    {
        for (int k = 0; k < 4; k++)
        {
            pages[k].SetActive(k == i);
            SetColor(tabs[k], k == i ? Color.Lerp(accent, Color.black, 0.25f) : Panel);
        }
        if (i == 3) pTimer = 0f;
    }

    // ---------------------------------------------------------------- quiz
    void BuildQuiz(Transform p)
    {
        if (questions.Count == 0) { Txt("None", p, 0, 0, 1180, 60, "No questions for this section yet.", 30, Text); return; }
        qText = Txt("Question", p, 0, 0, 1180, 120, "", 34, Text, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        qOptions = new GazeButton[4]; qOptionLabels = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            qOptions[i] = Btn("Option" + i, p, (i % 2) * 595, 130 + (i / 2) * 120, 585, 108, "", 28, () => Answer(k), out qOptionLabels[i]);
            qOptionLabels[i].fontStyle = FontStyles.Normal;
        }
        qFeedback = Txt("Feedback", p, 0, 380, 900, 120, "", 26, Text);
        qScore = Txt("Score", p, 900, 380, 280, 50, "", 26, new Color(1f, 0.84f, 0.52f), TextAlignmentOptions.TopRight, FontStyles.Bold);
        qNext = Btn("Next", p, 900, 440, 280, 70, "Next question", 28, NextQuestion, out qNextLabel);
        LoadQuestion(0);
    }

    void LoadQuestion(int i)
    {
        qIndex = i; answered = false;
        var q = questions[i];
        qText.text = $"<size=70%><color=#D8B070>Question {i + 1} of {questions.Count}</color></size>\n{q.text}";
        for (int k = 0; k < 4; k++)
        {
            bool has = q.options != null && k < q.options.Length;
            qOptions[k].gameObject.SetActive(has);
            if (has) { qOptionLabels[k].text = $"<b>{(char)('A' + k)}.</b>  {q.options[k]}"; SetColor(qOptions[k], Panel); }
        }
        qFeedback.text = ""; qScore.text = $"Score {score} / {questions.Count}";
        qNext.gameObject.SetActive(false);
    }

    void Answer(int k)
    {
        if (answered) return;
        answered = true;
        var q = questions[qIndex];
        bool right = k == q.answer;
        if (right) score++;
        SetColor(qOptions[k], right ? Good : Bad);
        if (!right && q.answer >= 0 && q.answer < qOptions.Length) SetColor(qOptions[q.answer], Good);
        qFeedback.text = (right ? "<color=#8FE39A><b>Correct!</b></color>  " : "<color=#F09A8A><b>Not quite.</b></color>  ") + q.why;
        qScore.text = $"Score {score} / {questions.Count}";
        qNext.gameObject.SetActive(true);
        qNextLabel.text = qIndex + 1 < questions.Count ? "Next question" : "See result";
    }

    void NextQuestion()
    {
        if (qIndex + 1 < questions.Count) { LoadQuestion(qIndex + 1); return; }
        if (qText == null) return;
        string id = sectionTitle;
        BestScores[id] = Mathf.Max(score, BestScores.TryGetValue(id, out var b) ? b : 0);
        qText.text = $"<size=70%><color=#D8B070>Quiz complete</color></size>\nYou scored {score} out of {questions.Count}." +
                     (score == questions.Count ? " Excellent!" : score > 0 ? " Well done. Explore the boards and try again." : " Have a look around the hall and try again.");
        foreach (var o in qOptions) o.gameObject.SetActive(false);
        qFeedback.text = "";
        qNextLabel.text = "Try again";
        score = 0; qIndex = -1;
        qNext.onSelect.RemoveAllListeners();
        qNext.onSelect.AddListener(() => { qNext.onSelect.RemoveAllListeners(); qNext.onSelect.AddListener(NextQuestion); LoadQuestion(0); });
    }

    // ---------------------------------------------------------------- timeline
    const int PerPage = 5;

    void BuildTimeline(Transform p)
    {
        if (sorted.Count == 0) { Txt("None", p, 0, 0, 1180, 60, "No exhibits.", 30, Text); return; }
        Img("Axis", p, 0, 300, 1180, 6, accent);
        Btn("Prev", p, 0, 470, 160, 70, "<", 40, () => TimelinePage(tPage - 1), out _);
        Btn("Next", p, 1020, 470, 160, 70, ">", 40, () => TimelinePage(tPage + 1), out _);
        tPageLabel = Txt("Page", p, 200, 480, 780, 50, "", 24, new Color(0.85f, 0.78f, 0.68f), TextAlignmentOptions.Center);
        Txt("Hint", p, 0, 0, 1180, 40, "Select an exhibit to walk to it.", 24, new Color(0.85f, 0.78f, 0.68f));
        TimelinePage(0);
    }

    void TimelinePage(int page)
    {
        int pageCount = Mathf.CeilToInt(sorted.Count / (float)PerPage);
        tPage = Mathf.Clamp(page, 0, pageCount - 1);
        foreach (var g in tItems) Destroy(g);
        tItems.Clear();
        var p = pages[1].transform;
        for (int i = 0; i < PerPage; i++)
        {
            int idx = tPage * PerPage + i;
            if (idx >= sorted.Count) break;
            var e = sorted[idx];
            float x = i * 236;
            bool up = i % 2 == 0;
            var tick = Img("Tick", p, x + 112, up ? 250 : 306, 6, 50, accent); tItems.Add(tick.gameObject);
            var knob = Img("Knob", p, x + 101, 289, 28, 28, accent); knob.sprite = dot; tItems.Add(knob.gameObject);
            var b = Btn("Item", p, x, up ? 60 : 360, 228, 190, "", 22, () => GoTo(e), out var label);
            label.alignment = TextAlignmentOptions.Top; label.fontStyle = FontStyles.Normal;
            label.text = $"<b><color=#F5C66A><size=120%>{e.label}</size></color></b>\n{e.title}";
            tItems.Add(b.gameObject);
        }
        tPageLabel.text = $"{tPage + 1} / {pageCount}   ·   {sorted.First().label}  to  {sorted.Last().label}";
    }


    void GoTo(Entry e)
    {
        if (e != null && e.target) e.target.OnSelect();
    }

    // ---------------------------------------------------------------- map
    void BuildMap(Transform p)
    {
        float mh = 580, mw = mh * 1024f / 1046f;
        var map = Img("Map", p, 0, 0, mw, mh, Color.white);
        map.sprite = mapSprite; map.preserveAspect = false;
        Txt("Head", p, mw + 30, 0, 1180 - mw - 30, 50, "Places in this section", 30, new Color(1f, 0.84f, 0.52f), TextAlignmentOptions.TopLeft, FontStyles.Bold);
        mInfo = Txt("Info", p, mw + 30, 60, 1180 - mw - 30, 300, "Select a pin on the map.", 26, Text);
        mGo = Btn("Go", p, mw + 30, 470, 1180 - mw - 30, 80, "Go to exhibit", 28, () => GoTo(mSelected), out _);
        mGo.gameObject.SetActive(false);
        var seen = new HashSet<string>(); int count = 0;
        foreach (var e in entries)
            if (e.places != null)
                foreach (var pl in e.places)
                {
                    if (pl.u < 0 || pl.u > 1 || pl.v < 0 || pl.v > 1) continue;
                    count++;
                    var entry = e; var place = pl;
                    float px = pl.u * mw, py = (1f - pl.v) * mh;
                    bool dup = !seen.Add($"{pl.u:0.00},{pl.v:0.00}");
                    var b = Btn("Pin", map.transform, px - 18 + (dup ? 10 : 0), py - 18 + (dup ? 10 : 0), 36, 36, "", 10, () =>
                    {
                        mSelected = entry;
                        mInfo.text = $"<b><color=#F5C66A>{place.name}</color></b>\n{entry.title}\n<size=80%><color=#D8C8B0>{entry.label}</color></size>";
                        mGo.gameObject.SetActive(entry.target);
                    }, out _);
                    b.background.sprite = dot; b.normalColor = new Color(0.85f, 0.15f, 0.1f); b.hoverColor = new Color(1f, 0.85f, 0.2f); b.background.color = b.normalColor;
                }
        if (count == 0) mInfo.text = "The exhibits in this section are not tied to particular places.";
    }

    // ---------------------------------------------------------------- photos
    void BuildPhotos(Transform p)
    {
        if (withPhotos.Count == 0) { Txt("None", p, 0, 0, 1180, 60, "Photos for this section will appear here once downloaded.", 28, Text); return; }
        pImage = Img("Photo", p, 0, 0, 1180, 430, Color.white); pImage.preserveAspect = true;
        pCaption = Txt("Caption", p, 170, 440, 840, 60, "", 28, Text, TextAlignmentOptions.Top, FontStyles.Bold);
        pCredit = Txt("Credit", p, 170, 500, 840, 40, "", 18, new Color(0.8f, 0.74f, 0.66f), TextAlignmentOptions.Top);
        pCounter = Txt("Counter", p, 170, 540, 840, 40, "", 22, new Color(0.85f, 0.78f, 0.68f), TextAlignmentOptions.Top);
        Btn("Prev", p, 0, 450, 150, 80, "<", 40, () => ShowPhoto(pIndex - 1), out _);
        Btn("Next", p, 1030, 450, 150, 80, ">", 40, () => ShowPhoto(pIndex + 1), out _);
        ShowPhoto(0);
    }

    void ShowPhoto(int i)
    {
        if (withPhotos.Count == 0) return;
        pIndex = (i % withPhotos.Count + withPhotos.Count) % withPhotos.Count;
        var e = withPhotos[pIndex];
        pImage.sprite = e.photo;
        pCaption.text = $"{e.title}  <color=#F5C66A><size=80%>{e.label}</size></color>";
        pCredit.text = string.IsNullOrEmpty(e.credit) ? "" : "Photo: " + e.credit;
        pCounter.text = $"{pIndex + 1} / {withPhotos.Count}";
        pTimer = 0f;
    }

    void Update()
    {
        if (pages[3] && pages[3].activeSelf && withPhotos.Count > 1)
        {
            pTimer += Time.deltaTime;
            if (pTimer > 7f) ShowPhoto(pIndex + 1);
        }
    }
}
