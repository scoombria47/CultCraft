using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CultCraft
{
    // Actual native resources and callbacks, confined to SelfTest's memory-only world.
    internal sealed class RepairSelfTest : IDisposable
    {
        private readonly MinecraftAssets assets;
        private readonly ManualLogSource logger;
        private float started=-1f,phaseSince;
        private int phase,voicesBefore,unknownBefore,sermonBefore;
        private int silencedBefore;
        private bool blessEnded;
        private GameObject orb,wild,controllerObject;
        private PickUp camellia;
        private Action nativeCallback;
        private Collider2D[] wildColliders;
        private bool[] wildTriggers;
        public bool Complete { get; private set; }
        public bool Passed { get; private set; }
        public string Detail { get; private set; }
        public RepairSelfTest(MinecraftAssets assets,ManualLogSource logger){this.assets=assets;this.logger=logger;}
        public void Tick(PlayerFarming player)
        {
            if(Complete||player==null||player.state==null)return;
            if(started<0f){started=phaseSince=Time.unscaledTime;}
            if(Time.unscaledTime-started>25f){Finish(false,"New regression probe timed out: phase="+phase+", "+Detail);return;}
            if(phase==0)
            {
                if(player.state.CURRENT_STATE!=StateMachine.State.Idle)return;
                voicesBefore=SermonAudio.Current.ReplacedVoiceStems;unknownBefore=SermonAudio.Current.UnknownStems;
                silencedBefore=SermonAudio.Current.SilencedVoiceStems;
                // Actual blessing emits this event before the bless animation.
                AudioManager.Instance.PlayOneShot("event:/player/speak_to_follower",player.transform.position);
                player.CustomAnimationWithCallback("bless",false,delegate{blessEnded=true;if(player!=null&&player.state.CURRENT_STATE==StateMachine.State.CustomAnimation)player.state.CURRENT_STATE=StateMachine.State.Idle;});
                phase=1;phaseSince=Time.unscaledTime;return;
            }
            if(phase==1)
            {
                if(!blessEnded||SermonAudio.Current.ReplacedVoiceStems<=voicesBefore)return;
                if(SermonAudio.Current.SilencedVoiceStems<=silencedBefore){Detail="Blessing vocal has no persistent native-channel DSP silencer";return;}
                if(SermonAudio.Current.UnknownStems!=unknownBefore){Finish(false,"Blessing emitted an unclassified native vocal stem.");return;}
                if(DevotionVisuals.Current==null||DevotionVisuals.Current.ExperienceSoundsPlayed<2){Detail="actual naturally collected XP fixtures did not play Minecraft pickup audio";return;}
                controllerObject=new GameObject("CultCraft isolated native sermon callback owner");
                SermonController controller=controllerObject.AddComponent<SermonController>();
                MethodInfo increment=AccessTools.Method(typeof(SermonController),"IncrementXPBar");
                nativeCallback=(Action)Delegate.CreateDelegate(typeof(Action),controller,increment);
                sermonBefore=DevotionVisuals.Current.SermonOrbs;
                orb=SoulCustomTarget.Create(player.CrownBone.gameObject,player.transform.position+new Vector3(1f,-1f,0f),Color.white,nativeCallback);
                InventoryItem.Spawn(InventoryItem.ITEM_TYPE.FLOWERS,1,player.transform.position+new Vector3(2f,-1f,0f),4f,pickup=>{camellia=pickup;if(pickup!=null){pickup.CanBePickedUp=false;pickup.MagnetToPlayer=false;}});
                ObjectPool.Spawn("Assets/Prefabs/Structures/Other/SacredFlowers.prefab",player.transform.position+new Vector3(-3f,0f,0f),Quaternion.identity,player.transform.parent,created=>
                {wild=created;wildColliders=created.GetComponentsInChildren<Collider2D>(true);wildTriggers=new bool[wildColliders.Length];for(int i=0;i<wildColliders.Length;i++)wildTriggers[i]=wildColliders[i].isTrigger;
                    foreach(MonoBehaviour component in created.GetComponentsInChildren<MonoBehaviour>(true))if(component!=null)logger.LogInfo("Actual wild SacredFlowers component: "+component.GetType().FullName);
                    foreach(DropLootOnDeath drop in created.GetComponentsInChildren<DropLootOnDeath>(true))logger.LogInfo("Actual wild SacredFlowers native source: "+drop.name+", local loot="+drop.LootToDrop+", natural="+drop.IsNaturalResource+", native structure="+(drop.Structure==null?"none":drop.Structure.Type.ToString()));},true);
                phase=2;phaseSince=Time.unscaledTime;return;
            }
            if(phase==2)
            {
                DevotionOrbVisual visual=orb==null?null:orb.GetComponent<DevotionOrbVisual>();
                if(visual==null||visual.SuccessfulGameDraws<2||!visual.NativeArtSuppressed||!visual.UsesMinecraftAtlas||DevotionVisuals.Current.SermonOrbs<=sermonBefore)
                {Detail="actual sermon callback orb has not rendered its Minecraft atlas twice";return;}
                object retained=AccessTools.Field(typeof(SoulCustomTarget),"Callback").GetValue(orb.GetComponent<SoulCustomTarget>());
                if(!ReferenceEquals(retained,nativeCallback)){Finish(false,"Native sermon callback changed.");return;}
                // Do not invoke an out-of-sermon doctrine UI callback. Its native
                // visual routing was observed; normal GetSoul collection/audio
                // is covered separately by the two actual pooled XP lifetimes.
                ObjectPool.Recycle(orb);orb=null;phase=3;
            }
            if(phase==3)
            {
                if(wild==null||WorldResourceVisuals.Current==null){Detail="waiting for actual addressable wild SacredFlowers prefab";return;}
                PickupFirstDrawProbe first=camellia==null?null:camellia.GetComponentInChildren<PickupFirstDrawProbe>(true);
                if(first==null||!first.FirstGameDrawHadMinecraft||first.FirstGameDrawSprite==null||first.FirstGameDrawSprite.texture!=assets.Texture("poppy"))
                {Detail="actual FLOWERS19 camellia drop has not rendered as a poppy in its first game frame";return;}
                GameObject model=WorldResourceVisuals.Current.VisualFor(wild);
                if(model==null||!model.activeInHierarchy||WorldResourceVisuals.Current.ProfileFor(wild)!="rose_bush_full")
                {Detail="actual wild SacredFlowers has no full rose replacement";return;}
                bool lower=false,upper=false;
                foreach(MeshRenderer mesh in model.GetComponentsInChildren<MeshRenderer>())
                {if(mesh.sharedMaterial!=null){lower|=mesh.sharedMaterial.mainTexture==assets.Texture("rose_bush_bottom");upper|=mesh.sharedMaterial.mainTexture==assets.Texture("rose_bush_top");}}
                if(!lower||!upper){Detail="wild rose misses one of its original Minecraft halves";return;}
                Collider2D[] current=wild.GetComponentsInChildren<Collider2D>(true);
                if(current.Length!=wildColliders.Length){Finish(false,"Wild flower collider count changed.");return;}
                for(int i=0;i<current.Length;i++)if(current[i]!=wildColliders[i]||current[i].isTrigger!=wildTriggers[i]){Finish(false,"Wild flower collider identity/state changed.");return;}
                Finish(true,"actual pre-animation blessing voice replaced; real sermon IncrementXPBar callback orb retained and rendered Minecraft atlas in game-camera frames; actual wild SacredFlowers addressable uses both rose halves and preserves colliders; naturally collected XP sound plays="+DevotionVisuals.Current.ExperienceSoundsPlayed);
            }
        }
        private void Finish(bool pass,string detail){Complete=true;Passed=pass;Detail=detail;logger.LogInfo("Actual0.7 regression proof: "+detail);DisposeOwned();}
        private void DisposeOwned(){foreach(GameObject value in new[]{orb,wild,camellia==null?null:camellia.gameObject})if(value!=null){if(ObjectPool.IsSpawned(value))ObjectPool.Recycle(value);else UnityEngine.Object.Destroy(value);}orb=wild=null;camellia=null;if(controllerObject!=null)UnityEngine.Object.Destroy(controllerObject);controllerObject=null;}
        public void Dispose(){DisposeOwned();}
    }
}
