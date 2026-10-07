using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// Generates the Museum of Indian History from Assets/_Museum/Content/museum.json:
///   Lobby.unity        Grand Hall on the gallery floor plan: title wall, six wing gateways, directory, timeline, guide
///   Wing1..Wing6.unity one wing per era; each section is a room (intro board, centrepiece, one board per sub-topic)
/// Runs once automatically (state file), or via Museum > Rebuild All Scenes.
/// Everything visual is generated: art in Assets/_Museum/Art, models are primitives.
[InitializeOnLoad]
public static partial class MuseumBuilder
{
    const string StateFile = "ProjectSettings/MuseumIHM_State.txt";
    const string ReportFile = "ProjectSettings/MuseumIHM_Report.txt";
    const string JsonPath = "Assets/_Museum/Content/museum.json";
    const string SceneDir = "Assets/_Museum/Scenes";
    const string MatDir = "Assets/_Museum/Materials";
    const string TexDir = "Assets/_Museum/Textures";
    const string ArtDir = "Assets/_Museum/Art";
    const string MeshDir = "Assets/_Museum/Generated";
    const string DataDir = "Assets/_Museum/Data";
    const string FontTtf = "Assets/_Museum/Fonts/Lora-Regular.ttf";
    const string FontAssetPath = "Assets/_Museum/Fonts/Lora SDF.asset";

    const float H = 4f, T = 0.2f, PT = 0.3f;       // ceiling height, wall and partition thickness
    const float HW = 3f;                            // wing rooms: inner half width (z = -3..3)
    const float Slot = 1.9f, BoardW = 1.2f, BoardH = 1.65f;

    static int interactiveLayer, teleportLayer;
    static readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
    static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
    static TMP_FontAsset font;
    static Sprite ring, disc;
    static double waitStart, lastPolyCheck;
    static bool loggedWaiting;

    static bool PolyHavenReady()
    {
        const string manifest = "Assets/_Museum/PolyHaven/manifest.json";
        return File.Exists(manifest) && File.ReadAllText(manifest).Contains("\"complete\": true");
    }

    // ------------------------------------------------------------------ JSON model
    [Serializable] class MuseumJ { public string title; public string welcome; public List<WingJ> wings; }
    [Serializable] class WingJ { public string id, title, subtitle, color, intro; public List<SectionJ> sections; }
    [Serializable] class SectionJ
    {
        public int num, yearStart, yearEnd; public string id, title, period, intro, centrepiece;
        public List<ExhibitJ> exhibits; public List<QuizJ> quiz; public ExhibitJ centre;
    }
    [Serializable] class ExhibitJ { public string id, title, briefTitle, text, yearLabel, stand, show; public int year; public List<PlaceJ> places = new List<PlaceJ>(); }
    [Serializable] class PlaceJ { public string name; public float lat, lon, u, v; }
    [Serializable] class QuizJ { public string q, why; public List<string> options; public int answer; }

    // ------------------------------------------------------------------ entry points
    static MuseumBuilder() => EditorApplication.delayCall += () =>
    {
        if (File.Exists(StateFile) || !File.Exists(JsonPath)) return;
        waitStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += WaitForEditor;
    };

