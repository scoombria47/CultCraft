using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CultCraft
{
    // The shared strip stays in RAM; frame UVs are written into owned meshes.
    public sealed class MinecraftTextureAnimator : MonoBehaviour
    {
        private sealed class Strip
        {
            public Material Material;
            public int[] Frames;
            public float[] Durations;
            public int Frame, Count;
            public float Elapsed;
            public readonly List<MeshFrames> Meshes = new List<MeshFrames>();
        }
        private sealed class MeshFrames { public Mesh Mesh; public Vector2[] Source, Current; }
        private readonly List<Strip> strips = new List<Strip>();
        public int AnimatedMaterialCount { get { return strips.Count; } }
        public int AnimatedMeshCount { get { int count=0;foreach(Strip strip in strips)count+=strip.Meshes.Count;return count; } }

        public void Bind(Material material, string metadata)
        {
            if (material == null || material.mainTexture == null || String.IsNullOrEmpty(metadata)) throw new InvalidOperationException("Installed animated Minecraft texture is unavailable.");
            JObject animation = JObject.Parse(metadata)["animation"] as JObject;
            if (animation == null) throw new InvalidOperationException("Installed Minecraft texture has no animation metadata.");
            int frameHeight = (int?)animation["height"] ?? material.mainTexture.width;
            int frameWidth = (int?)animation["width"] ?? material.mainTexture.width;
            if (frameWidth != material.mainTexture.width || frameHeight <= 0 || material.mainTexture.height % frameHeight != 0) throw new InvalidOperationException("Minecraft animation strip dimensions are invalid.");
            int count = material.mainTexture.height / frameHeight;
            int defaultTicks = Math.Max(1, (int?)animation["frametime"] ?? 1);
            JArray declared = animation["frames"] as JArray;
            var frames = new List<int>(); var durations = new List<float>();
            if (declared == null || declared.Count == 0)
            {
                for (int i = 0; i < count; i++) { frames.Add(i); durations.Add(defaultTicks * 0.05f); }
            }
            else foreach (JToken entry in declared)
            {
                int index = entry.Type == JTokenType.Integer ? (int)entry : (int)entry["index"];
                int ticks = entry.Type == JTokenType.Integer ? defaultTicks : (int?)entry["time"] ?? defaultTicks;
                if (index < 0 || index >= count || ticks < 1) throw new InvalidOperationException("Installed Minecraft animation frame is invalid.");
                frames.Add(index); durations.Add(ticks * 0.05f);
            }
            var strip = new Strip { Material = material, Frames = frames.ToArray(), Durations = durations.ToArray(), Count = count };
            strips.Add(strip);
        }

        public void BindMeshes()
        {
            foreach(Strip strip in strips)
            {
                strip.Meshes.Clear();
                strip.Material.mainTextureScale=Vector2.one;strip.Material.mainTextureOffset=Vector2.zero;
                foreach(MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
                {
                    if(renderer.sharedMaterial!=strip.Material)continue;
                    MeshFilter filter=renderer.GetComponent<MeshFilter>();
                    if(filter==null||filter.sharedMesh==null)continue;
                    Vector2[] source=filter.sharedMesh.uv;
                    filter.sharedMesh.MarkDynamic();
                    strip.Meshes.Add(new MeshFrames{Mesh=filter.sharedMesh,Source=source,Current=new Vector2[source.Length]});
                }
                if(strip.Meshes.Count==0)throw new InvalidOperationException("Minecraft animation has no owned rendered mesh.");
                SetFrame(strip);
            }
        }

        private static void SetFrame(Strip strip)
        {
            float offset=1f-(strip.Frames[strip.Frame]+1f)/strip.Count;
            foreach(MeshFrames frame in strip.Meshes)
            {
                for(int i=0;i<frame.Source.Length;i++)frame.Current[i]=new Vector2(frame.Source[i].x,offset+frame.Source[i].y/strip.Count);
                if(frame.Mesh!=null)frame.Mesh.uv=frame.Current;
            }
        }

        private void Update()
        {
            foreach (Strip strip in strips)
            {
                if (strip.Material == null) continue;
                strip.Elapsed += Time.deltaTime;
                bool changed = false;
                while (strip.Elapsed >= strip.Durations[strip.Frame])
                {
                    strip.Elapsed -= strip.Durations[strip.Frame];
                    strip.Frame = (strip.Frame + 1) % strip.Frames.Length; changed = true;
                }
                if (changed) SetFrame(strip);
            }
        }
    }
}
