using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// Modern UI for the v5 museum: Inter typeface, rounded cards, a dark editorial start menu and the interactive
/// welcome wall at the Great Court's information desk.
public static partial class MuseumBuilder
{
    const string UiDir = "Assets/_Museum/UI";
    const string InterRegular = "Assets/_Museum/Fonts/Inter-Regular.ttf", InterSemiBold = "Assets/_Museum/Fonts/Inter-SemiBold.ttf";
    static TMP_FontAsset uiFont;          // Inter; FontStyles.Bold maps to Inter SemiBold through the weight table
    static Sprite uiRounded;              // 9-sliced rounded rectangle, 48 px corner
    static Texture2D uiGlow;              // soft radial glow for the menu background

    static readonly Color UiBg = new Color(0.047f, 0.051f, 0.06f), UiCard = new Color(0.098f, 0.102f, 0.118f),
        UiInk = new Color(0.96f, 0.96f, 0.97f), UiMuted = new Color(0.63f, 0.64f, 0.68f), UiAccent = new Color(0.96f, 0.65f, 0.14f);

    // ---------------------------------------------------------------- assets
    static void EnsureUIAssets()
    {
        Directory.CreateDirectory(UiDir);
        var semi = MakeInter(InterSemiBold, "Inter SemiBold SDF", null);
        uiFont = MakeInter(InterRegular, "Inter SDF", semi);
        uiRounded = RoundedSprite(48);
        uiGlow = GlowTexture();
    }

