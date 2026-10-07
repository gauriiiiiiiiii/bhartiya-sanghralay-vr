using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// Baked lighting for the museum scenes: soft global illumination from chandeliers, spotlights and the glowing jali
/// windows, ambient occlusion, light probes and reflection probes. Baked light costs nothing at runtime on a phone.
/// The bake runs automatically after the scenes are rebuilt (state file), or via Museum > Bake Lighting (all scenes).
[InitializeOnLoad]
public static class MuseumLighting
{
    const string StateFile = "ProjectSettings/MuseumBake_State.txt";       // "pending" while a bake is requested
    const string ReportFile = "ProjectSettings/MuseumBake_Report.txt";
    static double waitStart;

    static MuseumLighting() => EditorApplication.delayCall += () =>
    {
        EditorSceneManager.sceneOpened += (_, __) => FixLightmapMode();
        FixLightmapMode();
        if (!Pending) return;
        waitStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += Wait;
    };

    static bool Pending => File.Exists(StateFile) && File.ReadAllText(StateFile).Trim() == "pending";

    /// The baked lighting data can come back flagged "directional" although only non-directional maps were baked; shaders
    /// then read a missing direction map and lit surfaces turn pure black/white. (MuseumApp applies the same fix at runtime.)
    public static void FixLightmapMode()
    {
        var maps = LightmapSettings.lightmaps;
        if (maps.Length > 0 && LightmapSettings.lightmapsMode != LightmapsMode.NonDirectional && maps.All(m => m.lightmapDir == null))
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
    }

    public static void Request() => File.WriteAllText(StateFile, "pending");

    [MenuItem("Museum/Bake Lighting (all scenes)")]
    static void Menu() { Request(); ContinueIfPending(); }

    static void Wait()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { waitStart = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.timeSinceStartup - waitStart < 3) return;
        EditorApplication.update -= Wait;
        ContinueIfPending();
    }

    /// Lightmapping settings for a museum scene (call while the scene is open, before saving).
    public static void Configure(UnityEngine.SceneManagement.Scene scene)
    {
        const string path = "Assets/_Museum/Settings/Lighting_Museum.lighting";
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        if (ls == null)
        {
            ls = new LightingSettings { name = "Lighting_Museum" };
            Directory.CreateDirectory("Assets/_Museum/Settings");
            AssetDatabase.CreateAsset(ls, path);
        }
        ls.bakedGI = true; ls.realtimeGI = false;
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        ls.lightmapResolution = 7f;            // texels per metre: halls are large, light is soft
        ls.lightmapMaxSize = 1024;
        ls.lightmapPadding = 2;
        ls.directionalityMode = LightmapsMode.NonDirectional;
        ls.ao = true; ls.aoMaxDistance = 0.8f; ls.aoExponentIndirect = 1f; ls.aoExponentDirect = 0f;
        ls.lightmapCompression = LightmapCompression.NormalQuality;
        ls.maxBounces = 2;
        ls.directSampleCount = 32; ls.indirectSampleCount = 256; ls.environmentSampleCount = 64;
        ls.filteringMode = LightingSettings.FilterMode.Auto;
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
        EditorUtility.SetDirty(ls);
        Lightmapping.lightingSettings = ls;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.reflectionIntensity = 0.8f;
    }

    public static void ContinueIfPending()
    {
        if (!Pending || EditorApplication.isPlaying) return;
        if (!File.Exists("ProjectSettings/MuseumIHM_State.txt")) return;     // scenes first
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && !s.path.EndsWith("/Menu.unity")).Select(s => s.path).ToList();
        var log = new System.Text.StringBuilder();
        var started = System.DateTime.Now;
        try
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Museum of Indian History — baking light", $"{Path.GetFileNameWithoutExtension(scenes[i])} ({i + 1}/{scenes.Count}). This can take a few minutes per scene.", i / (float)scenes.Count))
                { log.AppendLine("Cancelled"); break; }
                var t0 = System.DateTime.Now;
                var scene = EditorSceneManager.OpenScene(scenes[i], OpenSceneMode.Single);
                Configure(scene);
                bool ok = Lightmapping.Bake();
                // Unity can leave the scene flagged "directional" after a non-directional bake: the shader then reads a missing
                // direction map and lit surfaces turn pure black/white. Match the flag to what was actually baked.
                var maps = LightmapSettings.lightmaps;
                FixLightmapMode();
                // keep Unity's default lightmap compression (undo any manual override on the baked EXRs)
                foreach (var m in maps)
                {
                    var ti = m.lightmapColor ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(m.lightmapColor)) as TextureImporter : null;
                    if (ti == null) continue;
                    var ps = ti.GetPlatformTextureSettings("iPhone");
                    if (ps.overridden) { ps.overridden = false; ti.SetPlatformTextureSettings(ps); ti.SaveAndReimport(); }
                }
                log.Append($"[{LightmapSettings.lightmapsMode}, {maps.Length} maps] ");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                log.AppendLine($"{Path.GetFileNameWithoutExtension(scenes[i])}: {(ok ? "baked" : "FAILED")} in {(System.DateTime.Now - t0).TotalSeconds:0}s");
            }
            log.Insert(0, $"RESULT: DONE in {(System.DateTime.Now - started).TotalMinutes:0.0} min\n");
        }
        catch (System.Exception e)
        {
            log.Insert(0, "RESULT: ERROR " + e.Message + "\n");
            Debug.LogException(e);
        }
        finally { EditorUtility.ClearProgressBar(); }
        File.WriteAllText(ReportFile, log.ToString());
        File.WriteAllText(StateFile, "done");
        Debug.Log("[Museum] Lighting bake\n" + log);
        if (scenes.Count > 0) EditorSceneManager.OpenScene(scenes[0]);
        EditorApplication.delayCall += MuseumIOSBuild.ContinueIfPending;
    }
}
