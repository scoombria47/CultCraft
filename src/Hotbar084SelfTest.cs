using System;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.DeathScreen;
using MMTools;
using UnityEngine;

namespace CultCraft
{
    internal sealed class Hotbar084SelfTest
    {
        private readonly Gameplay gameplay;
        private readonly MinecraftAvatar avatar;
        private readonly Action<string,bool,string> report;
        private int step,shots,power,draws;
        private float since,nextDialogue;
        private bool wood,coin,dialogue;
        private GameObject plot;
        private PlayerFarming deadPlayer;
        internal bool SafeComplete,DungeonComplete,ReturnComplete;

        internal Hotbar084SelfTest(Gameplay gameplay,MinecraftAvatar avatar,Action<string,bool,string> report)
        {
            this.gameplay=gameplay;
            this.avatar=avatar;
            this.report=report;
        }

        internal void TickSafe(PlayerFarming player,bool ready)
        {
            if(SafeComplete||(!ready&&step==0))return;
            if(step==0)
            {
                MeltyPlay.Point(0.6f,0.38f);
                MeltyPlay.Hold("1",true);
                Advance(1);
                return;
            }
            if(Time.unscaledTime-since>12f)
            {
                MeltyPlay.Hold("1",false);
                if(step<=5)report("minecraft-safe-hand",false,"Picker or real held sword did not respond, step "+step);
                report("minecraft-construction-materials",false,"Native bed work missing its recipe items, step "+step+", wood="+wood+", coin="+coin);
                SafeComplete=true;
                return;
            }
            switch(step)
            {
                case 1:
                case 4:
                    if(!gameplay.HandPickerOpen)return;
                    MeltyPlay.Scroll(120f);
                    Advance(step+1);
                    break;
                case 2:
                case 5:
                    if(Time.unscaledTime-since<0.2f)return;
                    MeltyPlay.Hold("1",false);
                    Advance(step+1);
                    break;
                case 3:
                    if(gameplay.HandPickerOpen||gameplay.SelectedItemId.Length>0||avatar.HeldItemId.Length>0)return;
                    MeltyPlay.Hold("1",true);
                    Advance(4);
                    break;
                case 6:
                    if(gameplay.HandPickerOpen||!avatar.HeldItemId.EndsWith("_sword",StringComparison.Ordinal))return;
                    report("minecraft-safe-hand",true,"Held digit 1 and real wheel selected empty hand and then restored the sword on release.");
                    var design=StructuresData.GetInfoByType(StructureBrain.TYPES.BUILD_SITE,0);
                    design.ToBuildType=StructureBrain.TYPES.BED;
                    StructureManager.BuildStructure(FollowerLocation.Base,design,player.transform.position+Vector3.right*2.5f,
                        new Vector2Int(2,2),false,created=>plot=created,null,false);
                    Advance(7);
                    break;
                case 7:
                    if(plot==null)return;
                    var interaction=plot.GetComponentInChildren<Interaction_PlayerBuild>();
                    if(interaction==null)return;
                    draws=avatar.HeldRenderFrames;
                    interaction.OnInteract(player.state);
                    Advance(8);
                    break;
                case 8:
                    if(avatar.AnimationPose=="build"&&avatar.HeldRenderFrames>draws)
                    {
                        wood|=avatar.HeldItemId=="oak_log";
                        coin|=avatar.HeldItemId==Gameplay.ResourceTextureId((int)InventoryItem.ITEM_TYPE.BLACK_GOLD);
                    }
                    if(!wood||!coin)return;
                    report("minecraft-construction-materials",true,"Native bed build displayed both required materials in the game camera: oak log and emerald.");
                    SafeComplete=true;
                    break;
            }
        }

