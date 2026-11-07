using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CultCraft
{
    // One immutable mesh/material per texture. The native pickup owns position
    // and physics, while its detached mesh spins about the world vertical.
    internal sealed class DroppedItemVisuals : IDisposable
    {
        private sealed class Geometry { public Mesh Mesh; public Material Material; }
        private readonly Dictionary<Texture2D,Geometry> geometry=new Dictionary<Texture2D,Geometry>();
        private readonly Dictionary<SpriteRenderer,DroppedItemBinding> bindings=new Dictionary<SpriteRenderer,DroppedItemBinding>();
        private readonly List<DroppedItemBinding> ordered=new List<DroppedItemBinding>();
        private bool disposed;
        private readonly MinecraftAssets assets;
        public DroppedItemBinding Get(SpriteRenderer source){DroppedItemBinding binding;return source!=null&&bindings.TryGetValue(source,out binding)?binding:null;}
        public DroppedItemVisuals(MinecraftAssets assets){this.assets=assets;Camera.onPreCull+=Prepare;}
        public void Apply(SpriteRenderer source,Sprite sprite)
        {
            if(disposed||source==null||sprite==null)return;
            Geometry cached;
            if(!geometry.TryGetValue(sprite.texture,out cached))
            {
                Shader shader=Shader.Find("Unlit/Texture");
                if(shader==null||!shader.isSupported)throw new InvalidOperationException("Depth-writing drop material unavailable.");
                string block=null;
                foreach(string id in new[]{"oak_log","stone","oak_planks","stone_bricks","pumpkin","melon","black_shulker_box"})
                    if(sprite.texture==assets.Texture(id)){block=id;break;}
                cached=new Geometry{Mesh=block!=null?MinecraftAvatar.GenerateBlockMesh(block,1f):MinecraftAvatar.GenerateItemMesh(sprite.texture,"drop "+sprite.name,1f,Spec.Visual("pickup_half_depth").Value),
                    Material=new Material(shader){name="CultCraft solid dropped "+sprite.name,mainTexture=block!=null?assets.HeldBlockTexture(block):sprite.texture,renderQueue=(int)Spec.Visual("material_queue").Value}};
                geometry.Add(sprite.texture,cached);
            }
            DroppedItemBinding binding;
            if(!bindings.TryGetValue(source,out binding))
            {
                GameObject model=new GameObject("CultCraft small 3D native pickup");
                model.AddComponent<MeshFilter>();model.AddComponent<MeshRenderer>();
                binding=model.AddComponent<DroppedItemBinding>();binding.Initialise(source);
                bindings.Add(source,binding);ordered.Add(binding);
            }
            binding.Bind(sprite,cached.Mesh,cached.Material);
            binding.Prepare();
        }
        public void Restore(SpriteRenderer source)
        {
            if(source==null)return;DroppedItemBinding binding;
            if(bindings.TryGetValue(source,out binding)){bindings.Remove(source);ordered.Remove(binding);if(binding!=null)binding.Release();}
        }
        private void Prepare(Camera camera)
        {
            if(disposed||camera==null||camera.cameraType!=CameraType.Game)return;
            for(int i=ordered.Count-1;i>=0;i--)
            {
                DroppedItemBinding binding=ordered[i];
                if(binding==null||binding.Source==null){if(binding!=null){if(!ReferenceEquals(binding.Source,null))bindings.Remove(binding.Source);binding.Release();}ordered.RemoveAt(i);continue;}
                binding.Prepare();
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;Camera.onPreCull-=Prepare;
            foreach(DroppedItemBinding binding in ordered)if(binding!=null)binding.Release();ordered.Clear();bindings.Clear();
            foreach(Geometry value in geometry.Values){UnityEngine.Object.Destroy(value.Mesh);UnityEngine.Object.Destroy(value.Material);}geometry.Clear();
        }
    }

    public sealed class DroppedItemBinding : MonoBehaviour
    {
        public SpriteRenderer Source { get; private set; }
        private bool nativeForceOff;
        private Sprite sprite;
        private MeshFilter filter;
        private MeshRenderer renderer;
        private int lastFrame=-1;
        private int preparedFrame = -1;
        private Quaternion firstDrawRotation;
        private static readonly Quaternion Upright = Quaternion.Euler(-90f, 0f, 0f);
        public int GameDrawFrames { get; private set; }
        public bool SpinObserved { get; private set; }
        public float WorldSize { get; private set; }
        public bool HasVolume { get { return filter!=null&&filter.sharedMesh!=null&&filter.sharedMesh.bounds.size.z>0.001f; } }
        public bool IsCube { get { return HasVolume&&filter.sharedMesh.bounds.size.z>=filter.sharedMesh.bounds.size.x*0.99f; } }
        public bool CubeAtlasMatches(Texture source,Texture atlas)
        {return IsCube&&sprite!=null&&sprite.texture==source&&renderer!=null&&renderer.sharedMaterial.mainTexture==atlas&&atlas!=null&&atlas.width==source.width&&atlas.height==source.height*3;}
        public bool NativeArtSuppressed { get { return Source!=null&&Source.forceRenderingOff; } }
        public void Initialise(SpriteRenderer source)
        {Source=source;nativeForceOff=source.forceRenderingOff;filter=GetComponent<MeshFilter>();renderer=GetComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
        public void Bind(Sprite value,Mesh mesh,Material material)
        {sprite=value;filter.sharedMesh=mesh;renderer.sharedMaterial=material;Source.forceRenderingOff=true;gameObject.layer=Source.gameObject.layer;preparedFrame=-1;}
        public void Prepare()
        {
            if (preparedFrame == Time.frameCount) return;
            preparedFrame = Time.frameCount;
            if(Source==null){renderer.enabled=false;return;}
            bool visible=Source.enabled&&Source.gameObject.activeInHierarchy&&Source.color.a>0.001f;
            renderer.enabled=visible;if(!visible)return;
            Source.forceRenderingOff=true;
            transform.position=Source.transform.position;
            float yaw = Mathf.Repeat(Time.time * Spec.Visual("pickup_spin_speed").Value, 360f);
            transform.rotation = Quaternion.AngleAxis(yaw, Vector3.back) * Upright;
            float longest=Mathf.Max(filter.sharedMesh.bounds.size.x,filter.sharedMesh.bounds.size.y);
            WorldSize=MinecraftAvatar.BodyHeight*Spec.Visual("pickup_height_fraction").Value;
            transform.localScale=Vector3.one*(WorldSize/Mathf.Max(0.001f,longest));
            renderer.sortingLayerID=Source.sortingLayerID;renderer.sortingOrder=Source.sortingOrder;
        }
        private void OnWillRenderObject()
        {
            Camera camera=Camera.current;
            if(camera==null||camera.cameraType!=CameraType.Game||Source==null||sprite==null||!HasVolume)return;
            if(renderer.sharedMaterial.mainTexture!=sprite.texture&&!CubeAtlasMatches(sprite.texture,renderer.sharedMaterial.mainTexture))return;
            if(lastFrame!=Time.frameCount)
            {
                if (GameDrawFrames == 0) firstDrawRotation = transform.rotation;
                if (Quaternion.Angle(firstDrawRotation, transform.rotation) > 8f &&
                    Vector3.Dot(transform.up, Vector3.back) > 0.999f) SpinObserved = true;
                lastFrame=Time.frameCount;GameDrawFrames++;
            }
            PickupFirstDrawProbe probe=Source.GetComponent<PickupFirstDrawProbe>();
            if(probe!=null)probe.ObserveMeshDraw(camera,sprite,renderer.sharedMaterial.mainTexture);
        }
        public void Release()
        {if(Source!=null)Source.forceRenderingOff=nativeForceOff;renderer.enabled=false;UnityEngine.Object.Destroy(gameObject);}
    }
}
