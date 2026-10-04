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
using UnityEngine.Rendering;
using UnityEngine.UI;

/// Generates Assets/_Museum/Scenes/Museum.unity (v2: themed halls, Indian / Maratha design).
///
/// Layout (1 unit = 1 m, origin = gallery south-west corner, +X east, +Z north)
///   Foyer (entrance, title wall)  x 18.9-25, z 6.75-9.45
///   Forts & Navy room             x 21.95-25, z 1.85-6.75
///   Mavlas' Armoury               x 18.9-21.95, z 1.85-6.75
///   Gallery 18.9 x 9.45 m, split by two cusped-arch partitions into three chronological halls:
///     Hall 1 Swarajya Begins (x 12.6-18.9) -> Hall 2 Forts & Campaigns (x 6.3-12.6) -> Hall 3 Rajyabhishek & the Raj (x 0-6.3)
///   Storage, restrooms, hall, utility and AC are not built.
///
/// Design language (inspired by multi-hall history museums): themed wall colours per hall with a basalt dado and a
/// Paithani-border frieze, wall murals, illustrated story boards on partitions, spot-lit pedestals, centrepieces
/// (cannon, throne under a canopy), teak coffered ceiling with brass lanterns, dhurrie runner, rangoli, toran,
/// samai lamps, nagara drums and bhagwa flags. All art is generated (Assets/_Museum/Art); models are primitives.
[InitializeOnLoad]
public static class MuseumSceneBuilder
{
    const string StateFile = "ProjectSettings/MuseumSceneState_v2.txt";
    const string ReportFile = "ProjectSettings/MuseumSceneReport.txt";
    const string ScenePath = "Assets/_Museum/Scenes/Museum.unity";
    const string MatDir = "Assets/_Museum/Materials";
    const string TexDir = "Assets/_Museum/Textures";
    const string ArtDir = "Assets/_Museum/Art";
    const string MeshDir = "Assets/_Museum/Generated";
    const string DataDir = "Assets/_Museum/Data";
    const float H = 3.96f, T = 0.2f, PT = 0.3f, DoorH = 2.4f;

    static int interactiveLayer, teleportLayer;
    static readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
    static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
    static double waitStart;

    // ------------------------------------------------------------------ entry points
    static MuseumSceneBuilder() => EditorApplication.delayCall += () =>
    {
        if (File.Exists(StateFile)) return;
        waitStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += WaitForEditor;
    };

