using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// The Great Court: a glass-roofed courtyard (after the British Museum's Great Court) with a central stone drum,
/// two curving staircases, a classical portico and doorways to the six galleries.
public static partial class MuseumBuilder
{
    static string BuildCourtV5(CurJ cur)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        DaylightScene(new Vector3(55, 25, 0), 3.0f, 1f);
        var root = new GameObject("GreatCourt").transform;
        var a = Group(root, "Architecture"); var decor = Group(root, "Furniture"); var lights = Group(root, "Lighting"); var pts = Group(root, "WalkPoints");
        const float S = 24f, Hwall = 18f, Rd = 11f, Hd = 15.6f;
        var stone = M["V5_PortlandStone"];
        Box("Floor", new Vector3(0, -0.15f, 0), new Vector3(2 * S + 2, 0.3f, 2 * S + 2), M["V5_PaleFloor"], a);

        // ---- perimeter facades with six gallery doorways
        var doors = new[] {
            (wi: 0, p: new Vector3(-S, 0, -8), n: Vector3.right), (wi: 1, p: new Vector3(-S, 0, 8), n: Vector3.right),
            (wi: 2, p: new Vector3(-8, 0, S), n: Vector3.back), (wi: 3, p: new Vector3(8, 0, S), n: Vector3.back),
            (wi: 4, p: new Vector3(S, 0, 8), n: Vector3.left), (wi: 5, p: new Vector3(S, 0, -8), n: Vector3.left) };
        const float dw = 5f, dh = 7.5f;
        WallZ(a, "WallW", -S - 1, S + 1, -S - 0.5f, 1f, 0, Hwall, stone, new Vector3(-8, dw, dh), new Vector3(8, dw, dh));
        WallZ(a, "WallE", -S - 1, S + 1, S + 0.5f, 1f, 0, Hwall, stone, new Vector3(-8, dw, dh), new Vector3(8, dw, dh));
        WallX(a, "WallN", -S, S, S + 0.5f, 1f, 0, Hwall, stone, new Vector3(-8, dw, dh), new Vector3(8, dw, dh));
        WallX(a, "WallS", -S, S, -S - 0.5f, 1f, 0, Hwall, stone);
        foreach (var d in doors)
        {
            var w = cur.wings[d.wi];
            var right = Vector3.Cross(Vector3.up, d.n);
            var g = Group(a, "Doorcase_" + w.id);
            foreach (var s in new[] { -1f, 1f })
                Box("Jamb", d.p + right * s * (dw / 2 + 0.35f) + Vector3.up * (dh / 2) + d.n * 0.12f, Abs(right * 0.7f + d.n * 0.24f) + Vector3.up * dh, stone, g);
            Box("Lintel", d.p + Vector3.up * (dh + 0.5f) + d.n * 0.16f, Abs(right * (dw + 1.6f) + d.n * 0.32f) + Vector3.up * 1.0f, stone, g);
            Box("Cornice", d.p + Vector3.up * (dh + 1.1f) + d.n * 0.26f, Abs(right * (dw + 2.0f) + d.n * 0.52f) + Vector3.up * 0.2f, stone, g);
            var t = Text3D("Inscription", w.title.ToUpperInvariant(), d.p + Vector3.up * (dh + 1.85f) + d.n * 0.02f, d.n, new Vector2(dw + 3f, 0.75f), new Color(0.34f, 0.31f, 0.27f), g, false, 6f);
            t.characterSpacing = 12f;
            var y = Text3D("Years", w.years, d.p + Vector3.up * (dh + 2.45f) + d.n * 0.02f, d.n, new Vector2(dw + 2f, 0.35f), new Color(0.4f, 0.37f, 0.33f), g, false, 3f);
            // banner hanging above the doorway
            var bc = d.p + Vector3.up * 14.2f + d.n * 0.7f; var col = Hex(w.color);
            var cloth = Lit("V5_Banner_" + w.id, col * 0.8f, 0f, 0.05f);
            GMesh("Banner", "Banner", mb => Geo.ChamferBox(mb, Vector3.zero, new Vector3(3.4f, 6.4f, 0.03f), 0.004f), cloth, g, bc, FaceTo(d.n), false);
            var pm = CurPhotoMat(w.items[0].id, out float asp);
            if (pm) GMesh("BannerPhoto", "BannerPhoto_" + w.id, mb => Geo.ImageQuad(mb, Vector3.zero, Vector3.back, 3.0f, 3.0f / Mathf.Max(0.6f, asp)), pm, g, bc + Vector3.up * 1.2f + d.n * 0.02f, FaceTo(d.n), false);
            var bt = Text3D("BannerTitle", $"<b>{w.title}</b>\n<size=55%>{w.subtitle}</size>", bc + Vector3.down * 2.1f + d.n * 0.025f, d.n, new Vector2(3.0f, 1.6f), new Color(0.98f, 0.96f, 0.92f), g, false, 7f);
            GMesh("Rods", "BannerRod", mb => { Geo.Bar(mb, new Vector3(-1.8f, 3.25f, 0), new Vector3(1.8f, 3.25f, 0), 0.04f, 0.04f); Geo.Bar(mb, new Vector3(-1.5f, 3.25f, 0), new Vector3(-1.5f, 5.0f, 0), 0.015f, 0.015f); Geo.Bar(mb, new Vector3(1.5f, 3.25f, 0), new Vector3(1.5f, 5.0f, 0), 0.015f, 0.015f); },
                  M["V5_Charcoal"], g, bc, FaceTo(d.n), false);
            Doorway(a, w.id, d.p - d.n * 0.5f, d.n, dw - 0.4f, dh, SceneOfWing[d.wi], "Entry", $"{w.title}\n<size=55%>{w.subtitle}</size>", M["V5_Plaster"]);
            Marker("Arrive_" + w.id, d.p + d.n * 4.5f, Quaternion.LookRotation(d.n).eulerAngles.y, pts);
            MakePoint("Door_" + w.id, d.p + d.n * 4.5f, Quaternion.LookRotation(d.n).eulerAngles.y, pts);
        }

