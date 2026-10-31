using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// v5 museum: a Great Court and six galleries modelled on real museums, built from Content/curated.json.
public static partial class MuseumBuilder
{
    // ------------------------------------------------------------------ curated data
    [Serializable] class CurJ { public string title; public LobbyJ lobby; public List<CWingJ> wings; }
    [Serializable] class LobbyJ { public string title, welcome; }
    [Serializable] class CWingJ { public string id, title, subtitle, years, gallery, color, intro; public List<CItemJ> items; public CItemJ centre; public List<QuizJ> quiz; }
    [Serializable] class CItemJ { public string id, title, date, medium, where, label, narration, wiki, query, kind, replica; public int year; public PlaceJ place; }

    const string CurPath = "Assets/_Museum/Content/curated.json";
    const string CurPhotos = "Assets/_Museum/Curated/Photos";
    const string CurAudio = "Assets/_Museum/Curated/Audio";
    static bool V5Available => File.Exists(CurPath);
    static Dictionary<string, string[]> curCredits;

    class Ctx
    {
        public CWingJ w; public int wi; public Transform root, arch, decor, lights, pts, exhibits;
        public readonly List<Artifact> tour = new List<Artifact>();
        public readonly List<CItemJ> placed = new List<CItemJ>();
        public Color accent; public string key;
        public int next;                         // next item to place
    }

    class WSlot
    {
        public Vector3 c, n, spot; public float maxW, maxH; public string frame = "mount"; public float curveR;   // curveR > 0: concave wall of that radius
        public WSlot(Vector3 c, Vector3 n, float maxW, float maxH, Vector3 spot, string frame = "mount") { this.c = c; this.n = n.normalized; this.maxW = maxW; this.maxH = maxH; this.spot = spot; this.frame = frame; }
    }

