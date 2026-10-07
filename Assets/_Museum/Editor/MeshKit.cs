using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Procedural, reasonably high-poly architecture meshes for the museum (editor only).
/// All UVs are in metres so tiling stone textures keep a constant scale; lightmap UVs are generated.
public static class MeshKit
{
    public const string Dir = "Assets/_Museum/Generated";
    static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
    public static void ClearCache() => cache.Clear();

    public enum Arch { Round, Pointed, Horseshoe, Cusped, Flat }

    // ------------------------------------------------------------------ builder
    public class MB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();

        public int V(Vector3 p, Vector2 u) { v.Add(p); uv.Add(u); return v.Count - 1; }
        public void Tri(int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// Quad from 4 points (counter-clockwise seen from the front), UVs from world metres along two axes.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 uAxis, Vector3 vAxis)
        {
            int i0 = V(a, new Vector2(Vector3.Dot(a, uAxis), Vector3.Dot(a, vAxis)));
            int i1 = V(b, new Vector2(Vector3.Dot(b, uAxis), Vector3.Dot(b, vAxis)));
            int i2 = V(c, new Vector2(Vector3.Dot(c, uAxis), Vector3.Dot(c, vAxis)));
            int i3 = V(d, new Vector2(Vector3.Dot(d, uAxis), Vector3.Dot(d, vAxis)));
            Quad(i0, i1, i2, i3);
        }

        /// Duplicates every triangle with reversed winding and its own vertices, so either side renders and lights correctly.
        public void DoubleSide()
        {
            int tc = t.Count, vc = v.Count;
            for (int i = 0; i < vc; i++) { v.Add(v[i]); uv.Add(uv[i]); }
            for (int i = 0; i < tc; i += 3) { t.Add(t[i] + vc); t.Add(t[i + 2] + vc); t.Add(t[i + 1] + vc); }
        }

        public void Append(MB o, Matrix4x4 m)
        {
            int b = v.Count;
            foreach (var p in o.v) v.Add(m.MultiplyPoint3x4(p));
            uv.AddRange(o.uv);
            foreach (var i in o.t) t.Add(i + b);
        }
    }