        // ---- classical portico on the south side
        var por = Group(a, "Portico");
        var colMesh = MeshKit.Column("V5_PorticoColumn", "delhi", 11f, 0.62f);
        for (int i = 0; i < 8; i++)
        {
            var cp = new Vector3(-10.5f + 3f * i, 0.6f, -S + 2.6f);
            MeshObj("Column", colMesh, stone, por, cp, Quaternion.identity);
        }
        for (int st = 0; st < 3; st++) Box("Step", new Vector3(0, 0.1f + 0.2f * st, -S + 1.9f - 0.45f * (2 - st) + 0.45f), new Vector3(26f - st * 0.6f, 0.2f, 1.6f + 0.9f * (2 - st)), stone, por);
        Box("Entablature", new Vector3(0, 12.4f, -S + 2.3f), new Vector3(25f, 1.6f, 2.6f), stone, por);
        Box("Architrave", new Vector3(0, 11.55f, -S + 2.65f), new Vector3(24.4f, 0.3f, 2.2f), stone, por);
        GMesh("Pediment", "Pediment", mb =>
        {
            float hw = 12.5f, h = 3.2f, z0 = 0f, z1 = 2.6f;
            Geo.Poly(mb, new[] { new Vector3(-hw, 0, z1), new Vector3(hw, 0, z1), new Vector3(0, h, z1) }, Vector3.forward);
            Geo.Poly(mb, new[] { new Vector3(-hw, 0, z0), new Vector3(hw, 0, z0), new Vector3(0, h, z0) }, Vector3.back);
            Geo.Quad(mb, new Vector3(-hw, 0, z0), new Vector3(-hw, 0, z1), new Vector3(0, h, z1), new Vector3(0, h, z0), new Vector3(-h, hw, 0));
            Geo.Quad(mb, new Vector3(hw, 0, z0), new Vector3(hw, 0, z1), new Vector3(0, h, z1), new Vector3(0, h, z0), new Vector3(h, hw, 0));
        }, stone, por, new Vector3(0, 13.2f, -S + 1.0f), Quaternion.identity);
        Text3D("Frieze", "MUSEUM OF INDIAN HISTORY", new Vector3(0, 12.45f, -S + 3.61f), Vector3.forward, new Vector2(20f, 0.9f), new Color(0.36f, 0.33f, 0.29f), por, false, 8f).characterSpacing = 18f;
        Box("GreatDoor", new Vector3(0, 3.5f, -S + 0.04f), new Vector3(4.4f, 7f, 0.1f), M["V5_DarkWood"], por);
        Box("DoorCase", new Vector3(0, 7.3f, -S + 0.1f), new Vector3(5.6f, 0.6f, 0.2f), stone, por);