    static TMP_FontAsset MakeInter(string ttf, string name, TMP_FontAsset bold)
    {
        var path = $"Assets/_Museum/Fonts/{name}.asset";
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (fa == null)
        {
            var src = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (src == null) return null;
            fa = TMP_FontAsset.CreateFontAsset(src, 64, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            fa.name = name;
            fa.isMultiAtlasTexturesEnabled = true;
            AssetDatabase.CreateAsset(fa, path);
            foreach (var tex in fa.atlasTextures) { tex.name = name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
        }
        if (bold != null && fa.fontWeightTable != null && fa.fontWeightTable.Length > 7)
        {
            var t = fa.fontWeightTable;
            t[6].regularTypeface = bold; t[7].regularTypeface = bold;      // SemiBold for both 600 and Bold (700)
        }
        fa.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?'\"()-–—·‹›<>/&%+=…‘’“”×₹", out _);
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    static Sprite RoundedSprite(int r)
    {
        var path = $"{UiDir}/Rounded{r}.png";
        if (!File.Exists(path))
        {
            int n = 2 * r + 4;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                // signed distance to a rounded square inset by 0.5 px; 1 px anti-aliased edge
                float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - (r - 0.5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(0.5f - d)));
            }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti.textureType != TextureImporterType.Sprite || ti.spriteBorder.x != r)
        {
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
            ti.spriteBorder = new Vector4(r, r, r, r); ti.mipmapEnabled = false; ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Texture2D GlowTexture()
    {
        var path = $"{UiDir}/Glow.png";
        if (!File.Exists(path))
        {
            const int n = 256; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a = a * a * (3 - 2 * a);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------------------------------------------------------- UI helpers (screen space, 1080 units tall)
    static Image URound(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c, float radius)
    {
        var i = Rect(name, parent, aMin, aMax, oMin, oMax).gameObject.AddComponent<Image>();
        i.color = c; i.raycastTarget = false;
        if (uiRounded) { i.sprite = uiRounded; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 48f / Mathf.Max(1f, radius); }
        return i;
    }

    static TextMeshProUGUI UText(Transform parent, string name, string text, float size, Color c, bool bold, TextAlignmentOptions align,
                                 Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var t = Label(parent, name, text, size, c, bold ? FontStyles.Bold : FontStyles.Normal, align, aMin, aMax, oMin, oMax);
        if (uiFont) t.font = uiFont;
        return t;
    }

    static Button UButton(Image img, Color normal)
    {
        var b = img.gameObject.AddComponent<Button>(); img.raycastTarget = true; b.targetGraphic = img;
        var cb = b.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(0.92f, 0.92f, 0.95f); cb.pressedColor = new Color(0.75f, 0.75f, 0.8f);
        cb.selectedColor = Color.white; cb.fadeDuration = 0.08f; b.colors = cb;
        img.color = normal;
        return b;
    }

    // ---------------------------------------------------------------- start menu
    /// Dark editorial start screen: headline, a segmented Phone / Cardboard switch and a row of photo cards.
    static string BuildMenuModern(CurJ cur)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupLighting();
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = UiBg; cam.cullingMask = 0;
        EnsureEventSystem();

        var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
        menu.selected = Color.white; menu.unselected = new Color(1, 1, 1, 0f);
        menu.selectedText = UiBg; menu.unselectedText = UiMuted;
        var canvas = ScreenCanvas("MenuCanvas", 0);
        var ct = canvas.transform;
        var bg = Rect("Background", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        bg.color = UiBg; bg.raycastTarget = false;
        var glow = Rect("Glow", ct, new Vector2(-0.25f, 0.25f), new Vector2(0.55f, 1.6f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
        glow.texture = uiGlow; glow.color = new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.16f); glow.raycastTarget = false;
        var glow2 = Rect("Glow2", ct, new Vector2(0.6f, -0.6f), new Vector2(1.3f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
        glow2.texture = uiGlow; glow2.color = new Color(0.25f, 0.45f, 0.95f, 0.10f); glow2.raycastTarget = false;

        var safe = Rect("SafeArea", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        safe.gameObject.AddComponent<SafeArea>();
        int nObjects = cur.wings.Sum(w => w.items.Count + (w.centre != null ? 1 : 0));

        // header (left)
        var eb = UText(safe, "Eyebrow", "MUSEUM OF INDIAN HISTORY", 30, UiAccent, true, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(0.62f, 1), new Vector2(96, -116), new Vector2(0, -76));
        eb.characterSpacing = 14f;
        var hl = UText(safe, "Headline", "Five thousand years of India,\nin six galleries.", 74, UiInk, true, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(0.62f, 1), new Vector2(96, -306), new Vector2(0, -126));
        hl.lineSpacing = -14f; hl.enableAutoSizing = true; hl.fontSizeMin = 56; hl.fontSizeMax = 74;
        UText(safe, "Sub", $"{nObjects} artifacts  ·  an audio guide in every room  ·  on your phone or in a Cardboard viewer", 32, UiMuted, false, TextAlignmentOptions.TopLeft,
              new Vector2(0, 1), new Vector2(0.62f, 1), new Vector2(96, -370), new Vector2(0, -318));

        // segmented control (right)
        UText(safe, "ModeLabel", "VIEW WITH", 28, UiMuted, true, TextAlignmentOptions.BottomLeft, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-96 - 640, -116), new Vector2(-96, -76)).characterSpacing = 10f;
        var seg = URound(safe, "ModeSwitch", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-96 - 640, -236), new Vector2(-96, -128), new Color(1, 1, 1, 0.07f), 54f);
        (Image, TMP_Text) Segment(string name, string text, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, bool vr)
        {
            var im = URound(seg.transform, name, aMin, aMax, oMin, oMax, Color.clear, 46f);
            var b = UButton(im, Color.clear);
            UnityEventTools.AddBoolPersistentListener(b.onClick, menu.SetVR, vr);
            var l = UText(im.transform, "Label", text, 34, UiInk, true, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return (im, l);
        }
        (menu.phonePill, menu.phoneLabel) = Segment("Phone", "Phone", new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(8, 8), new Vector2(-4, -8), false);
        (menu.vrPill, menu.vrLabel) = Segment("Cardboard", "Cardboard", new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(4, 8), new Vector2(-8, -8), true);

        // cards
        var view = Rect("Cards", safe, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 150), new Vector2(0, -400));
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        view.gameObject.AddComponent<RectMask2D>();
        view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
        var content = Rect("Content", view, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        content.pivot = new Vector2(0, 0.5f);
        var hlg = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(96, 96, 8, 8); hlg.spacing = 28; hlg.childControlHeight = true; hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
        content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = view; scroll.horizontal = true; scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Elastic; scroll.decelerationRate = 0.12f;

        const float cardW = 430, photoH = 250;
        void Card(string sceneName, Texture art, Color col, string kicker, string title, string meta, bool start)
        {
            var c = URound(content, "Card_" + sceneName, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiCard, 28f);
            c.gameObject.AddComponent<LayoutElement>().preferredWidth = cardW;
            c.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var b = UButton(c, UiCard);
            UnityEventTools.AddStringPersistentListener(b.onClick, menu.Enter, sceneName);
            if (art)
            {
                var pic = Rect("Photo", c.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -photoH), Vector2.zero).gameObject.AddComponent<RawImage>();
                pic.texture = art; pic.raycastTarget = false; pic.uvRect = InfoDesk.Crop(art, cardW / photoH);
            }
            var stripe = Rect("Stripe", c.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -photoH - 6), new Vector2(0, -photoH)).gameObject.AddComponent<Image>();
            stripe.color = col; stripe.raycastTarget = false;
            if (start)
            {
                var chip = URound(c.transform, "Chip", new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -76), new Vector2(240, -20), UiAccent, 28f);
                UText(chip.transform, "Label", "START HERE", 28, UiBg, true, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).characterSpacing = 4f;
            }
            var k = UText(c.transform, "Kicker", kicker, 28, Color.Lerp(col, Color.white, 0.45f), true, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -photoH - 66), new Vector2(-28, -photoH - 28));
            k.characterSpacing = 8f;
            var t = UText(c.transform, "Title", title, 42, UiInk, true, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -photoH - 180), new Vector2(-28, -photoH - 70));
            t.enableAutoSizing = true; t.fontSizeMin = 34; t.fontSizeMax = 42; t.lineSpacing = -8f;
            UText(c.transform, "Meta", meta, 30, UiMuted, false, TextAlignmentOptions.BottomLeft, new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 22), new Vector2(-28, 70));
        }
        Texture Photo(CItemJ it) => it != null ? (Texture)CurSprite(it.id)?.texture : null;
        Card("Lobby", Photo(cur.wings[5].items.Skip(1).FirstOrDefault()) ?? Photo(cur.wings[0].items.FirstOrDefault()), UiAccent, "ENTRANCE", "The Great Court", "Information desk and all six galleries", true);
        for (int i = 0; i < cur.wings.Count; i++)
        {
            var w = cur.wings[i];
            Card("Wing" + (i + 1), Photo(w.items.FirstOrDefault()), Hex(w.color), $"GALLERY {i + 1}", w.title,
                 $"{w.years}  ·  {w.items.Count + (w.centre != null ? 1 : 0)} artifacts", false);
        }

        // footer hint
        menu.modeHint = UText(safe, "ModeHint", "", 30, UiMuted, false, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0), new Vector2(1, 0), new Vector2(96, 30), new Vector2(-96, 130));

        var path = $"{SceneDir}/{MuseumApp.MenuScene}.unity";
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, path)) throw new System.Exception("Could not save " + path);
        return path;
    }

    // ---------------------------------------------------------------- information desk
    /// Dresses the Great Court's information desk and puts an interactive welcome wall on the drum behind it.
    static void InfoDeskV5(CurJ cur, Transform decor, Transform pts, ArtifactData welcome, float Rd)
    {
        var g = Group(decor, "InformationDesk");
        if (!M.ContainsKey("V5_Accent")) Lit("V5_Accent", UiAccent, 0f, 0.4f);
        // welcome wall: a 4.16 m × 2.4 m screen in a charcoal surround standing proud of the curved drum
        float wz = -Rd - 0.24f, cy = 2.6f;
        Box("WallSurround", new Vector3(0, cy, wz + 0.07f), new Vector3(4.5f, 2.74f, 0.1f), M["V5_Charcoal"], g);
        var screenGo = new GameObject("WelcomeWall", typeof(RectTransform));
        screenGo.transform.SetParent(g, false);
        screenGo.transform.SetPositionAndRotation(new Vector3(0, cy, wz - 0.002f), Quaternion.LookRotation(Vector3.forward));
        var canvas = screenGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)screenGo.transform; rt.sizeDelta = new Vector2(2600, 1500); rt.localScale = Vector3.one * 0.0016f;
        var desk = screenGo.AddComponent<InfoDesk>();
        desk.font = uiFont ? uiFont : font; desk.rounded = uiRounded; desk.accent = UiAccent; desk.welcome = welcome;
        desk.museumTitle = "Museum of Indian History";
        desk.galleries = cur.wings.Select((w, i) => new InfoDesk.Gallery
        {
            scene = "Wing" + (i + 1), title = w.title, subtitle = w.subtitle, years = w.years, color = Hex(w.color),
            photo = CurSprite(w.items.FirstOrDefault()?.id ?? "")
        }).ToList();
        screenGo.layer = interactiveLayer;
        // a soft wash of light on the wall and desk
        AreaLight("DeskLight", g, new Vector3(0, 5.2f, -Rd - 2.2f), new Vector2(5f, 1.2f), Vector3.down, 4f, new Color(1f, 0.95f, 0.88f));

        // on the desk: two staff monitors (backs to the visitor), a brochure stand with gallery leaflets, an "i" roundel
        foreach (var ang in new[] { 251f, 287f })      // either side of the welcome wall, not in front of it
        {
            var o = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0, Mathf.Sin(ang * Mathf.Deg2Rad));
            var p = o * 12.3f + Vector3.up * 1.06f; var face = Quaternion.LookRotation(-o);
            GMesh("MonitorStand", "MonitorStand", mb => { Geo.ChamferBox(mb, new Vector3(0, 0.12f, 0), new Vector3(0.05f, 0.24f, 0.05f), 0.005f); Geo.ChamferBox(mb, new Vector3(0, 0.006f, 0), new Vector3(0.24f, 0.012f, 0.18f), 0.004f); }, M["V5_Charcoal"], g, p, face, false);
            GMesh("Monitor", "Monitor", mb => Geo.ChamferBox(mb, Vector3.zero, new Vector3(0.62f, 0.38f, 0.035f), 0.006f), M["V5_Charcoal"], g, p + Vector3.up * 0.42f, face * Quaternion.Euler(-6, 0, 0), false);
        }
        {
            float ang = 296f; var o = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0, Mathf.Sin(ang * Mathf.Deg2Rad));
            var basePos = o * 12.45f + Vector3.up * 1.06f; var face = Quaternion.LookRotation(-o);   // leaflets face the visitor (outward)
            var stand = Group(g, "BrochureStand"); stand.SetPositionAndRotation(basePos, face);
            GMesh("Stand", "BrochureStand", mb => Geo.ChamferBox(mb, new Vector3(0, 0.17f, 0), new Vector3(0.78f, 0.34f, 0.2f), 0.01f), M["V5_Oak"], stand, basePos, face, false);
            for (int i = 0; i < cur.wings.Count && i < 6; i++)
            {
                int row = i / 3, col = i % 3;
                var local = new Vector3((col - 1) * 0.25f, 0.36f + row * 0.17f, -0.06f + row * 0.07f);
                var lp = stand.TransformPoint(local);
                var tilt = face * Quaternion.Euler(-18, 180, 0);
                var item = cur.wings[i].items.FirstOrDefault();
                GMesh("Leaflet", "Leaflet", mb => Geo.ChamferBox(mb, Vector3.zero, new Vector3(0.2f, 0.28f, 0.004f), 0.001f), M["V5_LabelBoard"], stand, lp, tilt, false);
                var mat = item != null ? CurPhotoMat(item.id, out float a) : null;
                if (mat)
                    GMesh("LeafletPhoto", "LeafletPhoto_" + item.id, mb => Geo.ImageQuad(mb, Vector3.zero, Vector3.back, 0.18f, 0.16f), mat, stand, lp + tilt * new Vector3(0, 0.045f, -0.003f), tilt, false);
            }
        }
        {
            float ang = 261f; var o = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0, Mathf.Sin(ang * Mathf.Deg2Rad));
            var p = o * 12.83f + Vector3.up * 0.6f;
            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); disk.name = "InfoRoundel";
            Object.DestroyImmediate(disk.GetComponent<Collider>());
            disk.transform.SetParent(g, false); disk.transform.position = p;
            disk.transform.rotation = Quaternion.LookRotation(o) * Quaternion.Euler(90, 0, 0); disk.transform.localScale = new Vector3(0.34f, 0.006f, 0.34f);
            disk.GetComponent<MeshRenderer>().sharedMaterial = M["V5_Accent"];
            disk.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
            var it = Text3D("i", "<b>i</b>", p + o * 0.01f, o, new Vector2(0.3f, 0.3f), Color.white, g, true, 2.4f);
            if (uiFont) it.font = uiFont;
        }
        MakePoint("InfoDesk", new Vector3(0, 0, -14.3f), 0f, pts);
    }
}
