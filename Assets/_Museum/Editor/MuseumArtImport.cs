using UnityEditor;

/// Import settings for the generated museum art (applied on first import, so nothing is imported twice).
///   Art/Illus/*  -> sprites for the info panel
///   Art/Boards, Sections, Wings, Lobby -> wall textures (mipmaps, no power-of-two rescale to keep text sharp)
///   Art/tex_*    -> tiling textures
public class MuseumArtImport : AssetPostprocessor
{
    public const string Root = "Assets/_Museum/Art/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root)) return;
        Apply((TextureImporter)assetImporter, assetPath);
    }

    /// Returns true if anything changed.
    public static bool Apply(TextureImporter ti, string path)
    {
        string rel = path.Substring(Root.Length);
        string file = System.IO.Path.GetFileName(path);
        bool sprite = rel.StartsWith("Illus/");
        bool tile = file.StartsWith("tex_") && file != "tex_rangoli.png";
        bool big = rel.StartsWith("Lobby/");

        var type = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        var wrap = tile ? UnityEngine.TextureWrapMode.Repeat : UnityEngine.TextureWrapMode.Clamp;
        int max = big ? 2048 : 1024;
        // Phones: ASTC (iOS and Android). 5x5 keeps board text crisp; 6x6 for pictures and patterns.
        bool textHeavy = rel.StartsWith("Boards/") || rel.StartsWith("Sections/") || rel.StartsWith("Lobby/") || rel.StartsWith("Wings/");
        var fmt = textHeavy ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
        bool platformChanged = false;
        foreach (var platform in new[] { "iPhone", "Android" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            if (ps.overridden && ps.format == fmt && ps.maxTextureSize == max) continue;
            ps.overridden = true; ps.format = fmt; ps.maxTextureSize = max;
            ps.compressionQuality = (int)TextureCompressionQuality.Normal;
            ti.SetPlatformTextureSettings(ps);
            platformChanged = true;
        }
        bool changed = ti.textureType != type || ti.wrapMode != wrap || ti.maxTextureSize != max ||
                       ti.npotScale != TextureImporterNPOTScale.None || ti.mipmapEnabled == sprite;
        if (!changed) return platformChanged;
        ti.textureType = type;
        if (sprite) ti.spriteImportMode = SpriteImportMode.Single;
        ti.wrapMode = wrap;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = !sprite;              // mipmaps stop shimmering on distant walls in VR
        ti.alphaIsTransparency = sprite || file == "tex_rangoli.png";
        ti.maxTextureSize = max;
        ti.anisoLevel = 2;
        return true;
    }
}