        // ---- the central drum with information desk and staircases
        var drum = Group(a, "Drum");
        GMesh("DrumWall", "DrumWall", mb => Geo.RingWall(mb, Rd - 0.6f, Rd, 0, Hd, 0, 360, 128), stone, drum, Vector3.zero, Quaternion.identity, true, true);
        GMesh("DrumCornice", "DrumCornice", mb => { Geo.RingWall(mb, Rd - 0.6f, Rd + 0.45f, Hd - 0.5f, Hd, 0, 360, 128); Geo.RingWall(mb, Rd, Rd + 0.2f, 6.9f, 7.15f, 0, 360, 128); }, stone, drum, Vector3.zero, Quaternion.identity);
        GMesh("DrumCap", "DrumCap", mb => Geo.RingWall(mb, 0, Rd + 0.45f, Hd, Hd + 0.15f, 0, 360, 64), stone, drum, Vector3.zero, Quaternion.identity);
        for (int i = 0; i < 16; i++)
        {
            float ang = (i * 22.5f + 11.25f) * Mathf.Deg2Rad; var o = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            var win = Box("DrumWindow", o * (Rd + 0.01f) + Vector3.up * 11f, new Vector3(1.4f, 4.6f, 0.04f), M["V5_Screen"], drum, 0, true);
            win.transform.rotation = Quaternion.LookRotation(o);
            var frame = Box("DrumWindowFrame", o * (Rd + 0.03f) + Vector3.up * 13.45f, new Vector3(1.8f, 0.25f, 0.08f), stone, drum);
            frame.transform.rotation = Quaternion.LookRotation(o);
        }
        var doorsDrum = Box("ReadingRoomDoors", new Vector3(0, 2.6f, -Rd - 0.01f), new Vector3(4.2f, 5.2f, 0.06f), M["V5_Screen"], drum);
        Box("ReadingRoomCase", new Vector3(0, 5.45f, -Rd - 0.12f), new Vector3(5.2f, 0.5f, 0.3f), stone, drum);
        Text3D("ReadingRoom", "READING ROOM", new Vector3(0, 6.1f, -Rd - 0.05f), Vector3.back, new Vector2(5f, 0.4f), new Color(0.36f, 0.33f, 0.29f), drum, false, 3f).characterSpacing = 14f;
        GMesh("InfoDesk", "InfoDesk", mb => Geo.RingWall(mb, 12.0f, 12.8f, 0, 1.0f, 240f, 300f, 24), M["V5_Oak"], decor, Vector3.zero, Quaternion.identity, true, true);
        GMesh("InfoDeskTop", "InfoDeskTop", mb => Geo.RingWall(mb, 11.95f, 12.9f, 1.0f, 1.06f, 239f, 301f, 24), M["V5_PaintWhite"], decor, Vector3.zero, Quaternion.identity);
        Text3D("Info", "Information", new Vector3(0, 0.62f, -12.81f), Vector3.back, new Vector2(2.2f, 0.3f), new Color(0.95f, 0.93f, 0.88f), decor, false, 2.5f);

