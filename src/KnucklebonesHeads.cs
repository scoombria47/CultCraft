using System;
using System.Collections.Generic;
using HarmonyLib;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;
using src.UI.Menus;

namespace CultCraft
{
    // Native KBPlayer owns both the player identity and all animation timing.
    internal sealed class KnucklebonesHeads : IDisposable
    {
        private static KnucklebonesHeads current;
        private readonly PlayerSkins skins;
        private readonly MinecraftAssets assets;
        private readonly Harmony harmony=new Harmony("cultcraft.knucklebones.heads");
        private readonly Dictionary<KBPlayer,Binding> bindings=new Dictionary<KBPlayer,Binding>();
        private readonly List<KBPlayer> removed=new List<KBPlayer>();
        private static readonly System.Reflection.FieldInfo NativeGraphic=AccessTools.Field(typeof(KBPlayer),"_lambSpine"),NativeOwner=AccessTools.Field(typeof(KBPlayer),"playerFarming");
        internal int DrawFrames,Reactions;
        internal readonly HashSet<string> ObservedEmotions=new HashSet<string>();
        internal KnucklebonesHeads(MinecraftAssets assets,PlayerSkins skins)
        {
            this.assets=assets;this.skins=skins;current=this;
            var configure=AccessTools.Method(typeof(KBPlayer),"Configure",new[]{typeof(PlayerFarming),typeof(Vector2),typeof(Vector2)});
            harmony.Patch(configure,postfix:new HarmonyMethod(typeof(KnucklebonesHeads),nameof(Configured)));
            foreach(string name in new[]{"OnDiceMatched","OnDiceLost","SetWonLoop","SetLostLoop"})
                harmony.Patch(AccessTools.Method(typeof(KBPlayerBase),name),postfix:new HarmonyMethod(typeof(KnucklebonesHeads),nameof(Reacted)));
        }
        private static void Configured(KBPlayer __instance,Vector2 __1)
        {
            // The native offset describes the seat, independent of the character skin.
            if(current!=null)current.Bind(__instance,__1.x>0f?-1f:1f);
        }
        private static void Reacted(KBPlayerBase __instance,System.Reflection.MethodBase __originalMethod)
        {
            KBPlayer player=__instance as KBPlayer;if(current==null||player==null)return;
            Binding binding=current.For(player);if(binding==null)return;
            string emotion=__originalMethod.Name=="OnDiceMatched"?"happy":__originalMethod.Name=="OnDiceLost"?"angry":__originalMethod.Name=="SetWonLoop"?"win":"lose";
            binding.Effect.Burst(emotion);current.Reactions++;current.ObservedEmotions.Add(emotion);
        }
        internal Binding For(KBPlayer player){Binding b;return player!=null&&bindings.TryGetValue(player,out b)?b:null;}
        private void Bind(KBPlayer player,float facing)
        {
            Binding b;
            if(bindings.TryGetValue(player,out b)){b.Facing=facing;return;}
            SkeletonGraphic source=NativeGraphic.GetValue(player) as SkeletonGraphic;
            PlayerFarming owner=NativeOwner.GetValue(player) as PlayerFarming;
            if(source==null||owner==null||source.Skeleton==null)return;
            b=new Binding(this,source,PlayerSkins.Role(owner),facing);bindings.Add(player,b);
        }
        internal void LateTick()
        {
            removed.Clear();foreach(var pair in bindings)
            {if(pair.Key==null||pair.Value.Source==null){pair.Value.Dispose();removed.Add(pair.Key);}else pair.Value.Tick(skins.Get(pair.Value.Role));}
            foreach(var key in removed)bindings.Remove(key);
        }
        public void Dispose(){harmony.UnpatchSelf();foreach(var b in bindings.Values)b.Dispose();bindings.Clear();if(current==this)current=null;}

