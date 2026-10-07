using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Arch = MeshKit.Arch;

/// Era wings v3: spacious stone halls with vaults, ribs, carved arches, jali windows, chandeliers, display stands,
/// a showcase centrepiece per section, a brass floor timeline with years and an interactive kiosk per section.
public static partial class MuseumBuilder
{
    // ------------------------------------------------------------------ hall dimensions (metres)
    const float HW2 = 6f;          // half width (hall is 12 m wide)
    const float WALLH = 5.2f;      // springing line of the vault
    const float BAY = 5.4f;        // bay length; two exhibit slots per wall per bay
    const float BW = 1.35f, BH = 1.86f, BY = 1.75f;   // exhibit board size and centre height
    const float TPZ = 3.0f;        // visitor stand line for wall exhibits (z = ±3)
    const float STANDZ = 4.6f;     // display stands in front of the boards

    class EraStyle
    {
        public string id, wall, wallAlt, floor, vault, trim, rib, column, chandelier, jali, glow;
        public Arch arch; public Arch vaultArch; public float vaultRise; public bool flatCeiling;
        public Color wallTint = Color.white, trimTint = Color.white;
    }

    static readonly Dictionary<string, EraStyle> Styles = new Dictionary<string, EraStyle>
    {   // Ancient: buff sandstone, chaitya (horseshoe) arches, a barrel vault with teak ribs like the Karla cave hall
        ["W1"] = new EraStyle { id = "W1", wall = "Era_W1_Wall", wallAlt = "Era_Brick", floor = "Era_W1_Floor", vault = "Era_W1_Vault", trim = "Era_W1_Trim", rib = "Teak",
                             column = "ashokan", chandelier = "brass", jali = "Jali_Diamond", glow = "Glow_Warm", arch = Arch.Horseshoe, vaultArch = Arch.Round, vaultRise = 3.4f },
        // Classical: Gupta sandstone, round arches, stone ribs, pot-and-foliage pillars
        ["W2"] = new EraStyle { id = "W2", wall = "Era_W2_Wall", wallAlt = "Era_W2_Wall", floor = "Era_W2_Floor", vault = "Era_W2_Vault", trim = "Era_W2_Trim", rib = "Era_W2_Trim",
                             column = "gupta", chandelier = "brass", jali = "Jali_Diamond", glow = "Glow_Warm", arch = Arch.Round, vaultArch = Arch.Round, vaultRise = 3.0f },
        // Medieval: red sandstone with white marble, Mughal cusped arches, star jalis, lanterns
        ["W3"] = new EraStyle { id = "W3", wall = "Era_W3_Wall", wallAlt = "Era_Marble", floor = "Era_W3_Floor", vault = "Era_W3_Vault", trim = "Era_Marble", rib = "Era_Marble",
                             column = "mughal", chandelier = "lantern", jali = "Jali_Star", glow = "Glow_Warm", arch = Arch.Cusped, vaultArch = Arch.Pointed, vaultRise = 4.0f },
        // Colonial: Indo-Saracenic / Gothic stone, pointed ribbed vaults, leaded lattice windows, iron chandeliers with track spots
        ["W4"] = new EraStyle { id = "W4", wall = "Era_W4_Wall", wallAlt = "Era_W4_Wall", floor = "Era_W4_Floor", vault = "Era_W4_Vault", trim = "Era_W4_Trim", rib = "Era_W4_Trim",
                             column = "gothic", chandelier = "iron", jali = "Jali_Lead", glow = "Glow_Sky", arch = Arch.Pointed, vaultArch = Arch.Pointed, vaultRise = 4.2f },
        // Freedom & Republic: Lutyens' Delhi buff and red sandstone, round arches, hexagonal jalis
        ["W5"] = new EraStyle { id = "W5", wall = "Era_W5_Wall", wallAlt = "Era_W3_Wall", floor = "Era_W5_Floor", vault = "Era_W5_Vault", trim = "Era_W5_Trim", rib = "Era_W3_Wall",
                             column = "delhi", chandelier = "iron", jali = "Jali_Hex", glow = "Glow_Warm", arch = Arch.Round, vaultArch = Arch.Round, vaultRise = 3.0f },
        // Themes: modern gallery, plaster and polished stone, flat ceiling with skylights
        ["W6"] = new EraStyle { id = "W6", wall = "Era_W6_Wall", wallAlt = "Era_W6_Wall", floor = "Era_W6_Floor", vault = "Era_W6_Vault", trim = "Era_W6_Trim", rib = "Era_W6_Trim",
                             column = "modern", chandelier = "pendant", jali = "Jali_Grid", glow = "Glow_Sky", arch = Arch.Flat, vaultArch = Arch.Round, vaultRise = 0f, flatCeiling = true },
    };

    // ------------------------------------------------------------------ PBR materials (Poly Haven CC0 textures when present)
    const string PHTex = "Assets/_Museum/PolyHaven/Textures";