        // staircases: east stair rises anticlockwise from the south-east, west stair clockwise from the south-west, to a north landing
        const float r0 = Rd, r1 = 14.0f, rb = 14.3f, up = 6.0f; const int steps = 36;
        void Stair(string name, float aBottom, float aTop)
        {
            GMesh(name, name, mb =>
            {
                for (int k = 0; k < steps; k++)
                {
                    float f0 = k / (float)steps, f1 = (k + 1) / (float)steps;
                    float s0 = Mathf.Lerp(aBottom, aTop, f0), s1 = Mathf.Lerp(aBottom, aTop, f1);
                    Geo.RingWall(mb, r0, rb, 0, up * f1, Mathf.Min(s0, s1), Mathf.Max(s0, s1), 3);
                }
            }, stone, a, Vector3.zero, Quaternion.identity, true, true);
            GMesh(name + "Balustrade", name + "Bal", mb =>
            {
                float lo = Mathf.Min(aBottom, aTop), hi = Mathf.Max(aBottom, aTop);
                float rise = aTop > aBottom ? up : -up;
                Geo.Helix(mb, r1, rb, -0.2f, 1.25f, lo, hi, rise, 72);
            }, M["V5_PaintWhite"], a, new Vector3(0, aTop > aBottom ? up / steps : up + up / steps, 0), Quaternion.identity);
        }
        Stair("StairEast", -50f, 45f);
        Stair("StairWest", 230f, 135f);
        GMesh("Landing", "Landing", mb => { Geo.RingWall(mb, r0, rb, 0, up, 45f, 135f, 24); }, stone, a, Vector3.zero, Quaternion.identity, true, true);
        GMesh("LandingRail", "LandingRail", mb => Geo.RingWall(mb, r1, rb, up, up + 1.05f, 45f, 135f, 24), M["V5_PaintWhite"], a, Vector3.zero, Quaternion.identity);