    public static Mesh Save(string key, MB mb, bool lightmapUV = true, bool doubleSided = false)
    {
        if (cache.TryGetValue(key, out var m)) return m;
        if (doubleSided) mb.DoubleSide();
        var path = $"{Dir}/{key}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var mesh = existing ? existing : new Mesh();
        mesh.Clear();
        mesh.name = key;
        mesh.indexFormat = mb.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(mb.v); mesh.SetUVs(0, mb.uv); mesh.SetTriangles(mb.t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        if (lightmapUV && mb.t.Count > 0)
        {
            var settings = new UnwrapParam(); UnwrapParam.SetDefaults(out settings);
            Unwrapping.GenerateSecondaryUVSet(mesh, settings);
        }
        if (!existing) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
        return cache[key] = mesh;
    }

    // ------------------------------------------------------------------ curves
    /// Arch outline in the (s, y) plane: s across the span (-span/2..span/2), y up from the springing line (0).
    /// Returns points from the left springing point over the top to the right springing point.
    public static List<Vector2> ArchCurve(Arch a, float span, float rise, int n = 32)
    {
        var pts = new List<Vector2>();
        float h = span / 2f;
        switch (a)
        {
            case Arch.Flat:
                pts.Add(new Vector2(-h, 0)); pts.Add(new Vector2(-h, rise)); pts.Add(new Vector2(h, rise)); pts.Add(new Vector2(h, 0));
                return pts;
            case Arch.Pointed:
            {   // two-centred arch: each half is an arc of radius R centred on the springing line
                float R = (h * h + rise * rise) / (2f * h);           // circle through (−h,0) centred at (−h+R,0) reaching apex (0,rise)
                float cx = -h + R;
                float a0 = Mathf.PI, a1 = Mathf.Atan2(rise, -cx);     // left half: angle from 180° down to the apex direction
                for (int i = 0; i <= n / 2; i++) { float t = Mathf.Lerp(a0, a1, i / (n / 2f)); pts.Add(new Vector2(cx + R * Mathf.Cos(t), R * Mathf.Sin(t))); }
                for (int i = n / 2 - 1; i >= 0; i--) { var p = pts[i]; pts.Add(new Vector2(-p.x, p.y)); }
                return pts;
            }
            case Arch.Horseshoe:
            {   // chaitya / horseshoe arch: a circle wider than the opening, continuing below its widest point
                float R = (h * h + rise * rise) / (2f * rise), cy = rise - R;
                float th0 = Mathf.Asin(Mathf.Clamp(-cy / R, -1f, 1f));          // angle where the circle meets the jambs
                for (int i = 0; i <= n; i++)
                {
                    float t = Mathf.Lerp(Mathf.PI + th0, -th0, i / (float)n);
                    pts.Add(new Vector2(R * Mathf.Cos(t), cy + R * Mathf.Sin(t)));
                }
                pts[0] = new Vector2(-h, 0); pts[pts.Count - 1] = new Vector2(h, 0);
                return pts;
            }
            case Arch.Cusped:
            {   // Mughal multifoil: a pointed arch with inward scallops (lobes)
                var basePts = ArchCurve(Arch.Pointed, span, rise, n * 2);
                int lobes = 7; var c = new Vector2(0, rise * 0.25f);
                for (int i = 0; i < basePts.Count; i++)
                {
                    float t = i / (float)(basePts.Count - 1);
                    float scallop = Mathf.Abs(Mathf.Sin(t * lobes * Mathf.PI));
                    float depth = 0.07f * span * (1f - scallop) * (t > 0.04f && t < 0.96f ? 1f : 0f);
                    var p = basePts[i]; var dir = (p - c).normalized;
                    pts.Add(p - dir * depth);
                }
                return pts;
            }
            default:
            {   // round / segmental (elliptical when rise != span/2)
                for (int i = 0; i <= n; i++) { float t = Mathf.PI * (1f - i / (float)n); pts.Add(new Vector2(h * Mathf.Cos(t), rise * Mathf.Sin(t))); }
                return pts;
            }
        }
    }

    /// Full opening outline (jambs + arch) from left bottom to right bottom, in (s, y) with y=0 at the floor.
    public static List<Vector2> Opening(Arch a, float span, float spring, float rise, int n = 32)
    {
        var pts = new List<Vector2> { new Vector2(-span / 2f, 0f) };
        foreach (var p in ArchCurve(a, span, rise, n)) pts.Add(new Vector2(p.x, p.y + spring));
        pts.Add(new Vector2(span / 2f, 0f));
        return pts;
    }

    // ------------------------------------------------------------------ generators
    /// Sweeps a 2D profile (x = outwards/side, y = up/out of wall) along a path. 'up' gives the profile's y axis at each point.
    public static void Sweep(MB mb, IList<Vector2> profile, IList<Vector3> path, IList<Vector3> ups, bool closedPath = false)
    {
        int pc = profile.Count, n = path.Count;
        float[] plen = new float[pc]; for (int i = 1; i < pc; i++) plen[i] = plen[i - 1] + (profile[i] - profile[i - 1]).magnitude;
        float along = 0f;
        int baseIndex = mb.v.Count;
        int rings = closedPath ? n + 1 : n;
        for (int k = 0; k < rings; k++)
        {
            int i = k % n;
            Vector3 tan = closedPath ? (path[(i + 1) % n] - path[(i - 1 + n) % n])
                                     : (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]);
            tan.Normalize();
            var up = ups[i].normalized;
            var side = Vector3.Cross(up, tan).normalized;
            up = Vector3.Cross(tan, side).normalized;
            // mitre correction at corners of closed polygons
            float mitre = 1f;
            if (closedPath)
            {
                var t0 = (path[i] - path[(i - 1 + n) % n]).normalized; var t1 = (path[(i + 1) % n] - path[i]).normalized;
                mitre = 1f / Mathf.Max(0.3f, Mathf.Cos(Vector3.Angle(t0, t1) * 0.5f * Mathf.Deg2Rad));
            }
            if (k > 0) along += (path[i] - path[(k - 1) % n]).magnitude;
            for (int j = 0; j < pc; j++)
                mb.V(path[i] + side * profile[j].x * mitre + up * profile[j].y, new Vector2(along, plen[j]));
        }
        for (int k = 0; k < rings - 1; k++)
            for (int j = 0; j < pc - 1; j++)
            {
                int a = baseIndex + k * pc + j, b = a + pc;
                mb.Quad(a, b, b + 1, a + 1);
            }
    }

