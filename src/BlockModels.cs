using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace CultCraft
{
    // Geometry lives in a detached positive-scale model. Never inherit a native Spine flip.
    internal sealed class BlockModels
    {
        private readonly MinecraftAssets assets;
        private readonly List<Material> materials;
        private readonly List<Mesh> meshes;
        private readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();
        private readonly Dictionary<Material, Geometry> batches = new Dictionary<Material, Geometry>();
        private readonly Transform parent;
        private Color foliageTint = new Color(0.38f, 0.7f, 0.16f, 1f);
        private static Vector3[] cubeVertices, cubeNormals;
        private static Vector2[] cubeUv;
        private static int[] cubeTriangles;

        private sealed class Geometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        public BlockModels(MinecraftAssets assets, Transform parent, List<Material> materials, List<Mesh> meshes)
        { this.assets = assets; this.parent = parent; this.materials = materials; this.meshes = meshes; }

        private static void EnsureCubeGeometry()
        {
            if (cubeVertices != null) return;
            // Read Unity's correctly wound builtin cube once. No per-block GameObjects or colliders.
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.SetActive(false);
            Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            cubeVertices = mesh.vertices; cubeNormals = mesh.normals;
            cubeUv = mesh.uv; cubeTriangles = mesh.triangles;
            UnityEngine.Object.Destroy(primitive);
        }

        private Material Material(string id, bool cutout = false)
        {
            bool leaves = id.EndsWith("_leaves", StringComparison.Ordinal);
            cutout |= leaves;
            string key = id + (cutout ? "/cutout" : "/solid");
            Material material;
            if (palette.TryGetValue(key, out material)) return material;
            // Both shaders write depth and cull backs. A sprite shader is not a valid 3D fallback.
            Shader shader = Shader.Find(cutout ? "Unlit/Transparent Cutout" : "Unlit/Texture");
            bool solidLeaves = cutout && leaves && (shader == null || !shader.isSupported);
            // Stripped player builds may omit Unity's optional cutout shader.
            // Keep solid cubes depth-writing; only crossed plants/flames use the
            // game's ordinary alpha sprite material as their transparent fallback.
            if (shader == null || !shader.isSupported) shader = Shader.Find(leaves || !cutout ? "Unlit/Texture" : "Sprites/Default");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("No supported depth-writing Minecraft block shader: " + key);
            Texture2D texture = leaves ? assets.TintedTexture(id, foliageTint) : assets.Texture(id);
            if (solidLeaves) texture = assets.OpaqueFoliage(id, foliageTint);
            if (texture == null) throw new InvalidOperationException("Installed Minecraft texture is unavailable: " + id);
            material = new Material(shader) { name = "CultCraft solid Minecraft " + id, mainTexture = texture };
            if (cutout) material.SetFloat("_Cutoff", 0.2f);
            materials.Add(material); palette.Add(key, material); return material;
        }

        private Geometry Batch(Material material)
        {
            Geometry geometry;
            if (!batches.TryGetValue(material, out geometry)) batches.Add(material, geometry = new Geometry());
            return geometry;
        }

        private static int FaceBit(Vector3 normal)
        {
            if (normal.x > 0.5f) return 1; if (normal.x < -0.5f) return 2;
            if (normal.y > 0.5f) return 4; if (normal.y < -0.5f) return 8;
            return normal.z > 0.5f ? 16 : 32;
        }

        private void Cube(Vector3 position, Vector3 size, string side, string top = null, string front = null, Rect? topPixels = null, int faceMask = 63)
        {
            EnsureCubeGeometry();
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) throw new ArgumentOutOfRangeException("size", "Block sizes must be positive.");
            Material sideMaterial = Material(side), topMaterial = Material(top ?? side);
            Material frontMaterial = front == null ? null : Material(front);
            Texture2D topTexture = topPixels.HasValue ? assets.Texture(top ?? side) : null;
            for (int i = 0; i < cubeTriangles.Length; i += 3)
            {
                Vector3 normal = cubeNormals[cubeTriangles[i]];
                if ((faceMask & FaceBit(normal)) == 0) continue;
                bool endCap = normal.y > 0.5f || normal.y < -0.5f && side.EndsWith("_log", StringComparison.Ordinal);
                Geometry geometry = Batch(endCap ? topMaterial : normal.z < -0.5f && frontMaterial != null ? frontMaterial : sideMaterial);
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = cubeTriangles[i + corner];
                    Vector3 vertex = cubeVertices[index]; Vector2 uv = cubeUv[index];
                    if(Mathf.Abs(normal.y)<0.5f && (side=="crafting_table_side" || side=="furnace_side"))
                        uv=new Vector2(uv.x,vertex.y+0.5f);
                    if (normal.y > 0.5f && topPixels.HasValue)
                    {
                        Rect rect = topPixels.Value;
                        uv = new Vector2((rect.x + (vertex.x + 0.5f) * rect.width) / topTexture.width, 1f - (rect.y + (vertex.z + 0.5f) * rect.height) / topTexture.height);
                    }
                    geometry.Triangles.Add(geometry.Vertices.Count);
                    geometry.Vertices.Add(Vector3.Scale(vertex, size) + position);
                    geometry.Normals.Add(cubeNormals[index]); geometry.Uv.Add(uv);
                }
            }
        }

        public void Build(BuildingRow design, string vegetationId = null, float size = 1f, RockPileFit.Footprint rockFootprint = null)
        {
            if (!String.IsNullOrEmpty(vegetationId)) BuildVegetation(Spec.Vegetation(vegetationId), Mathf.Clamp(size, 0.1f, 4f));
            else if (design.Model == "tree") BuildVegetation(Spec.Vegetation("oak_medium"), 1f);
            else if (design.Model == "berry_bush" || design.Model == "berry_crop") BuildVegetation(Spec.Vegetation("berry_3"), 1f);
            else if (design.Model == "rose_bush") BuildVegetation(Spec.Vegetation("rose_bush_full"), 1f);
            else
            {
                float w = design.Width, d = design.Depth, h = design.Height;
                if (design.Model == "rock_pile")
                {
                    RockPile(w, d, h, rockFootprint);
                }
                else if (design.Model == "bed") RedBed26(design);
                else if (design.Model == "straw_bed") StrawBed(design);
                else if (design.Model == "campfire") Campfire();
                else if (design.Model == "furnace") Cube(Vector3.up * h * 0.5f, new Vector3(w, h, d), "furnace_side", "furnace_top", "furnace_front");
                else if (design.Model == "workbench") Cube(Vector3.up * h * 0.5f, new Vector3(w, h, d), "crafting_table_side", "crafting_table_top", "crafting_table_front");
                else Cube(Vector3.up * h * 0.5f, new Vector3(w, h, d), design.Texture, design.TopTexture);
            }
            Bake();
            MinecraftTextureAnimator animation = parent.GetComponent<MinecraftTextureAnimator>();
            if(animation!=null)animation.BindMeshes();
        }

        private void RockPile(float width, float depth, float height, RockPileFit.Footprint footprint)
        {
            const float u = RockPileFit.BlockEdge;
            // Larger native rocks add columns and layers, never larger texels/cubes.
            int columns = Mathf.Clamp(2 * Mathf.CeilToInt((width / u - 1f) * 0.5f) + 1, 1, 25);
            int rows = Mathf.Clamp(2 * Mathf.CeilToInt((depth / u - 1f) * 0.5f) + 1, 1, 21);
            int layers = Mathf.Clamp(Mathf.CeilToInt(height / u), 1, 15);
            float xCenter = (columns - 1f) * 0.5f, zCenter = (rows - 1f) * 0.5f;
            var stacks = new int[columns, rows];
            for (int x = 0; x < columns; x++)
            for (int z = 0; z < rows; z++)
            {
                float rx = columns == 1 ? 0f : (x - xCenter) / xCenter;
                float rz = rows == 1 ? 0f : (z - zCenter) / zCenter;
                float radius = Mathf.Sqrt(rx * rx + rz * rz);
                if (radius > 1.08f) continue;
                Vector3 cell = new Vector3((x - xCenter) * u, 0f, (z - zCenter) * u);
                if (footprint != null && !footprint.ContainsCell(parent.TransformPoint(cell), u * 0.5f)) continue;
                int stack = 1 + Mathf.FloorToInt((layers - 1f) * Mathf.Pow(Mathf.Clamp01(1f - radius), 0.7f));
                if (stack > 2 && (x * 31 + z * 17) % 5 == 0) stack--;
                stacks[x, z] = stack;
            }
            for (int x = 0; x < columns; x++)
            for (int z = 0; z < rows; z++)
            for (int y = 0; y < stacks[x, z]; y++)
            {
                // Adjacent opaque cubes share hidden faces. Keep every outer
                // face, including the bottom used for the native footprint fit.
                int faces = 0;
                if (x + 1 == columns || stacks[x + 1, z] <= y) faces |= 1;
                if (x == 0 || stacks[x - 1, z] <= y) faces |= 2;
                if (y + 1 == stacks[x, z]) faces |= 4;
                if (y == 0) faces |= 8;
                if (z + 1 == rows || stacks[x, z + 1] <= y) faces |= 16;
                if (z == 0 || stacks[x, z - 1] <= y) faces |= 32;
                if (faces != 0)
                    Cube(new Vector3((x - xCenter) * u, (y + 0.5f) * u, (z - zCenter) * u), Vector3.one * u, "stone", faceMask: faces);
            }
        }

        public static Vector3 BedPillow(BuildingRow design)
        {
            // Both actual source beds put the pillow center at Z12 in the head's 16px half.
            return new Vector3(0f, design.Height, design.Depth * 12f / 32f);
        }

        private void StrawBed(BuildingRow design)
        {
            foreach (bool head in new[] { true, false })
            {
                string id = head ? "straw_bed_head_model" : "straw_bed_foot_model";
                JObject model = JObject.Parse(assets.ReadText(Spec.Asset(id).Entry));
                string texture = ((string)model["textures"]?["all"] ?? "").Replace("minecraft:", "");
                if (texture != "block/straw_bed") throw new InvalidOperationException("Installed straw bed model has an unexpected texture.");
                foreach (JToken element in (JArray)model["elements"])
                {
                    Vector3 from = ModelVector(element["from"]), to = ModelVector(element["to"]);
                    // Vanilla 26.3 keeps the fringes as thin double-sided cutout planes;
                    // the solid straw base and raised pillow retain opaque depth materials.
                    bool fringe = Math.Abs(to.x - from.x) < 0.001f || Math.Abs(to.y - from.y) < 0.001f || Math.Abs(to.z - from.z) < 0.001f;
                    Geometry batch = Batch(Material("straw_bed", fringe));
                    foreach (var face in (JObject)element["faces"])
                    {
                        Vector3[] corners = ModelFace(face.Key, from, to);
                        RotateModelElement(corners, element["rotation"]);
                        for (int i = 0; i < corners.Length; i++)
                            corners[i] = new Vector3((corners[i].x - 8f) * design.Width / 16f,
                                corners[i].y * design.Height / 5f, (corners[i].z + (head ? 0f : -16f)) * design.Depth / 32f);
                        Quad(batch, corners, false, ModelUv(face.Value));
                    }
                }
            }
        }

        private static Vector2[] ModelUv(JToken face)
        {
            JArray source = (JArray)face["uv"];
            float u0 = (float)source[0] / 16f, v0 = 1f - (float)source[1] / 16f;
            float u1 = (float)source[2] / 16f, v1 = 1f - (float)source[3] / 16f;
            var uv = new[] { new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), new Vector2(u0, v0) };
            int turn = ((int?)face["rotation"] ?? 0) / 90;
            var result = new Vector2[4];
            for (int i = 0; i < 4; i++) result[i] = uv[(i + turn) % 4];
            return result;
        }

        private void RedBed(BuildingRow design)
        {
            // Exact vanilla 1.21 BedRenderer head/foot model layers and 64x64 atlas.
            // Source ModelPart coordinates use Y down and Z for the mattress thickness.
            // Reorient those layers so the head/pillow is +Z, opposite the old facade.
            BedAtlasBox(design, true, Vector3.zero, new Vector3(16f, 16f, 6f), 0, 0, Vector3.zero);
            BedAtlasBox(design, true, new Vector3(0f, 6f, 0f), Vector3.one * 3f, 50, 6, new Vector3(90f, 0f, 90f));
            BedAtlasBox(design, true, new Vector3(-16f, 6f, 0f), Vector3.one * 3f, 50, 18, new Vector3(90f, 0f, 180f));
            BedAtlasBox(design, false, Vector3.zero, new Vector3(16f, 16f, 6f), 0, 22, Vector3.zero);
            BedAtlasBox(design, false, new Vector3(0f, 6f, -16f), Vector3.one * 3f, 50, 0, new Vector3(90f, 0f, 0f));
            BedAtlasBox(design, false, new Vector3(-16f, 6f, -16f), Vector3.one * 3f, 50, 12, new Vector3(90f, 0f, 270f));
        }

        private void RedBed26(BuildingRow design)
        {
            foreach(bool head in new[]{true,false})
            {
                JObject model=JObject.Parse(assets.ReadText(Spec.Asset(head?"red_bed_head26":"red_bed_foot26").Entry));
                var textures=new Dictionary<string,string>(StringComparer.Ordinal);
                for(int depth=0;depth<6;depth++)
                {
                    JObject map=model["textures"] as JObject;
                    if(map!=null)foreach(var pair in map)if(!textures.ContainsKey(pair.Key)||textures[pair.Key]=="#"+pair.Key)textures[pair.Key]=(string)pair.Value;
                    if(model["elements"]!=null)break;
                    string ancestor=(string)model["parent"];
                    if(String.IsNullOrEmpty(ancestor))throw new InvalidOperationException("Minecraft red-bed model has no elements.");
                    model=JObject.Parse(assets.ReadText("assets/minecraft/models/"+ancestor.Replace("minecraft:","")+".json"));
                }
                foreach(JToken element in (JArray)model["elements"])
                    foreach(var face in (JObject)element["faces"])
                    {
                        string texture=(string)face.Value["texture"];
                        for(int i=0;i<8&&texture.StartsWith("#",StringComparison.Ordinal);i++)texture=textures[texture.Substring(1)];
                        string name=texture.Replace("minecraft:","");
                        Vector3[] points=ModelFace(face.Key,ModelVector(element["from"]),ModelVector(element["to"]));
                        for(int i=0;i<points.Length;i++){Vector3 p=points[i];points[i]=new Vector3((8f-p.x)*design.Width/16f,p.y*design.Height/9f,((head?16f:0f)-p.z)*design.Depth/32f);}
                        Quad(Batch(Material("assets/minecraft/textures/"+name+".png")),points,false,ModelUv(face.Value));
                    }
            }
        }

        private void BedAtlasBox(BuildingRow design, bool head, Vector3 min, Vector3 size, int u, int v, Vector3 angles)
        {
            Quaternion rotation = Quaternion.AngleAxis(angles.z, Vector3.forward) * Quaternion.AngleAxis(angles.y, Vector3.up) * Quaternion.AngleAxis(angles.x, Vector3.right);
            float w = size.x, h = size.y, d = size.z;
            Geometry batch = Batch(Material("red_bed"));
            foreach (string face in new[] { "north", "south", "west", "east", "up", "down" })
            {
                Rect atlas;
                switch (face)
                {
                    case "north": atlas = new Rect(u + d, v + d, w, h); break;
                    case "south": atlas = new Rect(u + d + w + d, v + d, w, h); break;
                    case "west": atlas = new Rect(u, v + d, d, h); break;
                    case "east": atlas = new Rect(u + d + w, v + d, d, h); break;
                    case "up": atlas = new Rect(u + d, v, w, d); break;
                    default: atlas = new Rect(u + d + w, v, w, d); break;
                }
                Vector3[] corners = ModelFace(face, min, min + size);
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 source = rotation * corners[i];
                    corners[i] = new Vector3((8f - source.x) * design.Width / 16f,
                        (9f - source.z) * design.Height / 9f, ((head ? 16f : 0f) - source.y) * design.Depth / 32f);
                }
                // ModelPart vertical texture coordinates increase down its original Y axis.
                float u0 = atlas.x / 64f, u1 = (atlas.x + atlas.width) / 64f;
                float v0 = 1f - atlas.y / 64f, v1 = 1f - (atlas.y + atlas.height) / 64f;
                // ModelPart uses Y-down; mapping it to Z-up reverses the face's
                // vertical direction compared with ordinary Minecraft block JSON.
                Quad(batch, corners, false, new[] { new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), new Vector2(u0, v0) });
            }
        }

        private void BuildVegetation(VegetationRow row, float scale)
        {
            float unit = row.Unit * scale;
            foliageTint = new Color(row.TintR, row.TintG, row.TintB, 1f);
            if (row.Shape == "cross") { Cross(row.Texture, unit); return; }
            if (row.Shape == "double_cross") { Cross(row.Texture,unit);Cross(row.TopTexture,unit,unit);return; }
            if (row.Shape == "stump") { Cube(Vector3.up * unit * 0.25f, new Vector3(unit, unit * 0.5f, unit), row.Texture, row.TopTexture); return; }
            for (int n = 0; n < row.TrunkBlocks; n++) Cube(new Vector3(0f, (n + 0.5f) * unit, 0f), Vector3.one * unit, row.Texture, row.TopTexture);
            var leaves = new HashSet<Vector3Int>();
            if (row.Shape == "spruce")
            {
                // Tiered evergreen silhouette: broad lower whorls and a narrow pointed top.
                for (int y = 0; y < row.CrownLayers; y++)
                {
                    int radius = Math.Max(0, row.CrownRadius - y / 2);
                    int layer = Math.Max(1, row.TrunkBlocks - row.CrownLayers + 2) + y;
                    for (int x = -radius; x <= radius; x++) for (int z = -radius; z <= radius; z++)
                        if (Math.Abs(x) + Math.Abs(z) <= radius + 1) leaves.Add(new Vector3Int(x, layer, z));
                }
                leaves.Add(new Vector3Int(0, row.TrunkBlocks + 1, 0));
            }
            else
            {
                AddCrown(leaves, new Vector3Int(0, row.TrunkBlocks, 0), row.CrownRadius, row.CrownLayers);
                if (row.Shape == "oak_forked")
                {
                    for (int n = 1; n <= 2; n++)
                    {
                        Cube(new Vector3(n * unit, (row.TrunkBlocks - 1.5f) * unit, 0f), Vector3.one * unit, row.Texture, row.TopTexture);
                        Cube(new Vector3(-n * unit, (row.TrunkBlocks - 0.5f) * unit, unit), Vector3.one * unit, row.Texture, row.TopTexture);
                    }
                    AddCrown(leaves, new Vector3Int(2, row.TrunkBlocks, 0), 1, 2);
                    AddCrown(leaves, new Vector3Int(-2, row.TrunkBlocks + 1, 1), 1, 2);
                }
            }
            foreach (Vector3Int cell in leaves)
            {
                // Hidden internal leaf faces never reach the renderer.
                int mask = 0;
                if (!leaves.Contains(cell + Vector3Int.right)) mask |= 1;
                if (!leaves.Contains(cell + Vector3Int.left)) mask |= 2;
                if (!leaves.Contains(cell + Vector3Int.up)) mask |= 4;
                if (!leaves.Contains(cell + Vector3Int.down)) mask |= 8;
                if (!leaves.Contains(cell + new Vector3Int(0, 0, 1))) mask |= 16;
                if (!leaves.Contains(cell + new Vector3Int(0, 0, -1))) mask |= 32;
                Cube((Vector3)cell * unit, Vector3.one * unit, row.Foliage, faceMask: mask);
            }
        }

        private static void AddCrown(HashSet<Vector3Int> cells, Vector3Int center, int radius, int layers)
        {
            for (int y = 0; y < layers; y++)
            {
                int spread = Math.Max(0, radius - (y == layers - 1 ? 1 : 0));
                for (int x = -spread; x <= spread; x++) for (int z = -spread; z <= spread; z++)
                    if (spread < 2 || Math.Abs(x) != spread || Math.Abs(z) != spread) cells.Add(center + new Vector3Int(x, y, z));
            }
        }

        private static Vector3 ModelVector(JToken token)
        {
            JArray vector = token as JArray;
            if (vector == null || vector.Count < 3) throw new InvalidOperationException("Minecraft block model has an invalid vector.");
            return new Vector3((float)vector[0], (float)vector[1], (float)vector[2]);
        }

        private void Campfire()
        {
            // Resolve the owner's actual vanilla parent model, including its per-face atlas UVs.
            JObject model = JObject.Parse(assets.ReadText(Spec.Asset("campfire_model").Entry));
            var textures = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int depth = 0; depth < 8; depth++)
            {
                JObject sourceTextures = model["textures"] as JObject;
                if (sourceTextures != null) foreach (var entry in sourceTextures)
                    if (!textures.ContainsKey(entry.Key)) textures.Add(entry.Key, (string)entry.Value);
                if (model["elements"] != null) break;
                string path = (string)model["parent"];
                if (String.IsNullOrEmpty(path)) throw new InvalidOperationException("Installed Minecraft campfire has no block elements.");
                model = JObject.Parse(assets.ReadText("assets/minecraft/models/" + path.Replace("minecraft:", "") + ".json"));
            }
            JArray elements = model["elements"] as JArray;
            if (elements == null) throw new InvalidOperationException("Installed Minecraft campfire parent chain is invalid.");
            float size = Spec.Visual("campfire_model_size").Value;
            foreach (JToken element in elements)
            {
                Vector3 from = ModelVector(element["from"]), to = ModelVector(element["to"]);
                foreach (var face in (JObject)element["faces"])
                {
                    string texture = (string)face.Value["texture"];
                    for (int depth = 0; texture.StartsWith("#", StringComparison.Ordinal) && depth < 8; depth++) texture = textures[texture.Substring(1)];
                    string id = texture.Substring(texture.LastIndexOf('/') + 1);
                    if (id != "campfire_log" && id != "campfire_log_lit" && id != "campfire_fire") throw new InvalidOperationException("Unexpected installed campfire texture: " + texture);
                    Vector3[] corners = ModelFace(face.Key, from, to);
                    RotateModelElement(corners, element["rotation"]);
                    for (int i = 0; i < corners.Length; i++) corners[i] = (corners[i] / 16f - new Vector3(0.5f, 0f, 0.5f)) * size;
                    JArray sourceUv = (JArray)face.Value["uv"];
                    float u0 = (float)sourceUv[0] / 16f, v0 = 1f - (float)sourceUv[1] / 16f;
                    float u1 = (float)sourceUv[2] / 16f, v1 = 1f - (float)sourceUv[3] / 16f;
                    var uv = new[] { new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), new Vector2(u0, v0) };
                    int turn = ((int?)face.Value["rotation"] ?? 0) / 90;
                    var rotatedUv = new Vector2[4];
                    for (int i = 0; i < 4; i++) rotatedUv[i] = uv[(i + turn) % 4];
                    Quad(Batch(Material(id, id == "campfire_fire")), corners, false, rotatedUv);
                }
            }
            var animation = parent.gameObject.AddComponent<MinecraftTextureAnimator>();
            animation.Bind(Material("campfire_fire", true), assets.ReadText(Spec.Asset("campfire_fire_animation").Entry));
            animation.Bind(Material("campfire_log_lit"), assets.ReadText(Spec.Asset("campfire_log_lit_animation").Entry));
        }

        private static Vector3[] ModelFace(string face, Vector3 min, Vector3 max)
        {
            switch (face)
            {
                case "north": return new[] { new Vector3(max.x, min.y, min.z), min, new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z) };
                case "south": return new[] { new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), max, new Vector3(min.x, max.y, max.z) };
                case "west": return new[] { min, new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z) };
                case "east": return new[] { new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), max };
                case "up": return new[] { new Vector3(min.x, max.y, max.z), max, new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z) };
                case "down": return new[] { min, new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z) };
                default: throw new InvalidOperationException("Unknown installed Minecraft model face: " + face);
            }
        }

        private static void RotateModelElement(Vector3[] corners, JToken rotation)
        {
            if (rotation == null) return;
            string axisName = (string)rotation["axis"];
            Vector3 axis = axisName == "x" ? Vector3.right : axisName == "y" ? Vector3.up : Vector3.forward;
            Vector3 origin = ModelVector(rotation["origin"]);
            float angle = (float)rotation["angle"];
            float rescale = (bool?)rotation["rescale"] == true ? 1f / Mathf.Cos(angle * Mathf.Deg2Rad) : 1f;
            Quaternion turn = Quaternion.AngleAxis(angle, axis);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 offset = corners[i] - origin;
                offset = Vector3.Scale(offset, axisName == "x" ? new Vector3(1f, rescale, rescale) : axisName == "y" ? new Vector3(rescale, 1f, rescale) : new Vector3(rescale, rescale, 1f));
                corners[i] = turn * offset + origin;
            }
        }

        private void Cross(string textureId, float scale,float heightOffset=0f)
        {
            string json = assets.ReadText(Spec.Asset(textureId + "_model").Entry);
            if (String.IsNullOrEmpty(json)) throw new InvalidOperationException("Installed Minecraft plant model is unavailable: " + textureId);
            JObject model = JObject.Parse(json);
            string texture = (string)model["textures"]?["cross"];
            if (texture != "minecraft:block/" + textureId) throw new InvalidOperationException("Minecraft plant model texture does not match declared profile: " + textureId);
            for (int depth = 0; model["elements"] == null && depth < 4; depth++)
            {
                string parentModel = (string)model["parent"];
                if (String.IsNullOrEmpty(parentModel)) break;
                model = JObject.Parse(assets.ReadText("assets/minecraft/models/" + parentModel.Replace("minecraft:", "") + ".json"));
            }
            JArray elements = model["elements"] as JArray;
            if (elements == null || elements.Count == 0) throw new InvalidOperationException("Minecraft crossed plant model has no elements.");
            Geometry geometry = Batch(Material(textureId, true));
            foreach (JToken element in elements)
            {
                Vector3 from = ModelVector(element["from"]), to = ModelVector(element["to"]);
                Vector3[] corners = Math.Abs(from.z - to.z) < 0.001f
                    ? new[] { from, new Vector3(to.x, from.y, from.z), to, new Vector3(from.x, to.y, to.z) }
                    : new[] { from, new Vector3(from.x, from.y, to.z), to, new Vector3(to.x, to.y, from.z) };
                JToken rotation = element["rotation"];
                if (rotation != null)
                {
                    string axisName = (string)rotation["axis"];
                    Vector3 axis = axisName == "x" ? Vector3.right : axisName == "y" ? Vector3.up : Vector3.forward;
                    Vector3 origin = ModelVector(rotation["origin"]);
                    float angle = (float)rotation["angle"];
                    float rescale = (bool?)rotation["rescale"] == true ? 1f / Mathf.Cos(angle * Mathf.Deg2Rad) : 1f;
                    Quaternion turn = Quaternion.AngleAxis(angle, axis);
                    for (int i = 0; i < corners.Length; i++)
                    {
                        Vector3 offset = corners[i] - origin;
                        offset = Vector3.Scale(offset, axisName == "x" ? new Vector3(1f, rescale, rescale) : axisName == "y" ? new Vector3(rescale, 1f, rescale) : new Vector3(rescale, rescale, 1f));
                        corners[i] = turn * offset + origin;
                    }
                }
                for (int i = 0; i < corners.Length; i++) corners[i] = (corners[i] / 16f - new Vector3(0.5f, 0f, 0.5f)) * scale + Vector3.up*heightOffset;
                Quad(geometry, corners, false); Quad(geometry, corners, true);
            }
        }

        private static void Quad(Geometry geometry, Vector3[] corners, bool reverse, Vector2[] sourceUv = null)
        {
            int start = geometry.Vertices.Count;
            Vector3 normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).normalized * (reverse ? -1f : 1f);
            Vector2[] uv = sourceUv ?? new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            for (int i = 0; i < 4; i++) { geometry.Vertices.Add(corners[i]); geometry.Normals.Add(normal); geometry.Uv.Add(uv[i]); }
            int[] triangles = reverse ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
            foreach (int index in triangles) geometry.Triangles.Add(start + index);
        }

        private void Bake()
        {
            foreach (var batch in batches)
            {
                Geometry geometry = batch.Value;
                if (geometry.Vertices.Count == 0) continue;
                var mesh = new Mesh { name = "CultCraft batched solid model" };
                if (geometry.Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(geometry.Vertices); mesh.SetUVs(0, geometry.Uv); mesh.SetNormals(geometry.Normals);
                var colors = new Color[geometry.Vertices.Count];
                for(int i=0;i<colors.Length;i++)colors[i]=Color.white;
                mesh.colors=colors;
                mesh.SetTriangles(geometry.Triangles, 0); mesh.RecalculateBounds(); meshes.Add(mesh);
                var obj = new GameObject("CultCraft batched " + batch.Key.name); obj.transform.SetParent(parent, false);
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = batch.Key;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
        }
    }
}
