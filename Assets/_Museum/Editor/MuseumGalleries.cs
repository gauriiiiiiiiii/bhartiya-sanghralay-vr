using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// The six v5 galleries. Each has its own architecture, inspired by real museum rooms:
///   arches   – a stone-arched gallery with a dark polished floor and windows at the far end (Ancient India)
///   skylit   – a tall top-lit hall with a glazed skylight lantern (Classical India, with the Iron Pillar)
///   octagon  – an octagonal domed hall with an oculus and eight arched niches (Medieval India)
///   pictures – a picture gallery under a luminous laylight, parquet floor, gilt frames (Colonial India)
///   rotunda  – a white spiral rotunda under a glass dome (Freedom and the Republic)
///   modern   – a white skylit hall with a glazed wall onto a courtyard (Themes of Indian History)
public static partial class MuseumBuilder
{
    static readonly string[] SceneOfWing = { "Wing1", "Wing2", "Wing3", "Wing4", "Wing5", "Wing6" };

    static string BuildGalleryV5(CurJ cur, int wi)
    {
        var w = cur.wings[wi];
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var x = new Ctx { w = w, wi = wi, accent = Hex(w.color), key = "G" + (wi + 1) };
        x.root = new GameObject($"Gallery{wi + 1}_{w.title.Replace(' ', '_')}").transform;
        x.arch = Group(x.root, "Architecture"); x.decor = Group(x.root, "Furniture"); x.lights = Group(x.root, "Lighting");
        x.pts = Group(x.root, "WalkPoints"); x.exhibits = Group(x.root, "Exhibits");
        fixtures.Clear();
        Vector3 spawn; float yaw;
        switch (w.gallery)
        {
            case "arches": (spawn, yaw) = GalleryArches(x); break;
            case "skylit": (spawn, yaw) = GallerySkylit(x); break;
            case "octagon": (spawn, yaw) = GalleryOctagon(x); break;
            case "pictures": (spawn, yaw) = GalleryPictures(x); break;
            case "rotunda": (spawn, yaw) = GalleryRotunda(x); break;
            default: (spawn, yaw) = GalleryModern(x); break;
        }
        Marker("Arrive_Entry", spawn, yaw, x.pts);
        BuildPlayer(spawn, yaw);
        var intro = Data("V5", w.id + "_00", w.title, w.years, $"{w.title}. {w.subtitle}. {w.intro}", null);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{CurAudio}/{w.id}_00.ogg"); if (clip) intro.narration = clip;
        intro.stop = (wi + 1) * 100; EditorUtility.SetDirty(intro);
        MakeAudioGuide(intro, x.accent);
        RealCamera();
        MuseumLighting.Configure(scene);
        BuildPhoneHUD();
        return SaveWithOcclusion(scene, $"{SceneDir}/Wing{wi + 1}.unity");
    }

    // spot fixtures collected while placing exhibits, turned into ceiling tracks afterwards
    static readonly List<(Vector3 from, Vector3 to)> fixtures = new List<(Vector3, Vector3)>();

    static void PlaceItems(Ctx x, IList<WSlot> slots, bool gilt = false)
    {
        for (int i = 0; i < x.w.items.Count && i < slots.Count; i++)
        {
            var it = x.w.items[i]; var s = slots[i];
            bool site = it.where != null && it.where.StartsWith("In situ");
            bool painting = gilt && (it.medium.Contains("oil") || it.medium.StartsWith("Portrait") || it.medium.Contains("Oil"));
            s.frame = painting ? "gilt" : site ? "panel" : "mount";
            if (site) { s.maxW *= 1.3f; s.maxH *= 1.15f; }
            else if (!painting) { s.maxW *= 0.8f; s.maxH *= 0.85f; }
            WallExhibit(x, it, (x.wi + 1) * 100 + i + 1, s);
            fixtures.Add((s.spot, s.c));
        }
    }

    static void Tracks(Ctx x, float ceilingY)
    {
        foreach (var grp in fixtures.GroupBy(f => (Mathf.Round(f.from.z * 2f) / 2f, Mathf.Round(f.from.x * 2f) / 2f)).GroupBy(g => g.Key.Item1))
        {
            var list = grp.SelectMany(g => g).ToList();
            float x0 = list.Min(f => f.from.x) - 1f, x1 = list.Max(f => f.from.x) + 1f;
            var a = new Vector3(x0, ceilingY - 0.02f, grp.Key); var b = new Vector3(x1, ceilingY - 0.02f, grp.Key);
            TrackWithFixtures(x.decor, a, b, list.Select(f => (new Vector3(f.from.x, ceilingY - 0.05f, f.from.z), f.to)));
        }
    }

    static void Walk(Ctx x, IEnumerable<Vector3> pts, float yaw)
    {
        int i = 0;
        foreach (var p in pts) MakePoint("Walk_" + (i++), p, yaw, x.pts);
    }

    static void CentreFixtures(Vector3 pos, float yaw, float ceiling)
    {
        var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward; var right = Vector3.Cross(Vector3.up, fwd);
        foreach (var s in new[] { -1f, 1f })
        {
            var from = pos + fwd * 3.2f + right * s * 2.6f; from.y = ceiling - 0.3f;
            fixtures.Add((from, pos + Vector3.up));
        }
    }

