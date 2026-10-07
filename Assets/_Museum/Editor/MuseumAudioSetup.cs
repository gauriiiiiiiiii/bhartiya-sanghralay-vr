using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Assigns narration clips to every ArtifactData from Assets/_Museum/Audio:
///   <id>.mp3 | .ogg | .wav   (also accepted: <id>_en.*), e.g. S01_03.mp3, S01_00.mp3, W1_00.mp3, L00.mp3
/// If several files exist for one id, the most recently modified wins.
/// Add or replace files, then run Museum > Assign Narration Audio (the scene builder also runs it).
public static class MuseumAudioSetup
{
    const string Dir = "Assets/_Museum/Audio";
    static readonly string[] Exts = { ".ogg", ".mp3", ".wav" };

    static AudioClip Newest(string id)
    {
        var best = new[] { id, id + "_en" }
            .SelectMany(b => Exts.Select(e => $"{Dir}/{b}{e}"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return best == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(best);
    }

    [MenuItem("Museum/Assign Narration Audio")]
    static void Menu()
    {
        var msg = Assign();
        EditorUtility.DisplayDialog("Museum of Indian History — narration", msg, "OK");
    }

    public static string Assign()
    {
        Directory.CreateDirectory(Dir);
        foreach (var file in Directory.GetFiles(Dir).Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            var ai = AssetImporter.GetAtPath(file.Replace('\\', '/')) as AudioImporter;
            if (ai == null) continue;
            var s = ai.defaultSampleSettings;
            if (ai.forceToMono && s.loadType == AudioClipLoadType.CompressedInMemory && !s.preloadAudioData && ai.loadInBackground) continue;
            s.loadType = AudioClipLoadType.CompressedInMemory;   // narration: small, starts instantly
            s.preloadAudioData = false;                          // phones: load a clip only when it is played (fast scene loads)
            ai.loadInBackground = true;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = true;
            ai.SaveAndReimport();
        }
        int found = 0, total = 0;
        var missing = new System.Collections.Generic.List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:ArtifactData", new[] { "Assets/_Museum/Data" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<ArtifactData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            total++;
            var clip = Newest(data.id);
            if (!clip && data.id.EndsWith("C")) clip = Newest(data.id.Substring(0, data.id.Length - 1));   // centrepiece: section narration
            if (data.narration != clip) { data.narration = clip; EditorUtility.SetDirty(data); }
            if (clip) found++; else missing.Add(data.id);
        }
        AssetDatabase.SaveAssets();
        var msg = $"Narration assigned: {found}/{total}";
        if (missing.Count > 0) msg += $"\nNo audio yet for {missing.Count} items, e.g. {string.Join(", ", missing.OrderBy(x => x).Take(5))}";
        File.WriteAllText("ProjectSettings/MuseumAudioReport.txt", msg);
        Debug.Log("[Museum] " + msg);
        return msg;
    }
}
