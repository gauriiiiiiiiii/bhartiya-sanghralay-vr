using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Assigns narration clips to every ArtifactData: Assets/_Museum/Audio/<id>_en.(ogg|mp3|wav) and <id>_mr.(ogg|mp3|wav).
/// If several formats exist for one clip, the most recently modified file wins (so new TTS files replace the placeholders).
/// Add or replace files, then run Museum > Assign Narration Audio.
[InitializeOnLoad]
public static class MuseumAudioSetup
{
    const string StateFile = "ProjectSettings/MuseumAudioState.txt";
    const string Dir = "Assets/_Museum/Audio";
    static readonly string[] Exts = { ".ogg", ".mp3", ".wav" };

    static AudioClip Newest(string baseName)
    {
        var best = Exts.Select(e => $"{Dir}/{baseName}{e}").Where(File.Exists)
                       .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return best == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(best);
    }

    static MuseumAudioSetup() => EditorApplication.delayCall += () => { if (!File.Exists(StateFile)) Run(); };

    [MenuItem("Museum/Assign Narration Audio")]
    static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !Directory.Exists(Dir)) return;
        foreach (var file in Directory.GetFiles(Dir).Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            var ai = AssetImporter.GetAtPath(file.Replace('\\', '/')) as AudioImporter;
            if (ai == null) continue;
            var s = ai.defaultSampleSettings;
            if (ai.forceToMono && s.loadType == AudioClipLoadType.CompressedInMemory) continue;
            s.loadType = AudioClipLoadType.CompressedInMemory;   // narration: small, starts instantly
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = true;
            ai.SaveAndReimport();
        }
        int en = 0, mr = 0, total = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:ArtifactData", new[] { "Assets/_Museum/Data" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<ArtifactData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            total++;
            var e = Newest($"{data.id}_en");
            var m = Newest($"{data.id}_mr");
            if (e) { data.narrationEn = e; en++; }
            if (m) { data.narrationMr = m; mr++; }
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(StateFile, "done");
        var msg = $"Narration assigned.\nEnglish: {en}/{total}  Marathi: {mr}/{total}";
        Debug.Log("[Museum] " + msg);
        EditorUtility.DisplayDialog("Shivaji Museum VR — narration", msg, "OK");
    }
}
