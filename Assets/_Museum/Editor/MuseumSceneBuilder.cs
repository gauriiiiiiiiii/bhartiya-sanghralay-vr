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

/// Generates Assets/_Museum/Scenes/Museum.unity from the gallery floor plan
/// (1 unit = 1 m; origin = gallery south-west corner; +X east, +Z north).
///
///   Gallery 18.9 x 9.45 m (62' x 31'), ceiling 3.96 m (13'). East wing x 18.9-25:
///   entrance foyer + title wall (north), Armour Room and Forts Room (south).
///   Storage, restrooms, hall, utility and AC are not built.
///
/// Every artifact is a placeholder "Model" child built from primitives: replace it with the real
/// 3D model and keep the parent (it holds the Artifact component, collider, label and panel anchor).
/// Runs once automatically after the URP migration; rebuild from Museum > Rebuild Museum Scene.
[InitializeOnLoad]
public static class MuseumSceneBuilder
{
    const string StateFile = "ProjectSettings/MuseumSceneState.txt";
    const string ReportFile = "ProjectSettings/MuseumSceneReport.txt";
    const string ScenePath = "Assets/_Museum/Scenes/Museum.unity";
    const string MatDir = "Assets/_Museum/Materials";
    const string TexDir = "Assets/_Museum/Textures";
    const string DataDir = "Assets/_Museum/Data";
    const float H = 3.96f, T = 0.2f, DoorH = 2.4f;

    static int interactiveLayer, teleportLayer;
    static readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
    static double waitStart;

    // ------------------------------------------------------------------ entry points
    static MuseumSceneBuilder() => EditorApplication.delayCall += () =>
    {
        if (File.Exists(StateFile)) return;
        waitStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += WaitForUrp;
    };