    // ------------------------------------------------------------------ materials
    static void CreateV5Materials()
    {
        PbrDesat("V5_PortlandStone", "stone_buff2", 2.6f, 0.3f, 1.2f, new Color(1f, 0.98f, 0.94f), 0.18f, new Color(0.86f, 0.84f, 0.8f));
        PbrDesat("V5_PaleFloor", "floor_slabs", 2.2f, 0.35f, 1.1f, new Color(0.98f, 0.96f, 0.92f), 0.45f, new Color(0.8f, 0.78f, 0.74f));
        Pbr("V5_DarkFloor", "floor_granite", 1.6f, new Color(0.5f, 0.48f, 0.46f), 0.82f, new Color(0.08f, 0.08f, 0.08f));
        PbrDesat("V5_Plaster", "plaster", 3f, 0.05f, 1.35f, new Color(0.97f, 0.97f, 0.95f), 0.12f, new Color(0.9f, 0.89f, 0.87f));
        PbrDesat("V5_PlasterSage", "plaster", 3f, 0f, 1.2f, new Color(0.62f, 0.66f, 0.58f), 0.12f, new Color(0.6f, 0.62f, 0.56f));
        Pbr("V5_PlasterWarm", "plaster", 3f, new Color(0.9f, 0.85f, 0.76f), 0.12f, new Color(0.86f, 0.8f, 0.7f));
        PbrDesat("V5_GalleryGreen", "plaster", 3f, 0f, 1.2f, new Color(0.24f, 0.33f, 0.29f), 0.14f, new Color(0.22f, 0.29f, 0.26f));
        Pbr("V5_ArchStone", "stone_buff", 2.4f, Color.white, 0.2f, new Color(0.78f, 0.68f, 0.52f));
        Pbr("V5_Sandstone", "stone_buff2", 2.4f, new Color(0.96f, 0.88f, 0.76f), 0.2f, new Color(0.84f, 0.72f, 0.56f));
        Pbr("V5_RedStone", "stone_red", 2.2f, Color.white, 0.2f, new Color(0.6f, 0.3f, 0.22f));
        Pbr("V5_Marble", "marble", 2f, Color.white, 0.78f, new Color(0.92f, 0.91f, 0.89f));
        Pbr("V5_MarbleFloor", "floor_marble", 2.4f, Color.white, 0.72f, new Color(0.9f, 0.88f, 0.85f));
        PbrDesat("V5_Parquet", "wood", 0.55f, 0.45f, 1.0f, new Color(0.86f, 0.76f, 0.64f), 0.5f, new Color(0.55f, 0.38f, 0.22f));
        Pbr("V5_Oak", "wood", 1.2f, new Color(0.82f, 0.68f, 0.52f), 0.4f, new Color(0.5f, 0.36f, 0.22f));
        Pbr("V5_DarkWood", "wood", 1.2f, new Color(0.42f, 0.3f, 0.22f), 0.45f, new Color(0.25f, 0.16f, 0.1f));
        PbrDesat("V5_Concrete", "floor_concrete", 3f, 0.2f, 1.1f, new Color(0.9f, 0.89f, 0.87f), 0.5f, new Color(0.7f, 0.7f, 0.68f));
        Pbr("V5_Granite", "granite", 1.5f, new Color(0.55f, 0.55f, 0.57f), 0.6f, new Color(0.35f, 0.35f, 0.37f));
        Pbr("V5_Khondalite", "stone_buff2", 1.6f, new Color(0.66f, 0.58f, 0.5f), 0.2f, new Color(0.55f, 0.48f, 0.4f));
        Lit("V5_PaintWhite", new Color(0.9f, 0.9f, 0.88f), 0f, 0.3f);
        Lit("V5_Charcoal", new Color(0.11f, 0.11f, 0.115f), 0f, 0.5f);
        Lit("V5_MountBoard", new Color(0.93f, 0.92f, 0.89f), 0f, 0.05f);
        // labels are lit in real galleries; a faint warm glow keeps them crisp white whatever the probes give them
        var lb = Lit("V5_LabelBoard", new Color(0.96f, 0.95f, 0.92f), 0f, 0.15f);
        lb.EnableKeyword("_EMISSION"); lb.SetColor("_EmissionColor", new Color(0.97f, 0.95f, 0.9f) * 0.3f);
        lb.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; EditorUtility.SetDirty(lb);
        Lit("V5_Gilt", new Color(0.83f, 0.66f, 0.33f), 1f, 0.62f);
        Lit("V5_Brass", new Color(0.78f, 0.6f, 0.34f), 1f, 0.7f);
        Lit("V5_Steel", new Color(0.62f, 0.63f, 0.65f), 1f, 0.55f);
        Lit("V5_Bronze", new Color(0.5f, 0.36f, 0.2f), 1f, 0.48f);
        Lit("V5_Leather", new Color(0.13f, 0.09f, 0.07f), 0f, 0.35f);
        Lit("V5_Water", new Color(0.04f, 0.06f, 0.07f), 0f, 0.96f);
        Lit("V5_Terracotta", new Color(0.62f, 0.3f, 0.18f), 0f, 0.25f);
        Lit("V5_JarPaint", new Color(0.06f, 0.04f, 0.035f), 0f, 0.3f);
        Lit("V5_Cotton", new Color(0.92f, 0.9f, 0.84f), 0f, 0.1f);
        Lit("V5_Screen", new Color(0.02f, 0.02f, 0.025f), 0f, 0.9f);
        Glass("V5_Glass", new Color(0.86f, 0.93f, 0.96f, 0.1f));
        Glass("V5_CaseGlass", new Color(0.9f, 0.95f, 0.97f, 0.07f));
        var lay = Lit("V5_Laylight", new Color(0.96f, 0.96f, 0.95f), 0f, 0.1f);
        lay.EnableKeyword("_EMISSION"); lay.SetColor("_EmissionColor", new Color(1f, 0.98f, 0.95f) * 1.4f);
        lay.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        var iron = Lit("V5_Iron", Color.white, 0.55f, 0.32f);
        iron.SetTexture("_BaseMap", NoiseTex("Iron2", 512, 9f, new Color(0.2f, 0.19f, 0.18f), new Color(0.36f, 0.25f, 0.17f), 0.55f)); iron.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
        var solar = Lit("V5_Solar", Color.white, 0.4f, 0.75f);
        solar.SetTexture("_BaseMap", GridTex("SolarCells", 256, 8, new Color(0.04f, 0.07f, 0.2f), new Color(0.62f, 0.64f, 0.68f))); solar.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
        var ring = MakeCircleSprite("Reticle", 0.62f);
        M["TeleportDisc"] = SpriteMat("TeleportRing", ring.texture, new Color(1f, 1f, 1f, 0.22f));
    }

