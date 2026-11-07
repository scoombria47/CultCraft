using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MMTools;
using Spine;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Presentation only: native attack events/tracks, or the existing bow's actual deadline.</summary>
    public sealed class AttackCooldownUi : IDisposable
    {
        private sealed class Timing { public float At;public string Event; }
        private static AttackCooldownUi current;
        private readonly Harmony harmony=new Harmony("cultcraft.attack.cooldown.ui");
        private readonly MinecraftAssets assets;
        private readonly Gameplay gameplay;
        private readonly CameraRig camera;
        private readonly Dictionary<Spine.Animation,Timing> timings=new Dictionary<Spine.Animation,Timing>();
        private readonly FieldInfo thrownAxe=AccessTools.Field(typeof(PlayerWeapon),"thrownAxe");
        private readonly FieldInfo legacyAttack=AccessTools.Field(typeof(PlayerFarming),"CurrentAttack");
        private PlayerFarming player;
        private int lastShots,drawFrame=-1;
        private float bowPeriod,bowDeadline;
        private bool disposed;
        public int NativeAcceptedAttacks { get; private set; }
        public int NativeReadyEvents { get; private set; }
        public int BowAcceptedShots { get; private set; }
        public float LastNativeAttackTime { get; private set; }
        public float Progress { get; private set; } = 1f;
        public bool Ready { get; private set; } = true;
        public bool Visible { get; private set; }
        public float NativeTrackTime { get; private set; }
        public float NativeReadyAt { get; private set; }
        public float NativePlaybackRate { get; private set; }
        public float NativePeriodSeconds { get; private set; }
        public float NativeRecoveryRemaining { get; private set; }
        public string TimingSource { get; private set; } = "native-ready";
        public string NativeAnimation { get; private set; }
        public Rect IndicatorRect { get; private set; }
        public int DrawFrames { get; private set; }
        public int LastDrawFrame { get; private set; } = -1;
        public float LastDrawProgress { get; private set; }
        public bool LastDrawReady { get; private set; }
        public bool LastDrawFirstPerson { get; private set; }
        public bool LastDrawUsesMinecraftSprites { get; private set; }
        public int LastDrawAcceptedSequence { get; private set; }
        public string LastDrawSource { get; private set; }
        public int AcceptedSequence { get { return NativeAcceptedAttacks+BowAcceptedShots; } }
        public int Sequence { get { return AcceptedSequence; } }
        public float LastDrawNativeTrackTime { get; private set; }
        public float LastDrawNativeReadyAt { get; private set; }
        public float LastDrawNativePlaybackRate { get; private set; }
        public float BowReadyAt { get { return bowDeadline; } }
        public float BowPeriod { get { return bowPeriod; } }

        public AttackCooldownUi(MinecraftAssets source,Gameplay gameplay,CameraRig camera,ManualLogSource logger)
        {
            assets=source;this.gameplay=gameplay;this.camera=camera;lastShots=gameplay.ShotsFired;current=this;
            try
            {
                MethodInfo accepted=AccessTools.PropertySetter(typeof(PlayerWeapon),"TimeOfAttack");
                MethodInfo eventHandler=AccessTools.Method(typeof(PlayerWeapon),"HandleAnimationStateEvent",new[]{typeof(TrackEntry),typeof(Spine.Event)});
                if(accepted==null||eventHandler==null||thrownAxe==null)throw new MissingMethodException("Native weapon cooldown observation API changed.");
                harmony.Patch(accepted,postfix:new HarmonyMethod(typeof(AttackCooldownUi),"AcceptedPostfix"));
                harmony.Patch(eventHandler,postfix:new HarmonyMethod(typeof(AttackCooldownUi),"EventPostfix"));
            }
            catch {Dispose();throw;}
        }

        private static bool Owns(PlayerWeapon weapon)
        {
            return current!=null&&!current.disposed&&weapon!=null&&weapon.playerFarming!=null&&
                weapon.playerFarming==(current.player==null?PlayerFarming.Instance:current.player);
        }
        private static void AcceptedPostfix(PlayerWeapon __instance)
        {
            if(!Owns(__instance))return;
            current.NativeAcceptedAttacks++;current.LastNativeAttackTime=__instance.TimeOfAttack;
            current.NativeTrackTime=current.NativeReadyAt=current.NativePeriodSeconds=0f;current.NativeAnimation=null;
        }
        private static void EventPostfix(PlayerWeapon __instance,Spine.Event e)
        {
            if(!Owns(__instance)||e==null||e.Data==null)return;
            string name=e.Data.Name;
            if(name=="Attack Can Break"||name=="Attack Has Finished")current.NativeReadyEvents++;
        }

        public void Tick(PlayerFarming owner)
        {
            if(disposed)return;
            if(player!=owner){player=owner;bowDeadline=0f;bowPeriod=0f;}
            if(gameplay.ShotsFired!=lastShots)
            {
                BowAcceptedShots+=Math.Max(0,gameplay.ShotsFired-lastShots);lastShots=gameplay.ShotsFired;
                bowDeadline=gameplay.BowCooldownReadyAt;bowPeriod=gameplay.BowCooldownTotal;
            }
            Refresh();
        }

        private bool Eligible()
        {
            if(player==null||!player.enabled||!player.gameObject.activeInHierarchy||player.state==null||player.playerWeapon==null||
                player.IsKnockedOut||player.GoToAndStopping||player.currentWeapon==EquipmentType.None||DataManager.Instance==null||!DataManager.Instance.EnabledSword||
                (!LocationManager.LocationIsDungeon(PlayerFarming.Location)&&!player.playerWeapon.ForceWeapons)||
                player.CurrentWeaponInfo==null||player.CurrentWeaponInfo.WeaponData==null||(!gameplay.BowHeld&&gameplay.SelectedSlot==2)||gameplay.SelectedItemId.Length==0||
                camera.SettingsOpen||PhotoModeManager.PhotoModeActive||PhotoModeManager.TakeScreenShotActive||LetterBox.IsPlaying||MMTransition.IsPlaying||
                (MMConversation.isPlaying&&!MMConversation.isBark)||GameManager.InMenu||Lamb.UI.UIMenuBase.ActiveMenus.Count>0)return false;
            var ui=Lamb.UI.UIManager.Instance;
            if(ui!=null&&(ui.IsPaused||ui.CurrentMenu!=null))return false;
            switch(player.state.CURRENT_STATE)
            {
                case StateMachine.State.Idle:case StateMachine.State.Moving:case StateMachine.State.Idle_Winter:case StateMachine.State.Moving_Winter:
                case StateMachine.State.Attacking:case StateMachine.State.ChargingHeavyAttack:case StateMachine.State.RecoverFromAttack:case StateMachine.State.SignPostAttack:
                case StateMachine.State.Dodging:case StateMachine.State.Stealth:return true;
                default:return false;
            }
        }

        private void Refresh()
        {
            Visible=Eligible();if(!Visible)return;
            bool crossbow=Gameplay.IsCrossbowItem(gameplay.SelectedItemId);
            if(gameplay.SelectedItemId=="bow"||crossbow)
            {
                float remaining=crossbow?gameplay.CrossbowCooldownRemaining:gameplay.BowCooldownRemaining;
                float period=crossbow?gameplay.CrossbowCooldownTotal:gameplay.BowCooldownTotal;
                Progress=remaining<=0f?1f:period>0f?Mathf.Clamp01(1f-remaining/period):0f;
                bool canAct=player.state.CURRENT_STATE==StateMachine.State.Idle||player.state.CURRENT_STATE==StateMachine.State.Moving||
                    player.state.CURRENT_STATE==StateMachine.State.Idle_Winter||player.state.CURRENT_STATE==StateMachine.State.Moving_Winter;
                Ready=remaining<=0f&&canAct;
                if(!canAct&&remaining<=0f)Progress=0f;
                TimingSource=crossbow?"custom-crossbow-deadline":canAct?"custom-bow-deadline":"native-state-blocks-bow";
                return;
            }
            PlayerWeapon weapon=player.playerWeapon;
            bool recovering=player.state.CURRENT_STATE==StateMachine.State.RecoverFromAttack;
            if(recovering||player.state.CURRENT_STATE==StateMachine.State.SignPostAttack)
            {
                // The installed legacy/timed-attack branch uses State.Timer, not
                // PlayerWeapon's Spine readiness events. PlayerController leaves
                // recovery only after this actual native countdown becomes negative.
                PlayerFarming.Attack action=legacyAttack==null?null:legacyAttack.GetValue(player) as PlayerFarming.Attack;
                float total=action==null?0f:recovering?action.PostTimer:action.PreTimer;
                NativeRecoveryRemaining=Mathf.Max(0f,player.state.Timer);
                Progress=total>0f?Mathf.Min(0.999f,Mathf.Clamp01(1f-NativeRecoveryRemaining/total)):0f;
                Ready=false;TimingSource=recovering?"native-recovery-timer":"native-windup-timer";
                return;
            }
            NativeRecoveryRemaining=0f;
            bool attackMotion=player.state.CURRENT_STATE==StateMachine.State.Attacking||player.state.CURRENT_STATE==StateMachine.State.ChargingHeavyAttack;
            bool flying=(thrownAxe.GetValue(weapon) as UnityEngine.Object)!=null;
            Ready=(!attackMotion||weapon.CurrentAttackState!=PlayerWeapon.AttackState.Begin)&&!flying;
            if(Ready){Progress=1f;TimingSource="native-state-ready";return;}
            if(flying){Progress=0f;TimingSource="native-thrown-axe-pending";return;}
            TrackEntry track=weapon.skeletonAnimation==null||weapon.skeletonAnimation.AnimationState==null?null:weapon.skeletonAnimation.AnimationState.GetCurrent(0);
            if(track==null||track.Animation==null){Progress=0f;TimingSource="native-track-pending";return;}
            NativeAnimation=track.Animation.Name;NativeTrackTime=track.AnimationTime;
            Timing timing=GetTiming(track.Animation);
            NativeReadyAt=timing.At;
            NativePlaybackRate=Mathf.Abs(weapon.skeletonAnimation.timeScale*weapon.skeletonAnimation.AnimationState.TimeScale*track.TimeScale);
            NativePeriodSeconds=NativePlaybackRate>0f?NativeReadyAt/NativePlaybackRate:0f;
            // AnimationTime already includes the native speed, tarot/attachment rate,
            // charging track changes and hitstop. No UI timer advances this value.
            Progress=track.Loop||NativeReadyAt<=0f?0f:Mathf.Min(0.999f,Mathf.Clamp01(NativeTrackTime/NativeReadyAt));
            TimingSource="native-track:"+timing.Event;
        }

        private Timing GetTiming(Spine.Animation animation)
        {
            Timing timing;if(timings.TryGetValue(animation,out timing))return timing;
            float canBreak=Single.PositiveInfinity,finished=Single.PositiveInfinity;
            foreach(Timeline timeline in animation.Timelines)
            {
                EventTimeline events=timeline as EventTimeline;if(events==null)continue;
                foreach(Spine.Event e in events.Events)
                {
                    if(e==null||e.Data==null)continue;
                    if(e.Data.Name=="Attack Can Break")canBreak=Mathf.Min(canBreak,e.Time);
                    if(e.Data.Name=="Attack Has Finished")finished=Mathf.Min(finished,e.Time);
                }
            }
            timing=!Single.IsPositiveInfinity(canBreak)?new Timing{At=canBreak,Event="Attack Can Break"}:
                !Single.IsPositiveInfinity(finished)?new Timing{At=finished,Event="Attack Has Finished"}:
                new Timing{At=animation.Duration,Event="animation-end-awaiting-native-state"};
            timings.Add(animation,timing);return timing;
        }

        public void Draw(PlayerFarming owner)
        {
            if(disposed||owner!=player||UnityEngine.Event.current==null||UnityEngine.Event.current.type!=EventType.Repaint)return;
            Refresh();if(!Visible)return;
            float scale=PixelUi.Scale;
            bool firstPerson=camera.Active;
            string backgroundId=firstPerson?"gui_attack_crosshair_background":"gui_attack_hotbar_background";
            string progressId=firstPerson?"gui_attack_crosshair_progress":"gui_attack_hotbar_progress";
            Texture2D background=assets.Texture(backgroundId),progress=assets.Texture(progressId),full=assets.Texture("gui_attack_crosshair_full");
            if(background==null||progress==null||firstPerson&&Ready&&full==null){Visible=false;return;}
            Rect bar=Hotbar.ScreenRect;
            IndicatorRect=firstPerson?new Rect(Mathf.Round(Screen.width*0.5f-8f*scale),Mathf.Round(Screen.height*0.5f+PixelUi.V("attack_crosshair_offset")*scale),16f*scale,(Ready?16f:4f)*scale):
                new Rect(bar.xMax+PixelUi.V("attack_hotbar_gap")*scale,bar.y+PixelUi.V("attack_hotbar_y")*scale,18f*scale,18f*scale);
            Color old=GUI.color;GUI.color=Color.white;
            if(firstPerson&&Ready)GUI.DrawTexture(IndicatorRect,full,ScaleMode.StretchToFill,true);
            else
            {
                GUI.DrawTexture(IndicatorRect,background,ScaleMode.StretchToFill,true);
                float pixels=Mathf.Floor((firstPerson?16f:18f)*Progress);
                if(pixels>0f)
                {
                    Rect fill=firstPerson?new Rect(IndicatorRect.x,IndicatorRect.y,pixels*scale,IndicatorRect.height):new Rect(IndicatorRect.x,IndicatorRect.yMax-pixels*scale,IndicatorRect.width,pixels*scale);
                    Rect uv=firstPerson?new Rect(0f,0f,pixels/16f,1f):new Rect(0f,0f,1f,pixels/18f);
                    GUI.DrawTextureWithTexCoords(fill,progress,uv,true);
                }
            }
            GUI.color=old;
            if(drawFrame!=Time.frameCount){drawFrame=Time.frameCount;DrawFrames++;}
            LastDrawFrame=Time.frameCount;LastDrawProgress=Progress;LastDrawReady=Ready;LastDrawFirstPerson=firstPerson;
            LastDrawAcceptedSequence=AcceptedSequence;LastDrawSource=TimingSource;LastDrawUsesMinecraftSprites=true;
            LastDrawNativeTrackTime=NativeTrackTime;LastDrawNativeReadyAt=NativeReadyAt;LastDrawNativePlaybackRate=NativePlaybackRate;
        }

        public void Dispose(){if(disposed)return;disposed=true;harmony.UnpatchSelf();if(current==this)current=null;timings.Clear();Visible=false;}
    }
}
