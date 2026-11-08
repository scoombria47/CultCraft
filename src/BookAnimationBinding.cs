using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace CultCraft
{
    // Reads the actual post-IK pose. No native animation, event, state, or attachment is replaced.
    public sealed class BookAnimationBinding : IDisposable
    {
        private static readonly Dictionary<Spine.Animation,bool> bookTracks=new Dictionary<Spine.Animation,bool>();
        private SkeletonAnimation source;
        private Skeleton skeleton,reference;
        private Slot book;
        private Bone hip,head,shoulderR,shoulderL,handR,handL,legR,legL,footR,footL;
        private Bone refHip,refHead,refTool,back,refBack;
        private Vector2 idleHead, idleShoulders, idleRightArm, idleLeftArm;
        private float idleHipY, idleHeadAngle;
        private Bone refFootR,refFootL;
        private readonly float[] corners=new float[512];
        private Vector2 center,rightDirection;
        private Vector2 poseHip,poseHead,poseShoulderR,poseShoulderL,poseHandR,poseHandL,poseLegR,poseLegL,poseFootR,poseFootL;
        private float poseHeadRotation;
        private float scale,lastTime=-1f,open;
        private TrackEntry previousTrack;
        private float poseBackRotation,animationTime,throwAt=-1f;
        private bool itemVisible;
        private readonly Slot[] cardSlots=new Slot[6];
        private readonly Vector2[] cardPositions=new Vector2[6];
        private readonly float[] cardAngles=new float[6];
        private readonly bool[] cardVisible=new bool[6];
        private AnimationPages pages;
        private PlayerFarming owner;
        private Quaternion lastPaperRotation;
        private float lastPaperTime=-1f;
        private string lastPaperTrack="";
        private readonly MinecraftAssets assets;
        public bool GestureActive {get;private set;}
        public string CeremonyClip {get;private set;}="";
        internal string BodyClip {get;private set;}="";
        public bool Grounded {get;private set;}
        public int PageGameDrawFrames {get{return pages==null?0:pages.GameDrawFrames;}}
        public bool PageThrown {get{return pages!=null&&pages.ThrowObserved;}}
        public bool CardsDrawn {get{return pages!=null&&pages.CardsObserved;}}
        public bool CardsHorizontal {get{return pages!=null&&pages.HorizontalOrbitObserved;}}
        public bool PageBookReceptionValid {get{return pages!=null&&pages.OpenReceptionFrames>=2&&!pages.ClosedBeforeArrival;}}
        public float RightReachError {get;private set;}
        private bool disposed;
        public bool Visible {get;private set;}
        public bool PoseActive {get;private set;}
        public bool PoseDriven {get;private set;}
        public int PoseRevision {get;private set;}
        public string AttachmentName {get;private set;}="";
        public string AnimationName {get;private set;}="";
        public float NativeTime {get;private set;}
        public float Openness {get{return open;}}
        public float PageA {get;private set;}
        public float PageB {get;private set;}
        public int ReplacedGlyphCount {get{return glyphs.ReplacedCount;}}
        public int GlyphGameDrawFrames {get{return glyphs.GameDrawFrames;}}
        public int GlyphOpaquePixels {get{return glyphs.OpaqueGlyphPixels;}}
        public bool NativeGlyphsPreserved {get{return glyphs.PreservedNonGlyphSlots;}}
        private readonly NativeSermonGlyphs glyphs;

        public BookAnimationBinding(MinecraftAssets assets){this.assets=assets;glyphs=NativeSermonGlyphs.Acquire(assets);}
        public void Bind(PlayerFarming player)
        {
            owner=player;
            SkeletonAnimation next=player==null?null:player.Spine;
            if(next==source && (next==null || ReferenceEquals(skeleton,next.Skeleton)))return;
            if(!ReferenceEquals(source,null))source.OnMeshAndMaterialsUpdated-=AfterNativePose;
            source=next;skeleton=source==null?null:source.Skeleton;reference=null;book=null;Visible=false;PoseActive=false;PoseDriven=false;CeremonyClip=BodyClip="";open=0f;previousTrack=null;lastTime=-1f;
            if(skeleton==null){glyphs.Suspend();return;}
            book=skeleton.FindSlot("BOOK");hip=skeleton.FindBone("HIP");head=skeleton.FindBone("HEAD");
            shoulderR=skeleton.FindBone("SHOULDER_RIGHT");shoulderL=skeleton.FindBone("SHOULDER_LEFT");handR=skeleton.FindBone("HAND_RIGHT");handL=skeleton.FindBone("HAND_LEFT");
            legR=skeleton.FindBone("LEG_TOP_RIGHT");legL=skeleton.FindBone("LEG_TOP_LEFT");footR=skeleton.FindBone("FOOT_RIGHT");footL=skeleton.FindBone("FOOT_LEFT");
            reference=new Skeleton(skeleton.Data);if(skeleton.Skin!=null)reference.SetSkin(skeleton.Skin);reference.UpdateWorldTransform();
            refHip=reference.FindBone("HIP");refHead=reference.FindBone("HEAD");refTool=reference.FindBone("TOOL");
            refFootR=reference.FindBone("FOOT_RIGHT");refFootL=reference.FindBone("FOOT_LEFT");
            back=skeleton.FindBone("BACK_BTM");refBack=reference.FindBone("BACK_BTM");
            CaptureIdleReference();
            for(int i=0;i<6;i++)cardSlots[i]=skeleton.FindSlot("Card_"+(i+1));
            // Spine's header bounds are unscaled; the actual bones already include
            // SkeletonDataAsset.scale. Retarget in that same physical coordinate space.
            float importScale=source.skeletonDataAsset!=null?source.skeletonDataAsset.scale:1f;
            scale=MinecraftAvatar.BodyHeight/Mathf.Max(0.001f,skeleton.Data.Height*Mathf.Abs(importScale));
            // This game's customized Spine declares UpdateComplete but never raises it.
            // Its renderer callback runs on the main thread after waiting for ProcessFrame,
            // including when the native Lamb's MeshRenderer is hidden by the avatar.
            source.OnMeshAndMaterialsUpdated+=AfterNativePose;
        }
        private void AfterNativePose(SkeletonRenderer renderer){Capture();}
        private void CaptureIdleReference()
        {
            // Use the authored neutral stance. The rig's bind pose has its
            // arms out to the sides and is not a useful resting reference.
            var idle=new Skeleton(skeleton.Data);
            if(skeleton.Skin!=null)idle.SetSkin(skeleton.Skin);
            idle.Data.FindAnimation("idle")?.Apply(idle,0f,0f,false,null,1f,MixBlend.Replace,MixDirection.In);
            idle.UpdateWorldTransform();
            Vector2 hipPoint=Rest(idle.FindBone("HIP"));
            Bone idleHeadBone=idle.FindBone("HEAD");
            Vector2 shoulderR=Rest(idle.FindBone("SHOULDER_RIGHT")),shoulderL=Rest(idle.FindBone("SHOULDER_LEFT"));
            idleHipY=hipPoint.y;
            idleHead=Rest(idleHeadBone)-hipPoint;
            idleHeadAngle=idleHeadBone?.Rotation??0f;
            idleShoulders=(shoulderR+shoulderL)*0.5f-hipPoint;
            idleRightArm=Rest(idle.FindBone("HAND_RIGHT"))-shoulderR;
            idleLeftArm=Rest(idle.FindBone("HAND_LEFT"))-shoulderL;
        }
        private static bool TrackHasBook(Spine.Animation animation,int slot)
        {
            if(animation==null)return false;bool found;if(bookTracks.TryGetValue(animation,out found))return found;
            foreach(Timeline timeline in animation.Timelines){AttachmentTimeline attachment=timeline as AttachmentTimeline;
                if(attachment==null || attachment.SlotIndex!=slot)continue;
                foreach(string name in attachment.AttachmentNames)if(IsBookAttachment(name)){bookTracks[animation]=true;return true;}}
            bookTracks[animation]=false;return false;
        }
        private static bool IsBookAttachment(string name){return !String.IsNullOrEmpty(name)&&name.IndexOf("Tools/Book",StringComparison.OrdinalIgnoreCase)>=0;}
        public static bool IsReading(PlayerFarming player)
        {
            if(player==null||player.Spine==null||player.Spine.Skeleton==null)return false;
            Slot slot=player.Spine.Skeleton.FindSlot("BOOK");if(slot==null)return false;
            if(slot.Attachment!=null&&IsBookAttachment(slot.Attachment.Name)&&slot.A>0.001f)return true;
            if(player.Spine.AnimationState==null)return false;
            foreach(TrackEntry entry in player.Spine.AnimationState.Tracks)if(entry!=null&&entry.Alpha>0f&&TrackHasBook(entry.Animation,slot.Data.Index))return true;
            return false;
        }
        public static bool ObserveNativeSermonEffect(SkeletonAnimation effect){return NativeSermonGlyphs.Observe(effect);}
        private Vector2 Point(Bone bone){return bone==null?Vector2.zero:new Vector2(bone.WorldX*(skeleton.ScaleX<0f?-1f:1f),bone.WorldY*(skeleton.ScaleY<0f?-1f:1f));}
        private static Vector2 Rest(Bone bone){return bone==null?Vector2.zero:new Vector2(bone.WorldX,bone.WorldY);}
        internal void Capture()
        {
            if(disposed||source==null||skeleton==null||book==null||hip==null||head==null||source.AnimationState==null)return;
            // Keep a complete immutable-for-the-frame pose: the next native Update may
            // already be evaluating its animation on a worker when our LateUpdate runs.
            poseHip=Point(hip);poseHead=Point(head);poseShoulderR=Point(shoulderR);poseShoulderL=Point(shoulderL);
            poseHandR=Point(handR);poseHandL=Point(handL);poseLegR=Point(legR);poseLegL=Point(legL);
            poseFootR=Point(footR);poseFootL=Point(footL);poseHeadRotation=head.Rotation;
            PoseRevision++;AttachmentName=book.Attachment==null?"":book.Attachment.Name;
            TrackEntry baseTrack=source.AnimationState.GetCurrent(0);
            string baseClip=baseTrack?.Animation?.Name??"";
            BodyClip=baseClip;
            CeremonyClip=IsCeremony(baseClip)?baseClip:"";
            TrackEntry track=null;
            foreach(TrackEntry entry in source.AnimationState.Tracks)if(entry!=null&&entry.Alpha>0f&&TrackHasBook(entry.Animation,book.Data.Index))track=entry;
            bool keyedBookTrack=track!=null;
            if(track==null)track=baseTrack;
            AnimationName=track==null||track.Animation==null?"":track.Animation.Name;NativeTime=track==null?0f:track.TrackTime;
            animationTime=track==null?0f:track.AnimationTime;
            GestureActive=AnimationName.StartsWith("cards/",StringComparison.Ordinal)||AnimationName=="recruit"||AnimationName=="recruit-end"||Gameplay.IsWeaponPresentation(owner,AnimationName);
            poseBackRotation=back==null?0f:back.WorldRotationX;
            Slot item=skeleton.FindSlot("Items");itemVisible=item!=null&&item.Attachment!=null&&item.A>0.001f;
            throwAt=-1f;
            if(track!=null&&AnimationName.StartsWith("find-item/",StringComparison.Ordinal))
                foreach(Timeline timeline in track.Animation.Timelines){AttachmentTimeline a=timeline as AttachmentTimeline;
                    if(a==null||item==null||a.SlotIndex!=item.Data.Index)continue;
                    bool hadItem=false;for(int i=0;i<a.Frames.Length;i++){if(!String.IsNullOrEmpty(a.AttachmentNames[i]))hadItem=true;else if(hadItem){throwAt=a.Frames[i];break;}}}
            for(int i=0;i<6;i++){Slot card=cardSlots[i];cardVisible[i]=GestureActive&&AnimationName.StartsWith("cards/",StringComparison.Ordinal)&&card!=null&&card.Attachment!=null&&card.A*skeleton.A>0.001f;
                if(card!=null){cardPositions[i]=Point(card.Bone);cardAngles[i]=card.Bone.WorldRotationX;}}
            Visible=IsBookAttachment(AttachmentName)&&book.A*skeleton.A>0.001f&&book.Bone.Active&&source.gameObject.activeInHierarchy;
            RegionAttachment region=book.Attachment as RegionAttachment;
            VertexAttachment mesh=book.Attachment as VertexAttachment;int count=0;
            if(region!=null&&region.A>0.001f){region.ComputeWorldVertices(book.Bone,corners,0,2);count=8;}
            else if(mesh!=null&&mesh.WorldVerticesLength<=corners.Length){mesh.ComputeWorldVertices(book,0,mesh.WorldVerticesLength,corners,0,2);count=mesh.WorldVerticesLength;}
            else Visible=false;
            if(Visible&&count>=8){center=Vector2.zero;for(int i=0;i<count;i+=2)center+=new Vector2(corners[i]*(skeleton.ScaleX<0f?-1f:1f),corners[i+1]*(skeleton.ScaleY<0f?-1f:1f));center/=(count/2f);
                rightDirection=new Vector2((corners[4]-corners[2])*(skeleton.ScaleX<0f?-1f:1f),(corners[5]-corners[3])*(skeleton.ScaleY<0f?-1f:1f));
                if(rightDirection.sqrMagnitude<0.001f)Visible=false;}
            PoseActive=keyedBookTrack||Visible;
            if(!Visible){open=0f;if(!PoseActive)PoseDriven=false;lastTime=NativeTime;previousTrack=track;return;}
            float delta=track==previousTrack&&lastTime>=0f?NativeTime-lastTime:0f;if(delta<0f&&track!=null)delta+=track.Animation.Duration;
            float target=AttachmentName.IndexOf("closed",StringComparison.OrdinalIgnoreCase)>=0?0f:1f;
            open=Mathf.MoveTowards(open,target,Mathf.Max(0f,delta)*Spec.Visual("book_open_speed").Value);
            int n=AttachmentName.LastIndexOf("BookFlipping_",StringComparison.Ordinal);
            PageA=0f;PageB=1f;
            if(n>=0){int digit;if(Int32.TryParse(AttachmentName.Substring(n+13),out digit)){PageA=(Mathf.Clamp(digit,1,4)-1f)/4f;PageB=Mathf.Repeat(PageA+0.5f,1f);}}
            lastTime=NativeTime;previousTrack=track;
        }

        private bool IsCeremony(string clip)
        {
            if(clip.StartsWith("rituals/",StringComparison.Ordinal)||
                clip.StartsWith("sermons/doctrine-",StringComparison.Ordinal)||
                clip.StartsWith("sermons/declare-doctrine",StringComparison.Ordinal)||
                (clip.StartsWith("sermons/",StringComparison.Ordinal)&&clip.EndsWith("-nobook",StringComparison.Ordinal)))
                return true;
            // The wedding and altar routines also call shared clips used elsewhere in the game.
            return owner!=null&&owner.state!=null&&owner.state.CURRENT_STATE==StateMachine.State.CustomAnimation&&
                PlayerFarming.Location==FollowerLocation.Church&&
                (clip=="build"||clip=="kiss-follower"||clip=="dance"||clip=="bleat"||
                clip=="reactions/react-happy"||clip=="eat-react-bad");
        }

        public void ApplyCeremonyPose(Transform root,Transform torso,Transform steveHead,Transform rightArm,Transform leftArm,Transform rightLeg,Transform leftLeg)
        {
            PoseDriven=false;
            if(refHip==null||refHead==null||refFootR==null||refFootL==null||footR==null||footL==null||handR==null||handL==null)return;
            Vector2 hipOffset=(poseHip-Rest(refHip))*scale;
            float rightHeight=(poseFootR.y-refFootR.WorldY)*scale;
            float leftHeight=(poseFootL.y-refFootL.WorldY)*scale;
            float lift=Mathf.Clamp(Mathf.Min(rightHeight,leftHeight),0f,2f*MinecraftAvatar.BodyHeight);
            Grounded=lift<0.045f;
            // The feet, rather than the bouncing hips, distinguish airborne motions from planted gestures.
            root.position+=root.TransformVector(new Vector3(Mathf.Clamp(hipOffset.x,-0.4f,0.4f),Grounded?0f:lift,0f));
            Vector2 shoulders=(poseShoulderR+poseShoulderL)*0.5f-poseHip;
            float lean=Mathf.Clamp(Mathf.Atan2(-shoulders.x,shoulders.y)*Mathf.Rad2Deg,-16f,16f);
            torso.localRotation=Quaternion.Euler(Grounded?lean*0.25f:-6f,lean*0.55f,lean);
            float look=(poseHead.x-poseHip.x-refHead.WorldX+refHip.WorldX)*scale;
            float headRoll=Mathf.Clamp(Mathf.DeltaAngle(head.Data.Rotation,poseHeadRotation),-30f,30f);
            steveHead.localRotation=Quaternion.Euler(-headRoll*0.25f,Mathf.Clamp(look*65f,-30f,30f),headRoll);
            PoseCeremonyArm(rightArm,poseHandR-poseShoulderR,torso,1f);
            PoseCeremonyArm(leftArm,poseHandL-poseShoulderL,torso,-1f);
            PoseCeremonyLeg(rightLeg,poseFootR-poseLegR,rightHeight,1f);
            PoseCeremonyLeg(leftLeg,poseFootL-poseLegL,leftHeight,-1f);
            PoseDriven=true;
        }

        private static void PoseCeremonyArm(Transform arm,Vector2 native,Transform torso,float side)
        {
            Vector2 direction=native.sqrMagnitude>0.00001f?native.normalized:Vector2.down;
            float inward=Mathf.Max(0f,-side*direction.x);
            float depth=0.16f+inward*0.85f+Mathf.Max(0f,direction.y)*0.25f;
            arm.localPosition+=Vector3.forward*(inward*Spec.Visual("book_shoulder_depth").Value);
            float lateral=direction.x*Mathf.Lerp(1f,0.42f,inward);
            arm.localRotation=Quaternion.Inverse(torso.localRotation)*
                Quaternion.FromToRotation(Vector3.down,new Vector3(lateral,direction.y,depth).normalized);
        }

        private static void PoseCeremonyLeg(Transform leg,Vector2 native,float footHeight,float side)
        {
            float weight=Mathf.InverseLerp(0.03f,0.18f,footHeight);
            Vector2 direction=native.sqrMagnitude>0.00001f?native.normalized:Vector2.down;
            leg.localRotation=Quaternion.Euler(direction.x*24f*weight,side*6f*weight,direction.x*14f*weight);
        }

        internal bool ApplyGroundedAction(string clip,Transform torso,Transform steveHead,
            Transform rightArm,Transform leftArm,Transform rightLeg,Transform leftLeg)
        {
            if(BodyClip!=clip||reference==null||hip==null||head==null||shoulderR==null||
                shoulderL==null||handR==null||handL==null)return false;
            Vector2 shoulders=((poseShoulderR+poseShoulderL)*0.5f-poseHip-idleShoulders)*scale;
            Vector2 look=(poseHead-poseHip-idleHead)*scale;
            float dip=Mathf.Clamp((poseHip.y-idleHipY)*scale,-0.09f,0f);
            float twist=Mathf.Clamp((poseHandL.y-poseShoulderL.y-poseHandR.y+poseShoulderR.y)*scale*26f,-20f,20f);
            torso.localPosition+=Vector3.up*dip;
            torso.localRotation=Quaternion.Euler(Mathf.Clamp(-shoulders.y*78f,-18f,28f),
                Mathf.Clamp(shoulders.x*55f+twist,-28f,28f),Mathf.Clamp(-shoulders.x*45f,-18f,18f));
            float headRoll=Mathf.Clamp(Mathf.DeltaAngle(idleHeadAngle,poseHeadRotation),-30f,30f);
            steveHead.localRotation=Quaternion.Euler(Mathf.Clamp(-look.y*54f+headRoll*0.28f,-25f,30f),
                Mathf.Clamp(look.x*70f,-30f,30f),headRoll*0.65f);
            GroundedActionArm(rightArm,poseHandR-poseShoulderR,idleRightArm,1f);
            GroundedActionArm(leftArm,poseHandL-poseShoulderL,idleLeftArm,-1f);
            rightLeg.localRotation=leftLeg.localRotation=Quaternion.identity;
            return true;
        }

        private void GroundedActionArm(Transform arm,Vector2 direction,Vector2 rest,float side)
        {
            Vector2 delta=(direction-rest)*scale/Mathf.Max(0.1f,rest.magnitude*scale);
            float lateral=Mathf.Clamp(side*0.12f+delta.x*0.62f,-0.52f,0.52f);
            float vertical=Mathf.Clamp(-0.92f+delta.y*1.7f,-1f,0.84f);
            float depth=Mathf.Clamp(0.11f+Mathf.Abs(delta.x)*0.74f+Mathf.Abs(delta.y)*0.65f,0.11f,1.15f);
            float roll=Mathf.Clamp(delta.x*28f,-26f,26f);
            arm.localRotation=Quaternion.FromToRotation(Vector3.down,new Vector3(lateral,vertical,depth).normalized)*
                Quaternion.AngleAxis(roll,Vector3.down);
        }

        public void ApplyPose(Transform root,Transform torso,Transform steveHead,Transform rightArm,Transform leftArm,Transform rightLeg=null,Transform leftLeg=null)
        {
            PoseDriven=false;if((!PoseActive&&!GestureActive)||hip==null||head==null||handR==null||handL==null||reference==null)return;
            Vector2 lift=(poseHip-Rest(refHip))*scale;
            Grounded=lift.y<0.16f;
            float lean=Grounded?0f:Mathf.Clamp(Mathf.DeltaAngle(refBack==null?0f:refBack.WorldRotationX,poseBackRotation),-8f,8f);
            torso.localRotation=Quaternion.AngleAxis(lean,Vector3.forward);
            float headDelta=Mathf.DeltaAngle(head.Data.Rotation,poseHeadRotation);
            steveHead.localRotation=Quaternion.AngleAxis(headDelta,Vector3.forward);
            Vector2 r=poseHandR-poseShoulderR,l=poseHandL-poseShoulderL;
            PoseArm(leftArm,l,torso,false);
            PoseArm(rightArm,r,torso,true);
            if(Visible&&AnimationName.StartsWith("find-item/",StringComparison.Ordinal)&&!Gameplay.IsWeaponPresentation(owner,AnimationName))
            {
                float reach=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0.1f,-0.85f,r.normalized.x));
                if(throwAt>=0f)
                {
                    float contact=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(throwAt-0.22f,throwAt,animationTime))*(1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(throwAt+0.3f,throwAt+0.65f,animationTime)));
                    reach=Mathf.Max(reach,contact);
                }
                rightArm.localPosition+=new Vector3(-0.07f,0f,0.15f)*reach;
                Vector3 shoulder=root.InverseTransformPoint(rightArm.position);
                // Keep the book hand's original pose. The paper hand sweeps
                // across to the opposite torso edge, then releases toward it.
                Vector3 target=new Vector3(-4f*MinecraftAvatar.PixelSize,shoulder.y-4f*MinecraftAvatar.PixelSize,0.32f);
                Vector3 neutral=Palm(root,rightArm,true);
                AimPalm(root,rightArm,Vector3.Lerp(neutral,target,reach),true);
                if(lastPaperTrack==AnimationName&&NativeTime>=lastPaperTime&&lastPaperTime>=0f)
                    rightArm.localRotation=Quaternion.Slerp(lastPaperRotation,rightArm.localRotation,1f-Mathf.Exp(-Mathf.Min(0.1f,NativeTime-lastPaperTime)*22f));
                lastPaperTrack=AnimationName;lastPaperTime=NativeTime;lastPaperRotation=rightArm.localRotation;
                RightReachError=Vector3.Distance(Palm(root,rightArm,true),target);
            }
            PoseLeg(rightLeg,poseFootR-poseLegR,1f);PoseLeg(leftLeg,poseFootL-poseLegL,-1f);
            // Hip translation in an authored 2D squash must not slide planted feet.
            root.position+=root.TransformVector(new Vector3(Grounded?0f:Mathf.Clamp(lift.x,-0.08f,0.08f),Grounded?0f:Mathf.Max(0f,lift.y),0f));PoseDriven=true;
        }
        private void PoseLeg(Transform leg,Vector2 native,float side)
        {
            if(leg==null)return;
            if(Grounded){leg.localRotation=Quaternion.identity;return;}
            Vector2 direction=native.sqrMagnitude>0.001f?native.normalized:Vector2.down;
            float drift=Mathf.Sin(NativeTime*3f+side*0.6f)*4f;
            leg.localRotation=Quaternion.Euler(Mathf.Clamp(direction.x*18f+drift,-22f,22f),Mathf.Sin(NativeTime*1.7f+side)*3f,side*4f+Mathf.Clamp(direction.y+1f,0f,0.4f)*side*8f);
        }
        private static Vector3 Palm(Transform root,Transform arm,bool right)
        {return root.InverseTransformPoint(arm.TransformPoint(new Vector3(MinecraftAvatar.PalmX(arm,right),-9.2f,0f)*MinecraftAvatar.PixelSize));}
        private static void AimPalm(Transform root,Transform arm,Vector3 target,bool right)
        {Vector3 origin=root.InverseTransformPoint(arm.position);Vector3 neutral=new Vector3(MinecraftAvatar.PalmX(arm,right),-9.2f,0f);arm.rotation=root.rotation*Quaternion.FromToRotation(neutral,target-origin);}
        private Vector3 BookLocalPosition(Transform root,Transform leftArm)
        {Vector2 offset=(center-poseHandL)*scale;Vector3 local=Palm(root,leftArm,false)+new Vector3(offset.x,offset.y,0.10f);local.z=Mathf.Max(local.z,Spec.Visual("book_chest_clearance").Value);return local;}
        private static void PoseArm(Transform arm,Vector2 native,Transform torso,bool paperHand)
        {
            // Spine's flat shoulder-to-hand vector supplies timing/height, not
            // depth. Route inward reaches in front of the chest in 3D instead
            // of sweeping a rigid Minecraft arm through the torso's XY plane.
            Vector2 direction=native.sqrMagnitude>0.00001f?native.normalized:Vector2.down;
            float lateral=paperHand?Mathf.Clamp(direction.x*0.7f,-0.65f,0.65f):Mathf.Clamp(direction.x*Spec.Visual("book_arm_lateral").Value,-0.4f,0.4f);
            float vertical=Mathf.Clamp(direction.y,-0.88f,0.88f);
            float forward=Mathf.Sqrt(Mathf.Max(0.12f,1f-lateral*lateral-vertical*vertical));
            arm.localPosition+=Vector3.forward*Spec.Visual("book_shoulder_depth").Value;
            arm.localRotation=Quaternion.Inverse(torso.localRotation)*Quaternion.FromToRotation(Vector3.down,new Vector3(lateral,vertical,forward).normalized);
        }
        public void Present(BookVisual world,BookVisual view,Transform modelRoot,Transform leftArm,Transform rightArm,bool visible,bool firstPerson)
        {
            bool paperSequence=throwAt>=0f&&AnimationName.StartsWith("find-item/",StringComparison.Ordinal);
            float arrival=throwAt+AnimationPages.FlightDuration;
            bool receiving=paperSequence&&(itemVisible||animationTime>=throwAt&&animationTime<arrival);
            bool closing=paperSequence&&animationTime>=arrival&&animationTime<arrival+0.25f;
            bool active=(Visible||receiving||closing)&&visible;
            float displayOpen=receiving?1f:closing?Mathf.Max(open,1f-Mathf.SmoothStep(0f,1f,(animationTime-arrival)/0.25f)):open;
            if(active){Vector3 position=modelRoot.TransformPoint(BookLocalPosition(modelRoot,leftArm));
                float roll=Mathf.Atan2(rightDirection.y,rightDirection.x)*Mathf.Rad2Deg;
                Quaternion orientation=Quaternion.AngleAxis(roll,Vector3.forward)*Quaternion.AngleAxis(20f,Vector3.right);
                world.SetWorldPose(position,modelRoot.rotation*orientation);
                Vector2 relative=center-poseHip,rest=Rest(refTool)-Rest(refHip);
                view.SetLocalPose(new Vector3(Mathf.Clamp(relative.x*scale*0.5f,-0.32f,0.32f),Spec.Visual("book_view_y").Value+Mathf.Clamp((relative.y-rest.y)*scale*0.35f,-0.2f,0.2f),Spec.Visual("book_view_z").Value),orientation);
            }
            world.Tick(active&&!firstPerson,displayOpen,NativeTime,PageA,PageB);view.Tick(active&&firstPerson,displayOpen,NativeTime,PageA,PageB);
            glyphs.Tick(active,firstPerson);
            if(pages==null&&assets.IsAssetReady("page_26"))pages=new AnimationPages(assets,modelRoot);
            if(pages!=null)pages.Tick(visible,firstPerson,AnimationName,animationTime,itemVisible,throwAt,modelRoot,world,rightArm.TransformPoint(new Vector3(1f,-8.8f,2f)*MinecraftAvatar.PixelSize),rightArm.rotation,cardVisible,cardPositions,cardAngles,poseHip,scale);
        }
        public void Dispose(){if(disposed)return;disposed=true;if(!ReferenceEquals(source,null))source.OnMeshAndMaterialsUpdated-=AfterNativePose;glyphs.Dispose();if(pages!=null)pages.Dispose();source=null;skeleton=null;reference=null;}
    }
}
