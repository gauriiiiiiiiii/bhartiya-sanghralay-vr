using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Museum > Snapshot Scenes: renders each museum scene from the visitor's starting view (and a view further in)
/// to Tools/snapshots/*.png, for checking materials and lighting without entering Play mode.
public static class MuseumSnapshot
{
    [MenuItem("Museum/Snapshot Scenes")]
    public static void Run()
    {
        var dir = "Tools/snapshots"; Directory.CreateDirectory(dir);
        foreach (var path in EditorBuildSettings.scenes.Where(s => s.enabled && !s.path.EndsWith("/Menu.unity")).Select(s => s.path))
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var cam = Object.FindObjectsByType<Camera>().FirstOrDefault(c => c.enabled && c.targetTexture == null);
            if (cam == null) continue;
            string name = Path.GetFileNameWithoutExtension(path);
            MuseumLighting.FixLightmapMode();
            foreach (var g in Object.FindObjectsByType<GazeInteractor>()) if (g.reticle) g.reticle.gameObject.SetActive(false);
            var t = cam.transform; var p0 = t.position; var r0 = t.rotation;
            Shot(cam, $"{dir}/{name}_a.png");
            var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            t.SetPositionAndRotation(new Vector3(p0.x, 1.6f, p0.z) + fwd * 9f, Quaternion.LookRotation(Quaternion.Euler(0, 35, 0) * fwd)); Shot(cam, $"{dir}/{name}_b.png");
            t.SetPositionAndRotation(new Vector3(p0.x, 1.6f, p0.z) + fwd * 9f, Quaternion.LookRotation(Quaternion.Euler(-28, -40, 0) * fwd)); Shot(cam, $"{dir}/{name}_c.png");
            int k = 0;
            foreach (var tp in Object.FindObjectsByType<TeleportPoint>().Where(p => p.name.StartsWith("TP_View_") && !p.name.Contains("Table")).OrderBy(p => p.name).Take(3))
            {
                t.SetPositionAndRotation(tp.transform.position + Vector3.up * 1.6f, Quaternion.Euler(4, tp.transform.eulerAngles.y + 14f, 0));   // turned slightly towards the label, which hangs to the right
                Shot(cam, $"{dir}/{name}_v{k++}.png");
            }
            // the information desk's welcome wall (built in edit mode for the picture only; the scene is not saved)
            foreach (var desk in Object.FindObjectsByType<InfoDesk>())
            {
                desk.EnsureBuilt();
                Canvas.ForceUpdateCanvases();
                var dp = Object.FindObjectsByType<TeleportPoint>().FirstOrDefault(p => p.name == "TP_InfoDesk");
                var from = dp ? dp.transform.position + Vector3.up * 1.6f : desk.transform.position + new Vector3(0, -1f, -3.1f);
                t.SetPositionAndRotation(from, Quaternion.LookRotation(desk.transform.position + Vector3.down * 0.25f - from));
                Shot(cam, $"{dir}/{name}_desk.png");
            }
            t.SetPositionAndRotation(p0, r0);
        }
        ShotMenu(dir);
        Debug.Log("[Museum] Snapshots written to " + dir);
    }

    /// The start menu at an iPhone's landscape resolution (its overlay canvas is switched to the camera for the shot).
    static void ShotMenu(string dir)
    {
        var path = EditorBuildSettings.scenes.Select(s => s.path).FirstOrDefault(p => p.EndsWith("/Menu.unity"));
        if (path == null) return;
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var cam = Object.FindObjectsByType<Camera>().FirstOrDefault();
        if (!cam) return;
        cam.cullingMask = 1 << LayerMask.NameToLayer("UI");
        foreach (var c in Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas))
        {
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f;
            foreach (var t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerMask.NameToLayer("UI");
        }
        Shot(cam, $"{dir}/Menu.png", 2532, 1170);
    }

    static void Shot(Camera cam, string file, int w = 960, int h = 540)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        var old = cam.targetTexture; cam.targetTexture = rt;
        Canvas.ForceUpdateCanvases(); cam.Render(); cam.targetTexture = old;
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release();
    }
}
