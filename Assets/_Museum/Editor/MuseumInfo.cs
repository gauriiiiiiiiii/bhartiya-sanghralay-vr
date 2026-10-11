using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class MuseumInfo
{
    [MenuItem("Museum/Write Rendering Info")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"playing={Application.isPlaying} colorSpace={QualitySettings.activeColorSpace} quality={QualitySettings.GetQualityLevel()} {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        sb.AppendLine($"default RP={GraphicsSettings.defaultRenderPipeline?.name} quality RP={QualitySettings.renderPipeline?.name} current={GraphicsSettings.currentRenderPipeline?.name}");
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u) sb.AppendLine($"HDR={u.supportsHDR} msaa={u.msaaSampleCount} grading={u.colorGradingMode}");
        sb.AppendLine($"lightmaps={LightmapSettings.lightmaps.Length} mode={(int)LightmapSettings.lightmapsMode} dirs={LightmapSettings.lightmaps.Count(l => l.lightmapDir)}");
        var cam = Camera.main;
        if (cam) { var d = cam.GetUniversalAdditionalCameraData(); sb.AppendLine($"cam={cam.name} hdr={cam.allowHDR} post={d.renderPostProcessing} clear={cam.clearFlags} target={cam.targetTexture} stereo={cam.stereoEnabled}"); }
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) sb.AppendLine($"volume {v.name} global={v.isGlobal} profile={(v.sharedProfile ? v.sharedProfile.name : "null")} weight={v.weight}");
        var stack = VolumeManager.instance.stack;
        if (stack != null) { var ca = stack.GetComponent<ColorAdjustments>(); var tm = stack.GetComponent<Tonemapping>(); sb.AppendLine($"stack exposure={ca?.postExposure.value} tonemap={tm?.mode.value}"); }
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name.StartsWith("Wall")).Take(3)) sb.AppendLine($"{r.name} lmIdx={r.lightmapIndex} so={r.lightmapScaleOffset}");
        var ag = Object.FindFirstObjectByType<AudioGuide>();
        if (ag) sb.AppendLine($"guide intro={(ag.intro ? ag.intro.id : "null")} clip={(ag.intro && ag.intro.narration ? ag.intro.narration.name : "null")} playing={ag.source.isPlaying} time={ag.source.time:0.0} srcClip={(ag.source.clip ? ag.source.clip.name : "null")} children={string.Join(",", ag.GetComponentsInChildren<Transform>(true).Where(t => t.parent == ag.transform).Select(t => t.name + ":" + t.gameObject.activeSelf))}");
        if (Application.isPlaying && ag && Object.FindObjectsByType<Artifact>(FindObjectsSortMode.None).FirstOrDefault() is Artifact a1) { ag.Toggle(a1); sb.AppendLine("toggled " + a1.data.title); }
        System.IO.File.WriteAllText("Tools/render_info.txt", sb.ToString());
        Debug.Log(sb);
    }
}