    static void WaitForUrp()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var urp = File.Exists("ProjectSettings/MuseumURPState.txt") ? File.ReadAllText("ProjectSettings/MuseumURPState.txt").Trim() : "";
        if (urp == "done" || urp == "partial")
        {
            EditorApplication.update -= WaitForUrp;
            if (!File.Exists(StateFile)) Build();
        }
        else if (EditorApplication.timeSinceStartup - waitStart > 120)
        {
            EditorApplication.update -= WaitForUrp;
            Debug.LogWarning("[Museum] URP migration not finished; build the scene later from Museum > Rebuild Museum Scene.");
        }
    }

    [MenuItem("Museum/Rebuild Museum Scene (overwrites Museum.unity)")]
    static void Rebuild()
    {
        if (EditorUtility.DisplayDialog("Rebuild museum scene",
            "Regenerates Museum.unity from the floor plan. Changes made inside that scene are lost.\n" +
            "Artifact data (text, audio) in Assets/_Museum/Data is kept.", "Rebuild", "Cancel"))
            Build();
    }

    // ------------------------------------------------------------------ build
    static void Build()
    {
        interactiveLayer = LayerMask.NameToLayer("Interactive");
        teleportLayer = LayerMask.NameToLayer("Teleport");
        if (interactiveLayer < 0 || teleportLayer < 0)
        {
            Debug.LogError("[Museum] Layers 'Interactive' and 'Teleport' are missing. Run Museum > Run Project Setup Again.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        foreach (var d in new[] { MatDir, TexDir, DataDir, "Assets/_Museum/Scenes" }) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();

        var report = new StringBuilder();
        try
        {
            CreateMaterials();
            var ring = MakeCircleSprite("Reticle", 0.62f);
            var disc = MakeCircleSprite("TeleportDisc", 0f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Museum").transform;

            BuildArchitecture(Group(root, "Architecture"));
            BuildLighting(Group(root, "Lighting"));
            var points = BuildTeleportPoints(Group(root, "TeleportPoints"), disc);
            BuildPlayer(points["Title"], ring);
            var panel = BuildInfoPanel(root);
            var artifacts = BuildArtifacts(Group(root, "Artifacts"), points);

            points["Exit"].destination = points["Title"].transform;
            panel.tour = TourOrder.Select(id => artifacts[id]).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("Could not save " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            report.AppendLine("RESULT: BUILT " + ScenePath);
            report.AppendLine($"Artifacts: {artifacts.Count}  Teleport points: {points.Count}");
            report.AppendLine("Tour: " + string.Join(" > ", TourOrder));
            report.AppendLine("Build Settings scenes: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));
            report.AppendLine("Not built (per brief): Storage, Restroom x2, Hall, Utility, AC");
            report.AppendLine("Placeholders: artifact models are primitives; no narration audio or images yet; no Devanagari font yet.");
            File.WriteAllText(StateFile, "done");
        }
        catch (Exception e)
        {
            report.Insert(0, "RESULT: ERROR " + e.Message + "\n");
            Debug.LogException(e);
        }
        File.WriteAllText(ReportFile, report.ToString());
        Debug.Log("[Museum] Scene build\n" + report);
        EditorUtility.DisplayDialog("Shivaji Museum VR — scene", report.ToString(), "OK");
    }

    // ------------------------------------------------------------------ materials & textures
    static void CreateMaterials()
    {
        Lit("Wall_Basalt", new Color(0.36f, 0.32f, 0.29f));
        Lit("Floor_Kota", new Color(0.52f, 0.53f, 0.5f), 0f, 0.45f);
        Lit("Ceiling_Plaster", new Color(0.86f, 0.83f, 0.77f));
        Lit("Teak", new Color(0.40f, 0.24f, 0.12f), 0f, 0.35f);
        Lit("Door_Wood", new Color(0.30f, 0.17f, 0.08f), 0f, 0.3f);
        Lit("Saffron", new Color(1f, 0.53f, 0.08f));
        Lit("Stone_Plinth", new Color(0.78f, 0.74f, 0.67f));
        Lit("Gold", new Color(0.95f, 0.74f, 0.30f), 1f, 0.65f);
        Lit("Steel", new Color(0.62f, 0.64f, 0.67f), 0.9f, 0.6f);
        Lit("Steel_Dark", new Color(0.35f, 0.36f, 0.38f), 0.9f, 0.45f);
        Lit("Bronze", new Color(0.55f, 0.37f, 0.20f), 0.85f, 0.5f);
        Lit("Copper", new Color(0.72f, 0.42f, 0.25f), 0.85f, 0.45f);
        Lit("Hide", new Color(0.42f, 0.27f, 0.15f));
        Lit("Fort_Stone", new Color(0.50f, 0.46f, 0.40f));
        Lit("Hill_Green", new Color(0.33f, 0.40f, 0.24f));
        Lit("Sea_Blue", new Color(0.18f, 0.38f, 0.52f), 0f, 0.7f);
        Lit("Sail_Cloth", new Color(0.92f, 0.87f, 0.76f));
        Lit("Painting_Portrait", new Color(0.62f, 0.38f, 0.18f));
        Lit("Painting_Coronation", new Color(0.55f, 0.13f, 0.10f));
        Lit("Map_Parchment", new Color(0.86f, 0.78f, 0.60f));
    }

    static Material Lit(string name, Color c, float metallic = 0f, float smooth = 0.2f)
    {
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
        return M[name] = m;
    }

    static Material SpriteMat(string name, Texture tex, Color c)
    {
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(m, path); }
        m.mainTexture = tex;
        m.color = c;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// inner = 0 makes a soft filled disc with a bright rim; inner > 0 makes a ring.
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
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float outerEdge = Mathf.Clamp01((0.98f - r) * n * 0.5f);
                    float a = inner > 0f
                        ? outerEdge * Mathf.Clamp01((r - inner) * n * 0.5f)
                        : outerEdge * Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0.55f, 0.95f, r));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
        if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    /// A wall between two floor points (x,z), from height y0 to y1.
    static void Wall(string name, Vector2 a, Vector2 b, Transform parent, Material m = null, float y0 = 0f, float y1 = H)
    {
        var d = b - a;
        var mid = (a + b) * 0.5f;
        float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        Box(name, new Vector3(mid.x, (y0 + y1) * 0.5f, mid.y), new Vector3(T, y1 - y0, d.magnitude + T), m ?? M["Wall_Basalt"], parent, yaw);
    }

    /// A wall with a doorway from distance g0 to g1 along a->b (clear width is ~0.2 m less).
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
        t.text = text;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.rectTransform.sizeDelta = size;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.05f;
        t.fontSizeMax = 30f;
        if (bold) t.fontStyle = FontStyles.Bold;
        return t;
    }

    // ------------------------------------------------------------------ architecture
    static void BuildArchitecture(Transform p)
    {
        Box("Floor", new Vector3(12.5f, -0.05f, 4.725f), new Vector3(25f, 0.1f, 9.45f), M["Floor_Kota"], p);
        Box("Ceiling", new Vector3(12.5f, H + 0.05f, 4.725f), new Vector3(25f, 0.1f, 9.45f), M["Ceiling_Plaster"], p);

        // Outer shell
        Wall("Wall_South", new Vector2(0, 0), new Vector2(18.9f, 0), p);
        Wall("Wall_West", new Vector2(0, 0), new Vector2(0, 9.45f), p);
        Wall("Wall_North", new Vector2(0, 9.45f), new Vector2(25f, 9.45f), p);
        Wall("Wall_East", new Vector2(25f, 1.85f), new Vector2(25f, 9.45f), p);
        Wall("Wall_GalleryEast", new Vector2(18.9f, 0), new Vector2(18.9f, 1.85f), p);   // closes the removed storage/restroom side

        // East wing: Armour Room (Office 1) and Forts Room (Office 2) under the title wall
        WallWithDoor("Wall_ArmourRoomWest", new Vector2(18.9f, 1.85f), new Vector2(18.9f, 6.75f), 2.3f, 3.9f, p);
        Wall("Wall_RoomsSouth", new Vector2(18.9f, 1.85f), new Vector2(25f, 1.85f), p);
        Wall("Wall_RoomsPartition", new Vector2(21.95f, 1.85f), new Vector2(21.95f, 6.75f), p);
        WallWithDoor("TitleWall", new Vector2(18.9f, 6.75f), new Vector2(25f, 6.75f), 4.4f, 6.0f, p);

        // Angled spot-lit display wall (dashed on the plan), 3.7 m at 45 degrees
        Wall("AngledDisplayWall", new Vector2(15.5f, 7.0f), new Vector2(18.1f, 4.4f), p, M["Teak"], 0f, 3.0f);

        // Moving Wall A (8' high x 6' wide) and Moving Wall B (8' x 8' island dais)
        Box("MovingWall_A", new Vector3(1.5f, 1.22f, 4.415f), new Vector3(0.2f, 2.44f, 1.83f), M["Teak"], p);
        Box("MovingWall_B_Dais", new Vector3(12f, 0.1f, 3f), new Vector3(2.44f, 0.2f, 2.44f), M["Stone_Plinth"], p);

        // Doors (decorative; the west doors are the exit hotspot)
        Box("Door_WestDouble", new Vector3(0.12f, 1.2f, 7.7f), new Vector3(0.05f, 2.4f, 1.8f), M["Door_Wood"], p);
        Box("Door_Entrance", new Vector3(23.5f, 1.2f, 9.33f), new Vector3(1.2f, 2.4f, 0.05f), M["Door_Wood"], p);

        // Title wall dressing (faces north, toward the entrance)
        Box("Banner_Left", new Vector3(19.6f, 2.3f, 6.87f), new Vector3(0.7f, 2.6f, 0.02f), M["Saffron"], p);
        Box("Banner_Right", new Vector3(22.4f, 2.3f, 6.87f), new Vector3(0.7f, 2.6f, 0.02f), M["Saffron"], p);
        Text3D("TitleText", "Chhatrapati Shivaji Maharaj\n<size=60%>Hindavi Swarajya</size>",
            new Vector3(21f, 3.25f, 6.88f), Vector3.forward, new Vector2(3.6f, 0.9f), new Color(1f, 0.85f, 0.55f), p, true);
        Text3D("RoomLabel_Armour", "Armour Room", new Vector3(19.65f, 2.75f, 6.64f), Vector3.back, new Vector2(1.6f, 0.25f), Color.white, p);
    }

    static void BuildLighting(Transform p)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.50f, 0.45f);
        RenderSettings.ambientEquatorColor = new Color(0.42f, 0.38f, 0.33f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.20f, 0.17f);
        RenderSettings.fog = false;

        var sun = NewLight("Fill_Directional", LightType.Directional, Vector3.zero, p);
        sun.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
        sun.intensity = 0.55f;

        var warm = new Color(1f, 0.88f, 0.72f);
        foreach (var (name, pos) in new[]
        {
            ("Point_GalleryWest", new Vector3(5f, 3.5f, 4.7f)),
            ("Point_GalleryEast", new Vector3(13f, 3.5f, 4.7f)),
            ("Point_Foyer", new Vector3(22f, 3.5f, 8.1f)),
            ("Point_ArmourRoom", new Vector3(20.4f, 3.4f, 4.3f)),
            ("Point_FortsRoom", new Vector3(23.5f, 3.4f, 4.3f)),
        })
        {
            var l = NewLight(name, LightType.Point, pos, p);
            l.range = 9f; l.intensity = 1.3f; l.color = warm;
        }

        var spot = NewLight("Spot_AngledWall", LightType.Spot, new Vector3(15.4f, 3.6f, 4.9f), p);
        spot.transform.LookAt(new Vector3(16.8f, 1.4f, 5.7f));
        spot.range = 6f; spot.spotAngle = 70f; spot.intensity = 3f; spot.color = warm;
    }

    static Light NewLight(string name, LightType type, Vector3 pos, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.shadows = LightShadows.None;      // mobile VR budget: no real-time shadows
        return l;
    }

    // ------------------------------------------------------------------ teleport points
    static Dictionary<string, TeleportPoint> BuildTeleportPoints(Transform p, Sprite disc)
    {
        var mat = SpriteMat("TeleportDisc", disc.texture, new Color(1f, 0.55f, 0.1f, 0.6f));
        var pts = new Dictionary<string, TeleportPoint>();
        void Add(string name, float x, float z, float yaw) => pts[name] = MakePoint(name, x, z, yaw, mat, p);

        Add("Title", 21.0f, 8.4f, 180f);        // spawn: entrance foyer, facing the title wall
        Add("AngledWall", 15.3f, 4.2f, 45f);
        Add("ArmourRoom", 19.75f, 4.95f, 90f);
        Add("FortsRoom", 23.5f, 5.0f, 180f);
        Add("Island", 12.0f, 5.2f, 180f);
        Add("NorthPainting", 8.0f, 7.3f, 0f);
        Add("SouthMap", 6.0f, 2.2f, 180f);
        Add("GalleryCentre", 9.0f, 4.7f, 90f);
        Add("WeaponsWall", 3.6f, 4.42f, 270f);
        Add("Exit", 1.0f, 7.7f, 270f);          // destination set to Title after build
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
        if (name == "Exit") Text3D("ExitLabel", "Exit to entrance", go.transform.position + new Vector3(0f, 0.25f, 0f) + Vector3.right * 0.5f,
            Vector3.right, new Vector2(1.2f, 0.18f), Color.white, go.transform);
        return tp;
    }

    // ------------------------------------------------------------------ player rig
    static void BuildPlayer(TeleportPoint spawn, Sprite ring)
    {
        var player = new GameObject("Player");
        player.transform.SetPositionAndRotation(new Vector3(spawn.transform.position.x, 0f, spawn.transform.position.z), spawn.transform.rotation);
        player.AddComponent<MuseumCardboardRuntime>();

        var look = new GameObject("EditorLook");                // mouse look in the Editor only
        look.transform.SetParent(player.transform, false);
        look.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        look.AddComponent<EditorMouseLook>();

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(look.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.07f, 0.06f);
        camGo.AddComponent<AudioListener>();

        var tpd = camGo.AddComponent<TrackedPoseDriver>();   // head rotation from the phone (3DoF)
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationOnly;
        tpd.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.Value,
            "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        tpd.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.Value,
            "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));

        // Reticle: ring quad + radial dwell fill
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
        fill.sprite = ring;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillAmount = 0f;
        fill.color = new Color(1f, 0.6f, 0.15f);

        var gaze = camGo.AddComponent<GazeInteractor>();
        gaze.gazeMask = (1 << 0) | (1 << interactiveLayer) | (1 << teleportLayer);   // Default blocks rays through walls
        gaze.reticle = reticle;
        gaze.dwellFill = fill;

        // Fade-to-black for teleports (world space; screen overlays don't render in Cardboard)
        var fadeCanvas = NewCanvas("FadeCanvas", camGo.transform, new Vector2(400, 400), 0.005f);
        fadeCanvas.transform.localPosition = new Vector3(0f, 0f, 0.3f);
        UIChild<Image>("Black", fadeCanvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).color = Color.black;
        var group = fadeCanvas.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        var tele = player.AddComponent<PlayerTeleporter>();
        tele.playerRig = player.transform;
        tele.headCamera = camGo.transform;
        tele.fade = group;
    }

    static Canvas NewCanvas(string name, Transform parent, Vector2 size, float scale)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * scale;
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
        narration.playOnAwake = false;
        narration.spatialBlend = 0f;

        var canvas = NewCanvas("Content", go.transform, new Vector2(900, 600), 0.001f);
        var ct = canvas.transform;
        UIChild<Image>("Background", ct, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).color = new Color(0.12f, 0.07f, 0.04f, 0.94f);
        UIChild<Image>("TopBar", ct, new Vector2(0, 1), Vector2.one, new Vector2(0, -12), Vector2.zero).color = new Color(1f, 0.53f, 0.08f);

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
        mrImage.preserveAspect = true;
        mrImage.gameObject.SetActive(false);

        var ip = go.AddComponent<InfoPanel>();
        ip.root = canvas.gameObject;
        ip.titleText = title; ip.eraText = era; ip.bodyText = body;
        ip.picture = picture; ip.marathiRenderedImage = mrImage;
        ip.latinFont = TMP_Settings.defaultFontAsset;
        ip.narration = narration;

        float x = 30;
        ip.playLabel = GazeButton(ct, "Btn_PlayPause", "Pause", ref x, ip.TogglePlay);
        GazeButton(ct, "Btn_Close", "Close", ref x, ip.Close);
        GazeButton(ct, "Btn_Next", "Next >", ref x, ip.Next);
        ip.languageLabel = GazeButton(ct, "Btn_Language", "Marathi", ref x, ip.ToggleLanguage);
        return ip;
    }

    static void Style(TMP_Text t, float size, Color c, FontStyles style, TextAlignmentOptions align)
    {
        t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = align;
    }

    static TextMeshProUGUI GazeButton(Transform canvas, string name, string label, ref float x, UnityAction action)
    {
        const float w = 195, h = 70;
        var bg = UIChild<Image>(name, canvas, Vector2.zero, Vector2.zero, new Vector2(x, 25), new Vector2(x + w, 25 + h));
        x += w + 20;
        bg.gameObject.layer = interactiveLayer;
        var col = bg.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(w / 2f, h / 2f, 0f);
        col.size = new Vector3(w, h, 20f);
        // RectTransform pivot is centre by default; keep the collider centred on the button
        col.center = Vector3.zero;

        var gb = bg.gameObject.AddComponent<GazeButton>();
        gb.background = bg;
        bg.color = gb.normalColor;
        if (gb.onSelect == null) gb.onSelect = new UnityEvent();
        UnityEventTools.AddPersistentListener(gb.onSelect, action);

        var text = UIChild<TextMeshProUGUI>("Label", bg.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Style(text, 30, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        text.text = label;
        text.raycastTarget = false;
        return text;
    }

    // ------------------------------------------------------------------ artifacts
    static readonly string[] TourOrder =
        { "A01", "A02", "A03", "A04", "A09", "A10", "A11", "A12", "A13", "A14", "A08", "A15", "A16", "A05", "A06", "A07" };

    enum Mount { Plinth, Wall }

    class Def
    {
        public string id, slug, titleEn, titleMr, panelEn, point;
        public Vector3 pos, face;
        public Mount mount;
        public bool enlarged;
        public float spin = 20f, panelSide = 0.95f;
        public Action<Transform> model;
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

            // Collider around the model (rotation-safe: square footprint)
            var b = new Bounds(d.pos, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            float foot = Mathf.Max(b.size.x, b.size.z) + 0.1f;
            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(b.center);
            col.size = new Vector3(foot, b.size.y + 0.1f, d.mount == Mount.Wall ? Mathf.Max(0.12f, Mathf.Min(b.size.x, b.size.z) + 0.1f) : foot);

            // Mount + label
            if (d.mount == Mount.Plinth)
            {
                float top = d.pos.y - 0.3f;
                Box("Plinth", new Vector3(d.pos.x, top / 2f, d.pos.z), new Vector3(0.62f, top, 0.62f), M["Stone_Plinth"], go.transform.parent, Quaternion.LookRotation(-d.face).eulerAngles.y);
                Text3D("Label", d.titleEn, new Vector3(d.pos.x, top - 0.12f, d.pos.z) + d.face * 0.32f, d.face, new Vector2(0.56f, 0.1f), Color.black, go.transform);
            }
            else
            {
                Text3D("Label", d.titleEn, d.pos + Vector3.down * (b.extents.y + 0.12f) + d.face * 0.04f, d.face, new Vector2(0.9f, 0.1f), Color.white, go.transform);
            }

            // Info panel anchor: beside the exhibit, on the side nearer the visitor's stand point
            var stand = points[d.point];
            var side = Vector3.Cross(Vector3.up, d.face).normalized;
            var c1 = d.pos + side * d.panelSide; var c2 = d.pos - side * d.panelSide;
            var sp = stand.transform.position;
            var chosen = (Flat(c1) - Flat(sp)).sqrMagnitude <= (Flat(c2) - Flat(sp)).sqrMagnitude ? c1 : c2;
            var anchor = new GameObject("PanelAnchor").transform;
            anchor.SetParent(go.transform, false);
            anchor.SetPositionAndRotation(new Vector3(chosen.x, 1.55f, chosen.z) + d.face * 0.35f, Quaternion.LookRotation(-d.face));

            var art = go.AddComponent<Artifact>();
            art.data = data;
            art.model = model;
            art.panelAnchor = anchor;
            art.standPoint = stand;
            art.spinSpeed = d.mount == Mount.Wall && !d.enlarged ? 0f : d.spin;
            result[d.id] = art;
        }
        return result;
    }

    static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

    static ArtifactData LoadOrCreateData(Def d)
    {
        var path = $"{DataDir}/{d.id}_{d.slug}.asset";
        var data = AssetDatabase.LoadAssetAtPath<ArtifactData>(path);
        if (data != null) return data;                       // keep any text/audio you have added
        data = ScriptableObject.CreateInstance<ArtifactData>();
        data.id = d.id;
        data.titleEn = d.titleEn;
        data.titleMr = d.titleMr;
        data.panelEn = d.panelEn;
        data.panelMr = "(मराठी मजकूर लवकरच)";
        data.shownEnlarged = d.enlarged;
        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    // Primitive helper for placeholder models (local space: -Z faces the visitor)
    static Transform P(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, string mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = M[mat];
        return go.transform;
    }

    static Action<Transform> Painting(float w, float h, string mat) => m =>
    {
        P(PrimitiveType.Cube, m, Vector3.zero, new Vector3(w + 0.16f, h + 0.16f, 0.05f), "Teak");
        P(PrimitiveType.Quad, m, new Vector3(0, 0, -0.03f), new Vector3(w, h, 1f), mat);
    };

    static IEnumerable<Def> Defs()
    {
        var n = new Vector3(-0.7071f, 0f, -0.7071f);          // angled wall faces the gallery (south-west)
        Vector3 OnAngled(float t) => new Vector3(Mathf.Lerp(15.5f, 18.1f, t), 1.45f, Mathf.Lerp(7.0f, 4.4f, t)) + n * 0.3f;

        yield return new Def
        {
            id = "A01", slug = "Portrait", point = "Title", mount = Mount.Wall, spin = 0, panelSide = 1.25f,
            pos = new Vector3(21f, 1.75f, 6.89f), face = Vector3.forward,
            titleEn = "Portrait of Chhatrapati Shivaji Maharaj", titleMr = "छत्रपती शिवाजी महाराजांचे चित्र",
            panelEn = "Chhatrapati Shivaji Maharaj (1630–1680) founded Hindavi Swarajya, the Maratha kingdom of the Deccan, and was crowned at Raigad in 1674. This gallery follows his life through the arms, coins, forts and ships of his era. (Replica portrait.)",
            model = Painting(1.0f, 1.3f, "Painting_Portrait"),
        };
        yield return new Def
        {
            id = "A02", slug = "Rajmudra", point = "AngledWall", mount = Mount.Wall, enlarged = true, spin = 25,
            pos = OnAngled(0.25f), face = n,
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
            id = "A03", slug = "ShivraiHon", point = "AngledWall", mount = Mount.Wall, enlarged = true, spin = 30,
            pos = OnAngled(0.5f), face = n,
            titleEn = "Shivrai Hon — Gold Coin", titleMr = "शिवराई होन",
            panelEn = "A small gold coin minted after the 1674 coronation, inscribed with the king's title. Issuing coinage was a public declaration of sovereignty. (Replica, shown enlarged.)",
            model = m => P(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.12f, 0.008f, 0.12f), "Gold", new Vector3(90, 0, 0)),
        };
        yield return new Def
        {
            id = "A04", slug = "ShivraiCopper", point = "AngledWall", mount = Mount.Wall, enlarged = true, spin = 30,
            pos = OnAngled(0.75f), face = n,
            titleEn = "Shivrai — Copper Coin", titleMr = "शिवराई (तांब्याचे नाणे)",
            panelEn = "The copper Shivrai was the everyday coin of the Swarajya, used in markets across the kingdom, and stayed in circulation long after Shivaji Maharaj's reign. (Replica, shown enlarged.)",
            model = m => P(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.10f, 0.014f, 0.10f), "Copper", new Vector3(90, 0, 0)),
        };
        yield return new Def
        {
            id = "A05", slug = "BhavaniTalwar", point = "WeaponsWall", mount = Mount.Wall, spin = 0,
            pos = new Vector3(1.72f, 1.45f, 3.85f), face = Vector3.right,
            titleEn = "Bhavani Talwar — Sword", titleMr = "भवानी तलवार",
            panelEn = "By tradition, Shivaji Maharaj's sword was named after the goddess Bhavani. Its history and present whereabouts are debated by historians; this is a representative sword of the period. (Replica.)",
            model = m =>
            {
                var s = new GameObject("Sword").transform; s.SetParent(m, false); s.localRotation = Quaternion.Euler(0, 0, 20);
                P(PrimitiveType.Cube, s, new Vector3(0, 0.1f, 0), new Vector3(0.04f, 0.9f, 0.008f), "Steel");
                P(PrimitiveType.Cube, s, new Vector3(0, -0.36f, 0), new Vector3(0.18f, 0.03f, 0.04f), "Gold");
                P(PrimitiveType.Cylinder, s, new Vector3(0, -0.43f, 0), new Vector3(0.035f, 0.05f, 0.035f), "Gold");
            },
        };
        yield return new Def
        {
            id = "A06", slug = "Dhal", point = "WeaponsWall", mount = Mount.Wall, spin = 0,
            pos = new Vector3(1.72f, 1.45f, 4.42f), face = Vector3.right,
            titleEn = "Dhal — Shield", titleMr = "ढाल",
            panelEn = "A round shield of hardened hide or metal with raised bosses, carried by Maratha infantry and cavalry alongside the sword. (Replica.)",
            model = m =>
            {
                P(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.5f, 0.03f, 0.5f), "Hide", new Vector3(90, 0, 0));
                foreach (var o in new[] { new Vector2(0.08f, 0.08f), new Vector2(-0.08f, 0.08f), new Vector2(0.08f, -0.08f), new Vector2(-0.08f, -0.08f) })
                    P(PrimitiveType.Sphere, m, new Vector3(o.x, o.y, -0.03f), Vector3.one * 0.05f, "Steel");
            },
        };
        yield return new Def
        {
            id = "A07", slug = "Dandpatta", point = "WeaponsWall", mount = Mount.Wall, spin = 0,
            pos = new Vector3(1.72f, 1.45f, 4.98f), face = Vector3.right,
            titleEn = "Dandpatta — Gauntlet Sword", titleMr = "दांडपट्टा",
            panelEn = "A long, flexible blade fixed to an armoured gauntlet that covers the hand and forearm, closely associated with Maratha warriors. (Replica.)",
            model = m =>
            {
                P(PrimitiveType.Capsule, m, new Vector3(0, -0.3f, 0), new Vector3(0.12f, 0.12f, 0.12f), "Steel_Dark");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.15f, 0), new Vector3(0.04f, 0.8f, 0.006f), "Steel");
            },
        };
        yield return new Def
        {
            id = "A08", slug = "WaghNakh", point = "Island", mount = Mount.Plinth, enlarged = true, spin = 20,
            pos = new Vector3(12f, 1.25f, 3f), face = Vector3.forward,
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
            id = "A09", slug = "Jiretop", point = "ArmourRoom", mount = Mount.Plinth, spin = 20,
            pos = new Vector3(21.3f, 1.3f, 3.3f), face = Vector3.left,
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
            id = "A10", slug = "Chilkhat", point = "ArmourRoom", mount = Mount.Plinth, spin = 20,
            pos = new Vector3(21.3f, 1.4f, 4.6f), face = Vector3.left,
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
            id = "A11", slug = "Bichwa", point = "ArmourRoom", mount = Mount.Plinth, enlarged = true, spin = 20,
            pos = new Vector3(21.3f, 1.25f, 5.9f), face = Vector3.left,
            titleEn = "Bichwa — Dagger", titleMr = "बिचवा",
            panelEn = "A curved dagger named after the scorpion's sting, easy to conceal and used at close quarters. (Replica, shown enlarged.)",
            model = m =>
            {
                P(PrimitiveType.Cube, m, new Vector3(0, 0.08f, 0), new Vector3(0.04f, 0.28f, 0.008f), "Steel", new Vector3(0, 0, 15));
                P(PrimitiveType.Cube, m, new Vector3(0.03f, -0.12f, 0), new Vector3(0.04f, 0.14f, 0.04f), "Teak", new Vector3(0, 0, 15));
            },
        };
        yield return new Def
        {
            id = "A12", slug = "RaigadModel", point = "FortsRoom", mount = Mount.Plinth, spin = 12,
            pos = new Vector3(22.7f, 1.15f, 2.55f), face = Vector3.forward,
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
            id = "A13", slug = "SindhudurgModel", point = "FortsRoom", mount = Mount.Plinth, spin = 12,
            pos = new Vector3(23.5f, 1.15f, 2.55f), face = Vector3.forward,
            titleEn = "Sindhudurg Sea Fort", titleMr = "सिंधुदुर्ग किल्ला",
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
            id = "A14", slug = "Gurab", point = "FortsRoom", mount = Mount.Plinth, spin = 12,
            pos = new Vector3(24.3f, 1.15f, 2.55f), face = Vector3.forward,
            titleEn = "Gurab — Maratha Warship", titleMr = "गुराब (मराठा युद्धनौका)",
            panelEn = "Gurabs were broad-beamed, cannon-carrying warships. Shivaji Maharaj built up a navy of gurabs and smaller gallivats to protect the coast and its trade. (Scale model.)",
            model = m =>
            {
                P(PrimitiveType.Capsule, m, new Vector3(0, -0.06f, 0), new Vector3(0.14f, 0.27f, 0.12f), "Teak", new Vector3(90, 0, 0));
                P(PrimitiveType.Cylinder, m, new Vector3(0, 0.12f, 0), new Vector3(0.012f, 0.16f, 0.012f), "Teak");
                P(PrimitiveType.Cube, m, new Vector3(0, 0.14f, 0.02f), new Vector3(0.004f, 0.2f, 0.18f), "Sail_Cloth", new Vector3(20, 0, 0));
            },
        };
        yield return new Def
        {
            id = "A15", slug = "Rajyabhishek", point = "NorthPainting", mount = Mount.Wall, spin = 0, panelSide = 1.8f,
            pos = new Vector3(8f, 1.85f, 9.31f), face = Vector3.back,
            titleEn = "Rajyabhishek — The Coronation", titleMr = "राज्याभिषेक",
            panelEn = "The coronation of Shivaji Maharaj as Chhatrapati at Raigad in June 1674, attended by court, clergy and envoys. (Artistic reconstruction.)",
            model = Painting(2.4f, 1.6f, "Painting_Coronation"),
        };
        yield return new Def
        {
            id = "A16", slug = "SwarajyaMap", point = "SouthMap", mount = Mount.Wall, spin = 0, panelSide = 1.6f,
            pos = new Vector3(6f, 1.75f, 0.14f), face = Vector3.forward,
            titleEn = "Map of the Swarajya, c. 1680", titleMr = "स्वराज्याचा नकाशा",
            panelEn = "The approximate extent of the Swarajya around 1680, with its principal forts along the Sahyadri hills and the Konkan coast. (Illustrative map.)",
            model = Painting(2.0f, 1.4f, "Map_Parchment"),
        };
    }
}
