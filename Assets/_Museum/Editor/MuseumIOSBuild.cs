using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Builds the iOS Xcode project into Builds/iOS (Museum > Build iOS Xcode Project).
/// Switching the editor to iOS reloads scripts, so the request is kept in a state file and resumed after reload.
[InitializeOnLoad]
public static class MuseumIOSBuild
{
    const string StateFile = "ProjectSettings/MuseumIOSBuild_State.txt";     // "pending" while a build is requested
    const string ReportFile = "ProjectSettings/MuseumIOSBuild_Report.txt";
    const string OutDir = "Builds/iOS";
    static double waitStart;

    static MuseumIOSBuild() => EditorApplication.delayCall += () =>
    {
        if (!Pending) return;
        waitStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += Wait;
    };

    static bool Pending => File.Exists(StateFile) && File.ReadAllText(StateFile).Trim() == "pending";

    [MenuItem("Museum/Build iOS Xcode Project")]
    static void Menu()
    {
        File.WriteAllText(StateFile, "pending");
        ContinueIfPending();
    }

    static void Wait()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { waitStart = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.timeSinceStartup - waitStart < 3) return;
        EditorApplication.update -= Wait;
        ContinueIfPending();
    }

    public static void ContinueIfPending()
    {
        if (!Pending || EditorApplication.isPlaying) return;
        if (!File.Exists("ProjectSettings/MuseumIHM_State.txt")) return;     // scenes are built first; the builder calls back
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
        {
            Finish("RESULT: NEEDS iOS MODULE\nInstall 'iOS Build Support' for Unity 6000.6.3f1 in Unity Hub (Installs > ⋯ > Add modules), " +
                   "reopen the project, then run Museum > Build iOS Xcode Project.", false);
            return;
        }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
        {
            File.WriteAllText(ReportFile, "RESULT: SWITCHING TO iOS (re-importing textures and audio for iOS, then building)\n");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);   // scripts reload; resumes in the static constructor
            return;
        }
        Build();
    }

    static void Build()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        Directory.CreateDirectory(OutDir);
        var opts = new BuildPlayerOptions
        {
            scenes = scenes, locationPathName = OutDir, target = BuildTarget.iOS, targetGroup = BuildTargetGroup.iOS,
            options = Directory.Exists(Path.Combine(OutDir, "Unity-iPhone.xcodeproj")) ? BuildOptions.AcceptExternalModificationsToPlayer : BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        var sum = report.summary;
        var msg = $"RESULT: {sum.result.ToString().ToUpper()}\nXcode project: {Path.GetFullPath(OutDir)}\n" +
                  $"Time: {sum.totalTime.TotalMinutes:0.0} min  Size: {sum.totalSize / 1048576f:0} MB  Errors: {sum.totalErrors}  Warnings: {sum.totalWarnings}\n" +
                  $"Scenes: {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}\n";
        if (sum.result != BuildResult.Succeeded)
            msg += string.Join("\n", report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(15));
        else
            msg += "\nNext: open Builds/iOS/Unity-iPhone.xcodeproj in Xcode, pick your team under Signing & Capabilities, connect the iPhone and press Run.";
        Finish(msg, sum.result == BuildResult.Succeeded);
    }

    static void Finish(string msg, bool ok)
    {
        File.WriteAllText(ReportFile, msg);
        File.WriteAllText(StateFile, ok ? "done" : "failed");
        Debug.Log("[Museum] iOS build\n" + msg);
        EditorUtility.DisplayDialog("Museum of Indian History — iOS build", msg, "OK");
        if (ok) EditorUtility.RevealInFinder(Path.Combine(OutDir, "Unity-iPhone.xcodeproj"));
    }
}
