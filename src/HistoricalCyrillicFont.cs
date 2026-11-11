using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace CultCraft
{
    internal static class HistoricalCyrillicFont
    {
        private const int AtlasSize = 256;
        private const int PointSize = 56;
        private const int Padding = 7;
        private const GlyphRenderMode RenderMode = GlyphRenderMode.SDFAA;

        private delegate bool PackGlyph(uint index, int padding, GlyphPackingMode mode, List<GlyphRect> free,
            List<GlyphRect> used, GlyphRenderMode renderMode, Texture2D atlas, out Glyph glyph);

        internal static TMP_FontAsset Create(string letters)
        {
            // TMP's Font overload expects imported font data. Unity's FontEngine can read the system file directly.
            var packMethod = AccessTools.Method(typeof(FontEngine), "TryAddGlyphToTexture");
            Shader shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (packMethod == null || shader == null) return null;
            var pack = (PackGlyph)Delegate.CreateDelegate(typeof(PackGlyph), packMethod);
            FontEngine.InitializeFontEngine();
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (string file in new[] { "segoeui.ttf", "arial.ttf", "times.ttf" })
            {
                string path = Path.Combine(directory, file);
                if (!File.Exists(path) || FontEngine.LoadFontFace(path, PointSize) != FontEngineError.Success) continue;
                if (!letters.All(letter => FontEngine.TryGetGlyphIndex(letter, out _))) continue;
                var atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.Alpha8, false) { name = "CultCraft historical Cyrillic atlas" };
                atlas.LoadRawTextureData(new byte[AtlasSize * AtlasSize]);
                atlas.Apply();
                var free = new List<GlyphRect> { new GlyphRect(0, 0, AtlasSize - 1, AtlasSize - 1) };
                var used = new List<GlyphRect>();
                var glyphs = new Dictionary<char, Glyph>();
                foreach (char letter in letters)
                {
                    FontEngine.TryGetGlyphIndex(letter, out uint index);
                    if (!pack(index, Padding, GlyphPackingMode.BestShortSideFit, free, used, RenderMode, atlas, out Glyph glyph)) break;
                    glyphs.Add(letter, glyph);
                }
                if (glyphs.Count != letters.Length)
                {
                    UnityEngine.Object.Destroy(atlas);
                    continue;
                }

                atlas.Apply();
                var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
                font.name = "CultCraft historical Cyrillic";
                font.faceInfo = FontEngine.GetFaceInfo();
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                font.atlasTextures = new[] { atlas };
                AccessTools.Field(typeof(TMP_FontAsset), "m_Version").SetValue(font, "1.1.0");
                AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasWidth").SetValue(font, AtlasSize);
                AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasHeight").SetValue(font, AtlasSize);
                AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasPadding").SetValue(font, Padding);
                AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasRenderMode").SetValue(font, RenderMode);
                foreach (var pair in glyphs)
                {
                    font.glyphTable.Add(pair.Value);
                    font.characterTable.Add(new TMP_Character(pair.Key, font, pair.Value));
                }

                var material = new Material(shader) { mainTexture = atlas };
                material.SetFloat("_TextureWidth", AtlasSize);
                material.SetFloat("_TextureHeight", AtlasSize);
                material.SetFloat("_GradientScale", Padding + 1);
                material.SetFloat("_WeightNormal", font.normalStyle);
                material.SetFloat("_WeightBold", font.boldStyle);
                font.material = material;
                font.ReadFontAssetDefinition();
                return font;
            }
            return null;
        }
    }
}
