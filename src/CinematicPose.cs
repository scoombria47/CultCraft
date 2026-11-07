using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace CultCraft
{
    internal sealed class CinematicPose : IDisposable
    {
        private SkeletonAnimation source;
        private Skeleton rest;
        private readonly Dictionary<string,Vector2> point=new Dictionary<string,Vector2>();
        private readonly Dictionary<string,float> angle=new Dictionary<string,float>();
        private static readonly string[] Names={"HIP","HEAD","BACK_BTM","SHOULDER_RIGHT","SHOULDER_LEFT","HAND_RIGHT","HAND_LEFT","LEG_TOP_RIGHT","LEG_TOP_LEFT","FOOT_RIGHT","FOOT_LEFT"};
        internal int Revision;
        internal string Clip="";
        private float scale=1f;
        internal void Bind(SkeletonAnimation next)
        {
            if(source==next)return;Dispose();source=next;if(source==null||source.Skeleton==null)return;
            rest=new Skeleton(source.Skeleton.Data);rest.UpdateWorldTransform();
            float import=source.skeletonDataAsset==null?1f:source.skeletonDataAsset.scale;
            scale=MinecraftAvatar.BodyHeight/Mathf.Max(0.01f,source.Skeleton.Data.Height*Mathf.Abs(import));
            source.OnMeshAndMaterialsUpdated+=Captured;
        }
        private void Captured(SkeletonRenderer renderer)
        {
            if(source==null||source.Skeleton==null)return;
            foreach(string name in Names)
            {
                Bone b=source.Skeleton.FindBone(name);if(b==null)continue;
                point[name]=new Vector2(b.WorldX,b.WorldY);angle[name]=b.WorldRotationX;
            }
            var track=source.AnimationState.GetCurrent(0);Clip=track==null||track.Animation==null?"":track.Animation.Name;Revision++;
        }
        private Vector2 Delta(string a,string b)
        {Vector2 x,y;return point.TryGetValue(a,out x)&&point.TryGetValue(b,out y)?x-y:Vector2.down;}
        private float Rotation(string name)
        {float value;Bone b=rest==null?null:rest.FindBone(name);return b!=null&&angle.TryGetValue(name,out value)?Mathf.DeltaAngle(b.WorldRotationX,value):0f;}
        internal void Apply(Transform root,Transform torso,Transform head,Transform rightArm,Transform leftArm,Transform rightLeg,Transform leftLeg)
        {
            if(Revision==0||rest==null)return;
            Vector2 hip;Bone rh=rest.FindBone("HIP");
            if(rh!=null&&point.TryGetValue("HIP",out hip))
            {
                Vector2 lift=(hip-new Vector2(rh.WorldX,rh.WorldY))*scale;
                Vector3 displacement=new Vector3(Mathf.Clamp(lift.x,-0.25f,0.25f),Mathf.Clamp(lift.y,-0.65f,2f),0f);
                torso.localPosition+=displacement;rightLeg.localPosition+=displacement;leftLeg.localPosition+=displacement;
            }
            float lean=Rotation("BACK_BTM");
            torso.localRotation=Quaternion.Euler(Mathf.Clamp(-lean*0.6f,-45f,45f),0f,Mathf.Clamp(lean*0.25f,-20f,20f));
            Vector2 look=Delta("HEAD","HIP");
            head.localRotation=Quaternion.Euler(Mathf.Clamp(-Rotation("HEAD")*0.4f,-35f,35f),Mathf.Clamp(look.x*scale*35f,-30f,30f),Mathf.Clamp(Rotation("HEAD"),-40f,40f));
            Limb(rightArm,Delta("HAND_RIGHT","SHOULDER_RIGHT"),1f,false);
            Limb(leftArm,Delta("HAND_LEFT","SHOULDER_LEFT"),-1f,false);
            Limb(rightLeg,Delta("FOOT_RIGHT","LEG_TOP_RIGHT"),1f,true);
            Limb(leftLeg,Delta("FOOT_LEFT","LEG_TOP_LEFT"),-1f,true);
        }
        private static void Limb(Transform limb,Vector2 native,float side,bool leg)
        {
            Vector2 n=native.sqrMagnitude>0.00001f?native.normalized:Vector2.down;
            Vector3 direction=new Vector3(n.x*(leg?0.25f:0.5f),n.y,Mathf.Abs(n.x)*(leg?0.7f:0.8f));
            limb.localRotation=Quaternion.FromToRotation(Vector3.down,direction.normalized)*Quaternion.AngleAxis(side*(leg?0f:3f),Vector3.forward);
        }
        public void Dispose(){if(!ReferenceEquals(source,null))source.OnMeshAndMaterialsUpdated-=Captured;source=null;rest=null;point.Clear();angle.Clear();Revision=0;Clip="";}
    }
}
