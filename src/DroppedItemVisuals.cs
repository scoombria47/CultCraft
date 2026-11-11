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
        private readonly Dictionary<(Texture2D,string,bool),Geometry> geometry=new Dictionary<(Texture2D,string,bool),Geometry>();
        private readonly Dictionary<SpriteRenderer,DroppedItemBinding> bindings=new Dictionary<SpriteRenderer,DroppedItemBinding>();
        private readonly List<DroppedItemBinding> ordered=new List<DroppedItemBinding>();
        private readonly Stack<DroppedItemBinding> available=new Stack<DroppedItemBinding>();
        private const int PoolCapacity=48;
        private Shader shader;
        private bool disposed;
        private readonly MinecraftAssets assets;
        internal bool TestDraws { get; set; }
        public DroppedItemBinding Get(SpriteRenderer source){DroppedItemBinding binding;return source!=null&&bindings.TryGetValue(source,out binding)?binding:null;}
        public DroppedItemVisuals(MinecraftAssets assets){this.assets=assets;Camera.onPreCull+=Prepare;}
        public void Warm(Sprite sprite,string itemId)
        {
            if(!disposed&&sprite!=null)GeometryFor(sprite,itemId,false);
        }
        private Geometry GeometryFor(Sprite sprite,string itemId,bool reward)
        {
            string block=MinecraftAvatar.IsBlockItem(itemId)?itemId:null;
            var key=(sprite.texture,block,reward);
            if(geometry.TryGetValue(key,out Geometry cached))return cached;
            if(shader==null)shader=Shader.Find("Unlit/Texture");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Depth-writing drop material unavailable.");
            Texture2D texture=block==null?sprite.texture:reward?assets.RewardBlockTexture(block):assets.HeldBlockTexture(block);
            cached=new Geometry{Mesh=block!=null?MinecraftAvatar.GenerateBlockMesh(block,1f,reward):MinecraftAvatar.GenerateItemMesh(sprite.texture,"drop "+sprite.name,1f,Spec.Visual("pickup_half_depth").Value),
                Material=new Material(shader){name="CultCraft solid dropped "+sprite.name,mainTexture=texture,renderQueue=(int)Spec.Visual("material_queue").Value}};
            geometry.Add(key,cached);
            return cached;
        }
        public void Apply(SpriteRenderer source,Sprite sprite,string itemId=null,Bounds? rewardBounds=null)
        {
            if(disposed||source==null||sprite==null)return;
            Geometry cached=GeometryFor(sprite,itemId,rewardBounds.HasValue);
            if(!bindings.TryGetValue(source,out DroppedItemBinding binding)||binding==null)
            {
                binding=Acquire();
                binding.Initialise(source,TestDraws);
                bindings[source]=binding;
                ordered.Add(binding);
            }
            binding.Bind(sprite,cached.Mesh,cached.Material,rewardBounds);
            binding.Prepare();
        }
        private DroppedItemBinding Acquire()
        {
            while(available.Count>0)
            {
                DroppedItemBinding binding=available.Pop();
                if(binding!=null)return binding;
            }
            var model=new GameObject("CultCraft small 3D native pickup");
            return model.AddComponent<DroppedItemBinding>();
        }
        private void Recycle(DroppedItemBinding binding)
        {
            if(binding==null)return;
            binding.Unbind();
            if(available.Count<PoolCapacity)available.Push(binding);
            else UnityEngine.Object.Destroy(binding.gameObject);
        }
        public void Restore(SpriteRenderer source)
        {
            if(source==null)return;DroppedItemBinding binding;
            if(bindings.TryGetValue(source,out binding)){bindings.Remove(source);ordered.Remove(binding);Recycle(binding);}
        }
        private void Prepare(Camera camera)
        {
            if(disposed||camera==null||camera.cameraType!=CameraType.Game)return;
            for(int i=ordered.Count-1;i>=0;i--)
            {
                DroppedItemBinding binding=ordered[i];
                if(binding==null||binding.Source==null)
                {
                    if(!ReferenceEquals(binding,null)&&!ReferenceEquals(binding.Source,null)&&bindings.TryGetValue(binding.Source,out DroppedItemBinding current)&&ReferenceEquals(current,binding))
                        bindings.Remove(binding.Source);
                    Recycle(binding);
                    ordered.RemoveAt(i);
                    continue;
                }
                binding.Prepare();
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;Camera.onPreCull-=Prepare;
            foreach(DroppedItemBinding binding in ordered)if(binding!=null){binding.Unbind();UnityEngine.Object.Destroy(binding.gameObject);}
            foreach(DroppedItemBinding binding in available)if(binding!=null)UnityEngine.Object.Destroy(binding.gameObject);
            available.Clear();ordered.Clear();bindings.Clear();
            foreach(Geometry value in geometry.Values){UnityEngine.Object.Destroy(value.Mesh);UnityEngine.Object.Destroy(value.Material);}geometry.Clear();
        }
    }

    [RequireComponent(typeof(MeshFilter),typeof(MeshRenderer))]
    public sealed class DroppedItemBinding : MonoBehaviour
    {
        public SpriteRenderer Source { get; private set; }
        private bool nativeForceOff;
        private Sprite sprite;
        private MeshFilter filter;
        private MeshRenderer renderer;
        private Bounds? rewardBounds;
        private int lastFrame=-1;
        private int preparedFrame = -1;
        private PickupFirstDrawProbe probe;
        private Quaternion firstDrawRotation;
        private static readonly Quaternion Upright = Quaternion.Euler(-90f, 0f, 0f);
        private static readonly Quaternion RewardAngle = Quaternion.Euler(-25f, 45f, 0f);
        private static readonly float SpinSpeed = Spec.Visual("pickup_spin_speed").Value;
        public int GameDrawFrames { get; private set; }
        public bool SpinObserved { get; private set; }
        public float WorldSize { get; private set; }
        public bool HasVolume { get { return filter!=null&&filter.sharedMesh!=null&&filter.sharedMesh.bounds.size.z>0.001f; } }
        public bool IsCube { get { return HasVolume&&filter.sharedMesh.bounds.size.z>=filter.sharedMesh.bounds.size.x*0.99f; } }
        public bool CubeAtlasMatches(Texture source,Texture atlas)
        {return IsCube&&sprite!=null&&sprite.texture==source&&renderer!=null&&renderer.sharedMaterial.mainTexture==atlas&&atlas!=null&&atlas.width==source.width&&atlas.height==source.height*(rewardBounds.HasValue?4:3);}
        public bool NativeArtSuppressed { get { return Source!=null&&Source.forceRenderingOff; } }
        private void Awake()
        {
            filter=GetComponent<MeshFilter>();
            renderer=GetComponent<MeshRenderer>();
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
        }
        internal void Initialise(SpriteRenderer source,bool testDraws)
        {
            Source=source;
            nativeForceOff=source.forceRenderingOff;
            gameObject.layer=source.gameObject.layer;
            GameDrawFrames=0;
            SpinObserved=false;
            lastFrame=-1;
            if(testDraws)
            {
                probe=source.GetComponent<PickupFirstDrawProbe>();
                if(!TryGetComponent<DroppedItemDrawProbe>(out var observer))observer=gameObject.AddComponent<DroppedItemDrawProbe>();
                observer.Binding=this;
            }
        }
        public void Bind(Sprite value,Mesh mesh,Material material,Bounds? bounds)
        {
            if(sprite==value&&filter.sharedMesh==mesh&&renderer.sharedMaterial==material&&Nullable.Equals(rewardBounds,bounds))return;
            sprite=value;filter.sharedMesh=mesh;renderer.sharedMaterial=material;
            rewardBounds=bounds;
            if(bounds.HasValue)
            {
                transform.SetParent(Source.transform,false);
                transform.localPosition=bounds.Value.center;
                transform.localRotation=RewardAngle;
                // The diagonal of the cube fits inside a compact square even
                // when the native reward reveal scales up the whole display.
                transform.localScale=Vector3.one*(Mathf.Min(0.49f,bounds.Value.size.x,bounds.Value.size.y)/Mathf.Sqrt(3f));
            }
            else
            {
                transform.SetParent(null,false);
                Vector3 size=mesh.bounds.size;
                WorldSize=MinecraftAvatar.BodyHeight*Spec.Visual("pickup_height_fraction").Value;
                float worldScale=WorldSize/Mathf.Max(0.001f,Mathf.Max(size.x,size.y));
                transform.localScale=Vector3.one*worldScale;
            }
            preparedFrame=-1;
        }
        public void Prepare()
        {
            if (preparedFrame == Time.frameCount) return;
            preparedFrame = Time.frameCount;
            if(Source==null){renderer.enabled=false;return;}
            bool visible=Source.enabled&&Source.gameObject.activeInHierarchy&&Source.color.a>0.001f;
            if(renderer.enabled!=visible)renderer.enabled=visible;
            if(!visible)return;
            if(!Source.forceRenderingOff)Source.forceRenderingOff=true;
            if(!rewardBounds.HasValue)
            {
                float yaw = Mathf.Repeat(Time.time * SpinSpeed, 360f);
                transform.SetPositionAndRotation(Source.transform.position,Quaternion.AngleAxis(yaw,Vector3.back)*Upright);
            }
            int layer=Source.sortingLayerID,order=Source.sortingOrder;
            if(renderer.sortingLayerID!=layer)renderer.sortingLayerID=layer;
            if(renderer.sortingOrder!=order)renderer.sortingOrder=order;
        }
        internal void ObserveDraw(Camera camera)
        {
            if(camera==null||camera.cameraType!=CameraType.Game||Source==null||sprite==null||!HasVolume)return;
            if(renderer.sharedMaterial.mainTexture!=sprite.texture&&!CubeAtlasMatches(sprite.texture,renderer.sharedMaterial.mainTexture))return;
            if(lastFrame!=Time.frameCount)
            {
                if (GameDrawFrames == 0) firstDrawRotation = transform.rotation;
                if (Quaternion.Angle(firstDrawRotation, transform.rotation) > 8f &&
                    Vector3.Dot(transform.up, Vector3.back) > 0.999f) SpinObserved = true;
                lastFrame=Time.frameCount;GameDrawFrames++;
            }
            if(probe!=null)probe.ObserveMeshDraw(camera,sprite,renderer.sharedMaterial.mainTexture);
        }
        internal void Unbind()
        {
            if(Source!=null)Source.forceRenderingOff=nativeForceOff;
            renderer.enabled=false;
            filter.sharedMesh=null;
            renderer.sharedMaterial=null;
            transform.SetParent(null,false);
            Source=null;
            sprite=null;
            probe=null;
            rewardBounds=null;
            WorldSize=0f;
        }
    }

    internal sealed class DroppedItemDrawProbe : MonoBehaviour
    {
        internal DroppedItemBinding Binding;
        private void OnWillRenderObject(){Binding.ObserveDraw(Camera.current);}
    }
}
