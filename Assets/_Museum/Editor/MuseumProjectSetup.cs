using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;

/// One-time project setup for Shivaji Museum VR. Runs automatically the first time the project opens:
///   1. Installs packages through the Package Manager Client API (Cardboard XR Plugin, Input System, ...).
///   2. Moves the museum runtime scripts from Scripts~ (ignored by Unity) into Scripts, so they only
///      compile once their packages exist.
///   3. Applies the Cardboard player settings for Android and iOS and adds the Interactive / Teleport layers.
/// Progress is saved in ProjectSettings/MuseumSetupState.txt. Re-run from the menu: Museum > Run Project Setup Again.
[InitializeOnLoad]
public static class MuseumProjectSetup
{
    const string StateFile = "ProjectSettings/MuseumSetupState.txt";
    const string BundleId = "com.jaykumarpatel.shivajimuseumvr";

    static readonly string[] PackagesToAdd =
    {
        "https://github.com/googlevr/cardboard-xr-plugin.git",
        "com.unity.xr.management",
        "com.unity.inputsystem",
        "com.unity.ugui",              // uGUI + TextMeshPro
        "com.unity.probuilder",        // wall block-out
    };

    static AddAndRemoveRequest request;

    static MuseumProjectSetup()
    {
        EditorApplication.delayCall += Run;
    }

    static string State => File.Exists(StateFile) ? File.ReadAllText(StateFile).Trim() : "";
    static void SetState(string s) => File.WriteAllText(StateFile, s);

    [MenuItem("Museum/Run Project Setup Again")]
    static void RunAgain()
    {
        SetState("");
        Run();
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        switch (State)
        {
            case "": InstallPackages(); break;
            case "packages": ConfigureProject(); break;
        }
    }

    static void InstallPackages()
    {
        if (request != null && !request.IsCompleted) return;
        Debug.Log("[Museum] Installing packages: " + string.Join(", ", PackagesToAdd));
        request = Client.AddAndRemove(PackagesToAdd, null);
        EditorApplication.update += PollInstall;
    }

    static void PollInstall()
    {
        if (!request.IsCompleted) return;
        EditorApplication.update -= PollInstall;

        if (request.Status == StatusCode.Success)
        {
            Debug.Log("[Museum] Packages installed.");
            SetState("packages");
            EditorApplication.delayCall += ConfigureProject;
        }
        else
        {
            Debug.LogError("[Museum] Package install failed: " + request.Error?.message +
                           "\nCheck that git is installed, then use Museum > Run Project Setup Again.");
        }
    }

    static void ConfigureProject()
    {
        if (State != "packages") return;
        try
        {
            CopyRuntimeScripts();
            ConfigurePlayerSettings();
            AddLayers("Interactive", "Teleport");
            SetState("done");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Museum] Setup complete. See SETUP.md for the few remaining manual steps.");
            EditorUtility.DisplayDialog("Shivaji Museum VR",
                "Packages, scripts, player settings and layers are set up.\n\n" +
                "Finish the manual steps in SETUP.md (project root):\n" +
                "• XR Plug-in Management → tick Cardboard XR Plugin (Android + iOS)\n" +
                "• Publishing Settings → gradle templates (Android)\n" +
                "• Restart Unity if asked, so the new Input System is active.", "OK");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    static void CopyRuntimeScripts()
    {
        const string src = "Assets/_Museum/Scripts~";
        const string dst = "Assets/_Museum/Scripts";
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*.cs"))
        {
            var target = Path.Combine(dst, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }

    static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = "Jaykumar Patel";
        PlayerSettings.productName = "Shivaji Museum VR";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

        // Android (Google Cardboard quickstart values)
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
        PlayerSettings.Android.optimizedFramePacing = false;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

        // iOS
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
        PlayerSettings.iOS.cameraUsageDescription =
            "Cardboard SDK requires camera permission to read the QR code (required to get the encoded device parameters).";
        // Defer system gestures on Top (1) + Left (2) + Right (8) edges. Set through the serialized
        // property so this compiles even when the iOS module is not the active target.
        SetProjectSettingInt("deferSystemGesturesMode", 1 | 2 | 8);

        // Active Input Handling = Input System Package (New). Takes effect after an Editor restart.
        SetProjectSettingInt("activeInputHandler", 1);
    }

    static void SetProjectSettingInt(string property, int value)
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (asset == null) return;
        var so = new SerializedObject(asset);
        var p = so.FindProperty(property);
        if (p == null) return;
        p.intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AddLayers(params string[] names)
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (asset == null) return;
        var so = new SerializedObject(asset);
        var layers = so.FindProperty("layers");

        foreach (var name in names)
        {
            bool exists = false;
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) exists = true;
            if (exists) continue;

            for (int i = 8; i < layers.arraySize; i++)   // 0-7 are Unity's built-in layers
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = name; break; }
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
