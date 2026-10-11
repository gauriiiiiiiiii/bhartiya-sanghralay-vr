using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Realistic rendering for the v5 museum: linear colour, HDR with filmic tone mapping and gentle bloom, a physical
/// sky with a sun, box-projected reflections and soft baked daylight.
public static partial class MuseumBuilder
{
    const string PostProfilePath = "Assets/_Museum/Settings/MuseumPost.asset";
    const string SkyMatPath = "Assets/_Museum/Materials/Sky_Procedural.mat";
    public static float PostExposure = 0.45f;

    static void ApplyRealism(System.Text.StringBuilder report)
    {
        if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
        var urp = GraphicsSettings.defaultRenderPipeline;
        if (urp)
        {
            var so = new SerializedObject(urp);
            void SetB(string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }
            void SetI(string n, int v) { var p = so.FindProperty(n); if (p != null) p.intValue = v; }
            SetB("m_SupportsHDR", true);
            SetI("m_HDRColorBufferPrecision", 0);
            SetI("m_MSAA", 4);
            SetI("m_ColorGradingMode", 1);          // HDR grading
            SetI("m_ColorGradingLutSize", 32);
            SetB("m_ReflectionProbeBlending", true);
            SetB("m_ReflectionProbeBoxProjection", true);
            SetI("m_AdditionalLightsRenderingMode", 1);
            SetI("m_AdditionalLightsPerObjectLimit", 4);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        report.AppendLine("Rendering: linear colour, HDR, ACES tone mapping, bloom, MSAA 4x, box-projected reflection probes");
    }

    static VolumeProfile PostProfile()
    {
        var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
        if (prof == null)
        {
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, PostProfilePath);
        }
        T Get<T>() where T : VolumeComponent
        {
            if (!prof.TryGet<T>(out var c)) { c = prof.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, prof); }
            return c;
        }
        var tm = Get<Tonemapping>(); tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;
        var bl = Get<Bloom>();
        bl.threshold.overrideState = true; bl.threshold.value = 1.1f;
        bl.intensity.overrideState = true; bl.intensity.value = 0.22f;
        bl.scatter.overrideState = true; bl.scatter.value = 0.65f;
        bl.highQualityFiltering.overrideState = true; bl.highQualityFiltering.value = false;
        var ca = Get<ColorAdjustments>();
        ca.postExposure.overrideState = true; ca.postExposure.value = PostExposure;
        ca.contrast.overrideState = true; ca.contrast.value = 6f;
        ca.saturation.overrideState = true; ca.saturation.value = 4f;
        var wb = Get<WhiteBalance>(); wb.temperature.overrideState = true; wb.temperature.value = 9f;
        EditorUtility.SetDirty(prof);
        return prof;
    }

    static Material SkyMaterial()
    {
        var sh = Shader.Find("Skybox/Procedural");
        var m = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, SkyMatPath); }
        m.shader = sh;
        m.SetFloat("_SunSize", 0.035f); m.SetFloat("_SunSizeConvergence", 6f);
        m.SetFloat("_AtmosphereThickness", 0.85f);
        m.SetColor("_SkyTint", new Color(0.56f, 0.6f, 0.66f));
        m.SetColor("_GroundColor", new Color(0.42f, 0.4f, 0.37f));
        m.SetFloat("_Exposure", 1.15f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Sky, sun, ambient light, post-processing volume and camera settings for one v5 scene.
    static Light DaylightScene(Vector3 sunEuler, float sunIntensity = 2.6f, float skyLight = 0.75f)
    {
        RenderSettings.skybox = SkyMaterial();
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = skyLight;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.fog = false;
        var sunGo = new GameObject("Sun");
        sunGo.transform.rotation = Quaternion.Euler(sunEuler);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional; sun.lightmapBakeType = LightmapBakeType.Baked;
        sun.color = new Color(1f, 0.95f, 0.86f); sun.intensity = sunIntensity;
        sun.shadows = LightShadows.Soft; sun.shadowAngle = 1.2f;
        RenderSettings.sun = sun;
        var vol = new GameObject("PostProcessing").AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 1; vol.sharedProfile = PostProfile();
        return sun;
    }

    /// Camera: HDR, post-processing, skybox background.
    static void RealCamera()
    {
        var cam = Camera.main; if (!cam) return;
        cam.allowHDR = true; cam.allowMSAA = true;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.farClipPlane = 160f;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.None;
    }

    /// Soft rectangular area light (baked only) – skylights, laylights, cove lighting.
    static Light AreaLight(string name, Transform p, Vector3 center, Vector2 size, Vector3 facing, float intensity, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.position = center;
        go.transform.rotation = Quaternion.LookRotation(facing, Mathf.Abs(facing.y) > 0.9f ? Vector3.forward : Vector3.up);
        var l = go.AddComponent<Light>();
        l.type = LightType.Rectangle; l.areaSize = size; l.lightmapBakeType = LightmapBakeType.Baked;
        l.intensity = intensity; l.color = c; l.range = 30f;
        return l;
    }

    /// A baked spotlight aimed at a target (track lighting).
    static Light AimSpot(string name, Transform p, Vector3 from, Vector3 to, float angle, float intensity, Color? c = null, float range = 14f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.position = from; go.transform.LookAt(to);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * 0.55f;
        l.lightmapBakeType = LightmapBakeType.Baked; l.intensity = intensity; l.range = range;
        l.color = c ?? new Color(1f, 0.93f, 0.82f); l.shadows = LightShadows.Soft; l.shapeRadius = 0.05f;
        return l;
    }

    /// The realtime "audio guide" highlight: same aim as the baked spot, off until the narration plays.
    static Light HighlightSpot(Transform p, Vector3 from, Vector3 to, float angle)
    {
        var go = new GameObject("GuideHighlight");
        go.transform.SetParent(p, false);
        go.transform.position = from; go.transform.LookAt(to);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f;
        l.lightmapBakeType = LightmapBakeType.Realtime; l.shadows = LightShadows.None;
        l.intensity = 0f; l.range = Vector3.Distance(from, to) + 3f; l.color = new Color(1f, 0.92f, 0.8f);
        l.enabled = false;
        return l;
    }

    static void ReflectionProbeBox(Transform p, Vector3 center, Vector3 size, int res = 256)
    {
        var go = new GameObject("ReflectionProbe");
        go.transform.SetParent(p, false); go.transform.position = center;
        var r = go.AddComponent<ReflectionProbe>();
        r.mode = ReflectionProbeMode.Baked; r.boxProjection = true; r.resolution = res; r.hdr = true;
        r.size = size + Vector3.one * 2f; r.center = Vector3.zero; r.blendDistance = 0.5f;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
    }

    static void ProbeGrid(Transform p, Vector3 min, Vector3 max, float step)
    {
        var pts = new System.Collections.Generic.List<Vector3>();
        for (float x = min.x; x <= max.x + 0.01f; x += step)
            for (float z = min.z; z <= max.z + 0.01f; z += step)
                for (float y = min.y; y <= max.y + 0.01f; y += Mathf.Max(1.5f, (max.y - min.y) / 2f)) pts.Add(new Vector3(x, y, z));
        var g = new GameObject("LightProbes"); g.transform.SetParent(p, false);
        g.AddComponent<LightProbeGroup>().probePositions = pts.ToArray();
    }
}
