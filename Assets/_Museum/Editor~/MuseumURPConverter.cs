using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// URP migration, step 2 of 2 (copied in by MuseumURPInstaller after URP is installed).
/// Creates a Cardboard-tuned mobile URP asset, assigns it to Graphics + every Quality level,
/// converts project materials from a pre-conversion snapshot, then re-queries saved state and
/// writes ProjectSettings/MuseumURPReport.txt. Re-run: Museum > Run URP Migration Again.
[InitializeOnLoad]
public static class MuseumURPConverter
{
    const string StateFile = "ProjectSettings/MuseumURPState.txt";
    const string ReportFile = "ProjectSettings/MuseumURPReport.txt";
    const string SettingsDir = "Assets/_Museum/Settings";
    const string AssetPath = SettingsDir + "/URP-Cardboard.asset";
    const string RendererPath = SettingsDir + "/URP-Cardboard_Renderer.asset";

    static MuseumURPConverter() => EditorApplication.delayCall += Run;

    [MenuItem("Museum/Run URP Migration Again")]
    static void RunAgain()
    {
        File.WriteAllText(StateFile, "installed");
        Run();
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (!File.Exists(StateFile) || File.ReadAllText(StateFile).Trim() != "installed") return;

        var report = new StringBuilder();
        try
        {
            var snapshots = Snapshot();                       // 1. before any change
            var urp = CreateOrLoadAsset(report);              // 2. URP asset + renderer
            Assign(urp);                                      // 3. Graphics + Quality
            ConvertMaterials(snapshots, report);              // 4. materials
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            bool ok = Verify(urp, snapshots, report);         // 5. re-query saved state
            File.WriteAllText(StateFile, ok ? "done" : "partial");
            report.Insert(0, ok ? "RESULT: PASS\n" : "RESULT: PARTIAL\n");
        }
        catch (System.Exception e)
        {
            report.Insert(0, "RESULT: ERROR " + e.Message + "\n");
            Debug.LogException(e);
            File.WriteAllText(StateFile, "error");
        }
        File.WriteAllText(ReportFile, report.ToString());
        Debug.Log("[Museum URP]\n" + report);
        EditorUtility.DisplayDialog("Shivaji Museum VR — URP", report.ToString().Split('\n')[0] +
            "\n\nDetails: ProjectSettings/MuseumURPReport.txt", "OK");
    }

    // ---------- 1. snapshot ----------
    class Snap { public string shader; public Texture main; public Vector2 scale = Vector2.one, offset; public Color color = Color.white;
                 public Texture normal, metallic, emission; public Color emissionColor = Color.black; public float cutoff = 0.5f; }

