using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

/// Second setup pass (runs after MuseumProjectSetup has finished):
///   1. Enables the Cardboard XR loader for Android and iOS (XR Plug-in Management).
///   2. Creates the Android custom gradle templates with the Cardboard dependencies.
///   3. Imports TMP Essential Resources.
///   4. Imports the Hello Cardboard sample (Player rig, CardboardStartup, reticle).
/// Re-run from Museum > Run Cardboard Setup Again.
[InitializeOnLoad]
public static class MuseumCardboardSetup
{
    const string StateFile = "ProjectSettings/MuseumCardboardState.txt";
    const string LoaderType = "Google.XR.Cardboard.XRLoader";
    const string AndroidDir = "Assets/Plugins/Android";

    static readonly string[] GradleDeps =
    {
        "    implementation 'androidx.appcompat:appcompat:1.6.1'",
        "    implementation 'com.google.android.gms:play-services-vision:20.1.3'",
        "    implementation 'com.google.android.material:material:1.12.0'",
        "    implementation 'com.google.protobuf:protobuf-javalite:3.19.4'",
    };

    static MuseumCardboardSetup()
    {
        EditorApplication.delayCall += Run;
    }

    [MenuItem("Museum/Run Cardboard Setup Again")]
    static void RunAgain()
    {
        if (File.Exists(StateFile)) File.Delete(StateFile);
        Run();
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (File.Exists(StateFile)) return;
        var first = "ProjectSettings/MuseumSetupState.txt";
        if (!File.Exists(first) || File.ReadAllText(first).Trim() != "done") return;

        var log = new System.Text.StringBuilder();
        Step(log, "Cardboard XR loader (Android, iOS)", EnableCardboardLoaders);
        Step(log, "Android gradle templates", CreateGradleTemplates);
        Step(log, "TMP Essential Resources", ImportTmpEssentials);
        Step(log, "Hello Cardboard sample", ImportCardboardSample);

        File.WriteAllText(StateFile, "done");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Museum] Cardboard setup:\n" + log);
        EditorUtility.DisplayDialog("Shivaji Museum VR — Cardboard setup", log.ToString(), "OK");
    }

    static void Step(System.Text.StringBuilder log, string name, System.Func<string> action)
    {
        try { log.AppendLine("✓ " + name + ": " + action()); }
        catch (System.Exception e)
        {
            log.AppendLine("✗ " + name + ": " + e.Message);
            Debug.LogException(e);
        }
    }

    static string EnableCardboardLoaders()
    {
        const string path = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, path);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }

        var result = "";
        foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.iOS })
        {
            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            var manager = perTarget.ManagerSettingsForBuildTarget(group);
            bool already = manager.activeLoaders.Any(l => l != null && l.GetType().FullName == LoaderType);
            bool ok = already || XRPackageMetadataStore.AssignLoader(manager, LoaderType, group);
            EditorUtility.SetDirty(manager);
            result += $"{group} {(ok ? "on" : "FAILED")}  ";
        }
        EditorUtility.SetDirty(perTarget);
        return result.Trim();
    }

    static string CreateGradleTemplates()
    {
        var engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
        var templates = Path.Combine(engine ?? "", "Tools", "GradleTemplates");
        if (string.IsNullOrEmpty(engine) || !Directory.Exists(templates))
            throw new System.Exception("Android Build Support module not found — install it in Unity Hub, then use Museum > Run Cardboard Setup Again.");

        Directory.CreateDirectory(AndroidDir);

        var mainPath = Path.Combine(AndroidDir, "mainTemplate.gradle");
        if (!File.Exists(mainPath)) File.Copy(Path.Combine(templates, "mainTemplate.gradle"), mainPath);
        var main = File.ReadAllText(mainPath);
        if (!main.Contains("protobuf-javalite"))
        {
            int i = main.IndexOf("dependencies {", System.StringComparison.Ordinal);
            if (i < 0) throw new System.Exception("No dependencies block in mainTemplate.gradle");
            int eol = main.IndexOf('\n', i);
            main = main.Insert(eol + 1, string.Join("\n", GradleDeps) + "\n");
            File.WriteAllText(mainPath, main);
        }

        var propsPath = Path.Combine(AndroidDir, "gradleTemplate.properties");
        if (!File.Exists(propsPath)) File.Copy(Path.Combine(templates, "gradleTemplate.properties"), propsPath);
        var props = File.ReadAllText(propsPath);
        if (!props.Contains("android.enableJetifier")) props += "\nandroid.enableJetifier=true";
        if (!props.Contains("android.useAndroidX")) props += "\nandroid.useAndroidX=true";
        File.WriteAllText(propsPath, props.TrimEnd() + "\n");

        return "mainTemplate.gradle + gradleTemplate.properties";
    }

    static string ImportTmpEssentials()
    {
        if (Directory.Exists("Assets/TextMesh Pro")) return "already imported";
        var pkg = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        var full = Path.GetFullPath(pkg);
        if (!File.Exists(full)) throw new System.Exception("TMP Essential Resources package not found");
        AssetDatabase.ImportPackage(full, false);
        return "imported";
    }

    static string ImportCardboardSample()
    {
        var sample = Sample.FindByPackage("com.google.xr.cardboard", null).FirstOrDefault();
        if (string.IsNullOrEmpty(sample.displayName)) throw new System.Exception("Hello Cardboard sample not found");
        if (sample.isImported) return "already imported";
        sample.Import(Sample.ImportOptions.OverridePreviousImports);
        return "imported to " + sample.importPath.Replace(Path.GetFullPath(".") + Path.DirectorySeparatorChar, "");
    }
}