        // ---- the tessellated glass roof (steel members lit by light probes; glass casts no shadow)
        const int Na = 72, Nr = 8;
        Vector3 RoofPt(int i, int j)
        {
            float th = i / (float)Na * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th));
            float edge = S / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z));
            float t = j / (float)Nr;
            float r = Mathf.Lerp(Rd + 0.45f, edge, t);
            return d * r + Vector3.up * (Mathf.Lerp(Hd + 0.15f, Hwall, t) + 1.3f * Mathf.Sin(Mathf.PI * t));
        }
        GMesh("RoofSteel", "RoofSteel", mb =>
        {
            for (int i = 0; i < Na; i++)
                for (int j = 0; j <= Nr; j++)
                {
                    var p = RoofPt(i, j);
                    Geo.Bar(mb, p, RoofPt(i + 1, j), 0.07f, 0.16f);
                    if (j < Nr)
                    {
                        Geo.Bar(mb, p, RoofPt(i, j + 1), 0.07f, 0.16f);
                        Geo.Bar(mb, p, RoofPt((i + j) % 2 == 0 ? i + 1 : i - 1, j + 1), 0.05f, 0.12f);
                    }
                }
        }, M["V5_Steel"], a, Vector3.zero, Quaternion.identity, false);
        var roofGlass = GMesh("RoofGlass", "RoofGlass", mb =>
        {
            for (int i = 0; i < Na; i++)
                for (int j = 0; j < Nr; j++)
                {
                    Geo.Quad(mb, RoofPt(i, j), RoofPt(i + 1, j), RoofPt(i + 1, j + 1), RoofPt(i, j + 1), Vector3.up);
                }
            mb.DoubleSide();
        }, M["V5_Glass"], a, Vector3.up * 0.09f, Quaternion.identity, false);
        roofGlass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(roofGlass, 0);
        Box("WallTop", new Vector3(0, Hwall + 0.15f, -S - 0.5f), new Vector3(2 * S + 2, 0.3f, 1.2f), stone, a);

        // ---- furniture and wayfinding
        foreach (var bp in new[] { new Vector3(-6f, 0, -17.5f), new Vector3(6f, 0, -17.5f) }) StoneBench(decor, bp, 0f);
        foreach (var bp in new[] { new Vector3(-19f, 0, 0), new Vector3(19f, 0, 0) }) StoneBench(decor, bp, 90f);
        var dir = Group(decor, "Directory");
        var dp = new Vector3(-4.2f, 0, -19.5f); var df = new Vector3(0.35f, 0, -1f).normalized;
        df = (new Vector3(0, 0, -21) - dp).normalized; df = new Vector3(0.25f, 0, -1).normalized;
        GMesh("Totem", "DirectoryTotem", mb => Geo.ChamferBox(mb, new Vector3(0, 1.35f, 0), new Vector3(1.5f, 2.7f, 0.14f), 0.015f), M["V5_Charcoal"], dir, dp, Quaternion.LookRotation(-df));
        var lines = string.Join("\n", cur.wings.Select((w, i) => $"<b>{w.title}</b>  <size=70%><color=#C9C2B6>{w.years}</color></size>\n<size=70%>{DoorHint(i)}</size>"));
        var tt = Text3D("DirectoryText", $"<size=130%><b>Galleries</b></size>\n\n{lines}", dp + Vector3.up * 1.4f + df * 0.075f, df, new Vector2(1.3f, 2.4f), new Color(0.95f, 0.93f, 0.88f), dir, false, 1.2f);
        tt.alignment = TMPro.TextAlignmentOptions.TopLeft;

        ProbeGrid(lights, new Vector3(-21, 1, -21), new Vector3(21, 17, 21), 6f);
        ReflectionProbeBox(lights, new Vector3(0, 9f, 0), new Vector3(2 * S, 18f, 2 * S), 256);
        foreach (var p in new[] { new Vector3(0, 0, -15f), new Vector3(-9, 0, -15f), new Vector3(9, 0, -15f), new Vector3(-17, 0, -15f), new Vector3(17, 0, -15f),
                                   new Vector3(-17, 0, 0), new Vector3(17, 0, 0), new Vector3(0, 0, 18f), new Vector3(-17, 0, 17f), new Vector3(17, 0, 17f), new Vector3(0, 0, -12.6f) })
            MakePoint("Walk", p, 0f, pts);
        var spawn = new Vector3(0, 0, -18.5f);
        Marker("Arrive_Entry", spawn, 0f, pts);
        Marker("Arrive_Lobby", spawn, 0f, pts);
        BuildPlayer(spawn, 0f);
        var welcome = Data("V5", "L00", cur.lobby.title, "Welcome", cur.lobby.welcome, null);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{CurAudio}/L00.ogg"); if (clip) welcome.narration = clip;
        welcome.stop = 0; EditorUtility.SetDirty(welcome);
        MakeAudioGuide(welcome, new Color(0.86f, 0.62f, 0.28f));
        RealCamera();
        MuseumLighting.Configure(scene);
        BuildPhoneHUD();
        return SaveWithOcclusion(scene, $"{SceneDir}/Lobby.unity");
    }

    static string DoorHint(int i) => i switch
    {
        0 => "West side, left doorway", 1 => "West side, right doorway", 2 => "North side, left doorway",
        3 => "North side, right doorway", 4 => "East side, left doorway", _ => "East side, right doorway"
    };

    static void StoneBench(Transform p, Vector3 pos, float yaw)
    {
        var g = Group(p, "StoneBench"); g.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        GMesh("Seat", "StoneBenchSeat", mb => { Geo.ChamferBox(mb, new Vector3(0, 0.42f, 0), new Vector3(3.2f, 0.12f, 0.6f), 0.02f); Geo.ChamferBox(mb, new Vector3(-1.2f, 0.18f, 0), new Vector3(0.3f, 0.36f, 0.5f), 0.01f); Geo.ChamferBox(mb, new Vector3(1.2f, 0.18f, 0), new Vector3(0.3f, 0.36f, 0.5f), 0.01f); },
              M["V5_Granite"], g, pos, g.rotation);
        var col = g.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0.25f, 0); col.size = new Vector3(3.2f, 0.5f, 0.6f);
    }
}
