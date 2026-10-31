using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// The welcome wall behind the Great Court's information desk: a large touch / gaze screen with a card for every
/// gallery (selecting one takes the visitor straight there), the spoken welcome, and a short "how to visit" guide.
/// The UI is built at runtime from the data the scene builder stores on this component.
[RequireComponent(typeof(Canvas))]
public class InfoDesk : MonoBehaviour
{
    [System.Serializable] public class Gallery { public string scene, title, subtitle, years; public Sprite photo; public Color color = Color.gray; }

    public TMP_FontAsset font;
    public Sprite rounded;                 // 9-sliced rounded rectangle
    public Color accent = new Color(0.96f, 0.65f, 0.14f);
    public string museumTitle = "Museum of Indian History";
    public ArtifactData welcome;
    public List<Gallery> galleries = new List<Gallery>();

    // The wall is 4.16 m wide (0.0016 m per unit) and read from the desk's stand point ~3 m away:
    // 60 units ≈ 1.2° cap height, the smallest size that stays readable on a phone in a Cardboard viewer.
    const float W = 2600, H = 1500, MinText = 58f;
    static readonly Color Bg = new Color(0.055f, 0.06f, 0.07f, 0.98f), Card = new Color(0.11f, 0.115f, 0.13f, 1f),
        CardHover = new Color(0.19f, 0.2f, 0.23f, 1f), Ink = new Color(0.96f, 0.96f, 0.97f), Muted = new Color(0.64f, 0.65f, 0.68f);

    int interactiveLayer;
    bool built;
    GameObject tips;
    readonly List<Collider> tileColliders = new List<Collider>();

    void Awake() => EnsureBuilt();

    /// Builds the screen once (also used by the editor's snapshot tool to preview it outside Play mode).
    public void EnsureBuilt()
    {
        if (built) return;
        built = true;
        interactiveLayer = LayerMask.NameToLayer("Interactive");
        if (interactiveLayer < 0) interactiveLayer = gameObject.layer;
        AudioGuide.EnsureGlyphs(font);
        Build();
    }

    // ---------------------------------------------------------------- actions
    void GoTo(Gallery g)
    {
        if (g != null && PlayerTeleporter.Instance) PlayerTeleporter.Instance.LoadScene(g.scene, "Entry");
    }

    void PlayWelcome()
    {
        if (AudioGuide.Instance && welcome) AudioGuide.Instance.Play(welcome, null);
    }

    void ShowTips(bool on)
    {
        if (tips) tips.SetActive(on);
        foreach (var c in tileColliders) if (c) c.enabled = !on;     // the sheet covers the cards: don't select through it
    }