    static void WaitForEditor()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup - waitStart < 2) return;
        EditorApplication.update -= WaitForEditor;
        if (!File.Exists(StateFile)) Build();
    }

    [MenuItem("Museum/Rebuild Museum Scene (overwrites Museum.unity)")]
    static void Rebuild()
    {
        if (EditorUtility.DisplayDialog("Rebuild museum scene",
            "Regenerates Museum.unity. Changes made inside that scene are lost.\nArtifact data (text, audio) in Assets/_Museum/Data is kept.", "Rebuild", "Cancel"))
            Build();
    }

    // ------------------------------------------------------------------ build
    static void Build()
    {
        interactiveLayer = LayerMask.NameToLayer("Interactive");
        teleportLayer = LayerMask.NameToLayer("Teleport");
        if (interactiveLayer < 0 || teleportLayer < 0) { Debug.LogError("[Museum] Layers 'Interactive' and 'Teleport' are missing."); return; }
        if (!AssetDatabase.IsValidFolder(ArtDir)) { Debug.LogError("[Museum] Missing " + ArtDir); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        foreach (var d in new[] { MatDir, TexDir, MeshDir, DataDir, "Assets/_Museum/Scenes" }) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        M.Clear(); Meshes.Clear();

        var report = new StringBuilder();
        try
        {
            ImportArt();
            CreateMaterials();
            var ring = MakeCircleSprite("Reticle", 0.62f);
            var disc = MakeCircleSprite("TeleportDisc", 0f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Museum").transform;

            BuildArchitecture(Group(root, "Architecture"));
            BuildFinishes(Group(root, "Wall Finishes"));
            BuildCeiling(Group(root, "Ceiling"));
            BuildDecor(Group(root, "Decor"));
            BuildLighting(Group(root, "Lighting"));
            var points = BuildTeleportPoints(Group(root, "TeleportPoints"), disc);
            BuildPlayer(points["Title"], ring);
            var panel = BuildInfoPanel(root);
            var artifacts = BuildArtifacts(Group(root, "Exhibits"), points);

            points["Exit"].destination = points["Title"].transform;
            panel.tour = TourOrder.Where(artifacts.ContainsKey).Select(id => artifacts[id]).ToArray();
            panel.transform.position = new Vector3(21f, -5f, 8f);    // parked out of sight until an exhibit is selected

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("Could not save " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            File.WriteAllText(StateFile, "done");

            report.AppendLine("RESULT: BUILT " + ScenePath + " (v2 themed halls)");
            report.AppendLine($"Exhibits: {artifacts.Count}  Teleport points: {points.Count}  Tour stops: {panel.tour.Length}");
            report.AppendLine("Halls: Foyer · Forts & Navy · Hall 1 Swarajya Begins · Mavlas' Armoury · Hall 2 Forts & Campaigns · Hall 3 Rajyabhishek & the Raj");
            report.AppendLine("Not built (per brief): Storage, Restroom x2, Hall, Utility, AC");
        }
        catch (Exception e)
        {
            report.Insert(0, "RESULT: ERROR " + e.Message + "\n");
            Debug.LogException(e);
        }
        File.WriteAllText(ReportFile, report.ToString());
        Debug.Log("[Museum] Scene build\n" + report);
        EditorUtility.DisplayDialog("Shivaji Museum VR — scene", report.ToString(), "OK");
        if (File.Exists(StateFile)) EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Museum/Run Marathi Setup Again");
    }

    // ------------------------------------------------------------------ art import & materials
    static void ImportArt()
    {
        foreach (var path in Directory.GetFiles(ArtDir, "*.png").Select(p => p.Replace('\\', '/')))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) continue;
            var file = Path.GetFileName(path);
            bool sprite = file.StartsWith("illus_");
            bool tile = file.StartsWith("tex_") && file != "tex_rangoli.png";
            var wantType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            var wantWrap = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (ti.textureType == wantType && ti.wrapMode == wantWrap && ti.maxTextureSize == 2048) continue;
            ti.textureType = wantType;
            ti.wrapMode = wantWrap;
            ti.mipmapEnabled = !sprite;            // mipmaps stop shimmering on distant walls in VR
            ti.alphaIsTransparency = file == "tex_rangoli.png" || sprite;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = tile ? 4 : 2;
            ti.SaveAndReimport();
        }
    }

    static Texture2D Art(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{file}");

    static void CreateMaterials()
    {
        // architecture
        Lit("Wall_Basalt", new Color(0.36f, 0.32f, 0.29f));
        Lit("Floor_Kota", new Color(0.50f, 0.50f, 0.47f), 0f, 0.5f);
        Lit("Ceiling_Plaster", new Color(0.86f, 0.80f, 0.68f));
        Lit("Plaster_Cream", new Color(0.90f, 0.84f, 0.70f));
        Lit("Teak", new Color(0.38f, 0.22f, 0.11f), 0f, 0.35f);
        Lit("Teak_Dark", new Color(0.22f, 0.12f, 0.06f), 0f, 0.4f);
        Lit("Door_Wood", new Color(0.30f, 0.17f, 0.08f), 0f, 0.3f);
        Lit("Saffron", new Color(1f, 0.53f, 0.08f));
        Lit("Marigold", new Color(1f, 0.62f, 0.05f));
        Lit("Leaf_Green", new Color(0.18f, 0.45f, 0.16f));
        Lit("Red_Cloth", new Color(0.55f, 0.06f, 0.08f));
        Lit("Stone_Plinth", new Color(0.78f, 0.74f, 0.67f));
        Lit("Basalt_Dark", new Color(0.18f, 0.17f, 0.16f));
        // hall colours
        Lit("Hall_Foyer", new Color(0.84f, 0.66f, 0.40f));
        Lit("Hall_1", new Color(0.20f, 0.27f, 0.48f));
        Lit("Hall_2", new Color(0.62f, 0.30f, 0.19f));
        Lit("Hall_3", new Color(0.48f, 0.11f, 0.13f));
        Lit("Hall_Armour", new Color(0.19f, 0.33f, 0.24f));
        Lit("Hall_Forts", new Color(0.13f, 0.37f, 0.41f));
        // metals & objects
        Lit("Gold", new Color(0.95f, 0.74f, 0.30f), 1f, 0.65f);
        Lit("Brass", new Color(0.82f, 0.62f, 0.26f), 1f, 0.55f);
        Lit("Steel", new Color(0.62f, 0.64f, 0.67f), 0.9f, 0.6f);
        Lit("Steel_Dark", new Color(0.35f, 0.36f, 0.38f), 0.9f, 0.45f);
        Lit("Bronze", new Color(0.55f, 0.37f, 0.20f), 0.85f, 0.5f);
        Lit("Gunmetal", new Color(0.24f, 0.22f, 0.20f), 0.9f, 0.45f);
        Lit("Copper", new Color(0.72f, 0.42f, 0.25f), 0.85f, 0.45f);
        Lit("Hide", new Color(0.42f, 0.27f, 0.15f));
        Lit("Hide_Light", new Color(0.86f, 0.76f, 0.58f));
        Lit("Fort_Stone", new Color(0.50f, 0.46f, 0.40f));
        Lit("Hill_Green", new Color(0.33f, 0.40f, 0.24f));
        Lit("Sea_Blue", new Color(0.18f, 0.38f, 0.52f), 0f, 0.7f);
        Lit("Sail_Cloth", new Color(0.92f, 0.87f, 0.76f));
        Emissive("Lantern_Glow", new Color(1f, 0.62f, 0.25f), 2.2f);
        Emissive("Flame", new Color(1f, 0.55f, 0.1f), 3f);
        var flagMat = Lit("Bhagwa_Flag", new Color(1f, 0.45f, 0.05f));
        if (flagMat.HasProperty("_Cull")) flagMat.SetFloat("_Cull", 0f);
        flagMat.doubleSidedGI = true;

        // textured
        TexMat("Dado", "tex_dado.png");
        TexMat("Frieze", "tex_frieze.png");
        TexMat("Dhurrie", "tex_dhurrie.png");
        TexMat("Rangoli", "tex_rangoli.png", alphaClip: true);
        foreach (var f in Directory.GetFiles(ArtDir, "*.png").Select(Path.GetFileName))
            if (f.StartsWith("board_") || f.StartsWith("mural_") || f.StartsWith("painting_") || f.StartsWith("banner_"))
                TexMat(Path.GetFileNameWithoutExtension(f), f, smooth: 0.08f);
    }

    static Material GetOrCreate(string name, Shader sh)
    {
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }

    static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

    static Material Lit(string name, Color c, float metallic = 0f, float smooth = 0.2f)
    {
        var m = GetOrCreate(name, LitShader);
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

    static Material TexMat(string name, string file, float smooth = 0.15f, bool alphaClip = false)
    {
        var m = Lit(name, Color.white, 0f, smooth);
        var tex = Art(file);
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
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

    static Transform P(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, string mat, Vector3 euler = default, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        if (!keepCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = M[mat];
        return go.transform;
    }

    /// Unit quad mesh (faces -Z like Unity's Quad) with tiled UVs, saved as an asset and cached.
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
        return Meshes[key] = mesh;
    }

    /// A flat textured panel. 'normal' points toward the viewer.
    static GameObject Panel(string name, Vector3 center, Vector3 normal, float w, float h, Material m, Transform parent, float uRep = 1f, float vRep = 1f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var up = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
        go.transform.rotation = Quaternion.LookRotation(-normal, up);
        go.transform.localScale = new Vector3(w, h, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh(Mathf.Round(uRep * 4f) / 4f, vRep);
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        MakeStatic(go);
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
        Wall(name + "_Lintel", a + dir * g0, a + dir * g1, parent, null, DoorH, H);
    }

    static TextMeshPro Text3D(string name, string text, Vector3 pos, Vector3 face, Vector2 size, Color c, Transform parent, bool bold = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(-face);
        var t = go.AddComponent<TextMeshPro>();
        t.text = text; t.color = c; t.alignment = TextAlignmentOptions.Center;
        t.rectTransform.sizeDelta = size;
        t.enableAutoSizing = true; t.fontSizeMin = 0.05f; t.fontSizeMax = 30f;
        if (bold) t.fontStyle = FontStyles.Bold;
        return t;
    }

    static Vector2 V(float x, float z) => new Vector2(x, z);

    // ------------------------------------------------------------------ architecture
    static void BuildArchitecture(Transform p)
    {
        Box("Floor", new Vector3(12.5f, -0.05f, 4.725f), new Vector3(25f, 0.1f, 9.45f), M["Floor_Kota"], p);
        Box("Ceiling", new Vector3(12.5f, H + 0.05f, 4.725f), new Vector3(25f, 0.1f, 9.45f), M["Ceiling_Plaster"], p);

        Wall("Wall_South", V(0, 0), V(18.9f, 0), p);
        Wall("Wall_West", V(0, 0), V(0, 9.45f), p);
        Wall("Wall_North", V(0, 9.45f), V(25f, 9.45f), p);
        Wall("Wall_East", V(25f, 1.85f), V(25f, 9.45f), p);
        Wall("Wall_GalleryEast", V(18.9f, 0), V(18.9f, 1.85f), p);         // closes the removed storage/restroom side

        WallWithDoor("Wall_ArmouryWest", V(18.9f, 1.85f), V(18.9f, 6.75f), 2.3f, 3.9f, p);   // door z 4.15-5.75
        Wall("Wall_RoomsSouth", V(18.9f, 1.85f), V(25f, 1.85f), p);
        Wall("Wall_RoomsPartition", V(21.95f, 1.85f), V(21.95f, 6.75f), p);
        WallWithDoor("TitleWall", V(18.9f, 6.75f), V(25f, 6.75f), 4.4f, 6.0f, p);             // Forts room door x 23.3-24.9

        Wall("AngledDisplayWall", V(15.5f, 7.0f), V(18.1f, 4.4f), p, M["Teak"], 0f, 3.0f);
        Box("AngledWall_Cap", new Vector3(16.8f, 3.03f, 5.7f), new Vector3(0.32f, 0.06f, 3.9f), M["Brass"], p, Mathf.Atan2(2.6f, -2.6f) * Mathf.Rad2Deg);

        // Moving Wall A (8' x 6') becomes the throne backdrop; Moving Wall B (8' x 8') the island dais
        Box("MovingWall_A_ThroneBackdrop", new Vector3(0.35f, 1.22f, 4.4f), new Vector3(0.2f, 2.44f, 2.0f), M["Teak_Dark"], p);
        Box("MovingWall_B_Dais", new Vector3(9.45f, 0.1f, 2.1f), new Vector3(2.44f, 0.2f, 2.44f), M["Basalt_Dark"], p);
        Box("Dais_Trim", new Vector3(9.45f, 0.205f, 2.1f), new Vector3(2.5f, 0.02f, 2.5f), M["Brass"], p);

        Partition(p, 12.6f, "Arch_Hall1_Hall2");
        Partition(p, 6.3f, "Arch_Hall2_Hall3");
    }

    /// Partition wall with a cusped (multifoil) arch gateway, carved wada pillars and a solid spandrel to the ceiling.
    static void Partition(Transform parent, float x, string name)
    {
        var g = Group(parent, name);
        const float z0 = 2.6f, z1 = 6.85f, spring = 2.35f, apex = 3.2f;
        Box("Wall_S", new Vector3(x, H / 2, z0 / 2), new Vector3(PT, H, z0), M["Wall_Basalt"], g);
        Box("Wall_N", new Vector3(x, H / 2, (z1 + 9.45f) / 2), new Vector3(PT, H, 9.45f - z1), M["Wall_Basalt"], g);

        const int n = 18;
        Vector2 Curve(float t) => new Vector2(Mathf.Lerp(z0, z1, t), spring + (apex - spring) * (1f - Mathf.Pow(Mathf.Abs(1f - 2f * t), 1.6f)));
        for (int i = 0; i < n; i++)
        {
            var a = Curve(i / (float)n); var b = Curve((i + 1) / (float)n);
            var mid = (a + b) / 2; var tan = new Vector3(0, b.y - a.y, b.x - a.x);
            var ring = Box($"ArchRing_{i}", new Vector3(x, mid.y, mid.x), new Vector3(PT + 0.06f, 0.16f, tan.magnitude + 0.03f), M["Teak"], g);
            ring.transform.localRotation = Quaternion.LookRotation(tan.normalized, Vector3.up);
            float top = H, h = top - (mid.y + 0.06f);
            Box($"Spandrel_{i}", new Vector3(x, mid.y + 0.06f + h / 2, mid.x), new Vector3(PT, h, tan.magnitude + 0.02f), M["Plaster_Cream"], g);
        }
        foreach (var t in new[] { 0.14f, 0.3f, 0.7f, 0.86f })                  // cusps under the arch ring
        {
            var c = Curve(t);
            P(PrimitiveType.Sphere, g, new Vector3(x, c.y - 0.1f, c.x), new Vector3(PT + 0.04f, 0.16f, 0.2f), "Teak");
        }
        foreach (var z in new[] { z0 + 0.14f, z1 - 0.14f }) Pillar(g, new Vector3(x, 0f, z));
    }

    /// Maratha wada-style teak pillar: stone base, shaft with brass bands, capital and bracket (tod).
    static void Pillar(Transform parent, Vector3 basePos)
    {
        var g = Group(parent, "WadaPillar");
        g.position = basePos;
        P(PrimitiveType.Cube, g, new Vector3(0, 0.15f, 0), new Vector3(0.42f, 0.3f, 0.42f), "Basalt_Dark");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 1.25f, 0), new Vector3(0.26f, 0.95f, 0.26f), "Teak");
        foreach (var y in new[] { 0.45f, 1.25f, 2.05f }) P(PrimitiveType.Cylinder, g, new Vector3(0, y, 0), new Vector3(0.31f, 0.035f, 0.31f), "Brass");
        P(PrimitiveType.Cube, g, new Vector3(0, 2.26f, 0), new Vector3(0.4f, 0.1f, 0.4f), "Teak_Dark");
        P(PrimitiveType.Cube, g, new Vector3(0, 2.36f, 0), new Vector3(0.62f, 0.1f, 0.3f), "Teak_Dark");
        MakeStatic(g.gameObject);
    }

    // ------------------------------------------------------------------ wall finishes (dado / hall colour / Paithani frieze)
    static void Finish(Transform p, string hall, Vector2 a, Vector2 b, Vector3 normal, bool frieze = true)
    {
        const float off = 0.012f;
        var d = b - a; float len = d.magnitude; if (len < 0.05f) return;
        var mid = (a + b) / 2;
        Vector3 C(float y) => new Vector3(mid.x, y, mid.y) + normal * off;
        var g = Group(p, $"{hall}_{a.x:0.#}_{a.y:0.#}");
        Panel("Dado", C(0.45f), normal, len, 0.9f, M["Dado"], g, len / 1.2f);
        if (frieze)
        {
            Panel("Plaster", C(1.925f), normal, len, 2.05f, M[hall], g);
            Panel("Frieze", C(3.11f), normal, len, 0.32f, M["Frieze"], g, len / 1.7f);
            Panel("Upper", C(3.615f), normal, len, 0.69f, M["Plaster_Cream"], g);
        }
        else Panel("Plaster", C(2.43f), normal, len, 3.06f, M[hall], g);
    }

    static void BuildFinishes(Transform p)
    {
        // Hall 3 (west), Hall 2 (middle), Hall 1 (east) on the long walls
        var halls = new[] { ("Hall_3", 0.1f, 6.15f), ("Hall_2", 6.45f, 12.45f), ("Hall_1", 12.75f, 18.8f) };
        foreach (var (hall, x0, x1) in halls)
        {
            Finish(p, hall, V(x0, 0.1f), V(x1, 0.1f), Vector3.forward);
            Finish(p, hall, V(x0, 9.35f), V(x1, 9.35f), Vector3.back);
        }
        Finish(p, "Hall_3", V(0.1f, 0.1f), V(0.1f, 9.35f), Vector3.right);
        // partition faces
        foreach (var (x, east, west) in new[] { (12.6f, "Hall_1", "Hall_2"), (6.3f, "Hall_2", "Hall_3") })
            foreach (var (z0, z1) in new[] { (0.1f, 2.6f), (6.85f, 9.35f) })
            {
                Finish(p, east, V(x + PT / 2, z0), V(x + PT / 2, z1), Vector3.right);
                Finish(p, west, V(x - PT / 2, z0), V(x - PT / 2, z1), Vector3.left);
            }
        // Hall 1 east side and the armoury wall seen from the gallery (skip the door z 4.15-5.75)
        Finish(p, "Hall_1", V(18.8f, 0.1f), V(18.8f, 4.15f), Vector3.left);
        Finish(p, "Hall_1", V(18.8f, 5.75f), V(18.8f, 6.75f), Vector3.left);
        // angled wall, gallery side
        var n = new Vector3(-0.7071f, 0f, -0.7071f);
        Finish(p, "Hall_1", V(15.5f, 7.0f) + V(n.x, n.z) * 0.1f, V(18.1f, 4.4f) + V(n.x, n.z) * 0.1f, n, frieze: false);
        // foyer
        Finish(p, "Hall_Foyer", V(18.9f, 9.35f), V(24.9f, 9.35f), Vector3.back);
        Finish(p, "Hall_Foyer", V(24.9f, 6.85f), V(24.9f, 9.35f), Vector3.left);
        Finish(p, "Hall_Foyer", V(18.9f, 6.85f), V(23.3f, 6.85f), Vector3.forward, frieze: false);   // title wall
        Finish(p, "Hall_Foyer", V(24.9f, 6.85f), V(24.9f, 6.85f), Vector3.forward);
        // Mavlas' Armoury (x 19.0-21.85, z 1.95-6.65)
        Finish(p, "Hall_Armour", V(19.0f, 1.95f), V(19.0f, 4.15f), Vector3.right);
        Finish(p, "Hall_Armour", V(19.0f, 5.75f), V(19.0f, 6.65f), Vector3.right);
        Finish(p, "Hall_Armour", V(19.0f, 6.65f), V(21.85f, 6.65f), Vector3.back);
        Finish(p, "Hall_Armour", V(21.85f, 1.95f), V(21.85f, 6.65f), Vector3.left);
        Finish(p, "Hall_Armour", V(19.0f, 1.95f), V(21.85f, 1.95f), Vector3.forward);
        // Forts & Navy room (x 22.05-24.9, z 1.95-6.65), door x 23.3-24.9 in the north wall
        Finish(p, "Hall_Forts", V(22.05f, 1.95f), V(22.05f, 6.65f), Vector3.right);
        Finish(p, "Hall_Forts", V(22.05f, 6.65f), V(23.3f, 6.65f), Vector3.back);
        Finish(p, "Hall_Forts", V(24.9f, 1.95f), V(24.9f, 6.65f), Vector3.left);
        Finish(p, "Hall_Forts", V(22.05f, 1.95f), V(24.9f, 1.95f), Vector3.forward);
    }

    // ------------------------------------------------------------------ ceiling
    static void BuildCeiling(Transform p)
    {
        for (float z = 1.575f; z < 9.4f; z += 1.575f)
            Box("Beam_X", new Vector3(9.45f, H - 0.07f, z), new Vector3(18.9f, 0.14f, 0.16f), M["Teak_Dark"], p);
        for (float x = 2.1f; x < 18.8f; x += 2.1f)
            Box("Beam_Z", new Vector3(x, H - 0.1f, 4.725f), new Vector3(0.18f, 0.2f, 9.45f), M["Teak_Dark"], p);
        foreach (var z in new[] { 7.65f, 8.55f }) Box("Beam_Foyer", new Vector3(21.95f, H - 0.07f, z), new Vector3(6.1f, 0.14f, 0.16f), M["Teak_Dark"], p);

        foreach (var pos in new[] { new Vector3(3.2f, 0, 2.6f), new Vector3(3.2f, 0, 7.4f), new Vector3(8.0f, 0, 4.7f), new Vector3(10.9f, 0, 4.7f),
                                    new Vector3(14.0f, 0, 4.7f), new Vector3(17.3f, 0, 2.6f), new Vector3(21.0f, 0, 7.6f),
                                    new Vector3(20.4f, 0, 4.3f), new Vector3(23.5f, 0, 4.3f) })
            Lantern(p, pos);
    }

    /// Hanging brass lantern (kandil) with a glowing body.
    static void Lantern(Transform parent, Vector3 floorPos)
    {
        var g = Group(parent, "Lantern");
        g.position = new Vector3(floorPos.x, 0f, floorPos.z);
        P(PrimitiveType.Cylinder, g, new Vector3(0, 3.48f, 0), new Vector3(0.015f, 0.44f, 0.015f), "Brass");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 3.06f, 0), new Vector3(0.32f, 0.03f, 0.32f), "Brass");
        P(PrimitiveType.Sphere, g, new Vector3(0, 3.16f, 0), new Vector3(0.22f, 0.16f, 0.22f), "Brass");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 2.86f, 0), new Vector3(0.24f, 0.17f, 0.24f), "Lantern_Glow");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 2.67f, 0), new Vector3(0.28f, 0.025f, 0.28f), "Brass");
        P(PrimitiveType.Sphere, g, new Vector3(0, 2.6f, 0), new Vector3(0.08f, 0.1f, 0.08f), "Brass");
    }

    // ------------------------------------------------------------------ decor
    static void BuildDecor(Transform p)
    {
        // Dhurrie runner along the gallery aisle and rangoli in the foyer
        Panel("Dhurrie_Runner", new Vector3(9.45f, 0.004f, 4.72f), Vector3.up, 17.8f, 0.95f, M["Dhurrie"], p, 17.8f / 1.9f);
        Panel("Rangoli_Foyer", new Vector3(19.85f, 0.006f, 7.95f), Vector3.up, 1.3f, 1.3f, M["Rangoli"], p);
        Panel("Rangoli_Island", new Vector3(9.45f, 0.006f, 4.0f), Vector3.up, 1.0f, 1.0f, M["Rangoli"], p);

        // Murals
        Mural(p, "mural_sahyadri", new Vector3(15.75f, 1.95f, 0.1f), Vector3.forward, 5.3f, 1.9f);            // Hall 1 south wall
        Mural(p, "mural_pratapgad", new Vector3(9.45f, 1.95f, 9.35f), Vector3.back, 5.5f, 1.74f);             // Hall 2 north wall
        Mural(p, "mural_seafort", new Vector3(24.9f, 1.85f, 4.3f), Vector3.left, 4.0f, 1.69f);                // Forts room
        Mural(p, "mural_throne", new Vector3(0.45f, 1.42f, 4.4f), Vector3.right, 1.6f, 2.0f, frame: false);    // throne backdrop

        // Hall banners over the arch gateways and room doors (text faces the hall you are about to enter)
        Banner(p, "banner_Z2", new Vector3(12.6f + PT / 2, 3.58f, 4.725f), Vector3.right, 2.4f);
        Banner(p, "banner_Z1", new Vector3(12.6f - PT / 2, 3.58f, 4.725f), Vector3.left, 2.4f);
        Banner(p, "banner_Z3", new Vector3(6.3f + PT / 2, 3.58f, 4.725f), Vector3.right, 2.4f);
        Banner(p, "banner_Z2", new Vector3(6.3f - PT / 2, 3.58f, 4.725f), Vector3.left, 2.4f);
        Banner(p, "banner_Armour", new Vector3(18.8f, 2.78f, 4.95f), Vector3.left, 1.6f);
        Banner(p, "banner_Forts", new Vector3(24.1f, 2.78f, 6.85f), Vector3.forward, 1.6f);
        // Hanging Hall 1 banner where the foyer opens into the gallery
        var hang = Group(p, "HangingBanner_Hall1");
        P(PrimitiveType.Cylinder, hang, new Vector3(18.95f, 3.86f, 8.1f), new Vector3(0.04f, 1.3f, 0.04f), "Brass", new Vector3(90, 0, 0));
        Box("Cloth_Back", new Vector3(18.95f, 3.56f, 8.1f), new Vector3(0.02f, 0.58f, 2.5f), M["Red_Cloth"], hang);
        Banner(hang, "banner_Z1", new Vector3(18.97f, 3.56f, 8.1f), Vector3.right, 2.4f);

        // Title wall (faces the entrance) and doors
        Box("Banner_Left", new Vector3(19.65f, 2.25f, 6.875f), new Vector3(0.7f, 2.7f, 0.02f), M["Saffron"], p);
        Box("Banner_Right", new Vector3(22.35f, 2.25f, 6.875f), new Vector3(0.7f, 2.7f, 0.02f), M["Saffron"], p);
        foreach (var x in new[] { 19.65f, 22.35f }) Box("Banner_Rod", new Vector3(x, 3.62f, 6.885f), new Vector3(0.86f, 0.035f, 0.035f), M["Brass"], p);
        Text3D("TitleText", "Chhatrapati Shivaji Maharaj\n<size=60%>Hindavi Swarajya</size>",
            new Vector3(21f, 3.2f, 6.9f), Vector3.forward, new Vector2(3.6f, 0.9f), new Color(0.45f, 0.08f, 0.06f), p, true);
        Box("Door_Entrance", new Vector3(23.5f, 1.2f, 9.31f), new Vector3(1.2f, 2.4f, 0.05f), M["Door_Wood"], p);
        Box("Door_WestDouble", new Vector3(0.14f, 1.2f, 7.7f), new Vector3(0.05f, 2.4f, 1.8f), M["Door_Wood"], p);
        foreach (var (c, n) in new[] { (new Vector3(23.5f, 0f, 9.28f), Vector3.back), (new Vector3(0.17f, 0f, 7.7f), Vector3.right) })
        {
            var side = Vector3.Cross(Vector3.up, n);
            for (int i = 0; i < 4; i++) for (int j = -1; j <= 1; j += 2)        // brass studs
                P(PrimitiveType.Sphere, p, c + new Vector3(0, 0.5f + i * 0.5f, 0) + side * (j * 0.3f), Vector3.one * 0.06f, "Brass");
        }

        // Foyer: toran over the entrance, samai lamps, nagara drums, bhagwa flags
        Toran(p, 22.85f, 24.15f, 2.52f, 9.3f);
        Toran(p, 23.25f, 24.95f, 2.52f, 6.87f, sign: 1f);                // over the Forts room door
        Samai(p, new Vector3(19.35f, 0f, 7.3f));
        Samai(p, new Vector3(22.75f, 0f, 7.3f));
        Nagara(p, new Vector3(22.4f, 0f, 9.0f));
        Nagara(p, new Vector3(24.6f, 0f, 9.0f));
        BhagwaFlag(p, new Vector3(19.15f, 0f, 9.15f), 90f);
        BhagwaFlag(p, new Vector3(24.75f, 0f, 7.1f), -90f);
        BhagwaFlag(p, new Vector3(0.75f, 0f, 2.75f), 90f);                // flanking the throne
        BhagwaFlag(p, new Vector3(0.75f, 0f, 6.05f), -90f);
    }

    static void Mural(Transform p, string tex, Vector3 center, Vector3 normal, float w, float h, bool frame = true)
    {
        var g = Group(p, "Mural_" + tex);
        if (frame)
        {
            var f = Box("Frame", center + normal * 0.02f, new Vector3(w + 0.14f, h + 0.14f, 0.04f), M["Teak_Dark"], g);
            f.transform.rotation = Quaternion.LookRotation(-normal);
        }
        Panel("Painting", center + normal * 0.045f, normal, w, h, M[tex], g);
    }

    static void Banner(Transform p, string tex, Vector3 center, Vector3 normal, float w) =>
        Panel(tex, center + normal * 0.012f, normal, w, w * 360f / 1600f, M[tex], p);

    static void Toran(Transform p, float x0, float x1, float y, float z, float sign = -1f)
    {
        var g = Group(p, "Toran");
        P(PrimitiveType.Cylinder, g, new Vector3((x0 + x1) / 2, y + 0.03f, z), new Vector3(0.012f, (x1 - x0) / 2, 0.012f), "Gold", new Vector3(0, 0, 90));
        int n = 13;
        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f), x = Mathf.Lerp(x0, x1, t);
            if (i % 2 == 0) P(PrimitiveType.Cube, g, new Vector3(x, y - 0.1f, z + sign * 0.01f), new Vector3(0.07f, 0.17f, 0.008f), "Leaf_Green", new Vector3(0, 0, 0));
            else P(PrimitiveType.Sphere, g, new Vector3(x, y - 0.06f, z + sign * 0.02f), Vector3.one * 0.07f, "Marigold");
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
    }

    static void Nagara(Transform p, Vector3 pos)
    {
        var g = Group(p, "Nagara_Drum"); g.position = pos;
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.18f, 0), new Vector3(0.5f, 0.18f, 0.5f), "Teak_Dark");
        P(PrimitiveType.Sphere, g, new Vector3(0, 0.5f, 0), new Vector3(0.62f, 0.5f, 0.62f), "Copper");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.74f, 0), new Vector3(0.6f, 0.02f, 0.6f), "Hide_Light");
        P(PrimitiveType.Cylinder, g, new Vector3(0, 0.71f, 0), new Vector3(0.64f, 0.025f, 0.64f), "Red_Cloth");
    }

    static void BhagwaFlag(Transform p, Vector3 pos, float yaw)
    {
        var g = Group(p, "Bhagwa_Flag"); g.position = pos; g.rotation = Quaternion.Euler(0, yaw, 0);
        P(PrimitiveType.Cylinder, g, new Vector3(0, 1.3f, 0), new Vector3(0.05f, 1.3f, 0.05f), "Teak_Dark");
        P(PrimitiveType.Sphere, g, new Vector3(0, 2.63f, 0), Vector3.one * 0.08f, "Gold");
        var flag = new GameObject("Jaripatka");
        flag.transform.SetParent(g, false);
        flag.transform.localPosition = new Vector3(0.03f, 2.55f, 0);
        flag.AddComponent<MeshFilter>().sharedMesh = FlagMesh();
        flag.AddComponent<MeshRenderer>().sharedMaterial = M["Bhagwa_Flag"];
    }

    /// Swallow-tailed (jari-patka style) pennant in the XY plane; the material renders both sides.
    static Mesh FlagMesh()
    {
        const string key = "Flag_Jaripatka";
        if (Meshes.TryGetValue(key, out var cached)) return cached;
        var path = $"{MeshDir}/{key}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = key };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0.95f, -0.22f, 0), new Vector3(0.62f, -0.34f, 0), new Vector3(0.95f, -0.48f, 0), new Vector3(0, -0.66f, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 4, 2, 3, 4 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
        }
        return Meshes[key] = mesh;
    }

    // ------------------------------------------------------------------ lighting
    static void BuildLighting(Transform p)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.58f, 0.52f, 0.46f);
        RenderSettings.ambientEquatorColor = new Color(0.44f, 0.38f, 0.32f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.19f, 0.16f);
        RenderSettings.fog = false;

        var sun = NewLight("Fill_Directional", LightType.Directional, Vector3.zero, p);
        sun.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
        sun.intensity = 0.4f;

        var warm = new Color(1f, 0.85f, 0.65f);
        foreach (var (name, pos) in new[]
        {
            ("Point_Hall1", new Vector3(15.6f, 3.4f, 4.7f)), ("Point_Hall2", new Vector3(9.45f, 3.4f, 4.7f)),
            ("Point_Hall3", new Vector3(3.2f, 3.4f, 4.7f)), ("Point_Foyer", new Vector3(22f, 3.4f, 8.1f)),
            ("Point_Armoury", new Vector3(20.4f, 3.3f, 4.3f)), ("Point_FortsRoom", new Vector3(23.5f, 3.3f, 4.3f)),
        })
        {
            var l = NewLight(name, LightType.Point, pos, p);
            l.range = 9f; l.intensity = 1.25f; l.color = warm;
        }

        foreach (var (name, from, to, angle, intensity) in new[]
        {
            ("Spot_AngledWall", new Vector3(15.4f, 3.7f, 4.9f), new Vector3(16.8f, 1.4f, 5.7f), 70f, 3f),
            ("Spot_Hall1Mural", new Vector3(15.75f, 3.8f, 2.6f), new Vector3(15.75f, 1.9f, 0.1f), 85f, 2.5f),
            ("Spot_Island", new Vector3(9.45f, 3.8f, 3.4f), new Vector3(9.45f, 1.2f, 2.1f), 50f, 4f),
            ("Spot_Cannon", new Vector3(9.45f, 3.8f, 6.4f), new Vector3(9.45f, 0.5f, 7.9f), 60f, 3.5f),
            ("Spot_Throne", new Vector3(3.2f, 3.8f, 4.4f), new Vector3(1.3f, 1.0f, 4.4f), 60f, 4f),
            ("Spot_Coronation", new Vector3(3.8f, 3.8f, 5.2f), new Vector3(3.8f, 1.6f, 8.6f), 65f, 3f),
            ("Spot_Map", new Vector3(3.6f, 3.8f, 2.2f), new Vector3(3.6f, 1.7f, 0.2f), 60f, 2.5f),
        })
        {
            var s = NewLight(name, LightType.Spot, from, p);
            s.transform.LookAt(to);
            s.spotAngle = angle; s.range = 7f; s.intensity = intensity; s.color = warm;
        }
    }

    static Light NewLight(string name, LightType type, Vector3 pos, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.shadows = LightShadows.None;      // mobile VR budget
        return l;
    }

    // ------------------------------------------------------------------ teleport points
    static Dictionary<string, TeleportPoint> BuildTeleportPoints(Transform p, Sprite disc)
    {
        var mat = SpriteMat("TeleportDisc", disc.texture, new Color(1f, 0.55f, 0.1f, 0.6f));
        var pts = new Dictionary<string, TeleportPoint>();
        void Add(string name, float x, float z, float yaw) => pts[name] = MakePoint(name, x, z, yaw, mat, p);

        Add("Title", 21.0f, 8.4f, 180f);          // spawn: foyer, facing the title wall
        Add("FortsRoom", 23.5f, 5.0f, 180f);
        Add("H1_Weapons", 15.3f, 4.2f, 45f);
        Add("H1_BoardN", 14.1f, 7.4f, 270f);
        Add("H1_BoardS", 14.1f, 2.1f, 270f);
        Add("Armoury", 19.75f, 4.95f, 90f);
        Add("H2_Centre", 9.45f, 5.0f, 180f);
        Add("H2_BoardE", 11.2f, 2.2f, 90f);
        Add("H2_BoardWN", 7.7f, 7.4f, 270f);
        Add("H2_BoardWS", 7.7f, 2.2f, 270f);
        Add("H3_Throne", 3.4f, 4.2f, 270f);
        Add("H3_Coins", 3.8f, 5.4f, 0f);
        Add("H3_Map", 3.6f, 2.3f, 180f);
        Add("H3_BoardS", 4.9f, 1.6f, 90f);
        Add("H3_BoardN", 4.9f, 7.9f, 90f);
        Add("Exit", 1.0f, 7.7f, 270f);            // destination set to Title after build
        return pts;
    }

    static TeleportPoint MakePoint(string name, float x, float z, float yaw, Material mat, Transform parent)
    {
        var go = new GameObject("TP_" + name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, 0.02f, z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.layer = teleportLayer;

        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "Disc";
        UnityEngine.Object.DestroyImmediate(disc.GetComponent<Collider>());
        disc.transform.SetParent(go.transform, false);
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        disc.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
        disc.layer = teleportLayer;
        disc.GetComponent<Renderer>().sharedMaterial = mat;

        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.05f, 0f);
        col.size = new Vector3(0.8f, 0.1f, 0.8f);

        var tp = go.AddComponent<TeleportPoint>();
        tp.disc = disc.GetComponent<Renderer>();
        if (name == "Exit") Text3D("ExitLabel", "Return to entrance", go.transform.position + new Vector3(0.5f, 0.25f, 0f),
            Vector3.right, new Vector2(1.2f, 0.18f), Color.white, go.transform);
        return tp;
    }

    // ------------------------------------------------------------------ player rig
    static void BuildPlayer(TeleportPoint spawn, Sprite ring)
    {
        var player = new GameObject("Player");
        player.transform.SetPositionAndRotation(new Vector3(spawn.transform.position.x, 0f, spawn.transform.position.z), spawn.transform.rotation);
        player.AddComponent<MuseumCardboardRuntime>();

        var look = new GameObject("EditorLook");
        look.transform.SetParent(player.transform, false);
        look.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        look.AddComponent<EditorMouseLook>();

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(look.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
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

    // ------------------------------------------------------------------ info panel
    static InfoPanel BuildInfoPanel(Transform parent)
    {
        var go = new GameObject("InfoPanel");
        go.transform.SetParent(parent, false);
        var narration = go.AddComponent<AudioSource>();
        narration.playOnAwake = false; narration.spatialBlend = 0f;

        var canvas = NewCanvas("Content", go.transform, new Vector2(900, 600), 0.001f);
        var ct = canvas.transform;
        UIChild<Image>("Background", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).color = new Color(0.13f, 0.06f, 0.04f, 0.95f);
        UIChild<Image>("TopBar", ct, new Vector2(0, 1), Vector2.one, new Vector2(0, -12), Vector2.zero).color = new Color(1f, 0.53f, 0.08f);
        UIChild<Image>("BottomBar", ct, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 6)).color = new Color(0.84f, 0.65f, 0.25f);

        var title = UIChild<TextMeshProUGUI>("Title", ct, new Vector2(0, 1), Vector2.one, new Vector2(30, -105), new Vector2(-30, -22));
        Style(title, 46, new Color(1f, 0.82f, 0.5f), FontStyles.Bold, TextAlignmentOptions.Left);
        var era = UIChild<TextMeshProUGUI>("Era", ct, new Vector2(0, 1), Vector2.one, new Vector2(30, -150), new Vector2(-30, -105));
        Style(era, 26, new Color(0.85f, 0.8f, 0.7f), FontStyles.Italic, TextAlignmentOptions.Left);
        var picture = UIChild<Image>("Picture", ct, Vector2.zero, new Vector2(0, 1), new Vector2(30, 115), new Vector2(290, -165));
        picture.preserveAspect = true;
        var body = UIChild<TextMeshProUGUI>("Body", ct, Vector2.zero, Vector2.one, new Vector2(310, 115), new Vector2(-30, -165));
        Style(body, 28, new Color(0.96f, 0.93f, 0.87f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
        body.enableAutoSizing = true; body.fontSizeMin = 16; body.fontSizeMax = 30;
        var mrImage = UIChild<Image>("MarathiRendered", ct, Vector2.zero, Vector2.one, new Vector2(30, 115), new Vector2(-30, -22));
        mrImage.preserveAspect = true; mrImage.gameObject.SetActive(false);

        var ip = go.AddComponent<InfoPanel>();
        ip.root = canvas.gameObject;
        ip.titleText = title; ip.eraText = era; ip.bodyText = body;
        ip.picture = picture; ip.marathiRenderedImage = mrImage;
        ip.latinFont = TMP_Settings.defaultFontAsset;
        ip.devanagariFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Museum/Fonts/NotoSansDevanagari SDF.asset");
        ip.narration = narration;

        float x = 30;
        ip.playLabel = MakeGazeButton(ct, "Btn_PlayPause", "Pause", ref x, ip.TogglePlay);
        MakeGazeButton(ct, "Btn_Close", "Close", ref x, ip.Close);
        MakeGazeButton(ct, "Btn_Next", "Next >", ref x, ip.Next);
        ip.languageLabel = MakeGazeButton(ct, "Btn_Language", "Marathi", ref x, ip.ToggleLanguage);
        return ip;
    }

    static void Style(TMP_Text t, float size, Color c, FontStyles style, TextAlignmentOptions align)
    {
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

    // ------------------------------------------------------------------ exhibits
    static readonly string[] TourOrder =
    {
        "A01",                                   // foyer
        "A12", "A13", "A14", "B08",              // Forts & Navy room
        "B01", "B02", "A05", "A06", "A07",       // Hall 1 Swarajya Begins
        "B03", "A09", "A10", "A11",              // Mavlas' Armoury
        "B04", "A08", "A17", "B05", "B06",       // Hall 2 Forts & Campaigns
        "A18", "A15", "A02", "A03", "A04", "B07", "A16", "B09",   // Hall 3 Rajyabhishek & the Raj
    };

    enum Mount { Plinth, Wall, Floor, Board }

    class Def
    {
        public string id, slug, titleEn, titleMr, panelEn, point, era, image;
        public Vector3 pos, face;
        public Mount mount;
        public bool enlarged;
        public float spin = 20f, panelSide = 0.95f;
        public Action<Transform> model;
        public string replacesTitle;     // update English text if the asset still has this old default title
    }

    static Dictionary<string, Artifact> BuildArtifacts(Transform p, Dictionary<string, TeleportPoint> points)
    {
        var result = new Dictionary<string, Artifact>();
        foreach (var d in Defs())
        {
            var data = LoadOrCreateData(d);
            var go = new GameObject($"{d.id}_{d.slug}");
            go.transform.SetParent(p, false);
            go.transform.SetPositionAndRotation(d.pos, Quaternion.LookRotation(-d.face));
            go.layer = interactiveLayer;

            var model = new GameObject("Model").transform;   // <- replace with the real 3D model
            model.SetParent(go.transform, false);
            d.model(model);
            foreach (var c in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (var t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = interactiveLayer;

            var b = new Bounds(d.pos, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(b.center);
            float foot = Mathf.Max(b.size.x, b.size.z) + 0.1f;
            bool flat = d.mount == Mount.Wall || d.mount == Mount.Board;
            col.size = new Vector3(foot, b.size.y + 0.1f, flat ? Mathf.Max(0.12f, Mathf.Min(b.size.x, b.size.z) + 0.1f) : foot);

            float yaw = Quaternion.LookRotation(-d.face).eulerAngles.y;
            switch (d.mount)
            {
                case Mount.Plinth:
                    float top = d.pos.y - 0.3f;
                    Pedestal(go.transform.parent, new Vector3(d.pos.x, 0, d.pos.z), top, yaw);
                    Text3D("Label", d.titleEn, new Vector3(d.pos.x, top - 0.14f, d.pos.z) + d.face * 0.335f, d.face, new Vector2(0.54f, 0.1f), new Color(1f, 0.85f, 0.55f), go.transform);
                    break;
                case Mount.Wall:
                    Text3D("Label", d.titleEn, d.pos + Vector3.down * (b.extents.y + 0.12f) + d.face * 0.04f, d.face, new Vector2(0.9f, 0.1f), Color.white, go.transform);
                    break;
                case Mount.Floor:
                    var lect = d.pos + d.face * 1.1f + Vector3.left * 0f;
                    Box("Lectern", new Vector3(lect.x, 0.45f, lect.z), new Vector3(0.5f, 0.9f, 0.08f), M["Teak_Dark"], go.transform.parent, yaw);
                    Text3D("Label", d.titleEn, new Vector3(lect.x, 0.8f, lect.z) + d.face * 0.05f, d.face, new Vector2(0.46f, 0.12f), new Color(1f, 0.85f, 0.55f), go.transform);
                    break;
            }

            var stand = points[d.point];
            var side = Vector3.Cross(Vector3.up, d.face).normalized;
            var c1 = d.pos + side * d.panelSide; var c2 = d.pos - side * d.panelSide;
            var sp = stand.transform.position;
            var chosen = (Flat(c1) - Flat(sp)).sqrMagnitude <= (Flat(c2) - Flat(sp)).sqrMagnitude ? c1 : c2;
            var anchor = new GameObject("PanelAnchor").transform;
            anchor.SetParent(go.transform, false);
            anchor.SetPositionAndRotation(new Vector3(chosen.x, 1.55f, chosen.z) + d.face * 0.4f, Quaternion.LookRotation(-d.face));

            var art = go.AddComponent<Artifact>();
            art.data = data; art.model = model; art.panelAnchor = anchor; art.standPoint = stand;
            art.spinSpeed = (flat || d.mount == Mount.Floor) && !d.enlarged ? 0f : d.spin;
            result[d.id] = art;
        }
        return result;
    }

    /// Dark teak pedestal with a brass cap and basalt foot.
    static void Pedestal(Transform parent, Vector3 floor, float top, float yaw)
    {
        var g = Group(parent, "Pedestal");
        g.position = floor; g.rotation = Quaternion.Euler(0, yaw, 0);
        P(PrimitiveType.Cube, g, new Vector3(0, 0.05f, 0), new Vector3(0.72f, 0.1f, 0.72f), "Basalt_Dark");
        P(PrimitiveType.Cube, g, new Vector3(0, top / 2f, 0), new Vector3(0.6f, top - 0.04f, 0.6f), "Teak_Dark");
        P(PrimitiveType.Cube, g, new Vector3(0, top - 0.015f, 0), new Vector3(0.66f, 0.03f, 0.66f), "Brass");
        MakeStatic(g.gameObject);
    }

    static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

    static ArtifactData LoadOrCreateData(Def d)
    {
        var path = $"{DataDir}/{d.id}_{d.slug}.asset";
        var data = AssetDatabase.LoadAssetAtPath<ArtifactData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ArtifactData>();
            data.id = d.id;
            data.titleEn = d.titleEn; data.titleMr = d.titleMr; data.panelEn = d.panelEn;
            data.panelMr = "(मराठी मजकूर लवकरच)";
            data.shownEnlarged = d.enlarged;
            if (d.era != null) data.era = d.era;
            AssetDatabase.CreateAsset(data, path);
        }
        else if (d.replacesTitle != null && data.titleEn == d.replacesTitle)
        {
            data.titleEn = d.titleEn; data.panelEn = d.panelEn; data.titleMr = d.titleMr;
        }
        if (d.era != null) data.era = d.era;
        if (d.image != null) data.image = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{d.image}");
        EditorUtility.SetDirty(data);
        return data;
    }

    // placeholder model helpers ----------------------------------------------------------------
    static Action<Transform> Picture(string tex, float w, float h) => m =>
    {
        P(PrimitiveType.Cube, m, new Vector3(0, 0, 0.02f), new Vector3(w + 0.14f, h + 0.14f, 0.04f), "Teak_Dark");
        P(PrimitiveType.Cube, m, new Vector3(0, 0, 0.0f), new Vector3(w + 0.05f, h + 0.05f, 0.012f), "Gold");
        var q = P(PrimitiveType.Quad, m, new Vector3(0, 0, -0.012f), new Vector3(w, h, 1f), tex);
    };

    static Action<Transform> StoryBoard(string id) => Picture("board_" + id, 1.5f, 1.9f);

    static IEnumerable<Def> Defs()
    {
        var n = new Vector3(-0.7071f, 0f, -0.7071f);          // angled wall faces the gallery (south-west)
        Vector3 OnAngled(float t) => new Vector3(Mathf.Lerp(15.5f, 18.1f, t), 1.45f, Mathf.Lerp(7.0f, 4.4f, t)) + n * 0.3f;
        const float faceOff = 0.05f;
        Vector3 OnPartition(float x, float z, float sign) => new Vector3(x + sign * (PT / 2 + faceOff), 1.75f, z);

        // ---------------- Foyer
        yield return new Def
        {
            id = "A01", slug = "Portrait", point = "Title", mount = Mount.Wall, spin = 0, panelSide = 1.1f,
            pos = new Vector3(21f, 1.8f, 6.85f + 0.06f), face = Vector3.forward,
            replacesTitle = "Portrait of Chhatrapati Shivaji Maharaj",
            titleEn = "Chhatrapati Shivaji Maharaj (1630–1680)", titleMr = "छत्रपती शिवाजी महाराज (१६३०–१६८०)",
            panelEn = "Chhatrapati Shivaji Maharaj (1630–1680) founded Hindavi Swarajya, the Maratha kingdom of the Deccan, and was crowned at Raigad in 1674. These halls follow his life through the forts, campaigns, arms, coins and ships of his era.",
            model = Picture("painting_intro", 1.15f, 1.5f),
        };

        // ---------------- Forts & Navy room
        yield return Board("B08", "SeaForts", "FortsRoom", new Vector3(21.95f + 0.1f + faceOff, 1.75f, 4.3f), Vector3.right,
            "Sea Forts and the Navy", "जलदुर्ग आणि आरमार", "From the 1650s",
            "Seeing the threat from the sea, Shivaji Maharaj built a navy and sea forts such as Sindhudurg and Vijaydurg to guard the Konkan coast and its trade. He is often called the father of the Indian Navy.");
        foreach (var dd in FortModels()) yield return dd;

        // ---------------- Hall 1: Swarajya Begins
        yield return Board("B01", "Shivneri", "H1_BoardN", OnPartition(12.6f, 8.15f, 1), Vector3.right,
            "Shivneri and Jijabai", "शिवनेरी आणि जिजाबाई", "1630 – 1645",
            "Shivaji was born at Shivneri fort near Junnar in 1630. Raised largely by his mother Jijabai in the Pune jagir, he grew up hearing the epics and learning the Sahyadri country and its people, the Mavlas, who would become his first soldiers.");
        yield return Board("B02", "Oath", "H1_BoardS", OnPartition(12.6f, 1.3f, 1), Vector3.right,
            "The Oath of Swarajya", "स्वराज्याची शपथ", "1645 – 1646",
            "Tradition holds that in 1645 the young Shivaji and his companions vowed at the Raireshwar temple to found Hindavi Swarajya, self-rule. In 1646 he took Torna fort, the first of the many forts that became the backbone of his kingdom.");
        yield return new Def
        {
            id = "A05", slug = "BhavaniTalwar", point = "H1_Weapons", mount = Mount.Wall, spin = 0, pos = OnAngled(0.25f), face = n,
            titleEn = "Bhavani Talwar — Sword", titleMr = "भवानी तलवार",
            panelEn = "By tradition, Shivaji Maharaj's sword was named after the goddess Bhavani. Its history and present whereabouts are debated by historians; this is a representative sword of the period. (Replica.)",
            model = m =>
            {
                var s = new GameObject("Sword").transform; s.SetParent(m, false); s.localRotation = Quaternion.Euler(0, 0, 20);
                P(PrimitiveType.Cube, s, new Vector3(0, 0.1f, 0), new Vector3(0.04f, 0.9f, 0.008f), "Steel");
                P(PrimitiveType.Cube, s, new Vector3(0, -0.36f, 0), new Vector3(0.18f, 0.03f, 0.04f), "Gold");
                P(PrimitiveType.Cylinder, s, new Vector3(0, -0.43f, 0), new Vector3(0.035f, 0.05f, 0.035f), "Gold");
                P(PrimitiveType.Sphere, s, new Vector3(0, -0.5f, 0), new Vector3(0.07f, 0.03f, 0.07f), "Gold");
            },
        };
        yield return new Def
        {
            id = "A06", slug = "Dhal", point = "H1_Weapons", mount = Mount.Wall, spin = 0, pos = OnAngled(0.5f), face = n,
            titleEn = "Dhal — Shield", titleMr = "ढाल",
            panelEn = "A round shield of hardened hide or metal with raised bosses, carried by Maratha infantry and cavalry alongside the sword. (Replica.)",
            model = m =>
            {
                P(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.5f, 0.03f, 0.5f), "Hide", new Vector3(90, 0, 0));
                P(PrimitiveType.Cylinder, m, new Vector3(0, 0, -0.005f), new Vector3(0.52f, 0.02f, 0.52f), "Brass", new Vector3(90, 0, 0));
                P(PrimitiveType.Cylinder, m, new Vector3(0, 0, -0.02f), new Vector3(0.47f, 0.02f, 0.47f), "Hide", new Vector3(90, 0, 0));
                foreach (var o in new[] { new Vector2(0.08f, 0.08f), new Vector2(-0.08f, 0.08f), new Vector2(0.08f, -0.08f), new Vector2(-0.08f, -0.08f) })
                    P(PrimitiveType.Sphere, m, new Vector3(o.x, o.y, -0.035f), Vector3.one * 0.05f, "Brass");
            },
        };
        yield return new Def
        {
            id = "A07", slug = "Dandpatta", point = "H1_Weapons", mount = Mount.Wall, spin = 0, pos = OnAngled(0.75f), face = n,
            titleEn = "Dandpatta — Gauntlet Sword", titleMr = "दांडपट्टा",
            panelEn = "A long, flexible blade fixed to an armoured gauntlet that covers the hand and forearm, closely associated with Maratha warriors. (Replica.)",
            model = m =>
            {
                P(PrimitiveType.Capsule, m, new Vector3(0, -0.3f, 0), new Vector3(0.12f, 0.12f, 0.12f), "Steel_Dark");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.15f, 0), new Vector3(0.04f, 0.8f, 0.006f), "Steel");
            },
        };

        // ---------------- Mavlas' Armoury
        var b03 = Board("B03", "Mavlas", "Armoury", new Vector3(20.0f, 1.75f, 6.75f - 0.1f - faceOff), Vector3.back,
            "The Mavlas", "मावळे", "Soldiers of the Sahyadri",
            "Hill farmers of the Maval valleys formed the core of Shivaji Maharaj's infantry. Lightly armed and expert in the terrain, they mastered swift raids and surprise attacks, a way of fighting later called ganimi kava.");
        b03.panelSide = 0.55f;
        yield return b03;
        yield return new Def
        {
            id = "A09", slug = "Jiretop", point = "Armoury", mount = Mount.Plinth, spin = 20, pos = new Vector3(21.3f, 1.3f, 3.3f), face = Vector3.left,
            titleEn = "Jiretop — Helmet", titleMr = "जिरेटोप",
            panelEn = "A steel helmet with a mail curtain to protect the neck, worn by soldiers and commanders of the period. (Replica.)",
            model = m =>
            {
                P(PrimitiveType.Sphere, m, new Vector3(0, 0.05f, 0), new Vector3(0.26f, 0.22f, 0.26f), "Steel");
                P(PrimitiveType.Cylinder, m, new Vector3(0, -0.08f, 0), new Vector3(0.27f, 0.06f, 0.27f), "Steel_Dark");
                P(PrimitiveType.Cylinder, m, new Vector3(0, 0.2f, 0), new Vector3(0.015f, 0.04f, 0.015f), "Gold");
            },
        };
        yield return new Def
        {
            id = "A10", slug = "Chilkhat", point = "Armoury", mount = Mount.Plinth, spin = 20, pos = new Vector3(21.3f, 1.4f, 4.6f), face = Vector3.left,
            titleEn = "Chilkhat — Mail Armour", titleMr = "चिलखत",
            panelEn = "Armour of thousands of interlinked iron rings, worn over padded cloth. Flexible and strong, it was prized by cavalry. (Replica.)",
            model = m =>
            {
                P(PrimitiveType.Capsule, m, Vector3.zero, new Vector3(0.36f, 0.3f, 0.22f), "Steel_Dark");
                P(PrimitiveType.Capsule, m, new Vector3(0.22f, 0.12f, 0), new Vector3(0.1f, 0.15f, 0.1f), "Steel_Dark", new Vector3(0, 0, 70));
                P(PrimitiveType.Capsule, m, new Vector3(-0.22f, 0.12f, 0), new Vector3(0.1f, 0.15f, 0.1f), "Steel_Dark", new Vector3(0, 0, -70));
            },
        };
        yield return new Def
        {
            id = "A11", slug = "Bichwa", point = "Armoury", mount = Mount.Plinth, enlarged = true, spin = 20, pos = new Vector3(21.3f, 1.25f, 5.9f), face = Vector3.left,
            titleEn = "Bichwa — Dagger", titleMr = "बिचवा",
            panelEn = "A curved dagger named after the scorpion's sting, easy to conceal and used at close quarters. (Replica, shown enlarged.)",
            model = m =>
            {
                P(PrimitiveType.Cube, m, new Vector3(0, 0.08f, 0), new Vector3(0.04f, 0.28f, 0.008f), "Steel", new Vector3(0, 0, 15));
                P(PrimitiveType.Cube, m, new Vector3(0.03f, -0.12f, 0), new Vector3(0.04f, 0.14f, 0.04f), "Teak", new Vector3(0, 0, 15));
            },
        };

        // ---------------- Hall 2: Forts & Campaigns
        yield return Board("B04", "Pratapgad", "H2_BoardE", OnPartition(12.6f, 1.3f, -1), Vector3.left,
            "Pratapgad", "प्रतापगड", "November 1659",
            "The Bijapur general Afzal Khan met Shivaji Maharaj below Pratapgad fort. The meeting turned to combat; Afzal Khan was killed and his army routed in the forests that followed. The victory made Shivaji's name known across India.");
        yield return new Def
        {
            id = "A08", slug = "WaghNakh", point = "H2_Centre", mount = Mount.Plinth, enlarged = true, spin = 20,
            pos = new Vector3(9.45f, 1.45f, 2.1f), face = Vector3.forward,
            titleEn = "Wagh Nakh — Tiger Claws", titleMr = "वाघनखे",
            panelEn = "Steel 'tiger claws' worn concealed in the hand, with rings for two fingers. Tradition links them to Shivaji Maharaj's encounter with Afzal Khan at Pratapgad in 1659. (Replica, shown enlarged.)",
            model = m =>
            {
                var k = new GameObject("Claws").transform; k.SetParent(m, false); k.localScale = Vector3.one * 3f;
                P(PrimitiveType.Cube, k, Vector3.zero, new Vector3(0.12f, 0.02f, 0.02f), "Steel");
                for (int i = 0; i < 4; i++)
                    P(PrimitiveType.Cube, k, new Vector3(-0.042f + i * 0.028f, 0.03f, -0.01f), new Vector3(0.008f, 0.05f, 0.008f), "Steel", new Vector3(-25, 0, 0));
                P(PrimitiveType.Cylinder, k, new Vector3(-0.07f, 0, 0), new Vector3(0.03f, 0.004f, 0.03f), "Steel", new Vector3(0, 0, 90));
                P(PrimitiveType.Cylinder, k, new Vector3(0.07f, 0, 0), new Vector3(0.03f, 0.004f, 0.03f), "Steel", new Vector3(0, 0, 90));
            },
        };
        yield return new Def
        {
            id = "A17", slug = "Tof", point = "H2_Centre", mount = Mount.Floor, spin = 0,
            pos = new Vector3(9.45f, 0.62f, 7.9f), face = Vector3.back,
            titleEn = "Tof — Fort Cannon", titleMr = "तोफ",
            panelEn = "Cannon of the period, mounted on wooden carriages, defended fort walls and gateways. (Representative replica.)",
            model = m =>
            {
                P(PrimitiveType.Cube, m, new Vector3(0, -0.52f, 0), new Vector3(1.9f, 0.1f, 1.0f), "Basalt_Dark");
                P(PrimitiveType.Cylinder, m, new Vector3(0.05f, 0.1f, 0), new Vector3(0.3f, 0.45f, 0.3f), "Gunmetal", new Vector3(0, 0, 90));
                P(PrimitiveType.Cylinder, m, new Vector3(0.65f, 0.1f, 0), new Vector3(0.24f, 0.3f, 0.24f), "Gunmetal", new Vector3(0, 0, 90));
                P(PrimitiveType.Cylinder, m, new Vector3(0.95f, 0.1f, 0), new Vector3(0.29f, 0.05f, 0.29f), "Bronze", new Vector3(0, 0, 90));
                P(PrimitiveType.Sphere, m, new Vector3(-0.42f, 0.1f, 0), new Vector3(0.24f, 0.24f, 0.24f), "Gunmetal");
                foreach (var z in new[] { -0.2f, 0.2f })
                {
                    P(PrimitiveType.Cube, m, new Vector3(-0.05f, -0.12f, z), new Vector3(1.1f, 0.22f, 0.08f), "Teak");
                    P(PrimitiveType.Cylinder, m, new Vector3(0.15f, -0.2f, z * 1.35f), new Vector3(0.62f, 0.03f, 0.62f), "Teak_Dark", new Vector3(90, 0, 0));
                    P(PrimitiveType.Cylinder, m, new Vector3(0.15f, -0.2f, z * 1.4f), new Vector3(0.14f, 0.04f, 0.14f), "Brass", new Vector3(90, 0, 0));
                }
            },
        };
        yield return Board("B05", "Agra", "H2_BoardWN", OnPartition(6.3f, 8.15f, 1), Vector3.right,
            "Escape from Agra", "आग्र्याहून सुटका", "1666",
            "Summoned to Aurangzeb's court at Agra and placed under house arrest, Shivaji Maharaj escaped with his son Sambhaji, by tradition hidden in large baskets of sweets, and made his way safely back to the Deccan.");
        yield return Board("B06", "Sinhagad", "H2_BoardWS", OnPartition(6.3f, 1.3f, 1), Vector3.right,
            "Sinhagad", "सिंहगड", "February 1670",
            "Tanaji Malusare led a night assault up the cliffs of Kondhana fort and captured it, but fell in the battle. Tradition says Shivaji Maharaj mourned, 'Gad aala, pan sinha gela' (the fort is won, but the lion is lost), and the fort became Sinhagad.");

        // ---------------- Hall 3: Rajyabhishek & the Raj
        yield return new Def
        {
            id = "A18", slug = "Simhasan", point = "H3_Throne", mount = Mount.Floor, spin = 0, era = "Rajyabhishek, 1674",
            pos = new Vector3(1.35f, 0.0f, 4.4f), face = Vector3.right, panelSide = 1.2f,
            titleEn = "Simhasan — The Throne", titleMr = "सिंहासन",
            panelEn = "The original throne used at the 1674 coronation has not survived; descriptions speak of a golden throne beneath a royal canopy. This is an artistic reconstruction.",
            model = m =>
            {
                P(PrimitiveType.Cube, m, new Vector3(0, 0.15f, 0), new Vector3(1.8f, 0.3f, 1.8f), "Basalt_Dark");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.31f, 0), new Vector3(1.84f, 0.02f, 1.84f), "Gold");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.55f, 0.05f), new Vector3(0.85f, 0.45f, 0.6f), "Gold");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.8f, 0.04f), new Vector3(0.7f, 0.06f, 0.5f), "Red_Cloth");
                P(PrimitiveType.Cube, m, new Vector3(0, 1.2f, 0.32f), new Vector3(0.85f, 0.9f, 0.1f), "Gold");
                P(PrimitiveType.Sphere, m, new Vector3(0, 1.68f, 0.32f), new Vector3(0.4f, 0.22f, 0.1f), "Gold");
                foreach (var x in new[] { -0.46f, 0.46f })
                {
                    P(PrimitiveType.Cube, m, new Vector3(x, 0.9f, 0.05f), new Vector3(0.1f, 0.12f, 0.6f), "Gold");
                    P(PrimitiveType.Sphere, m, new Vector3(x, 0.98f, -0.27f), Vector3.one * 0.13f, "Gold");          // lion-head finials
                }
                foreach (var (x, z) in new[] { (-0.8f, -0.8f), (0.8f, -0.8f), (-0.8f, 0.8f), (0.8f, 0.8f) })
                    P(PrimitiveType.Cylinder, m, new Vector3(x, 1.4f, z), new Vector3(0.06f, 1.1f, 0.06f), "Gold");
                P(PrimitiveType.Cube, m, new Vector3(0, 2.52f, 0), new Vector3(1.8f, 0.08f, 1.8f), "Gold");
                P(PrimitiveType.Cube, m, new Vector3(0, 2.42f, 0), new Vector3(1.76f, 0.12f, 1.76f), "Red_Cloth");
                P(PrimitiveType.Sphere, m, new Vector3(0, 2.68f, 0), new Vector3(1.3f, 0.5f, 1.3f), "Gold");
                P(PrimitiveType.Sphere, m, new Vector3(0, 2.98f, 0), Vector3.one * 0.12f, "Gold");
            },
        };
        yield return new Def
        {
            id = "A15", slug = "Rajyabhishek", point = "H3_Coins", mount = Mount.Wall, spin = 0, panelSide = 1.7f,
            pos = new Vector3(3.8f, 1.95f, 9.45f - 0.1f - 0.06f), face = Vector3.back,
            titleEn = "Rajyabhishek — The Coronation", titleMr = "राज्याभिषेक",
            panelEn = "The coronation of Shivaji Maharaj as Chhatrapati at Raigad in June 1674, attended by court, clergy and envoys. (Artistic reconstruction.)",
            model = Picture("painting_coronation", 2.4f, 1.6f),
        };
        yield return new Def
        {
            id = "A02", slug = "Rajmudra", point = "H3_Coins", mount = Mount.Plinth, enlarged = true, spin = 25,
            pos = new Vector3(2.8f, 1.35f, 6.6f), face = Vector3.back,
            titleEn = "Rajmudra — Royal Seal", titleMr = "राजमुद्रा",
            panelEn = "The royal seal of Shivaji Maharaj. Its Sanskrit verse likens the seal to the new moon's crescent, which grows day by day and is honoured by all. It authenticated official letters and orders. (Replica, shown enlarged.)",
            model = m =>
            {
                P(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.22f, 0.02f, 0.22f), "Bronze", new Vector3(90, 0, 0));
                P(PrimitiveType.Cylinder, m, new Vector3(0, 0, 0.06f), new Vector3(0.06f, 0.05f, 0.06f), "Bronze", new Vector3(90, 0, 0));
            },
        };
        yield return new Def
        {
            id = "A03", slug = "ShivraiHon", point = "H3_Coins", mount = Mount.Plinth, enlarged = true, spin = 30,
            pos = new Vector3(3.8f, 1.3f, 6.6f), face = Vector3.back,
            titleEn = "Shivrai Hon — Gold Coin", titleMr = "शिवराई होन",
            panelEn = "A small gold coin minted after the 1674 coronation, inscribed with the king's title. Issuing coinage was a public declaration of sovereignty. (Replica, shown enlarged.)",
            model = m => P(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.12f, 0.008f, 0.12f), "Gold", new Vector3(90, 0, 0)),
        };
        yield return new Def
        {
            id = "A04", slug = "ShivraiCopper", point = "H3_Coins", mount = Mount.Plinth, enlarged = true, spin = 30,
            pos = new Vector3(4.8f, 1.3f, 6.6f), face = Vector3.back,
            titleEn = "Shivrai — Copper Coin", titleMr = "शिवराई (तांब्याचे नाणे)",
            panelEn = "The copper Shivrai was the everyday coin of the Swarajya, used in markets across the kingdom, and stayed in circulation long after Shivaji Maharaj's reign. (Replica, shown enlarged.)",
            model = m => P(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.10f, 0.014f, 0.10f), "Copper", new Vector3(90, 0, 0)),
        };
        yield return Board("B07", "AshtaPradhan", "H3_BoardS", OnPartition(6.3f, 1.3f, -1), Vector3.left,
            "The Ashta Pradhan", "अष्टप्रधान मंडळ", "Council of Eight",
            "Shivaji Maharaj governed with a council of eight ministers, including the Peshwa (prime minister), Amatya (finance), Senapati (army) and Nyayadhish (justice). Officials were paid salaries instead of land grants, strengthening the state.");
        yield return new Def
        {
            id = "A16", slug = "SwarajyaMap", point = "H3_Map", mount = Mount.Wall, spin = 0, panelSide = 1.2f,
            pos = new Vector3(3.6f, 1.8f, 0.1f + 0.06f), face = Vector3.forward,
            titleEn = "Forts of the Swarajya", titleMr = "स्वराज्याचे किल्ले",
            replacesTitle = "Map of the Swarajya, c. 1680",
            panelEn = "The heartland of the Swarajya: the Sahyadri forts and the Konkan sea forts that Shivaji Maharaj took, built or rebuilt. By 1680 his kingdom also reached far to the south, to Jinji and Vellore. (Illustrative map.)",
            model = Picture("painting_map", 1.4f, 1.9f),
        };
        yield return Board("B09", "Legacy", "H3_BoardN", OnPartition(6.3f, 8.15f, -1), Vector3.left,
            "Legacy", "वारसा", "1680 and after",
            "Shivaji Maharaj died at Raigad in April 1680. The Swarajya he founded grew into the Maratha Empire, which in the next century spread across much of India. His forts, his system of government and his respect for all faiths remain his legacy.");
    }

    static Def Board(string id, string slug, string point, Vector3 pos, Vector3 face, string title, string titleMr, string era, string text) => new Def
    {
        id = id, slug = slug, point = point, mount = Mount.Board, spin = 0, panelSide = 1.25f,
        pos = pos, face = face, titleEn = title, titleMr = titleMr, era = era, panelEn = text,
        image = $"illus_{id}.png", model = StoryBoard(id),
    };

    static IEnumerable<Def> FortModels()
    {
        yield return new Def
        {
            id = "A12", slug = "RaigadModel", point = "FortsRoom", mount = Mount.Plinth, spin = 12, pos = new Vector3(22.7f, 1.15f, 2.55f), face = Vector3.forward,
            titleEn = "Raigad Fort", titleMr = "रायगड किल्ला",
            panelEn = "Raigad became Shivaji Maharaj's capital. His coronation as Chhatrapati was held here on 6 June 1674. (Scale model.)",
            model = m =>
            {
                P(PrimitiveType.Cube, m, new Vector3(0, -0.1f, 0), new Vector3(0.55f, 0.06f, 0.45f), "Hill_Green");
                P(PrimitiveType.Cube, m, new Vector3(0, -0.02f, 0), new Vector3(0.4f, 0.1f, 0.32f), "Fort_Stone");
                foreach (var o in new[] { new Vector2(0.18f, 0.14f), new Vector2(-0.18f, 0.14f), new Vector2(0.18f, -0.14f), new Vector2(-0.18f, -0.14f) })
                    P(PrimitiveType.Cylinder, m, new Vector3(o.x, 0.07f, o.y), new Vector3(0.06f, 0.06f, 0.06f), "Fort_Stone");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.08f, 0), new Vector3(0.12f, 0.1f, 0.1f), "Saffron");
            },
        };
        yield return new Def
        {
            id = "A13", slug = "SindhudurgModel", point = "FortsRoom", mount = Mount.Plinth, spin = 12, pos = new Vector3(23.5f, 1.15f, 2.55f), face = Vector3.forward,
            titleEn = "Sindhudurg Sea Fort", titleMr = "सिंधुदुर्ग जलदुर्ग",
            panelEn = "A sea fort built on an island off Malvan between 1664 and 1667 to guard the Konkan coast and the young Maratha navy. (Scale model.)",
            model = m =>
            {
                P(PrimitiveType.Cylinder, m, new Vector3(0, -0.12f, 0), new Vector3(0.58f, 0.01f, 0.58f), "Sea_Blue");
                P(PrimitiveType.Cylinder, m, new Vector3(0, -0.08f, 0), new Vector3(0.42f, 0.03f, 0.38f), "Fort_Stone");
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    P(PrimitiveType.Cylinder, m, new Vector3(Mathf.Cos(a) * 0.17f, 0f, Mathf.Sin(a) * 0.15f), new Vector3(0.06f, 0.07f, 0.06f), "Fort_Stone");
                }
            },
        };
        yield return new Def
        {
            id = "A14", slug = "Gurab", point = "FortsRoom", mount = Mount.Plinth, spin = 12, pos = new Vector3(24.3f, 1.15f, 2.55f), face = Vector3.forward,
            titleEn = "Gurab — Maratha Warship", titleMr = "गुराब (मराठा युद्धनौका)",
            panelEn = "Gurabs were broad-beamed, cannon-carrying warships. Shivaji Maharaj built up a navy of gurabs and smaller gallivats to protect the coast and its trade. (Scale model.)",
            model = m =>
            {
                P(PrimitiveType.Capsule, m, new Vector3(0, -0.06f, 0), new Vector3(0.14f, 0.27f, 0.12f), "Teak", new Vector3(90, 0, 0));
                P(PrimitiveType.Cylinder, m, new Vector3(0, 0.12f, 0), new Vector3(0.012f, 0.16f, 0.012f), "Teak");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.14f, 0.02f), new Vector3(0.004f, 0.2f, 0.18f), "Sail_Cloth", new Vector3(20, 0, 0));
            },
        };
    }
}
