using System;
using System.Collections.Generic;
using HarmonyLib;
using Spine.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CultCraft
{
    internal sealed class CinematicActors : IDisposable
    {
        private readonly MinecraftAssets assets;
        private readonly PlayerSkins skins;
        private readonly Dictionary<GameObject,MinecraftAvatar> actors=new Dictionary<GameObject,MinecraftAvatar>();
        private readonly Dictionary<GameObject,int> roles=new Dictionary<GameObject,int>();
        private readonly HashSet<GameObject> seen=new HashSet<GameObject>();
        private readonly List<GameObject> removed=new List<GameObject>();
        private IntroDeathSceneManager intro;
        private GameObject pentagramActor;
        private static readonly System.Reflection.FieldInfo DeathPlayer=AccessTools.Field(typeof(DeathCatRoomManager),"Player");
        private static readonly System.Reflection.FieldInfo RespawnPlayer=AccessTools.Field(typeof(RespawnRoomManager),"Player");
        private static readonly System.Reflection.FieldInfo IntroPlayer=AccessTools.Field(typeof(IntroDeathSceneManager),"Player");
        private int lastRole;
        internal int ActorsCount {get{return actors.Count;}}
        internal int DrawFrames;
        internal CinematicActors(MinecraftAssets assets,PlayerSkins skins)
        {
            this.assets=assets;this.skins=skins;
            intro=UnityEngine.Object.FindObjectOfType<IntroDeathSceneManager>();
            SceneManager.sceneLoaded+=SceneLoaded;
        }
        private void SceneLoaded(Scene scene,LoadSceneMode mode){intro=UnityEngine.Object.FindObjectOfType<IntroDeathSceneManager>();}
        internal bool Owns(GameObject actor){return actor!=null&&actors.ContainsKey(actor);}
        internal MinecraftAvatar For(GameObject actor){MinecraftAvatar model;return actor!=null&&actors.TryGetValue(actor,out model)?model:null;}
        internal void Tick()
        {
            foreach(var player in PlayerFarming.players)
                if(player!=null&&player.isLamb&&player.gameObject.activeInHierarchy&&!Owns(player.gameObject))lastRole=PlayerSkins.Role(player);
            seen.Clear();
            pentagramActor=Observe(DeathCatRoomManager.Instance,DeathPlayer);
            Observe(RespawnRoomManager.Instance,RespawnPlayer);
            Observe(intro,IntroPlayer);
            removed.Clear();foreach(var pair in actors)if(pair.Key==null||!seen.Contains(pair.Key)){pair.Value.Dispose();removed.Add(pair.Key);}
            foreach(var key in removed){actors.Remove(key);roles.Remove(key);}
        }
        private GameObject Observe(Component manager,System.Reflection.FieldInfo field)
        {
            if(manager==null||!manager.gameObject.activeInHierarchy||field==null)return null;
            GameObject actor=field.GetValue(manager) as GameObject;
            if(actor==null||!actor.activeInHierarchy||!actor.scene.IsValid())return null;
            SkeletonAnimation source=Source(actor);if(source==null)return null;
            seen.Add(actor);
            if(!actors.ContainsKey(actor)){actors.Add(actor,new MinecraftAvatar(assets));roles.Add(actor,lastRole);}
            return actor;
        }
        private static SkeletonAnimation Source(GameObject actor)
        {
            var farming=actor.GetComponent<PlayerFarming>();if(farming!=null&&farming.Spine!=null)return farming.Spine;
            var prisoner=actor.GetComponent<PlayerPrisonerController>();return prisoner!=null?prisoner.Spine:actor.GetComponentInChildren<SkeletonAnimation>(true);
        }
        internal void LateTick()
        {
            foreach(var pair in actors)
            {
                if(pair.Key==null)continue;var c=skins.Get(roles[pair.Key]);pair.Value.SetSkin(c.Texture,c.Slim);
                pair.Value.TickCinematic(pair.Key,Source(pair.Key),pair.Key.GetComponent<StateMachine>(),pair.Key==pentagramActor);
                DrawFrames=Math.Max(DrawFrames,pair.Value.SkinRenderFrames);
            }
        }
        public void Dispose(){SceneManager.sceneLoaded-=SceneLoaded;foreach(var model in actors.Values)model.Dispose();actors.Clear();roles.Clear();}
    }
}
