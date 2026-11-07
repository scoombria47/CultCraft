using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CultCraft
{
    // Only entered by the isolated Melty scenario. UI input is real virtual-device
    // input; source profiles/PNGs are fetched live, not fixture substitutes.
    internal sealed class SkinSelfTest : IDisposable
    {
        private readonly PlayerSkins skins;
        private readonly MinecraftAvatar avatar;
        private readonly CameraRig camera;
        private readonly Func<PlayerFarming,MinecraftAvatar> find;
        private readonly Action<string,bool,string> report;
        private readonly MonoBehaviour host;
        private readonly bool localCoopOnly;
        private Action restore;
        private PlayerFarming goat;
        private bool started;
        private bool removalCaptured,originalRemoval;
        public bool Complete { get; private set; }
        public SkinSelfTest(MonoBehaviour host,PlayerSkins skins,MinecraftAvatar avatar,CameraRig camera,Func<PlayerFarming,MinecraftAvatar> find,Action<string,bool,string> report,bool localCoopOnly)
        {this.host=host;this.skins=skins;this.avatar=avatar;this.camera=camera;this.find=find;this.report=report;this.localCoopOnly=localCoopOnly;}
        public void Tick(PlayerFarming player,bool ready)
        {
            if(started||!ready)return;started=true;host.StartCoroutine(Drive(localCoopOnly?RunLocalCoop(player):Run(player)));
        }
        private IEnumerator Drive(IEnumerator routine)
        {
            string error=null;
            while(true)
            {
                object step=null;bool next=false;
                try{next=routine.MoveNext();if(next)step=routine.Current;}catch(Exception ex){error=ex.Message;}
                if(error!=null||!next)break;
                yield return step;
            }
            Dispose();Complete=true;
            if(localCoopOnly)
                report("minecraft-local-coop",error==null,error??"Native Goat joined in the same settlement, both assigned Minecraft skins and arm widths drew in the game camera, and removing the Goat left the Lamb in play.");
            else
                report("minecraft-skins",error==null,error??"Live Scoombria skin, UI text/apply, independent native co-op Alex, 3/4 pixel arm geometry, reset and join/leave rendered.");
        }

        private IEnumerator RunLocalCoop(PlayerFarming owner)
        {
            Require(PlayerFarming.players.Count==1&&owner.isLamb,"Co-op probe requires an isolated solo Lamb.");
            BeginNativeJoin();
            float joinedBy=Time.unscaledTime+15f;
            while((PlayerFarming.players.Count!=2||CoopManager.Instance.IsSpawningPlayer)&&Time.unscaledTime<joinedBy)
                yield return null;
            goat=PlayerFarming.players.Find(player=>player!=owner&&!player.isLamb);
            Require(goat!=null&&PlayerSkins.Role(goat)==1&&CoopManager.CoopActive,"Native Goat failed to join the shared settlement.");

            // Let the shared camera render both characters for the footage and draw counters.
            yield return new WaitForSecondsRealtime(8f);
            MinecraftAvatar first=find(owner),second=find(goat);
            Require(first!=second&&MatchesProfile(first,skins.Get(0))&&MatchesProfile(second,skins.Get(1)),
                "The two Minecraft avatars did not draw their assigned profiles. Lamb: "+Describe(first)+". Goat: "+Describe(second));

            CoopManager.RemovePlayerFromMenu();
            float leftBy=Time.unscaledTime+12f;
            while(PlayerFarming.players.Count!=1&&Time.unscaledTime<leftBy)yield return null;
            yield return null;
            Require(PlayerFarming.players.Count==1&&PlayerFarming.players[0]==owner&&!CoopManager.CoopActive&&
                first.WorldVisible&&!second.WorldVisible,"Leaving co-op did not restore the visible solo Lamb.");
            goat=null;
        }

        private static bool MatchesProfile(MinecraftAvatar model,PlayerSkins.Choice choice)
        {
            return model!=null&&model.WorldVisible&&model.LambHidden&&model.SkinTexture==choice.Texture&&
                model.SlimArms==choice.Slim&&Mathf.Abs(model.ArmMeshWidth-(choice.Slim?3f:4f))<0.01f&&model.SkinRenderFrames>=12;
        }

        private void BeginNativeJoin()
        {
            originalRemoval=CoopManager.Instance.temporaryDisableRemoval;removalCaptured=true;
            // The visual probe has no second physical controller. Keep the native actor
            // alive in the lab using the game's existing temporary removal guard.
            CoopManager.Instance.temporaryDisableRemoval=true;
            CoopManager.Instance.SpawnCoopPlayer(1,false);
        }

        private IEnumerator Run(PlayerFarming owner)
        {
            if(PlayerFarming.players.Count!=1)throw new Exception("Skin probe requires isolated solo start.");
            Action a=Snapshot(skins.Get(0)),b=Snapshot(skins.Get(1));restore=delegate{a();b();};
            skins.Reset(0);skins.Reset(1);
            Open();yield return new WaitForSecondsRealtime(0.3f);
            yield return TypeNickname();yield return Click(camera.SkinUi.ApplySkinRect);
            yield return Wait(()=>skins.Get(0).Nickname=="Scoombria",50f);
            Require(skins.Get(0).Nickname=="Scoombria","Live Scoombria lookup/apply failed: "+skins.Get(0).Status);
            Close();yield return new WaitForSecondsRealtime(2f);
            Require(avatar.SkinTexture==skins.Get(0).Texture&&!avatar.SlimArms&&avatar.SkinRenderFrames>=12,"Scoombria classic skin was not drawn.");
            BeginNativeJoin();
            yield return Wait(()=>PlayerFarming.players.Count>1&&!CoopManager.Instance.IsSpawningPlayer,12f);
            foreach(var player in PlayerFarming.players)if(player!=owner)goat=player;
            Require(goat!=null&&PlayerSkins.Role(goat)==1,"Native co-op Goat did not join.");
            yield return new WaitForSecondsRealtime(2f);
            MinecraftAvatar model=find(goat);
            Require(model!=null&&model.WorldVisible&&model.LambHidden&&model.SlimArms&&Mathf.Abs(model.ArmMeshWidth-3f)<0.01f&&model.SkinRenderFrames>=12,"Native co-op Alex slim geometry did not draw: "+Describe(model));
            Open();yield return new WaitForSecondsRealtime(0.2f);yield return Click(camera.SkinUi.SkinRoleRect);
            yield return TypeNickname();yield return Click(camera.SkinUi.ApplySkinRect);
            yield return Wait(()=>skins.Get(1).Nickname=="Scoombria",50f);
            Close();yield return new WaitForSecondsRealtime(2f);
            Require(skins.Get(1).Nickname=="Scoombria"&&!model.SlimArms&&Mathf.Abs(model.ArmMeshWidth-4f)<0.01f&&model.SkinRenderFrames>=12,"Goat did not independently adopt classic custom skin.");
            Open();yield return new WaitForSecondsRealtime(0.2f);
            yield return Click(camera.SkinUi.SkinArmsRect);yield return Click(camera.SkinUi.SkinArmsRect);
            Close();yield return new WaitForSecondsRealtime(2f);
            Require(model.SlimArms&&Mathf.Abs(model.ArmMeshWidth-3f)<0.01f&&!avatar.SlimArms,"Manual slim override leaked to Lamb or did not change Goat geometry.");
            Open();yield return new WaitForSecondsRealtime(0.2f);yield return Click(camera.SkinUi.SkinResetRect);
            Close();yield return new WaitForSecondsRealtime(2f);
            Require(skins.Get(1).Nickname==""&&model.SlimArms&&model.SkinTexture!=avatar.SkinTexture&&model.SkinRenderFrames>=12,"Goat reset did not restore independent default Alex.");
            Open();yield return new WaitForSecondsRealtime(0.2f);yield return Click(camera.SkinUi.SkinArmsRect);
            Close();yield return new WaitForSecondsRealtime(2f);
            Require(!model.SlimArms&&Mathf.Abs(model.ArmMeshWidth-4f)<0.01f,"Manual classic override on Alex did not produce four-pixel arms.");
            skins.Reset(1);
            Open();yield return new WaitForSecondsRealtime(0.2f);
            for(int retry=0;retry<3&&camera.SkinUi.SelectedSkinRole!=0;retry++)yield return Click(camera.SkinUi.SkinRoleRect);
            Require(camera.SkinUi.SelectedSkinRole==0,"Could not select Lamb profile via UI.");
            for(int retry=0;retry<3&&skins.Get(0).Nickname!="";retry++)yield return Click(camera.SkinUi.SkinResetRect);
            Close();yield return new WaitForSecondsRealtime(2f);
            MinecraftAvatar lamb=find(owner);
            Require(skins.Get(0).Nickname==""&&lamb!=null&&!lamb.SlimArms&&Mathf.Abs(lamb.ArmMeshWidth-4f)<0.01f,"Lamb reset did not restore Steve: nickname="+skins.Get(0).Nickname+", selected role="+camera.SkinUi.SelectedSkinRole+", "+Describe(lamb));
        }
        private static Action Snapshot(PlayerSkins.Choice c)
        {string name=c.Nickname,status=c.Status,config=c.NickConfig.Value;int arms=c.Arms,ac=c.ArmsConfig.Value;bool slim=c.SourceSlim;Texture2D tex=c.Texture;
            return delegate{c.Request++;c.Nickname=name;c.Status=status;c.Texture=tex;c.SourceSlim=slim;c.Arms=arms;c.NickConfig.Value=config;c.ArmsConfig.Value=ac;c.Revision++;};}
        private void Open(){if(!camera.SettingsOpen)camera.ToggleSettings();}
        private void Close(){if(camera.SettingsOpen)camera.ToggleSettings();}
        private static string Describe(MinecraftAvatar model){return model==null?"model absent":"world="+model.WorldVisible+", native hidden="+model.LambHidden+", slim="+model.SlimArms+", solid arm width="+model.ArmMeshWidth+", draws="+model.SkinRenderFrames;}
        private static void Require(bool value,string error){if(!value)throw new Exception(error);}
        private static IEnumerator Wait(Func<bool> condition,float timeout){float until=Time.unscaledTime+timeout;while(!condition()&&Time.unscaledTime<until)yield return null;}
        private static IEnumerator Click(Rect rect)
        {MeltyPlay.Point(rect.center.x/Screen.width,rect.center.y/Screen.height);yield return new WaitForSecondsRealtime(0.12f);MeltyPlay.Button(0,true);yield return new WaitForSecondsRealtime(0.12f);MeltyPlay.Button(0,false);yield return new WaitForSecondsRealtime(0.18f);}
        private IEnumerator TypeNickname()
        {
            yield return Click(camera.SkinUi.NicknameRect);
            foreach(char c in "Scoombria"){InputSystem.QueueTextEvent(Keyboard.current,c);yield return null;}
            yield return new WaitForSecondsRealtime(0.2f);
        }
        public void Dispose()
        {
            MeltyPlay.Button(0,false);Close();
            if(goat!=null){CoopManager.RemovePlayerFromMenu();goat=null;}
            if(removalCaptured&&CoopManager.Instance!=null){CoopManager.Instance.temporaryDisableRemoval=originalRemoval;removalCaptured=false;}
            if(restore!=null){restore();restore=null;}
        }
    }
}