    static float YawToward(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

    // ======================================================================= W1: stone arches
    static (Vector3, float) GalleryArches(Ctx x)
    {
        DaylightScene(new Vector3(32, -90, 0), 2.6f);
        const float L = 42f, Hw = 7f, H = 7.5f;
        var a = x.arch; var wall = M["V5_PlasterSage"];
        Box("Floor", new Vector3(L / 2, -0.15f, 0), new Vector3(L + 0.8f, 0.3f, 2 * Hw + 0.8f), M["V5_DarkFloor"], a);
        Box("Ceiling", new Vector3(L / 2, H + 0.15f, 0), new Vector3(L + 0.8f, 0.3f, 2 * Hw + 0.8f), M["V5_Plaster"], a);
        WallX(a, "WallN", -0.4f, L + 0.4f, Hw + 0.2f, 0.4f, 0, H, wall);
        WallX(a, "WallS", -0.4f, L + 0.4f, -Hw - 0.2f, 0.4f, 0, H, wall);
        WallZ(a, "WallW", -Hw - 0.4f, Hw + 0.4f, -0.2f, 0.4f, 0, H, wall, new Vector3(0, 3.2f, 4.4f));
        // east wall: two tall windows and the door to the next gallery
        WallZ(a, "WallE_Low", -Hw - 0.4f, Hw + 0.4f, L + 0.2f, 0.4f, 0, 1.2f, wall, new Vector3(0, 3.2f, 1.2f));
        WallZ(a, "WallE_Mid", -Hw - 0.4f, Hw + 0.4f, L + 0.2f, 0.4f, 1.2f, 6.0f, wall, new Vector3(-3.6f, 2.2f, 6.0f), new Vector3(0, 3.2f, 4.4f), new Vector3(3.6f, 2.2f, 6.0f));
        WallZ(a, "WallE_Top", -Hw - 0.4f, Hw + 0.4f, L + 0.2f, 0.4f, 6.0f, H, wall);
        foreach (var z in new[] { -3.6f, 3.6f })
        {
            var g = Box("WindowGlass", new Vector3(L + 0.3f, 3.6f, z), new Vector3(0.02f, 4.8f, 2.2f), M["V5_Glass"], a, 0, false);
            Object.DestroyImmediate(g.GetComponent<Collider>()); g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            GMesh("Mullions", "W1_Mullion", mb => { Geo.Bar(mb, new Vector3(0, 1.2f, 0), new Vector3(0, 6.0f, 0), 0.05f, 0.06f); for (float y = 2.4f; y < 6f; y += 1.2f) Geo.Bar(mb, new Vector3(0, y, -1.1f), new Vector3(0, y, 1.1f), 0.05f, 0.05f); },
                  M["V5_Charcoal"], a, new Vector3(L + 0.28f, 0, z), Quaternion.identity, false);
            Box("Sill", new Vector3(L + 0.05f, 1.17f, z), new Vector3(0.36f, 0.06f, 2.3f), M["V5_ArchStone"], a);
        }
        // transverse stone arches
        foreach (var ax in new[] { 7f, 14f, 21f, 28f, 35f })
            MeshObj("Arch", MeshKit.ArchWall($"V5_W1Arch", 2 * Hw + 0.6f, H, 1.0f, MeshKit.Arch.Round, 10.4f, 2.6f, 4.2f), M["V5_ArchStone"], a, new Vector3(ax - 0.5f, 0, 0), Quaternion.Euler(0, 90, 0));
        ShadowGap(a, new Vector3(0, 0, Hw), new Vector3(L, 0, Hw), Vector3.back);
        ShadowGap(a, new Vector3(0, 0, -Hw), new Vector3(L, 0, -Hw), Vector3.forward);

        WallIntro(x.decor, x.w, new Vector3(3.5f, 2.3f, Hw - 0.01f), Vector3.back, 4.4f, new Color(0.12f, 0.12f, 0.11f));
        var slots = new List<WSlot>();
        foreach (var (bx, side) in new[] { (10.5f, 1), (10.5f, -1), (17.5f, 1), (24.5f, -1), (31.5f, 1), (31.5f, -1), (38.5f, 1) })
            slots.Add(new WSlot(new Vector3(bx, 1.62f, side * (Hw - 0.01f)), new Vector3(0, 0, -side), 2.3f, 1.8f, new Vector3(bx, H - 0.3f, side * 2.6f)));
        PlaceItems(x, slots);
        Centrepiece(x, x.w.centre, new Vector3(24.5f, 0, 1.6f), 270f, H);
        CentreFixtures(new Vector3(24.5f, 0, 1.6f), 270f, H);
        DigitalTable(x, new Vector3(4.2f, 0, -4.6f), 180f);
        Bench(x.decor, new Vector3(17.5f, 0, -1.2f), 0f, "V5_Oak");
        Bench(x.decor, new Vector3(38.0f, 0, -2.0f), 0f, "V5_Oak");
        Tracks(x, H);
        for (int b = 0; b < 6; b++) AreaLight("BayCove", x.lights, new Vector3(3.5f + 7 * b, H - 0.02f, 0), new Vector2(4.5f, 9f), Vector3.down, 2.4f, new Color(1f, 0.93f, 0.84f));
        ProbeGrid(x.lights, new Vector3(1, 0.6f, -6), new Vector3(41, 6.6f, 6), 3.5f);
        ReflectionProbeBox(x.lights, new Vector3(L / 2, H / 2, 0), new Vector3(L, H, 2 * Hw));
        Doorway(a, "Court", new Vector3(-0.4f, 0, 0), Vector3.right, 3.2f, 4.4f, "Lobby", "W1", "The Great Court", M["V5_Plaster"]);
        Doorway(a, "Next", new Vector3(L + 0.4f, 0, 0), Vector3.left, 3.2f, 4.4f, SceneOfWing[1], "Entry", "Next gallery\n<size=70%>Classical India</size>", M["V5_Plaster"]);
        Walk(x, new[] { 3f, 9f, 15f, 21f, 31f, 37f, 40.5f }.Select(px => new Vector3(px, 0, 0)), 90f);
        return (new Vector3(2.4f, 0, 0), 90f);
    }

    // ======================================================================= W2: top-lit hall
    static (Vector3, float) GallerySkylit(Ctx x)
    {
        DaylightScene(new Vector3(62, 150, 0), 1.9f);
        const float L = 44f, Hw = 8f, H = 9f;
        var a = x.arch; var wall = M["V5_PlasterWarm"];
        Box("Floor", new Vector3(L / 2, -0.15f, 0), new Vector3(L + 0.8f, 0.3f, 2 * Hw + 0.8f), M["V5_PaleFloor"], a);
        WallX(a, "WallN", -0.4f, L + 0.4f, Hw + 0.2f, 0.4f, 0, H, wall);
        WallX(a, "WallS", -0.4f, L + 0.4f, -Hw - 0.2f, 0.4f, 0, H, wall);
        WallZ(a, "WallW", -Hw - 0.4f, Hw + 0.4f, -0.2f, 0.4f, 0, H, wall, new Vector3(0, 3.4f, 4.6f));
        WallZ(a, "WallE", -Hw - 0.4f, Hw + 0.4f, L + 0.2f, 0.4f, 0, H, wall, new Vector3(0, 3.4f, 4.6f));
        foreach (var s in new[] { 1f, -1f })
        {
            Box("Dado", new Vector3(L / 2, 0.55f, s * (Hw - 0.03f)), new Vector3(L, 1.1f, 0.06f), M["V5_Sandstone"], a);
            Box("Cornice", new Vector3(L / 2, 6.55f, s * (Hw - 0.1f)), new Vector3(L, 0.3f, 0.2f), M["V5_Sandstone"], a);
            Box("DadoW", new Vector3(0.03f, 0.55f, s * 5.0f), new Vector3(0.06f, 1.1f, 6.0f), M["V5_Sandstone"], a);
            Box("DadoE", new Vector3(L - 0.03f, 0.55f, s * 5.0f), new Vector3(0.06f, 1.1f, 6.0f), M["V5_Sandstone"], a);
        }
        // ceiling with a long skylight lantern
        float sx0 = 4f, sx1 = L - 4f, sz = 2.6f;
        Box("Ceil_W", new Vector3((-0.4f + sx0) / 2, H + 0.15f, 0), new Vector3(sx0 + 0.4f, 0.3f, 2 * Hw + 0.8f), M["V5_Plaster"], a);
        Box("Ceil_E", new Vector3((sx1 + L + 0.4f) / 2, H + 0.15f, 0), new Vector3(L + 0.4f - sx1, 0.3f, 2 * Hw + 0.8f), M["V5_Plaster"], a);
        foreach (var s in new[] { 1f, -1f })
        {
            Box("Ceil_Side", new Vector3((sx0 + sx1) / 2, H + 0.15f, s * (sz + Hw + 0.4f) / 2), new Vector3(sx1 - sx0, 0.3f, Hw + 0.4f - sz), M["V5_Plaster"], a);
            Box("Well", new Vector3((sx0 + sx1) / 2, H + 0.9f, s * (sz + 0.1f)), new Vector3(sx1 - sx0 + 0.4f, 1.5f, 0.2f), M["V5_Plaster"], a);
        }
        foreach (var s in new[] { sx0 - 0.1f, sx1 + 0.1f }) Box("WellEnd", new Vector3(s, H + 0.9f, 0), new Vector3(0.2f, 1.5f, 2 * sz + 0.4f), M["V5_Plaster"], a);
        var glass = Box("Skylight", new Vector3((sx0 + sx1) / 2, H + 1.62f, 0), new Vector3(sx1 - sx0, 0.03f, 2 * sz), M["V5_Glass"], a, 0, false);
        Object.DestroyImmediate(glass.GetComponent<Collider>()); glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        GMesh("Glazing", "W2_Glazing", mb => { for (float gx = sx0; gx <= sx1 + 0.01f; gx += 2f) Geo.Bar(mb, new Vector3(gx, 0, -sz), new Vector3(gx, 0, sz), 0.06f, 0.12f); Geo.Bar(mb, new Vector3(sx0, 0, 0), new Vector3(sx1, 0, 0), 0.08f, 0.14f); },
              M["V5_Steel"], a, new Vector3(0, H + 1.58f, 0), Quaternion.identity, false);
        ShadowGap(a, new Vector3(0, 0, Hw - 0.06f), new Vector3(L, 0, Hw - 0.06f), Vector3.back);

        WallIntro(x.decor, x.w, new Vector3(0.02f, 2.5f, 4.9f), Vector3.right, 4.6f, new Color(0.14f, 0.12f, 0.1f));
        var slots = new List<WSlot>();
        foreach (var (bx, side) in new[] { (9f, 1), (9f, -1), (17f, 1), (17f, -1), (29f, 1), (29f, -1), (37f, 1) })
            slots.Add(new WSlot(new Vector3(bx, 1.85f, side * (Hw - 0.07f)), new Vector3(0, 0, -side), 2.3f, 2.0f, new Vector3(bx, H - 0.3f, side * 4.6f)));
        PlaceItems(x, slots);
        Centrepiece(x, x.w.centre, new Vector3(23f, 0, 0), 270f, H);
        CentreFixtures(new Vector3(23f, 0, 0), 270f, H);
        DigitalTable(x, new Vector3(4.5f, 0, -5.8f), 180f);
        Bench(x.decor, new Vector3(13f, 0, 0), 0f, "V5_Oak"); Bench(x.decor, new Vector3(33f, 0, 0), 0f, "V5_Oak");
        Tracks(x, H);
        AreaLight("SkylightDiffuse", x.lights, new Vector3((sx0 + sx1) / 2, H + 1.5f, 0), new Vector2(sx1 - sx0, 2 * sz), Vector3.down, 3.0f, new Color(0.95f, 0.97f, 1f));
        ProbeGrid(x.lights, new Vector3(1, 0.6f, -7), new Vector3(43, 8f, 7), 3.5f);
        ReflectionProbeBox(x.lights, new Vector3(L / 2, H / 2, 0), new Vector3(L, H, 2 * Hw));
        Doorway(a, "Court", new Vector3(-0.4f, 0, 0), Vector3.right, 3.4f, 4.6f, "Lobby", "W2", "The Great Court", M["V5_Plaster"]);
        Doorway(a, "Next", new Vector3(L + 0.4f, 0, 0), Vector3.left, 3.4f, 4.6f, SceneOfWing[2], "Entry", "Next gallery\n<size=70%>Medieval India</size>", M["V5_Plaster"]);
        Walk(x, new[] { 3f, 9f, 17f, 29f, 37f, 41f }.Select(px => new Vector3(px, 0, 0)), 90f);
        return (new Vector3(2.4f, 0, 0), 90f);
    }

    // ======================================================================= W3: octagonal domed hall
    static (Vector3, float) GalleryOctagon(Ctx x)
    {
        DaylightScene(new Vector3(72, 200, 0), 2.8f);
        const float R = 12f, H = 9f, drumTop = 10.2f, domeTop = 18.6f, oculus = 2.4f;
        float Rv = R / Mathf.Cos(22.5f * Mathf.Deg2Rad);
        var a = x.arch;
        Box("Floor", new Vector3(0, -0.15f, 0), new Vector3(36, 0.3f, 36), M["V5_MarbleFloor"], a);
        GMesh("Inlay", "W3_Inlay", mb => { Geo.RingWall(mb, 3.4f, 3.8f, 0f, 0.004f, 0, 360, 72); Geo.RingWall(mb, 8.6f, 8.9f, 0f, 0.004f, 0, 360, 96); }, M["V5_Granite"], a, Vector3.zero, Quaternion.identity);
        var slots = new List<WSlot>();
        for (int k = 0; k < 8; k++)
        {
            float th = (-90f + 45f * k) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th));
            var side = Group(a, "Side_" + k);
            side.SetPositionAndRotation(outward * R, Quaternion.LookRotation(outward));
            MeshObj("ArchWall", MeshKit.ArchWall("V5_W3Arch", 2 * R * Mathf.Tan(22.5f * Mathf.Deg2Rad) + 0.25f, H, 0.8f, MeshKit.Arch.Cusped, 5.6f, 3.6f, 2.6f), M["V5_RedStone"], side, side.position, side.rotation);
            // marble frame around the arch and a band at the springing
            Box("Pier_L", new Vector3(-3.0f, H / 2, -0.06f), new Vector3(0.3f, H, 0.12f), M["V5_Marble"], side);
            Box("Pier_R", new Vector3(3.0f, H / 2, -0.06f), new Vector3(0.3f, H, 0.12f), M["V5_Marble"], side);
            Box("Band", new Vector3(0, 7.0f, -0.06f), new Vector3(9.9f, 0.25f, 0.12f), M["V5_Marble"], side);
            if (k == 0) continue;     // entrance
            Box("NicheBack", new Vector3(0, 3.2f, 3.9f), new Vector3(6.2f, 6.4f, 0.2f), M["V5_Plaster"], side);
            Box("NicheSideL", new Vector3(-3.0f, 3.2f, 2.3f), new Vector3(0.2f, 6.4f, 3.2f), M["V5_RedStone"], side);
            Box("NicheSideR", new Vector3(3.0f, 3.2f, 2.3f), new Vector3(0.2f, 6.4f, 3.2f), M["V5_RedStone"], side);
            Box("NicheCeil", new Vector3(0, 6.3f, 2.3f), new Vector3(6.2f, 0.2f, 3.2f), M["V5_Plaster"], side);
            var c = side.TransformPoint(new Vector3(0, 1.75f, 3.79f)); c.y = 1.75f;
            var spot = side.TransformPoint(new Vector3(0, 6.0f, 1.0f));
            slots.Add(new WSlot(c, -outward, 2.5f, 2.2f, spot));
        }
        // drum band and dome with an oculus (double-sided so daylight only enters through the oculus)
        GMesh("Drum", "W3_Drum", mb => { Geo.Revolve(mb, Pts(Rv, H, Rv, drumTop), 8, false, -67.5f, 292.5f); mb.DoubleSide(); }, M["V5_Marble"], a, Vector3.zero, Quaternion.identity);
        GMesh("Cornice", "W3_Cornice", mb => Geo.Revolve(mb, Pts(Rv - 0.02f, H - 0.3f, Rv - 0.35f, H - 0.15f, Rv - 0.35f, H + 0.05f, Rv - 0.02f, H + 0.1f), 8, false, -67.5f, 292.5f), M["V5_Marble"], a, Vector3.zero, Quaternion.identity);
        var dome = new List<Vector2>();
        for (int i = 0; i <= 14; i++) { float p = i / 14f * Mathf.PI / 2; dome.Add(new Vector2(oculus + (Rv - oculus) * Mathf.Cos(p), drumTop + (domeTop - drumTop) * Mathf.Sin(p))); }
        GMesh("Dome", "W3_Dome", mb => { Geo.Revolve(mb, dome, 8, false, -67.5f, 292.5f); mb.DoubleSide(); }, M["V5_PlasterWarm"], a, Vector3.zero, Quaternion.identity);
        GMesh("Ribs", "W3_Ribs", mb =>
        {
            for (int k = 0; k < 8; k++)
            {
                float ang = (-67.5f + 45f * k) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                for (int i = 0; i < dome.Count - 1; i++)
                    Geo.Bar(mb, d * (dome[i].x - 0.12f) + Vector3.up * dome[i].y, d * (dome[i + 1].x - 0.12f) + Vector3.up * dome[i + 1].y, 0.3f, 0.22f);
            }
        }, M["V5_Marble"], a, Vector3.zero, Quaternion.identity, false);
        GMesh("Oculus", "W3_Oculus", mb => { Geo.RingWall(mb, oculus, oculus + 0.35f, domeTop - 0.1f, domeTop + 0.5f, 0, 360, 48); }, M["V5_Marble"], a, Vector3.zero, Quaternion.identity);

