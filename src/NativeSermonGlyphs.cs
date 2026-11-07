using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;

namespace CultCraft
{
    // Replace only the twelve audited native text strips, leaving their animation and every other slot intact.
    public sealed class NativeSermonGlyphs : IDisposable
    {
        private struct Pixel {public float X,Y,Width,Height;public Vector2 Uv;}
        private sealed class Source
        {
            public SkeletonAnimation Native;public Renderer NativeRenderer;public GameObject Visual;public Mesh Mesh;public MeshRenderer Renderer;
            public readonly List<Slot> Letters=new List<Slot>();
            public readonly Dictionary<Slot,Material> Originals=new Dictionary<Slot,Material>();
            public readonly HashSet<Slot> HadOriginal=new HashSet<Slot>();
            public readonly Dictionary<Slot,Material> OtherOverrides=new Dictionary<Slot,Material>();
            public readonly HashSet<Slot> OtherPresent=new HashSet<Slot>();
            public readonly List<Vector3> Vertices=new List<Vector3>();public readonly List<Vector2> Uv=new List<Vector2>();
            public readonly List<Color32> Colors=new List<Color32>();public readonly List<int> Triangles=new List<int>();
            public readonly float[] Corners=new float[8];public bool Applied;
        }
        private static NativeSermonGlyphs current;
        private int references=1,lastTickFrame=-1;
        internal static NativeSermonGlyphs Acquire(MinecraftAssets assets)
        {if(current!=null&&!current.disposed){current.references++;return current;}return new NativeSermonGlyphs(assets);}
        private readonly Harmony harmony=new Harmony("cultcraft.book.glyphs");
        private readonly List<Source> sources=new List<Source>();
        private readonly Dictionary<char,List<Pixel>> letters=new Dictionary<char,List<Pixel>>();
        private readonly Material hidden,glyphMaterial;
        private ChurchFollowerManager lastManager;
        private bool disposed;private int lastDrawFrame=-1;
        public int ReplacedCount {get;private set;}
        public int GameDrawFrames {get;private set;}
        public bool PreservedNonGlyphSlots {get;private set;}=true;
        public int OpaqueGlyphPixels {get;private set;}

