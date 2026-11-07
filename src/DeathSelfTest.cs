using System;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    // Final action of the isolated memory-only test world, after combat/food proofs.
    internal sealed class DeathSelfTest
    {
        private readonly MinecraftAvatar avatar;
        private readonly ManualLogSource logger;
        private readonly Action<string,bool,string> report;
        private float started=-1f,lastAttempt=-1f,previousRoll,intermediateStarted=-1f;
        private float maxFacingError;
        private Vector3 standingForward,standingRight,standingPosition;
        private int intermediateFrames;
        private bool accepted,finished,nativeTurned,fellRight=true;
        public DeathSelfTest(MinecraftAvatar avatar,ManualLogSource logger,Action<string,bool,string> report)
        {this.avatar=avatar;this.logger=logger;this.report=report;}
        public bool Tick(PlayerFarming player)
        {
            if(finished)return true;
            if(player==null||player.health==null||player.state==null)return false;
            float now=Time.unscaledTime;if(started<0f)started=now;
            if(!accepted&&now-lastAttempt>1f)
            {
                if(avatar.Root==null)return false;
                lastAttempt=now;
                standingForward=avatar.Root.forward;
                standingRight=avatar.Root.right;
                standingPosition=player.transform.position;
                float heading=player.state.facingAngle;
                player.health.invincible=false;player.health.untouchable=false;
                var attacker=new GameObject("CultCraft test lethal hit origin");
                attacker.transform.position=standingPosition+(Vector3)Utils.DegreeToVector2(heading+140f)*3f;
                try
                {
                    accepted=player.health.DealDamage(10000f,attacker,attacker.transform.position,true,Health.AttackTypes.Melee,true,(Health.AttackFlags)0);
                    nativeTurned=Mathf.Abs(Mathf.DeltaAngle(heading,player.state.facingAngle))>25f;
                }
                finally { UnityEngine.Object.Destroy(attacker); }
                logger.LogInfo("Native death probe: lethal DealDamage accepted="+accepted+", native turned="+nativeTurned+", state="+player.state.CURRENT_STATE+", HP="+player.health.CurrentHP);
            }
            bool nativeDeath=player.state.CURRENT_STATE==StateMachine.State.GameOver||player.state.CURRENT_STATE==StateMachine.State.Dieing||player.state.CURRENT_STATE==StateMachine.State.Dead;
            float roll=avatar.DeathRoll;
            if(nativeDeath&&avatar.AnimationPose=="death"&&avatar.DeathTint&&avatar.WorldVisible)
            {
                maxFacingError=Mathf.Max(maxFacingError,Vector3.Angle(standingForward,avatar.Root.forward));
                if(roll>3f&&roll<87f)
                {
                    fellRight&=Vector3.Dot(avatar.Root.up,standingRight)>0f;
                    if(intermediateStarted<0f)intermediateStarted=now;
                    if(roll>=previousRoll)intermediateFrames++;
                }
            }
            previousRoll=roll;
            if(accepted&&nativeDeath&&roll>=89.9f&&avatar.DeathTint&&avatar.WorldVisible&&avatar.DeathDrawFrames>=12&&intermediateFrames>=8&&intermediateStarted>=0f&&now-intermediateStarted>=0.4f)
            {
                finished=true;
                float side=Vector3.Dot(avatar.Root.position-standingPosition,standingRight);
                bool facingHeld=nativeTurned&&fellRight&&maxFacingError<1f&&side>0.05f&&Vector3.Dot(avatar.Root.up,standingRight)>0.98f;
                report("minecraft-death",facingHeld,"Native lethal hit and 90-degree right-side fall, original heading error="+maxFacingError.ToString("F2")+
                    " degrees, native turned="+nativeTurned+", leaned right="+fellRight+", rightward displacement="+side.ToString("F2")+
                    ", intermediate frames="+intermediateFrames+", game draws="+avatar.DeathDrawFrames);
                return true;
            }
            if(now-started>15f)
            {
                finished=true;report("minecraft-death",false,"Native death visual timeout: accepted="+accepted+", state="+player.state.CURRENT_STATE+", roll="+roll+", red="+avatar.DeathTint+", world="+avatar.WorldVisible+", draws="+avatar.DeathDrawFrames+", intermediate="+intermediateFrames);
                return true;
            }
            return false;
        }
    }
}
