using UnityEngine;

/// Keeps narration at its natural speed and quality.
/// If the app (or the Editor) starts while Bluetooth headphones are in hands-free mode, the audio output opens at
/// 16 kHz; when the headphones switch back to 44.1/48 kHz without the output being reopened, everything plays about
/// three times too fast. This opens the mixer at 48 kHz and reopens it whenever the output device changes, then
/// resumes the audio guide where it was.
public static class AudioOutputGuard
{
    const int Rate = 48000;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnChanged;
        AudioSettings.OnAudioConfigurationChanged += OnChanged;
        var cfg = AudioSettings.GetConfiguration();
        if (cfg.sampleRate != Rate) Reopen(cfg);
    }

    static void OnChanged(bool deviceWasChanged)
    {
        if (deviceWasChanged) Reopen(AudioSettings.GetConfiguration());
    }

    static void Reopen(AudioConfiguration cfg)
    {
        var guide = AudioGuide.Instance;
        var src = guide ? guide.source : null;
        bool wasPlaying = src && src.isPlaying; float at = src ? src.time : 0f;
        cfg.sampleRate = Rate;
        if (!AudioSettings.Reset(cfg)) return;          // reopening stops every AudioSource
        if (wasPlaying && src && src.clip) { src.time = Mathf.Min(at, src.clip.length - 0.05f); src.Play(); }
        Debug.Log($"[Museum] Audio output reopened at {AudioSettings.outputSampleRate} Hz");
    }
}
