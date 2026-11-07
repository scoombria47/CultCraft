using System;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    internal sealed class WeaponPresentationSelfTest
    {
        private readonly MinecraftAvatar avatar;private readonly ManualLogSource logger;
        private readonly EquipmentType[] types={EquipmentType.Sword,EquipmentType.Axe,EquipmentType.Hammer,EquipmentType.Blunderbuss};
        private PlayerFarming owner;private EquipmentType saved;private int savedLevel;
        private int step,phase,draws;private float since,equippedAt;private bool ended,shown;
        public bool Complete {get;private set;}public bool Passed {get;private set;}public string Detail {get;private set;}
        public WeaponPresentationSelfTest(MinecraftAvatar avatar,ManualLogSource logger){this.avatar=avatar;this.logger=logger;}
        public void Tick(PlayerFarming player,Gameplay gameplay)
        {
            if(Complete||player==null||player.playerWeapon==null||player.state==null)return;
            if(owner==null){owner=player;saved=player.currentWeapon;savedLevel=Math.Max(1,player.currentWeaponLevel);since=Time.unscaledTime;}
            if(Time.unscaledTime-since>35f){Finish(false,"Native pickup clip timed out: "+types[step]+", phase="+phase);return;}
            if(phase==0)
            {
                if(player.state.CURRENT_STATE!=StateMachine.State.Idle)return;
                player.playerWeapon.SetWeapon(types[step],1);equippedAt=Time.unscaledTime;phase=1;return;
            }
            if(phase==1)
            {
                if(Time.unscaledTime-equippedAt<0.6f||player.currentWeapon!=types[step]||player.state.CURRENT_STATE!=StateMachine.State.Idle)return;
                string clip=EquipmentManager.GetEquipmentData(player.currentWeapon).PickupAnimationKey;
                if(String.IsNullOrEmpty(clip)){Finish(false,"Actual equipment has no pickup clip: "+player.currentWeapon);return;}
                draws=avatar.WeaponPresentationDraws;ended=shown=false;
                player.CustomAnimationWithCallback(clip,false,delegate{ended=true;if(player!=null&&player.state.CURRENT_STATE==StateMachine.State.CustomAnimation)player.state.CURRENT_STATE=StateMachine.State.Idle;});
                logger.LogInfo("Actual native weapon presentation clip: "+types[step]+" -> "+clip+", expected Minecraft="+gameplay.WeaponId);phase=2;return;
            }
            shown|=player.currentWeapon==types[step]&&avatar.AnimationPose=="weapon-pickup"&&avatar.HeldItemId==gameplay.WeaponId&&gameplay.SelectedSlot==0&&avatar.WeaponPresentationDraws>=draws+2;
            if(!ended)return;
            if(!shown){Finish(false,"Actual native weapon pickup did not show equipped Minecraft item: "+types[step]+", held="+avatar.HeldItemId+", expected="+gameplay.WeaponId+", drawn="+(avatar.WeaponPresentationDraws-draws));return;}
            logger.LogInfo("Actual native weapon presentation observed: "+types[step]+" -> "+gameplay.WeaponId+", game frames="+(avatar.WeaponPresentationDraws-draws)+", selected slot0");
            step++;phase=0;if(step==types.Length)Finish(true,"Native Sword/Axe/Hammer/Blunderbuss pickup clips rendered corresponding Minecraft held meshes and selected weapon slot");
        }
        private void Finish(bool pass,string detail){Complete=true;Passed=pass;Detail=detail;logger.LogInfo("Actual native weapon presentation proof: "+detail);if(owner!=null&&owner.playerWeapon!=null)owner.playerWeapon.SetWeapon(saved,savedLevel);}
    }
}
