using System;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    // Native clips in the existing save-isolated Melty world, observed after real rendering.
    internal sealed class Animation074SelfTest
    {
        private readonly MinecraftAvatar avatar;
        private readonly ManualLogSource logger;
        private readonly string[] clips={"find-item/find-item-combined","sermons/sermon-start","cards/cards-start","cards/cards-loop","recruit","recruit-end"};
        private int step,groundFrames,drawsBefore;
        private float started=-1f,stepStarted;
        private bool requested,ended,reached;
        private Quaternion firstRight,firstLeft;
        private bool airSample;
        private float rightMotion,leftMotion;
        private float minimumReachError=999f;
        public bool Complete {get;private set;}
        public bool Passed {get;private set;}
        public string Detail {get;private set;}
        public Animation074SelfTest(MinecraftAvatar avatar,ManualLogSource logger){this.avatar=avatar;this.logger=logger;}
        public void Tick(PlayerFarming player)
        {
            if(Complete||player==null||player.state==null)return;
            if(started<0f)started=Time.unscaledTime;
            if(Time.unscaledTime-started>35f){Finish(false,"Native0.7.4 clip timed out at "+clips[step]+", state="+player.state.CURRENT_STATE);return;}
            Transform root=avatar.Root;
            if(root==null)return;
            Transform right=root.Find(Spec.Avatar("right_leg").Label),left=root.Find(Spec.Avatar("left_leg").Label);
            if(!requested)
            {
                if(player.state.CURRENT_STATE!=StateMachine.State.Idle)return;
                ended=false;requested=true;stepStarted=Time.unscaledTime;drawsBefore=avatar.AnimationPageDraws;
                player.CustomAnimationWithCallback(clips[step],false,delegate{ended=true;if(player!=null&&player.state.CURRENT_STATE==StateMachine.State.CustomAnimation)player.state.CURRENT_STATE=StateMachine.State.Idle;});
                return;
            }
            if(avatar.AnimationPose=="book/"+clips[step]||avatar.AnimationPose=="gesture/"+clips[step])
            {
                if(right!=null&&left!=null)
                {
                    if(avatar.AnimationGrounded)
                    {
                        if(Quaternion.Angle(right.localRotation,Quaternion.identity)>0.05f||Quaternion.Angle(left.localRotation,Quaternion.identity)>0.05f)
                        {Finish(false,"Grounded native book/gesture clip moved planted rigid legs: "+clips[step]);return;}
                        groundFrames++;
                    }
                    else if(step==0)
                    {
                        if(!airSample){firstRight=right.localRotation;firstLeft=left.localRotation;airSample=true;}
                        rightMotion=Mathf.Max(rightMotion,Quaternion.Angle(firstRight,right.localRotation));leftMotion=Mathf.Max(leftMotion,Quaternion.Angle(firstLeft,left.localRotation));
                    }
                }
                var track=player.Spine.AnimationState.GetCurrent(0);
                if(step==0&&track!=null&&track.TrackTime>3.4f&&track.TrackTime<3.9f)
                {minimumReachError=Mathf.Min(minimumReachError,avatar.BookRightReachError);if(avatar.BookRightReachError<0.15f)reached=true;}
            }
            if(!ended)return;
            if(step==0&&(!avatar.AnimationPageThrown||!avatar.AnimationPageBookOpen||avatar.AnimationPageDraws<=drawsBefore||!reached||rightMotion<1f||leftMotion<1f))
            {Finish(false,"Actual page/reach/air-leg proof incomplete: page="+avatar.AnimationPageThrown+", receiving book open="+avatar.AnimationPageBookOpen+", draws="+(avatar.AnimationPageDraws-drawsBefore)+", reached="+reached+", air right/left="+rightMotion+"/"+leftMotion+", closest reach error="+minimumReachError);return;}
            if((step==2||step==3)&&(!avatar.AnimationCardsDrawn||!avatar.AnimationCardsHorizontal||avatar.AnimationPageDraws<=drawsBefore))
            {Finish(false,"Native tarot clip did not render Minecraft26.3 pages: "+clips[step]);return;}
            logger.LogInfo("Native0.7.4 animation observed: "+clips[step]+", page draw delta="+(avatar.AnimationPageDraws-drawsBefore)+", ground frames="+groundFrames+", right/left air movement="+rightMotion+"/"+leftMotion+", reach="+reached);
            requested=false;step++;
            if(step>=clips.Length)Finish(groundFrames>=2,"Native book throw/page contact, both airborne legs, planted grounded legs, six-slot tarot pages and recruit/recruit-end rendered; page draws="+avatar.AnimationPageDraws+", grounded frames="+groundFrames);
        }
        private void Finish(bool pass,string detail){Complete=true;Passed=pass;Detail=detail;logger.LogInfo("Native0.7.4 animation proof: "+detail);}
    }
}