    // ---------------------------------------------------------------- layout
    void Build()
    {
        var root = (RectTransform)transform;
        Box("Background", root, 0, 0, W, H, Bg, 40f);
        var eyebrow = Txt("Eyebrow", root, 90, 70, 1500, 70, "INFORMATION", 58, accent, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        eyebrow.characterSpacing = 12f;
        Txt("Title", root, 90, 140, 1500, 130, "Welcome to the " + museumTitle, 100, Ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        Txt("Sub", root, 90, 280, 1500, 80, "Choose a gallery to go straight there.", 62, Muted);
        Button("Listen", root, 1690, 80, 820, 120, "Listen to the welcome", accent, Color.Lerp(accent, Color.white, 0.3f), new Color(0.06f, 0.06f, 0.07f), PlayWelcome);
        Button("Tips", root, 1690, 222, 820, 120, "How to visit", Card, CardHover, Ink, () => ShowTips(true));

        const float x0 = 90, y0 = 390, gap = 40;
        float tw = (W - 2 * x0 - 2 * gap) / 3f, th = (H - y0 - 90 - gap) / 2f;
        for (int i = 0; i < galleries.Count && i < 6; i++)
        {
            var g = galleries[i];
            Tile(root, x0 + (i % 3) * (tw + gap), y0 + (i / 3) * (th + gap), tw, th, i + 1, g);
        }

        // "how to visit" sheet over the gallery cards
        tips = Box("TipsSheet", root, x0, y0, W - 2 * x0, H - y0 - 90, Card, 32f).gameObject;
        var t = tips.transform;
        Txt("Head", t, 60, 50, 1600, 110, "How to visit", 90, Ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        Txt("Body", t, 60, 190, W - 2 * x0 - 120, 640,
            "<b>Walk</b>  –  tap (or look at) a circle on the floor.\n" +
            "<b>Listen</b>  –  tap an exhibit: its audio guide plays with subtitles.\n" +
            "<b>Galleries</b>  –  six doorways around this court; each gallery has a digital table.\n" +
            "<b>Menu</b>  –  switch between phone and Cardboard viewer at any time.", 64, Ink);
        Button("Close", t, W - 2 * x0 - 560, H - y0 - 90 - 170, 500, 120, "Close", accent, Color.Lerp(accent, Color.white, 0.3f), new Color(0.06f, 0.06f, 0.07f), () => ShowTips(false));
        tips.SetActive(false);
    }

    void Tile(Transform parent, float x, float y, float w, float h, int n, Gallery g)
    {
        var bg = Box("Gallery" + n, parent, x, y, w, h, Card, 28f);
        bg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        tileColliders.Add(MakeButton(bg, w, h, Card, CardHover, () => GoTo(g)));
        float ph = h * 0.5f;
        if (g.photo)
        {
            var ri = R("Photo", bg.transform, 0, 0, w, ph).gameObject.AddComponent<RawImage>();
            ri.texture = g.photo.texture; ri.raycastTarget = false; ri.uvRect = Crop(g.photo.texture, w / ph);
        }
        Img("Stripe", bg.transform, 0, ph, w, 10, g.color);
        // one line each, shrinking to fit rather than wrapping into the line below
        var title = Txt("Title", bg.transform, 36, ph + 30, w - 72, 84, $"<color=#{ColorUtility.ToHtmlStringRGB(accent)}>{n}</color>   {g.title}", 66, Ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        title.textWrappingMode = TextWrappingModes.NoWrap; title.fontSizeMin = 44f;
        var years = Txt("Years", bg.transform, 36, h - 100, w - 72, 72, g.years, 58, Muted, TextAlignmentOptions.MidlineLeft);
        years.textWrappingMode = TextWrappingModes.NoWrap; years.fontSizeMin = 40f;
    }

    // ---------------------------------------------------------------- helpers
    RectTransform R(string name, Transform parent, float x, float y, float w, float h)
    {   // x, y = top-left in canvas units (y down from the top)
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

    Image Box(string name, Transform parent, float x, float y, float w, float h, Color c, float radius)
    {
        var i = Img(name, parent, x, y, w, h, c);
        if (rounded)
        {
            i.sprite = rounded; i.type = Image.Type.Sliced;
            i.pixelsPerUnitMultiplier = rounded.border.x / Mathf.Max(1f, radius);
        }
        return i;
    }

    TMP_Text Txt(string name, Transform parent, float x, float y, float w, float h, string text, float size, Color c,
                 TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
    {
        var t = R(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        size = Mathf.Max(size, MinText);
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style; t.raycastTarget = false;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = MinText; t.textWrappingMode = TextWrappingModes.Normal;
        t.lineSpacing = -4f;
        return t;
    }

    void Button(string name, Transform parent, float x, float y, float w, float h, string label, Color normal, Color hover, Color ink, UnityAction action)
    {
        var bg = Box(name, parent, x, y, w, h, normal, h / 2f);
        MakeButton(bg, w, h, normal, hover, action);
        Txt("Label", bg.transform, 24, 0, w - 48, h, label, 60, ink, TextAlignmentOptions.Center, FontStyles.Bold);
    }

    Collider MakeButton(Image bg, float w, float h, Color normal, Color hover, UnityAction action)
    {
        bg.gameObject.layer = interactiveLayer;
        var col = bg.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, 20f); col.center = new Vector3(w / 2, -h / 2, 0);
        var gb = bg.gameObject.AddComponent<GazeButton>();
        gb.background = bg; gb.normalColor = normal; gb.hoverColor = hover;
        gb.onSelect = new UnityEvent(); gb.onSelect.AddListener(action);
        return col;
    }

    /// uvRect that fills a box of the given aspect with the centre of the texture (like CSS object-fit: cover).
    public static Rect Crop(Texture t, float boxAspect)
    {
        if (!t) return new Rect(0, 0, 1, 1);
        float ta = t.width / (float)Mathf.Max(1, t.height);
        if (ta > boxAspect) return new Rect((1 - boxAspect / ta) / 2, 0, boxAspect / ta, 1);
        float h = ta / boxAspect;
        return new Rect(0, (1 - h) * 0.78f, 1, h);       // tall pictures: keep the upper part, where faces and tops of buildings are
    }
}
