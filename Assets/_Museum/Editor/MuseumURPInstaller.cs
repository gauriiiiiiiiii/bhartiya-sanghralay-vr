using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// URP migration, step 1 of 2. Installs the Universal Render Pipeline package, then copies the
/// converter script (which needs URP types to compile) out of Editor~ so it runs after the reload.
[InitializeOnLoad]
public static class MuseumURPInstaller
{
    const string StateFile = "ProjectSettings/MuseumURPState.txt";
    const string ConverterSrc = "Assets/_Museum/Editor~/MuseumURPConverter.cs";
    const string ConverterDst = "Assets/_Museum/Editor/MuseumURPConverter.cs";
    static AddRequest request;

    static MuseumURPInstaller() => EditorApplication.delayCall += Run;

    static string State => File.Exists(StateFile) ? File.ReadAllText(StateFile).Trim() : "";

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (State != "") return;
        if (!File.ReadAllText("ProjectSettings/MuseumCardboardState.txt").Contains("done")) return;

        Debug.Log("[Museum URP] Installing com.unity.render-pipelines.universal ...");
        File.WriteAllText(StateFile, "installing");
        request = Client.Add("com.unity.render-pipelines.universal");
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!request.IsCompleted) return;
        EditorApplication.update -= Poll;
        if (request.Status != StatusCode.Success)
        {
            File.Delete(StateFile);
            Debug.LogError("[Museum URP] Install failed: " + request.Error?.message);
            return;
        }
        Debug.Log("[Museum URP] Installed " + request.Result.packageId);
        File.Copy(ConverterSrc, ConverterDst, true);
        File.WriteAllText(StateFile, "installed");
        AssetDatabase.Refresh();
    }
}
