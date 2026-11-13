using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Command-line builds for CI or a terminal. Pass -buildTarget so Unity starts on the right platform:
///   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod MuseumCLIBuild.Android   -> Builds/Android/BhartiyaSanghralayVR.apk
///   Unity -batchmode -quit -projectPath . -buildTarget iOS     -executeMethod MuseumCLIBuild.IOS       -> Builds/iOS (Xcode project)
/// The scenes must already be generated and baked (Museum > Rebuild All Scenes); this only packages them.
public static class MuseumCLIBuild
{
    public const string ApkPath = "Builds/Android/BhartiyaSanghralayVR.apk";
    public const string IOSDir = "Builds/iOS";

    public static void Android()
    {
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        Run(BuildTarget.Android, BuildTargetGroup.Android, ApkPath, BuildOptions.None);
    }

    public static void IOS()
    {
        Directory.CreateDirectory(IOSDir);
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        var team = Environment.GetEnvironmentVariable("MUSEUM_APPLE_TEAM");
        if (!string.IsNullOrEmpty(team)) PlayerSettings.iOS.appleDeveloperTeamID = team;
        Run(BuildTarget.iOS, BuildTargetGroup.iOS, IOSDir, BuildOptions.None);
    }

    static void Run(BuildTarget target, BuildTargetGroup group, string path, BuildOptions options)
    {
        if (EditorUserBuildSettings.activeBuildTarget != target)
            EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes, locationPathName = path, target = target, targetGroup = group, options = options,
        });
        var sum = report.summary;
        Debug.Log($"[MuseumCLIBuild] {target}: {sum.result}  {sum.totalSize / 1048576f:0} MB  {sum.totalTime.TotalMinutes:0.0} min  " +
                  $"errors {sum.totalErrors}  scenes {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}");
        if (sum.result != BuildResult.Succeeded)
        {
            foreach (var m in report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Take(20))
                Debug.LogError("[MuseumCLIBuild] " + m.content);
            EditorApplication.Exit(1);
        }
    }
}
