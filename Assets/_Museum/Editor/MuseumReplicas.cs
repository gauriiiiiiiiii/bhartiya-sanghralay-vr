using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// Full-size replicas for the gallery centrepieces, modelled procedurally from published dimensions.
public static partial class MuseumBuilder
{
    static List<Vector2> Pts(params float[] xy) { var l = new List<Vector2>(); for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(new Vector2(xy[i], xy[i + 1])); return l; }

    static void AppendRot(MeshKit.MB dst, System.Action<MeshKit.MB> build, Matrix4x4 m) { var t = new MeshKit.MB(); build(t); dst.Append(t, m); }

    /// Returns the selectable object, its top height above the floor and the plinth footprint.
    static (GameObject obj, float top, Vector3 plinth) Replica(string kind, Transform g, Vector3 pos, float yaw, string plinthMat)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        var obj = new GameObject("Object"); obj.transform.SetParent(g, false); obj.transform.SetPositionAndRotation(pos, rot);
        var o = obj.transform;
        Vector3 plinth; float top;
        GameObject Part(string name, string key, System.Action<MeshKit.MB> b, string mat, Vector3 local, bool lm = false) =>
            GMesh(name, key, b, M[mat], o, o.TransformPoint(local), rot, lm);
        switch (kind)
        {
            case "jar":
            {
                plinth = new Vector3(1.0f, 0.55f, 1.0f);
                GMesh("Plinth", "Plinth_Jar", mb => Geo.ChamferBox(mb, new Vector3(0, plinth.y / 2, 0), plinth, 0.01f), M[plinthMat], g, pos, rot, true);
                var prof = Pts(0.001f, 0, 0.12f, 0, 0.145f, 0.03f, 0.22f, 0.15f, 0.29f, 0.33f, 0.32f, 0.45f, 0.305f, 0.58f, 0.23f, 0.71f, 0.14f, 0.79f, 0.12f, 0.83f, 0.16f, 0.865f, 0.165f, 0.9f, 0.125f, 0.9f, 0.112f, 0.86f, 0.001f, 0.75f);
                Part("Jar", "Jar", mb => Geo.Revolve(mb, prof, 56, true), "V5_Terracotta", new Vector3(0, plinth.y, 0));
                float R(float y) { for (int i = 1; i < prof.Count; i++) if (prof[i].y >= y && prof[i - 1].y <= y) return Mathf.Lerp(prof[i - 1].x, prof[i].x, (y - prof[i - 1].y) / Mathf.Max(1e-4f, prof[i].y - prof[i - 1].y)); return 0.1f; }
                Part("Paint", "JarBands", mb =>
                {
                    foreach (var (y0, y1) in new[] { (0.2f, 0.225f), (0.38f, 0.395f), (0.5f, 0.56f), (0.63f, 0.645f), (0.74f, 0.765f), (0.86f, 0.9f) })
                    {
                        var band = new List<Vector2>(); for (int k = 0; k <= 4; k++) { float y = Mathf.Lerp(y0, y1, k / 4f); band.Add(new Vector2(R(y) + 0.003f, y)); }
                        Geo.Revolve(mb, band, 56, true);
                    }
                }, "V5_JarPaint", new Vector3(0, plinth.y, 0));
                var glass = Box("Vitrine", Vector3.zero, new Vector3(0.9f, 1.1f, 0.9f), M["V5_CaseGlass"], g, yaw, false);
                glass.transform.position = pos + Vector3.up * (plinth.y + 0.55f);
                Object.DestroyImmediate(glass.GetComponent<Collider>());
                glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                top = plinth.y + 0.9f;
                break;
            }
            case "pillar":
            {
                plinth = new Vector3(2.2f, 0.22f, 2.2f);
                GMesh("Platform", "Plinth_Pillar", mb => Geo.ChamferBox(mb, new Vector3(0, plinth.y / 2, 0), plinth, 0.02f), M["V5_Granite"], g, pos, rot, true);
                var prof = Pts(0.001f, 0, 0.205f, 0, 0.2f, 1.5f, 0.18f, 3.5f, 0.16f, 5.4f, 0.15f, 6.05f, 0.172f, 6.09f, 0.172f, 6.13f, 0.15f, 6.16f, 0.16f, 6.22f,
                               0.205f, 6.36f, 0.25f, 6.5f, 0.24f, 6.55f, 0.27f, 6.62f, 0.285f, 6.7f, 0.27f, 6.78f, 0.21f, 6.83f, 0.2f, 6.88f, 0.001f, 6.88f);
                Part("Pillar", "IronPillar", mb => { Geo.Revolve(mb, prof, 40, true); Geo.ChamferBox(mb, new Vector3(0, 7.0f, 0), new Vector3(0.52f, 0.24f, 0.52f), 0.02f); }, "V5_Iron", new Vector3(0, plinth.y, 0));
                top = plinth.y + 7.12f;
                break;
            }
            case "wheel":
            {
                plinth = new Vector3(3.4f, 0.3f, 1.0f);
                GMesh("Plinth", "Plinth_Wheel", mb => Geo.ChamferBox(mb, new Vector3(0, plinth.y / 2, 0), plinth, 0.015f), M[plinthMat], g, pos, rot, true);
                var upright = Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0));
                Part("Wheel", "KonarkWheel", mb =>
                {
                    AppendRot(mb, t =>
                    {
                        Geo.RingWall(t, 1.14f, 1.42f, -0.17f, 0.17f, 0, 360, 72);
                        Geo.RingWall(t, 1.42f, 1.47f, -0.11f, 0.11f, 0, 360, 72);
                        Geo.RingWall(t, 0f, 0.33f, -0.24f, 0.24f, 0, 360, 40);
                        Geo.RingWall(t, 0f, 0.13f, -0.42f, 0.42f, 0, 360, 24);
                        for (int i = 0; i < 8; i++)
                        {
                            float a = i * 45f;
                            var m = Matrix4x4.Rotate(Quaternion.Euler(0, a, 0));
                            AppendRot(t, s => Geo.ChamferBox(s, new Vector3(0.74f, 0, 0), new Vector3(0.84f, 0.2f, 0.17f), 0.02f), m);
                            AppendRot(t, s => Geo.RingWall(s, 0f, 0.19f, -0.13f, 0.13f, 0, 360, 28), Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0.78f, 0, 0), Quaternion.identity, Vector3.one));
                            AppendRot(t, s => Geo.ChamferBox(s, new Vector3(0.74f, 0, 0), new Vector3(0.84f, 0.1f, 0.06f), 0.01f), Matrix4x4.Rotate(Quaternion.Euler(0, a + 22.5f, 0)));
                        }
                        for (int i = 0; i < 48; i++)
                            AppendRot(t, s => Geo.ChamferBox(s, new Vector3(1.28f, 0, 0), new Vector3(0.05f, 0.36f, 0.07f), 0.01f), Matrix4x4.Rotate(Quaternion.Euler(0, i * 7.5f, 0)));
                    }, upright);
                }, "V5_Khondalite", new Vector3(0, plinth.y + 1.47f, 0));
                top = plinth.y + 2.94f;
                break;
            }
            case "cannon":
            {
                plinth = new Vector3(2.0f, 0.15f, 3.0f);
                GMesh("Platform", "Plinth_Cannon", mb => Geo.ChamferBox(mb, new Vector3(0, plinth.y / 2, 0), plinth, 0.015f), M["V5_Granite"], g, pos, rot, true);
                float axleY = plinth.y + 0.56f;
                var barrel = Pts(0.001f, 0, 0.06f, 0, 0.085f, 0.04f, 0.06f, 0.1f, 0.045f, 0.16f, 0.2f, 0.19f, 0.235f, 0.24f, 0.235f, 0.33f, 0.21f, 0.37f, 0.205f, 1.05f, 0.225f, 1.08f,
                                 0.225f, 1.16f, 0.195f, 1.2f, 0.168f, 2.12f, 0.19f, 2.17f, 0.2f, 2.3f, 0.185f, 2.42f, 0.075f, 2.42f, 0.075f, 2.2f, 0.001f, 2.2f);
                var bm = Matrix4x4.TRS(new Vector3(0, axleY + 0.32f, -1.05f), Quaternion.Euler(84, 0, 0), Vector3.one);
                Part("Barrel", "CannonBarrel", mb =>
                {
                    AppendRot(mb, t => Geo.Revolve(t, barrel, 40, true), bm);
                    AppendRot(mb, t => Geo.RingWall(t, 0, 0.07f, -0.36f, 0.36f, 0, 360, 20), Matrix4x4.TRS(new Vector3(0, axleY + 0.33f, 0.05f), Quaternion.Euler(0, 0, 90), Vector3.one));
                }, "V5_Bronze", Vector3.zero);
                Part("Carriage", "CannonCarriage", mb =>
                {
                    foreach (var s in new[] { -1f, 1f })
                        AppendRot(mb, t => Geo.ChamferBox(t, Vector3.zero, new Vector3(0.09f, 0.42f, 1.7f), 0.015f), Matrix4x4.TRS(new Vector3(s * 0.27f, axleY + 0.05f, -0.35f), Quaternion.Euler(-14, 0, 0), Vector3.one));
                    Geo.ChamferBox(mb, new Vector3(0, axleY - 0.05f, 0.1f), new Vector3(0.62f, 0.14f, 0.2f), 0.01f);
                    Geo.ChamferBox(mb, new Vector3(0, axleY - 0.2f, -1.05f), new Vector3(0.5f, 0.12f, 0.5f), 0.01f);
                    AppendRot(mb, t => Geo.RingWall(t, 0, 0.055f, -0.5f, 0.5f, 0, 360, 16), Matrix4x4.TRS(new Vector3(0, axleY, 0.1f), Quaternion.Euler(0, 0, 90), Vector3.one));
                }, "V5_DarkWood", Vector3.zero);
                Part("Wheels", "CannonWheels", mb =>
                {
                    foreach (var s in new[] { -1f, 1f })
                        AppendRot(mb, t =>
                        {
                            Geo.RingWall(t, 0.47f, 0.56f, -0.045f, 0.045f, 0, 360, 48);
                            Geo.RingWall(t, 0, 0.11f, -0.08f, 0.08f, 0, 360, 20);
                            for (int i = 0; i < 12; i++) AppendRot(t, u => Geo.ChamferBox(u, new Vector3(0.29f, 0, 0), new Vector3(0.38f, 0.035f, 0.05f), 0.006f), Matrix4x4.Rotate(Quaternion.Euler(0, i * 30f, 0)));
                        }, Matrix4x4.TRS(new Vector3(s * 0.47f, axleY, 0.1f), Quaternion.Euler(0, 0, 90), Vector3.one));
                }, "V5_DarkWood", Vector3.zero);
                Part("Tyres", "CannonTyres", mb =>
                {
                    foreach (var s in new[] { -1f, 1f })
                        AppendRot(mb, t => Geo.RingWall(t, 0.555f, 0.575f, -0.05f, 0.05f, 0, 360, 48), Matrix4x4.TRS(new Vector3(s * 0.47f, axleY, 0.1f), Quaternion.Euler(0, 0, 90), Vector3.one));
                }, "V5_Charcoal", Vector3.zero);
                top = axleY + 0.6f;
                break;
            }
            case "charkha":
            {
                plinth = new Vector3(1.5f, 0.42f, 0.9f);
                GMesh("Plinth", "Plinth_Charkha", mb => Geo.ChamferBox(mb, new Vector3(0, plinth.y / 2, 0), plinth, 0.01f), M[plinthMat], g, pos, rot, true);
                float y0 = plinth.y;
                Part("Frame", "CharkhaFrame", mb =>
                {
                    Geo.ChamferBox(mb, new Vector3(0, y0 + 0.03f, 0), new Vector3(1.15f, 0.06f, 0.3f), 0.008f);
                    foreach (var z in new[] { -0.1f, 0.1f }) Geo.ChamferBox(mb, new Vector3(-0.22f, y0 + 0.36f, z), new Vector3(0.05f, 0.62f, 0.05f), 0.008f);
                    foreach (var z in new[] { -0.07f, 0.07f }) Geo.ChamferBox(mb, new Vector3(0.45f, y0 + 0.14f, z), new Vector3(0.04f, 0.2f, 0.04f), 0.006f);
                    AppendRot(mb, t => Geo.RingWall(t, 0, 0.018f, -0.16f, 0.2f, 0, 360, 12), Matrix4x4.TRS(new Vector3(-0.22f, y0 + 0.6f, 0), Quaternion.Euler(90, 0, 0), Vector3.one));
                    foreach (var z in new[] { -0.055f, 0.055f })
                        AppendRot(mb, t =>
                        {
                            Geo.RingWall(t, 0, 0.05f, -0.02f, 0.02f, 0, 360, 16);
                            for (int i = 0; i < 8; i++) AppendRot(t, u => Geo.ChamferBox(u, new Vector3(0.22f, 0, 0), new Vector3(0.38f, 0.016f, 0.022f), 0.004f), Matrix4x4.Rotate(Quaternion.Euler(0, i * 45f + (z > 0 ? 22.5f : 0), 0)));
                        }, Matrix4x4.TRS(new Vector3(-0.22f, y0 + 0.6f, z), Quaternion.Euler(90, 0, 0), Vector3.one));
                    // crank handle
                    Geo.ChamferBox(mb, new Vector3(-0.22f, y0 + 0.52f, 0.2f), new Vector3(0.03f, 0.18f, 0.03f), 0.005f);
                    AppendRot(mb, t => Geo.RingWall(t, 0, 0.016f, 0f, 0.1f, 0, 360, 10), Matrix4x4.TRS(new Vector3(-0.22f, y0 + 0.44f, 0.2f), Quaternion.Euler(90, 0, 0), Vector3.one));
                }, "V5_DarkWood", Vector3.zero);
                Part("Thread", "CharkhaThread", mb =>
                {
                    AppendRot(mb, t => Geo.RingWall(t, 0.4f, 0.415f, -0.065f, 0.065f, 0, 360, 48), Matrix4x4.TRS(new Vector3(-0.22f, y0 + 0.6f, 0), Quaternion.Euler(90, 0, 0), Vector3.one));
                    Geo.Bar(mb, new Vector3(-0.22f, y0 + 1.0f, 0), new Vector3(0.45f, y0 + 0.25f, 0), 0.006f, 0.006f);
                    Geo.Bar(mb, new Vector3(-0.22f, y0 + 0.2f, 0), new Vector3(0.45f, y0 + 0.23f, 0), 0.006f, 0.006f);
                    AppendRot(mb, t => Geo.Revolve(t, Pts(0.001f, -0.09f, 0.012f, -0.09f, 0.03f, -0.03f, 0.03f, 0.03f, 0.012f, 0.09f, 0.001f, 0.09f), 16, true), Matrix4x4.TRS(new Vector3(0.45f, y0 + 0.24f, 0), Quaternion.Euler(90, 0, 0), Vector3.one));
                }, "V5_Cotton", Vector3.zero);
                top = y0 + 1.05f;
                break;
            }
            default: // satellite
            {
                plinth = new Vector3(1.6f, 0.45f, 1.6f);
                GMesh("Plinth", "Plinth_Satellite", mb => Geo.RingWall(mb, 0, 0.8f, 0, 0.45f, 0, 360, 48), M[plinthMat], g, pos, rot, true);
                float cy = plinth.y + 1.15f + 0.7f;
                float k = 1f + Mathf.Sqrt(2f), sc = 0.7f / Mathf.Sqrt(2f + k * k);
                var verts = new List<Vector3>();
                foreach (var sx in new[] { -1f, 1f }) foreach (var sy in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                {
                    verts.Add(new Vector3(sx, sy, sz * k) * sc); verts.Add(new Vector3(sx, sy * k, sz) * sc); verts.Add(new Vector3(sx * k, sy, sz) * sc);
                }
                var normals = new List<(Vector3 n, bool solar)>();
                foreach (var a in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }) normals.Add((a, true));
                normals.Add((Vector3.up, false)); normals.Add((Vector3.down, false));
                foreach (var s1 in new[] { -1f, 1f }) foreach (var s2 in new[] { -1f, 1f })
                {
                    normals.Add((new Vector3(s1, s2, 0).normalized, true)); normals.Add((new Vector3(s1, 0, s2).normalized, true)); normals.Add((new Vector3(0, s1, s2).normalized, true));
                }
                foreach (var sx in new[] { -1f, 1f }) foreach (var sy in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) normals.Add((new Vector3(sx, sy, sz).normalized, false));
                void Faces(MeshKit.MB mb, bool solar)
                {
                    foreach (var (n, isSolar) in normals)
                    {
                        if (isSolar != solar) continue;
                        float best = verts.Max(v => Vector3.Dot(v, n));
                        var face = verts.Where(v => Vector3.Dot(v, n) > best - 1e-3f).Distinct().ToList();
                        var c = face.Aggregate(Vector3.zero, (acc, v) => acc + v) / face.Count;
                        var u = (face[0] - c).normalized; var w2 = Vector3.Cross(n, u);
                        face = face.OrderBy(v => Mathf.Atan2(Vector3.Dot(v - c, w2), Vector3.Dot(v - c, u))).ToList();
                        Geo.Poly(mb, face, n);
                    }
                }
                Part("Solar", "SatSolar", mb => Faces(mb, true), "V5_Solar", new Vector3(0, cy, 0));
                Part("Body", "SatBody", mb => { Faces(mb, false); for (int i = 0; i < 4; i++) { var d = Quaternion.Euler(0, 45 + i * 90, 0) * new Vector3(0.3f, 0, 0); Geo.Bar(mb, d + Vector3.up * 0.66f, d * 1.6f + Vector3.up * 0.95f, 0.012f, 0.012f); } }, "V5_Steel", new Vector3(0, cy, 0));
                GMesh("Stand", "SatStand", mb => Geo.RingWall(mb, 0, 0.045f, 0, 1.2f, 0, 360, 16), M["V5_Charcoal"], g, pos + Vector3.up * plinth.y, rot, false);
                top = cy + 0.75f;
                break;
            }
        }
        return (obj, top, plinth);
    }
}