        internal void TickDungeon(PlayerFarming player,bool ready)
        {
            if(DungeonComplete||(!ready&&step!=10))return;
            if(step<10)
            {
                player.playerWeapon.SetWeapon(EquipmentType.Sword,1);
                power=gameplay.BowEnchantmentLevel;
                player.playerWeapon.SetWeapon(EquipmentType.Blunderbuss,1);
                gameplay.SelectSlot(0);
                shots=gameplay.ShotsFired;
                draws=avatar.HeldRenderFrames;
                Advance(10);
            }
            if(Time.unscaledTime-since>12f){FinishCrossbow(false,"Timeout at stage "+step+", shots="+(gameplay.ShotsFired-shots)+", ammo="+gameplay.CrossbowAmmo+", item="+avatar.HeldItemId);return;}
            switch(step)
            {
                case 10:
                    if(avatar.HeldItemId!="crossbow_arrow"||gameplay.CrossbowAmmo!=3||avatar.HeldRenderFrames<=draws)return;
                    if(player.playerWeapon.blunderAmmo!=null&&player.playerWeapon.blunderAmmo.gameObject.activeSelf)
                    {FinishCrossbow(false,"Native firearm HUD is still visible for the crossbow.");return;}
                    MeltyPlay.Hold("1",true);
                    Advance(11);
                    break;
                case 11:
                    if(Time.unscaledTime-since<0.4f)return;
                    MeltyPlay.Scroll(120f);
                    MeltyPlay.Hold("1",false);
                    // Retain the ordinary combat slot wheel binding after checking the picker is unavailable.
                    if(gameplay.HandPickerOpen){FinishCrossbow(false,"Empty hand picker opened in combat.");return;}
                    Advance(12);
                    break;
                case 12:
                    gameplay.SelectSlot(0);
                    MeltyPlay.Button(0,true);
                    Advance(13);
                    break;
                case 13:
                    if(gameplay.CrossbowAmmo!=0)return;
                    MeltyPlay.Button(0,false);
                    if(gameplay.ShotsFired-shots!=3){FinishCrossbow(false,"Magazine exhausted with the wrong arrow count.");return;}
                    Advance(14);
                    break;
                case 14:
                    if(!avatar.HeldItemId.StartsWith("crossbow_pulling_",StringComparison.Ordinal))return;
                    gameplay.SelectSlot(1);
                    MeltyPlay.Button(0,true);
                    Advance(15);
                    break;
                case 15:
                    if(gameplay.ShotsFired-shots<4)return;
                    MeltyPlay.Button(0,false);
                    if(gameplay.CrossbowAmmo!=0||gameplay.BowEnchantmentLevel!=power||!gameplay.BowHeld)
                    {FinishCrossbow(false,"The secondary bow inherited the magazine or forced power bonus.");return;}
                    gameplay.SelectSlot(0);
                    Advance(16);
                    break;
                case 16:
                    if(gameplay.CrossbowAmmo!=3||avatar.HeldItemId!="crossbow_arrow")return;
                    FinishCrossbow(true,"Three primary arrows, visible crossbow reload, independent secondary bow during reload, and three charges restored.");
                    break;
            }
        }

        internal void BeginReturn(PlayerFarming player)
        {
            deadPlayer=player;
            Advance(20);
        }

        internal void TickReturn(PlayerFarming player,bool ready)
        {
            if(ReturnComplete)return;
            if(Time.unscaledTime-since>65f)
            {
                FinishReturn(false,"Return or held item timed out at stage "+step+", location="+PlayerFarming.Location+", dialogue="+dialogue+", item="+avatar.HeldItemId);
                return;
            }
            if(step==20)
            {
                var overlay=UIDeathScreenOverlayController.Instance;
                if(overlay!=null)
                {
                    var button=AccessTools.Field(typeof(UIDeathScreenOverlayController),"_continueButton").GetValue(overlay) as MMButton;
                    if(button!=null&&button.enabled&&button.interactable){button.onClick.Invoke();step=21;}
                }
                return;
            }
            if(step==21)
            {
                if(DeathCatRoomManager.Instance!=null&&DeathCatRoomManager.Instance.gameObject.activeInHierarchy&&MMConversation.isPlaying&&
                    MMConversation.mmConversation!=null&&Time.unscaledTime>=nextDialogue)
                {
                    dialogue=true;
                    nextDialogue=Time.unscaledTime+0.6f;
                    AccessTools.Method(typeof(MMConversation),"HandleNextClicked").Invoke(MMConversation.mmConversation,null);
                }
                if(!ready||player==deadPlayer||PlayerFarming.Location!=FollowerLocation.Base)return;
                if(gameplay.SelectedSlot!=0||!avatar.HeldItemId.EndsWith("_sword",StringComparison.Ordinal))return;
                draws=avatar.BroomDraws;
                player.CustomAnimation("cleaning",true);
                step=22;
                return;
            }
            if(avatar.AnimationPose=="sweep"&&avatar.BroomDraws>draws&&avatar.HeldItemId==BroomVisual.ForLevel(DataManager.Instance.ChoreXPLevel).Id)
            {
                player.state.CURRENT_STATE=StateMachine.State.Idle;
                FinishReturn(true,"Native death Continue and base arrival reset slot 1, rendered the sword, and rendered a temporary broom on the returned actor.");
            }
        }

        private void FinishCrossbow(bool pass,string detail)
        {
            MeltyPlay.Button(0,false);
            MeltyPlay.Hold("1",false);
            gameplay.SelectSlot(1);
            report("minecraft-crossbow-magazine",pass,detail);
            DungeonComplete=true;
        }

        private void FinishReturn(bool pass,string detail)
        {
            report("minecraft-return-held-items",pass,detail);
            ReturnComplete=true;
        }

        private void Advance(int value){step=value;since=Time.unscaledTime;}
    }
}