        public NativeSermonGlyphs(MinecraftAssets assets)
        {
            Texture2D texture=assets.Texture("enchanting_glyphs");
            JObject font=JObject.Parse(assets.ReadText(Spec.Asset("enchanting_font_model").Entry));
            foreach(JToken provider in (JArray)font["providers"])
            {
                if((string)provider["type"]!="bitmap" || ((string)provider["file"]??"").Replace("minecraft:","")!="font/ascii_sga.png")continue;
                JArray rows=(JArray)provider["chars"];int ch=texture.height/rows.Count;Color32[] pixels=texture.GetPixels32();
                for(int row=0;row<rows.Count;row++){string chars=(string)rows[row];int cw=texture.width/chars.Length;
                    for(int cell=0;cell<chars.Length;cell++){char key=chars[cell];if(key<'A'||key>'Z')continue;var data=new List<Pixel>();
                        for(int y=0;y<ch;y++)for(int x=0;x<cw;x++){int tx=cell*cw+x,ty=texture.height-1-(row*ch+y);
                            if(pixels[ty*texture.width+tx].a<128)continue;
                            data.Add(new Pixel{X=(float)x/cw,Y=1f-(float)(y+1)/ch,Width=1f/cw,Height=1f/ch,Uv=new Vector2((tx+0.5f)/texture.width,(ty+0.5f)/texture.height)});}
                        if(data.Count==0)throw new InvalidOperationException("Installed galactic font has an empty letter: "+key);
                        letters[key]=data;OpaqueGlyphPixels+=data.Count;
                    }
                }
            }
            if(letters.Count!=26)throw new InvalidOperationException("Installed Minecraft galactic font provider is incomplete.");
            Shader shader=Shader.Find("Sprites/Default");if(shader==null||!shader.isSupported)throw new InvalidOperationException("Native galactic glyph material unavailable.");
            glyphMaterial=new Material(shader){name="CultCraft original Minecraft galactic font",mainTexture=texture,color=Color.white};
            hidden=new Material(shader){name="CultCraft only native sermon text hidden",mainTexture=texture,color=Color.clear};
            current=this;
            try {
                foreach(string method in new[]{"StartSermonEffect","StartSermonEffectClean","PlayEffect"})
                    harmony.Patch(AccessTools.Method(typeof(ChurchFollowerManager),method),postfix:new HarmonyMethod(typeof(NativeSermonGlyphs),"NativeStarted"));
            }catch{harmony.UnpatchSelf();UnityEngine.Object.Destroy(hidden);UnityEngine.Object.Destroy(glyphMaterial);if(current==this)current=null;throw;}
        }
        private static void NativeStarted(ChurchFollowerManager __instance){Observe(__instance.SermonEffect);}
        public static bool Observe(SkeletonAnimation effect){return current!=null&&!current.disposed&&current.Register(effect);}
        private static bool IsLetter(Slot slot)
        {
            const string prefix="images/Sermon_Line_";string name=slot.Data.Name;int n;
            return name.StartsWith(prefix,StringComparison.Ordinal)&&Int32.TryParse(name.Substring(prefix.Length),out n)&&n>=1&&n<=12;
        }
        private bool Register(SkeletonAnimation effect)
        {
            if(effect==null||effect.Skeleton==null||effect.Skeleton.Data.FindAnimation("sermons/1-loop")==null)return false;
            foreach(Source known in sources)if(known.Native==effect)return true;
            var source=new Source{Native=effect,NativeRenderer=effect.GetComponent<Renderer>()};
            foreach(Slot slot in effect.Skeleton.Slots)
            {
                Material original;bool had=effect.CustomSlotMaterials.TryGetValue(slot,out original);
                if(IsLetter(slot)){source.Letters.Add(slot);source.Originals[slot]=original;if(had)source.HadOriginal.Add(slot);}
                else{source.OtherOverrides[slot]=original;if(had)source.OtherPresent.Add(slot);}
            }
            if(source.Letters.Count!=12)return false;
            source.Visual=new GameObject("CultCraft actual native sermon text in Minecraft galactic font");
            source.Visual.layer=effect.gameObject.layer;
            source.Mesh=new Mesh{name="CultCraft native sermon glyph strips"};source.Mesh.MarkDynamic();
            source.Visual.AddComponent<MeshFilter>().sharedMesh=source.Mesh;source.Renderer=source.Visual.AddComponent<MeshRenderer>();source.Renderer.sharedMaterial=glyphMaterial;
            source.Renderer.shadowCastingMode=ShadowCastingMode.Off;source.Renderer.receiveShadows=false;
            source.Visual.AddComponent<BookRenderProbe>().Observed=ObservedDraw;source.Visual.SetActive(false);sources.Add(source);return true;
        }
        private void ObservedDraw(Camera camera)
        {if(camera!=null&&camera.cameraType==CameraType.Game&&lastDrawFrame!=Time.frameCount){lastDrawFrame=Time.frameCount;GameDrawFrames++;}}
        public static BookParticles CreateBookParticles(Transform parent)
        {return current==null||current.disposed?null:new BookParticles(current,parent);}

