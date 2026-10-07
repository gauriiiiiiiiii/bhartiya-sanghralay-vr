using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// Diagnostics: renders the lobby camera with different lighting inputs switched off and logs renderer setup.
public static class MuseumDiag
{
    const string Out = "Diag";
    static bool Menu_TestLightmapFormat = false;

    [MenuItem("Museum/Diagnose Rendering")]
    public static void Run()
    {
        Directory.CreateDirectory(Out);
        var sb = new StringBuilder();
        var scene = EditorSceneManager.OpenScene("Assets/_Museum/Scenes/Lobby.unity", OpenSceneMode.Single);
        var cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.enabled && c.targetTexture == null);
        sb.AppendLine($"camera: {(cam ? cam.name : "none")} lightmaps={LightmapSettings.lightmaps.Length} mode={LightmapSettings.lightmapsMode} probes={(LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0)}");
        sb.AppendLine($"ambientMode={RenderSettings.ambientMode} sky={RenderSettings.ambientSkyColor} eq={RenderSettings.ambientEquatorColor} gr={RenderSettings.ambientGroundColor} intensity={RenderSettings.ambientIntensity} refl={RenderSettings.reflectionIntensity} colorSpace={PlayerSettings.colorSpace}");
        int n = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).OrderBy(r => r.name))
        {
            var mf = r.GetComponent<MeshFilter>(); var m = r.sharedMaterial; var mesh = mf ? mf.sharedMesh : null;
            if (m == null || !m.name.StartsWith("Lobby_")) continue;
            if (n++ > 25) break;
            sb.AppendLine($"{r.name} mat={m.name} shader={m.shader.name} kw=[{string.Join(",", m.shaderKeywords)}] lmIdx={r.lightmapIndex} lmSO={r.lightmapScaleOffset} receiveGI={r.receiveGI} flags={GameObjectUtility.GetStaticEditorFlags(r.gameObject)} mesh={(mesh ? mesh.name : "null")} uv2={(mesh ? mesh.HasVertexAttribute(VertexAttribute.TexCoord1) : false)} scale={r.transform.lossyScale}");
        }
        if (cam)
        {
            Shot(cam, "1_normal");
            var lm = LightmapSettings.lightmaps; LightmapSettings.lightmaps = new LightmapData[0];
            Shot(cam, "2_no_lightmaps"); LightmapSettings.lightmaps = lm;
            var mats = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Select(r => r.sharedMaterial).Where(m => m && m.name.StartsWith("Lobby_")).Distinct().ToList();
            foreach (var m in mats) m.DisableKeyword("_NORMALMAP");
            Shot(cam, "3_no_normalmap");
            var saved = mats.ToDictionary(m => m, m => m.GetTexture("_BaseMap"));
            foreach (var m in mats) m.SetTexture("_BaseMap", null);
            Shot(cam, "4_no_basemap_no_normal");
            foreach (var m in mats) { m.SetTexture("_BaseMap", saved[m]); m.EnableKeyword("_NORMALMAP"); }
            Shot(cam, "5_normalmap_on");
            var amb = RenderSettings.ambientProbe; var sbp = new StringBuilder();
            for (int c = 0; c < 3; c++) { for (int k = 0; k < 9; k++) sbp.Append($"{amb[c, k]:0.##} "); sbp.Append("| "); }
            sb.AppendLine("ambientProbe: " + sbp);
            sb.AppendLine($"customReflection={(RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : "none")} defaultReflMode={RenderSettings.defaultReflectionMode} skybox={(RenderSettings.skybox ? RenderSettings.skybox.name : "none")}");
            foreach (var rp in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) sb.AppendLine($"reflProbe {rp.name} tex={(rp.texture ? rp.texture.name : "null")} intensity={rp.intensity} hdr={rp.hdr}");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) sb.AppendLine($"light {l.name} {l.type} mode={l.lightmapBakeType} int={l.intensity} range={l.range} enabled={l.enabled}");
            float ri = RenderSettings.reflectionIntensity; RenderSettings.reflectionIntensity = 0; Shot(cam, "6_no_reflection"); RenderSettings.reflectionIntensity = ri;
            RenderSettings.ambientProbe = new SphericalHarmonicsL2(); Shot(cam, "7_no_ambient_probe"); RenderSettings.ambientProbe = amb;
            foreach (var m in mats) { m.SetFloat("_EnvironmentReflections", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
            Shot(cam, "8_no_env_no_spec");
            var rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.sharedMaterial && r.sharedMaterial.name.StartsWith("Lobby_")).ToList();
            var idx = rends.ToDictionary(r => r, r => r.lightmapIndex);
            foreach (var r in rends) r.lightmapIndex = -1;
            Shot(cam, "9_renderers_no_lightmap_index");
            foreach (var r in rends) r.lightmapIndex = idx[r];
            var orig = rends.ToDictionary(r => r, r => r.sharedMaterial);
            var fresh = new Material(Shader.Find("Universal Render Pipeline/Lit")); fresh.SetColor("_BaseColor", Color.white);
            foreach (var r in rends) r.sharedMaterial = fresh;
            Shot(cam, "10_fresh_lit_material");
            var teak = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Museum/Materials/Teak_Dark.mat");
            foreach (var r in rends) r.sharedMaterial = teak;
            Shot(cam, "11_teak_material");
            foreach (var r in rends) r.sharedMaterial = orig[r];
            sb.AppendLine("lightmap textures: " + string.Join(", ", LightmapSettings.lightmaps.Select(l => l.lightmapColor ? $"{l.lightmapColor.name} {l.lightmapColor.format} {l.lightmapColor.width}" : "null")));
            foreach (var m in mats) { m.SetFloat("_EnvironmentReflections", 1); m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.SetFloat("_SpecularHighlights", 1); m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
        }
        if (cam && Menu_TestLightmapFormat)
        {
            foreach (var l in LightmapSettings.lightmaps)
            {
                var path = AssetDatabase.GetAssetPath(l.lightmapColor);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) continue;
                var ps = ti.GetPlatformTextureSettings("iPhone");
                sb.AppendLine($"  {path}: type={ti.textureType} iOS override={ps.overridden} fmt={ps.format}");
                ps.overridden = true; ps.format = TextureImporterFormat.RGBAHalf; ti.SetPlatformTextureSettings(ps); ti.SaveAndReimport();
            }
            Shot(cam, "12_lightmaps_rgbahalf");
        }
        File.WriteAllText(Path.Combine(Out, "report.txt"), sb.ToString());
        Debug.Log("[Museum] Diagnose\n" + sb);
    }

    [MenuItem("Museum/Diagnose Linear Colour Space")]
    public static void Linear()
    {
        Directory.CreateDirectory(Out);
        PlayerSettings.colorSpace = ColorSpace.Linear;
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene("Assets/_Museum/Scenes/Lobby.unity", OpenSceneMode.Single);
        var cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.enabled && c.targetTexture == null);
        Shot(cam, "13_linear");
        File.WriteAllText(Path.Combine(Out, "linear.txt"), "colorSpace=" + PlayerSettings.colorSpace);
    }

    [MenuItem("Museum/Diagnose Lightmap Mode")]
    public static void Mode()
    {
        Directory.CreateDirectory(Out);
        if (PlayerSettings.colorSpace != ColorSpace.Gamma) PlayerSettings.colorSpace = ColorSpace.Gamma;
        EditorSceneManager.OpenScene("Assets/_Museum/Scenes/Lobby.unity", OpenSceneMode.Single);
        var cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.enabled && c.targetTexture == null);
        var sb = new StringBuilder();
        sb.AppendLine($"mode int={(int)LightmapSettings.lightmapsMode} name={LightmapSettings.lightmapsMode} dirTex={string.Join(",", LightmapSettings.lightmaps.Select(l => l.lightmapDir ? l.lightmapDir.name : "none"))} shadowmask={string.Join(",", LightmapSettings.lightmaps.Select(l => l.shadowMask ? l.shadowMask.name : "none"))}");
        sb.AppendLine("global kw: " + string.Join(",", Shader.enabledGlobalKeywords.Select(k => k.name)));
        var r = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(x => x.sharedMaterial && x.sharedMaterial.name == "Lobby_Floor");
        var mesh = r.GetComponent<MeshFilter>().sharedMesh; var uv2 = new System.Collections.Generic.List<Vector2>(); mesh.GetUVs(1, uv2);
        sb.AppendLine($"floor mesh {mesh.name} verts={mesh.vertexCount} uv2 min=({uv2.Min(u => u.x)},{uv2.Min(u => u.y)}) max=({uv2.Max(u => u.x)},{uv2.Max(u => u.y)}) readable={mesh.isReadable}");
        Shot(cam, "14_mode_as_is");
        LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional; Shot(cam, "15_force_nondirectional");
        File.WriteAllText(Path.Combine(Out, "mode.txt"), sb.ToString());
    }

    static void Shot(Camera cam, string name)
    {
        var rt = new RenderTexture(960, 540, 24);
        var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
        RenderTexture.active = rt; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release();
    }
}
