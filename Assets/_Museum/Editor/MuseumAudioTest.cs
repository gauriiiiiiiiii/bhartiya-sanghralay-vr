using UnityEditor;
using UnityEngine;

/// Museum > Test Audio Speed (Play mode): plays the scene's audio-guide intro and, after ~8 s of real time, compares
/// how far the clip has advanced with the wall-clock time. A ratio of 1.00 means normal speed. Writes Tools/audio_test.txt.
public static class MuseumAudioTest
{
    static double t0; static AudioGuide ag;

    [MenuItem("Museum/Test Audio Speed")]
    static void Run()
    {
        ag = Object.FindAnyObjectByType<AudioGuide>();
        if (!Application.isPlaying || !ag || !ag.intro || !ag.intro.narration) { System.IO.File.WriteAllText("Tools/audio_test.txt", "needs Play mode and an AudioGuide with an intro clip"); return; }
        ag.Play(ag.intro, null);
        t0 = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - t0 < 8.0) return;
        EditorApplication.update -= Tick;
        double real = EditorApplication.timeSinceStartup - t0;
        var s = ag ? ag.source : null;
        var c = s ? s.clip : null;
        var cfg = AudioSettings.GetConfiguration();
        string r = c ? $"clip={c.name} freq={c.frequency} channels={c.channels} length={c.length:0.00}s loadType={c.loadType}\n" +
                       $"source.time={s.time:0.00}s timeSamples/freq={s.timeSamples / (double)c.frequency:0.00}s real={real:0.00}s ratio={s.time / real:0.00} pitch={s.pitch}\n" +
                       $"output sampleRate={AudioSettings.outputSampleRate} cfg.sampleRate={cfg.sampleRate} speaker={cfg.speakerMode} dsp={cfg.dspBufferSize} timeScale={Time.timeScale}"
                     : "no clip";
        System.IO.File.WriteAllText("Tools/audio_test.txt", r);
        Debug.Log("[Museum] " + r);
    }
}