        internal sealed class Binding : IDisposable
        {
            internal readonly SkeletonGraphic Source;
            internal readonly int Role;
            internal readonly MinecraftHeadGraphic Head;
            internal readonly HeadReactionGraphic Effect;
            internal float Facing;
            private readonly Color original;
            private readonly Bone head,restHead;
            private readonly KnucklebonesHeads owner;
            private float[] boundsScratch;
            private bool disposed;
            internal Binding(KnucklebonesHeads owner,SkeletonGraphic source,int role,float facing)
            {
                this.owner=owner;Source=source;Role=role;Facing=facing;original=source.color;
                head=source.Skeleton.FindBone("HEAD")??source.Skeleton.RootBone;
                var rest=new Skeleton(source.Skeleton.Data);rest.UpdateWorldTransform();restHead=rest.FindBone(head.Data.Name)??rest.RootBone;
                var obj=new GameObject("CultCraft skinned Knucklebones head",typeof(RectTransform),typeof(CanvasRenderer),typeof(MinecraftHeadGraphic));
                obj.transform.SetParent(source.transform.parent,false);obj.transform.SetSiblingIndex(source.transform.GetSiblingIndex()+1);
                Head=obj.GetComponent<MinecraftHeadGraphic>();Head.raycastTarget=false;Head.Rendered=()=>owner.DrawFrames++;
                var effects=new GameObject("CultCraft native Knucklebones emotions",typeof(RectTransform),typeof(CanvasRenderer),typeof(HeadReactionGraphic));
                effects.transform.SetParent(obj.transform,false);Effect=effects.GetComponent<HeadReactionGraphic>();Effect.raycastTarget=false;Effect.Assets=owner.assets;
                Effect.rectTransform.anchorMin=Vector2.zero;Effect.rectTransform.anchorMax=Vector2.one;Effect.rectTransform.sizeDelta=Vector2.zero;
            }
            internal void Tick(PlayerSkins.Choice choice)
            {
                if(disposed||Source==null)return;
                bool visible=Source.gameObject.activeInHierarchy&&Source.enabled;
                Head.gameObject.SetActive(visible);if(!visible)return;
                if(Source.color.a>0f){Color c=Source.color;c.a=0f;Source.color=c;}
                Head.SetSkin(choice.Texture);
                var canvas=Source.canvas;
                var rect=Head.rectTransform;
                // The right-hand seat mirrors its native artwork. Cancel that reflection for the cube and skin UVs.
                if(canvas!=null)
                {
                    Vector3 parentRight=rect.parent.TransformVector(Vector3.right);
                    float sign=Vector3.Dot(parentRight,canvas.rootCanvas.transform.right)<0f?-1f:1f;
                    if(rect.localScale.x!=sign)rect.localScale=new Vector3(sign,1f,1f);
                }
                float x,y,w,h;
                Source.Skeleton.GetBounds(out x,out y,out w,out h,ref boundsScratch);
                // SkeletonGraphic scales native mesh vertices before drawing them on its canvas.
                float pixelsPerUnit=canvas==null?1f:canvas.referencePixelsPerUnit;
                bool validBounds=w>0f&&h>0f&&!float.IsInfinity(w)&&!float.IsInfinity(h);
                Vector3 center=validBounds?new Vector3(x+w*0.5f,y+h*0.5f,0f):new Vector3(head.WorldX,head.WorldY,0f);
                rect.position=Source.transform.TransformPoint(center*pixelsPerUnit);
                float size=validBounds?Mathf.Clamp(Mathf.Max(w,h)*pixelsPerUnit*Mathf.Abs(Source.transform.localScale.y)*0.8f,24f,400f):120f;
                rect.sizeDelta=Vector2.one*size;
                float dx=head.WorldX-restHead.WorldX,dy=head.WorldY-restHead.WorldY;
                float unit=validBounds?Mathf.Max(w,h):1f;
                float roll=Mathf.Clamp(Mathf.DeltaAngle(restHead.WorldRotationX,head.WorldRotationX),-40f,40f);
                var track=Source.AnimationState==null?null:Source.AnimationState.GetCurrent(0);
                Head.DamageTintActive=PlaysAnger(track);
                float phase=track==null||track.Animation==null?0f:track.AnimationTime/Mathf.Max(0.01f,track.Animation.Duration);
                float depth=track!=null&&track.Animation!=null&&!track.Animation.Name.EndsWith("/idle",StringComparison.Ordinal)?Mathf.Sin(phase*Mathf.PI*2f):0f;
                float pitch=5f+Mathf.Clamp(dy/unit*90f+(head.ScaleY-1f)*25f,-10f,10f)+depth*5f;
                float yaw=Facing*(30f+Mathf.Clamp(dx/unit*100f,-14f,14f)+depth*7f);
                Head.Rotation=Quaternion.Euler(pitch,yaw,roll+depth*3f);
                Head.SetVerticesDirty();Effect.SetVerticesDirty();
            }
            private static bool PlaysAnger(TrackEntry track)
            {
                while(track!=null)
                {
                    string clip=track.Animation?.Name;
                    if(clip!=null&&(clip.EndsWith("/lose-dice",StringComparison.Ordinal)||clip.EndsWith("/lose-game",StringComparison.Ordinal)))return true;
                    // Keep the color during the final blend to the normal-sized idle or defeat loop.
                    track=track.MixTime<track.MixDuration?track.MixingFrom:null;
                }
                return false;
            }
            public void Dispose(){if(disposed)return;disposed=true;if(Source!=null)Source.color=original;if(Head!=null)UnityEngine.Object.Destroy(Head.gameObject);}
        }
    }