    /// Solid of revolution around Y. profile: (radius, y) from bottom to top.
    public static void Lathe(MB mb, IList<Vector2> profile, int segments = 32, float sx = 1f, float sz = 1f)
    {
        int b = mb.v.Count, pc = profile.Count;
        float[] plen = new float[pc]; for (int i = 1; i < pc; i++) plen[i] = plen[i - 1] + (profile[i] - profile[i - 1]).magnitude;
        for (int s = 0; s <= segments; s++)
        {
            float a = s / (float)segments * Mathf.PI * 2f;
            for (int j = 0; j < pc; j++)
                mb.V(new Vector3(Mathf.Cos(a) * profile[j].x * sx, profile[j].y, Mathf.Sin(a) * profile[j].x * sz), new Vector2(s / (float)segments * 2f, plen[j]));
        }
        for (int s = 0; s < segments; s++)
            for (int j = 0; j < pc - 1; j++)
            {
                int a = b + s * pc + j, c = a + pc;
                mb.Quad(a, a + 1, c + 1, c);
            }
    }

    /// Box with UVs in metres.
    public static void Box(MB mb, Vector3 c, Vector3 size)
    {
        var h = size / 2f;
        Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
        mb.Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), Vector3.right, Vector3.up);   // -z
        mb.Quad(P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left, Vector3.up);       // +z
        mb.Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), Vector3.back, Vector3.up);   // -x
        mb.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.forward, Vector3.up);    // +x
        mb.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.right, Vector3.forward); // top
        mb.Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), Vector3.right, Vector3.forward); // bottom
    }

    /// Fills a star-shaped outline (fan from its centroid). Outline is in the local (x, y) plane, facing -z.
    public static void Fill(MB mb, IList<Vector2> outline, bool flip = false)
    {
        var c = Vector2.zero; foreach (var p in outline) c += p; c /= outline.Count;
        int ci = mb.V(new Vector3(c.x, c.y, 0), c);
        int b = mb.v.Count;
        foreach (var p in outline) mb.V(new Vector3(p.x, p.y, 0), p);
        for (int i = 0; i < outline.Count; i++)
        {
            int a = b + i, d = b + (i + 1) % outline.Count;
            if (flip) mb.Tri(ci, d, a); else mb.Tri(ci, a, d);
        }
    }

    // ------------------------------------------------------------------ architecture pieces (all built in local space)
    /// A wall slab in the local XY plane (front face towards -Z, thickness towards +Z), width W centred on x=0, from y=0 to H,
    /// with an arched opening. Triangulated radially from the opening centre so it works for any star-shaped arch.
    public static Mesh ArchWall(string key, float W, float H, float thick, Arch a, float span, float spring, float rise)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        var open = Opening(a, span, spring, rise, 40);
        var c = new Vector2(0, spring * 0.6f);
        // outer boundary point on the rectangle along the ray from c through each opening point
        Vector2 Outer(Vector2 p)
        {
            var d = (p - c).normalized; float best = float.MaxValue;
            if (Mathf.Abs(d.x) > 1e-5f) { float t = ((d.x > 0 ? W / 2 : -W / 2) - c.x) / d.x; if (t > 0) best = Mathf.Min(best, t); }
            if (Mathf.Abs(d.y) > 1e-5f) { float t = ((d.y > 0 ? H : 0) - c.y) / d.y; if (t > 0) best = Mathf.Min(best, t); }
            return c + d * best;
        }
        // densify the outer ring so corners are captured: add the 4 corners at the right places
        var inner = new List<Vector2>(); var outer = new List<Vector2>();
        var corners = new[] { new Vector2(-W / 2, 0), new Vector2(-W / 2, H), new Vector2(W / 2, H), new Vector2(W / 2, 0) };
        float Ang(Vector2 p) => Mathf.Atan2(p.y - c.y, p.x - c.x);
        // the opening runs clockwise (left jamb -> top -> right jamb): angles decrease; unwrap them so they stay monotonic
        var angs = new float[open.Count];
        angs[0] = Ang(open[0]);
        for (int i = 1; i < open.Count; i++)
        {
            float ang = Ang(open[i]);
            while (ang > angs[i - 1] + 1e-4f) ang -= Mathf.PI * 2f;
            angs[i] = ang;
        }
        for (int i = 0; i < open.Count; i++)
        {
            inner.Add(open[i]); outer.Add(Outer(open[i]));
            if (i < open.Count - 1)
            {
                float a0 = angs[i], a1 = angs[i + 1];
                var hits = new List<(float, Vector2)>();
                foreach (var k in corners)
                    for (int w = -2; w <= 1; w++)
                    {
                        float ak = Ang(k) + w * Mathf.PI * 2f;
                        if (ak < a0 && ak > a1) hits.Add((ak, k));
                    }
                foreach (var (ak, k) in hits.OrderByDescending(h => h.Item1))
                {
                    inner.Add(Vector2.Lerp(open[i], open[i + 1], (a0 - ak) / (a0 - a1)));
                    outer.Add(k);
                }
            }
        }
        // bottom strips (floor level between the jambs is open; the wall has no bottom face)
        for (int side = 0; side < 2; side++)
        {
            float z = side == 0 ? 0f : thick;
            int b = mb.v.Count;
            for (int i = 0; i < inner.Count; i++)
            {
                mb.V(new Vector3(inner[i].x, inner[i].y, z), inner[i]);
                mb.V(new Vector3(outer[i].x, outer[i].y, z), outer[i]);
            }
            for (int i = 0; i < inner.Count - 1; i++)
            {
                int i0 = b + i * 2, o0 = i0 + 1, i1 = i0 + 2, o1 = i0 + 3;
                if (side == 0) mb.Quad(i0, o0, o1, i1); else mb.Quad(i0, i1, o1, o0);
            }
        }
        // soffit (inside of the opening) and the wall's outer edges
        var soffitPath = open.Select(p => new Vector3(p.x, p.y, 0)).ToList();
        float run = 0;
        for (int i = 0; i < soffitPath.Count - 1; i++)
        {
            var p0 = soffitPath[i]; var p1 = soffitPath[i + 1]; float l = (p1 - p0).magnitude;
            int b = mb.v.Count;
            mb.V(p0, new Vector2(run, 0)); mb.V(p1, new Vector2(run + l, 0));
            mb.V(p1 + Vector3.forward * thick, new Vector2(run + l, thick)); mb.V(p0 + Vector3.forward * thick, new Vector2(run, thick));
            mb.Quad(b, b + 1, b + 2, b + 3);
            run += l;
        }
        return Save(key, mb);
    }

    /// Moulding (several stepped rings) following an arch outline, standing proud of a wall plane (local XY, facing -Z).
    public static Mesh ArchMoulding(string key, Arch a, float span, float spring, float rise, float width = 0.28f, float depth = 0.12f, int steps = 3)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        var outline = Opening(a, span, spring, rise, 48);
        var path = outline.Select(p => new Vector3(p.x, p.y, 0)).ToList();
        // offset path outward so the moulding frames the opening; profile: stepped torus/fillet bands
        var prof = new List<Vector2>();
        for (int s = 0; s < steps; s++)
        {
            float x0 = s * width / steps, x1 = (s + 1) * width / steps, d = depth * (1f - s * 0.28f);
            prof.Add(new Vector2(x0, d * 0.4f)); prof.Add(new Vector2(x0 + (x1 - x0) * 0.2f, d)); prof.Add(new Vector2(x0 + (x1 - x0) * 0.8f, d)); prof.Add(new Vector2(x1, d * 0.4f));
        }
        prof.Add(new Vector2(width, 0f));
        prof.Insert(0, new Vector2(0f, 0f));
        // 'up' for the profile = out of the wall (-Z); side = outward from the opening
        var ups = path.Select(_ => Vector3.back).ToList();
        // sweeping left -> over the top -> right, cross(up, tangent) points into the opening: negate x to frame it from outside
        Sweep(mb, prof.Select(p => new Vector2(-p.x, p.y)).ToList(), path, ups);
        return Save(key, mb, true, true);
    }

    /// Barrel vault across a hall: inner surface of the arch curve (span across Z) extruded along X.
    public static Mesh Vault(string key, Arch a, float span, float rise, float length)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        var curve = ArchCurve(a == Arch.Cusped || a == Arch.Horseshoe ? (a == Arch.Cusped ? Arch.Pointed : Arch.Round) : a, span, rise, 40);
        float run = 0; var runs = new float[curve.Count];
        for (int i = 1; i < curve.Count; i++) runs[i] = run += (curve[i] - curve[i - 1]).magnitude;
        int segs = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
        for (int s = 0; s <= segs; s++)
            for (int i = 0; i < curve.Count; i++)
                mb.V(new Vector3(length * s / segs, curve[i].y, curve[i].x), new Vector2(length * s / segs, runs[i]));
        int pc = curve.Count;
        for (int s = 0; s < segs; s++)
            for (int i = 0; i < pc - 1; i++)
            {
                int q = s * pc + i, r = q + pc;
                mb.Quad(q, r, r + 1, q + 1);                    // faces downwards / inwards
            }
        return Save(key, mb);
    }

    /// Rib following the vault curve at x = 0 (profile: chamfered rectangle hanging below the vault surface).
    public static Mesh Rib(string key, Arch a, float span, float rise, float w = 0.36f, float d = 0.28f)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        var curve = ArchCurve(a == Arch.Cusped || a == Arch.Horseshoe ? (a == Arch.Cusped ? Arch.Pointed : Arch.Round) : a, span, rise, 40);
        var path = curve.Select(p => new Vector3(0, p.y, p.x)).ToList();
        var c = new Vector3(0, -rise, 0);
        var ups = path.Select(p => (c - p).normalized).ToList();     // towards the hall centre (down/inward)
        var prof = new List<Vector2> { new Vector2(-w / 2, 0), new Vector2(-w / 2, d * 0.55f), new Vector2(-w / 2 + 0.05f, d * 0.8f), new Vector2(-w * 0.18f, d),
                                       new Vector2(w * 0.18f, d), new Vector2(w / 2 - 0.05f, d * 0.8f), new Vector2(w / 2, d * 0.55f), new Vector2(w / 2, 0) };
        Sweep(mb, prof, path, ups);
        return Save(key, mb, true, true);
    }

    /// Straight moulding (cornice / skirting) along +X of given length. Profile in (z out from wall, y up).
    public static Mesh Cornice(string key, float length, IList<Vector2> profile)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        var path = new List<Vector3> { Vector3.zero, new Vector3(length, 0, 0) };
        // profile.x -> out of the wall (local -Z), profile.y -> up
        var ups = new List<Vector3> { Vector3.up, Vector3.up };
        var prof = profile.Select(p => new Vector2(p.x, p.y)).ToList();
        Sweep(mb, prof, path, ups);
        return Save(key, mb, true, true);
    }

    /// Picture frame moulding around a w x h rectangle (in local XY, facing -Z).
    public static Mesh Frame(string key, float w, float h, float fw = 0.09f, float fd = 0.06f)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        var path = new List<Vector3> { new Vector3(-w / 2, -h / 2), new Vector3(-w / 2, h / 2), new Vector3(w / 2, h / 2), new Vector3(w / 2, -h / 2) };
        var ups = path.Select(_ => Vector3.back).ToList();
        var prof = new List<Vector2> { new Vector2(0, 0), new Vector2(0, fd * 0.5f), new Vector2(fw * 0.15f, fd * 0.85f), new Vector2(fw * 0.35f, fd),
                                       new Vector2(fw * 0.55f, fd * 0.8f), new Vector2(fw * 0.7f, fd * 0.95f), new Vector2(fw * 0.9f, fd * 0.7f), new Vector2(fw, fd * 0.3f), new Vector2(fw, 0) };
        // profile x must grow outward from the picture: path runs clockwise seen from the front, so negate x
        Sweep(mb, prof.Select(p => new Vector2(-p.x, p.y)).ToList(), path, ups, true);
        return Save(key, mb, true, true);
    }

    /// Plinth: square base + shaft + cap with mouldings, centred on the origin, standing on y = 0.
    public static Mesh Plinth(string key, float w, float d, float h)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        Box(mb, new Vector3(0, 0.06f, 0), new Vector3(w + 0.12f, 0.12f, d + 0.12f));
        Box(mb, new Vector3(0, 0.15f, 0), new Vector3(w + 0.06f, 0.06f, d + 0.06f));
        Box(mb, new Vector3(0, (h - 0.06f) / 2f + 0.09f, 0), new Vector3(w, h - 0.3f, d));
        Box(mb, new Vector3(0, h - 0.13f, 0), new Vector3(w + 0.05f, 0.04f, d + 0.05f));
        Box(mb, new Vector3(0, h - 0.05f, 0), new Vector3(w + 0.1f, 0.1f, d + 0.1f));
        return Save(key, mb);
    }

    // ------------------------------------------------------------------ columns (lathe profiles), all 1 unit = 1 m, height h
    public static Mesh Column(string key, string style, float h, float r = 0.2f)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB();
        var p = new List<Vector2>();
        void Add(float rr, float y) => p.Add(new Vector2(rr * r / 0.2f, y));
        switch (style)
        {
            case "ashokan":      // polished round shaft with an inverted-lotus bell capital
                Add(0.001f, 0); Add(0.26f, 0); Add(0.26f, 0.12f); Add(0.21f, 0.18f); Add(0.2f, 0.25f); Add(0.17f, h * 0.82f);
                Add(0.2f, h * 0.84f); Add(0.27f, h * 0.9f); Add(0.25f, h * 0.94f); Add(0.28f, h * 0.95f); Add(0.28f, h); Add(0.001f, h); break;
            case "gupta":        // pot-and-foliage capital (purna-ghata)
                Add(0.001f, 0); Add(0.27f, 0); Add(0.27f, 0.3f); Add(0.2f, 0.34f); Add(0.18f, h * 0.7f); Add(0.25f, h * 0.76f); Add(0.28f, h * 0.82f);
                Add(0.24f, h * 0.88f); Add(0.3f, h * 0.92f); Add(0.3f, h); Add(0.001f, h); break;
            case "mughal":       // baluster with lotus base and bracket capital
                Add(0.001f, 0); Add(0.26f, 0); Add(0.26f, 0.1f); Add(0.22f, 0.16f); Add(0.25f, 0.3f); Add(0.17f, 0.45f); Add(0.15f, h * 0.55f);
                Add(0.16f, h * 0.75f); Add(0.2f, h * 0.82f); Add(0.17f, h * 0.86f); Add(0.24f, h * 0.92f); Add(0.3f, h * 0.96f); Add(0.3f, h); Add(0.001f, h); break;
            case "gothic":       // slender column with base, ring and foliate capital
                Add(0.001f, 0); Add(0.25f, 0); Add(0.25f, 0.18f); Add(0.2f, 0.24f); Add(0.21f, 0.3f); Add(0.15f, 0.36f); Add(0.14f, h * 0.84f);
                Add(0.17f, h * 0.86f); Add(0.15f, h * 0.88f); Add(0.24f, h * 0.95f); Add(0.27f, h * 0.97f); Add(0.27f, h); Add(0.001f, h); break;
            case "delhi":        // Lutyens' Delhi order: bell capital
                Add(0.001f, 0); Add(0.28f, 0); Add(0.28f, 0.2f); Add(0.2f, 0.26f); Add(0.19f, h * 0.8f); Add(0.22f, h * 0.82f); Add(0.2f, h * 0.84f);
                Add(0.26f, h * 0.9f); Add(0.24f, h * 0.93f); Add(0.3f, h * 0.95f); Add(0.3f, h); Add(0.001f, h); break;
            default:             // modern: plain cylinder
                Add(0.001f, 0); Add(0.2f, 0); Add(0.2f, h); Add(0.001f, h); break;
        }
        Lathe(mb, p, 32);
        return Save(key, mb);
    }

    /// Vase / lamp shapes for decoration and placeholders.
    public static Mesh LatheShape(string key, IList<Vector2> profile, int seg = 32)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var mb = new MB(); Lathe(mb, profile, seg);
        return Save(key, mb);
    }

    /// Torus ring (for chandeliers, rope).
    public static void Torus(MB mb, float R, float r, int seg = 48, int side = 10, float y = 0)
    {
        var circle = new List<Vector2>();
        for (int i = 0; i <= side; i++) { float a = i / (float)side * Mathf.PI * 2; circle.Add(new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a))); }
        var path = new List<Vector3>(); var ups = new List<Vector3>();
        for (int i = 0; i < seg; i++) { float a = i / (float)seg * Mathf.PI * 2; path.Add(new Vector3(R * Mathf.Cos(a), y, R * Mathf.Sin(a))); ups.Add(Vector3.up); }
        Sweep(mb, circle, path, ups, true);
    }

    /// Tube along a path (rope, chain, curved iron bars).
    public static void Tube(MB mb, IList<Vector3> path, float r, int side = 8)
    {
        var circle = new List<Vector2>();
        for (int i = 0; i <= side; i++) { float a = i / (float)side * Mathf.PI * 2; circle.Add(new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a))); }
        var ups = new List<Vector3>();
        for (int i = 0; i < path.Count; i++)
        {
            var tan = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
            ups.Add(Mathf.Abs(Vector3.Dot(tan, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up);
        }
        Sweep(mb, circle, path, ups);
    }
}
