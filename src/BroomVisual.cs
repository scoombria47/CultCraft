using System;
using UnityEngine;

namespace CultCraft
{
    internal sealed class BroomVisual
    {
        private static readonly BroomVisual[] Levels =
        {
            new BroomVisual("broom_bamboo", 16, 5, 16, 28, 32, "broom_bamboo_source"),
            new BroomVisual("broom_sticks", 16, 5, 16, 28, 32, "broom_stick_source"),
            new BroomVisual("broom_dead_bush", 8.5f, 1.5f, 8, 13),
            new BroomVisual("broom_birch", 8.5f, 1.5f, 8.5f, 13),
            new BroomVisual("broom_oak", 8.5f, 1.5f, 8, 12),
            new BroomVisual("broom_spruce", 8, 1.5f, 8, 13),
            new BroomVisual("broom_dark_oak", 8.5f, 2, 8.5f, 12.5f),
            new BroomVisual("broom_sugar_cane", 5.5f, 2.5f, 11, 12.5f),
            new BroomVisual("broom_wheat", 3, 2.5f, 11, 13),
            new BroomVisual("broom_brush", 3.5f, 2.5f, 12, 12.5f)
        };

        public readonly string Id;
        public readonly Vector2 Grip;
        public readonly float BristleReach;
        public readonly string BundleSource;
        private readonly Quaternion alignBristles;

        private BroomVisual(string id, float gripX, float gripY, float headX, float headY,
            int pixels = 16, string bundleSource = null)
        {
            Id = id;
            Grip = new Vector2(gripX, gripY) / pixels;
            Vector2 bristles = new Vector2(headX - gripX, headY - gripY) / pixels;
            BristleReach = bristles.magnitude;
            alignBristles = Quaternion.AngleAxis(Vector2.SignedAngle(bristles, Vector2.up), Vector3.forward);
            BundleSource = bundleSource;
        }

        public static BroomVisual ForLevel(int zeroBased) { return Levels[Mathf.Clamp(zeroBased, 0, Levels.Length - 1)]; }

        public static BroomVisual Find(string id)
        {
            foreach (BroomVisual level in Levels)
                if (level.Id == id) return level;
            return null;
        }

        public Quaternion Orient(Vector3 towardsBristles, Vector3 faceNormal)
        {
            Vector3 normal = Vector3.ProjectOnPlane(faceNormal, towardsBristles);
            return Quaternion.LookRotation(normal, towardsBristles) * alignBristles;
        }

        // Sample the installed item pixels into five separate stems, then add a
        // binding woven from the installed string texture. The result stays in RAM.
        public Texture2D Bundle(Texture2D stem, Texture2D binding)
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            bool bamboo = BundleSource == "broom_bamboo_source";
            for (int strand = -2; strand <= 2; strand++)
            {
                int end = 29 - Math.Abs(strand);
                for (int y = 1; y <= end; y++)
                {
                    float splay = Mathf.InverseLerp(10, end, y);
                    int x = 15 + Mathf.RoundToInt(strand * (1.2f + 3.1f * splay));
                    int sampleY = 1 + Mathf.RoundToInt(12f * y / end);
                    int sampleX = bamboo ? Mathf.Max(1, sampleY - 1) : Mathf.Max(2, sampleY);
                    pixels[y * size + x] = stem.GetPixel(sampleX, sampleY);
                    pixels[y * size + x + 1] = stem.GetPixel(sampleX + 1, sampleY);
                }
            }
            for (int y = 8; y <= 10; y++)
                for (int x = 12; x <= 19; x++)
                    pixels[y * size + x] = binding.GetPixel(10 + (x + y) % 2, 13);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "CultCraft local Minecraft: " + Id,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