    // Rotated, depth-sorted cube faces are projected directly into the native Canvas.
    // This keeps masking, menu fades, resolution scaling and input hit areas native.
    internal sealed class MinecraftHeadGraphic : MaskableGraphic
    {
        private Texture2D skin;
        private Mesh mesh;
        internal Quaternion Rotation=Quaternion.identity;
        internal Action Rendered;
        internal int Draws;
        internal bool DamageTintActive;
        internal bool LastDrawHadDamageTint {get;private set;}
        public override Texture mainTexture {get{return skin??Texture2D.whiteTexture;}}
        internal void SetSkin(Texture2D value)
        {
            if(value==null||skin==value)return;skin=value;
            if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            mesh=MinecraftAvatar.GenerateSkinPart(skin,Spec.Avatar("head"));SetMaterialDirty();SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(mesh==null)return;
            LastDrawHadDamageTint=DamageTintActive;
            Color tint=LastDrawHadDamageTint?color*MinecraftAvatar.DamageTintColor:color;
            Vector3[] vertices=mesh.vertices;Vector2[] uv=mesh.uv;Color[] colors=mesh.colors;
            var faces=new List<Tuple<float,int,Vector3[]>>();
            for(int i=0;i+3<vertices.Length;i+=4)
            {
                var points=new Vector3[4];float depth=0f;
                for(int j=0;j<4;j++){points[j]=Rotation*(vertices[i+j]-Vector3.up*4f*MinecraftAvatar.PixelSize);depth+=points[j].z;}
                if(Vector3.Cross(points[1]-points[0],points[2]-points[0]).z<=0f)continue;
                faces.Add(Tuple.Create(depth,i,points));
            }
            faces.Sort((a,b)=>a.Item1.CompareTo(b.Item1));
            float scale=rectTransform.rect.width/(8f*MinecraftAvatar.PixelSize)*0.8f;
            foreach(var face in faces)
            {
                int start=vh.currentVertCount;
                for(int j=0;j<4;j++)
                {
                    Vector3 p=face.Item3[j];float perspective=1f/(1f-p.z*0.4f);
                    var v=UIVertex.simpleVert;v.position=new Vector3(p.x*scale*perspective,p.y*scale*perspective,0f);v.uv0=uv[face.Item2+j];v.color=(colors.Length>face.Item2+j?colors[face.Item2+j]:Color.white)*tint;
                    vh.AddVert(v);
                }
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
            if(vh.currentVertCount>0){Draws++;if(Rendered!=null)Rendered();}
        }
        protected override void OnDestroy(){if(mesh!=null)UnityEngine.Object.Destroy(mesh);base.OnDestroy();}
    }

    internal sealed class HeadReactionGraphic : MaskableGraphic
    {
        internal MinecraftAssets Assets;
        private string emotion;
        private float since=-10f;
        private Texture2D texture;
        internal float Alpha {get {float age=Time.unscaledTime-since;return age<1f?1f:Mathf.Clamp01(1f-(age-1f)/0.6f);}}
        internal string Emotion {get{return emotion;}}
        public override Texture mainTexture {get{return texture??Texture2D.whiteTexture;}}
        internal void Burst(string name)
        {
            emotion=name;since=Time.unscaledTime;
            texture=name=="angry"?Assets.GrayTexture("kb_angry"):Assets.Texture(name=="happy"?"kb_happy":name=="win"?"kb_heart":"kb_crit");
            SetMaterialDirty();SetVerticesDirty();
        }
        private void Update(){if(Alpha>0f)SetVerticesDirty();else if(canvasRenderer.GetAlpha()>0f){canvasRenderer.SetAlpha(0f);SetVerticesDirty();}}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(texture==null||Alpha<=0f)return;canvasRenderer.SetAlpha(1f);
            float age=Time.unscaledTime-since,size=rectTransform.rect.width;
            Color tint=emotion=="lose"?new Color(0.85f,0.74f,0.6f):Color.white;
            tint.a=Alpha;
            for(int i=0;i<7;i++)
            {
                float angle=(i*137.5f+15f)*Mathf.Deg2Rad;
                Vector2 p=new Vector2(Mathf.Cos(angle)*size*(0.42f+0.05f*(i%2)),Mathf.Sin(angle)*size*0.3f+size*0.25f+age*size*0.08f);
                float half=size*0.11f;int start=vh.currentVertCount;
                Vector2[] corner={new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
                for(int k=0;k<4;k++){var v=UIVertex.simpleVert;v.position=p+corner[k]*half;v.uv0=(corner[k]+Vector2.one)*0.5f;v.color=tint;vh.AddVert(v);}
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
    }
}
