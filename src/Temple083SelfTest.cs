using System;
using UnityEngine;

namespace CultCraft
{
    internal sealed class Temple083SelfTest
    {
        private static readonly string[] Clips =
        {
            "sermons/doctrine-start", "sermons/doctrine-loop", "sermons/declare-doctrine",
            "rituals/fire-ritual-start", "rituals/fire-ritual-loop", "rituals/fire-ritual-stop",
            "rituals/ritual-start", "rituals/ritual-stop", "rituals/feast-start", "rituals/feast-eat", "rituals/feast-end"
        };
        private readonly MinecraftAvatar avatar;
        private readonly Action<string,bool,string> report;
        private int clipIndex=-1,startDraws,measuredFrames,slowFrames;
        private float longestFrame;
        private float stageStarted;
        private bool started, movedLeft, movedRight, sawLift, sawGround;
        private Quaternion initialLeft,initialRight;
        private Vector3 settlementPosition;
        private Spine.TrackEntry entry;

        internal Temple083SelfTest(MinecraftAvatar avatar,Action<string,bool,string> report)
        {
            this.avatar=avatar;
            this.report=report;
        }

        internal bool Complete {get;private set;}

        internal void Tick(PlayerFarming player,bool ready)
        {
            if(Complete)return;
            if(!started)
            {
                if(!ready||BiomeBaseManager.Instance==null)return;
                started=true;
                stageStarted=Time.unscaledTime;
                settlementPosition=player.transform.position;
                BiomeBaseManager.Instance.ActivateChurch();
                PlayerFarming.Location=FollowerLocation.Church;
                player.transform.position=ChurchFollowerManager.Instance.AltarPosition.position;
                player.state.facingAngle=270f;
                GameManager.GetInstance().CameraSnapToPosition(player.transform.position);
                return;
            }
            if(player==null||player.Spine==null||avatar.Root==null)return;
            if(Time.timeScale<=0f&&player.Spine.UseDeltaTime)
            {
                stageStarted+=Time.unscaledDeltaTime;
                return;
            }
            if(Time.unscaledTime-stageStarted>9f)
            {
                Finish(false,"Native temple animation did not finish: "+(clipIndex<0?"temple setup":Clips[clipIndex]));
                return;
            }
            Transform torso=avatar.Root.Find("Torso");
            Transform left=torso.Find("LeftArm"),right=torso.Find("RightArm");
            if(clipIndex<0)
            {
                if(Time.unscaledTime-stageStarted<1.3f)return;
                StartClip(player,left,right);
                return;
            }
            if(avatar.AnimationPose!="ceremony/"+Clips[clipIndex]||!avatar.BookPoseDriven)return;
            measuredFrames++;
            float frameMs=Time.unscaledDeltaTime*1000f;
            if(frameMs>50f)slowFrames++;
            longestFrame=Mathf.Max(longestFrame,frameMs);
            movedLeft|=Quaternion.Angle(initialLeft,left.localRotation)>15f;
            movedRight|=Quaternion.Angle(initialRight,right.localRotation)>15f;
            sawLift|=player.transform.position.z-avatar.Root.position.z>0.25f;
            sawGround|=avatar.AnimationGrounded;
            if(entry.TrackTime<entry.Animation.Duration+0.12f)return;
            if(avatar.SkinRenderFrames-startDraws<4)
            {
                Finish(false,"The game camera did not draw the temple pose: "+Clips[clipIndex]);
                return;
            }
            if(clipIndex+1<Clips.Length)StartClip(player,left,right);
            else
            {
                player.state.CURRENT_STATE=StateMachine.State.Idle;
                player.Spine.AnimationState.SetAnimation(0,"idle",true);
                Finish(movedLeft&&movedRight&&sawLift&&sawGround,
                    "Original church and "+Clips.Length+" real player clips; both arms moved, lift and planted pose observed: "+
                    movedLeft+"/"+movedRight+"/"+sawLift+"/"+sawGround+
                    ". Frame pacing: "+slowFrames+"/"+measuredFrames+" over 50 ms, max "+longestFrame.ToString("F1")+" ms.");
            }
        }

        internal void ReturnToSettlement(PlayerFarming player)
        {
            if(player==null||BiomeBaseManager.Instance==null)return;
            player.state.CURRENT_STATE=StateMachine.State.Idle;
            player.Spine.AnimationState.SetAnimation(0,"idle",true);
            BiomeBaseManager.Instance.ActivateRoom();
            PlayerFarming.Location=FollowerLocation.Base;
            player.transform.position=settlementPosition;
            GameManager.GetInstance().CameraSnapToPosition(settlementPosition);
        }

        private void StartClip(PlayerFarming player,Transform left,Transform right)
        {
            string clip=Clips[++clipIndex];
            if(player.Spine.Skeleton.Data.FindAnimation(clip)==null)
            {
                Finish(false,"Installed player rig lacks "+clip);
                return;
            }
            stageStarted=Time.unscaledTime;
            startDraws=avatar.SkinRenderFrames;
            initialLeft=left.localRotation;
            initialRight=right.localRotation;
            player.state.CURRENT_STATE=StateMachine.State.CustomAnimation;
            entry=player.Spine.AnimationState.SetAnimation(0,clip,false);
        }

        private void Finish(bool pass,string evidence)
        {
            Complete=true;
            report("minecraft-temple-ceremonies",pass,evidence);
        }
    }
}