        var panel = Group(x.decor, "IntroPanel");
        var pp = new Vector3(-4.6f, 0, -7.6f); var pf = (new Vector3(0, 0, -11) - pp).normalized; pf = new Vector3(0.35f, 0, -1f).normalized;
        GMesh("Panel", "IntroStand", mb => Geo.ChamferBox(mb, new Vector3(0, 1.4f, 0), new Vector3(2.8f, 2.8f, 0.12f), 0.01f), M["V5_Charcoal"], panel, pp, Quaternion.LookRotation(-pf), true);
        WallIntro(panel, x.w, pp + Vector3.up * 1.15f + pf * 0.07f, pf, 2.5f, new Color(0.93f, 0.9f, 0.84f));
        PlaceItems(x, slots);
        Centrepiece(x, x.w.centre, Vector3.zero, 180f, 12f);
        DigitalTable(x, new Vector3(5.2f, 0, -7.2f), 200f);
        Bench(x.decor, new Vector3(-3.2f, 0, 4.8f), 0f, "V5_DarkWood"); Bench(x.decor, new Vector3(3.2f, 0, 4.8f), 0f, "V5_DarkWood");
        foreach (var f in fixtures) GMesh("NicheLamp", "NicheLamp", mb => Geo.Revolve(mb, Pts(0.001f, 0, 0.06f, 0, 0.07f, 0.14f, 0.001f, 0.14f), 16, true), M["V5_Brass"], x.decor, f.from, Quaternion.FromToRotation(Vector3.up, (f.from - f.to).normalized), false);
        for (int k = 0; k < 8; k++)
        {
            float th = (-90f + 45f * k) * Mathf.Deg2Rad; var o = new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th));
            AreaLight("DomeUplight", x.lights, o * (R - 0.6f) + Vector3.up * (H + 0.2f), new Vector2(5.5f, 0.3f), (Vector3.up * 1.4f - o).normalized, 9f, new Color(1f, 0.9f, 0.76f));
        }
        AreaLight("OculusSky", x.lights, Vector3.up * (domeTop - 0.2f), new Vector2(3.4f, 3.4f), Vector3.down, 22f, new Color(0.95f, 0.97f, 1f));
        ProbeGrid(x.lights, new Vector3(-10, 0.6f, -10), new Vector3(10, 9f, 10), 4f);
        ReflectionProbeBox(x.lights, new Vector3(0, 6f, 0), new Vector3(2 * R, 12f, 2 * R));
        Doorway(a, "Court", new Vector3(0, 0, -R - 0.8f), Vector3.forward, 5.0f, 5.6f, "Lobby", "W3", "The Great Court", M["V5_RedStone"]);
        var ring = new List<Vector3>(); for (int k = 0; k < 8; k++) { float th = (-90f + 45f * k + 22.5f) * Mathf.Deg2Rad; ring.Add(new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th)) * 6.5f); }
        Walk(x, ring, 0f);
        return (new Vector3(0, 0, -9.6f), 0f);
    }

    // ======================================================================= W4: picture gallery under a laylight
    static (Vector3, float) GalleryPictures(Ctx x)
    {
        DaylightScene(new Vector3(55, 160, 0), 2.0f);
        const float L = 36f, Hw = 7f, H = 8f;
        var a = x.arch; var wall = M["V5_GalleryGreen"];
        Box("Floor", new Vector3(L / 2, -0.15f, 0), new Vector3(L + 0.8f, 0.3f, 2 * Hw + 0.8f), M["V5_Parquet"], a);
        WallX(a, "WallN", -0.4f, L + 0.4f, Hw + 0.2f, 0.4f, 0, H, wall);
        WallX(a, "WallS", -0.4f, L + 0.4f, -Hw - 0.2f, 0.4f, 0, H, wall);
        WallZ(a, "WallW", -Hw - 0.4f, Hw + 0.4f, -0.2f, 0.4f, 0, H, wall, new Vector3(0, 3.0f, 4.2f));
        WallZ(a, "WallE", -Hw - 0.4f, Hw + 0.4f, L + 0.2f, 0.4f, 0, H, wall, new Vector3(0, 3.0f, 4.2f));
        foreach (var s in new[] { 1f, -1f })
        {
            Box("Dado", new Vector3(L / 2, 0.5f, s * (Hw - 0.03f)), new Vector3(L, 1.0f, 0.06f), M["V5_DarkWood"], a);
            Box("DadoRail", new Vector3(L / 2, 1.02f, s * (Hw - 0.05f)), new Vector3(L, 0.06f, 0.1f), M["V5_DarkWood"], a);
            Box("PictureRail", new Vector3(L / 2, 5.4f, s * (Hw - 0.04f)), new Vector3(L, 0.08f, 0.08f), M["V5_Gilt"], a);
            Box("Cornice", new Vector3(L / 2, H - 0.2f, s * (Hw - 0.15f)), new Vector3(L, 0.4f, 0.3f), M["V5_Plaster"], a);
            foreach (var ex in new[] { 0.03f, L - 0.03f }) Box("DadoEnd", new Vector3(ex, 0.5f, s * 4.6f), new Vector3(0.06f, 1.0f, 4.8f), M["V5_DarkWood"], a);
        }
        // laylight ceiling: frosted glass grid lit from above
        float lx0 = 2.5f, lx1 = L - 2.5f, lz = 5f;
        Box("Ceil_W", new Vector3((-0.4f + lx0) / 2, H + 0.15f, 0), new Vector3(lx0 + 0.4f, 0.3f, 2 * Hw + 0.8f), M["V5_Plaster"], a);
        Box("Ceil_E", new Vector3((lx1 + L + 0.4f) / 2, H + 0.15f, 0), new Vector3(L + 0.4f - lx1, 0.3f, 2 * Hw + 0.8f), M["V5_Plaster"], a);
        foreach (var s in new[] { 1f, -1f }) Box("Ceil_Side", new Vector3((lx0 + lx1) / 2, H + 0.15f, s * (lz + Hw + 0.4f) / 2), new Vector3(lx1 - lx0, 0.3f, Hw + 0.4f - lz), M["V5_Plaster"], a);
        Box("Laylight", new Vector3((lx0 + lx1) / 2, H + 0.32f, 0), new Vector3(lx1 - lx0, 0.04f, 2 * lz), M["V5_Laylight"], a);
        GMesh("LaylightGrid", "W4_Grid", mb =>
        {
            for (float gx = lx0; gx <= lx1 + 0.01f; gx += 1.24f) Geo.Bar(mb, new Vector3(gx, 0, -lz), new Vector3(gx, 0, lz), 0.05f, 0.08f);
            for (float gz = -lz; gz <= lz + 0.01f; gz += 1.25f) Geo.Bar(mb, new Vector3(lx0, 0, gz), new Vector3(lx1, 0, gz), 0.05f, 0.08f);
        }, M["V5_Plaster"], a, new Vector3(0, H + 0.26f, 0), Quaternion.identity, false);
        AreaLight("Laylight", x.lights, new Vector3((lx0 + lx1) / 2, H + 0.25f, 0), new Vector2(lx1 - lx0, 2 * lz), Vector3.down, 7.5f, new Color(1f, 0.97f, 0.92f));

        WallIntro(x.decor, x.w, new Vector3(0.02f, 2.6f, 4.6f), Vector3.right, 4.2f, new Color(0.93f, 0.9f, 0.84f));
        var slots = new List<WSlot>();
        foreach (var (bx, side) in new[] { (6.5f, 1), (6.5f, -1), (13.5f, 1), (13.5f, -1), (22.5f, 1), (29.5f, 1), (29.5f, -1) })
            slots.Add(new WSlot(new Vector3(bx, 2.3f, side * (Hw - 0.07f)), new Vector3(0, 0, -side), 1.7f, 2.2f, new Vector3(bx, H - 0.25f, side * 3.4f)));
        PlaceItems(x, slots, gilt: true);
        Centrepiece(x, x.w.centre, new Vector3(18f, 0, 0), 270f, H);
        DigitalTable(x, new Vector3(4.2f, 0, -5.0f), 180f);
        Bench(x.decor, new Vector3(10.5f, 0, 0), 0f, "V5_Leather"); Bench(x.decor, new Vector3(26f, 0, 0), 0f, "V5_Leather");
        foreach (var f in fixtures) GMesh("Lamp", "PictureLamp", mb => Geo.Revolve(mb, Pts(0.001f, 0, 0.05f, 0, 0.06f, 0.15f, 0.001f, 0.15f), 16, true), M["V5_Brass"], x.decor, f.from, Quaternion.FromToRotation(Vector3.up, (f.from - f.to).normalized), false);
        ProbeGrid(x.lights, new Vector3(1, 0.6f, -6), new Vector3(35, 7f, 6), 3.5f);
        ReflectionProbeBox(x.lights, new Vector3(L / 2, H / 2, 0), new Vector3(L, H, 2 * Hw));
        Doorway(a, "Court", new Vector3(-0.4f, 0, 0), Vector3.right, 3.0f, 4.2f, "Lobby", "W4", "The Great Court", M["V5_DarkWood"]);
        Doorway(a, "Next", new Vector3(L + 0.4f, 0, 0), Vector3.left, 3.0f, 4.2f, SceneOfWing[4], "Entry", "Next gallery\n<size=70%>Freedom and the Republic</size>", M["V5_DarkWood"]);
        Walk(x, new[] { 3f, 6.5f, 13.5f, 22.5f, 29.5f, 33.5f }.Select(px => new Vector3(px, 0, 0)), 90f);
        return (new Vector3(2.4f, 0, 0), 90f);
    }

    // ======================================================================= W5: spiral rotunda
    static (Vector3, float) GalleryRotunda(Ctx x)
    {
        DaylightScene(new Vector3(66, 210, 0), 2.6f);
        const float R = 14f, Ht = 13.6f, slab = 3.6f, rIn = 7.2f, turn = 3.4f;
        var a = x.arch; var white = M["V5_Plaster"];
        Box("Floor", new Vector3(0, -0.15f, 0), new Vector3(32, 0.3f, 32), M["V5_Concrete"], a);
        float hs = 1.9f / R * Mathf.Rad2Deg, hn = 1.5f / R * Mathf.Rad2Deg;     // half-angles of the south (3.8 m) and north (3.0 m) doors
        GMesh("OuterWall", "W5_Wall", mb =>
        {
            Geo.RingWall(mb, R, R + 0.4f, 0, Ht, 270f + hs, 450f - hn, 64); Geo.RingWall(mb, R, R + 0.4f, 0, Ht, 90f + hn, 270f - hs, 64);
            Geo.RingWall(mb, R, R + 0.4f, 4.4f, Ht, 270f - hs, 270f + hs, 4); Geo.RingWall(mb, R, R + 0.4f, 3.4f, Ht, 90f - hn, 90f + hn, 4);
        }, white, a, Vector3.zero, Quaternion.identity, true, true);
        GMesh("Level1", "W5_Level1", mb => { Geo.RingWall(mb, rIn, R, slab, slab + 0.4f, 0, 360, 96); Geo.RingWall(mb, rIn, rIn + 0.32f, slab + 0.4f, slab + 1.5f, 0, 360, 96); }, white, a, Vector3.zero, Quaternion.identity, true, true);
        GMesh("Ramp", "W5_Ramp", mb => { Geo.Helix(mb, rIn, R, 0f, 0.4f, 15f, 735f, 2 * turn, 180); Geo.Helix(mb, rIn, rIn + 0.32f, 0.4f, 1.1f, 15f, 735f, 2 * turn, 180); }, white, a, new Vector3(0, slab + 0.05f, 0), Quaternion.identity);
        GMesh("TopLevel", "W5_Top", mb => { Geo.RingWall(mb, rIn, R, slab + 2 * turn + 0.1f, slab + 2 * turn + 0.5f, 0, 360, 96); Geo.RingWall(mb, rIn, rIn + 0.32f, slab + 2 * turn + 0.5f, slab + 2 * turn + 1.6f, 0, 360, 96); }, white, a, Vector3.zero, Quaternion.identity);
        // glass dome with steel ribs
        var cap = new List<Vector2>(); for (int i = 0; i <= 10; i++) { float p = i / 10f * Mathf.PI / 2; cap.Add(new Vector2(Mathf.Max(0.001f, (R + 0.4f) * Mathf.Cos(p)), Ht + 3.6f * Mathf.Sin(p))); }
        var domeGlass = GMesh("DomeGlass", "W5_DomeGlass", mb => { Geo.Revolve(mb, cap, 48, true); mb.DoubleSide(); }, M["V5_Glass"], a, Vector3.zero, Quaternion.identity, false);
        domeGlass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(domeGlass, 0);
        GMesh("DomeRibs", "W5_DomeRibs", mb =>
        {
            for (int k = 0; k < 16; k++)
            {
                float ang = k * 22.5f * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                for (int i = 0; i < cap.Count - 1; i++) Geo.Bar(mb, d * cap[i].x + Vector3.up * cap[i].y, d * cap[i + 1].x + Vector3.up * cap[i + 1].y, 0.12f, 0.2f);
            }
            foreach (int i in new[] { 0, 4, 7 }) for (int k = 0; k < 48; k++)
            {
                float a0 = k * 7.5f * Mathf.Deg2Rad, a1 = (k + 1) * 7.5f * Mathf.Deg2Rad;
                Geo.Bar(mb, new Vector3(Mathf.Cos(a0) * cap[i].x, cap[i].y, Mathf.Sin(a0) * cap[i].x), new Vector3(Mathf.Cos(a1) * cap[i].x, cap[i].y, Mathf.Sin(a1) * cap[i].x), 0.12f, 0.16f);
            }
        }, M["V5_Steel"], a, Vector3.zero, Quaternion.identity, false);

        {   // intro on the curved wall left of the entrance
            float ang = 245f * Mathf.Deg2Rad; var o = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            WallIntro(x.decor, x.w, o * (R - 0.12f) + Vector3.up * 2.0f, -o, 4.2f, new Color(0.12f, 0.12f, 0.12f));
        }
        var slots = new List<WSlot>();
        foreach (var deg in new[] { 300f, 338f, 16f, 54f, 126f, 164f, 202f })
        {
            float ang = deg * Mathf.Deg2Rad; var o = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            slots.Add(new WSlot(o * (R - 0.12f) + Vector3.up * 1.75f, -o, 2.0f, 1.7f, o * 10.6f + Vector3.up * (slab - 0.1f)));
        }
        PlaceItems(x, slots);
        Centrepiece(x, x.w.centre, Vector3.zero, 180f, 12f);
        DigitalTable(x, new Vector3(-5.2f, 0, -8.8f), 180f);
        RoundSeat(x.decor, new Vector3(-4.2f, 0, 3.6f)); RoundSeat(x.decor, new Vector3(4.2f, 0, 3.6f));
        foreach (var f in fixtures) GMesh("Fixture", "SpotFixtureR", mb => Geo.Revolve(mb, Pts(0.001f, 0, 0.045f, 0, 0.05f, 0.16f, 0.001f, 0.16f), 16, true), M["V5_Charcoal"], x.decor, f.from, Quaternion.FromToRotation(Vector3.up, (f.from - f.to).normalized), false);
        for (int k = 0; k < 12; k++)
        {
            float ang = k * 30f * Mathf.Deg2Rad; var o = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            AreaLight("SoffitLight", x.lights, o * 11.8f + Vector3.up * (slab - 0.02f), new Vector2(2.0f, 0.6f), Vector3.down, 9f, new Color(1f, 0.95f, 0.88f));
        }
        ProbeGrid(x.lights, new Vector3(-12, 0.6f, -12), new Vector3(12, 12f, 12), 4f);
        ReflectionProbeBox(x.lights, new Vector3(0, 7f, 0), new Vector3(2 * R, 14f, 2 * R));
        Doorway(a, "Court", new Vector3(0, 0, -R - 0.4f), Vector3.forward, 3.8f, 4.4f, "Lobby", "W5", "The Great Court", M["V5_Plaster"]);
        Doorway(a, "Next", new Vector3(0, 0, R + 0.4f), Vector3.back, 3.0f, 3.4f, SceneOfWing[5], "Entry", "Next gallery\n<size=70%>Themes of Indian History</size>", M["V5_Plaster"]);
        var ring = new List<Vector3>(); foreach (var d in new[] { 45f, 135f, 225f, 315f }) { float ang = d * Mathf.Deg2Rad; ring.Add(new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 4.6f); }
        ring.Add(new Vector3(0, 0, 11.5f));
        Walk(x, ring, 0f);
        return (new Vector3(0, 0, -11.2f), 0f);
    }

    // ======================================================================= W6: modern skylit hall onto a courtyard
    static (Vector3, float) GalleryModern(Ctx x)
    {
        DaylightScene(new Vector3(48, -65, 0), 2.8f);
        const float L = 40f, Hw = 10f, H = 8f;
        var a = x.arch; var white = M["V5_Plaster"];
        Box("Floor", new Vector3(L / 2, -0.15f, 0), new Vector3(L + 0.8f, 0.3f, 2 * Hw + 0.8f), M["V5_Concrete"], a);
        WallX(a, "WallN", -0.4f, L, Hw + 0.2f, 0.4f, 0, H, white);
        WallX(a, "WallS", -0.4f, L, -Hw - 0.2f, 0.4f, 0, H, white);
        WallZ(a, "WallW", -Hw - 0.4f, Hw + 0.4f, -0.2f, 0.4f, 0, H, white, new Vector3(0, 3.4f, 4.4f));
        // ceiling with three deep skylight wells
        float[] wells = { 8f, 20f, 32f }; const float ws = 4f;
        Box("Ceil_A", new Vector3((-0.4f + 4f) / 2, H + 0.15f, 0), new Vector3(4.4f, 0.3f, 2 * Hw + 0.8f), white, a);
        Box("Ceil_B", new Vector3(14f, H + 0.15f, 0), new Vector3(4f, 0.3f, 2 * Hw + 0.8f), white, a);
        Box("Ceil_C", new Vector3(26f, H + 0.15f, 0), new Vector3(4f, 0.3f, 2 * Hw + 0.8f), white, a);
        Box("Ceil_D", new Vector3(38f, H + 0.15f, 0), new Vector3(4f, 0.3f, 2 * Hw + 0.8f), white, a);
        foreach (var wx in wells)
        {
            foreach (var s in new[] { 1f, -1f })
            {
                Box("CeilStrip", new Vector3(wx, H + 0.15f, s * (ws + Hw + 0.4f) / 2), new Vector3(2 * ws, 0.3f, Hw + 0.4f - ws), white, a);
                Box("WellZ", new Vector3(wx, H + 0.9f, s * (ws + 0.1f)), new Vector3(2 * ws + 0.4f, 1.5f, 0.2f), white, a);
                Box("WellX", new Vector3(wx + s * (ws + 0.1f), H + 0.9f, 0), new Vector3(0.2f, 1.5f, 2 * ws), white, a);
            }
            var gl = Box("SkyGlass", new Vector3(wx, H + 1.64f, 0), new Vector3(2 * ws, 0.03f, 2 * ws), M["V5_Glass"], a, 0, false);
            Object.DestroyImmediate(gl.GetComponent<Collider>()); gl.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            AreaLight("WellDiffuse", x.lights, new Vector3(wx, H + 1.5f, 0), new Vector2(2 * ws - 0.2f, 2 * ws - 0.2f), Vector3.down, 3.5f, new Color(0.95f, 0.97f, 1f));
        }
        // glazed east wall onto a courtyard with a reflecting pool
        var glass = Box("GlassWall", new Vector3(L, H / 2, 0), new Vector3(0.03f, H, 2 * Hw), M["V5_Glass"], a, 0, false);
        glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off; GameObjectUtility.SetStaticEditorFlags(glass, 0);
        GMesh("Mullions", "W6_Mullions", mb =>
        {
            for (float z = -Hw; z <= Hw + 0.01f; z += 2.5f) Geo.ChamferBox(mb, new Vector3(0, H / 2, z), new Vector3(0.12f, H, 0.08f), 0.01f);
            Geo.ChamferBox(mb, new Vector3(0, 3.2f, 0), new Vector3(0.1f, 0.1f, 2 * Hw), 0.01f);
            Geo.ChamferBox(mb, new Vector3(0, H - 0.1f, 0), new Vector3(0.2f, 0.2f, 2 * Hw), 0.01f);
        }, M["V5_Charcoal"], a, new Vector3(L, 0, 0), Quaternion.identity, false);
        Box("Courtyard", new Vector3(L + 10f, -0.15f, 0), new Vector3(20f, 0.3f, 30f), M["V5_PaleFloor"], a);
        Box("CourtWallE", new Vector3(L + 20.2f, 4.5f, 0), new Vector3(0.4f, 9f, 30f), M["V5_PortlandStone"], a);
        foreach (var s in new[] { 1f, -1f }) Box("CourtWallS", new Vector3(L + 10f, 4.5f, s * 15.2f), new Vector3(20.4f, 9f, 0.4f), M["V5_PortlandStone"], a);
        Box("Pool", new Vector3(L + 10f, 0.03f, 0), new Vector3(9f, 0.06f, 10f), M["V5_Water"], a);
        foreach (var s in new[] { 1f, -1f })
        {
            Box("Coping", new Vector3(L + 10f, 0.12f, s * 5.15f), new Vector3(9.6f, 0.24f, 0.3f), M["V5_Granite"], a);
            Box("Coping", new Vector3(L + 10f + s * 4.65f, 0.12f, 0), new Vector3(0.3f, 0.24f, 10.6f), M["V5_Granite"], a);
        }
        ShadowGap(a, new Vector3(0, 0, Hw), new Vector3(L, 0, Hw), Vector3.back);
        ShadowGap(a, new Vector3(0, 0, -Hw), new Vector3(L, 0, -Hw), Vector3.forward);

        WallIntro(x.decor, x.w, new Vector3(0.02f, 2.6f, 5.6f), Vector3.right, 5.2f, new Color(0.12f, 0.12f, 0.12f));
        var slots = new List<WSlot>();
        foreach (var (bx, side) in new[] { (8f, 1), (8f, -1), (17f, 1), (17f, -1), (31f, 1), (31f, -1) })
            slots.Add(new WSlot(new Vector3(bx, 1.8f, side * (Hw - 0.01f)), new Vector3(0, 0, -side), 2.6f, 2.0f, new Vector3(bx, H - 0.3f, side * 6.2f)));
        PlaceItems(x, slots);
        Centrepiece(x, x.w.centre, new Vector3(24f, 0, 0), 270f, H);
        CentreFixtures(new Vector3(24f, 0, 0), 270f, H);
        DigitalTable(x, new Vector3(4.2f, 0, -6.4f), 180f);
        RoundSeat(x.decor, new Vector3(12.5f, 0, 0), 1.0f); RoundSeat(x.decor, new Vector3(35.5f, 0, 2.5f), 1.0f);
        Tracks(x, H);
        ProbeGrid(x.lights, new Vector3(1, 0.6f, -9), new Vector3(39, 7f, 9), 4f);
        ReflectionProbeBox(x.lights, new Vector3(L / 2, H / 2, 0), new Vector3(L, H, 2 * Hw));
        Doorway(a, "Court", new Vector3(-0.4f, 0, 0), Vector3.right, 3.4f, 4.4f, "Lobby", "W6", "The Great Court", M["V5_Plaster"]);
        Walk(x, new[] { 3f, 8f, 17f, 30f, 37.5f }.Select(px => new Vector3(px, 0, 0)), 90f);
        return (new Vector3(2.4f, 0, 0), 90f);
    }
}
