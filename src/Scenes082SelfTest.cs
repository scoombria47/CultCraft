using System;
using System.Collections;
using BepInEx.Logging;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.DeathScreen;
using UnityEngine;
using src.UI.Menus;
using src.Extensions;

namespace CultCraft
{
    internal sealed class Scenes082SelfTest : IDisposable
    {
        private readonly MonoBehaviour host;
        private readonly PlayerSkins skins;
        private readonly CinematicActors actors;
        private readonly KnucklebonesHeads heads;
        private readonly ManualLogSource logger;
        private readonly Action<string,bool,string> report;
        private bool started,disposed,removalCaptured,removalOriginal,sawPentagramReveal;
        private PlayerFarming goat;
        private UIKnuckleBonesController menu;
        private Action restore;
        private float deathSince=-1f,continueAt=-1f;
        internal bool HeadsComplete,DeathComplete;
        internal Scenes082SelfTest(MonoBehaviour host,PlayerSkins skins,CinematicActors actors,KnucklebonesHeads heads,ManualLogSource logger,Action<string,bool,string> report)
        {this.host=host;this.skins=skins;this.actors=actors;this.heads=heads;this.logger=logger;this.report=report;}
        internal void TickHeads(PlayerFarming player,bool ready)
        {if(started||!ready)return;started=true;host.StartCoroutine(Drive(HeadProof(player)));}
        private IEnumerator Drive(IEnumerator routine)
        {
            string error=null;
            while(true){object step=null;bool next=false;try{next=routine.MoveNext();if(next)step=routine.Current;}catch(Exception ex){error=ex.ToString();}if(error!=null||!next)break;yield return step;}
            CleanupMenu();HeadsComplete=true;report("minecraft-knucklebones-heads",error==null,error??"Real native co-op UI, independent Steve/Alex/live skin, inward-facing heads, four native emotion cues, damage tint and 3D rotation with hold/fade checked.");
        }
        private IEnumerator HeadProof(PlayerFarming owner)
        {
            var ui=UIManager.Instance;
            Require(ui!=null,"Native UI manager is unavailable.");
            if(ui.KnucklebonesTemplate==null)
            {
                var load=ui.LoadKnucklebonesAssets();
                yield return Wait(()=>load.IsCompleted,30f);
                Require(load.IsCompleted&&!load.IsFaulted&&ui.KnucklebonesTemplate!=null,"Native Knucklebones loader did not provide its template: "+load.Exception);
            }
            Action a=Snapshot(skins.Get(0)),b=Snapshot(skins.Get(1));restore=()=>{a();b();};
            skins.Reset(0);skins.Reset(1);
            removalOriginal=CoopManager.Instance.temporaryDisableRemoval;removalCaptured=true;CoopManager.Instance.temporaryDisableRemoval=true;
            CoopManager.Instance.SpawnCoopPlayer(1,false);
            yield return Wait(()=>PlayerFarming.players.Count>1&&!CoopManager.Instance.IsSpawningPlayer,12f);
            foreach(var player in PlayerFarming.players)if(player!=owner)goat=player;
            Require(goat!=null,"Native Goat did not join for actual co-op dice UI.");
            menu=ui.KnucklebonesTemplate.Instantiate();
            SimulationManager.Pause();menu.Show(owner,new KnucklebonesOpponent{IsCoopPlayer=true},0);
            // The normal show animation clears the controller's black overlay.
            var overlay=(CanvasGroup)AccessTools.Field(typeof(UIKnuckleBonesController),"_blackOverlayCanvasGroup").GetValue(menu);
            yield return Wait(()=>overlay.alpha<=0.01f,5f);
            Require(overlay.alpha<=0.01f,"Native Knucklebones fade did not uncover the game screen.");
            AccessTools.Method(typeof(UIKnuckleBonesController),"TransitionTo").Invoke(menu,new object[]{menu.GameScreen});
            yield return new WaitForSecondsRealtime(3f);
            menu.GameScreen.StopAllCoroutines();
            KBPlayer[] players=menu.GameScreen.GetComponentsInChildren<KBPlayer>();Require(players.Length==2,"Actual co-op game screen did not expose both KB players.");
            int roles=0;
            foreach(var p in players)
            {
                var binding=heads.For(p);
                Require(binding!=null&&binding.Head.Draws>=3&&binding.Source.color.a==0f,"Actual native head was not replaced and drawn.");
                Require(binding.Head.mainTexture==skins.Get(binding.Role).Texture,"Wrong default skin on a native dice head.");
                var rect=binding.Head.rectTransform;
                Vector3 screenRight=binding.Head.canvas.rootCanvas.transform.right;
                Vector3 facing=rect.TransformVector(binding.Head.Rotation*Vector3.forward).normalized;
                Require(Vector3.Dot(facing,screenRight)*(binding.Role==0?1f:-1f)>0.1f,
                    "Dice head does not face inward after native canvas transforms: "+facing);
                roles|=1<<binding.Role;
            }
            Require(roles==3,"Both native profiles were not represented in the dice UI.");
            skins.Apply(0,"Scoombria");yield return Wait(()=>skins.Get(0).Nickname=="Scoombria",50f);
            Require(skins.Get(0).Nickname=="Scoombria","Live public skin lookup failed for dice head.");
            yield return new WaitForSecondsRealtime(2f);
            foreach(var p in players){var binding=heads.For(p);Require(binding.Head.mainTexture==skins.Get(binding.Role).Texture,"Dice head failed live selected-skin binding.");}
            string[] method={"OnDiceMatched","OnDiceLost","SetWonLoop","SetLostLoop"},emotion={"happy","angry","win","lose"};
            Vector3[] range={Vector3.zero,Vector3.zero};
            for(int i=0;i<method.Length;i++)
            {
                Vector3[] initial={heads.For(players[0]).Head.Rotation.eulerAngles,heads.For(players[1]).Head.Rotation.eulerAngles};
                foreach(var p in players)AccessTools.Method(typeof(KBPlayerBase),method[i]).Invoke(p,null);
                bool shouldFlash=emotion[i]=="angry"||emotion[i]=="lose";
                var angerTracks=new Spine.TrackEntry[players.Length];
                if(shouldFlash)
                    for(int j=0;j<players.Length;j++)angerTracks[j]=heads.For(players[j]).Source.AnimationState.GetCurrent(0);
                int tintedHeads=0;
                float reactionStart=Time.unscaledTime;
                while(Time.unscaledTime-reactionStart<0.65f)
                {
                    for(int j=0;j<players.Length;j++)
                    {
                        var binding=heads.For(players[j]);
                        var head=binding.Head;
                        if(head.LastDrawHadDamageTint)tintedHeads|=1<<j;
                        if(shouldFlash&&Time.unscaledTime-reactionStart>0.08f&&binding.Source.AnimationState.GetCurrent(0)==angerTracks[j])
                            Require(head.LastDrawHadDamageTint,"Red tint ended during native anger: "+emotion[i]);
                        Vector3 next=head.Rotation.eulerAngles;
                        range[j]=Vector3.Max(range[j],new Vector3(Mathf.Abs(Mathf.DeltaAngle(initial[j].x,next.x)),Mathf.Abs(Mathf.DeltaAngle(initial[j].y,next.y)),Mathf.Abs(Mathf.DeltaAngle(initial[j].z,next.z))));
                    }
                    yield return null;
                }
                Require(tintedHeads==(shouldFlash?3:0),"Wrong red damage flash on native head meshes: "+emotion[i]+", head mask="+tintedHeads);
                foreach(var p in players){var bnd=heads.For(p);Require(bnd.Effect.Emotion==emotion[i]&&bnd.Effect.Alpha>0.99f,"Emotion did not remain visible for first second: "+emotion[i]);}
                yield return new WaitForSecondsRealtime(0.65f);
                foreach(var p in players)Require(heads.For(p).Effect.Alpha>0.1f&&heads.For(p).Effect.Alpha<0.9f,"Emotion did not fade after first second: "+emotion[i]);
                yield return new WaitForSecondsRealtime(0.5f);
                foreach(var p in players)Require(heads.For(p).Effect.Alpha==0f,"Emotion did not expire: "+emotion[i]);
                if(shouldFlash)
                {
                    yield return Wait(()=>Array.TrueForAll(players,p=>!heads.For(p).Head.LastDrawHadDamageTint),8f);
                    foreach(var p in players)Require(!heads.For(p).Head.LastDrawHadDamageTint,"Head stayed red after the reaction returned to its normal pose: "+emotion[i]);
                }
                logger.LogInfo("Native Knucklebones emotion/render/fade proof: "+emotion[i]+", both actual co-op heads.");
            }
            foreach(var motion in range)Require(motion.x>1f&&motion.y>1f&&motion.z>1f,"Native dice head did not rotate on all axes: "+motion);
            Require(heads.DrawFrames>=20&&heads.ObservedEmotions.Count==4,"Native UI cube/particle render observations incomplete.");
        }
        internal void TickDeath(PlayerFarming player)
        {
            if(DeathComplete)return;if(deathSince<0f)deathSince=Time.unscaledTime;
            var overlay=UIDeathScreenOverlayController.Instance;
            if(overlay!=null&&continueAt<0f)
            {
                var button=AccessTools.Field(typeof(UIDeathScreenOverlayController),"_continueButton").GetValue(overlay) as MMButton;
                if(button!=null&&button.enabled&&button.interactable){continueAt=Time.unscaledTime;button.onClick.Invoke();logger.LogInfo("Native death Continue clicked through actual UI event.");}
            }
            var manager=DeathCatRoomManager.Instance;
            if(manager!=null&&manager.gameObject.activeInHierarchy)
            {
                var actor=AccessTools.Field(typeof(DeathCatRoomManager),"Player").GetValue(manager) as GameObject;
                var model=actors.For(actor);
                if(model!=null&&model.WorldVisible&&model.LambHidden&&model.AnimationPose=="warp-in"&&model.Root.localScale.x<0.98f)
                    sawPentagramReveal=true;
                if(sawPentagramReveal&&model!=null&&model.WorldVisible&&model.LambHidden&&model.SkinRenderFrames>=12&&model.AnimationPose=="walk/idle")
                {
                    DeathComplete=true;report("minecraft-cutscene-player",true,"Actual death/Continue/DeathCatRoomManager actor rendered with CultCraft pentagram reveal and then idle, game draws="+model.SkinRenderFrames);return;
                }
            }
            if(Time.unscaledTime-deathSince>80f){DeathComplete=true;report("minecraft-cutscene-player",false,"Did not observe the pentagram reveal followed by idle; continued="+(continueAt>=0f)+", manager="+(manager!=null)+", reveal="+sawPentagramReveal);}
        }
        private static IEnumerator Wait(Func<bool> condition,float seconds){float end=Time.unscaledTime+seconds;while(!condition()&&Time.unscaledTime<end)yield return null;}
        private static void Require(bool value,string why){if(!value)throw new Exception(why);}
        private static Action Snapshot(PlayerSkins.Choice c)
        {
            Texture2D texture=c.Texture;bool slim=c.SourceSlim;string name=c.Nickname,status=c.Status;int arms=c.Arms;
            return ()=>{c.Request++;c.Texture=texture;c.SourceSlim=slim;c.Nickname=name;c.Status=status;c.Arms=arms;c.NickConfig.Value=name;c.ArmsConfig.Value=arms;c.Revision++;};
        }
        private void CleanupMenu()
        {
            if(menu!=null){menu.Hide(true);UnityEngine.Object.Destroy(menu.gameObject);menu=null;}SimulationManager.UnPause();
            if(goat!=null&&CoopManager.Instance!=null){CoopManager.RemovePlayerFromMenu();goat=null;}
            if(removalCaptured&&CoopManager.Instance!=null){CoopManager.Instance.temporaryDisableRemoval=removalOriginal;removalCaptured=false;}
            // Keep custom test skin through actual death scene; restore at final cleanup.
        }
        public void Dispose(){if(disposed)return;disposed=true;CleanupMenu();if(restore!=null){restore();restore=null;}}
    }
}
