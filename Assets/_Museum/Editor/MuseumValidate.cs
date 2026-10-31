using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Museum > Validate Scenes: checks every museum scene for
///   1. openings to the outside: rays from each walk point (eye height, all directions) that hit nothing;
///   2. text cut by geometry: letters of wall text hidden behind something when seen from the front.
/// Temporary mesh colliders are added for the check only; scenes are not saved. Writes Tools/validate.txt.
public static class MuseumValidate
{
    [MenuItem("Museum/Validate Scenes")]
    public static void Run()
    {
        var sb = new StringBuilder();
        int totalGaps = 0, totalClipped = 0;
        foreach (var path in EditorBuildSettings.scenes.Where(s => s.enabled && !s.path.EndsWith("/Menu.unity")).Select(s => s.path))
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            string name = Path.GetFileNameWithoutExtension(path);
            // every visible mesh blocks rays (glass included: a window is not a gap); text and floor discs excluded
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>())
            {
                if (mr.GetComponent<Collider>() || mr.GetComponent<TMP_Text>() || mr.GetComponentInParent<TeleportPoint>()) continue;
                var mf = mr.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            Physics.SyncTransforms();
            const int mask = ~0;

            // 1. openings
            var gaps = new List<string>();
            foreach (var tp in Object.FindObjectsByType<TeleportPoint>())
            {
                var eye = tp.transform.position + Vector3.up * 1.58f;
                int n = 0; string first = null;
                foreach (var pitch in new[] { -25f, -8f, 4f, 16f, 28f })
                    for (int yaw = 0; yaw < 360; yaw += 2)
                    {
                        var dir = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
                        if (!Physics.Raycast(eye, dir, out _, 120f, mask, QueryTriggerInteraction.Ignore))
                        { n++; first ??= $"yaw {yaw}°, pitch {pitch}°"; }
                    }
                if (n > 0) gaps.Add($"  open to outside from {tp.name} at {Fmt(tp.transform.position)}: {n} rays, e.g. {first}");
            }

            // 2. text cut by geometry
            var clipped = new List<string>();
            foreach (var t in Object.FindObjectsByType<TextMeshPro>())
            {
                if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
                t.ForceMeshUpdate();
                var ti = t.textInfo; int vis = 0, hid = 0;
                var viewer = -t.transform.forward;        // text is read from the side its forward points away from
                for (int i = 0; i < ti.characterCount; i++)
                {
                    var c = ti.characterInfo[i]; if (!c.isVisible) continue;
                    vis++;
                    var p = t.transform.TransformPoint((c.bottomLeft + c.topRight) * 0.5f);
                    var from = p + viewer * 0.6f;
                    if (Physics.Raycast(from, -viewer, out var hit, 0.6f - 0.004f, mask, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(t.transform)) hid++;
                }
                if (hid > 0) clipped.Add($"  text cut: {PathOf(t.transform)} – {hid}/{vis} letters hidden (\"{Short(t.text)}\")");
            }
            // 3. flat pieces (photos, label boards, mounts, intro panels) sunk into a wall: sample their face from the front
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>())
            {
                var n = mr.name;
                if (!(n == "Photo" || n == "Board" || n == "Mount" || n == "IntroPanel" || n == "Panel")) continue;
                var mf = mr.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds; var tr = mr.transform; var owner = tr.parent ? tr.parent : tr;
                var viewer = -tr.forward; int hid = 0, tot = 0;
                for (int ix = -2; ix <= 2; ix++) for (int iy = -2; iy <= 2; iy++)
                {
                    tot++;
                    var lp = b.center + new Vector3(b.extents.x * 0.47f * ix, b.extents.y * 0.47f * iy, -b.extents.z);
                    var p = tr.TransformPoint(lp);
                    if (Physics.Raycast(p + viewer * 0.6f, -viewer, out var hit, 0.6f - 0.004f, mask, QueryTriggerInteraction.Ignore)
                        && !hit.collider.transform.IsChildOf(owner) && !hit.collider.transform.IsChildOf(tr)) hid++;
                }
                if (hid > 0) clipped.Add($"  surface hidden: {PathOf(tr)} – {hid}/{tot} sample points behind other geometry");
            }
            totalGaps += gaps.Count; totalClipped += clipped.Count;
            sb.AppendLine($"{name}: {gaps.Count} openings, {clipped.Count} cut texts or hidden surfaces");
            foreach (var g in gaps) sb.AppendLine(g);
            foreach (var c in clipped) sb.AppendLine(c);
        }
        sb.Insert(0, $"RESULT: {totalGaps} openings, {totalClipped} cut texts or hidden surfaces\n");
        File.WriteAllText("Tools/validate.txt", sb.ToString());
        Debug.Log("[Museum] " + sb.ToString().Split('\n')[0] + " (details in Tools/validate.txt)");
    }

    static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.z:0.0})";
    static string Short(string s) { s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Replace("\n", " "); return s.Length > 40 ? s.Substring(0, 40) + "…" : s; }
    static string PathOf(Transform t) { var p = t.name; while (t.parent) { t = t.parent; p = t.name + "/" + p; } return p; }
}
