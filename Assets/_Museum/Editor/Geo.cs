using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Second geometry toolkit (v5 museum): every polygon is oriented by an explicit outward direction, so faces always
/// point the right way; UVs are planar in metres along the face's dominant axes.
public static class Geo
{
    /// Adds a convex polygon (any vertex order) facing 'outward'.
    public static void Poly(MeshKit.MB mb, IList<Vector3> pts, Vector3 outward)
    {
        if (pts.Count < 3) return;
        var n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
        for (int i = 3; n.sqrMagnitude < 1e-10f && i < pts.Count; i++) n = Vector3.Cross(pts[1] - pts[0], pts[i] - pts[0]);
        bool flip = Vector3.Dot(n, outward) < 0f;
        var a = outward.normalized;
        Vector3 u = Mathf.Abs(a.y) > 0.7f ? Vector3.right : Vector3.Cross(Vector3.up, a).normalized;
        if (u.sqrMagnitude < 0.01f) u = Vector3.right;
        Vector3 v = Mathf.Abs(a.y) > 0.7f ? Vector3.forward : Vector3.up;
        int b = mb.v.Count;
        foreach (var p in pts) mb.V(p, new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
        for (int i = 1; i < pts.Count - 1; i++)
        {
            if (flip) mb.Tri(b, b + i + 1, b + i); else mb.Tri(b, b + i, b + i + 1);
        }
    }

    public static void Quad(MeshKit.MB mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward) => Poly(mb, new[] { a, b, c, d }, outward);

    /// Box with chamfered (bevelled) edges, centred on c. Bevelled edges catch the light, which makes objects read as real.
    public static void ChamferBox(MeshKit.MB mb, Vector3 c, Vector3 size, float bevel)
    {
        var h = size / 2f; float b = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
        if (b <= 0.0005f) { MeshKit.Box(mb, c, size); return; }
        Vector3 P(float x, float y, float z) => c + new Vector3(x, y, z);
        float X = h.x, Y = h.y, Z = h.z, x = X - b, y = Y - b, z = Z - b;
        // 6 faces
        foreach (var s in new[] { 1f, -1f })
        {
            Quad(mb, P(s * X, -y, -z), P(s * X, y, -z), P(s * X, y, z), P(s * X, -y, z), new Vector3(s, 0, 0));
            Quad(mb, P(-x, s * Y, -z), P(x, s * Y, -z), P(x, s * Y, z), P(-x, s * Y, z), new Vector3(0, s, 0));
            Quad(mb, P(-x, -y, s * Z), P(x, -y, s * Z), P(x, y, s * Z), P(-x, y, s * Z), new Vector3(0, 0, s));
        }
        // 12 edges
        foreach (var sa in new[] { 1f, -1f })
            foreach (var sb in new[] { 1f, -1f })
            {
                // edges along z (between x and y faces)
                Quad(mb, P(sa * X, sb * y, -z), P(sa * x, sb * Y, -z), P(sa * x, sb * Y, z), P(sa * X, sb * y, z), new Vector3(sa, sb, 0));
                // edges along x (between y and z faces)
                Quad(mb, P(-x, sa * Y, sb * z), P(-x, sa * y, sb * Z), P(x, sa * y, sb * Z), P(x, sa * Y, sb * z), new Vector3(0, sa, sb));
                // edges along y (between x and z faces)
                Quad(mb, P(sa * X, -y, sb * z), P(sa * x, -y, sb * Z), P(sa * x, y, sb * Z), P(sa * X, y, sb * z), new Vector3(sa, 0, sb));
            }
        // 8 corners
        foreach (var sx in new[] { 1f, -1f })
            foreach (var sy in new[] { 1f, -1f })
                foreach (var sz in new[] { 1f, -1f })
                    Poly(mb, new[] { P(sx * X, sy * y, sz * z), P(sx * x, sy * Y, sz * z), P(sx * x, sy * y, sz * Z) }, new Vector3(sx, sy, sz));
    }

    /// Cylinder wall (ring) between radii r0 < r1 from y0 to y1, over an angle range (degrees, 0 = +X, counter-clockwise seen from above).
    public static void RingWall(MeshKit.MB mb, float r0, float r1, float y0, float y1, float a0 = 0, float a1 = 360, int seg = 64, bool caps = true)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt(seg * Mathf.Abs(a1 - a0) / 360f));
        Vector3 Pt(float r, float a, float y) { float t = a * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(t) * r, y, Mathf.Sin(t) * r); }
        for (int i = 0; i < n; i++)
        {
            float aa = Mathf.Lerp(a0, a1, i / (float)n), ab = Mathf.Lerp(a0, a1, (i + 1) / (float)n), am = (aa + ab) / 2;
            var outN = Pt(1, am, 0);
            Quad(mb, Pt(r1, aa, y0), Pt(r1, ab, y0), Pt(r1, ab, y1), Pt(r1, aa, y1), outN);                     // outer face
            if (r0 > 0.001f) Quad(mb, Pt(r0, aa, y0), Pt(r0, ab, y0), Pt(r0, ab, y1), Pt(r0, aa, y1), -outN); // inner face
            Quad(mb, Pt(r0, aa, y1), Pt(r0, ab, y1), Pt(r1, ab, y1), Pt(r1, aa, y1), Vector3.up);
            Quad(mb, Pt(r0, aa, y0), Pt(r0, ab, y0), Pt(r1, ab, y0), Pt(r1, aa, y0), Vector3.down);
        }
        if (caps && Mathf.Abs(a1 - a0) < 359.9f)
        {
            Quad(mb, Pt(r0, a0, y0), Pt(r1, a0, y0), Pt(r1, a0, y1), Pt(r0, a0, y1), -Pt(1, a0 + 90, 0));
            Quad(mb, Pt(r0, a1, y0), Pt(r1, a1, y0), Pt(r1, a1, y1), Pt(r0, a1, y1), Pt(1, a1 + 90, 0));
        }
    }

    /// Thin square-section bar between two points (steel members, rails, tracks).
    public static void Bar(MeshKit.MB mb, Vector3 a, Vector3 b, float w, float h)
    {
        var d = b - a; float len = d.magnitude; if (len < 1e-4f) return;
        var f = d / len;
        var up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        var s = Vector3.Cross(up, f).normalized * (w / 2); var u = Vector3.Cross(f, s).normalized * (h / 2);
        Vector3[] c0 = { a - s - u, a + s - u, a + s + u, a - s + u }, c1 = { b - s - u, b + s - u, b + s + u, b - s + u };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4; var mid = (c0[i] + c0[j]) / 2 - a;
            Quad(mb, c0[i], c0[j], c1[j], c1[i], mid);
        }
    }

    /// Solid of revolution with an explicit inside/outside: profile (radius, y). Faces point away from the axis
    /// (outside = true) or towards it.
    public static void Revolve(MeshKit.MB mb, IList<Vector2> profile, int seg, bool outside, float a0 = 0, float a1 = 360)
    {
        for (int i = 0; i < seg; i++)
        {
            float ta = Mathf.Lerp(a0, a1, i / (float)seg) * Mathf.Deg2Rad, tb = Mathf.Lerp(a0, a1, (i + 1) / (float)seg) * Mathf.Deg2Rad;
            for (int j = 0; j < profile.Count - 1; j++)
            {
                var p = profile[j]; var q = profile[j + 1];
                Vector3 A = new Vector3(Mathf.Cos(ta) * p.x, p.y, Mathf.Sin(ta) * p.x), B = new Vector3(Mathf.Cos(tb) * p.x, p.y, Mathf.Sin(tb) * p.x);
                Vector3 C = new Vector3(Mathf.Cos(tb) * q.x, q.y, Mathf.Sin(tb) * q.x), D = new Vector3(Mathf.Cos(ta) * q.x, q.y, Mathf.Sin(ta) * q.x);
                // outward normal of this band: perpendicular to the profile segment, pointing away from the axis
                var e = q - p; var n2 = new Vector2(e.y, -e.x);
                if (n2.x < 0 || (Mathf.Abs(n2.x) < 1e-5f && n2.y < 0 && p.x < 1e-3f)) n2 = -n2;
                float tm = (ta + tb) / 2;
                var n = new Vector3(Mathf.Cos(tm) * n2.x, n2.y, Mathf.Sin(tm) * n2.x);
                if (!outside) n = -n;
                if ((A - B).sqrMagnitude < 1e-10f) Poly(mb, new[] { A, C, D }, n);
                else if ((C - D).sqrMagnitude < 1e-10f) Poly(mb, new[] { A, B, C }, n);
                else Quad(mb, A, B, C, D, n);
            }
        }
    }

    /// Rectangular cross-section ribbon swept along a horizontal arc that rises linearly (spiral ramp / curved parapet).
    /// Box section: radial from rIn to rOut, vertical from yOff to yOff + thick, relative to the helix height.
    public static void Helix(MeshKit.MB mb, float rIn, float rOut, float yOff, float thick, float a0, float a1, float rise, int seg)
    {
        Vector3 Pt(float r, float a, float y) { float t = a * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(t) * r, y, Mathf.Sin(t) * r); }
        for (int i = 0; i < seg; i++)
        {
            float fa = i / (float)seg, fb = (i + 1) / (float)seg;
            float aa = Mathf.Lerp(a0, a1, fa), ab = Mathf.Lerp(a0, a1, fb);
            float ya = rise * fa + yOff, yb = rise * fb + yOff;
            var nOut = Pt(1, (aa + ab) / 2, 0);
            Quad(mb, Pt(rOut, aa, ya), Pt(rOut, ab, yb), Pt(rOut, ab, yb + thick), Pt(rOut, aa, ya + thick), nOut);
            Quad(mb, Pt(rIn, aa, ya), Pt(rIn, ab, yb), Pt(rIn, ab, yb + thick), Pt(rIn, aa, ya + thick), -nOut);
            Quad(mb, Pt(rIn, aa, ya + thick), Pt(rIn, ab, yb + thick), Pt(rOut, ab, yb + thick), Pt(rOut, aa, ya + thick), Vector3.up);
            Quad(mb, Pt(rIn, aa, ya), Pt(rIn, ab, yb), Pt(rOut, ab, yb), Pt(rOut, aa, ya), Vector3.down);
        }
    }

    /// Regular polygon prism (octagonal piers, satellite bodies, etc.), axis along Y.
    public static void Prism(MeshKit.MB mb, Vector3 c, int sides, float r, float h, float rotDeg = 0)
    {
        var top = new List<Vector3>(); var bot = new List<Vector3>();
        for (int i = 0; i < sides; i++)
        {
            float a = (rotDeg + i * 360f / sides) * Mathf.Deg2Rad;
            bot.Add(c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); top.Add(bot[i] + Vector3.up * h);
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides; var mid = (bot[i] + bot[j]) / 2 - c; mid.y = 0;
            Quad(mb, bot[i], bot[j], top[j], top[i], mid);
        }
        Poly(mb, top, Vector3.up); Poly(mb, bot, Vector3.down);
    }

    /// Flat UV-mapped image quad (photo prints, labels' backing) facing 'normal', centred at c.
    public static void ImageQuad(MeshKit.MB mb, Vector3 c, Vector3 normal, float w, float h)
    {
        var right = Vector3.Cross(Vector3.up, normal).normalized; if (right.sqrMagnitude < 0.01f) right = Vector3.right;
        var up = Vector3.Cross(normal, right).normalized;
        int b = mb.v.Count;
        mb.V(c - right * w / 2 - up * h / 2, new Vector2(0, 0)); mb.V(c + right * w / 2 - up * h / 2, new Vector2(1, 0));
        mb.V(c + right * w / 2 + up * h / 2, new Vector2(1, 1)); mb.V(c - right * w / 2 + up * h / 2, new Vector2(0, 1));
        var n = Vector3.Cross(mb.v[b + 1] - mb.v[b], mb.v[b + 2] - mb.v[b]);
        if (Vector3.Dot(n, normal) < 0) { mb.Tri(b, b + 2, b + 1); mb.Tri(b, b + 3, b + 2); } else { mb.Tri(b, b + 1, b + 2); mb.Tri(b, b + 2, b + 3); }
    }
}