    static Material Pbr(string name, string texKey, float tileMetres, Color tint, float smooth, Color fallback)
    {
        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PHTex}/{texKey}_diff.jpg");
        var nor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PHTex}/{texKey}_nor.jpg");
        var m = Lit(name, diff ? tint : fallback, 0f, smooth);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", diff);
        m.mainTexture = diff;
        var scale = Vector2.one / Mathf.Max(0.1f, tileMetres);
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", scale);
        if (nor && m.HasProperty("_BumpMap"))
        {
            m.SetTexture("_BumpMap", nor); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
        }
        else m.DisableKeyword("_NORMALMAP");
        return m;
    }

    static Material Glass(string name, Color c)
    {
        var m = Lit(name, c, 0f, 0.95f);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    static Material GlowMat(string name, Color c, float intensity)
    {
        var m = Emissive(name, c, intensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;     // windows light the halls in the bake
        return m;
    }

    static Material JaliMat(string name, string file, float tile, Color tint)
    {
        var m = TexMat(name, file, 0.2f, alphaClip: true);
        m.SetColor("_BaseColor", tint);
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", Vector2.one / tile);
        return m;
    }

    static void CreateEraMaterials()
    {
        var buff = new Color(0.86f, 0.74f, 0.56f); var red = new Color(0.62f, 0.33f, 0.24f); var grey = new Color(0.7f, 0.66f, 0.6f);
        Pbr("Era_W1_Wall", "stone_buff", 2.4f, Color.white, 0.15f, buff);
        Pbr("Era_W1_Floor", "floor_slabs", 2.0f, new Color(0.95f, 0.9f, 0.85f), 0.35f, grey);
        Pbr("Era_W1_Vault", "plaster_rough", 3f, new Color(0.95f, 0.88f, 0.76f), 0.1f, buff);
        Pbr("Era_W1_Trim", "stone_buff2", 1.6f, Color.white, 0.2f, buff);
        Pbr("Era_Brick", "brick_red", 1.8f, Color.white, 0.15f, red);
        Pbr("Era_W2_Wall", "stone_buff2", 2.4f, Color.white, 0.15f, buff);
        Pbr("Era_W2_Floor", "floor_slabs", 2.0f, new Color(0.9f, 0.86f, 0.8f), 0.35f, grey);
        Pbr("Era_W2_Vault", "stone_buff", 3f, new Color(1f, 0.95f, 0.88f), 0.12f, buff);
        Pbr("Era_W2_Trim", "stone_buff", 1.5f, new Color(1f, 0.96f, 0.9f), 0.25f, buff);
        Pbr("Era_W3_Wall", "stone_red", 2.2f, Color.white, 0.15f, red);
        Pbr("Era_W3_Floor", "floor_marble", 2.0f, Color.white, 0.6f, new Color(0.9f, 0.9f, 0.88f));
        Pbr("Era_W3_Vault", "plaster", 3f, new Color(1f, 0.98f, 0.94f), 0.15f, new Color(0.95f, 0.93f, 0.9f));
        Pbr("Era_Marble", "marble", 1.5f, Color.white, 0.7f, new Color(0.94f, 0.93f, 0.9f));
        Pbr("Era_W4_Wall", "stone_castle", 2.6f, Color.white, 0.15f, grey);
        Pbr("Era_W4_Floor", "floor_pattern", 2.2f, Color.white, 0.45f, grey);
        Pbr("Era_W4_Vault", "stone_castle", 3f, new Color(1f, 0.96f, 0.9f), 0.12f, grey);
        Pbr("Era_W4_Trim", "stone_buff2", 1.4f, new Color(1f, 0.97f, 0.92f), 0.25f, buff);
        Pbr("Era_W5_Wall", "stone_buff", 2.4f, new Color(1f, 0.95f, 0.88f), 0.15f, buff);
        Pbr("Era_W5_Floor", "floor_granite", 2.0f, Color.white, 0.55f, grey);
        Pbr("Era_W5_Vault", "plaster", 3f, new Color(1f, 0.95f, 0.88f), 0.1f, buff);
        Pbr("Era_W5_Trim", "stone_red", 1.4f, Color.white, 0.2f, red);
        Pbr("Era_W6_Wall", "plaster", 3f, new Color(0.97f, 0.96f, 0.94f), 0.2f, new Color(0.93f, 0.92f, 0.9f));
        Pbr("Era_W6_Floor", "floor_concrete", 2.5f, new Color(0.85f, 0.85f, 0.85f), 0.7f, new Color(0.6f, 0.6f, 0.6f));
        Pbr("Era_W6_Vault", "plaster", 3f, Color.white, 0.1f, Color.white);
        Pbr("Era_W6_Trim", "granite", 1.5f, new Color(0.8f, 0.8f, 0.8f), 0.6f, new Color(0.4f, 0.4f, 0.42f));
        Pbr("Era_Wood", "wood", 1.2f, new Color(0.75f, 0.55f, 0.4f), 0.55f, new Color(0.35f, 0.2f, 0.1f));
        Pbr("Era_Granite", "granite", 1.2f, Color.white, 0.6f, new Color(0.35f, 0.33f, 0.32f));
        Lit("Iron", new Color(0.12f, 0.11f, 0.1f), 0.8f, 0.45f);
        Lit("Candle", new Color(0.95f, 0.92f, 0.85f), 0f, 0.3f);
        Lit("Velvet", new Color(0.45f, 0.05f, 0.07f), 0f, 0.15f);
        Lit("Screen_Bezel", new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.7f);
        Glass("Glass", new Color(0.85f, 0.95f, 1f, 0.12f));
        GlowMat("Glow_Warm", new Color(1f, 0.86f, 0.62f), 2.2f);
        GlowMat("Glow_Sky", new Color(0.86f, 0.92f, 1f), 2.4f);
        GlowMat("Bulb", new Color(1f, 0.8f, 0.5f), 4f);
        JaliMat("Jali_Diamond", "UI/jali_diamond.png", 0.7f, new Color(0.95f, 0.85f, 0.7f));
        JaliMat("Jali_Star", "UI/jali_star.png", 0.9f, Color.white);
        JaliMat("Jali_Hex", "UI/jali_hex.png", 0.8f, Color.white);
        JaliMat("Jali_Lead", "UI/lattice_lead.png", 0.6f, Color.white);
        JaliMat("Jali_Grid", "UI/grid_modern.png", 1.2f, Color.white);
        TexMat("India_Map", "UI/india_map.png");
    }

    // ------------------------------------------------------------------ placement helpers
    static GameObject MeshObj(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Quaternion rot, bool isStatic = true, Vector3? scale = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        if (scale.HasValue) go.transform.localScale = scale.Value;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (isStatic) StaticAll(go);
        return go;
    }

    static void StaticAll(GameObject go) =>
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                   StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

    static void LightmappedOnly(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
    }

    /// Wall quad facing 'normal' (towards the hall), UVs in metres.
    static GameObject WallQuad(string name, Vector3 a, Vector3 b, float y0, float y1, Material m, Transform p)
    {
        var mb = new MeshKit.MB();
        var dir = (b - a).normalized;
        mb.Quad(new Vector3(a.x, y0, a.z), new Vector3(a.x, y1, a.z), new Vector3(b.x, y1, b.z), new Vector3(b.x, y0, b.z),
                new Vector3(dir.x, 0, dir.z), Vector3.up);
        var key = $"Wall_{(b - a).magnitude:0.##}x{y1 - y0:0.##}_{Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg:0}";
        // vertices are absolute; bake them relative to 'a' so the mesh can be shared
        for (int i = 0; i < mb.v.Count; i++) mb.v[i] -= new Vector3(a.x, 0, a.z);
        return MeshObj(name, MeshKit.Save(key, mb), m, p, new Vector3(a.x, 0, a.z), Quaternion.identity);
    }

    static GameObject FloorQuad(string name, float x0, float x1, float z0, float z1, float y, Material m, Transform p, bool down = false)
    {
        var mb = new MeshKit.MB();
        if (!down) mb.Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.right, Vector3.forward);
        else mb.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.right, Vector3.forward);
        // keep UVs world-anchored (so tiles line up across rooms) but geometry local to x0
        for (int i = 0; i < mb.v.Count; i++) mb.v[i] -= new Vector3(x0, 0, 0);
        var key = $"Plane_{(down ? "d" : "u")}_{x1 - x0:0.##}x{z1 - z0:0.##}_{x0 % 10f:0.#}";
        return MeshObj(name, MeshKit.Save(key, mb), m, p, new Vector3(x0, 0, 0), Quaternion.identity);
    }

    /// Rotation so that local -Z (the "front" of MeshKit wall pieces) faces 'normal'.
    static Quaternion FaceTo(Vector3 normal) => Quaternion.LookRotation(-normal, Vector3.up);

    static TextMeshPro FloorText(string name, string text, Vector3 pos, float width, float height, Color c, Transform p, float max = 3f)
    {
        var t = Text3D(name, text, pos, Vector3.up, new Vector2(width, height), c, p, true, max);
        t.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.right);      // lies flat, reads while walking +X
        return t;
    }

    // ------------------------------------------------------------------ architecture pieces
    static float ArchRise(EraStyle s, float span) => s.arch switch
    {
        Arch.Pointed => span * 0.62f,
        Arch.Cusped => span * 0.6f,
        Arch.Horseshoe => span * 0.58f,
        Arch.Flat => 0.35f,
        _ => span * 0.42f,
    };

    /// Cross wall at x with a moulded doorway (and two jali windows) between two halls.
    static void Partition2(Transform parent, EraStyle s, float x, string titleWest, string subtitleWest, bool windowsWest = true)
    {
        var g = Group(parent, $"Partition_{x:0.#}");
        float crown = s.flatCeiling ? 6.4f : WALLH + s.vaultRise;
        const float thick = 0.9f, span = 5.2f, spring = 3.6f;
        float rise = s.arch == Arch.Flat ? 0.4f : ArchRise(s, span);
        var wall = MeshKit.ArchWall($"PartWall_{s.arch}_{crown:0.#}", 2 * HW2 + 0.4f, crown + 0.2f, thick, s.arch, span, spring, rise);
        // local front (z=0) faces -Z; rotate so it faces -X (west), thickness runs east
        MeshObj("Wall", wall, M[s.wall], g, new Vector3(x - thick / 2, 0, 0), Quaternion.Euler(0, 90, 0));
        // nested mouldings on both faces
        foreach (var (face, off) in new[] { (Vector3.left, -thick / 2 - 0.005f), (Vector3.right, thick / 2 + 0.005f) })
        {
            var m1 = MeshKit.ArchMoulding($"DoorMould_{s.arch}", s.arch, span, spring, rise, 0.3f, 0.14f, 3);
            var m2 = MeshKit.ArchMoulding($"DoorMould2_{s.arch}", s.arch, span + 0.9f, spring, rise + 0.45f, 0.18f, 0.08f, 2);
            MeshObj("DoorMoulding", m1, M[s.trim], g, new Vector3(x + off, 0, 0), FaceTo(face));
            MeshObj("DoorMoulding2", m2, M[s.trim], g, new Vector3(x + off, 0, 0), FaceTo(face));
            if (s.arch != Arch.Flat)
            {
                foreach (var z in new[] { -span / 2 - 0.25f, span / 2 + 0.25f })
                    MeshObj("DoorColumn", MeshKit.Column($"Col_{s.column}_{spring:0.#}", s.column, spring, 0.17f), M[s.trim], g, new Vector3(x + off * 1.25f, 0, z), Quaternion.identity);
            }
            if (windowsWest || face == Vector3.right)
                foreach (var z in new[] { -4.0f, 4.0f })
                    JaliWindow(g, s, new Vector3(x + off, 0, z), face, 1.5f, 1.4f, 3.4f, true);
        }
        if (!string.IsNullOrEmpty(titleWest))
        {
            float ty = spring + rise + (s.flatCeiling ? 0.55f : 0.75f);
            var t = Text3D("Title", titleWest, new Vector3(x - thick / 2 - 0.03f, ty, 0), Vector3.left, new Vector2(7.5f, 0.7f), new Color(0.98f, 0.82f, 0.45f), g, true, 8f);
            t.outlineWidth = 0.12f; t.outlineColor = new Color32(50, 25, 10, 255);
            if (!string.IsNullOrEmpty(subtitleWest))
                Text3D("Subtitle", subtitleWest, new Vector3(x - thick / 2 - 0.03f, ty - 0.55f, 0), Vector3.left, new Vector2(7f, 0.38f), new Color(1f, 0.92f, 0.78f), g, false, 4f);
        }
    }

    /// Arched window with a glowing pane behind a jali / lattice screen and a moulded frame. 'face' = direction it faces.
    static void JaliWindow(Transform p, EraStyle s, Vector3 basePos, Vector3 face, float span, float y0, float y1, bool withSill)
    {
        var g = Group(p, "JaliWindow");
        float rise = s.arch == Arch.Flat ? 0.2f : Mathf.Min(ArchRise(s, span), (y1 - y0) * 0.45f);
        float spring = y1 - y0 - rise;
        var outline = MeshKit.Opening(s.arch == Arch.Flat ? Arch.Flat : s.arch, span, spring, rise, 40);
        var key = $"Win_{s.arch}_{span:0.##}_{spring:0.##}_{rise:0.##}";
        var mb = new MeshKit.MB(); MeshKit.Fill(mb, outline);
        var fill = MeshKit.Save(key, mb);
        var rot = FaceTo(face);
        var origin = basePos + Vector3.up * y0;
        MeshObj("Glow", fill, M[s.glow], g, origin - face * 0.02f, rot);
        var jali = MeshObj("Jali", fill, M[s.jali], g, origin + face * 0.005f, rot);
        GameObjectUtility.SetStaticEditorFlags(jali, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        MeshObj("Frame", MeshKit.ArchMoulding($"WinMould_{key}", s.arch, span, spring, rise, 0.16f, 0.08f, 2), M[s.trim], g, origin + face * 0.01f, rot);
        if (withSill)
        {
            var sill = Box("Sill", origin + face * 0.08f + Vector3.down * 0.06f, new Vector3(span + 0.3f, 0.12f, 0.22f), M[s.trim], g);
            sill.transform.rotation = rot; StaticAll(sill);
        }
    }

    /// Blind arcade: an arched moulded frame on a side wall enclosing two exhibit boards, with a jali tympanum.
    static void BlindArch(Transform p, EraStyle s, float xc, float zWall, Vector3 normal)
    {
        var g = Group(p, "BlindArch");
        float span = BAY - 0.7f, spring = 3.0f;
        float rise = s.arch == Arch.Flat ? 0.3f : Mathf.Min(ArchRise(s, span), WALLH - spring - 0.35f);
        var rot = FaceTo(normal);
        var basePos = new Vector3(xc, 0, zWall) + normal * 0.012f;
        MeshObj("Moulding", MeshKit.ArchMoulding($"BlindMould_{s.arch}_{rise:0.##}", s.arch, span, spring, rise, 0.24f, 0.1f, 3), M[s.trim], g, basePos, rot);
        if (s.arch != Arch.Flat)
        {   // tympanum: jali in the arch head above the boards
            var curve = MeshKit.ArchCurve(s.arch, span - 0.3f, rise - 0.15f, 40);
            var mb = new MeshKit.MB(); MeshKit.Fill(mb, curve);
            var head = MeshKit.Save($"Tymp_{s.arch}_{rise:0.##}", mb);
            MeshObj("TympGlow", head, M[s.glow], g, basePos + Vector3.up * (spring + 0.08f) - normal * 0.004f, rot);
            var j = MeshObj("TympJali", head, M[s.jali], g, basePos + Vector3.up * (spring + 0.08f) + normal * 0.004f, rot);
            GameObjectUtility.SetStaticEditorFlags(j, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            var lintel = Box("Lintel", basePos + Vector3.up * (spring - 0.02f) + normal * 0.06f, new Vector3(span - 0.1f, 0.14f, 0.12f), M[s.trim], g);
            lintel.transform.rotation = rot; StaticAll(lintel);
        }
    }

    /// One hall segment (x0..x1): floor, walls with cornice and skirting, vault or flat ceiling, pilasters and ribs at bays.
    static void HallShell(Transform arch, EraStyle s, float x0, float x1, bool ribs = true)
    {
        float len = x1 - x0;
        FloorQuad("Floor", x0, x1, -HW2, HW2, 0f, M[s.floor], arch);
        WallQuad("Wall_L", new Vector3(x1, 0, HW2), new Vector3(x0, 0, HW2), 0, WALLH + 0.02f, M[s.wall], arch);
        WallQuad("Wall_R", new Vector3(x0, 0, -HW2), new Vector3(x1, 0, -HW2), 0, WALLH + 0.02f, M[s.wall], arch);
        if (s.flatCeiling)
        {
            WallQuad("Wall_L_Upper", new Vector3(x1, 0, HW2), new Vector3(x0, 0, HW2), WALLH, 6.42f, M[s.wall], arch);
            WallQuad("Wall_R_Upper", new Vector3(x0, 0, -HW2), new Vector3(x1, 0, -HW2), WALLH, 6.42f, M[s.wall], arch);
            FloorQuad("Ceiling", x0, x1, -HW2, HW2, 6.4f, M[s.vault], arch, true);
            for (float x = x0 + BAY / 2; x < x1; x += BAY)
            {   // skylight
                var sky = Box("Skylight", new Vector3(x, 6.38f, 0), new Vector3(3.2f, 0.02f, 4.4f), M["Glow_Sky"], arch); StaticAll(sky);
                var grid = Panel("SkylightGrid", new Vector3(x, 6.36f, 0), Vector3.down, 3.2f, 4.4f, M["Jali_Grid"], arch);
            }
        }
        else
        {
            MeshObj("Vault", MeshKit.Vault($"Vault_{s.vaultArch}_{s.vaultRise:0.#}_{len:0.##}", s.vaultArch, 2 * HW2, s.vaultRise, len), M[s.vault], arch, new Vector3(x0, WALLH, 0), Quaternion.identity);
        }
        // cornice at the springing line and skirting at the floor (both walls)
        var cornice = new List<Vector2> { new Vector2(0, 0), new Vector2(0.05f, 0.03f), new Vector2(0.09f, 0.1f), new Vector2(0.16f, 0.16f), new Vector2(0.22f, 0.24f),
                                          new Vector2(0.26f, 0.3f), new Vector2(0.24f, 0.34f), new Vector2(0f, 0.34f) };
        var skirting = new List<Vector2> { new Vector2(0, 0), new Vector2(0.07f, 0), new Vector2(0.07f, 0.16f), new Vector2(0.04f, 0.2f), new Vector2(0.02f, 0.24f), new Vector2(0, 0.26f) };
        var cm = MeshKit.Cornice($"Cornice_{len:0.##}", len, cornice);
        var sm = MeshKit.Cornice($"Skirting_{len:0.##}", len, skirting);
        foreach (var (z, n) in new[] { (HW2, Vector3.back), (-HW2, Vector3.forward) })
        {
            // Cornice() runs along +X with profile x towards local -Z: rotate so local -Z points into the hall
            var rot = FaceTo(n);
            var start = n == Vector3.back ? new Vector3(x0, 0, z) : new Vector3(x1, 0, z);
            MeshObj("Cornice", cm, M[s.trim], arch, start + Vector3.up * (WALLH - 0.34f), rot);
            MeshObj("Skirting", sm, M[s.trim], arch, start, rot);
        }
        // pilasters + ribs at bay boundaries
        for (float x = x0; x <= x1 + 0.01f; x += BAY)
        {
            foreach (var z in new[] { HW2 - 0.2f, -HW2 + 0.2f })
            {
                MeshObj("Pilaster", MeshKit.Column($"Col_{s.column}_{WALLH:0.#}", s.column, WALLH - 0.34f, 0.22f), M[s.trim], arch, new Vector3(x, 0, z), Quaternion.identity);
                if (!s.flatCeiling)
                {
                    var corbel = Box("Corbel", new Vector3(x, WALLH - 0.2f, z - Mathf.Sign(z) * 0.1f), new Vector3(0.6f, 0.4f, 0.6f), M[s.trim], arch); StaticAll(corbel);
                }
            }
            if (ribs && !s.flatCeiling)
                MeshObj("Rib", MeshKit.Rib($"Rib_{s.vaultArch}_{s.vaultRise:0.#}", s.vaultArch, 2 * HW2, s.vaultRise, 0.42f, 0.32f), M[s.rib], arch, new Vector3(x, WALLH, 0), Quaternion.identity);
            else if (s.flatCeiling)
            {
                var beam = Box("Beam", new Vector3(x, 6.25f, 0), new Vector3(0.3f, 0.3f, 2 * HW2), M[s.trim], arch); StaticAll(beam);
            }
        }
    }

    // ------------------------------------------------------------------ furniture
    static GameObject Chandelier(Transform p, EraStyle s, Vector3 hangPoint, float crownY)
    {
        var g = Group(p, "Chandelier");
        g.position = hangPoint;
        string kind = s.chandelier;
        var metal = kind == "brass" || kind == "lantern" ? "Brass" : "Iron";
        var mbMetal = new MeshKit.MB(); var mbFlame = new MeshKit.MB(); var mbCandle = new MeshKit.MB();
        float chain = Mathf.Max(0.3f, crownY - hangPoint.y);
        MeshKit.Tube(mbMetal, new[] { Vector3.zero, Vector3.up * chain }, 0.018f, 6);
        if (kind == "lantern")
        {   // Mughal hanging lantern: pierced brass globe with a glowing core
            var prof = new List<Vector2> { new Vector2(0.001f, -0.55f), new Vector2(0.08f, -0.5f), new Vector2(0.3f, -0.3f), new Vector2(0.36f, -0.05f),
                                           new Vector2(0.3f, 0.2f), new Vector2(0.12f, 0.36f), new Vector2(0.06f, 0.45f), new Vector2(0.001f, 0.5f) };
            MeshKit.Lathe(mbMetal, prof, 24);
            var core = new List<Vector2> { new Vector2(0.001f, -0.3f), new Vector2(0.22f, -0.15f), new Vector2(0.24f, 0.05f), new Vector2(0.15f, 0.22f), new Vector2(0.001f, 0.3f) };
            MeshKit.Lathe(mbFlame, core, 16);
        }
        else if (kind == "pendant")
        {   // modern disc pendant
            var shade = new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.45f, 0), new Vector2(0.5f, 0.04f), new Vector2(0.2f, 0.2f), new Vector2(0.03f, 0.24f) };
            MeshKit.Lathe(mbMetal, shade, 32);
            var bulb = new List<Vector2> { new Vector2(0.001f, -0.01f), new Vector2(0.4f, -0.01f), new Vector2(0.4f, 0.005f), new Vector2(0.001f, 0.005f) };
            MeshKit.Lathe(mbFlame, bulb, 24);
        }
        else
        {   // ring chandelier with candles (iron) or diya cups (brass)
            float R = kind == "brass" ? 0.6f : 0.7f; int n = kind == "brass" ? 12 : 10;
            MeshKit.Torus(mbMetal, R, 0.025f, 48, 8);
            MeshKit.Torus(mbMetal, R * 0.55f, 0.02f, 32, 8, 0.32f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2;
                MeshKit.Tube(mbMetal, new[] { new Vector3(0, 0.75f, 0), new Vector3(Mathf.Cos(a) * R * 0.5f, 0.3f, Mathf.Sin(a) * R * 0.5f), new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R) }, 0.015f, 6);
            }
            var hub = new List<Vector2> { new Vector2(0.001f, -0.25f), new Vector2(0.07f, -0.18f), new Vector2(0.05f, 0f), new Vector2(0.09f, 0.1f), new Vector2(0.04f, 0.3f), new Vector2(0.04f, 0.8f), new Vector2(0.001f, 0.8f) };
            MeshKit.Lathe(mbMetal, hub, 16);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n; var c = new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R);
                var cup = new MeshKit.MB();
                if (kind == "brass") MeshKit.Lathe(cup, new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.06f, 0.02f), new Vector2(0.07f, 0.05f) }, 12);
                else MeshKit.Lathe(cup, new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.045f, 0.0f), new Vector2(0.04f, 0.03f), new Vector2(0.001f, 0.03f) }, 12);
                mbMetal.Append(cup, Matrix4x4.Translate(c));
                if (kind != "brass")
                {
                    var candle = new MeshKit.MB(); MeshKit.Lathe(candle, new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.022f, 0), new Vector2(0.022f, 0.16f), new Vector2(0.001f, 0.16f) }, 10);
                    mbCandle.Append(candle, Matrix4x4.Translate(c + Vector3.up * 0.03f));
                }
                var flame = new MeshKit.MB(); MeshKit.Lathe(flame, new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.014f, 0.025f), new Vector2(0.008f, 0.06f), new Vector2(0.001f, 0.08f) }, 8);
                mbFlame.Append(flame, Matrix4x4.Translate(c + Vector3.up * (kind == "brass" ? 0.04f : 0.19f)));
            }
            if (kind == "iron")
            {   // square gallery track with spot cans, as in modern museum halls
                float t = 1.0f, yT = 0.1f;
                var sq = new[] { new Vector3(-t, yT, -t), new Vector3(-t, yT, t), new Vector3(t, yT, t), new Vector3(t, yT, -t), new Vector3(-t, yT, -t) };
                MeshKit.Tube(mbMetal, sq, 0.022f, 6);
                foreach (var c in new[] { new Vector3(-t, yT, -t), new Vector3(-t, yT, t), new Vector3(t, yT, t), new Vector3(t, yT, -t) })
                {
                    MeshKit.Tube(mbMetal, new[] { c, c + Vector3.up * (chain - 0.2f) }, 0.008f, 4);
                    for (int k = 0; k < 2; k++)
                    {
                        var can = new MeshKit.MB(); MeshKit.Lathe(can, new List<Vector2> { new Vector2(0.001f, -0.14f), new Vector2(0.045f, -0.14f), new Vector2(0.05f, 0.0f), new Vector2(0.001f, 0.0f) }, 10);
                        var mid = Vector3.Lerp(c, new Vector3(-c.z, yT, c.x), 0.3f + 0.4f * k);
                        mbMetal.Append(can, Matrix4x4.TRS(mid, Quaternion.Euler(25, 0, 0), Vector3.one));
                    }
                }
            }
        }
        var key = $"Chandelier_{kind}_{chain:0.#}";
        MeshObj("Metal", MeshKit.Save(key + "_m", mbMetal, true, true), M[metal], g, hangPoint, Quaternion.identity, false);
        MeshObj("Light", MeshKit.Save(key + "_f", mbFlame, false), M[kind == "lantern" || kind == "pendant" ? "Glow_Warm" : "Bulb"], g, hangPoint, Quaternion.identity, false);
        if (mbCandle.t.Count > 0) MeshObj("Candles", MeshKit.Save(key + "_c", mbCandle, false), M["Candle"], g, hangPoint, Quaternion.identity, false);
        return g.gameObject;                     // not lightmapped: lit by light probes
    }

    static void Bench(Transform p, Vector3 pos, float yaw)
    {
        var g = Group(p, "Bench"); g.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var seat = Box("Seat", Vector3.zero, Vector3.one, M["Era_Wood"], g);
        seat.transform.localPosition = new Vector3(0, 0.45f, 0); seat.transform.localScale = new Vector3(1.8f, 0.07f, 0.45f);
        foreach (var x in new[] { -0.75f, 0.75f })
            foreach (var z in new[] { -0.16f, 0.16f })
                MeshObj("Leg", MeshKit.LatheShape("BenchLeg", new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.035f, 0), new Vector2(0.03f, 0.2f), new Vector2(0.045f, 0.3f), new Vector2(0.03f, 0.42f), new Vector2(0.001f, 0.42f) }, 12),
                        M["Era_Wood"], g, g.TransformPoint(new Vector3(x, 0, z)), g.rotation);
        StaticAll(g.gameObject);
    }

    static void Stanchions(Transform p, Vector3 centre, float radius, int n)
    {
        var g = Group(p, "Stanchions");
        var post = MeshKit.LatheShape("StanchionPost", new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.14f, 0), new Vector2(0.14f, 0.03f), new Vector2(0.03f, 0.06f),
                                      new Vector2(0.025f, 0.85f), new Vector2(0.045f, 0.9f), new Vector2(0.04f, 0.96f), new Vector2(0.001f, 0.98f) }, 20);
        var tops = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2 / n; var pos = centre + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
            MeshObj("Post", post, M["Brass"], g, pos, Quaternion.identity, false);
            tops.Add(pos + Vector3.up * 0.88f);
        }
        var rope = new MeshKit.MB();
        for (int i = 0; i < n; i++)
        {
            var a = tops[i]; var b = tops[(i + 1) % n]; var pts = new List<Vector3>();
            for (int k = 0; k <= 12; k++) { float t = k / 12f; pts.Add(Vector3.Lerp(a, b, t) + Vector3.down * 0.18f * 4f * t * (1 - t) - centre); }
            MeshKit.Tube(rope, pts, 0.02f, 8);
        }
        MeshObj("Rope", MeshKit.Save($"Rope_{radius:0.#}_{n}", rope), M["Velvet"], g, centre, Quaternion.identity, false);
        LightmappedOnly(g.gameObject);
    }

    // ------------------------------------------------------------------ exhibits on stands
    static string ModelPath(string id)
    {
        foreach (var ext in new[] { ".prefab", ".fbx", ".obj", ".glb", ".gltf" })
        {
            var p = $"Assets/_Museum/Models/{id}{ext}";
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static Sprite PhotoSprite(string id) => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_Museum/Photos/{id}.jpg");

    /// Material showing a photo cropped to 'aspect' (width / height), centred.
    static Material PhotoMat(string id, float aspect)
    {
        var sp = PhotoSprite(id); if (!sp) return null;
        var tex = sp.texture;
        var m = Lit("Photo_" + id, Color.white, 0f, 0.15f, "/Photos");
        m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
        float a = tex.width / (float)Mathf.Max(1, tex.height);
        Vector2 sc = Vector2.one, off = Vector2.zero;
        if (a > aspect) { sc.x = aspect / a; off.x = (1 - sc.x) / 2; } else { sc.y = a / aspect; off.y = (1 - sc.y) / 2; }
        m.SetTextureScale("_BaseMap", sc); m.SetTextureOffset("_BaseMap", off);
        return m;
    }

    /// Fits an imported model inside a box of 'size' standing on 'pos'.
    static GameObject PlaceModel(string path, Transform parent, Vector3 pos, Vector3 size, float yaw)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return go;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        float k = Mathf.Min(size.x / Mathf.Max(0.01f, b.size.x), size.y / Mathf.Max(0.01f, b.size.y), size.z / Mathf.Max(0.01f, b.size.z));
        go.transform.localScale *= k;
        b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
        return go;
    }

    /// Stand + object for an exhibit, in front of its board. Returns the object group (made selectable by the caller).
    static GameObject ExhibitStand(Transform p, EraStyle s, string id, string stand, Vector3 pos, Vector3 facing, Sprite fallback)
    {
        var g = new GameObject($"{id}_Stand_{stand}"); g.transform.SetParent(p, false); g.transform.position = pos;
        float yaw = Quaternion.LookRotation(facing).eulerAngles.y;
        float top; Vector3 box;
        switch (stand)
        {
            case "table":
            {
                top = 0.95f; box = new Vector3(0.7f, 0.25f, 0.5f);
                var t = Group(g.transform, "Table"); t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
                var slab = Box("Top", Vector3.zero, Vector3.one, M["Era_Wood"], t); slab.transform.localPosition = new Vector3(0, 0.92f, 0); slab.transform.localScale = new Vector3(1.0f, 0.06f, 0.7f);
                var leg = MeshKit.LatheShape("TableLeg", new List<Vector2> { new Vector2(0.001f, 0), new Vector2(0.04f, 0), new Vector2(0.03f, 0.1f), new Vector2(0.045f, 0.5f), new Vector2(0.03f, 0.8f), new Vector2(0.04f, 0.89f), new Vector2(0.001f, 0.89f) }, 16);
                foreach (var x in new[] { -0.44f, 0.44f }) foreach (var z in new[] { -0.29f, 0.29f })
                    MeshObj("Leg", leg, M["Era_Wood"], t, t.TransformPoint(new Vector3(x, 0, z)), t.rotation, false);
                var glass = Box("Vitrine", Vector3.zero, Vector3.one, M["Glass"], t, 0, false); glass.transform.localPosition = new Vector3(0, 1.1f, 0); glass.transform.localScale = new Vector3(0.94f, 0.3f, 0.64f);
                UnityEngine.Object.DestroyImmediate(glass.GetComponent<Collider>());
                break;
            }
            case "sculpture":
                top = 0.75f; box = new Vector3(0.7f, 0.95f, 0.7f);
                MeshObj("Plinth", MeshKit.Plinth("Plinth_Sculpt", 0.72f, 0.72f, top), M["Era_Marble"], g.transform, pos, Quaternion.Euler(0, yaw, 0), false);
                break;
            case "monument":
                top = 0.55f; box = new Vector3(1.1f, 0.8f, 0.8f);
                MeshObj("Plinth", MeshKit.Plinth("Plinth_Monument", 1.2f, 0.9f, top), M["Era_Granite"], g.transform, pos, Quaternion.Euler(0, yaw, 0), false);
                break;
            default:
                top = 1.0f; box = new Vector3(0.45f, 0.5f, 0.45f);
                MeshObj("Plinth", MeshKit.Plinth("Plinth_Pedestal", 0.52f, 0.52f, top), M[s.trim], g.transform, pos, Quaternion.Euler(0, yaw, 0), false);
                break;
        }
        var objPos = pos + Vector3.up * top;
        var model = ModelPath(id);
        if (model != null && PlaceModel(model, g.transform, objPos, box, yaw + 180f) != null) { }
        else
        {   // until a 3D model is added: the photo (or picture) mounted on a small easel / lying in the vitrine
            var mat = PhotoMat(id, 1.3f);
            if (!mat && fallback) { mat = Lit("Picture_" + fallback.name, Color.white, 0, 0.15f, "/Photos"); mat.SetTexture("_BaseMap", fallback.texture); mat.mainTexture = fallback.texture; }
            if (mat)
            {
                bool flat = stand == "table";
                float w = flat ? 0.6f : Mathf.Min(0.62f, box.x), h = w / 1.3f;
                var card = Panel("Photo", objPos + (flat ? Vector3.up * 0.03f : Vector3.up * (h / 2 + 0.06f)), facing, w, h, mat, g.transform, isStatic: false);
                if (flat) card.transform.rotation = Quaternion.LookRotation(Vector3.down, facing) * Quaternion.Euler(0, 0, 0);
                else card.transform.rotation = Quaternion.LookRotation(-facing) * Quaternion.Euler(12, 0, 0);
                var fr = MeshObj("PhotoFrame", MeshKit.Frame($"Frame_{w:0.##}x{h:0.##}", w, h, 0.03f, 0.02f), M["Brass"], g.transform, card.transform.position, card.transform.rotation, false);
                if (!flat)
                {
                    var easel = Box("Easel", objPos + Vector3.up * 0.02f - facing * 0.05f, new Vector3(w * 0.6f, 0.04f, 0.12f), M["Era_Wood"], g.transform, yaw, false);
                    UnityEngine.Object.DestroyImmediate(easel.GetComponent<Collider>());
                }
            }
        }
        LightmappedOnly(g);
        return g;
    }

    // ------------------------------------------------------------------ centrepiece showcase
    static GameObject Showcase(Transform p, EraStyle s, SectionJ sec, Vector3 pos)
    {
        var g = Group(p, $"{sec.id}_Showcase");
        // stepped base: granite step, marble plinth with brass trim
        MeshObj("Step", MeshKit.Plinth("Showcase_Step", 1.9f, 1.9f, 0.18f), M["Era_Granite"], g, pos, Quaternion.identity);
        MeshObj("Plinth", MeshKit.Plinth("Showcase_Plinth", 1.25f, 1.25f, 0.95f), M["Era_Marble"], g, pos + Vector3.up * 0.18f, Quaternion.identity);
        var trim = new MeshKit.MB();
        MeshKit.Sweep(trim, new List<Vector2> { new Vector2(0, 0), new Vector2(0.03f, 0.01f), new Vector2(0.03f, 0.04f), new Vector2(0, 0.05f) },
                      new List<Vector3> { new Vector3(-0.68f, 0, -0.68f), new Vector3(-0.68f, 0, 0.68f), new Vector3(0.68f, 0, 0.68f), new Vector3(0.68f, 0, -0.68f) },
                      new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up }, true);
        MeshObj("BrassTrim", MeshKit.Save("Showcase_Trim", trim, true, true), M["Brass"], g, pos + Vector3.up * 1.05f, Quaternion.identity);
        // glass case with brass edges
        float top = 1.13f;
        var glass = Box("GlassCase", pos + Vector3.up * (top + 0.6f), new Vector3(1.1f, 1.2f, 1.1f), M["Glass"], g, 0, false);
        UnityEngine.Object.DestroyImmediate(glass.GetComponent<Collider>());
        var edges = new MeshKit.MB();
        foreach (var x in new[] { -0.55f, 0.55f }) foreach (var z in new[] { -0.55f, 0.55f })
            MeshKit.Tube(edges, new[] { new Vector3(x, 0, z), new Vector3(x, 1.2f, z) }, 0.012f, 6);
        foreach (var y in new[] { 0f, 1.2f })
            MeshKit.Tube(edges, new[] { new Vector3(-0.55f, y, -0.55f), new Vector3(-0.55f, y, 0.55f), new Vector3(0.55f, y, 0.55f), new Vector3(0.55f, y, -0.55f), new Vector3(-0.55f, y, -0.55f) }, 0.012f, 6);
        MeshObj("CaseEdges", MeshKit.Save("Showcase_Edges", edges), M["Brass"], g, pos + Vector3.up * top, Quaternion.identity);
        Stanchions(g, pos, 1.75f, 6);
        // label plaque
        var plaque = Box("Plaque", pos + new Vector3(-0.64f, 0.75f, 0), new Vector3(0.02f, 0.22f, 0.8f), M["Brass"], g); StaticAll(plaque);
        Text3D("PlaqueText", $"<b>SPECIAL ARTIFACT</b>\n<size=70%>Section {sec.num}</size>", pos + new Vector3(-0.655f, 0.75f, 0), Vector3.left, new Vector2(0.76f, 0.2f), new Color(0.2f, 0.12f, 0.05f), g, false, 1.2f);
        // the object itself: an imported model if present, else the procedural centrepiece (scaled up)
        var obj = new GameObject($"{sec.id}_Centrepiece"); obj.transform.SetParent(g, false); obj.transform.position = pos + Vector3.up * top;
        var model = ModelPath(sec.id + "_00");
        if (model == null || PlaceModel(model, obj.transform, obj.transform.position, new Vector3(0.9f, 1.05f, 0.9f), 0) == null)
        {
            Centrepiece(sec.centrepiece, obj.transform);
            if (obj.transform.childCount == 0)
            {
                var mat = PhotoMat(sec.id + "_00", 1.3f) ?? M[sec.id + "_illus"];
                var card = Panel("Photo", obj.transform.position + Vector3.up * 0.45f, Vector3.left, 0.8f, 0.6f, mat, obj.transform, isStatic: false);
            }
            obj.transform.localScale = Vector3.one * 1.35f;
        }
        LightmappedOnly(g.gameObject);
        foreach (var t in obj.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);   // spins: light probes
        return obj;
    }

    // ------------------------------------------------------------------ kiosk
    static KioskScreen Kiosk(Transform p, EraStyle s, SectionJ sec, WingJ w, int wi, Vector3 pos, List<Artifact> exhibits, List<ExhibitJ> exs)
    {
        var g = Group(p, $"{sec.id}_Kiosk");
        g.SetPositionAndRotation(pos, Quaternion.Euler(0, 90, 0));          // screen faces -X (towards arriving visitors)
        var stand = MeshKit.Plinth("Kiosk_Stand", 0.5f, 0.32f, 0.95f);
        MeshObj("Stand", stand, M[s.trim], g, pos, g.rotation);
        var bezel = Box("Bezel", Vector3.zero, Vector3.one, M["Screen_Bezel"], g, 0, false);
        bezel.transform.localPosition = new Vector3(0, 1.45f, 0.05f); bezel.transform.localRotation = Quaternion.Euler(12, 0, 0); bezel.transform.localScale = new Vector3(1.32f, 0.9f, 0.05f);
        UnityEngine.Object.DestroyImmediate(bezel.GetComponent<Collider>());
        LightmappedOnly(g.gameObject);

        var screenGo = new GameObject("Screen", typeof(RectTransform));
        screenGo.transform.SetParent(bezel.transform.parent, false);
        screenGo.transform.localPosition = new Vector3(0, 1.45f, 0.022f);
        screenGo.transform.localRotation = Quaternion.Euler(12, 0, 0);
        var canvas = screenGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)screenGo.transform; rt.sizeDelta = new Vector2(1240, 840); rt.localScale = Vector3.one * 0.001f;
        var k = screenGo.AddComponent<KioskScreen>();
        k.font = font; k.accent = Hex(w.color); k.mapSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/UI/india_map.png");
        k.sectionTitle = $"Section {sec.num}: {sec.title}"; k.period = sec.period;
        k.questions = sec.quiz.Select(q => new KioskScreen.Question { text = q.q, options = q.options.ToArray(), answer = q.answer, why = q.why }).ToList();
        k.entries = new List<KioskScreen.Entry>();
        for (int i = 0; i < exs.Count; i++)
        {
            var e = exs[i];
            k.entries.Add(new KioskScreen.Entry { year = e.year, label = e.yearLabel, title = e.title, target = exhibits[i], photo = PhotoSprite(e.id),
                credit = PhotoCredit(e.id), places = e.places.Select(pl => new KioskScreen.Place { name = pl.name, u = pl.u, v = pl.v }).ToArray() });
        }
        k.wingId = w.id;
        foreach (var c in screenGo.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = interactiveLayer;
        return k;
    }

    static Dictionary<string, string> credits;
    static string PhotoCredit(string id)
    {
        if (credits == null)
        {
            credits = new Dictionary<string, string>();
            var path = "Docs/photo_credits.csv";
            if (File.Exists(path))
                foreach (var line in File.ReadAllLines(path).Skip(1))
                {
                    var c = SplitCsv(line);
                    if (c.Count >= 4) credits[c[0]] = $"{(c[2].Length > 0 ? c[2] : "Unknown")} · {c[3]} · Wikimedia Commons";
                }
        }
        return credits.TryGetValue(id, out var v) ? v : "";
    }

    static List<string> SplitCsv(string line)
    {
        var res = new List<string>(); var cur = new System.Text.StringBuilder(); bool q = false;
        foreach (var ch in line)
        {
            if (ch == '"') q = !q;
            else if (ch == ',' && !q) { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        res.Add(cur.ToString());
        return res;
    }

    // ------------------------------------------------------------------ lighting helpers
    static Light BakedLight(string name, LightType type, Vector3 pos, Transform p, float range, float intensity, Color c)
    {
        var l = NewLight(name, type, pos, p, range, intensity);
        l.color = c; l.lightmapBakeType = LightmapBakeType.Baked; l.shadows = LightShadows.Soft;
        return l;
    }

    static void ProbesAndReflection(Transform p, float x0, float x1, float crown)
    {
        var probes = new List<Vector3>();
        for (float x = x0 + 1f; x < x1; x += BAY / 2)
            foreach (var z in new[] { -4.5f, -1.5f, 1.5f, 4.5f })
                foreach (var y in new[] { 0.6f, 1.8f, 3.4f }) probes.Add(new Vector3(x, y, z));
        var g = new GameObject("LightProbes"); g.transform.SetParent(p, false);
        g.AddComponent<LightProbeGroup>().probePositions = probes.ToArray();
        var rp = new GameObject("ReflectionProbe"); rp.transform.SetParent(p, false);
        rp.transform.position = new Vector3((x0 + x1) / 2, 1.8f, 0);
        var r = rp.AddComponent<ReflectionProbe>();
        r.mode = ReflectionProbeMode.Baked; r.boxProjection = true; r.resolution = 128;
        r.size = new Vector3(x1 - x0, crown, 2 * HW2); r.center = new Vector3(0, crown / 2 - 1.8f, 0);
        GameObjectUtility.SetStaticEditorFlags(rp, StaticEditorFlags.ReflectionProbeStatic);
    }

    // ------------------------------------------------------------------ WING (v3)
    static string BuildWing3(MuseumJ museum, int wi, ref int items)
    {
        var w = museum.wings[wi];
        var s = Styles[w.id];
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupLighting();
        RenderSettings.ambientSkyColor = new Color(0.32f, 0.29f, 0.26f);
        RenderSettings.ambientEquatorColor = new Color(0.24f, 0.21f, 0.18f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.1f, 0.09f);
        var root = new GameObject($"Wing{wi + 1}_{w.title.Replace(' ', '_')}").transform;
        var arch = Group(root, "Architecture"); var decor = Group(root, "Decor"); var lights = Group(root, "Lighting");
        var pts = Group(root, "TeleportPoints"); var exhibitsRoot = Group(root, "Exhibits"); var timeline = Group(root, "FloorTimeline");
        var tour = new List<Artifact>();
        var gold = new Color(0.98f, 0.82f, 0.45f);
        float crown = s.flatCeiling ? 6.4f : WALLH + s.vaultRise;
        var warm = new Color(1f, 0.82f, 0.62f);

        // ---- entry hall (x -10.8..0)
        float ex0 = -2 * BAY;
        HallShell(arch, s, ex0, 0f);
        var ew = Box("EndWall", new Vector3(ex0 - 0.3f, crown / 2, 0), new Vector3(0.6f, crown + 0.2f, 2 * HW2 + 0.4f), M[s.wall], arch); StaticAll(ew);
        Portal(decor, "Lobby", new Vector3(ex0 + 0.05f, 0, 0), Vector3.right, "Lobby", w.id, "Back to the\n<size=70%>Lobby</size>", "Portal_Lobby", new Color(1f, 0.6f, 0.2f));
        foreach (var z in new[] { -3.6f, 3.6f }) JaliWindow(decor, s, new Vector3(ex0 + 0.02f, 0, z), Vector3.right, 1.6f, 1.2f, 4.2f, true);
        var spawn = MakePoint("Entry", new Vector3(-BAY, 0, -1.0f), 0f, pts);
        Marker("Arrive_Entry", new Vector3(-BAY, 0, -1.0f), 0f, pts);
        var wingData = Data(w.id, w.id + "_00", $"Wing {wi + 1}: {w.title}", w.subtitle, w.intro, null);
        var titleArt = Framed(exhibitsRoot, w.id + "_TitleArt", M[w.id + "_title"], new Vector3(-BAY, 2.4f, HW2 - 0.03f), Vector3.back, 4.6f, 2.96f);
        MeshObj("TitleFrame", MeshKit.Frame("Frame_Title", 4.6f, 2.96f, 0.16f, 0.1f), M["Era_Wood"], exhibitsRoot, titleArt.transform.position + Vector3.back * 0.005f, FaceTo(Vector3.back));
        tour.Add(MakeArtifact(titleArt, wingData, spawn, new Vector3(-BAY, 1.55f, 0.1f), Vector3.back));
        items++;
        // wing timeline on the opposite wall
        var tl = string.Join("\n", w.sections.Select(sec => $"<color=#F5C66A><b>{sec.num}.</b></color> {sec.title}  <color=#D8C8B0><size=80%>{sec.period}</size></color>"));
        var board = Box("TimelineBoard", new Vector3(-BAY, 2.4f, -HW2 + 0.04f), new Vector3(5.2f, 3.0f, 0.06f), M["Teak_Dark"], decor); StaticAll(board);
        Text3D("WingTimeline", $"<b>{w.title}</b>  <size=70%>{w.subtitle}</size>\n<size=55%>{tl}</size>", new Vector3(-BAY, 2.4f, -HW2 + 0.08f), Vector3.forward, new Vector2(4.8f, 2.7f), new Color(1f, 0.94f, 0.82f), decor, false, 3f);
        for (float hx = ex0 + BAY / 2; hx < 0; hx += BAY)
        {
            Chandelier(decor, s, new Vector3(hx, crown - 2.2f, 0), crown - 0.1f);
            BakedLight("Chandelier", LightType.Point, new Vector3(hx, crown - 2.4f, 0), lights, 10f, 2.4f, warm);
        }
        Bench(decor, new Vector3(-BAY * 1.5f, 0, -2.2f), 0);
        ProbesAndReflection(lights, ex0, 0, crown);
        // floor timeline strip through the whole wing
        float x = 0f;

        // ---- section halls
        for (int si = 0; si < w.sections.Count; si++)
        {
            var sec = w.sections[si];
            int n = sec.exhibits.Count, perWall = Mathf.CeilToInt(n / 2f), exBays = Mathf.CeilToInt(perWall / 2f);
            int nb = 1 + exBays + 1;
            float x0 = x, x1 = x + nb * BAY;
            var room = Group(exhibitsRoot, $"{sec.id}_{sec.title}");
            var illus = ArtSprite($"Illus/{sec.id}.png");
            string era = $"Section {sec.num} · {sec.title} · {sec.period}";

            Partition2(arch, s, x0, $"Section {sec.num}: {sec.title}", sec.period, si != 0);
            HallShell(arch, s, x0, x1);
            MakePoint($"Door_{sec.id}", new Vector3(x0 + 1.4f, 0, 0), 90f, pts);

            // blind arcades on both walls, every bay
            for (int b = 0; b < nb; b++)
            {
                float xc = x0 + (b + 0.5f) * BAY;
                BlindArch(arch, s, xc, HW2, Vector3.back);
                BlindArch(arch, s, xc, -HW2, Vector3.forward);
                Chandelier(decor, s, new Vector3(xc, crown - 2.2f, 0), crown - 0.1f);
                BakedLight("Chandelier", LightType.Point, new Vector3(xc, crown - 2.4f, 0), lights, 10f, 2.2f, warm);
                foreach (var side in new[] { 1, -1 })
                {
                    var sp = BakedLight("BoardSpot", LightType.Spot, new Vector3(xc, WALLH - 0.3f, side * 2.2f), lights, 9f, 5f, new Color(1f, 0.9f, 0.76f));
                    sp.transform.LookAt(new Vector3(xc, 1.6f, side * HW2)); sp.spotAngle = 75f;
                }
            }

            // entry bay: section introduction (left wall), picture + bench (right)
            float bx = x0 + BAY / 2;
            var introTp = MakePoint(sec.id + "_00", new Vector3(bx, 0, TPZ), 0f, pts);
            var centreData = Data(w.id, sec.id + "_00", $"Section {sec.num}: {sec.title}", sec.period, sec.intro, illus);
            var introBoard = Framed(room, sec.id + "_00_IntroBoard", M[sec.id + "_intro"], new Vector3(bx, 1.75f, HW2 - 0.04f), Vector3.back, 1.6f, 2.13f);
            MeshObj("IntroFrame", MeshKit.Frame("Frame_Intro", 1.6f, 2.13f, 0.1f, 0.07f), M["Era_Wood"], room, introBoard.transform.position + Vector3.back * 0.005f, FaceTo(Vector3.back));
            tour.Add(MakeArtifact(introBoard, centreData, introTp, new Vector3(bx, 1.55f, TPZ + 1.0f), Vector3.back));
            items++;
            var picMat = M[sec.id + "_illus"];
            var pic = Panel("SectionPicture", new Vector3(bx, 1.85f, -HW2 + 0.05f), Vector3.forward, 2.4f, 1.12f, picMat, room);
            MeshObj("PictureFrame", MeshKit.Frame("Frame_Pic", 2.4f, 1.12f, 0.1f, 0.07f), M["Era_Wood"], room, pic.transform.position + Vector3.forward * 0.005f, FaceTo(Vector3.forward));
            Bench(decor, new Vector3(bx, 0, -2.6f), 0);
            // section medallion on the floor timeline
            var med = Box("Medallion", new Vector3(bx, 0.006f, 0), new Vector3(1.5f, 0.012f, 1.5f), M["Brass"], timeline); StaticAll(med);
            FloorText("MedallionText", $"<b>{sec.num}</b>\n<size=45%>{sec.period}</size>", new Vector3(bx, 0.016f, 0), 1.35f, 1.2f, new Color(0.22f, 0.12f, 0.04f), timeline, 4f);

            // exhibits: alternate walls, two per bay per wall
            var sectionArtifacts = new List<Artifact>();
            for (int k = 0; k < n; k++)
            {
                var e = sec.exhibits[k];
                int side = k % 2 == 0 ? 1 : -1, slot = k / 2;
                int bay = 1 + slot / 2; float ex = x0 + bay * BAY + (slot % 2 == 0 ? BAY * 0.27f : BAY * 0.73f);
                var normal = new Vector3(0, 0, -side);
                var tp = MakePoint(e.id, new Vector3(ex, 0, side * TPZ), side > 0 ? 0f : 180f, pts);
                var photo = PhotoSprite(e.id);
                var data = Data(w.id, e.id, e.title, $"{e.yearLabel} · {era}", e.text, photo ? photo : illus);
                var boardGo = Framed(room, e.id, M[e.id], new Vector3(ex, BY, side * (HW2 - 0.04f)), normal, BW, BH);
                MeshObj("BoardFrame", MeshKit.Frame("Frame_Board", BW, BH, 0.08f, 0.06f), M["Era_Wood"], room, boardGo.transform.position + normal * 0.004f, FaceTo(normal));
                var pm = PhotoMat(e.id, 2.316f);
                if (pm)
                {   // real photo over the board's picture area
                    var ph = Panel("BoardPhoto", boardGo.transform.position + normal * 0.004f + Vector3.up * (0.455f * BH / 1.86f), normal, BW * 0.906f, BH * 0.284f, pm, boardGo.transform, isStatic: false);
                    ph.transform.SetParent(boardGo.transform, true);
                }
                var art = MakeArtifact(boardGo, data, tp, new Vector3(ex, 1.55f, side * (TPZ + 1.0f)), normal);
                tour.Add(art); sectionArtifacts.Add(art);
                items++;
                if (e.stand != "wall")
                {
                    var standGo = ExhibitStand(room, s, e.id, e.stand, new Vector3(ex, 0, side * STANDZ), normal, illus);
                    var objs = standGo.transform;
                    MakeArtifact(standGo, data, tp, new Vector3(ex, 1.55f, side * (TPZ + 1.0f)), normal);
                }
                // year plaque on the floor timeline
                var plate = Box("YearPlate", new Vector3(ex, 0.005f, side * 0.75f), new Vector3(0.9f, 0.01f, 0.36f), M["Brass"], timeline); StaticAll(plate);
                FloorText("Year", e.yearLabel, new Vector3(ex, 0.012f, side * 0.75f), 0.84f, 0.3f, new Color(0.22f, 0.12f, 0.04f), timeline, 2f);
            }

            // kiosk in the middle of the exhibit bays
            float kx = x0 + (1 + (exBays - 1) / 2f + 0.5f) * BAY;
            if (exBays % 2 == 0) kx = x0 + (1 + exBays / 2f) * BAY;          // on a bay boundary when the count is even
            Kiosk(room, s, sec, w, wi, new Vector3(kx, 0, 0), sectionArtifacts, sec.exhibits);
            MakePoint($"{sec.id}_Kiosk", new Vector3(kx - 2.2f, 0, 0), 90f, pts);

            // centrepiece showcase in the last bay
            float cx = x1 - BAY / 2 - 0.2f;
            var cpTp = MakePoint($"{sec.id}_Centrepiece", new Vector3(cx - 2.8f, 0, 0), 90f, pts);
            var cpObj = Showcase(room, s, sec, new Vector3(cx, 0, 0));
            var cpData = Data(w.id, sec.id + "_00C", sec.centre != null && !string.IsNullOrEmpty(sec.centre.show) ? sec.centre.show : $"Section {sec.num}: {sec.title}",
                              $"Special artifact · {era}", sec.intro, PhotoSprite(sec.id + "_00") ?? illus);
            var cpArt = MakeArtifact(cpObj, cpData, cpTp, new Vector3(cx - 1.6f, 1.6f, 0), Vector3.left, 20f);
            tour.Add(cpArt);
            var spot = BakedLight("ShowcaseSpot", LightType.Spot, new Vector3(cx - 1.2f, WALLH - 0.2f, 0), lights, 9f, 7f, new Color(1f, 0.93f, 0.82f));
            spot.transform.LookAt(new Vector3(cx, 1.4f, 0)); spot.spotAngle = 45f;
            foreach (var z in new[] { -3.2f, 3.2f }) Bench(decor, new Vector3(cx - 2.6f, 0, z), 90f);

            // floor timeline strip
            var strip = Box("TimelineStrip", new Vector3((x0 + x1) / 2, 0.003f, 0), new Vector3(x1 - x0, 0.006f, 0.08f), M["Brass"], timeline); StaticAll(strip);
            ProbesAndReflection(lights, x0, x1, crown);
            x = x1;
        }

        // ---- exit hall
        float xe = x, xEnd = x + 2 * BAY;
        Partition2(arch, s, xe, "Onward", "", true);
        HallShell(arch, s, xe, xEnd);
        var ew2 = Box("EndWall", new Vector3(xEnd + 0.3f, crown / 2, 0), new Vector3(0.6f, crown + 0.2f, 2 * HW2 + 0.4f), M[s.wall], arch); StaticAll(ew2);
        bool last = wi == museum.wings.Count - 1;
        var nextW = museum.wings[last ? 0 : wi + 1];
        int nextIndex = last ? 1 : wi + 2;
        Portal(decor, "Next", new Vector3(xEnd - 0.05f, 0, -2.2f), Vector3.left, "Wing" + nextIndex, "Entry",
            (last ? "Start again\n" : "Next: ") + $"<size=70%>Wing {nextIndex}\n{nextW.title}</size>", "Portal_" + nextW.id, Hex(nextW.color) * 1.6f);
        Portal(decor, "Lobby", new Vector3(xEnd - 0.05f, 0, 2.2f), Vector3.left, "Lobby", w.id, "Back to the\n<size=70%>Lobby</size>", "Portal_Lobby", new Color(1f, 0.6f, 0.2f));
        MakePoint("Exit", new Vector3(xe + BAY, 0, 0), 90f, pts);
        for (float cxx = xe + BAY / 2; cxx < xEnd; cxx += BAY)
        {
            Chandelier(decor, s, new Vector3(cxx, crown - 2.2f, 0), crown - 0.1f);
            BakedLight("Chandelier", LightType.Point, new Vector3(cxx, crown - 2.4f, 0), lights, 10f, 2.4f, warm);
        }
        ProbesAndReflection(lights, xe, xEnd, crown);

        BuildPlayer(spawn.transform.position, 0f);
        var panel = BuildInfoPanel(root);
        panel.tour = tour.ToArray();
        MuseumLighting.Configure(scene);
        BuildPhoneHUD();
        return SaveWithOcclusion(scene, $"{SceneDir}/Wing{wi + 1}.unity");
    }
}
