using System;
using UnityEngine;

namespace CultCraft
{
    internal static class ArrowEntityVisual
    {
        // Minecraft 1.21 ArrowRenderer uses a 16x5 side patch and a 5x5 tail
        // patch on its 32x32 entity atlas. Local +X points towards the tip.
        public static Mesh Build(Texture2D atlas)
        {
            if (atlas.width != 32 || atlas.height != 32)
                throw new ArgumentException("Expected the Minecraft arrow entity atlas.", nameof(atlas));
            var mesh = new MinecraftAvatar.MeshBuilder();
            var texels = atlas.GetPixels32();
            for (int i = 0; i < 2; i++)
            {
                Quaternion roll = Quaternion.AngleAxis(45f + i * 90f, Vector3.right);
                AddPatch(mesh, texels, 0, 16, (u, v) => roll * new Vector3(-12f + 16f * u, 2f - 4f * v, 0f));
            }
            Quaternion tailRoll = Quaternion.AngleAxis(45f, Vector3.right);
            AddPatch(mesh, texels, 5, 5, (u, v) => tailRoll * new Vector3(-11f, -2f + 4f * v, -2f + 4f * u));
            return mesh.Build("Minecraft arrow entity: crossed shaft, fletching and tail");
        }

        private static void AddPatch(MinecraftAvatar.MeshBuilder mesh, Color32[] texels, int row, int width, Func<float, float, Vector3> point)
        {
            const float unit = 0.05625f;
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < width; x++)
            {
                if (texels[(31 - row - y) * 32 + x].a < 128) continue;
                float left = (float)x / width, right = (float)(x + 1) / width;
                float top = y / 5f, bottom = (y + 1) / 5f;
                Vector3 a = point(left, top) * unit, b = point(right, top) * unit;
                Vector3 c = point(right, bottom) * unit, d = point(left, bottom) * unit;
                Vector2 uv = new Vector2((x + 0.5f) / 32f, 1f - (row + y + 0.5f) / 32f);
                // Omit transparent atlas pixels so the same depth-writing
                // material as the other world meshes can render the arrow.
                mesh.SolidFace(a, b, c, d, uv, 1f);
                mesh.SolidFace(d, c, b, a, uv, 1f);
            }
        }
    }
}
