using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace CultCraft
{
    // Only the save-isolated Melty scenario can create this probe.
    internal sealed class PlayerAnimationAudit
    {
        private static readonly float[] Samples={0.1f,0.5f,0.9f};
        private static readonly string[] Bones={"HIP","HEAD","HAND_RIGHT","HAND_LEFT","FOOT_RIGHT","FOOT_LEFT"};
        private static readonly string[] Gallery={
            "actions/chop-wood","Fishing/fishing-start","sermons/declare-doctrine",
            "pet-dog","actions/shear","Drumming/Drum-hit-side","actions/harvest_meat","drink-good"
        };
        private static readonly HashSet<string> GroundedChecks=new HashSet<string>(StringComparer.Ordinal){
            "pet-dog","actions/shear","Drumming/Drum-hit-side","actions/harvest_meat","drink-good"
        };
        private static SkeletonAnimation isolatedSpine;
        private readonly MinecraftAvatar avatar;
        private readonly Action<string,bool,string> report;
        private readonly Quaternion[] first=new Quaternion[7],current=new Quaternion[7];
        private readonly Vector2[] nativeFirst=new Vector2[Bones.Length];
        private PlayerFarming owner;
        private Spine.Animation pending,installed;
        private TrackEntry track;
        private float time;
        private int draws;
        private bool applied;
        private string caption="";

        internal PlayerAnimationAudit(MinecraftAvatar avatar,Action<string,bool,string> report)
        {this.avatar=avatar;this.report=report;}

        internal bool Showcase {get;private set;}

        internal void Prepare(PlayerFarming player)
        {
            if(pending==null||player!=owner)return;
            var spine=owner.Spine;
            if(installed!=pending||spine.AnimationState.GetCurrent(0)!=track)
            {
                spine.AnimationState.ClearTracks();
                track=spine.AnimationState.SetAnimation(0,pending,false);
                track.MixDuration=0f;
                installed=pending;
            }
            track.TrackTime=time;
            owner.state.CURRENT_STATE=Context(pending.Name);
            var skeleton=spine.Skeleton;
            skeleton.SetToSetupPose();
            // Evaluate the real clip and constraints without invoking gameplay events.
            pending.Apply(skeleton,time,time,false,null,1f,MixBlend.Replace,MixDirection.In);
            skeleton.UpdateWorldTransform();
            avatar.SampleAuditPose(owner);
            draws=avatar.SkinRenderFrames;
            applied=true;
        }

        private static StateMachine.State Context(string name)
        {
            if(name=="actions/collect-berries")return StateMachine.State.TimedAction;
            if(name.StartsWith("roll",StringComparison.Ordinal))return StateMachine.State.Dodging;
            if(name.StartsWith("attack",StringComparison.Ordinal)||name.EndsWith("/attack",StringComparison.Ordinal))
                return StateMachine.State.Attacking;
            return StateMachine.State.CustomAnimation;
        }

        internal IEnumerator Run(PlayerFarming player,string directory,Action startShowcase)
        {
            owner=player;
            var spine=player.Spine;
            var clips=spine.Skeleton.Data.Animations;
            var skeleton=spine.Skeleton;
            var joints=Array.ConvertAll(Bones,skeleton.FindBone);
            float nativeHeight=Mathf.Max(0.001f,Mathf.Abs(spine.skeletonDataAsset.scale)*skeleton.Data.Height);
            var equipment=new Dictionary<string,EquipmentType>(StringComparer.Ordinal);
            foreach(EquipmentType type in Enum.GetValues(typeof(EquipmentType)))
            {
                if(type==EquipmentType.None)continue;
                var data=EquipmentManager.GetEquipmentData(type);
                if(data!=null&&!String.IsNullOrEmpty(data.PickupAnimationKey))
                    equipment[data.PickupAnimationKey]=type;
            }
            var rows=new StringBuilder("clip\tduration_s\tcontext\tresult\tnative_joint_motion_body_fraction\tminecraft_rotation_deg\trendered_samples\tpose_handlers\n");
            var end=new WaitForEndOfFrame();
            var patch=new Harmony("cultcraft.test.player-animation-audit");
            var originalWeapon=player.currentWeapon;
            int count=0, rendered=0, absent=0, candidate=0;
            var groundedResults=new List<string>();
            int groundedPass=0;
            patch.Patch(AccessTools.Method(typeof(SkeletonAnimation),nameof(SkeletonAnimation.OnUpdate)),
                prefix:new HarmonyMethod(typeof(PlayerAnimationAudit),nameof(SkipAutomaticUpdate)));
            spine.BaseLateUpdate();
            isolatedSpine=spine;
            try
            {
                foreach(var clip in clips)
                {
                    player.currentWeapon=equipment.TryGetValue(clip.Name,out EquipmentType type)?type:originalWeapon;
                    float sourceMotion=0f,modelMotion=0f;
                    int visible=0;
                    var handlers=new HashSet<string>();
                    bool checkGround=GroundedChecks.Contains(clip.Name),grounded=true,depth=false;
                    for(int i=0;i<Samples.Length;i++)
                    {
                        pending=clip;time=clip.Duration*Samples[i];applied=false;
                        yield return end;
                        if(!applied)continue;
                        visible+=avatar.SkinRenderFrames>draws?1:0;
                        handlers.Add(avatar.AnimationPose??"");
                        avatar.ReadAuditPose(current);
                        if(checkGround)
                        {
                            grounded&=avatar.FootGroundError<0.025f&&avatar.AnimationPose=="native/"+clip.Name;
                            depth|=Mathf.Abs((current[3]*Vector3.down).z)>0.12f||
                                Mathf.Abs((current[4]*Vector3.down).z)>0.12f;
                        }
                        for(int j=0;j<joints.Length;j++)
                        {
                            Bone bone=joints[j];
                            if(bone==null)continue;
                            var point=new Vector2(bone.WorldX,bone.WorldY);
                            if(i==0)nativeFirst[j]=point;
                            else sourceMotion=Mathf.Max(sourceMotion,Vector2.Distance(nativeFirst[j],point)/nativeHeight);
                        }
                        if(i==0)Array.Copy(current,first,current.Length);
                        else for(int j=0;j<current.Length;j++)
                            modelMotion=Mathf.Max(modelMotion,Quaternion.Angle(first[j],current[j]));
                        player.state.CURRENT_STATE=StateMachine.State.CustomAnimation;
                    }
                    bool dedicated=handlers.Count>1||!handlers.Contains("walk/idle");
                    bool generic=Context(clip.Name)!=StateMachine.State.CustomAnimation||
                        clip.Name.StartsWith("idle",StringComparison.Ordinal)||clip.Name.StartsWith("run",StringComparison.Ordinal);
                    bool legacy=clip.Name.StartsWith("oldstuff/",StringComparison.Ordinal)||
                        clip.Name.IndexOf("OLD",StringComparison.OrdinalIgnoreCase)>=0;
                    string result=dedicated?"dedicated":generic?"shared gameplay pose":sourceMotion<0.035f?"small or overlay motion":
                        legacy?"legacy clip without dedicated pose":"candidate for adaptation";
                    if(result=="candidate for adaptation")candidate++;
                    if(checkGround)
                    {
                        bool good=grounded&&depth&&modelMotion>3f&&visible==Samples.Length;
                        if(good)groundedPass++;
                        groundedResults.Add(clip.Name+"="+good+" ("+modelMotion.ToString("F1",CultureInfo.InvariantCulture)+"°)");
                    }
                    if(visible>0)rendered++;
                    else if(!clip.Name.StartsWith("warp-",StringComparison.Ordinal))absent++;
                    count++;
                    rows.Append(clip.Name).Append('\t').Append(clip.Duration.ToString("F3",CultureInfo.InvariantCulture))
                        .Append('\t').Append(Context(clip.Name)).Append('\t').Append(result)
                        .Append('\t').Append(sourceMotion.ToString("F3",CultureInfo.InvariantCulture))
                        .Append('\t').Append(modelMotion.ToString("F1",CultureInfo.InvariantCulture)).Append('\t').Append(visible)
                        .Append('\t').AppendLine(String.Join(", ",handlers));
                }
                File.WriteAllText(Path.Combine(directory,"animation-audit.tsv"),rows.ToString());
                report("minecraft-animation-catalog",count==clips.Count&&rendered>0&&absent==0,
                    count+" real clips sampled on the in-game avatar, "+rendered+" visible, "+absent+" unexpectedly undrawn, "+candidate+" adaptation candidates.");
                report("minecraft-grounded-poses",groundedPass==GroundedChecks.Count,
                    "Three-axis body and arm motion with planted feet: "+String.Join(", ",groundedResults));
                player.currentWeapon=originalWeapon;
                Showcase=true;
                startShowcase();
                foreach(string name in Gallery)
                {
                    var clip=skeleton.Data.FindAnimation(name);
                    if(clip==null)continue;
                    float start=Time.realtimeSinceStartup;
                    do
                    {
                        pending=clip;time=Mathf.Clamp01((Time.realtimeSinceStartup-start)/2f)*clip.Duration;
                        caption=name;
                        yield return end;
                        player.state.CURRENT_STATE=StateMachine.State.CustomAnimation;
                    }while(Time.realtimeSinceStartup-start<2f);
                }
            }
            finally
            {
                Showcase=false;caption="";pending=installed=null;isolatedSpine=null;patch.UnpatchSelf();
                player.currentWeapon=originalWeapon;
                player.state.CURRENT_STATE=StateMachine.State.Idle;
                spine.AnimationState.ClearTracks();
                spine.AnimationState.SetAnimation(0,"idle",true);
                owner=null;
            }
        }

        internal void DrawLabel()
        {
            if(!Showcase)return;
            var rect=new Rect(Screen.width*0.5f-230f,60f,460f,42f);
            GUI.Box(rect,"");
            GUI.Label(new Rect(rect.x+16f,rect.y+10f,rect.width-32f,28f),
                caption+"  |  "+avatar.AnimationPose);
        }

        private static bool SkipAutomaticUpdate(SkeletonAnimation __instance)=>__instance!=isolatedSpine;
    }
}
