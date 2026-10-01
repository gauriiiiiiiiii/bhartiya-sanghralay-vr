using UnityEngine;

/// One asset per artifact (A01..A16). Create via Assets > Create > Museum > Artifact Data.
[CreateAssetMenu(menuName = "Museum/Artifact Data", fileName = "A00_ArtifactData")]
public class ArtifactData : ScriptableObject
{
    [Header("Identity")]
    public string id = "A00";
    public string era = "c. 1630–1680";

    [Header("English")]
    public string titleEn;
    [TextArea(3, 8)] public string panelEn;
    public AudioClip narrationEn;

    [Header("Marathi")]
    public string titleMr;
    [TextArea(3, 8)] public string panelMr;
    public AudioClip narrationMr;
    [Tooltip("Optional: Marathi title + panel text pre-rendered as an image. " +
             "Use this if TextMeshPro does not shape Devanagari conjuncts correctly.")]
    public Sprite panelMrRendered;

    [Header("Display")]
    public Sprite image;
    [Tooltip("Tick for coins, seals and other small items scaled up for VR.")]
    public bool shownEnlarged;
}

public enum Lang { En, Mr }

public static class MuseumSettings
{
    public static Lang Language = Lang.En;
    public static event System.Action LanguageChanged;

    public static void ToggleLanguage()
    {
        Language = Language == Lang.En ? Lang.Mr : Lang.En;
        LanguageChanged?.Invoke();
    }
}
