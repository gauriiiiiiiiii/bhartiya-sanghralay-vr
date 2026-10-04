using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Adds Marathi to the museum:
///   - Imports Assets/_Museum/Marathi/A01_mr.png ... A16_mr.png as sprites. These are Marathi title + panel
///     text pre-rendered with proper Devanagari shaping, because TextMeshPro does not reorder vowel signs or
///     form conjuncts correctly.
///   - Fills titleMr, panelMr and panelMrRendered on every ArtifactData (keeps Marathi text you have edited).
///   - Creates a dynamic TMP font asset from Noto Sans Devanagari, adds it as a global TMP fallback (so short
///     labels like "मराठी" render) and assigns it to the InfoPanel in Museum.unity.
/// To change a panel's Marathi text: edit marathi.tsv, re-render the PNG, then Museum > Run Marathi Setup Again.
[InitializeOnLoad]
public static class MuseumMarathiSetup
{
    const string StateFile = "ProjectSettings/MuseumMarathiState.txt";
    const string Dir = "Assets/_Museum/Marathi";
    const string FontTtf = "Assets/_Museum/Fonts/NotoSansDevanagari-400.ttf";
    const string FontAssetPath = "Assets/_Museum/Fonts/NotoSansDevanagari SDF.asset";
    const string ScenePath = "Assets/_Museum/Scenes/Museum.unity";
    const string Placeholder = "(मराठी मजकूर लवकरच)";

    static MuseumMarathiSetup() => EditorApplication.delayCall += () =>
    {
        if (!File.Exists(StateFile) && File.Exists("ProjectSettings/MuseumSceneState.txt")) Run();
    };

    [MenuItem("Museum/Run Marathi Setup Again")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        var log = new System.Text.StringBuilder();
        try
        {
            int sprites = ImportSprites();
            int filled = FillData(log);
            var font = CreateFontAsset();
            AddGlobalFallback(font);
            bool scene = AssignToScene(font);
            AssetDatabase.SaveAssets();
            log.Insert(0, $"RESULT: OK\nSprites: {sprites}/16  Artifacts updated: {filled}/16\n" +
                          $"Font asset: {(font ? FontAssetPath : "FAILED")}\nInfoPanel font assigned: {scene}\n");
            File.WriteAllText(StateFile, "done");
        }
        catch (System.Exception e)
        {
            log.Insert(0, "RESULT: ERROR " + e.Message + "\n");
            Debug.LogException(e);
        }
        File.WriteAllText("ProjectSettings/MuseumMarathiReport.txt", log.ToString());
        Debug.Log("[Museum] Marathi setup\n" + log);
        EditorUtility.DisplayDialog("Shivaji Museum VR — Marathi", log.ToString(), "OK");
    }

    static int ImportSprites()
    {
        int n = 0;
        foreach (var path in Directory.GetFiles(Dir, "*_mr.png"))
        {
            var p = path.Replace('\\', '/');
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            if (ti == null) continue;
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
            n++;
        }
        return n;
    }

    static int FillData(System.Text.StringBuilder log)
    {
        var rows = File.ReadAllLines(Dir + "/marathi.tsv")
            .Select(l => l.Split('\t')).Where(c => c.Length == 3)
            .ToDictionary(c => c[0], c => c);
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:ArtifactData", new[] { "Assets/_Museum/Data" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<ArtifactData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || !rows.TryGetValue(data.id, out var row)) continue;
            if (string.IsNullOrEmpty(data.panelMr) || data.panelMr == Placeholder)
            {
                data.titleMr = row[1];
                data.panelMr = row[2];
            }
            data.panelMrRendered = AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/{data.id}_mr.png");
            if (data.panelMrRendered == null) log.AppendLine("Missing image for " + data.id);
            EditorUtility.SetDirty(data);
            n++;
        }
        return n;
    }

    static TMP_FontAsset CreateFontAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null) return existing;
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontTtf);
        if (font == null) throw new System.Exception("Font not found: " + FontTtf);

        var fa = TMP_FontAsset.CreateFontAsset(font);
        fa.name = "NotoSansDevanagari SDF";
        AssetDatabase.CreateAsset(fa, FontAssetPath);
        foreach (var tex in fa.atlasTextures)
        {
            tex.name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(tex, fa);
        }
        fa.material.name = fa.name + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    static void AddGlobalFallback(TMP_FontAsset font)
    {
        if (font == null || TMP_Settings.instance == null) return;
        var list = TMP_Settings.fallbackFontAssets;
        if (list != null && !list.Contains(font))
        {
            list.Add(font);
            EditorUtility.SetDirty(TMP_Settings.instance);
        }
    }

    static bool AssignToScene(TMP_FontAsset font)
    {
        if (font == null || !File.Exists(ScenePath)) return false;
        var active = EditorSceneManager.GetActiveScene();
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var panel = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<InfoPanel>(true)).FirstOrDefault();
        if (panel == null) return false;
        panel.devanagariFont = font;
        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (scene != active) EditorSceneManager.CloseScene(scene, true);
        return true;
    }
}
