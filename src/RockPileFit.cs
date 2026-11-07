using System;
using System.Collections.Generic;
using UnityEngine;

namespace CultCraft
{
    internal static class RockPileFit
    {
        // Use the game's native top-down projection, even when the player is
        // looking elsewhere in FPS. Fitting is done once, never on camera turns.
        private static readonly Vector3 Up = Quaternion.Euler(-45f, 0f, 0f) * Vector3.up;

        public const float BlockEdge = 0.4f;

        internal sealed class Footprint
        {
            public readonly Collider2D[] Colliders;
            public readonly Bounds Bounds;
            public Footprint(Collider2D[] colliders, Bounds bounds) { Colliders = colliders; Bounds = bounds; }
            public bool Contains(Vector2 point)
            {
                foreach (Collider2D collider in Colliders) if (collider != null && collider.OverlapPoint(point)) return true;
                return false;
            }
            public bool ContainsCell(Vector3 center, float half)
            {
                half = Mathf.Max(0f, half - 0.003f);
                return Contains(new Vector2(center.x - half, center.y - half)) && Contains(new Vector2(center.x + half, center.y - half)) &&
                    Contains(new Vector2(center.x - half, center.y + half)) && Contains(new Vector2(center.x + half, center.y + half)) && Contains(center);
            }
        }

        public static Footprint BlockingFootprint(Transform native)
        {
            var colliders = new List<Collider2D>();
            Structure structure = native.GetComponent<Structure>();
            // These are the native navigation obstructions, sometimes outside
            // the art hierarchy. Damage/interaction trigger bounds are not the footprint.
            if (structure != null && structure.ObstructionColldiers != null)
                foreach (Collider2D collider in structure.ObstructionColldiers)
                    if (Solid(collider) && !colliders.Contains(collider)) colliders.Add(collider);
            if (colliders.Count == 0)
                foreach (Collider2D collider in native.GetComponentsInChildren<Collider2D>(true))
                    if (Solid(collider) && !colliders.Contains(collider)) colliders.Add(collider);
            if (colliders.Count == 0) return null;
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Count; i++) bounds.Encapsulate(colliders[i].bounds);
            return new Footprint(colliders.ToArray(), bounds);
        }
        private static bool Solid(Collider2D collider)
        { return collider != null && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger; }

        public static BuildingRow Layout(Transform native, IEnumerable<Renderer> original, BuildingRow design)
        {
            Bounds source;
            if (!NativeBounds(native, original, out source)) return design;
            Vector2 size = ProjectSize(source);
            Footprint footprint = BlockingFootprint(native);
            float depth = footprint != null ? footprint.Bounds.size.y : size.x * 0.55f;
            design.Width = Mathf.Clamp(footprint != null ? footprint.Bounds.size.x : size.x, BlockEdge, 10f);
            design.Depth = Mathf.Clamp(depth, BlockEdge, 8f);
            design.Height = Mathf.Clamp(size.y / Mathf.Abs(Up.y) - design.Depth, BlockEdge, 6f);
            if (footprint != null) design.Height = Mathf.Min(design.Height, Mathf.Max(design.Width, design.Depth) * 1.25f);
            return design;
        }

        public static bool Align(Transform visual, Transform native, IEnumerable<Renderer> original)
        {
            Footprint footprint = BlockingFootprint(native);
            if (footprint != null)
            {
                Structure structure = native.GetComponent<Structure>();
                float ground = structure != null && structure.Structure_Info != null ? structure.Structure_Info.Position.z : native.position.z;
                visual.position = new Vector3(footprint.Bounds.center.x, footprint.Bounds.center.y, ground);
                return true;
            }
            Bounds source, model;
            if (!NativeBounds(native, original, out source) || !BoundsOf(visual.GetComponentsInChildren<Renderer>(true), out model)) return false;
            // Preserve small, fixed-size cubes and ground height. Align the whole
            // assembled hill with the original silhouette by moving along the bank.
            Vector3 offset = new Vector3(source.center.x - model.center.x, 0f, 0f);
            offset.y = Vector3.Dot(source.center - model.center - offset, Up) / Up.y;
            visual.position += offset;
            return true;
        }

