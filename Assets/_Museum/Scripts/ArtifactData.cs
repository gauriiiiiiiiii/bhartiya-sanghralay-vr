using UnityEngine;

/// One asset per exhibit, section introduction, wing introduction and the lobby welcome.
/// Generated from Assets/_Museum/Content/museum.json by Museum > Rebuild All Scenes (text is overwritten
/// from the JSON on every rebuild; the narration clip is kept).
[CreateAssetMenu(menuName = "Museum/Artifact Data", fileName = "S00_00")]
public class ArtifactData : ScriptableObject
{
    [Tooltip("S01_03 = section 1, exhibit 3; S01_00 = section 1 introduction; W1_00 = wing 1 introduction; L00 = lobby welcome")]
    public string id = "S00_00";
    [Tooltip("Shown under the title, e.g. 'Section 1 · Prehistoric India · c. 1.5 million years ago – 700 BCE'")]
    public string era;
    public string title;
    [TextArea(3, 10)] public string text;
    [Tooltip("Assigned automatically from Assets/_Museum/Audio/<id>.mp3|ogg|wav (Museum > Assign Narration Audio)")]
    public AudioClip narration;
    public Sprite image;
    [Tooltip("Tick for coins, seals and other small items scaled up for VR.")]
    public bool shownEnlarged;
}