    /// Poly Haven texture with reduced saturation and adjusted brightness (white plaster, pale Portland stone).
    static Material PbrDesat(string name, string key, float tile, float sat, float gain, Color tint, float smooth, Color fallback)
    {
        var m = Pbr(name, key, tile, tint, smooth, fallback);
        var src = $"{PHTex}/{key}_diff.jpg";
        if (!File.Exists(src)) return m;
        var path = $"{TexDir}/{key}_s{Mathf.RoundToInt(sat * 100)}_g{Mathf.RoundToInt(gain * 100)}.jpg";
        if (!File.Exists(path))
        {
            var t = new Texture2D(2, 2, TextureFormat.RGB24, true);
            t.LoadImage(File.ReadAllBytes(src));
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; float g = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                px[i] = new Color32((byte)Mathf.Clamp((g + (c.r - g) * sat) * gain, 0, 255), (byte)Mathf.Clamp((g + (c.g - g) * sat) * gain, 0, 255), (byte)Mathf.Clamp((g + (c.b - g) * sat) * gain, 0, 255), 255);
            }
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToJPG(92)); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti) { ti.wrapMode = TextureWrapMode.Repeat; ti.anisoLevel = 4; ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
        return m;
    }

    static Texture2D NoiseTex(string name, int n, float freq, Color a, Color b, float rust)
    {
        var path = $"{TexDir}/{name}.png";
        if (!File.Exists(path))
        {
            var t = new Texture2D(n, n, TextureFormat.RGB24, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float f = 0, amp = 1, fr = freq, norm = 0;
                    for (int o = 0; o < 5; o++) { f += amp * Mathf.PerlinNoise(u * fr + 17.3f * o, v * fr + 3.1f * o); norm += amp; amp *= 0.5f; fr *= 2.03f; }
                    f /= norm;
                    float r = Mathf.SmoothStep(0.5f, 0.75f, Mathf.PerlinNoise(u * freq * 0.6f + 40, v * freq * 0.6f + 9)) * rust;
                    var c = Color.Lerp(a, b, r) * Mathf.Lerp(0.7f, 1.3f, f);
                    t.SetPixel(x, y, c);
                }
            File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D GridTex(string name, int n, int cells, Color cell, Color line)
    {
        var path = $"{TexDir}/{name}.png";
        if (!File.Exists(path))
        {
            var t = new Texture2D(n, n, TextureFormat.RGB24, false);
            int step = n / cells;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool l = x % step < 2 || y % step < 2;
                    bool bus = (y % step) == step / 2 && x % step > 3;
                    float shade = 0.85f + 0.3f * Mathf.PerlinNoise(x / (float)step, y / (float)step);
                    t.SetPixel(x, y, l ? line : bus ? Color.Lerp(cell, line, 0.5f) : cell * shade);
                }
            File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ geometry helpers
    /// Builds a mesh with Geo/MeshKit, saves it and places it. lightmap=false: lit by light probes (fine detail, small parts).
    static GameObject GMesh(string name, string key, Action<MeshKit.MB> build, Material m, Transform parent, Vector3 pos, Quaternion rot, bool lightmap = true, bool collider = false)
    {
        var mb = new MeshKit.MB(); build(mb);
        var mesh = MeshKit.Save("V5_" + key, mb, lightmap);
        var go = MeshObj(name, mesh, m, parent, pos, rot, true);
        if (!lightmap) go.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
        if (collider)
        {
            // Unity 6.6 no longer pre-bakes collision data for meshes implicitly at build time; opt in per mesh
            if (!mesh.HasPreBakeCollisionMesh(false)) { mesh.SetPreBakeCollisionMesh(false, true); EditorUtility.SetDirty(mesh); }
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
        return go;
    }

    /// Wall along X (constant z) from x0 to x1 with rectangular openings (centre x, width, height).
    static void WallX(Transform p, string name, float x0, float x1, float z, float thick, float y0, float y1, Material m, params Vector3[] openings)
    {
        float cur = x0;
        foreach (var o in openings.OrderBy(o => o.x))
        {
            float a = o.x - o.y / 2, b = o.x + o.y / 2;
            if (a > cur + 0.01f) Box(name, new Vector3((cur + a) / 2, (y0 + y1) / 2, z), new Vector3(a - cur, y1 - y0, thick), m, p);
            if (y1 > o.z + 0.01f) Box(name + "_Lintel", new Vector3(o.x, (o.z + y1) / 2, z), new Vector3(o.y, y1 - o.z, thick), m, p);
            cur = b;
        }
        if (x1 > cur + 0.01f) Box(name, new Vector3((cur + x1) / 2, (y0 + y1) / 2, z), new Vector3(x1 - cur, y1 - y0, thick), m, p);
    }

    /// Wall along Z (constant x) from z0 to z1 with openings (centre z, width, height).
    static void WallZ(Transform p, string name, float z0, float z1, float x, float thick, float y0, float y1, Material m, params Vector3[] openings)
    {
        float cur = z0;
        foreach (var o in openings.OrderBy(o => o.x))
        {
            float a = o.x - o.y / 2, b = o.x + o.y / 2;
            if (a > cur + 0.01f) Box(name, new Vector3(x, (y0 + y1) / 2, (cur + a) / 2), new Vector3(thick, y1 - y0, a - cur), m, p);
            if (y1 > o.z + 0.01f) Box(name + "_Lintel", new Vector3(x, (o.z + y1) / 2, o.x), new Vector3(thick, y1 - o.z, o.y), m, p);
            cur = b;
        }
        if (z1 > cur + 0.01f) Box(name, new Vector3(x, (y0 + y1) / 2, (cur + z1) / 2), new Vector3(thick, y1 - y0, z1 - cur), m, p);
    }

    /// Shadow-gap skirting: a thin dark recess at the foot of a wall, as in modern galleries.
    static void ShadowGap(Transform p, Vector3 a, Vector3 b, Vector3 inward)
    {
        var mid = (a + b) / 2; var len = (b - a).magnitude;
        var along = (b - a).normalized;
        var size = new Vector3(Mathf.Abs(along.x) * len + Mathf.Abs(inward.x) * 0.02f, 0.06f, Mathf.Abs(along.z) * len + Mathf.Abs(inward.z) * 0.02f);
        Box("ShadowGap", mid + Vector3.up * 0.03f + inward * 0.005f, size, M["V5_Charcoal"], p);
    }

    static void Bench(Transform p, Vector3 pos, float yaw, string seat = "V5_Oak", float len = 2.2f)
    {
        var g = Group(p, "Bench"); g.position = pos; g.rotation = Quaternion.Euler(0, yaw, 0);
        GMesh("Seat", $"BenchSeat_{len:0.#}", mb => Geo.ChamferBox(mb, new Vector3(0, 0.43f, 0), new Vector3(len, 0.07f, 0.48f), 0.015f), M[seat], g, g.position, g.rotation, false);
        foreach (var s in new[] { -1f, 1f })
            GMesh("Leg", "BenchLeg", mb => { MeshKit.Box(mb, new Vector3(0, 0.2f, 0), new Vector3(0.05f, 0.4f, 0.42f)); }, M["V5_Charcoal"], g, g.TransformPoint(new Vector3(s * (len / 2 - 0.2f), 0, 0)), g.rotation, false);
        var col = g.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0.25f, 0); col.size = new Vector3(len, 0.5f, 0.5f);
    }

    static void RoundSeat(Transform p, Vector3 pos, float r = 0.9f)
    {
        GMesh("RoundSeat", $"RoundSeat_{r:0.#}", mb => { Geo.RingWall(mb, 0f, r, 0.05f, 0.44f, 0, 360, 48); Geo.RingWall(mb, 0f, r - 0.06f, 0f, 0.05f, 0, 360, 48); }, M["V5_Leather"], p, pos, Quaternion.identity, false);
    }

    /// A row of ceiling track with black spot fixtures pointing at each target.
    static void TrackWithFixtures(Transform p, Vector3 a, Vector3 b, IEnumerable<(Vector3 from, Vector3 to)> fixtures)
    {
        GMesh("Track", $"Track_{(b - a).magnitude:0.#}", mb => Geo.Bar(mb, Vector3.zero, b - a, 0.035f, 0.03f), M["V5_Charcoal"], p, a, Quaternion.identity, false);
        foreach (var (from, to) in fixtures)
        {
            var dir = (to - from).normalized;
            GMesh("Fixture", "SpotFixture", mb =>
            {
                Geo.Bar(mb, new Vector3(0, 0.0f, 0), new Vector3(0, -0.12f, 0), 0.02f, 0.02f);
                Geo.Revolve(mb, new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.045f, 0), new Vector2(0.05f, 0.16f), new Vector2(0.001f, 0.16f) }, 16, true);
            }, M["V5_Charcoal"], p, from, Quaternion.FromToRotation(Vector3.up, -dir), false);
        }
    }

    // ------------------------------------------------------------------ exhibits
    static Sprite CurSprite(string id) => AssetDatabase.LoadAssetAtPath<Sprite>($"{CurPhotos}/{id}.jpg");

    static string CurCredit(string id)
    {
        if (curCredits == null)
        {
            curCredits = new Dictionary<string, string[]>();
            var f = "Docs/curated_credits.csv";
            if (File.Exists(f))
                foreach (var line in File.ReadAllLines(f).Skip(1))
                {
                    var cells = ParseCsv(line); if (cells.Length >= 6) curCredits[cells[0]] = cells;
                }
        }
        if (!curCredits.TryGetValue(id, out var c)) return "";
        var author = c[2].Length > 60 ? c[2].Substring(0, 57) + "..." : c[2];
        return $"Photo: {author} / {c[3]} / Wikimedia Commons";
    }

    static string[] ParseCsv(string line)
    {
        var res = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (q) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(ch); }
            else if (ch == '"') q = true; else if (ch == ',') { res.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res.ToArray();
    }

    static ArtifactData CurData(CItemJ it, int stop, string era = null)
    {
        var d = Data("V5", it.id, it.title, era ?? it.date, it.narration, CurSprite(it.id));
        d.stop = stop;
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{CurAudio}/{it.id}.ogg");
        if (clip) d.narration = clip;
        EditorUtility.SetDirty(d);
        return d;
    }

    static Material CurPhotoMat(string id, out float aspect)
    {
        var sp = CurSprite(id); aspect = 4f / 3f;
        if (!sp) return null;
        var tex = sp.texture; aspect = tex.width / (float)Mathf.Max(1, tex.height);
        var m = Lit("CurPhoto_" + id, Color.white, 0f, 0.18f, "/Photos");
        m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
        m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
        return m;
    }

    // ---- Legibility on a phone ------------------------------------------------------------------------------
    // A phone in a Cardboard viewer gives each eye roughly 14 px per degree, so world text is sized by the angle it
    // covers from where the visitor stands: TMP world font size f has a cap height of about 0.07·f metres, and a cap
    // height of at least ~1.2° (0.021·distance) reads comfortably. Hence f >= 0.3 × viewing distance for text that
    // must be read, and long texts move to the audio guide's large on-screen captions instead of the wall.
    public const float ReadK = 0.34f;
    static float Legible(float distance) => ReadK * distance;

    /// Large-print object label ("tombstone"): audio stop, title, date and place. The long label text is spoken by the
    /// audio guide and shown as large captions, so the wall only carries what can be read from the viewing point.
    static void ObjectLabel(Transform p, CItemJ it, int stop, Vector3 center, Vector3 normal, float viewDist, float w = 0.95f)
    {
        var g = Group(p, "Label_" + it.id);
        var rot = FaceTo(normal);
        Vector3 up = rot * Vector3.up, pad = normal * 0.014f;
        float f = Legible(viewDist), m = 0.045f, iw = w - 2 * m, em = f * 0.1f;
        // lay the board out top-down from the text it carries: chip, title (wrapped), date and place.
        // Lora averages ~0.55 em per character (bold ~0.6); word wrapping loses a little more, hence the 0.9.
        int Lines(string t, float size, float perEm) => string.IsNullOrEmpty(t) ? 0 : Mathf.Max(1, Mathf.CeilToInt(t.Length / (0.9f * iw / (size * 0.1f * perEm))));
        int titleLines = Lines(it.title, f, 0.6f);
        int metaLines = Lines(it.date, f * 0.85f, 0.62f) + Lines(it.place?.name, f * 0.85f, 0.62f);
        float chipH = em * 1.1f, titleH = titleLines * em * 1.15f, metaH = metaLines * em * 0.85f * 1.2f, gap = em * 0.4f;
        float h = 2 * m + chipH + gap + titleH + gap + metaH + em * 0.3f;
        GMesh("Board", $"LabelBoard_{w:0.##}x{h:0.##}", mb => Geo.ChamferBox(mb, Vector3.zero, new Vector3(w, h, 0.012f), 0.003f), M["V5_LabelBoard"], g, center + normal * 0.006f, rot, true);   // lightmapped: warm gallery light, not the sky-tinted probes
        var ink = new Color(0.12f, 0.12f, 0.12f);
        float y = h / 2 - m;
        TextMeshPro Row(string name, string text, float rowH, float size, Color c, bool bold)
        {
            var t = Text3D(name, text, center + up * (y - rowH / 2) + pad, normal, new Vector2(iw, rowH), c, g, bold, size);
            t.alignment = TextAlignmentOptions.TopLeft; t.enableAutoSizing = false; t.fontSize = size;
            t.overflowMode = TextOverflowModes.Overflow; y -= rowH;
            return t;
        }
        Row("Stop", $"AUDIO {stop}", chipH, f * 0.8f, new Color(0.6f, 0.4f, 0.16f), true); y -= gap;
        Row("Title", it.title, titleH, f, ink, true).lineSpacing = -8f; y -= gap;
        Row("Meta", $"{it.date}\n<i>{it.place?.name ?? ""}</i>", metaH, f * 0.85f, new Color(0.16f, 0.16f, 0.16f), false);
        // credit under the board, small (attribution, not meant to be read from the viewing point)
        var credit = it.kind == "replica" ? "Replica made for this museum" : CurCredit(it.id);
        if (!string.IsNullOrEmpty(credit))
        {
            var c = Text3D("Credit", credit, center - up * (h / 2 + 0.05f) + normal * 0.004f, normal, new Vector2(w, 0.07f), new Color(0.35f, 0.35f, 0.35f), g, false, 0.24f);
            c.alignment = TextAlignmentOptions.TopLeft;
        }
    }

    /// Gallery introduction lettered directly on the wall, sized to be read from about 4 m. Only the opening
    /// sentences that fit at that size are lettered; the full introduction is narrated when the visitor arrives.
    static void WallIntro(Transform p, CWingJ w, Vector3 center, Vector3 normal, float width, Color ink, float viewDist = 4f)
    {
        var g = Group(p, "WallIntro");
        var title = Text3D("Title", w.title, center + Vector3.up * 1.25f + normal * 0.01f, normal, new Vector2(width, 0.75f), ink, g, false, 9f);
        title.alignment = TextAlignmentOptions.BottomLeft;
        float f = Legible(viewDist);
        var sub = Text3D("Sub", $"{w.subtitle}  ·  {w.years}", center + Vector3.up * 0.72f + normal * 0.01f, normal, new Vector2(width, 0.28f), ink * 0.85f + new Color(0, 0, 0, 0.15f), g, false, Mathf.Max(2.4f, f * 1.4f));
        sub.alignment = TextAlignmentOptions.TopLeft; sub.fontStyle = FontStyles.Italic; sub.fontSizeMin = f;
        // capacity at size f: ~0.5 em per character, 1.25 em per line
        float em = f * 0.1f, boxH = 1.45f;
        int perLine = Mathf.FloorToInt(width / (em * 0.5f)), lines = Mathf.FloorToInt(boxH / (em * 1.3f));
        var body = Text3D("Body", FitSentences(w.intro, (int)(perLine * lines * 0.85f)), center - Vector3.up * 0.25f + normal * 0.01f, normal, new Vector2(width, boxH), ink, g, false, f * 1.15f);
        body.alignment = TextAlignmentOptions.TopLeft; body.fontSizeMin = f;
    }

    /// The leading whole sentences of text that fit in maxChars (at least one sentence).
    static string FitSentences(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
        var parts = System.Text.RegularExpressions.Regex.Split(text.Trim(), @"(?<=[.!?])\s+");
        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (sb.Length > 0 && sb.Length + 1 + part.Length > maxChars) break;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(part);
        }
        return sb.ToString();
    }

    /// A framed print on a wall: photo, mount, frame, label beside it, baked spot + realtime highlight, stand point.
    static Artifact WallExhibit(Ctx x, CItemJ it, int stop, WSlot s)
    {
        var right = Vector3.Cross(Vector3.up, s.n).normalized;      // to the visitor's right when facing the wall
        right = -right;
        var mat = CurPhotoMat(it.id, out float a);
        float w = s.maxW, h = w / a;
        if (h > s.maxH) { h = s.maxH; w = h * a; }
        var g = Group(x.exhibits, "Exhibit_" + it.id);
        g.position = s.c;
        var rot = FaceTo(s.n);
        GameObject photo;
        float margin = 0f, fw = 0f;
        if (s.frame == "gilt")
        {
            fw = 0.11f;
            var fr = MeshObj("Frame", MeshKit.Frame($"V5_GiltFrame_{w:0.##}x{h:0.##}", w + 0.01f, h + 0.01f, fw, 0.085f), M["V5_Gilt"], g, s.c + s.n * 0.005f, rot);
            fr.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
        }
        else if (s.frame == "panel")
        {
            GMesh("Panel", $"Dibond_{w:0.##}x{h:0.##}", mb => Geo.ChamferBox(mb, Vector3.zero, new Vector3(w, h, 0.025f), 0.002f), M["V5_Charcoal"], g, s.c + s.n * 0.0125f, rot, false);
        }
        else
        {
            margin = Mathf.Clamp(Mathf.Min(w, h) * 0.12f, 0.06f, 0.16f); fw = 0.03f;
            float W = w + 2 * margin, H = h + 2 * margin;
            GMesh("Mount", $"Mount_{W:0.##}x{H:0.##}", mb => Geo.ChamferBox(mb, Vector3.zero, new Vector3(W, H, 0.02f), 0.001f), M["V5_MountBoard"], g, s.c + s.n * 0.012f, rot, true);
            GMesh("Frame", $"BlackFrame_{W:0.##}x{H:0.##}", mb =>
            {
                float t = 0.03f, d = 0.04f;
                Geo.ChamferBox(mb, new Vector3(0, H / 2 + t / 2, 0), new Vector3(W + 2 * t, t, d), 0.003f);
                Geo.ChamferBox(mb, new Vector3(0, -H / 2 - t / 2, 0), new Vector3(W + 2 * t, t, d), 0.003f);
                Geo.ChamferBox(mb, new Vector3(W / 2 + t / 2, 0, 0), new Vector3(t, H, d), 0.003f);
                Geo.ChamferBox(mb, new Vector3(-W / 2 - t / 2, 0, 0), new Vector3(t, H, d), 0.003f);
            }, M["V5_Charcoal"], g, s.c + s.n * 0.02f, rot, false);
        }
        float zoff = s.frame == "panel" ? 0.027f : 0.024f;
        if (mat)
            photo = GMesh("Photo", "ImageQuad_" + it.id, mb => Geo.ImageQuad(mb, Vector3.zero, Vector3.back, w, h), mat, g, s.c + s.n * zoff, rot, false);
        else
        {
            photo = GMesh("Photo", $"Blank_{w:0.##}x{h:0.##}", mb => Geo.ImageQuad(mb, Vector3.zero, Vector3.back, w, h), M["V5_LabelBoard"], g, s.c + s.n * zoff, rot, false);
            var t = Text3D("Pending", $"<b>{it.title}</b>\n<size=60%>Photograph to come</size>", s.c + s.n * (zoff + 0.004f), s.n, new Vector2(w * 0.8f, h * 0.6f), new Color(0.3f, 0.3f, 0.3f), g, false, 4f);
        }
        // label to the right of the frame, at a comfortable reading height
        float half = w / 2 + margin + fw;
        const float standDist = 1.9f;
        var lc = new Vector3(s.c.x, 1.4f, s.c.z) + right * (half + 0.6f);
        var ln = s.n;
        if (s.curveR > 0f)
        {
            // follow the curve: the label sits on the wall circle at the same arc distance
            var C = s.c + s.n * s.curveR; C.y = 0;
            var d0 = new Vector3(s.c.x, 0, s.c.z) - C;
            float th = (half + 0.6f) / s.curveR * Mathf.Rad2Deg;
            var d1 = Quaternion.Euler(0, th, 0) * d0;
            if (Vector3.Dot(d1 - d0, right) < 0) d1 = Quaternion.Euler(0, -th, 0) * d0;
            lc = C + d1 + Vector3.up * 1.4f; ln = -d1.normalized;
        }
        ObjectLabel(g, it, stop, lc, ln, Mathf.Sqrt(standDist * standDist + (half + 0.6f) * (half + 0.6f)));
        // lights
        float dist = Vector3.Distance(s.spot, s.c);
        float ang = 2f * Mathf.Atan((Mathf.Max(w, h) / 2f + margin + 0.35f) / dist) * Mathf.Rad2Deg;
        AimSpot("Spot_" + it.id, x.lights, s.spot, s.c, Mathf.Clamp(ang, 18f, 70f), 3.2f * dist * dist);
        var hl = HighlightSpot(g, s.spot + Vector3.down * 0.05f, s.c, Mathf.Clamp(ang, 18f, 70f));
        // stand point and the selectable object
        var stand = s.c + s.n * standDist; stand.y = 0;
        var tp = MakePoint("View_" + it.id, stand, Quaternion.LookRotation(-s.n).eulerAngles.y, x.pts);
        var data = CurData(it, stop);
        var art = MakeArtifact(photo, data, tp, s.c + s.n * 0.5f, s.n);
        art.highlight = hl; art.highlightIntensity = 1.6f * dist * dist;
        x.tour.Add(art); x.placed.Add(it);
        return art;
    }

    /// The gallery's replica centrepiece on a low plinth, with a lectern label in front and two spots.
    static Artifact Centrepiece(Ctx x, CItemJ it, Vector3 pos, float yaw, float ceiling, string plinthMat = "V5_PaintWhite")
    {
        int stop = (x.wi + 1) * 100 + 10;
        var g = Group(x.exhibits, "Centrepiece_" + it.id);
        g.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var (obj, top, plinthSize) = Replica(it.replica, g, pos, yaw, plinthMat);
        var fwd = g.forward;
        // lectern label
        var lp = pos + fwd * (plinthSize.z / 2 + 0.9f) + Vector3.Cross(Vector3.up, fwd) * (plinthSize.x / 2 * 0.0f);
        var lect = Group(g, "Lectern");
        GMesh("Post", "LecternPost", mb => Geo.ChamferBox(mb, new Vector3(0, 0.45f, 0), new Vector3(0.08f, 0.9f, 0.08f), 0.01f), M["V5_Charcoal"], lect, lp, Quaternion.Euler(0, yaw, 0), false);
        var face = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-35, 0, 0) * Vector3.forward;
        ObjectLabel(lect, it, stop, lp + Vector3.up * 1.05f, face, 1.9f);
        // lights from the ceiling, front-left and front-right
        var c = pos + Vector3.up * (top * 0.6f);
        foreach (var s in new[] { -1f, 1f })
        {
            var from = pos + fwd * 3.2f + Vector3.Cross(Vector3.up, fwd) * s * 2.6f; from.y = ceiling - 0.3f;
            AimSpot("CentreSpot", x.lights, from, c, 34f, 3.0f * (from - c).sqrMagnitude);
        }
        var hlFrom = pos + fwd * 2.5f; hlFrom.y = ceiling - 0.35f;
        var hl = HighlightSpot(g, hlFrom, c, 30f);
        var stand = pos + fwd * (plinthSize.z / 2 + 2.6f); stand.y = 0;
        var tp = MakePoint("View_" + it.id, stand, yaw + 180f, x.pts);
        var data = CurData(it, stop);
        var art = MakeArtifact(obj, data, tp, c + fwd, fwd);
        art.highlight = hl; art.highlightIntensity = 1.6f * (hlFrom - c).sqrMagnitude;
        x.tour.Add(art); x.placed.Add(it);
        return art;
    }

    /// Digital table (quiz, timeline, map, photos) – a slim charcoal pedestal with an angled screen.
    static void DigitalTable(Ctx x, Vector3 pos, float yaw)
    {
        var g = Group(x.decor, "DigitalTable"); g.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        GMesh("Pedestal", "TablePedestal", mb => { Geo.ChamferBox(mb, new Vector3(0, 0.45f, 0), new Vector3(0.5f, 0.9f, 0.4f), 0.02f); Geo.ChamferBox(mb, new Vector3(0, 0.01f, 0), new Vector3(0.7f, 0.02f, 0.55f), 0.005f); }, M["V5_Charcoal"], g, pos, g.rotation, false);
        var tilt = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-28, 0, 0);
        var top = pos + Vector3.up * 1.0f;
        GMesh("Top", "TableTop", mb => Geo.ChamferBox(mb, Vector3.zero, new Vector3(1.55f, 0.06f, 1.08f), 0.012f), M["V5_Charcoal"], g, top, tilt, false);
        var screenGo = new GameObject("Screen", typeof(RectTransform));
        screenGo.transform.SetParent(g, false);
        screenGo.transform.position = top + tilt * new Vector3(0, 0.032f, 0);
        screenGo.transform.rotation = Quaternion.LookRotation(-(tilt * Vector3.up), tilt * Vector3.forward);
        var canvas = screenGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)screenGo.transform; rt.sizeDelta = new Vector2(1240, 840); rt.localScale = Vector3.one * 0.00115f;   // 1.43 m screen: 26 px text ≈ 1.4° from the stand point
        var k = screenGo.AddComponent<KioskScreen>();
        k.font = uiFont ? uiFont : font; k.accent = x.accent; k.mapSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/UI/india_map.png");
        k.sectionTitle = x.w.title; k.period = x.w.years;
        k.questions = (x.w.quiz ?? new List<QuizJ>()).Select(q => new KioskScreen.Question { text = q.q, options = q.options.ToArray(), answer = q.answer, why = q.why }).ToList();
        k.entries = new List<KioskScreen.Entry>();
        for (int i = 0; i < x.placed.Count; i++)
        {
            var e = x.placed[i];
            k.entries.Add(new KioskScreen.Entry { year = e.year, label = e.date, title = e.title, target = x.tour[i], photo = CurSprite(e.id), credit = CurCredit(e.id),
                places = e.place != null ? new[] { new KioskScreen.Place { name = e.place.name, u = e.place.u, v = e.place.v } } : new KioskScreen.Place[0] });
        }
        k.wingId = x.w.id;
        foreach (var c in screenGo.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = interactiveLayer;
        var tp = MakePoint("View_Table", pos + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -1.25f), yaw, x.pts);
    }

    /// A doorway portal: a short dark passage with a sign; selecting it changes scene.
    static void Doorway(Transform p, string name, Vector3 floorCenter, Vector3 inward, float w, float h, string scene, string arrival, string sign, Material passage)
    {
        var g = Group(p, "Door_" + name);
        var right = Vector3.Cross(Vector3.up, inward).normalized;
        var outward = -inward; float depth = 4f;
        var c = floorCenter + outward * (depth / 2 + 0.2f);
        // passage: floor, ceiling, sides and an end wall
        Box("PassFloor", c + Vector3.down * 0.05f, Abs(right * w + outward * depth) + Vector3.up * 0.1f, passage, g);
        Box("PassCeil", c + Vector3.up * (h + 0.05f), Abs(right * w + outward * depth) + Vector3.up * 0.1f, passage, g);
        foreach (var s in new[] { -1f, 1f }) Box("PassSide", c + right * s * (w / 2 + 0.1f) + Vector3.up * h / 2, Abs(right * 0.2f + outward * depth) + Vector3.up * h, passage, g);
        Box("PassEnd", floorCenter + outward * (depth + 0.3f) + Vector3.up * h / 2, Abs(right * (w + 0.4f) + outward * 0.2f) + Vector3.up * h, passage, g);
        var t = Text3D("Sign", sign, floorCenter + outward * (depth + 0.19f) + Vector3.up * (h * 0.55f), inward, new Vector2(w * 0.8f, 1.2f), new Color(0.92f, 0.9f, 0.85f), g, false, 4f);
        AreaLight("PassageLight", g, floorCenter + outward * (depth * 0.6f) + Vector3.up * (h - 0.05f), new Vector2(w * 0.7f, depth * 0.6f), Vector3.down, 7f, new Color(1f, 0.95f, 0.88f));
        // trigger
        var trig = new GameObject("Portal_" + name); trig.transform.SetParent(g, false);
        trig.transform.position = floorCenter + outward * 0.6f + Vector3.up * (h / 2);
        trig.transform.rotation = Quaternion.LookRotation(inward);
        trig.layer = interactiveLayer;
        var col = trig.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, 0.4f);
        var portal = trig.AddComponent<MuseumPortal>(); portal.sceneName = scene; portal.arrivalTag = arrival;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static AudioGuide MakeAudioGuide(ArtifactData intro, Color accent)
    {
        var go = new GameObject("AudioGuide");
        var src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f;
        var ag = go.AddComponent<AudioGuide>(); ag.font = uiFont ? uiFont : font; ag.source = src; ag.intro = intro; ag.accent = accent;
        return ag;
    }
}