    static Dictionary<string, Snap> Snapshot()
    {
        var d = new Dictionary<string, Snap>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;
            d[path] = new Snap
            {
                shader = m.shader.name,
                main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null,
                scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one,
                offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero,
                color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white,
                normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null,
                metallic = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null,
                emission = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null,
                emissionColor = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black,
                cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f,
            };
        }
        return d;
    }

    // ---------- 2. URP asset ----------
    static UniversalRenderPipelineAsset CreateOrLoadAsset(StringBuilder r)
    {
        Directory.CreateDirectory(SettingsDir);
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
        if (asset == null)
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
            asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, AssetPath);
            r.AppendLine("Created " + AssetPath + " + renderer");
        }
        else r.AppendLine("Reused " + AssetPath);

        // Cardboard / mobile VR tuning: no HDR or screen textures, light MSAA, short shadows.
        asset.supportsHDR = false;
        asset.msaaSampleCount = 2;
        asset.renderScale = 1f;
        asset.shadowDistance = 20f;
        asset.supportsCameraDepthTexture = false;
        asset.supportsCameraOpaqueTexture = false;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    // ---------- 3. assignment ----------
    static void Assign(UniversalRenderPipelineAsset urp)
    {
        GraphicsSettings.defaultRenderPipeline = urp;
        int original = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(original, false);
        AssetDatabase.SaveAssets();
    }

    // ---------- 4. materials ----------
    static string Target(string src)
    {
        if (src.StartsWith("Universal Render Pipeline/") || src.StartsWith("TextMeshPro/") || src.StartsWith("Shader Graphs/")
            || src.StartsWith("GUI/") || src.StartsWith("UI/") || src.StartsWith("Sprites/") || src.StartsWith("Hidden/")) return null; // URP-compatible built-ins
        if (src == "Unlit/Texture" || src == "Unlit/Color" || src.StartsWith("Unlit/Transparent")) return "Universal Render Pipeline/Unlit";
        if (src.Contains("Particles")) return "Universal Render Pipeline/Particles/Unlit";
        if (src.StartsWith("Standard") || src.StartsWith("Legacy Shaders/") || src.StartsWith("Mobile/")) return "Universal Render Pipeline/Lit";
        return "UNRESOLVED";
    }

    static void ConvertMaterials(Dictionary<string, Snap> snaps, StringBuilder r)
    {
        foreach (var kv in snaps)
        {
            var target = Target(kv.Value.shader);
            if (target == null) continue;
            if (target == "UNRESOLVED") { r.AppendLine($"UNRESOLVED shader '{kv.Value.shader}': {kv.Key}"); continue; }

            var m = AssetDatabase.LoadAssetAtPath<Material>(kv.Key);
            var s = kv.Value;
            m.shader = Shader.Find(target);
            if (m.HasProperty("_BaseMap") && s.main != null && s.shader != "Unlit/Color")
            {
                m.SetTexture("_BaseMap", s.main);
                m.SetTextureScale("_BaseMap", s.scale);
                m.SetTextureOffset("_BaseMap", s.offset);
            }
            // Unlit/Texture ignores _Color, so keep the URP tint white to match the old look.
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", s.shader == "Unlit/Texture" ? Color.white : s.color);
            if (s.normal != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", s.normal); m.EnableKeyword("_NORMALMAP"); }
            if (s.metallic != null && m.HasProperty("_MetallicGlossMap")) m.SetTexture("_MetallicGlossMap", s.metallic);
            if (s.emission != null && m.HasProperty("_EmissionMap")) { m.SetTexture("_EmissionMap", s.emission); m.SetColor("_EmissionColor", s.emissionColor); m.EnableKeyword("_EMISSION"); }
            if (s.shader.Contains("Cutout") && m.HasProperty("_AlphaClip")) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", s.cutoff); m.EnableKeyword("_ALPHATEST_ON"); }
            EditorUtility.SetDirty(m);
        }
    }

    // ---------- 5. verify saved state ----------
    static bool Verify(UniversalRenderPipelineAsset urp, Dictionary<string, Snap> snaps, StringBuilder r)
    {
        bool ok = true;
        var g = GraphicsSettings.defaultRenderPipeline;
        r.AppendLine("Graphics: " + (g == urp ? "URP-Cardboard" : "WRONG " + (g ? g.name : "null")));
        ok &= g == urp;

        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            var a = QualitySettings.GetRenderPipelineAssetAt(i);
            r.AppendLine($"Quality {i} {QualitySettings.names[i]}: " + (a == urp ? "URP-Cardboard" : "WRONG"));
            ok &= a == urp;
        }

        var so = new SerializedObject(urp);
        var list = so.FindProperty("m_RendererDataList");
        int idx = so.FindProperty("m_DefaultRendererIndex").intValue;
        bool rendererOk = list != null && list.arraySize > idx && list.GetArrayElementAtIndex(idx).objectReferenceValue != null;
        r.AppendLine($"Default renderer: {(rendererOk ? list.GetArrayElementAtIndex(idx).objectReferenceValue.name : "MISSING")}");
        r.AppendLine($"Asset: HDR={urp.supportsHDR} MSAA={urp.msaaSampleCount} scale={urp.renderScale} shadowDist={urp.shadowDistance}");
        ok &= rendererOk;

        r.AppendLine("Materials:");
        foreach (var kv in snaps)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(kv.Key);
            var expected = Target(kv.Value.shader);
            string now = m.shader ? m.shader.name : "null";
            if (expected == null) { r.AppendLine($"  skip  {now}  {Path.GetFileName(kv.Key)}"); continue; }
            bool shaderOk = now == expected;
            bool mapOk = kv.Value.main == null || kv.Value.shader == "Unlit/Color" || !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == kv.Value.main;
            r.AppendLine($"  {(shaderOk && mapOk ? "ok  " : "FAIL")}  {kv.Value.shader} -> {now}  BaseMap={(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "none")}  {Path.GetFileName(kv.Key)}");
            ok &= shaderOk && mapOk;
        }
        return ok;
    }
}
