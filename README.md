# Bhartiya Sanghralay VR — Museum of Indian History

A phone VR museum of Indian history for Google Cardboard (Android and iPhone). You walk through a Great Court
and six galleries, from the first stone tools to the Republic, and look at 41 objects. Each object has a photograph, a
label and a spoken audio guide. You can also play without a headset in phone mode.

## Download

| Platform | File | How to install |
|---|---|---|
| Android 8.0+ (ARM64) | [BhartiyaSanghralayVR.apk](https://github.com/gauriiiiiiiiii/bhartiya-sanghralay-vr/releases/latest/download/BhartiyaSanghralayVR.apk) | Open the file on the phone and allow *Install unknown apps* when asked. |
| iPhone (iOS 15+) | [BhartiyaSanghralayVR.ipa](https://github.com/gauriiiiiiiiii/bhartiya-sanghralay-vr/releases/latest/download/BhartiyaSanghralayVR.ipa) | See the note below. |

> **About the iPhone build:** Apple doesn't allow sideloading, so an `.ipa` only installs on iPhones listed in
> its signing profile. This build is signed with a free developer account: it installs on one registered test
> phone, and the signature expires seven days after the build date. To run the museum on your own iPhone, build
> it from source with your Apple ID (see [Build it yourself](#build-it-yourself)) or re-sign the `.ipa` with your
> own profile. Public iPhone release needs a paid Apple Developer account and TestFlight or the App Store.

All builds are on the [Releases page](https://github.com/gauriiiiiiiiii/bhartiya-sanghralay-vr/releases).

## What's inside

| Gallery | Period | Some of the objects |
|---|---|---|
| Great Court | — | Welcome desk and doorways to every gallery |
| 1 · Ancient India | c. 1.5 million years ago – 321 BCE | Bhimbetka rock paintings, the 'Priest-King', the Great Bath |
| 2 · Classical India | 321 BCE – 750 CE | Lion Capital of Ashoka, Sanchi stupa, Ajanta's Padmapani |
| 3 · Medieval India | 750 – 1707 | Brihadisvara Temple, Qutb Minar, Hampi's stone chariot, the Taj Mahal |
| 4 · Colonial India | 1498 – 1885 | Vasco da Gama, Tipu's Tiger, Rani Lakshmibai, Victoria Terminus |
| 5 · Freedom and the Republic | 1885 – 1950 | The Salt March, Bhagat Singh, Partition, the Constitution |
| 6 · Themes of Indian History | Antiquity to today | Nalanda, Jantar Mantar, Madhubani painting, the decipherment of Brahmi |

## How to play

On the start screen, choose **Cardboard** or **Phone**, then tap a gallery to enter it.

- **Cardboard:** put the phone in the viewer. Look at a floor circle or an exhibit and press the viewer button,
  or keep looking for two seconds, to walk there or hear its audio guide.
- **Phone:** drag to look around, tap a circle on the floor to walk, and tap an exhibit to hear its audio guide.
  Tap **Menu** to return to the start screen.

## Build it yourself

Requirements: Unity **6000.6.3f1** with Android Build Support and iOS Build Support, and Xcode for iOS.

1. Open the folder in Unity Hub. On first open, `MuseumProjectSetup` installs the packages and sets the player
   settings. [SETUP.md](SETUP.md) lists the few manual steps.
2. Run **Museum → Rebuild All Scenes**. This generates the galleries from
   `Assets/_Museum/Content/curated.json` and bakes the lighting.
3. Build from the command line. Close the Unity editor first.

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Android APK  ->  Builds/Android/BhartiyaSanghralayVR.apk
$UNITY -batchmode -quit -projectPath . -buildTarget Android -executeMethod MuseumCLIBuild.Android

# iOS Xcode project  ->  Builds/iOS
MUSEUM_APPLE_TEAM=<your team id> $UNITY -batchmode -quit -projectPath . -buildTarget iOS -executeMethod MuseumCLIBuild.IOS
xcodebuild -project Builds/iOS/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -archivePath Builds/BhartiyaSanghralayVR.xcarchive -allowProvisioningUpdates archive
xcodebuild -exportArchive -archivePath Builds/BhartiyaSanghralayVR.xcarchive \
  -exportOptionsPlist Tools/ExportOptions.plist -exportPath Builds/ipa -allowProvisioningUpdates
```

You can also use **Museum → Build iOS Xcode Project** inside the editor.

## Project layout

| Path | Contents |
|---|---|
| `Assets/_Museum/Content/curated.json` | The galleries, objects, labels and narration text |
| `Assets/_Museum/Editor/` | Scene generator, lighting bake, validation and build scripts |
| `Assets/_Museum/Scripts/` | Runtime: gaze input, teleport, info panels, audio guide, menus |
| `Assets/_Museum/Photos/`, `Audio/` | Exhibit photographs and narration clips |
| `Tools/` | Python scripts that fetch photos and generate the narration (Kokoro TTS) |
| `Docs/` | Photo credits, sourcing sheet and narration script |

## Credits

The exhibit photographs come from Wikimedia Commons under Creative Commons and public-domain licences. Each one
is credited with its author and licence in [Docs/photo_credits.csv](Docs/photo_credits.csv) and
[Docs/curated_credits.csv](Docs/curated_credits.csv). Textures and some models are from [Poly Haven](https://polyhaven.com) (CC0).
The narration is synthesised with Kokoro TTS. Cardboard support uses the
[Google Cardboard XR Plugin](https://github.com/googlevr/cardboard-xr-plugin).

## License

The code is released under the [Apache License 2.0](LICENSE). The photographs keep their own licences, which are
listed in the credits files.