        private static bool NativeBounds(Transform native, IEnumerable<Renderer> original, out Bounds bounds)
        {
            Rubble rubble = native.GetComponent<Rubble>();
            if (rubble == null) rubble = native.GetComponentInChildren<Rubble>(true);
            if (rubble != null && rubble.objectPick != null && rubble.objectPick.CreatedObject != null)
                return BoundsOf(rubble.objectPick.CreatedObject.GetComponentsInChildren<Renderer>(true), out bounds);
            return BoundsOf(original, out bounds);
        }

        internal static bool Aligned(Transform visual, Transform native, out string detail)
        {
            Bounds source, model;
            var originals = new List<Renderer>();
            foreach (Renderer r in native.GetComponentsInChildren<Renderer>(true))
                if (!r.transform.IsChildOf(visual)) originals.Add(r);
            if (!NativeBounds(native, originals, out source) || !BoundsOf(visual.GetComponentsInChildren<Renderer>(true), out model))
            { detail = "native/model bounds absent"; return false; }
            Vector2 a = ProjectSize(source), b = ProjectSize(model);
            float centerError = Mathf.Abs(Vector3.Dot(source.center - model.center, Up));
            float xError = Mathf.Abs(source.center.x - model.center.x);
            // BlockModels batches twelve independent triangles (36 vertices) per cube.
            bool fixedBlocks = true, insideBlocking = true;
            int cubes = 0;
            Footprint footprint = BlockingFootprint(native);
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                Vector3[] vertices = filter.sharedMesh.vertices;
                if (vertices.Length % 36 != 0) { fixedBlocks = false; break; }
                for (int i = 0; i < vertices.Length; i += 36)
                {
                    Bounds cube = new Bounds(filter.transform.TransformPoint(vertices[i]), Vector3.zero);
                    for (int j = 1; j < 36; j++) cube.Encapsulate(filter.transform.TransformPoint(vertices[i + j]));
                    if ((cube.size - Vector3.one * BlockEdge).sqrMagnitude > 0.0001f) { fixedBlocks = false; break; }
                    cubes++;
                    if (footprint != null && !footprint.ContainsCell(cube.center, BlockEdge * 0.5f)) insideBlocking = false;
                }
            }
            detail = "native silhouette=" + a + ", Minecraft=" + b + ", cubes=" + cubes + ", fixed0.4=" + fixedBlocks +
                ", blocking colliders=" + (footprint == null ? 0 : footprint.Colliders.Length) + ", all columns inside obstruction=" + insideBlocking;
            if (footprint != null) return cubes > 0 && fixedBlocks && insideBlocking &&
                model.size.x <= footprint.Bounds.size.x + 0.01f && model.size.y <= footprint.Bounds.size.y + 0.01f &&
                (visual.localScale - Vector3.one).sqrMagnitude < 0.0001f;
            return b.x >= a.x * 0.95f && b.y >= a.y * 0.90f && centerError < 0.08f && xError < 0.06f && fixedBlocks && (visual.localScale - Vector3.one).sqrMagnitude < 0.0001f;
        }

        private static Vector2 ProjectSize(Bounds bounds)
        { return new Vector2(bounds.size.x, Mathf.Abs(Up.y) * bounds.size.y + Mathf.Abs(Up.z) * bounds.size.z); }

        private static bool BoundsOf(IEnumerable<Renderer> renderers, out Bounds bounds)
        {
            bounds = new Bounds(); bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer || renderer.GetComponentInParent<Canvas>() != null) continue;
                string name = renderer.name.ToLowerInvariant();
                if (name.Contains("shadow") || name.Contains("prompt") || name.Contains("indicator") || name.Contains("icon") || name.Contains("halo")) continue;
                Bounds b = renderer.bounds;
                if (b.size.sqrMagnitude < 0.0001f) continue;
                if (!found) { bounds = b; found = true; } else bounds.Encapsulate(b);
            }
            return found;
        }

        private static bool ColliderBounds(Transform native, out Bounds bounds)
        {
            bounds = new Bounds(); bool found = false;
            foreach (Collider2D collider in native.GetComponentsInChildren<Collider2D>(true))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
                if (!found) { bounds = collider.bounds; found = true; } else bounds.Encapsulate(collider.bounds);
            }
            return found;
        }
    }
}