    static void WaitForEditor()
    {
        // never build while in (or entering) Play mode: keep waiting until the editor is idle
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) { waitStart = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.timeSinceStartup - waitStart < 3) return;
        if (!PolyHavenReady())
        {   // wait for Tools/fetch_polyhaven.py to finish (checked every few seconds), so the first build uses real materials
            if (EditorApplication.timeSinceStartup - lastPolyCheck < 5) return;
            lastPolyCheck = EditorApplication.timeSinceStartup;
            if (!loggedWaiting) { Debug.Log("[Museum] Waiting for the Poly Haven download (python3 Tools/fetch_polyhaven.py) before building. Use Museum > Rebuild All Scenes to build now."); loggedWaiting = true; }
            return;
        }
        EditorApplication.update -= WaitForEditor;
        AssetDatabase.Refresh();
        if (!File.Exists(StateFile)) Build();
    }

    [MenuItem("Museum/Rebuild All Scenes (overwrites Lobby and Wing scenes)")]
    static void Rebuild()
    {
        if (EditorUtility.DisplayDialog("Rebuild museum",
            "Regenerates Lobby.unity and Wing1-6.unity from museum.json. Changes made inside those scenes are lost.\n" +
            "Narration audio in Assets/_Museum/Audio is kept and re-assigned.", "Rebuild", "Cancel"))
            Build();
    }

    static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Museum of Indian History", "Stop Play mode first, then run Museum > Rebuild All Scenes.", "OK");
            return;
        }
        interactiveLayer = LayerMask.NameToLayer("Interactive");
        teleportLayer = LayerMask.NameToLayer("Teleport");
        if (interactiveLayer < 0 || teleportLayer < 0) { Debug.LogError("[Museum] Layers 'Interactive' and 'Teleport' are missing."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var report = new StringBuilder();
        var started = DateTime.Now;
        try
        {
            EditorUtility.DisplayProgressBar("Museum of Indian History", "Preparing assets", 0f);
            foreach (var d in new[] { MatDir, MatDir + "/Boards", MatDir + "/Photos", TexDir, MeshDir, DataDir, SceneDir }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            M.Clear(); Meshes.Clear(); MeshKit.ClearCache(); outlineMats.Clear(); credits = null;

            var museum = JsonUtility.FromJson<MuseumJ>(File.ReadAllText(JsonPath));
            int fixedImports = FixArtImports();
            font = CreateFont(museum);
            CleanTmpFallbacks();
            CreateMaterials(museum);
            CreateEraMaterials();
            UpgradeLobbyMaterials();
            ring = MakeCircleSprite("Reticle", 0.62f);
            disc = MakeCircleSprite("TeleportDisc", 0f);

            ApplyMobileSettings(report);
            var scenes = new List<string>();
            int items = 0;
            EditorUtility.DisplayProgressBar("Museum of Indian History", "Menu", 0.02f);
            scenes.Add(BuildMenu(museum));
            EditorUtility.DisplayProgressBar("Museum of Indian History", "Lobby", 0.05f);
            scenes.Add(BuildLobby(museum, ref items));
            for (int i = 0; i < museum.wings.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Museum of Indian History", $"Wing {i + 1}: {museum.wings[i].title}", 0.1f + 0.85f * i / museum.wings.Count);
                int n = 0;
                scenes.Add(BuildWing3(museum, i, ref n));
                items += n;
                report.AppendLine($"Wing{i + 1}: {museum.wings[i].title} — {museum.wings[i].sections.Count} sections, {n} items");
            }

            EditorBuildSettings.scenes = scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
            PlayerSettings.productName = "Museum of Indian History VR";
            AssetDatabase.SaveAssets();
            var audio = MuseumAudioSetup.Assign();
            File.WriteAllText(StateFile, "done");

            report.Insert(0, $"RESULT: BUILT {scenes.Count} scenes, {items} items with text + narration slots ({(DateTime.Now - started).TotalSeconds:0}s)\n" +
                             $"Scenes (build order): {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}\n" +
                             $"Art import settings fixed: {fixedImports}\nFont: {(font ? font.name : "TMP default")}\n{audio}\n");
            EditorSceneManager.OpenScene(scenes[0]);
            MuseumLighting.Request();                                   // bake light next, then (if requested) the iOS build
            EditorApplication.delayCall += MuseumLighting.ContinueIfPending;
        }
        catch (Exception e)
        {
            report.Insert(0, "RESULT: ERROR " + e.Message + "\n" + e.StackTrace + "\n");
            Debug.LogException(e);
        }
        finally { EditorUtility.ClearProgressBar(); }
        File.WriteAllText(ReportFile, report.ToString());
        Debug.Log("[Museum] Build\n" + report);
        EditorUtility.DisplayDialog("Museum of Indian History", report.ToString(), "OK");
    }

    // ------------------------------------------------------------------ assets
    static int FixArtImports()
    {
        int n = 0;
        foreach (var path in Directory.GetFiles(ArtDir, "*.png", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null && MuseumArtImport.Apply(ti, path)) { ti.SaveAndReimport(); n++; }
        }
        foreach (var (dir, poly) in new[] { ("Assets/_Museum/Photos", false), ("Assets/_Museum/PolyHaven/Textures", true) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.GetFiles(dir, "*.jpg").Select(p => p.Replace('\\', '/')))
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                if (poly ? MuseumArtImport.ApplyPoly(ti, path) : MuseumArtImport.ApplyPhoto(ti)) { ti.SaveAndReimport(); n++; }
            }
        }
        return n;
    }

    static TMP_FontAsset CreateFont(MuseumJ museum)
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        try
        {
            if (fa == null)
            {
                var src = AssetDatabase.LoadAssetAtPath<Font>(FontTtf);
                if (src == null) return TMP_Settings.defaultFontAsset;
                fa = TMP_FontAsset.CreateFontAsset(src, 56, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                fa.name = "Lora SDF";
                AssetDatabase.CreateAsset(fa, FontAssetPath);
                foreach (var tex in fa.atlasTextures) { tex.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            // pre-populate every character the museum uses, so text renders even before dynamic glyph generation
            var chars = new HashSet<char>("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?'\"()-–—·<>/&%+=…‘’“”");
            void Add(string s) { if (s != null) foreach (var c in s) chars.Add(c); }
            Add(museum.title); Add(museum.welcome);
            foreach (var w in museum.wings)
            {
                Add(w.title); Add(w.subtitle); Add(w.intro);
                foreach (var s in w.sections) { Add(s.title); Add(s.period); Add(s.intro); foreach (var e in s.exhibits) { Add(e.title); Add(e.text); } }
            }
            fa.TryAddCharacters(new string(chars.ToArray()), out _);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Museum] Could not create Lora font asset, using the TMP default: " + e.Message);
            return TMP_Settings.defaultFontAsset;
        }
    }

    static void CleanTmpFallbacks()     // removes fallbacks left over from deleted fonts
    {
        var list = TMP_Settings.fallbackFontAssets;
        if (list != null && list.RemoveAll(f => f == null) > 0) EditorUtility.SetDirty(TMP_Settings.instance);
    }

    static Texture2D Art(string rel) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{rel}");
    static Sprite ArtSprite(string rel) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{rel}");

    static Color Hex(string h) => ColorUtility.TryParseHtmlString("#" + h, out var c) ? c : Color.gray;

    static void CreateMaterials(MuseumJ museum)
    {
        Lit("Wall_Basalt", new Color(0.36f, 0.32f, 0.29f));
        Lit("Floor_Kota", new Color(0.50f, 0.50f, 0.47f), 0f, 0.5f);
        Lit("Ceiling_Plaster", new Color(0.86f, 0.80f, 0.68f));
        Lit("Plaster_Cream", new Color(0.90f, 0.84f, 0.70f));
        Lit("Teak", new Color(0.38f, 0.22f, 0.11f), 0f, 0.35f);
        Lit("Teak_Dark", new Color(0.22f, 0.12f, 0.06f), 0f, 0.4f);
        Lit("Saffron", new Color(1f, 0.53f, 0.08f));
        Lit("Marigold", new Color(1f, 0.62f, 0.05f));
        Lit("Leaf_Green", new Color(0.18f, 0.45f, 0.16f));
        Lit("Red_Cloth", new Color(0.55f, 0.06f, 0.08f));
        Lit("Basalt_Dark", new Color(0.18f, 0.17f, 0.16f));
        Lit("Hall_Lobby", new Color(0.48f, 0.11f, 0.13f));
        Lit("Hall_Foyer", new Color(0.84f, 0.66f, 0.40f));
        Lit("Hall_Guide", new Color(0.13f, 0.37f, 0.41f));
        Lit("Hall_Directory", new Color(0.19f, 0.33f, 0.24f));
        // objects
        Lit("Gold", new Color(0.95f, 0.74f, 0.30f), 1f, 0.65f);
        Lit("Brass", new Color(0.82f, 0.62f, 0.26f), 1f, 0.55f);
        Lit("Silver", new Color(0.80f, 0.80f, 0.82f), 1f, 0.6f);
        Lit("Steel", new Color(0.62f, 0.64f, 0.67f), 0.9f, 0.6f);
        Lit("Iron", new Color(0.30f, 0.28f, 0.27f), 0.8f, 0.35f);
        Lit("Bronze", new Color(0.55f, 0.37f, 0.20f), 0.85f, 0.5f);
        Lit("Gunmetal", new Color(0.24f, 0.22f, 0.20f), 0.9f, 0.45f);
        Lit("Copper", new Color(0.72f, 0.42f, 0.25f), 0.85f, 0.45f);
        Lit("Flint", new Color(0.52f, 0.46f, 0.38f), 0f, 0.45f);
        Lit("Steatite", new Color(0.82f, 0.77f, 0.66f));
        Lit("Terracotta", new Color(0.70f, 0.36f, 0.22f));
        Lit("Marble", new Color(0.93f, 0.91f, 0.87f), 0f, 0.6f);
        Lit("Sandstone", new Color(0.82f, 0.70f, 0.52f));
        Lit("Red_Sandstone", new Color(0.64f, 0.30f, 0.20f));
        Lit("Fort_Stone", new Color(0.50f, 0.46f, 0.40f));
        Lit("Sea_Blue", new Color(0.18f, 0.38f, 0.52f), 0f, 0.7f);
        Lit("Sail_Cloth", new Color(0.92f, 0.87f, 0.76f));
        Lit("Parchment", new Color(0.93f, 0.88f, 0.74f));
        Lit("White", new Color(0.95f, 0.95f, 0.93f));
        Lit("India_Green", new Color(0.07f, 0.53f, 0.03f));
        Lit("Navy", new Color(0.05f, 0.05f, 0.5f));
        Lit("Hill_Green", new Color(0.33f, 0.40f, 0.24f));
        Emissive("Lantern_Glow", new Color(1f, 0.62f, 0.25f), 2.2f);
        Emissive("Flame", new Color(1f, 0.55f, 0.1f), 3f);

        foreach (var w in museum.wings)
        {
            var c = Hex(w.color);
            Lit("Wall_" + w.id, Color.Lerp(c, new Color(0.9f, 0.84f, 0.7f), 0.15f));
            Lit("Wall_" + w.id + "_b", Color.Lerp(c, new Color(0.9f, 0.84f, 0.7f), 0.32f));
            Emissive("Portal_" + w.id, Color.Lerp(c, Color.white, 0.25f), 1.2f);
            TexMat(w.id + "_banner", $"Wings/{w.id}_banner.png");
            TexMat(w.id + "_title", $"Wings/{w.id}_title.png");
            foreach (var s in w.sections)
            {
                TexMat(s.id + "_intro", $"Sections/{s.id}.png");
                TexMat(s.id + "_illus", $"Illus/{s.id}.png");
                foreach (var e in s.exhibits) TexMat(e.id, $"Boards/{e.id}.png", subdir: "/Boards");
            }
        }
        Emissive("Portal_Lobby", new Color(1f, 0.6f, 0.2f), 1.2f);
        foreach (var n in new[] { "lobby_title", "lobby_timeline", "lobby_guide", "lobby_directory" }) TexMat(n, $"Lobby/{n}.png");
        TexMat("Dado", "tex_dado.png");
        TexMat("Frieze", "tex_frieze.png");
        TexMat("Dhurrie", "tex_dhurrie.png");
        TexMat("Rangoli", "tex_rangoli.png", alphaClip: true);
    }

    static Material GetOrCreate(string name, Shader sh, string subdir = "")
    {
        var path = $"{MatDir}{subdir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }

    static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

    static Material Lit(string name, Color c, float metallic = 0f, float smooth = 0.2f, string subdir = "")
    {
        var m = GetOrCreate(name, LitShader, subdir);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
        return M[name] = m;
    }

    static Material Emissive(string name, Color c, float intensity)
    {
        var m = Lit(name, c, 0f, 0.3f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * intensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return m;
    }

    static Material TexMat(string name, string file, float smooth = 0.1f, bool alphaClip = false, string subdir = "")
    {
        var m = Lit(name, Color.white, 0f, smooth, subdir);
        var tex = Art(file);
        if (tex == null) Debug.LogWarning("[Museum] Missing art " + file);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        if (alphaClip && m.HasProperty("_AlphaClip"))
        {
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        return m;
    }

    static Sprite MakeCircleSprite(string name, float inner)
    {
        var path = $"{TexDir}/{name}.png";
        if (!File.Exists(path))
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = Mathf.Clamp01((0.98f - r) * n * 0.5f);
                    float a = inner > 0f ? outer * Mathf.Clamp01((r - inner) * n * 0.5f) : outer * Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0.55f, 0.95f, r));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite; ti.alphaIsTransparency = true; ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Material SpriteMat(string name, Texture tex, Color c)
    {
        var m = GetOrCreate(name, Shader.Find("Sprites/Default"));
        m.mainTexture = tex; m.color = c;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ data assets
    static ArtifactData Data(string folder, string id, string title, string era, string text, Sprite image)
    {
        Directory.CreateDirectory($"{DataDir}/{folder}");
        var path = $"{DataDir}/{folder}/{id}.asset";
        var d = AssetDatabase.LoadAssetAtPath<ArtifactData>(path);
        if (d == null) { d = ScriptableObject.CreateInstance<ArtifactData>(); AssetDatabase.CreateAsset(d, path); }
        d.id = id; d.title = title; d.era = era; d.text = text; d.image = image;
        EditorUtility.SetDirty(d);
        return d;
    }

    // ------------------------------------------------------------------ geometry helpers
    static Transform Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Box(string name, Vector3 center, Vector3 size, Material m, Transform parent, float yaw = 0f, bool isStatic = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        if (isStatic) MakeStatic(go);
        return go;
    }

    static void MakeStatic(GameObject go) =>
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                   StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

    static Transform P(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, string mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = M[mat];
        return go.transform;
    }

    static Mesh QuadMesh(float uRep, float vRep)
    {
        string key = $"Quad_{uRep:0.##}x{vRep:0.##}";
        if (Meshes.TryGetValue(key, out var cached)) return cached;
        var path = $"{MeshDir}/{key}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = key };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(uRep, 0), new Vector2(0, vRep), new Vector2(uRep, vRep) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, path);
        }
        if (mesh.uv2 == null || mesh.uv2.Length == 0)
        {   // lightmap UVs (everything static is baked)
            mesh.uv2 = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            EditorUtility.SetDirty(mesh);
        }
        return Meshes[key] = mesh;
    }

    /// A flat textured panel. 'normal' points toward the viewer.
    static GameObject Panel(string name, Vector3 center, Vector3 normal, float w, float h, Material m, Transform parent, float uRep = 1f, float vRep = 1f, bool isStatic = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var up = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
        go.transform.rotation = Quaternion.LookRotation(-normal, up);
        go.transform.localScale = new Vector3(w, h, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh(Mathf.Max(0.25f, Mathf.Round(uRep * 4f) / 4f), vRep);
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        if (isStatic) MakeStatic(go);
        return go;
    }

    static void Wall(string name, Vector2 a, Vector2 b, Transform parent, Material m = null, float y0 = 0f, float y1 = H, float thick = T)
    {
        var d = b - a; var mid = (a + b) * 0.5f;
        float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        Box(name, new Vector3(mid.x, (y0 + y1) * 0.5f, mid.y), new Vector3(thick, y1 - y0, d.magnitude + thick), m ?? M["Wall_Basalt"], parent, yaw);
    }

    static void WallWithDoor(string name, Vector2 a, Vector2 b, float g0, float g1, Transform parent)
    {
        var dir = (b - a).normalized;
        Wall(name + "_A", a, a + dir * g0, parent);
        Wall(name + "_B", a + dir * g1, b, parent);
        Wall(name + "_Lintel", a + dir * g0, a + dir * g1, parent, null, 2.4f, H);
    }

    static TextMeshPro Text3D(string name, string text, Vector3 pos, Vector3 face, Vector2 size, Color c, Transform parent, bool bold = false, float maxSize = 30f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(-face);
        var t = go.AddComponent<TextMeshPro>();
        if (font) t.font = font;
        t.text = text; t.color = c; t.alignment = TextAlignmentOptions.Center;
        t.rectTransform.sizeDelta = size;
        t.enableAutoSizing = true; t.fontSizeMin = 0.05f; t.fontSizeMax = maxSize;
        if (bold) t.fontStyle = FontStyles.Bold;
        return t;
    }

    static Vector2 V(float x, float z) => new Vector2(x, z);
    static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

    /// Dado / colour / Paithani-style frieze on a wall face.
    static void Finish(Transform p, string mat, Vector2 a, Vector2 b, Vector3 normal, bool frieze = true)
    {
        const float off = 0.012f;
        var d = b - a; float len = d.magnitude; if (len < 0.05f) return;
        var mid = (a + b) / 2;
        Vector3 C(float y) => new Vector3(mid.x, y, mid.y) + normal * off;
        var g = Group(p, $"Finish_{a.x:0.#}_{a.y:0.#}");
        Panel("Dado", C(0.45f), normal, len, 0.9f, M["Dado"], g, len / 1.2f);
        if (frieze)
        {
            Panel("Plaster", C(1.95f), normal, len, 2.1f, M[mat], g);
            Panel("Frieze", C(3.16f), normal, len, 0.32f, M["Frieze"], g, len / 1.7f);
            Panel("Upper", C(3.66f), normal, len, 0.68f, M["Plaster_Cream"], g);
        }
        else Panel("Plaster", C(2.45f), normal, len, 3.1f, M[mat], g);
    }

    // ------------------------------------------------------------------ shared scene pieces
    static void SetupLighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.58f, 0.52f, 0.46f);
        RenderSettings.ambientEquatorColor = new Color(0.44f, 0.38f, 0.32f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.19f, 0.16f);
        RenderSettings.fog = false;
    }

    static Light NewLight(string name, LightType type, Vector3 pos, Transform parent, float range = 9f, float intensity = 1.25f)
    {
        var lt = NewLightRaw(name, type, pos, parent, range, intensity);
        lt.lightmapBakeType = LightmapBakeType.Baked;          // everything is baked: no runtime lighting cost on phones
        return lt;
    }

    static Light NewLightRaw(string name, LightType type, Vector3 pos, Transform parent, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.shadows = LightShadows.None;      // mobile VR budget
        l.range = range; l.intensity = intensity;
        l.color = new Color(1f, 0.85f, 0.65f);
        return l;
    }

    static void Spot(Transform p, string name, Vector3 from, Vector3 to, float angle, float intensity)
    {
        var s = NewLight(name, LightType.Spot, from, p, 8f, intensity);
        s.transform.LookAt(to);
        s.spotAngle = angle;
    }

    static TeleportPoint MakePoint(string name, Vector3 pos, float yaw, Transform parent)
    {
        var mat = M.TryGetValue("TeleportDisc", out var mm) ? mm : (M["TeleportDisc"] = SpriteMat("TeleportDisc", disc.texture, new Color(1f, 0.55f, 0.1f, 0.6f)));
        var go = new GameObject("TP_" + name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(pos.x, 0.02f, pos.z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.layer = teleportLayer;

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "Disc";
        UnityEngine.Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(go.transform, false);
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        q.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
        q.layer = teleportLayer;
        q.GetComponent<Renderer>().sharedMaterial = mat;

        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.05f, 0f);
        col.size = new Vector3(0.8f, 0.1f, 0.8f);
        var tp = go.AddComponent<TeleportPoint>();
        tp.disc = q.GetComponent<Renderer>();
        return tp;
    }

    static void Marker(string name, Vector3 pos, float yaw, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), Quaternion.Euler(0f, yaw, 0f));
    }

    static void BuildPlayer(Vector3 pos, float yaw)
    {
        var player = new GameObject("Player");
        player.transform.SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), Quaternion.Euler(0f, yaw, 0f));
        player.AddComponent<MuseumCardboardRuntime>();

        var look = new GameObject("EditorLook");
        look.transform.SetParent(player.transform, false);
        look.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        look.AddComponent<EditorMouseLook>();

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(look.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 70f;
        cam.useOcclusionCulling = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.07f, 0.06f);
        camGo.AddComponent<AudioListener>();

        var tpd = camGo.AddComponent<TrackedPoseDriver>();
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationOnly;
        tpd.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        tpd.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));

        var reticle = new GameObject("Reticle").transform;
        reticle.SetParent(camGo.transform, false);
        reticle.localPosition = new Vector3(0f, 0f, 3f);
        var ringQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ringQuad.name = "Ring";
        UnityEngine.Object.DestroyImmediate(ringQuad.GetComponent<Collider>());
        ringQuad.transform.SetParent(reticle, false);
        ringQuad.GetComponent<Renderer>().sharedMaterial = SpriteMat("Reticle", ring.texture, Color.white);

        var dwellCanvas = NewCanvas("DwellCanvas", reticle, new Vector2(100, 100), 0.01f);
        dwellCanvas.transform.localPosition = new Vector3(0f, 0f, -0.001f);
        var fill = UIChild<Image>("DwellFill", dwellCanvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fill.sprite = ring; fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top; fill.fillAmount = 0f; fill.color = new Color(1f, 0.6f, 0.15f);

        var gaze = camGo.AddComponent<GazeInteractor>();
        gaze.gazeMask = (1 << 0) | (1 << interactiveLayer) | (1 << teleportLayer);
        gaze.reticle = reticle; gaze.dwellFill = fill;

        var fadeCanvas = NewCanvas("FadeCanvas", camGo.transform, new Vector2(400, 400), 0.005f);
        fadeCanvas.transform.localPosition = new Vector3(0f, 0f, 0.3f);
        UIChild<Image>("Black", fadeCanvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).color = Color.black;
        var group = fadeCanvas.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false;

        var tele = player.AddComponent<PlayerTeleporter>();
        tele.playerRig = player.transform; tele.headCamera = camGo.transform; tele.fade = group;
    }

    static Canvas NewCanvas(string name, Transform parent, Vector2 size, float scale)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size; rt.localScale = Vector3.one * scale;
        return c;
    }

    static T UIChild<T>(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
        return go.AddComponent<T>();
    }

    static InfoPanel BuildInfoPanel(Transform parent)
    {
        var go = new GameObject("InfoPanel");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(0f, -5f, 0f);       // parked out of sight until something is selected
        var narration = go.AddComponent<AudioSource>();
        narration.playOnAwake = false; narration.spatialBlend = 0f;

        var canvas = NewCanvas("Content", go.transform, new Vector2(900, 600), 0.001f);
        var ct = canvas.transform;
        UIChild<Image>("Background", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).color = new Color(0.13f, 0.06f, 0.04f, 0.95f);
        UIChild<Image>("TopBar", ct, new Vector2(0, 1), Vector2.one, new Vector2(0, -12), Vector2.zero).color = new Color(1f, 0.53f, 0.08f);
        UIChild<Image>("BottomBar", ct, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 6)).color = new Color(0.84f, 0.65f, 0.25f);

        var title = UIChild<TextMeshProUGUI>("Title", ct, new Vector2(0, 1), Vector2.one, new Vector2(30, -105), new Vector2(-30, -22));
        Style(title, 44, new Color(1f, 0.82f, 0.5f), FontStyles.Bold, TextAlignmentOptions.Left);
        title.enableAutoSizing = true; title.fontSizeMin = 28; title.fontSizeMax = 44;
        var era = UIChild<TextMeshProUGUI>("Era", ct, new Vector2(0, 1), Vector2.one, new Vector2(30, -150), new Vector2(-30, -105));
        Style(era, 24, new Color(0.85f, 0.8f, 0.7f), FontStyles.Italic, TextAlignmentOptions.Left);
        era.enableAutoSizing = true; era.fontSizeMin = 16; era.fontSizeMax = 24;
        var picture = UIChild<Image>("Picture", ct, Vector2.zero, new Vector2(0, 1), new Vector2(30, 115), new Vector2(290, -165));
        picture.preserveAspect = true;
        var body = UIChild<TextMeshProUGUI>("Body", ct, Vector2.zero, Vector2.one, new Vector2(310, 115), new Vector2(-30, -165));
        Style(body, 28, new Color(0.96f, 0.93f, 0.87f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
        body.enableAutoSizing = true; body.fontSizeMin = 16; body.fontSizeMax = 30;

        var ip = go.AddComponent<InfoPanel>();
        ip.root = canvas.gameObject;
        ip.titleText = title; ip.eraText = era; ip.bodyText = body; ip.picture = picture;
        ip.narration = narration;

        float x = 30;
        ip.playLabel = MakeGazeButton(ct, "Btn_PlayPause", "Pause", ref x, ip.TogglePlay);
        MakeGazeButton(ct, "Btn_Close", "Close", ref x, ip.Close);
        MakeGazeButton(ct, "Btn_Next", "Next >", ref x, ip.Next);
        return ip;
    }

    static void Style(TMP_Text t, float size, Color c, FontStyles style, TextAlignmentOptions align)
    {
        if (font) t.font = font;
        t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = align;
    }

    static TextMeshProUGUI MakeGazeButton(Transform canvas, string name, string label, ref float x, UnityAction action)
    {
        const float w = 195, h = 70;
        var bg = UIChild<Image>(name, canvas, Vector2.zero, Vector2.zero, new Vector2(x, 25), new Vector2(x + w, 25 + h));
        x += w + 20;
        bg.gameObject.layer = interactiveLayer;
        bg.gameObject.AddComponent<BoxCollider>().size = new Vector3(w, h, 20f);
        var gb = bg.gameObject.AddComponent<GazeButton>();
        gb.background = bg; bg.color = gb.normalColor;
        if (gb.onSelect == null) gb.onSelect = new UnityEvent();
        UnityEventTools.AddPersistentListener(gb.onSelect, action);
        var text = UIChild<TextMeshProUGUI>("Label", bg.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Style(text, 30, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        text.text = label; text.raycastTarget = false;
        return text;
    }

    /// Makes 'target' (with its renderers) a selectable exhibit.
    static Artifact MakeArtifact(GameObject target, ArtifactData data, TeleportPoint stand, Vector3 panelPos, Vector3 panelFace, float spin = 0f)
    {
        var go = target;
        go.layer = interactiveLayer;
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = interactiveLayer;
        foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);

        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.one * 0.3f);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var col = go.AddComponent<BoxCollider>();
        col.center = go.transform.InverseTransformPoint(b.center);
        var ls = go.transform.lossyScale;
        var size = go.transform.InverseTransformVector(b.size);
        col.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.1f / Mathf.Max(ls.x, 0.001f)), Mathf.Abs(size.y) + 0.05f, Mathf.Max(Mathf.Abs(size.z), 0.1f / Mathf.Max(ls.z, 0.001f)));

        var anchor = new GameObject("PanelAnchor").transform;
        anchor.SetParent(go.transform.parent, false);     // not under the model: hover scaling and spin must not move it
        anchor.SetPositionAndRotation(panelPos, Quaternion.LookRotation(-panelFace));

        var art = go.AddComponent<Artifact>();
        art.data = data; art.model = go.transform; art.panelAnchor = anchor; art.standPoint = stand;
        art.spinSpeed = spin;
        return art;
    }

    // ------------------------------------------------------------------ decor helpers
    static void Lantern(Transform parent, Vector3 floorPos)
    {
        var g = Group(parent, "Lantern");
        g.position = new Vector3(floorPos.x, 0f, floorPos.z);
        P(PrimitiveType.Cylinder, g, new Vector3(0, 3.52f, 0), new Vector3(0.015f, 0.46f, 0.015f), "Brass");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 3.06f, 0), new Vector3(0.32f, 0.03f, 0.32f), "Brass");
        P(PrimitiveType.Sphere, g, new Vector3(0, 3.16f, 0), new Vector3(0.22f, 0.16f, 0.22f), "Brass");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 2.86f, 0), new Vector3(0.24f, 0.17f, 0.24f), "Lantern_Glow");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 2.67f, 0), new Vector3(0.28f, 0.025f, 0.28f), "Brass");
        P(PrimitiveType.Sphere, g, new Vector3(0, 2.6f, 0), new Vector3(0.08f, 0.1f, 0.08f), "Brass");
        MakeStatic(g.gameObject);
    }

    static void Pillar(Transform parent, Vector3 basePos, float height = 2.4f)
    {
        var g = Group(parent, "Pillar");
        g.position = basePos;
        float k = height / 2.4f;
        P(PrimitiveType.Cube, g, new Vector3(0, 0.15f, 0), new Vector3(0.42f, 0.3f, 0.42f), "Basalt_Dark");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 1.25f * k, 0), new Vector3(0.26f, 0.95f * k, 0.26f), "Teak");
        foreach (var y in new[] { 0.45f, 1.25f, 2.05f }) P(PrimitiveType.Cylinder, g, new Vector3(0, y * k, 0), new Vector3(0.31f, 0.035f, 0.31f), "Brass");
        P(PrimitiveType.Cube, g, new Vector3(0, 2.26f * k, 0), new Vector3(0.4f, 0.1f, 0.4f), "Teak_Dark");
        P(PrimitiveType.Cube, g, new Vector3(0, 2.36f * k, 0), new Vector3(0.62f, 0.1f, 0.3f), "Teak_Dark");
        MakeStatic(g.gameObject);
    }

    static void Toran(Transform p, Vector3 a, Vector3 b, Vector3 normal)
    {
        var g = Group(p, "Toran");
        int n = 13;
        var len = (b - a).magnitude;
        var rod = P(PrimitiveType.Cylinder, g, (a + b) / 2, new Vector3(0.012f, len / 2, 0.012f), "Gold");
        rod.rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        for (int i = 0; i < n; i++)
        {
            var pos = Vector3.Lerp(a, b, i / (n - 1f)) + normal * 0.02f;
            if (i % 2 == 0) { var l = P(PrimitiveType.Cube, g, pos + Vector3.down * 0.1f, new Vector3(0.07f, 0.17f, 0.008f), "Leaf_Green"); l.rotation = Quaternion.LookRotation(normal); }
            else P(PrimitiveType.Sphere, g, pos + Vector3.down * 0.06f, Vector3.one * 0.07f, "Marigold");
        }
        MakeStatic(g.gameObject);
    }

    static void Samai(Transform p, Vector3 pos)
    {
        var g = Group(p, "Samai_Lamp"); g.position = pos;
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.03f, 0), new Vector3(0.34f, 0.03f, 0.34f), "Brass");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.6f, 0), new Vector3(0.05f, 0.57f, 0.05f), "Brass");
        float[] ys = { 0.55f, 0.85f, 1.12f }, ds = { 0.26f, 0.21f, 0.16f };
        for (int i = 0; i < 3; i++)
        {
            P(PrimitiveType.Cylinder, g, new Vector3(0, ys[i], 0), new Vector3(ds[i], 0.02f, ds[i]), "Brass");
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f + i;
                P(PrimitiveType.Sphere, g, new Vector3(Mathf.Cos(a) * ds[i] * 0.42f, ys[i] + 0.035f, Mathf.Sin(a) * ds[i] * 0.42f), new Vector3(0.022f, 0.04f, 0.022f), "Flame");
            }
        }
        P(PrimitiveType.Sphere, g, new Vector3(0, 1.22f, 0), new Vector3(0.07f, 0.1f, 0.07f), "Brass");
        MakeStatic(g.gameObject);
    }

    static void Nagara(Transform p, Vector3 pos)
    {
        var g = Group(p, "Nagara_Drum"); g.position = pos;
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.18f, 0), new Vector3(0.5f, 0.18f, 0.5f), "Teak_Dark");
        P(PrimitiveType.Sphere, g, new Vector3(0, 0.5f, 0), new Vector3(0.62f, 0.5f, 0.62f), "Copper");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.74f, 0), new Vector3(0.6f, 0.02f, 0.6f), "Parchment");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.71f, 0), new Vector3(0.64f, 0.025f, 0.64f), "Red_Cloth");
        MakeStatic(g.gameObject);
    }

    static void Pedestal(Transform parent, Vector3 floor, float top)
    {
        var g = Group(parent, "Pedestal");
        g.position = floor;
        P(PrimitiveType.Cube, g, new Vector3(0, 0.05f, 0), new Vector3(0.82f, 0.1f, 0.82f), "Basalt_Dark");
        P(PrimitiveType.Cube, g, new Vector3(0, top / 2f, 0), new Vector3(0.7f, top - 0.04f, 0.7f), "Teak_Dark");
        P(PrimitiveType.Cube, g, new Vector3(0, top - 0.015f, 0), new Vector3(0.76f, 0.03f, 0.76f), "Brass");
        MakeStatic(g.gameObject);
    }

    /// A framed picture hung on a wall (returns the picture object).
    static GameObject Framed(Transform p, string name, Material m, Vector3 center, Vector3 normal, float w, float h)
    {
        var f = Box(name + "_Frame", center + normal * 0.02f, new Vector3(w + 0.12f, h + 0.12f, 0.04f), M["Teak_Dark"], p);
        f.transform.rotation = Quaternion.LookRotation(-normal);
        return Panel(name, center + normal * 0.045f, normal, w, h, m, p, isStatic: false);
    }

    /// Gateway with pillars, a glowing membrane and a label; selecting it loads 'scene'.

    /// Outlined text without per-object material instances (setting outlineWidth in edit mode leaks a material per label).
    static readonly Dictionary<string, Material> outlineMats = new Dictionary<string, Material>();
    static void Outline(TMP_Text t, float width, Color32 color)
    {
        if (t.font == null) return;
        string key = $"{t.font.name}_{width:0.00}_{color.r}_{color.g}_{color.b}";
        if (!outlineMats.TryGetValue(key, out var m) || m == null)
        {
            var path = $"Assets/_Museum/Materials/FontOutline_{key}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(t.font.material);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_OutlineWidth", width); m.SetColor("_OutlineColor", color);
            m.EnableKeyword("OUTLINE_ON"); EditorUtility.SetDirty(m);
            outlineMats[key] = m;
        }
        t.fontSharedMaterial = m;
    }

    static void Portal(Transform p, string name, Vector3 floorCenter, Vector3 normal, string scene, string arrival, string label, string glowMat, Color glowColor)
    {
        var g = Group(p, "Portal_" + name);
        var side = Vector3.Cross(Vector3.up, normal).normalized;
        const float w = 1.8f, h = 2.7f;
        foreach (var s in new[] { -1f, 1f }) Pillar(g, floorCenter + side * s * (w / 2 + 0.2f) + normal * 0.1f, h);
        var lintel = Box("Lintel", floorCenter + normal * 0.1f + Vector3.up * (h + 0.12f), new Vector3(w + 0.9f, 0.24f, 0.36f), M["Teak_Dark"], g);
        lintel.transform.rotation = Quaternion.LookRotation(-normal);
        Toran(g, floorCenter + normal * 0.3f + side * -(w / 2) + Vector3.up * (h - 0.05f), floorCenter + normal * 0.3f + side * (w / 2) + Vector3.up * (h - 0.05f), normal);

        var glow = Panel("Glow", floorCenter + normal * 0.06f + Vector3.up * (h / 2), normal, w, h, M[glowMat], g, isStatic: false);
        var t = Text3D("Label", label, floorCenter + normal * 0.1f + Vector3.up * (h * 0.55f), normal, new Vector2(w - 0.2f, 1.1f), Color.white, glow.transform.parent, true, 6f);
        Outline(t, 0.15f, new Color32(60, 20, 10, 255));

        glow.layer = interactiveLayer;
        var col = glow.AddComponent<BoxCollider>();
        col.size = new Vector3(1f, 1f, 0.2f);
        var portal = glow.AddComponent<MuseumPortal>();
        portal.sceneName = scene; portal.arrivalTag = arrival; portal.glow = glow.GetComponent<Renderer>(); portal.idleColor = glowColor;
    }

    // ------------------------------------------------------------------ centrepieces (primitive models)
    static void Centrepiece(string key, Transform g)
    {
        Transform C(PrimitiveType t, float x, float y, float z, float sx, float sy, float sz, string m, float ex = 0, float ey = 0, float ez = 0) =>
            P(t, g, new Vector3(x, y, z), new Vector3(sx, sy, sz), m, new Vector3(ex, ey, ez));
        const PrimitiveType Cu = PrimitiveType.Cube, Cy = PrimitiveType.Cylinder, Sp = PrimitiveType.Sphere;
        switch (key)
        {
            case "handaxe":
                C(Sp, 0, 0.22f, 0, 0.24f, 0.4f, 0.09f, "Flint"); C(Sp, 0.03f, 0.3f, 0.02f, 0.14f, 0.2f, 0.07f, "Flint"); break;
            case "seal":
                C(Cu, 0, 0.2f, 0, 0.32f, 0.32f, 0.07f, "Steatite"); C(Cu, 0, 0.2f, 0.04f, 0.2f, 0.12f, 0.02f, "Terracotta");
                C(Cu, 0, 0.33f, 0.04f, 0.24f, 0.03f, 0.02f, "Terracotta"); break;
            case "fire_altar":
                C(Cu, 0, 0.06f, 0, 0.56f, 0.12f, 0.56f, "Terracotta"); C(Cu, 0, 0.17f, 0, 0.44f, 0.1f, 0.44f, "Terracotta");
                C(Sp, 0, 0.36f, 0, 0.18f, 0.32f, 0.18f, "Flame"); C(Sp, 0.06f, 0.3f, 0.04f, 0.1f, 0.18f, 0.1f, "Flame"); break;
            case "stupa":
                C(Cy, 0, 0.05f, 0, 0.62f, 0.05f, 0.62f, "Sandstone"); C(Sp, 0, 0.12f, 0, 0.5f, 0.46f, 0.5f, "Marble");
                C(Cu, 0, 0.4f, 0, 0.12f, 0.08f, 0.12f, "Sandstone"); C(Cy, 0, 0.5f, 0, 0.012f, 0.08f, 0.012f, "Gold");
                for (int i = 0; i < 3; i++) C(Cy, 0, 0.47f + i * 0.06f, 0, 0.16f - i * 0.04f, 0.008f, 0.16f - i * 0.04f, "Gold"); break;
            case "punch_coin":
                C(Cy, 0, 0.26f, 0, 0.36f, 0.02f, 0.36f, "Silver", 90); break;
            case "kushana_coin":
                C(Cy, 0, 0.26f, 0, 0.36f, 0.02f, 0.36f, "Gold", 90); C(Sp, 0, 0.3f, 0.01f, 0.1f, 0.12f, 0.02f, "Brass"); break;
            case "coins":
                C(Cy, -0.2f, 0.18f, 0, 0.2f, 0.015f, 0.2f, "Silver", 90); C(Cy, 0, 0.2f, 0, 0.22f, 0.015f, 0.22f, "Gold", 90);
                C(Cy, 0.2f, 0.18f, 0, 0.2f, 0.015f, 0.2f, "Copper", 90); break;
            case "column":
                C(Cy, 0, 0.32f, 0, 0.14f, 0.3f, 0.14f, "Marble"); C(Cu, 0, 0.66f, 0, 0.3f, 0.07f, 0.16f, "Marble");
                C(Cy, -0.15f, 0.64f, 0, 0.07f, 0.05f, 0.07f, "Marble", 90); C(Cy, 0.15f, 0.64f, 0, 0.07f, 0.05f, 0.07f, "Marble", 90);
                C(Cu, 0, 0.03f, 0, 0.28f, 0.06f, 0.28f, "Marble"); break;
            case "lion_capital":
                C(Cy, 0, 0.12f, 0, 0.16f, 0.12f, 0.16f, "Sandstone"); C(Sp, 0, 0.3f, 0, 0.24f, 0.16f, 0.24f, "Sandstone");
                C(Cy, 0, 0.4f, 0, 0.3f, 0.04f, 0.3f, "Sandstone");
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI / 2;
                    C(Sp, Mathf.Cos(a) * 0.08f, 0.54f, Mathf.Sin(a) * 0.08f, 0.13f, 0.2f, 0.13f, "Sandstone");
                    C(Sp, Mathf.Cos(a) * 0.12f, 0.64f, Mathf.Sin(a) * 0.12f, 0.1f, 0.1f, 0.1f, "Sandstone");
                }
                break;
            case "ship":
            case "caravel":
                C(Cy, 0, 0.02f, 0, 0.7f, 0.02f, 0.45f, "Sea_Blue");
                C(Cu, 0, 0.1f, 0, 0.62f, 0.1f, 0.18f, "Teak"); C(Cu, 0.28f, 0.14f, 0, 0.1f, 0.12f, 0.16f, "Teak", 0, 0, 30);
                C(Cy, 0, 0.42f, 0, 0.015f, 0.3f, 0.015f, "Teak_Dark");
                C(Cu, 0.01f, 0.45f, 0, 0.01f, 0.36f, 0.34f, key == "ship" ? "Sail_Cloth" : "White");
                if (key == "caravel") C(Cu, 0.02f, 0.5f, 0, 0.005f, 0.1f, 0.1f, "Red_Cloth"); break;
            case "iron_pillar":
                C(Cy, 0, 0.36f, 0, 0.07f, 0.36f, 0.07f, "Iron"); C(Sp, 0, 0.76f, 0, 0.12f, 0.1f, 0.12f, "Iron");
                C(Cy, 0, 0.7f, 0, 0.11f, 0.02f, 0.11f, "Iron"); break;
            case "rock_temple":
                C(Cu, 0, 0.2f, 0, 0.62f, 0.4f, 0.34f, "Basalt_Dark"); C(Cu, 0, 0.14f, 0.16f, 0.14f, 0.24f, 0.04f, "Teak_Dark");
                C(Cy, 0, 0.42f, 0.1f, 0.2f, 0.1f, 0.2f, "Basalt_Dark", 90); break;
            case "shikhara":
                C(Cu, 0, 0.1f, 0, 0.46f, 0.2f, 0.46f, "Sandstone");
                for (int i = 0; i < 5; i++) C(Cu, 0, 0.26f + i * 0.1f, 0, 0.36f - i * 0.06f, 0.1f, 0.36f - i * 0.06f, "Sandstone", 0, i * 9f, 0);
                C(Cy, 0, 0.78f, 0, 0.1f, 0.02f, 0.1f, "Sandstone"); C(Sp, 0, 0.83f, 0, 0.04f, 0.06f, 0.04f, "Gold"); break;
            case "nataraja":
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8;
                    C(Cu, Mathf.Cos(a) * 0.3f, 0.36f + Mathf.Sin(a) * 0.3f, 0, 0.12f, 0.03f, 0.03f, "Bronze", 0, 0, a * Mathf.Rad2Deg + 90);
                }
                C(Cy, 0, 0.36f, 0, 0.05f, 0.14f, 0.05f, "Bronze"); C(Sp, 0, 0.55f, 0, 0.08f, 0.09f, 0.08f, "Bronze");
                C(Cy, 0, 0.44f, 0, 0.02f, 0.18f, 0.02f, "Bronze", 0, 0, 90); C(Cy, 0.06f, 0.18f, 0, 0.02f, 0.12f, 0.02f, "Bronze", 0, 0, 30);
                C(Cy, -0.06f, 0.26f, 0, 0.02f, 0.12f, 0.02f, "Bronze", 0, 0, -70); C(Cu, 0, 0.03f, 0, 0.5f, 0.06f, 0.2f, "Bronze"); break;
            case "qutub":
                for (int i = 0; i < 5; i++) C(Cy, 0, 0.09f + i * 0.17f, 0, 0.22f - i * 0.03f, 0.085f, 0.22f - i * 0.03f, i < 3 ? "Red_Sandstone" : "Marble");
                for (int i = 1; i < 5; i++) C(Cy, 0, i * 0.17f, 0, 0.27f - i * 0.03f, 0.01f, 0.27f - i * 0.03f, "Teak_Dark"); break;
            case "veena":
                C(Sp, 0.25f, 0.18f, 0, 0.26f, 0.26f, 0.26f, "Teak"); C(Sp, -0.3f, 0.2f, 0, 0.16f, 0.16f, 0.16f, "Terracotta");
                C(Cy, 0, 0.26f, 0, 0.05f, 0.32f, 0.05f, "Teak_Dark", 0, 0, 90); C(Cu, -0.42f, 0.3f, 0, 0.08f, 0.12f, 0.03f, "Teak_Dark"); break;
            case "taj":
                C(Cu, 0, 0.04f, 0, 0.7f, 0.08f, 0.7f, "Marble"); C(Cu, 0, 0.2f, 0, 0.42f, 0.24f, 0.42f, "Marble");
                C(Sp, 0, 0.42f, 0, 0.26f, 0.28f, 0.26f, "Marble"); C(Cy, 0, 0.6f, 0, 0.01f, 0.04f, 0.01f, "Gold");
                foreach (var s in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
                    C(Cy, s.x * 0.31f, 0.28f, s.y * 0.31f, 0.04f, 0.24f, 0.04f, "Marble");
                break;
            case "fort":
                C(Cu, 0, 0.03f, 0, 0.7f, 0.06f, 0.7f, "Hill_Green");
                foreach (var s in new[] { new Vector2(1, 0), new Vector2(-1, 0) }) C(Cu, s.x * 0.25f, 0.16f, 0, 0.06f, 0.2f, 0.56f, "Fort_Stone");
                foreach (var s in new[] { 1f, -1f }) C(Cu, 0, 0.16f, s * 0.25f, 0.56f, 0.2f, 0.06f, "Fort_Stone");
                foreach (var s in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
                    C(Cy, s.x * 0.25f, 0.2f, s.y * 0.25f, 0.12f, 0.15f, 0.12f, "Fort_Stone");
                C(Cy, 0.25f, 0.5f, 0.25f, 0.008f, 0.15f, 0.008f, "Teak_Dark"); C(Cu, 0.3f, 0.6f, 0.25f, 0.1f, 0.06f, 0.005f, "Saffron"); break;
            case "cannon":
                C(Cy, 0, 0.2f, 0, 0.12f, 0.36f, 0.12f, "Gunmetal", 0, 0, 80); C(Sp, -0.35f, 0.15f, 0, 0.14f, 0.14f, 0.14f, "Gunmetal");
                foreach (var s in new[] { 1f, -1f }) C(Cy, -0.05f, 0.14f, s * 0.12f, 0.26f, 0.025f, 0.26f, "Teak_Dark", 90); break;
            case "ledger":
                C(Cu, 0, 0.06f, 0, 0.44f, 0.08f, 0.32f, "Red_Cloth"); C(Cu, 0, 0.11f, 0, 0.42f, 0.02f, 0.3f, "Parchment");
                C(Cy, 0.16f, 0.16f, 0.05f, 0.01f, 0.12f, 0.01f, "Teak_Dark", 60, 0, 0); break;
            case "rifle":
                C(Cy, 0, 0.12f, 0, 0.025f, 0.42f, 0.025f, "Gunmetal", 0, 0, 90); C(Cu, -0.42f, 0.1f, 0, 0.22f, 0.08f, 0.05f, "Teak", 0, 0, -10);
                C(Cu, 0, 0.02f, 0, 0.9f, 0.04f, 0.2f, "Red_Cloth"); break;
            case "lamp":
                C(Sp, 0, 0.08f, 0, 0.3f, 0.12f, 0.24f, "Terracotta"); C(Sp, 0.1f, 0.2f, 0, 0.06f, 0.14f, 0.06f, "Flame"); break;
            case "locomotive":
                C(Cu, 0, 0.04f, 0, 0.8f, 0.02f, 0.18f, "Iron");
                C(Cy, 0.08f, 0.22f, 0, 0.18f, 0.24f, 0.18f, "Gunmetal", 0, 0, 90); C(Cu, -0.24f, 0.26f, 0, 0.2f, 0.26f, 0.2f, "Red_Cloth");
                C(Cy, 0.24f, 0.38f, 0, 0.05f, 0.07f, 0.05f, "Gunmetal");
                foreach (var x in new[] { -0.22f, 0f, 0.2f }) foreach (var s in new[] { 1f, -1f }) C(Cy, x, 0.1f, s * 0.1f, 0.14f, 0.015f, 0.14f, "Red_Cloth", 90); break;
            case "flag_pole":
                C(Cy, -0.2f, 0.45f, 0, 0.015f, 0.45f, 0.015f, "Teak_Dark");
                C(Cu, 0.02f, 0.82f, 0, 0.42f, 0.09f, 0.005f, "Saffron"); C(Cu, 0.02f, 0.73f, 0, 0.42f, 0.09f, 0.005f, "White");
                C(Cu, 0.02f, 0.64f, 0, 0.42f, 0.09f, 0.005f, "India_Green"); C(Cy, 0.02f, 0.73f, 0.004f, 0.07f, 0.002f, 0.07f, "Navy", 90); break;
            case "charkha":
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6;
                    C(Cu, -0.1f, 0.32f + Mathf.Sin(a) * 0.22f, Mathf.Cos(a) * 0.22f, 0.02f, 0.12f, 0.02f, "Teak", a * Mathf.Rad2Deg, 0, 0);
                    if (i % 2 == 0) C(Cy, -0.1f, 0.32f, 0, 0.01f, 0.11f, 0.01f, "Teak", a * Mathf.Rad2Deg, 0, 0);
                }
                C(Cu, 0, 0.03f, 0, 0.66f, 0.04f, 0.2f, "Teak_Dark"); C(Cu, -0.1f, 0.15f, 0, 0.03f, 0.3f, 0.03f, "Teak_Dark");
                C(Sp, 0.24f, 0.12f, 0, 0.06f, 0.06f, 0.06f, "White"); break;
            case "torch":
                C(Cy, 0, 0.22f, 0, 0.05f, 0.22f, 0.05f, "Teak"); C(Sp, 0, 0.52f, 0, 0.14f, 0.24f, 0.14f, "Flame"); break;
            case "scroll":
                C(Cu, 0, 0.32f, 0, 0.4f, 0.44f, 0.01f, "Parchment");
                foreach (var y in new[] { 0.1f, 0.54f }) C(Cy, 0, y, 0, 0.04f, 0.24f, 0.04f, "Teak", 0, 0, 90);
                C(Cy, 0.1f, 0.18f, 0.01f, 0.06f, 0.005f, 0.06f, "Red_Cloth", 90); break;
            case "book":
                C(Cu, -0.12f, 0.08f, 0, 0.26f, 0.03f, 0.34f, "Parchment", 0, 0, 8); C(Cu, 0.12f, 0.08f, 0, 0.26f, 0.03f, 0.34f, "Parchment", 0, 0, -8);
                C(Cu, 0, 0.05f, 0, 0.56f, 0.02f, 0.36f, "Navy"); break;
            case "tractor":
                C(Cu, 0.06f, 0.2f, 0, 0.36f, 0.14f, 0.2f, "Red_Cloth"); C(Cu, -0.12f, 0.36f, 0, 0.14f, 0.18f, 0.18f, "Steel");
                foreach (var s in new[] { 1f, -1f }) { C(Cy, -0.12f, 0.15f, s * 0.14f, 0.3f, 0.04f, 0.3f, "Gunmetal", 90); C(Cy, 0.2f, 0.09f, s * 0.13f, 0.18f, 0.03f, 0.18f, "Gunmetal", 90); }
                break;
            case "globe":
                C(Sp, 0, 0.4f, 0, 0.5f, 0.5f, 0.5f, "Sea_Blue"); C(Sp, 0.08f, 0.48f, 0.15f, 0.22f, 0.2f, 0.22f, "Hill_Green");
                C(Cy, 0, 0.08f, 0, 0.04f, 0.08f, 0.04f, "Brass"); C(Cy, 0, 0.02f, 0, 0.3f, 0.02f, 0.3f, "Brass"); break;
            case "rocket":
                C(Cy, 0, 0.42f, 0, 0.12f, 0.3f, 0.12f, "White"); C(Sp, 0, 0.74f, 0, 0.12f, 0.18f, 0.12f, "White");
                C(Cy, 0, 0.5f, 0, 0.125f, 0.03f, 0.125f, "Saffron");
                foreach (var s in new[] { 1f, -1f }) { C(Cy, s * 0.11f, 0.24f, 0, 0.06f, 0.2f, 0.06f, "Steel"); C(Cu, 0, 0.14f, s * 0.08f, 0.02f, 0.14f, 0.1f, "Steel"); }
                break;
            case "dancer":
                C(Cy, -0.15f, 0.12f, 0, 0.18f, 0.12f, 0.18f, "Teak"); C(Cy, -0.15f, 0.245f, 0, 0.16f, 0.005f, 0.16f, "Parchment");
                C(Sp, 0.12f, 0.12f, 0, 0.24f, 0.24f, 0.24f, "Copper"); C(Cy, 0.12f, 0.245f, 0, 0.18f, 0.005f, 0.18f, "Parchment"); break;
            case "lamps":
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.PI * 2 / 5;
                    C(Sp, Mathf.Cos(a) * 0.2f, 0.05f, Mathf.Sin(a) * 0.2f, 0.12f, 0.06f, 0.1f, "Terracotta");
                    C(Sp, Mathf.Cos(a) * 0.2f, 0.12f, Mathf.Sin(a) * 0.2f, 0.03f, 0.07f, 0.03f, "Flame");
                }
                C(Sp, 0, 0.12f, 0, 0.14f, 0.2f, 0.14f, "Brass"); break;
            case "village":
                C(Cy, 0, 0.02f, 0, 0.7f, 0.02f, 0.7f, "Hill_Green");
                foreach (var x in new[] { -0.15f, 0.15f })
                {
                    C(Cy, x, 0.12f, 0, 0.2f, 0.1f, 0.2f, "Terracotta"); C(Sp, x, 0.24f, 0, 0.26f, 0.16f, 0.26f, "Marigold");
                }
                C(Cy, 0.05f, 0.25f, -0.22f, 0.03f, 0.25f, 0.03f, "Teak"); C(Sp, 0.05f, 0.5f, -0.22f, 0.22f, 0.18f, 0.22f, "Leaf_Green"); break;
            case "magnifier":
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8;
                    C(Cu, Mathf.Cos(a) * 0.16f, 0.4f + Mathf.Sin(a) * 0.16f, 0, 0.065f, 0.025f, 0.025f, "Brass", 0, 0, a * Mathf.Rad2Deg + 90);
                }
                C(Cy, 0, 0.4f, 0, 0.3f, 0.003f, 0.3f, "Sea_Blue", 90);
                C(Cy, 0.18f, 0.12f, 0, 0.03f, 0.14f, 0.03f, "Teak_Dark", 0, 0, 30); C(Cu, -0.05f, 0.1f, 0.05f, 0.3f, 0.2f, 0.02f, "Parchment", 70, 0, 0); break;
            default:   // "map" and anything new: a tilted plaque with the section picture
                break;
        }
    }

    // ------------------------------------------------------------------ LOBBY (gallery floor plan)
    static readonly Vector3[] PortalSpots =
    {   // floor centre, wall normal (into the hall)
        new Vector3(15.6f, 0f, 9.2f), new Vector3(9.45f, 0f, 9.2f), new Vector3(3.3f, 0f, 9.2f),
        new Vector3(3.3f, 0f, 0.25f), new Vector3(9.45f, 0f, 0.25f), new Vector3(15.6f, 0f, 0.25f),
    };

    static string BuildLobby(MuseumJ museum, ref int items)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupLighting();
        var root = new GameObject("Lobby").transform;
        var arch = Group(root, "Architecture");
        var fin = Group(root, "Wall Finishes");
        var decor = Group(root, "Decor");
        var lights = Group(root, "Lighting");
        var pts = Group(root, "TeleportPoints");
        var exhibits = Group(root, "Exhibits");

        // shell (the original floor plan without storage / restrooms)
        Box("Floor", new Vector3(12.5f, -0.05f, 4.725f), new Vector3(25f, 0.1f, 9.45f), M["Floor_Kota"], arch);
        Box("Ceiling", new Vector3(12.5f, H + 0.05f, 4.725f), new Vector3(25f, 0.1f, 9.45f), M["Ceiling_Plaster"], arch);
        Wall("Wall_South", V(0, 0), V(18.9f, 0), arch);
        Wall("Wall_West", V(0, 0), V(0, 9.45f), arch);
        Wall("Wall_North", V(0, 9.45f), V(25f, 9.45f), arch);
        Wall("Wall_East", V(25f, 1.85f), V(25f, 9.45f), arch);
        Wall("Wall_GalleryEast", V(18.9f, 0), V(18.9f, 1.85f), arch);
        WallWithDoor("Wall_DirectoryWest", V(18.9f, 1.85f), V(18.9f, 6.75f), 2.3f, 3.9f, arch);
        Wall("Wall_RoomsSouth", V(18.9f, 1.85f), V(25f, 1.85f), arch);
        Wall("Wall_RoomsPartition", V(21.95f, 1.85f), V(21.95f, 6.75f), arch);
        WallWithDoor("TitleWall", V(18.9f, 6.75f), V(25f, 6.75f), 4.4f, 6.0f, arch);

        Finish(fin, "Hall_Lobby", V(0.1f, 0.1f), V(18.8f, 0.1f), Vector3.forward);
        Finish(fin, "Hall_Lobby", V(0.1f, 9.35f), V(18.9f, 9.35f), Vector3.back);
        Finish(fin, "Hall_Lobby", V(0.1f, 0.1f), V(0.1f, 9.35f), Vector3.right);
        Finish(fin, "Hall_Lobby", V(18.8f, 0.1f), V(18.8f, 4.15f), Vector3.left);
        Finish(fin, "Hall_Lobby", V(18.8f, 5.75f), V(18.8f, 6.75f), Vector3.left);
        Finish(fin, "Hall_Foyer", V(18.9f, 9.35f), V(24.9f, 9.35f), Vector3.back);
        Finish(fin, "Hall_Foyer", V(24.9f, 6.85f), V(24.9f, 9.35f), Vector3.left);
        Finish(fin, "Hall_Foyer", V(18.9f, 6.85f), V(23.3f, 6.85f), Vector3.forward, frieze: false);
        Finish(fin, "Hall_Directory", V(19.0f, 1.95f), V(19.0f, 4.15f), Vector3.right);
        Finish(fin, "Hall_Directory", V(19.0f, 5.75f), V(19.0f, 6.65f), Vector3.right);
        Finish(fin, "Hall_Directory", V(19.0f, 6.65f), V(21.85f, 6.65f), Vector3.back);
        Finish(fin, "Hall_Directory", V(21.85f, 1.95f), V(21.85f, 6.65f), Vector3.left);
        Finish(fin, "Hall_Directory", V(19.0f, 1.95f), V(21.85f, 1.95f), Vector3.forward);
        Finish(fin, "Hall_Guide", V(22.05f, 1.95f), V(22.05f, 6.65f), Vector3.right);
        Finish(fin, "Hall_Guide", V(22.05f, 6.65f), V(23.3f, 6.65f), Vector3.back);
        Finish(fin, "Hall_Guide", V(24.9f, 1.95f), V(24.9f, 6.65f), Vector3.left);
        Finish(fin, "Hall_Guide", V(22.05f, 1.95f), V(24.9f, 1.95f), Vector3.forward);

        // ceiling beams and lanterns
        for (float z = 1.575f; z < 9.4f; z += 1.575f) Box("Beam_X", new Vector3(9.45f, H - 0.07f, z), new Vector3(18.9f, 0.14f, 0.16f), M["Teak_Dark"], arch);
        for (float x = 2.1f; x < 18.8f; x += 2.1f) Box("Beam_Z", new Vector3(x, H - 0.1f, 4.725f), new Vector3(0.18f, 0.2f, 9.45f), M["Teak_Dark"], arch);
        foreach (var x in new[] { 3.3f, 9.45f, 15.6f }) Lantern(decor, new Vector3(x, 0, 4.725f));
        Lantern(decor, new Vector3(21f, 0, 8.1f)); Lantern(decor, new Vector3(20.4f, 0, 4.3f)); Lantern(decor, new Vector3(23.5f, 0, 4.3f));

        // title wall, welcome
        var welcome = Data("Lobby", "L00", museum.title, "Welcome", museum.welcome, null);
        var spawn = MakePoint("Foyer", new Vector3(21f, 0, 8.6f), 180f, pts);
        var title = Framed(exhibits, "TitleWall_Art", M["lobby_title"], new Vector3(21f, 2.05f, 6.86f), Vector3.forward, 3.6f, 2.25f);
        var welcomeArt = MakeArtifact(title, welcome, spawn, new Vector3(21f, 1.5f, 7.75f), Vector3.forward);
        items++;
        Toran(decor, new Vector3(23.35f, 2.5f, 6.87f), new Vector3(24.85f, 2.5f, 6.87f), Vector3.forward);
        Samai(decor, new Vector3(19.35f, 0f, 7.3f)); Samai(decor, new Vector3(22.75f, 0f, 7.3f));
        Nagara(decor, new Vector3(19.4f, 0f, 9.0f)); Nagara(decor, new Vector3(24.6f, 0f, 9.0f));
        Panel("Rangoli_Foyer", new Vector3(21f, 0.006f, 8.1f), Vector3.up, 1.3f, 1.3f, M["Rangoli"], decor);
        Panel("Dhurrie_Runner", new Vector3(9.45f, 0.004f, 4.72f), Vector3.up, 17.8f, 0.95f, M["Dhurrie"], decor, 17.8f / 1.9f);
        Box("EntranceSign_Back", new Vector3(18.9f, 3.0f, 8.1f), new Vector3(0.03f, 0.5f, 2.4f), M["Teak_Dark"], decor);
        Text3D("EntranceSign", "Grand Hall  >", new Vector3(18.92f, 3.0f, 8.1f), Vector3.right, new Vector2(2.2f, 0.4f), new Color(1f, 0.85f, 0.55f), decor, true, 4f);
        P(PrimitiveType.Cylinder, decor, new Vector3(18.9f, 3.62f, 7.2f), new Vector3(0.015f, 0.38f, 0.015f), "Brass");
        P(PrimitiveType.Cylinder, decor, new Vector3(18.9f, 3.62f, 9.0f), new Vector3(0.015f, 0.38f, 0.015f), "Brass");

        // wing gateways around the Grand Hall
        for (int i = 0; i < museum.wings.Count && i < PortalSpots.Length; i++)
        {
            var w = museum.wings[i];
            var spot = PortalSpots[i];
            var normal = spot.z > 4.7f ? Vector3.back : Vector3.forward;
            Portal(decor, w.id, spot, normal, "Wing" + (i + 1), "Entry", $"Wing {i + 1}\n<size=70%>{w.title}</size>", "Portal_" + w.id, Hex(w.color) * 1.6f);
            Panel(w.id + "_Banner", new Vector3(spot.x, 3.45f, spot.z - normal.z * 0.12f) + normal * 0.0f, normal, 2.9f, 2.9f * 360f / 1600f, M[w.id + "_banner"], decor);
            var front = spot + normal * 2.1f;
            MakePoint("Portal_" + w.id, front, normal.z < 0 ? 0f : 180f, pts);
            Marker("Arrive_" + w.id, front, normal.z < 0 ? 180f : 0f, pts);
        }
        // hall walkway points
        TeleportPoint hallCentre = null;
        foreach (var x in new[] { 3.3f, 9.45f, 15.6f })
        {
            var tp = MakePoint("Hall_" + x, new Vector3(x, 0, 4.725f), 270f, pts);
            if (x > 15f) hallCentre = tp;
        }
        MakePoint("Timeline", new Vector3(2.2f, 0, 4.725f), 270f, pts);

        // timeline (west wall), directory (east room), visitor guide (south-east room)
        Framed(decor, "Timeline", M["lobby_timeline"], new Vector3(0.12f, 1.95f, 4.725f), Vector3.right, 6.0f, 1.75f);
        MakePoint("Directory", new Vector3(20.0f, 0, 4.3f), 90f, pts);
        Framed(decor, "Directory", M["lobby_directory"], new Vector3(21.83f, 1.85f, 4.3f), Vector3.left, 2.6f, 1.86f);
        MakePoint("Guide", new Vector3(23.4f, 0, 4.3f), 90f, pts);
        Framed(decor, "VisitorGuide", M["lobby_guide"], new Vector3(24.88f, 1.75f, 4.3f), Vector3.left, 1.5f, 2.0f);
        Text3D("DirectorySign", "Directory", new Vector3(18.75f, 2.7f, 4.95f), Vector3.left, new Vector2(1.4f, 0.3f), new Color(1f, 0.85f, 0.55f), decor, true, 4f);
        Text3D("GuideSign", "How to explore", new Vector3(24.1f, 3.05f, 6.88f), Vector3.forward, new Vector2(1.4f, 0.3f), new Color(1f, 0.85f, 0.55f), decor, true, 4f);

        // lights
        NewLight("Fill_Directional", LightType.Directional, Vector3.zero, lights, 0f, 0.4f).transform.rotation = Quaternion.Euler(60f, -30f, 0f);
        foreach (var x in new[] { 3.3f, 9.45f, 15.6f }) NewLight("Point_Hall", LightType.Point, new Vector3(x, 3.4f, 4.7f), lights, 9f, 1.3f);
        NewLight("Point_Foyer", LightType.Point, new Vector3(22f, 3.4f, 8.1f), lights);
        NewLight("Point_Directory", LightType.Point, new Vector3(20.4f, 3.3f, 4.3f), lights);
        NewLight("Point_Guide", LightType.Point, new Vector3(23.5f, 3.3f, 4.3f), lights);
        Spot(lights, "Spot_Title", new Vector3(21f, 3.8f, 8.6f), new Vector3(21f, 2f, 6.85f), 70f, 3f);
        Spot(lights, "Spot_Timeline", new Vector3(3f, 3.8f, 4.7f), new Vector3(0.1f, 1.9f, 4.7f), 90f, 2.5f);

        BuildPlayer(new Vector3(20.3f, 0f, 8.1f), 270f);     // facing into the Grand Hall; the title wall is to the left
        var panel = BuildInfoPanel(root);
        panel.tour = new[] { welcomeArt };
        panel.nextFallback = hallCentre;      // Next on the welcome panel walks into the Grand Hall

        LobbyChandeliers(decor, lights);
        MuseumLighting.Configure(scene);
        BuildPhoneHUD();
        return SaveWithOcclusion(scene, $"{SceneDir}/Lobby.unity");
    }

    // ------------------------------------------------------------------ WINGS
    static string BuildWing(MuseumJ museum, int wi, ref int items)
    {
        var w = museum.wings[wi];
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupLighting();
        var root = new GameObject("Wing" + (wi + 1) + "_" + w.title.Replace(' ', '_')).transform;
        var arch = Group(root, "Architecture");
        var fin = Group(root, "Wall Finishes");
        var decor = Group(root, "Decor");
        var lights = Group(root, "Lighting");
        var pts = Group(root, "TeleportPoints");
        var exhibits = Group(root, "Exhibits");
        var tour = new List<Artifact>();
        string wallA = "Wall_" + w.id, wallB = "Wall_" + w.id + "_b";
        var gold = new Color(1f, 0.85f, 0.55f);

        NewLight("Fill_Directional", LightType.Directional, Vector3.zero, lights, 0f, 0.35f).transform.rotation = Quaternion.Euler(60f, -30f, 0f);

        // ---- entry hall (x -7..0)
        const float entryX = -7f;
        RoomShell(arch, fin, entryX, 0f, "Hall_Foyer", true, false);
        Wall("EndWall_Entry", V(entryX, -HW - T / 2), V(entryX, HW + T / 2), arch);
        Finish(fin, "Hall_Foyer", V(entryX + 0.1f, -HW), V(entryX + 0.1f, HW), Vector3.right);
        Portal(decor, "Lobby", new Vector3(entryX + 0.12f, 0, 0), Vector3.right, "Lobby", w.id, "Back to the\n<size=70%>Lobby</size>", "Portal_Lobby", new Color(1f, 0.6f, 0.2f));
        var spawn = MakePoint("Entry", new Vector3(-3.5f, 0, -0.6f), 0f, pts);
        Marker("Arrive_Entry", new Vector3(-3.5f, 0, -0.6f), 0f, pts);
        var wingData = Data(w.id, w.id + "_00", $"Wing {wi + 1}: {w.title}", w.subtitle, w.intro, null);
        var titleArt = Framed(exhibits, w.id + "_TitleArt", M[w.id + "_title"], new Vector3(-3.5f, 2.05f, HW - 0.02f), Vector3.back, 4.2f, 2.7f);
        tour.Add(MakeArtifact(titleArt, wingData, spawn, new Vector3(-3.5f, 1.5f, 0.45f), Vector3.back));
        items++;
        Samai(decor, new Vector3(-6.3f, 0, 2.4f)); Samai(decor, new Vector3(-0.8f, 0, 2.4f));
        Nagara(decor, new Vector3(-6.3f, 0, -2.4f));
        Panel("Rangoli", new Vector3(-3.5f, 0.006f, -0.6f), Vector3.up, 1.3f, 1.3f, M["Rangoli"], decor);
        Lantern(decor, new Vector3(-3.5f, 0, 0)); NewLight("Point_Entry", LightType.Point, new Vector3(-3.5f, 3.4f, 0), lights, 9f, 1.3f);
        Spot(lights, "Spot_Title", new Vector3(-3.5f, 3.8f, -1.5f), new Vector3(-3.5f, 2f, HW), 70f, 2.5f);
        var intro = Text3D("WingIntro", $"<b>{w.title}</b>\n<size=55%>{w.intro}</size>", new Vector3(-3.5f, 2.0f, -HW + 0.03f), Vector3.forward, new Vector2(3.6f, 1.6f), new Color(1f, 0.92f, 0.78f), decor, false, 3f);
        Box("WingIntro_Back", new Vector3(-3.5f, 2.0f, -HW + 0.015f), new Vector3(4f, 1.9f, 0.02f), M["Teak_Dark"], decor);

        // ---- section rooms
        float x = 0f;
        for (int si = 0; si < w.sections.Count; si++)
        {
            var s = w.sections[si];
            int n = s.exhibits.Count, perWall = Mathf.CeilToInt(n / 2f);
            float len = 3.2f + perWall * Slot + 1.0f, x0 = x, x1 = x + len;
            var room = Group(exhibits, $"{s.id}_{s.title}");
            string wallMat = si % 2 == 0 ? wallA : wallB;
            string era = $"Section {s.num} · {s.title} · {s.period}";
            var illus = ArtSprite($"Illus/{s.id}.png");

            Partition(arch, fin, x0, si == 0 ? "Hall_Foyer" : (si % 2 == 1 ? wallA : wallB), wallMat);
            SectionSign(decor, x0 - PT / 2 - 0.01f, si == 0 ? $"Wing {wi + 1}: {w.title}" : $"Section {s.num}: {s.title}", si == 0 ? w.id + "_banner" : null, gold);
            RoomShell(arch, fin, x0, x1, wallMat, true, true);

            // section introduction board (left wall) and centrepiece (right side)
            var introTp = MakePoint(s.id + "_00", new Vector3(x0 + 1.6f, 0, 1.0f), 0f, pts);
            var introData = Data(w.id, s.id + "_00", $"Section {s.num}: {s.title}", s.period, s.intro, illus);
            var introBoard = Framed(room, s.id + "_00_IntroBoard", M[s.id + "_intro"], new Vector3(x0 + 1.6f, 1.75f, HW - 0.02f), Vector3.back, 1.5f, 2.0f);
            tour.Add(MakeArtifact(introBoard, introData, introTp, new Vector3(x0 + 1.6f, 1.5f, 1.95f), Vector3.back));
            items++;

            var cpTp = MakePoint(s.id + "_Centrepiece", new Vector3(x0 + 1.6f, 0, -1.0f), 180f, pts);
            Pedestal(room, new Vector3(x0 + 1.6f, 0, -HW + 0.75f), 0.9f);
            var cp = new GameObject(s.id + "_Centrepiece_" + s.centrepiece);
            cp.transform.SetParent(room, false);
            cp.transform.position = new Vector3(x0 + 1.6f, 0.9f, -HW + 0.75f);
            Centrepiece(s.centrepiece, cp.transform);
            if (cp.transform.childCount == 0)   // plaque with the section picture
            {
                var plaque = Panel("Plaque", cp.transform.position + new Vector3(0, 0.3f, 0), Vector3.forward, 0.64f, 0.3f, M[s.id + "_illus"], cp.transform, isStatic: false);
                plaque.transform.rotation = Quaternion.Euler(-15f, 180f, 0f);
            }
            MakeArtifact(cp, introData, cpTp, new Vector3(x0 + 0.75f, 1.5f, -1.85f), Vector3.forward, 20f);
            Spot(lights, s.id + "_Spot_Centrepiece", new Vector3(x0 + 1.6f, 3.8f, -1.0f), new Vector3(x0 + 1.6f, 1.0f, -HW + 0.75f), 40f, 3f);

            // exhibit boards: left (+z) and right (-z) walls, alternating, so the tour zigzags down the room
            for (int k = 0; k < n; k++)
            {
                var e = s.exhibits[k];
                int side = k % 2 == 0 ? 1 : -1, slot = k / 2;
                float bx = x0 + 3.2f + (slot + 0.5f) * Slot;
                var normal = new Vector3(0, 0, -side);
                var tp = MakePoint(e.id, new Vector3(bx, 0, side * 1.0f), side > 0 ? 0f : 180f, pts);
                var data = Data(w.id, e.id, e.title, era, e.text, illus);
                var board = Framed(room, e.id, M[e.id], new Vector3(bx, 1.65f, side * (HW - 0.02f)), normal, BoardW, BoardH);
                tour.Add(MakeArtifact(board, data, tp, new Vector3(bx, 1.5f, side * 1.95f), normal));
                items++;
            }

            // ceiling, lanterns, lights, runner
            for (float bx = x0 + 2f; bx < x1 - 0.5f; bx += 2f) Box("Beam", new Vector3(bx, H - 0.1f, 0), new Vector3(0.18f, 0.2f, 2 * HW), M["Teak_Dark"], arch);
            int nl = Mathf.Max(1, Mathf.RoundToInt(len / 9f));
            for (int l = 0; l < nl; l++)
            {
                float lx = x0 + len * (l + 0.5f) / nl;
                Lantern(decor, new Vector3(lx, 0, 0));
                NewLight($"{s.id}_Point_{l}", LightType.Point, new Vector3(lx, 3.4f, 0), lights, Mathf.Max(8f, len / nl + 3f), 1.35f);
            }
            if (len > 5f) Panel("Dhurrie_Runner", new Vector3((x0 + 3f + x1) / 2, 0.004f, 0), Vector3.up, x1 - x0 - 3.4f, 0.9f, M["Dhurrie"], decor, (x1 - x0 - 3.4f) / 1.9f);
            x = x1;
        }

        // ---- exit hall with gateways onward
        float xe = x, xEnd = x + 7f;
        Partition(arch, fin, xe, w.sections.Count % 2 == 1 ? wallA : wallB, "Hall_Foyer");
        SectionSign(decor, xe - PT / 2 - 0.01f, "Onward", null, gold);
        RoomShell(arch, fin, xe, xEnd, "Hall_Foyer", true, true);
        Wall("EndWall_Exit", V(xEnd, -HW - T / 2), V(xEnd, HW + T / 2), arch);
        Finish(fin, "Hall_Foyer", V(xEnd - 0.1f, -HW), V(xEnd - 0.1f, HW), Vector3.left);
        bool last = wi == museum.wings.Count - 1;
        var nextW = museum.wings[last ? 0 : wi + 1];
        int nextIndex = last ? 1 : wi + 2;
        Portal(decor, "Next", new Vector3(xEnd - 0.12f, 0, -1.5f), Vector3.left, "Wing" + nextIndex, "Entry",
            (last ? "Start again\n" : "Next: ") + $"<size=70%>Wing {nextIndex}\n{nextW.title}</size>", "Portal_" + nextW.id, Hex(nextW.color) * 1.6f);
        Portal(decor, "Lobby", new Vector3(xEnd - 0.12f, 0, 1.5f), Vector3.left, "Lobby", w.id, "Back to the\n<size=70%>Lobby</size>", "Portal_Lobby", new Color(1f, 0.6f, 0.2f));
        MakePoint("Exit", new Vector3(xe + 3.2f, 0, 0), 90f, pts);
        Lantern(decor, new Vector3(xe + 3.5f, 0, 0));
        NewLight("Point_Exit", LightType.Point, new Vector3(xe + 3.5f, 3.4f, 0), lights, 9f, 1.3f);

        BuildPlayer(spawn.transform.position, 0f);
        var panel = BuildInfoPanel(root);
        panel.tour = tour.ToArray();

        BuildPhoneHUD();
        return SaveWithOcclusion(scene, $"{SceneDir}/Wing{wi + 1}.unity");
    }

    /// Floor, ceiling and the two long walls of a room spanning x0..x1 (inner width 2*HW).
    static void RoomShell(Transform arch, Transform fin, float x0, float x1, string wallMat, bool left, bool right)
    {
        float len = x1 - x0, mid = (x0 + x1) / 2;
        Box("Floor", new Vector3(mid, -0.05f, 0), new Vector3(len, 0.1f, 2 * HW + 2 * T), M["Floor_Kota"], arch);
        Box("Ceiling", new Vector3(mid, H + 0.05f, 0), new Vector3(len, 0.1f, 2 * HW + 2 * T), M["Ceiling_Plaster"], arch);
        Wall("Wall_L", V(x0, HW + T / 2), V(x1, HW + T / 2), arch);
        Wall("Wall_R", V(x0, -HW - T / 2), V(x1, -HW - T / 2), arch);
        float a = x0 + PT / 2, b = x1 - PT / 2;
        Finish(fin, wallMat, V(a, HW), V(b, HW), Vector3.back);
        Finish(fin, wallMat, V(a, -HW), V(b, -HW), Vector3.forward);
    }

    /// Cross wall at x with a cusped-arch opening (z -1.2..1.2) between two rooms.
    static void Partition(Transform parent, Transform fin, float x, string westMat, string eastMat)
    {
        var g = Group(parent, $"Arch_{x:0.#}");
        const float z0 = -1.2f, z1 = 1.2f, spring = 2.4f, apex = 3.15f;
        Box("Wall_S", new Vector3(x, H / 2, (-HW + z0) / 2), new Vector3(PT, H, z0 + HW), M["Wall_Basalt"], g);
        Box("Wall_N", new Vector3(x, H / 2, (z1 + HW) / 2), new Vector3(PT, H, HW - z1), M["Wall_Basalt"], g);
        const int n = 14;
        Vector2 Curve(float t) => new Vector2(Mathf.Lerp(z0, z1, t), spring + (apex - spring) * (1f - Mathf.Pow(Mathf.Abs(1f - 2f * t), 1.6f)));
        for (int i = 0; i < n; i++)
        {
            var a = Curve(i / (float)n); var b = Curve((i + 1) / (float)n);
            var mid = (a + b) / 2; var tan = new Vector3(0, b.y - a.y, b.x - a.x);
            var ringBox = Box($"ArchRing_{i}", new Vector3(x, mid.y, mid.x), new Vector3(PT + 0.06f, 0.16f, tan.magnitude + 0.03f), M["Teak"], g);
            ringBox.transform.localRotation = Quaternion.LookRotation(tan.normalized, Vector3.up);
            float h = H - (mid.y + 0.06f);
            Box($"Spandrel_{i}", new Vector3(x, mid.y + 0.06f + h / 2, mid.x), new Vector3(PT, h, tan.magnitude + 0.02f), M["Plaster_Cream"], g);
        }
        foreach (var t in new[] { 0.14f, 0.3f, 0.7f, 0.86f })
        {
            var c = Curve(t);
            P(PrimitiveType.Sphere, g, new Vector3(x, c.y - 0.1f, c.x), new Vector3(PT + 0.04f, 0.16f, 0.2f), "Teak");
        }
        foreach (var z in new[] { z0 - 0.05f, z1 + 0.05f }) Pillar(g, new Vector3(x, 0f, z));
        foreach (var (za, zb) in new[] { (-HW, z0 - 0.3f), (z1 + 0.3f, HW) })
        {
            Finish(fin, westMat, V(x - PT / 2, za), V(x - PT / 2, zb), Vector3.left);
            Finish(fin, eastMat, V(x + PT / 2, za), V(x + PT / 2, zb), Vector3.right);
        }
    }

    /// Sign above an arch, facing visitors who approach from the west (-x).
    static void SectionSign(Transform p, float x, string text, string bannerMat, Color c)
    {
        if (bannerMat != null)
        {
            Panel("Banner", new Vector3(x, 3.5f, 0), Vector3.left, 2.6f, 2.6f * 360f / 1600f, M[bannerMat], p);
            return;
        }
        Box("Sign_Back", new Vector3(x, 3.5f, 0), new Vector3(0.02f, 0.5f, 2.8f), M["Teak_Dark"], p);
        Text3D("Sign", text, new Vector3(x - 0.012f, 3.5f, 0), Vector3.left, new Vector2(2.6f, 0.42f), c, p, true, 4f);
    }

    // ------------------------------------------------------------------ mobile: save + occlusion, HUD, menu, settings
    static string SaveWithOcclusion(UnityEngine.SceneManagement.Scene scene, string path)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, path)) throw new Exception("Could not save " + path);
        // Bake occlusion culling: rooms hidden behind walls and arches are not drawn (big win in the long wings)
        StaticOcclusionCulling.smallestOccluder = 1.5f;
        StaticOcclusionCulling.smallestHole = 0.25f;
        StaticOcclusionCulling.backfaceThreshold = 100f;
        StaticOcclusionCulling.Compute();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>()) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.transform.SetAsLastSibling();
    }

    static Canvas ScreenCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
        return rt;
    }

    static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color c, FontStyles style, TextAlignmentOptions align,
                                 Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var t = Rect(name, parent, aMin, aMax, oMin, oMax).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = text; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    /// Phone-mode overlay in every museum scene: Menu button + a hint (hidden while Cardboard is running).
    static void BuildPhoneHUD()
    {
        EnsureEventSystem();
        var hud = new GameObject("PhoneHUD").AddComponent<PhoneHUD>();
        var canvas = ScreenCanvas("HUDCanvas", 10);
        canvas.transform.SetParent(hud.transform, false);
        var safe = Rect("SafeArea", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        safe.gameObject.AddComponent<SafeArea>();

        var btn = Rect("MenuButton", safe, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -132), new Vector2(276, -36));
        var img = btn.gameObject.AddComponent<Image>(); img.color = new Color(0.1f, 0.05f, 0.03f, 0.75f);
        var b = btn.gameObject.AddComponent<Button>(); b.targetGraphic = img;
        UnityEventTools.AddPersistentListener(b.onClick, hud.GoMenu);
        Label(btn, "Label", "< Menu", 40, new Color(1f, 0.85f, 0.55f), FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var hint = Rect("Hint", safe, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-640, 36), new Vector2(640, 116));
        hint.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.05f, 0.03f, 0.7f);
        hint.GetComponent<Image>().raycastTarget = false;
        Label(hint, "Text", "Drag to look around  ·  Tap a glowing circle to walk  ·  Tap a board or archway to open it", 30, Color.white, FontStyles.Normal,
              TextAlignmentOptions.Center, Vector2.zero, Vector2.one, new Vector2(20, 0), new Vector2(-20, 0));
        hud.root = canvas.gameObject; hud.hint = hint.gameObject;
    }

    /// 2D start screen for phones: viewer mode + a card per scene.
    static string BuildMenu(MuseumJ museum)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupLighting();
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.05f, 0.04f); cam.cullingMask = 0;
        EnsureEventSystem();

        var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
        var canvas = ScreenCanvas("MenuCanvas", 0);
        var ct = canvas.transform;
        var bg = Rect("Background", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
        bg.texture = Art("Lobby/lobby_title.png"); bg.color = new Color(0.55f, 0.5f, 0.48f); bg.raycastTarget = false;
        bg.uvRect = new UnityEngine.Rect(0f, 0.12f, 1f, 0.76f);
        var shade = Rect("Shade", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        shade.color = new Color(0.1f, 0.04f, 0.03f, 0.84f); shade.raycastTarget = false;

        var safe = Rect("SafeArea", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        safe.gameObject.AddComponent<SafeArea>();
        var gold = new Color(1f, 0.82f, 0.5f);
        int nEx = museum.wings.Sum(w => w.sections.Sum(s => s.exhibits.Count)), nSec = museum.wings.Sum(w => w.sections.Count);
        Label(safe, "Title", museum.title, 76, gold, FontStyles.Bold, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(0.62f, 1), new Vector2(80, -150), new Vector2(0, -50));
        Label(safe, "Subtitle", $"From the first stone tools to the present day  ·  {museum.wings.Count} wings  ·  {nSec} sections  ·  {nEx} exhibits",
              30, new Color(1f, 0.92f, 0.8f, 0.85f), FontStyles.Normal, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(0.62f, 1), new Vector2(80, -210), new Vector2(0, -150));

        // viewer mode pills (top right)
        Label(safe, "ModeLabel", "VIEW WITH", 24, new Color(1f, 1f, 1f, 0.6f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-660, -100), new Vector2(-80, -64));
        (Image, TMP_Text) Pill(string name, string text, float x0, bool vr)
        {
            var r = Rect(name, safe, new Vector2(1, 1), new Vector2(1, 1), new Vector2(x0, -196), new Vector2(x0 + 286, -110));
            var im = r.gameObject.AddComponent<Image>();
            var bt = r.gameObject.AddComponent<Button>(); bt.targetGraphic = im;
            UnityEventTools.AddBoolPersistentListener(bt.onClick, menu.SetVR, vr);
            var l = Label(r, "Label", text, 30, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return (im, l);
        }
        (menu.vrPill, menu.vrLabel) = Pill("Pill_Cardboard", "Cardboard viewer", -666, true);
        (menu.phonePill, menu.phoneLabel) = Pill("Pill_Phone", "Phone only", -366, false);

        // horizontally scrolling cards
        var view = Rect("Cards", safe, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 200), new Vector2(0, -250));
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        view.gameObject.AddComponent<RectMask2D>();
        view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);       // catches drags between cards
        var content = Rect("Content", view, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        content.pivot = new Vector2(0, 0.5f);
        var hl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(80, 80, 10, 10); hl.spacing = 36; hl.childControlHeight = true; hl.childControlWidth = true;
        hl.childForceExpandHeight = true; hl.childForceExpandWidth = false;
        content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = view; scroll.horizontal = true; scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Elastic; scroll.decelerationRate = 0.12f;

        void Card(string sceneName, Texture art, Color col, string kicker, string title, string sub, string stats)
        {
            var c = Rect("Card_" + sceneName, content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            c.gameObject.AddComponent<LayoutElement>().preferredWidth = 440;
            var im = c.gameObject.AddComponent<Image>(); im.color = new Color(0.16f, 0.08f, 0.05f, 0.96f);
            var bt = c.gameObject.AddComponent<Button>(); bt.targetGraphic = im;
            var colors = bt.colors; colors.highlightedColor = new Color(1f, 0.92f, 0.8f); colors.pressedColor = new Color(1f, 0.7f, 0.4f); bt.colors = colors;
            UnityEventTools.AddStringPersistentListener(bt.onClick, menu.Enter, sceneName);
            var pic = Rect("Art", c, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -283), Vector2.zero).gameObject.AddComponent<RawImage>();
            pic.texture = art; pic.raycastTarget = false;
            var bar = Rect("Bar", c, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -293), new Vector2(0, -283)).gameObject.AddComponent<Image>();
            bar.color = col; bar.raycastTarget = false;
            Label(c, "Kicker", kicker, 24, Color.Lerp(col, Color.white, 0.55f), FontStyles.Bold, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -345), new Vector2(-28, -310));
            Label(c, "Title", title, 40, gold, FontStyles.Bold, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -450), new Vector2(-28, -345));
            Label(c, "Subtitle", sub, 26, new Color(1f, 0.92f, 0.82f, 0.9f), FontStyles.Normal, TextAlignmentOptions.TopLeft, new Vector2(0, 0), new Vector2(1, 1), new Vector2(28, 60), new Vector2(-28, -455));
            Label(c, "Stats", stats, 24, new Color(1f, 1f, 1f, 0.6f), FontStyles.Normal, TextAlignmentOptions.BottomLeft, new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 18), new Vector2(-28, 56));
        }
        Card("Lobby", Art("Lobby/lobby_title.png"), new Color(1f, 0.53f, 0.08f), "START HERE", "Grand Hall", "Welcome, directory, visitor guide and a timeline of India. Archways lead to every wing.", "Lobby");
        for (int i = 0; i < museum.wings.Count; i++)
        {
            var w = museum.wings[i];
            int ex = w.sections.Sum(s => s.exhibits.Count);
            Card("Wing" + (i + 1), Art($"Wings/{w.id}_title.png"), Hex(w.color), $"WING {i + 1}", w.title, w.subtitle,
                 $"Sections {w.sections.First().num}–{w.sections.Last().num}  ·  {ex} exhibits");
        }

        menu.modeHint = Label(safe, "ModeHint", "", 27, new Color(1f, 1f, 1f, 0.75f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft,
                              new Vector2(0, 0), new Vector2(1, 0), new Vector2(80, 40), new Vector2(-80, 180));

        var path = $"{SceneDir}/{MuseumApp.MenuScene}.unity";
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, path)) throw new Exception("Could not save " + path);
        return path;
    }

    /// Phone / iOS / Android settings for a Cardboard app that starts in a 2D menu.
    static void ApplyMobileSettings(StringBuilder report)
    {
        // XR must not start with the app: the menu is 2D; MuseumApp starts Cardboard when VR mode is chosen
        if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) && perTarget)
        {
            foreach (var g in new[] { BuildTargetGroup.iOS, BuildTargetGroup.Android })
            {
                var st = perTarget.SettingsForBuildTarget(g);
                if (st != null && st.InitManagerOnStart) { st.InitManagerOnStart = false; EditorUtility.SetDirty(st); }
            }
            EditorUtility.SetDirty(perTarget);
        }
        else report.AppendLine("Warning: XR settings not found (Cardboard may start before the menu)");

        const string bundle = "com.jaykumarpatel.indianhistorymuseumvr";
        PlayerSettings.productName = "Museum of Indian History VR";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, bundle);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, bundle);
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.statusBarHidden = true;
        PlayerSettings.iOS.requiresFullScreen = true;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        if (string.IsNullOrEmpty(PlayerSettings.iOS.cameraUsageDescription))
            PlayerSettings.iOS.cameraUsageDescription = "The camera is used to scan the QR code on your Cardboard viewer.";
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Low);
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
        PlayerSettings.mipStripping = true;
        PlayerSettings.stripUnusedMeshComponents = true;

        // URP: no shadows at all (none of the lights cast them) -> fewer shader variants and passes
        var urp = GraphicsSettings.defaultRenderPipeline;
        if (urp)
        {
            var so = new SerializedObject(urp);
            void SetB(string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }
            void SetI(string n, int v) { var p = so.FindProperty(n); if (p != null) p.intValue = v; }
            SetB("m_MainLightShadowsSupported", false);
            SetB("m_AdditionalLightShadowsSupported", false);
            SetB("m_SupportsHDR", false);
            SetB("m_RequireDepthTexture", false);
            SetB("m_RequireOpaqueTexture", false);
            SetI("m_AdditionalLightsPerObjectLimit", 4);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        report.AppendLine($"Mobile: bundle {bundle}, iOS 15+, landscape, XR starts from the menu, URP shadows off, occlusion culling baked");
    }

    // ------------------------------------------------------------------ lobby upgrade (Indo-Saracenic stone, real materials, chandeliers)
    static void UpgradeLobbyMaterials()
    {
        // the lobby keeps its floor plan; its finishes switch to the Poly Haven materials (fall back to colours)
        M["Hall_Lobby"] = Pbr("Lobby_Wall", "stone_castle", 2.6f, Color.white, 0.15f, new Color(0.48f, 0.11f, 0.13f));
        M["Hall_Foyer"] = Pbr("Lobby_Foyer", "stone_buff", 2.4f, Color.white, 0.15f, new Color(0.84f, 0.66f, 0.40f));
        M["Hall_Guide"] = Pbr("Lobby_Guide", "stone_red", 2.2f, Color.white, 0.15f, new Color(0.13f, 0.37f, 0.41f));
        M["Hall_Directory"] = Pbr("Lobby_Directory", "stone_red", 2.2f, Color.white, 0.15f, new Color(0.19f, 0.33f, 0.24f));
        M["Floor_Kota"] = Pbr("Lobby_Floor", "floor_marble", 2.0f, Color.white, 0.6f, new Color(0.5f, 0.5f, 0.47f));
        M["Ceiling_Plaster"] = Pbr("Lobby_Ceiling", "plaster", 3f, new Color(1f, 0.96f, 0.9f), 0.1f, new Color(0.86f, 0.8f, 0.68f));
        M["Wall_Basalt"] = Pbr("Lobby_Basalt", "stone_castle", 2.6f, new Color(0.9f, 0.88f, 0.85f), 0.15f, new Color(0.36f, 0.32f, 0.29f));
        M["Dado"] = Pbr("Lobby_Dado", "stone_red", 1.6f, Color.white, 0.2f, new Color(0.5f, 0.3f, 0.25f));
        M["Plaster_Cream"] = Pbr("Lobby_Upper", "plaster", 3f, new Color(1f, 0.96f, 0.9f), 0.1f, new Color(0.9f, 0.84f, 0.7f));
    }

    static void LobbyChandeliers(Transform decor, Transform lights)
    {
        var s = Styles["W4"];
        var ph = "Assets/_Museum/PolyHaven/Models/Chandelier_01/Chandelier_01.fbx";
        foreach (var x in new[] { 3.3f, 9.45f, 15.6f })
        {
            var at = new Vector3(x, 2.7f, 4.725f);
            if (File.Exists(ph)) PlaceModel(ph, decor, at, new Vector3(1.6f, 1.2f, 1.6f), 0);
            else Chandelier(decor, s, at + Vector3.up * 0.3f, H - 0.05f);
            NewLight("Chandelier", LightType.Point, at + Vector3.up * 0.2f, lights, 9f, 2.0f);
        }
        Chandelier(decor, s, new Vector3(21f, 2.9f, 8.1f), H - 0.05f);
    }
}
