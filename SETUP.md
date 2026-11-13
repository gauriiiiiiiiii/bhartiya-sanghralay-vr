# Bhartiya Sanghralay VR (Museum of Indian History VR) — setup

A Google Cardboard / phone VR museum of Indian history: a Great Court and six galleries, 47 objects with an audio
guide. Unity 6000.6.3f1, iOS + Android. The museum is generated from `Assets/_Museum/Content/curated.json` by
**Museum → Rebuild All Scenes**; lighting is baked automatically afterwards.

## First open (automatic)

Open this folder from Unity Hub (Add → Add project from disk → `~/projects/bhartiya-sanghralay-vr`).
On first open, `Assets/_Museum/Editor/MuseumProjectSetup.cs` runs by itself and:

1. Installs packages: Google Cardboard XR Plugin (git), XR Plugin Management, Input System,
   uGUI/TextMeshPro, ProBuilder, glTFast. (Needs `git` on your Mac: `xcode-select --install`.)
2. Copies the 8 museum scripts from `Assets/_Museum/Scripts~` into `Assets/_Museum/Scripts`.
3. Sets player settings: Landscape Left, IL2CPP, ARM64, min API 26, target API 35,
   OpenGLES3, Internet Access Require, Activity entry point, frame pacing off, iOS camera text,
   iOS deferred edge gestures, bundle id `com.jaykumarpatel.indianhistorymuseumvr`,
   Active Input Handling = Input System (New).
4. Adds layers `Interactive` and `Teleport`.

If Unity asks to restart for the new Input System, click Yes. If anything fails,
use **Museum → Run Project Setup Again**.

## Manual steps (a few clicks)

- [ ] **Edit → Project Settings → XR Plug-in Management**: tick **Cardboard XR Plugin** on the Android tab and the iOS tab.
- [ ] **Player → Android → Publishing Settings**: tick *Custom Main Gradle Template* and
      *Custom Gradle Properties Template*, then add:
  - `Assets/Plugins/Android/mainTemplate.gradle`, in `dependencies { }`:
    ```
    implementation 'androidx.appcompat:appcompat:1.6.1'
    implementation 'com.google.android.gms:play-services-vision:20.1.3'
    implementation 'com.google.android.material:material:1.12.0'
    implementation 'com.google.protobuf:protobuf-javalite:3.19.4'
    ```
  - `Assets/Plugins/Android/gradleTemplate.properties`:
    ```
    android.enableJetifier=true
    android.useAndroidX=true
    ```
- [ ] **Window → TextMeshPro → Import TMP Essential Resources**.
- [ ] **Package Manager → Google Cardboard XR Plugin → Samples → Import** Hello Cardboard
      (reuse its Player rig and CardboardStartup).
- [ ] **File → Build Profiles**: switch to Android.

Source: https://developers.google.com/cardboard/develop/unity/quickstart
