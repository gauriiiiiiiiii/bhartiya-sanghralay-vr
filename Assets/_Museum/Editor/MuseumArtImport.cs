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
        if (assetPath.StartsWith(Root)) Apply((TextureImporter)assetImporter, assetPath);
        else if (assetPath.StartsWith(PhotoRoot)) ApplyPhoto((TextureImporter)assetImporter);
        else if (assetPath.StartsWith(PolyRoot)) ApplyPoly((TextureImporter)assetImporter, assetPath);
    }

    public const string PhotoRoot = "Assets/_Museum/Photos/";
    public const string PolyRoot = "Assets/_Museum/PolyHaven/Textures/";

    /// Exhibit photos: sprites (info panel, kiosk) that are also used on 3D boards, so keep mipmaps.
    public static bool ApplyPhoto(TextureImporter ti)
    {
        bool changed = ti.textureType != TextureImporterType.Sprite || !ti.mipmapEnabled || ti.maxTextureSize != 1024;
        if (!changed) return false;
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = true; ti.maxTextureSize = 1024; ti.npotScale = TextureImporterNPOTScale.None; ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        foreach (var platform in new[] { "iPhone", "Android" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true; ps.format = TextureImporterFormat.ASTC_6x6; ps.maxTextureSize = 1024; ti.SetPlatformTextureSettings(ps);
        }
        return true;
    }

    /// Poly Haven PBR textures: tiling, normal maps flagged, 2K on phones (ASTC).
    public static bool ApplyPoly(TextureImporter ti, string path)
    {
        bool normal = path.EndsWith("_nor.jpg");
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        bool changed = ti.textureType != type || ti.wrapMode != UnityEngine.TextureWrapMode.Repeat || ti.anisoLevel != 4;
        if (!changed) return false;
        ti.textureType = type; ti.wrapMode = UnityEngine.TextureWrapMode.Repeat; ti.anisoLevel = 4; ti.mipmapEnabled = true; ti.maxTextureSize = 2048;
        foreach (var platform in new[] { "iPhone", "Android" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true; ps.format = normal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6; ps.maxTextureSize = 2048; ti.SetPlatformTextureSettings(ps);
        }
        return true;
    }

    /// Returns true if anything changed.
    public static bool Apply(TextureImporter ti, string path)
    {
        string rel = path.Substring(Root.Length);
        string file = System.IO.Path.GetFileName(path);
        bool sprite = rel.StartsWith("Illus/") || file == "india_map.png";
        bool tile = (file.StartsWith("tex_") && file != "tex_rangoli.png") || (rel.StartsWith("UI/") && !sprite);
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
        ti.alphaIsTransparency = sprite || file == "tex_rangoli.png" || rel.StartsWith("UI/");
        ti.maxTextureSize = max;
        ti.anisoLevel = 2;
        return true;
    }
}
