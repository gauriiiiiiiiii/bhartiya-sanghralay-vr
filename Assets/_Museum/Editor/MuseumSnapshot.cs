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
            var cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.enabled && c.targetTexture == null);
            if (cam == null) continue;
            string name = Path.GetFileNameWithoutExtension(path);
            MuseumLighting.FixLightmapMode();
            var t = cam.transform; var p0 = t.position; var r0 = t.rotation;
            Shot(cam, $"{dir}/{name}_a.png");
            bool wing = name.StartsWith("Wing");
            var fwd = wing ? Vector3.right : Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            t.SetPositionAndRotation(new Vector3(p0.x, 1.6f, wing ? 0 : p0.z) + fwd * 8f, Quaternion.LookRotation(fwd)); Shot(cam, $"{dir}/{name}_b.png");
            t.SetPositionAndRotation(new Vector3(p0.x, 1.6f, wing ? 0 : p0.z) + fwd * 20f, Quaternion.LookRotation(Quaternion.Euler(0, 50, 0) * fwd)); Shot(cam, $"{dir}/{name}_c.png");
            t.SetPositionAndRotation(p0, r0);
        }
        Debug.Log("[Museum] Snapshots written to " + dir);
    }

    static void Shot(Camera cam, string file)
    {
        var rt = new RenderTexture(960, 540, 24) { antiAliasing = 2 };
        var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
        RenderTexture.active = rt; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release();
    }
}