        // Small enchant letters entering the pages are separate from the seven solid model parts.
        public sealed class BookParticles : IDisposable
        {
            private readonly NativeSermonGlyphs owner;
            private readonly GameObject root;
            private readonly Mesh mesh;
            private readonly MeshRenderer renderer;
            private readonly List<Vector3> vertices=new List<Vector3>();
            private readonly List<Vector2> uv=new List<Vector2>();
            private readonly List<Color32> colors=new List<Color32>();
            private readonly List<int> triangles=new List<int>();
            private int lastFrame=-1;
            public int GameDrawFrames {get;private set;}
            internal BookParticles(NativeSermonGlyphs owner,Transform parent)
            {
                this.owner=owner;root=new GameObject("CultCraft incoming Minecraft enchant letters");root.transform.SetParent(parent,false);
                mesh=new Mesh{name="CultCraft native-clock Minecraft enchant letters"};mesh.MarkDynamic();
                root.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=owner.glyphMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                root.AddComponent<BookRenderProbe>().Observed=Drawn;root.SetActive(false);
            }
            private void Drawn(Camera camera)
            {if(camera!=null&&camera.cameraType==CameraType.Game&&lastFrame!=Time.frameCount){lastFrame=Time.frameCount;GameDrawFrames++;}}
            public void SetSorting(int layer,int order){renderer.sortingLayerID=layer;renderer.sortingOrder=order;}
            public void Tick(bool visible,float time,float open)
            {
                root.SetActive(visible);if(!visible)return;
                vertices.Clear();uv.Clear();colors.Clear();triangles.Clear();int index=0;
                Camera camera=Camera.main;
                Vector3 right=camera!=null?root.transform.InverseTransformDirection(camera.transform.right).normalized:Vector3.right;
                Vector3 up=camera!=null?root.transform.InverseTransformDirection(camera.transform.up).normalized:Vector3.up;
                for(int letter=0;letter<8;letter++)
                {
                    float phase=Mathf.Repeat(time*0.65f+letter/8f,1f),angle=letter*Mathf.PI/4f;
                    Vector3 start=new Vector3(Mathf.Sin(angle)*0.65f,0.32f+Mathf.Cos(angle)*0.12f,Mathf.Cos(angle)*0.35f);
                    Vector3 position=Vector3.Lerp(start,new Vector3(0.03f,0f,-0.02f),phase);position.y+=Mathf.Sin(phase*Mathf.PI)*0.08f;
                    float alpha=Mathf.SmoothStep(0f,1f,Mathf.Min(phase/0.12f,(1f-phase)/0.2f))*open;
                    Color32 color=new Color32(255,255,255,(byte)Mathf.RoundToInt(alpha*255f));
                    foreach(Pixel pixel in owner.letters[(char)('A'+letter*3)])
                    {
                        Vector3 a=position+(right*(pixel.X-0.5f)+up*(pixel.Y-0.5f))*0.045f;
                        Vector3 dx=right*(pixel.Width*0.045f),dy=up*(pixel.Height*0.045f);
                        vertices.Add(a);vertices.Add(a+dx);vertices.Add(a+dx+dy);vertices.Add(a+dy);
                        for(int n=0;n<4;n++){uv.Add(pixel.Uv);colors.Add(color);}
                        triangles.Add(index);triangles.Add(index+1);triangles.Add(index+2);triangles.Add(index);triangles.Add(index+2);triangles.Add(index+3);index+=4;
                    }
                }
                mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            }
            public void Dispose(){UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(mesh);}
        }
        private void Apply(Source source)
        {
            if(source.Applied)return;foreach(Slot slot in source.Letters)source.Native.CustomSlotMaterials[slot]=hidden;source.Applied=true;
        }
        private void Restore(Source source)
        {
            if(!source.Applied)return;
            if(source.Native!=null)foreach(Slot slot in source.Letters)
            {if(source.HadOriginal.Contains(slot))source.Native.CustomSlotMaterials[slot]=source.Originals[slot];else source.Native.CustomSlotMaterials.Remove(slot);}
            source.Applied=false;
        }
        private static Vector3 QuadPoint(Vector3 bl,Vector3 br,Vector3 ul,Vector3 ur,float x,float y)
        {return Vector3.LerpUnclamped(Vector3.LerpUnclamped(bl,br,x),Vector3.LerpUnclamped(ul,ur,x),y);}
        public void Tick(bool reading,bool firstPerson)
        {
            if(disposed||lastTickFrame==Time.frameCount)return;lastTickFrame=Time.frameCount;ReplacedCount=0;PreservedNonGlyphSlots=true;
            if(ChurchFollowerManager.Instance!=lastManager){lastManager=ChurchFollowerManager.Instance;if(lastManager!=null)Register(lastManager.SermonEffect);}
            for(int i=sources.Count-1;i>=0;i--)
            {
                Source source=sources[i];if(source.Native==null){Release(source);sources.RemoveAt(i);continue;}
                source.Visual.layer=source.Native.gameObject.layer;
                TrackEntry track=source.Native.AnimationState==null?null:source.Native.AnimationState.GetCurrent(0);
                string animation=track==null||track.Animation==null?"":track.Animation.Name;
                bool nativeActive=source.Native.gameObject.activeInHierarchy&&source.Native.enabled;
                bool nativeSermon=track!=null&&animation.StartsWith("sermons/",StringComparison.Ordinal)&&(track.Loop||track.TrackTime<track.Animation.Duration);
                bool active=nativeActive&&nativeSermon;
                if(!active){source.Visual.SetActive(false);Restore(source);continue;}
                Apply(source);
                source.Vertices.Clear();source.Uv.Clear();source.Colors.Clear();source.Triangles.Clear();int index=0;
                foreach(Slot slot in source.Letters)
                {
                    RegionAttachment region=slot.Attachment as RegionAttachment;
                    if(region==null||slot.A*region.A*source.Native.Skeleton.A<0.002f||!slot.Bone.Active)continue;
                    region.ComputeWorldVertices(slot.Bone,source.Corners,0,2);
                    Vector3 bl=source.Native.transform.TransformPoint(source.Corners[0],source.Corners[1],0f),ul=source.Native.transform.TransformPoint(source.Corners[2],source.Corners[3],0f);
                    Vector3 ur=source.Native.transform.TransformPoint(source.Corners[4],source.Corners[5],0f),br=source.Native.transform.TransformPoint(source.Corners[6],source.Corners[7],0f);
                    float height=(ul-bl).magnitude,width=(br-bl).magnitude;if(height<0.001f||width<0.001f)continue;
                    // Minecraft enchant particles face the viewing camera. Keep the native
                    // animated strip center and size while making it readable in first person.
                    Camera camera=firstPerson?Camera.main:null;
                    if(camera!=null){Vector3 center=(bl+br+ul+ur)*0.25f,right=camera.transform.right*(width*0.5f),up=camera.transform.up*(height*0.5f);
                        bl=center-right-up;br=center+right-up;ul=center-right+up;ur=center+right+up;}
                    int count=Mathf.Clamp(Mathf.RoundToInt(width/height),3,12);
                    Color32 color=new Color(slot.R*region.R,slot.G*region.G,slot.B*region.B,slot.A*region.A*source.Native.Skeleton.A);
                    for(int cell=0;cell<count;cell++)foreach(Pixel pixel in letters[(char)('A'+(slot.Data.Index*11+cell*7)%26)])
                    {
                        float x0=(cell+pixel.X)/count,x1=x0+pixel.Width/count,y0=pixel.Y,y1=y0+pixel.Height;
                        source.Vertices.Add(QuadPoint(bl,br,ul,ur,x0,y0));source.Vertices.Add(QuadPoint(bl,br,ul,ur,x1,y0));
                        source.Vertices.Add(QuadPoint(bl,br,ul,ur,x1,y1));source.Vertices.Add(QuadPoint(bl,br,ul,ur,x0,y1));
                        for(int n=0;n<4;n++){source.Uv.Add(pixel.Uv);source.Colors.Add(color);}
                        source.Triangles.Add(index);source.Triangles.Add(index+1);source.Triangles.Add(index+2);
                        source.Triangles.Add(index);source.Triangles.Add(index+2);source.Triangles.Add(index+3);index+=4;
                    }
                    ReplacedCount++;
                }
                source.Mesh.Clear();source.Mesh.SetVertices(source.Vertices);source.Mesh.SetUVs(0,source.Uv);source.Mesh.SetColors(source.Colors);source.Mesh.SetTriangles(source.Triangles,0);source.Mesh.RecalculateBounds();
                if(source.NativeRenderer!=null){source.Renderer.sortingLayerID=source.NativeRenderer.sortingLayerID;source.Renderer.sortingOrder=source.NativeRenderer.sortingOrder;}
                source.Visual.SetActive(index>0);
                foreach(var pair in source.OtherOverrides){Material material;bool had=source.Native.CustomSlotMaterials.TryGetValue(pair.Key,out material);
                    if(had!=source.OtherPresent.Contains(pair.Key)||had&&material!=pair.Value)PreservedNonGlyphSlots=false;}
            }
        }
        private void Release(Source source){Restore(source);if(source.Visual!=null)UnityEngine.Object.Destroy(source.Visual);if(source.Mesh!=null)UnityEngine.Object.Destroy(source.Mesh);}
        public void Suspend(){foreach(Source source in sources){Restore(source);if(source.Visual!=null)source.Visual.SetActive(false);}ReplacedCount=0;}
        public void Dispose(){if(disposed)return;if(--references>0)return;disposed=true;harmony.UnpatchSelf();foreach(Source source in sources)Release(source);sources.Clear();UnityEngine.Object.Destroy(hidden);UnityEngine.Object.Destroy(glyphMaterial);if(current==this)current=null;}
    }
}
