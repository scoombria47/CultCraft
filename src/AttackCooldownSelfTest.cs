using System;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Bounded native-attack observation inside SelfTest's isolated world only.</summary>
    public sealed class AttackCooldownSelfTest : IDisposable
    {
        private readonly AttackCooldownUi ui;
        private readonly CameraRig camera;
        private readonly MinecraftAvatar avatar;
        private readonly Action<string,bool,string> report;
        private readonly ManualLogSource logger;
        private PlayerFarming owner;
        private Gameplay gameplay;
        private EquipmentType originalWeapon;
        private int originalLevel,originalSlot;
        private bool originalFirstPerson,captured,complete,disposed,cleanupStarted,result;
        private string detail;
        private float started=-1f,phaseStarted;
        private int phase,weaponIndex,configuredFrame,attackFrame,acceptedBefore,eventsBefore,sequence;
        private int partialFrames,lastPartialFrame=-1;
        private float minimum=1f,maximum,period,readyAt,rate;
        private bool readyDraw,position;
        private float swordPeriod;
        private string swordAnimation,animation;
        private int completedWeapons;

        public AttackCooldownSelfTest(AttackCooldownUi ui,CameraRig camera,MinecraftAvatar avatar,Action<string,bool,string> report,ManualLogSource logger)
        {this.ui=ui;this.camera=camera;this.avatar=avatar;this.report=report;this.logger=logger;}

        public bool Check(PlayerFarming player,Gameplay gameplay)
        {
            if(complete||disposed)return true;
            this.gameplay=gameplay;
            float now=Time.unscaledTime;
            if(started<0f){started=phaseStarted=now;}
            if(cleanupStarted)return FinishCleanup(now);
            if(now-started>25f){BeginCleanup(false,"Native attack indicator probe exceeded25s: phase="+phase+", weapon="+weaponIndex+", source="+ui.TimingSource+", ready="+ui.Ready+", partial="+partialFrames+", accepted="+ui.NativeAcceptedAttacks);return false;}
            if(player==null||player.playerWeapon==null||player.state==null||!player.gameObject.activeInHierarchy||player.IsKnockedOut)return false;
            if(!captured)
            {
                string marker=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Melty","test","cultcraft.json");
                if(!File.Exists(marker)){BeginCleanup(false,"Native cooldown probe requires the explicit isolated-test marker.");return false;}
                if(player.currentWeapon==EquipmentType.None||(!LocationManager.LocationIsDungeon(PlayerFarming.Location)&&!player.playerWeapon.ForceWeapons))return false;
                owner=player;originalWeapon=player.currentWeapon;originalLevel=player.currentWeaponLevel;
                originalSlot=gameplay.SelectedSlot;originalFirstPerson=camera.FirstPerson;captured=true;
                NeutralAttack();
            }
            if(player!=owner){BeginCleanup(false,"Native test player changed during cooldown observation.");return false;}
            switch(phase)
            {
                case 0:
                    // A real digit1 selection, including when the preceding weapon
                    // family test left the bow or food slot selected.
                    PulseSlot(0,now-phaseStarted);
                    if(gameplay.SelectedSlot!=0||!Idle(player))break;
                    ReleaseSlots();
                    bool firstPerson=weaponIndex==0;
                    if(camera.FirstPerson!=firstPerson)camera.Toggle();
                    player.playerWeapon.SetWeapon(WantedWeapon,Math.Max(1,originalLevel));
                    configuredFrame=Time.frameCount;Advance(1);break;
                case 1:
                    if(Time.frameCount<=configuredFrame+1||!Idle(player)||gameplay.SelectedSlot!=0||
                        player.currentWeapon!=WantedWeapon||player.CurrentWeaponInfo==null||player.CurrentWeaponInfo.WeaponData==null||
                        player.CurrentWeaponInfo.WeaponData.PrimaryEquipmentType!=WantedWeapon||
                        camera.Active!=(weaponIndex==0)||avatar.HeldItemId!=gameplay.WeaponId||
                        (weaponIndex==0?!avatar.ViewVisible:!avatar.WorldVisible))break;
                    acceptedBefore=ui.NativeAcceptedAttacks;eventsBefore=ui.NativeReadyEvents;attackFrame=Time.frameCount;
                    minimum=1f;maximum=0f;partialFrames=0;lastPartialFrame=-1;readyDraw=position=false;period=readyAt=rate=0f;animation=null;
                    // This public native API starts the actual weapon coroutine. It
                    // owns TimeOfAttack, Spine animation, damage and readiness events.
                    sequence=-1;player.playerWeapon.DoAttack();Advance(2);break;
                case 2:
                    if(ui.NativeAcceptedAttacks<=acceptedBefore)
                    {
                        if(now-phaseStarted>0.5f)BeginCleanup(false,"Native DoAttack did not reach its real TimeOfAttack setter: "+WantedWeapon);
                        break;
                    }
                    if(sequence<0)sequence=ui.Sequence;
                    if(player.currentWeapon!=WantedWeapon){BeginCleanup(false,"Native gear changed during its cooldown: "+WantedWeapon);break;}
                    ObserveDraw();
                    if(partialFrames>=2&&maximum-minimum>0.02f&&readyDraw&&Idle(player))
                    {
                        if(logger!=null)logger.LogInfo("Actual native attack cooldown: "+WantedWeapon+", clip="+animation+", native ready event at="+readyAt.ToString("F3")+", playback rate="+rate.ToString("F3")+", native interval="+period.ToString("F3")+", rendered partial range="+minimum.ToString("F3")+".."+maximum.ToString("F3")+", actual ready event/full draw=True, view="+(weaponIndex==0?"FPS":"top-down")+", MC sprite/placement=True.");
                        completedWeapons++;
                        if(weaponIndex==0){swordPeriod=period;swordAnimation=animation;weaponIndex=1;Advance(0);}
                        else if(Mathf.Abs(period-swordPeriod)>0.005f&&animation!=swordAnimation)
                            BeginCleanup(true,"Two actual native attack intervals observed: SwordFPS="+swordPeriod.ToString("F3")+", "+WantedWeapon+"TopDown="+period.ToString("F3")+"; sequence-matched Minecraft Repaint progress rises then native event/readiness completes; previous real custom-bow shots="+ui.BowAcceptedShots);
                        else if(weaponIndex==1){weaponIndex=2;Advance(0);}
                        else BeginCleanup(false,"Actual Sword/Axe/Hammer intervals were not distinct; no fixed UI timer was substituted.");
                    }
                    else if(now-phaseStarted>3.5f)
                        BeginCleanup(false,"Native cooldown did not produce strict partial+ready draws: "+WantedWeapon+", source="+ui.TimingSource+", native state="+player.state.CURRENT_STATE+"/"+player.playerWeapon.CurrentAttackState+", partial="+partialFrames+", range="+minimum+".."+maximum+", readyDraw="+readyDraw+", position="+position+", sequence="+sequence+"/"+ui.LastDrawAcceptedSequence);
                    break;
            }
            return false;
        }

        private EquipmentType WantedWeapon { get { return weaponIndex==0?EquipmentType.Sword:weaponIndex==1?EquipmentType.Axe:EquipmentType.Hammer; } }
        private static bool Idle(PlayerFarming player)
        {
            var state=player.state.CURRENT_STATE;
            return state==StateMachine.State.Idle||state==StateMachine.State.Moving||state==StateMachine.State.Idle_Winter||state==StateMachine.State.Moving_Winter;
        }
        private void ObserveDraw()
        {
            if(ui.LastDrawFrame<attackFrame||ui.LastDrawFrame<Time.frameCount-2||ui.LastDrawAcceptedSequence!=sequence||
                !ui.LastDrawUsesMinecraftSprites||ui.LastDrawFirstPerson!=(weaponIndex==0))return;
            Rect rect=ui.IndicatorRect,bar=Hotbar.ScreenRect;
            bool located=weaponIndex==0?Mathf.Abs(rect.center.x-Screen.width*0.5f)<=1f&&rect.yMin>Screen.height*0.5f&&rect.yMax<Screen.height:
                rect.xMin>=bar.xMax&&rect.yMin>=bar.yMin-1f&&rect.yMax<=bar.yMax+1f&&rect.xMax<=Screen.width;
            position|=located;if(!located)return;
            if(!ui.LastDrawReady&&ui.LastDrawProgress>0f&&ui.LastDrawProgress<0.999f&&
                ui.LastDrawSource!=null&&ui.LastDrawSource.StartsWith("native-track:",StringComparison.Ordinal)&&
                ui.LastDrawNativeReadyAt>0f&&ui.LastDrawNativePlaybackRate>0f)
            {
                if(lastPartialFrame!=ui.LastDrawFrame){lastPartialFrame=ui.LastDrawFrame;partialFrames++;}
                minimum=Mathf.Min(minimum,ui.LastDrawProgress);maximum=Mathf.Max(maximum,ui.LastDrawProgress);
                readyAt=ui.LastDrawNativeReadyAt;rate=ui.LastDrawNativePlaybackRate;period=readyAt/rate;animation=ui.NativeAnimation;
            }
            if(ui.LastDrawReady&&ui.LastDrawProgress>=0.999f&&ui.NativeReadyEvents>eventsBefore&&
                owner.playerWeapon.CurrentAttackState!=PlayerWeapon.AttackState.Begin)readyDraw=true;
        }
        private void Advance(int next){phase=next;phaseStarted=Time.unscaledTime;}
        private static void NeutralAttack(){MeltyPlay.Button(0,false);MeltyPlay.Hold("r",false);}
        private static void ReleaseSlots(){MeltyPlay.Hold("1",false);MeltyPlay.Hold("2",false);MeltyPlay.Hold("3",false);}
        private static void PulseSlot(int slot,float time)
        {
            ReleaseSlots();MeltyPlay.Hold((slot+1).ToString(),Mathf.Repeat(time,0.5f)<0.2f);
        }
        private void BeginCleanup(bool pass,string reason)
        {
            result=pass;detail=reason;cleanupStarted=true;phaseStarted=Time.unscaledTime;NeutralAttack();ReleaseSlots();
            if(captured&&owner!=null&&owner.playerWeapon!=null)
            {
                if(!Idle(owner))owner.playerWeapon.StopAttackRoutine();
                owner.playerWeapon.SetWeapon(originalWeapon,originalLevel);
                if(camera.FirstPerson!=originalFirstPerson)camera.Toggle();
            }
        }
        private bool FinishCleanup(float now)
        {
            if(captured&&gameplay!=null&&gameplay.SelectedSlot!=originalSlot)
            {
                PulseSlot(originalSlot,now-phaseStarted);
                if(now-phaseStarted<2f)return false;
                result=false;detail+="; original hotbar slot could not be restored through native digit input";
            }
            ReleaseSlots();NeutralAttack();complete=true;
            if(logger!=null)logger.LogInfo("Actual attack indicator proof: "+detail+"; native equipment/camera restored, selected slot="+(gameplay==null?-1:gameplay.SelectedSlot)+"/"+originalSlot+"; observed native attacks="+completedWeapons+".");
            report("minecraft-attack-cooldown",result,result?null:detail);
            return true;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;NeutralAttack();ReleaseSlots();
            if(captured&&!complete&&owner!=null&&owner.playerWeapon!=null)
            {
                owner.playerWeapon.StopAttackRoutine();owner.playerWeapon.SetWeapon(originalWeapon,originalLevel);
                if(camera.FirstPerson!=originalFirstPerson)camera.Toggle();
                // Normal Check completes real-key slot restoration before reporting.
                // An interrupted test must not leave a synthetic key held at teardown.
                if(gameplay!=null&&gameplay.SelectedSlot!=originalSlot&&logger!=null)logger.LogWarning("Interrupted isolated cooldown probe before native digit-slot cleanup completed.");
            }
        }
    }
}
