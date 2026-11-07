using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Lamb.UI;
using Lamb.UI.Assets;
using UnityEngine;

namespace CultCraft
{
    // Runs only through SelfTest's marked, isolated world. All objects come from
    // the real item pool/structure factory, never a replacement-only test mesh.
    internal sealed class LootVisualSelfTest : IDisposable
    {
        private sealed class Drop
        {
            public PickUp Native;
            public InventoryItem.ITEM_TYPE Input;
            public InventoryItem.ITEM_TYPE NativeType;
            public int Quantity;
            public PickupFirstDrawProbe Probe;
            public SpriteRenderer Renderer;
            public Collider2D Collider;
            public bool Trigger;
        }
        private readonly MinecraftAssets assets;
        private readonly ManualLogSource logger;
        private readonly Action<string,bool,string> report;
        private readonly List<GameObject> owned=new List<GameObject>();
        private readonly List<Drop> drops=new List<Drop>();
        private float started=-1f;
        private bool requested,complete,failed,recycleRequested,poolRestored,poolReused,icons;
        private GameObject flowerObject;
        private Structure flower;
        private int recycledId;
        private string detail;
        private bool roseObserved;
        private string roseDetail;
        private static readonly InventoryItem.ITEM_TYPE[] ExtraTypes={InventoryItem.ITEM_TYPE.LOG_REFINED,InventoryItem.ITEM_TYPE.STONE_REFINED,InventoryItem.ITEM_TYPE.CRYSTAL,InventoryItem.ITEM_TYPE.SPIDER_WEB,InventoryItem.ITEM_TYPE.SILK_THREAD,InventoryItem.ITEM_TYPE.SHELL,InventoryItem.ITEM_TYPE.BEETROOT,InventoryItem.ITEM_TYPE.PUMPKIN,InventoryItem.ITEM_TYPE.CAULIFLOWER,InventoryItem.ITEM_TYPE.MUSHROOM_SMALL,InventoryItem.ITEM_TYPE.GRAPES,InventoryItem.ITEM_TYPE.HOPS,InventoryItem.ITEM_TYPE.COTTON,InventoryItem.ITEM_TYPE.YOLK,InventoryItem.ITEM_TYPE.POOP,InventoryItem.ITEM_TYPE.POOP_GLOW,InventoryItem.ITEM_TYPE.POOP_GOLD,InventoryItem.ITEM_TYPE.POOP_RAINBOW,InventoryItem.ITEM_TYPE.POOP_DEVOTION,InventoryItem.ITEM_TYPE.SEED_BEETROOT,InventoryItem.ITEM_TYPE.SEED_PUMPKIN,InventoryItem.ITEM_TYPE.SEED_CAULIFLOWER,InventoryItem.ITEM_TYPE.SEED_COTTON,InventoryItem.ITEM_TYPE.SEED_HOPS,InventoryItem.ITEM_TYPE.SEED_MUSHROOM,InventoryItem.ITEM_TYPE.SEED_SOZO,InventoryItem.ITEM_TYPE.GIFT_MEDIUM};

        public LootVisualSelfTest(MinecraftAssets assets,ManualLogSource logger,Action<string,bool,string> report)
        {this.assets=assets;this.logger=logger;this.report=report;}

        public bool Check(PlayerFarming player)
        {
            if(complete)return true;
            if(failed||player==null||PlayerFarming.Location!=FollowerLocation.Base)return false;
            if(started<0f)started=Time.unscaledTime;
            if(Time.unscaledTime-started>30f)
            {
                failed=true;report("minecraft-resource-visuals",false,"Native loot/rose proof timed out: drops="+drops.Count+", pool restored="+poolRestored+", same instance reused="+poolReused+", icons="+icons+", flower="+(flowerObject!=null)+", rose="+roseObserved+"/"+roseDetail+", "+detail);return false;
            }
            foreach(string id in new[]{"creeper_banner_pattern","globe_banner_pattern","poppy","rose_bush_bottom","rose_bush_top","rose_bush_bottom_model","rose_bush_top_model"})
                if(!assets.IsAssetReady(id)){detail="waiting for actual local source asset "+id;return false;}
            foreach(var type in ExtraTypes)if(assets.Texture(PickupVisuals.ResourceTexture(type))==null){detail="waiting for 0.8.1 source "+type;return false;}
            if(!requested)
            {
                requested=true;
                Request(InventoryItem.ITEM_TYPE.LOG,player.transform.position+new Vector3(-3f,1.5f,0f));
                Request(InventoryItem.ITEM_TYPE.STONE,player.transform.position+new Vector3(-1.5f,1.5f,0f));
                Request(InventoryItem.ITEM_TYPE.FOUND_ITEM_DECORATION,player.transform.position+new Vector3(0f,1.8f,0f));
                Request(InventoryItem.ITEM_TYPE.FOUND_ITEM_FOLLOWERSKIN,player.transform.position+new Vector3(1.5f,1.5f,0f));
                Request(InventoryItem.ITEM_TYPE.FLOWER_RED,player.transform.position+new Vector3(3f,1.5f,0f));
                for(int i=0;i<ExtraTypes.Length;i++)Request(ExtraTypes[i],player.transform.position+new Vector3((i%7-3)*0.8f,0.7f+(i/7)*0.8f,0f));
                StructuresData data=StructuresData.GetInfoByType(StructureBrain.TYPES.RED_FLOWER_BUSH,0);
                Vector3 position=player.transform.position+new Vector3(4f,0f,0f);
                StructureManager.BuildStructure(PlayerFarming.Location,data,position,new Vector2Int(Math.Max(1,data.TILE_WIDTH),Math.Max(1,data.TILE_HEIGHT)),false,
                    created=>{flowerObject=created;if(created!=null)owned.Add(created);},null,false);
                logger.LogInfo("Native loot fixtures requested through InventoryItem.Spawn(LOG,STONE,Decoration40,FollowerSkin52,Camellia55) and actual RED_FLOWER_BUSH140 BuildStructure; optional1.21.2 source ready before creation.");
                return false;
            }
            if(!icons)icons=CheckIcons();
            roseObserved=CheckRose();roseDetail=detail;
            if(drops.Count<5+ExtraTypes.Length)return false;
            foreach(Drop drop in drops)if(!CheckDrop(drop))return false;
            if(!recycleRequested)
            {
                Drop log=drops.Find(value=>value.Input==InventoryItem.ITEM_TYPE.LOG);
                if(log==null)return false;
                recycledId=log.Native.GetInstanceID();Sprite native=PickupVisuals.Current.NativeSpriteFor(log.Renderer);
                ObjectPool.Recycle(log.Native.gameObject);
                poolRestored=log.Renderer!=null&&native!=null&&log.Renderer.sprite==native&&!log.Native.gameObject.activeInHierarchy;
                recycleRequested=true;
                Request(InventoryItem.ITEM_TYPE.LOG,player.transform.position+new Vector3(-3f,1.5f,0f));
                logger.LogInfo("Native loot pool cycle: original sprite restored before pooled reuse="+poolRestored+", original native pickup id="+recycledId);
                return false;
            }
            if(drops.Count<6+ExtraTypes.Length)return false;
            Drop reused=drops.Find(value=>value.Input==InventoryItem.ITEM_TYPE.LOG&&value.Probe!=null&&value.Probe.Lifetimes>=2);
            poolReused=reused!=null&&reused.Native!=null&&reused.Native.GetInstanceID()==recycledId;
            if(!poolRestored||!poolReused||!icons||!roseObserved||!CheckRecolours())return false;
            if(!EnsureCatalog())return false;
            foreach(Drop drop in drops)if(drop.Native!=null){drop.Native.CanBePickedUp=true;drop.Native.MagnetToPlayer=false;}
            complete=true;
            logger.LogInfo("0.8.1 native resource proof PASS: all26 additional native pooled pickups drawn with correct replacement before first camera frame, volume/spin and native identities/quantities/colliders retained; dyes from verified1.21.9.");
            logger.LogInfo("Native loot/rose proof PASS: actual initial pool/factory sprites correct before first game-camera draw, Decoration3 vs FollowerSkin6 actual native card discriminants retained despite PickUp.typeNONE0; 1.21.2 globe/creeper distinct source textures; Camellia55→poppy; ALT164 icon→globe; exact native sprite restored then same LOG pool instance reused with correct first draw; actual RED_FLOWER_BUSH140 has two original Minecraft rose model halves and retained native brain/colliders.");
            return true;
        }
        // This class exists only inside the marker-guarded isolated test world.
        // Populate the real inventory, with no extra button or live-save grant.
        public bool EnsureCatalog()
        {
            if(DataManager.Instance==null||Inventory.items==null)return false;
            int added=0;var ids=new HashSet<int>();
            foreach(ResourceRow row in Spec.ResourceRows)
            {
                if(!ids.Add(row.NativeId))continue;
                if(Inventory.GetItemQuantity(row.NativeId)<1){Inventory.AddItem(row.NativeId,1,true);added++;}
                if(Inventory.GetItemQuantity(row.NativeId)<1){detail="Test inventory grant failed for "+row.Id;return false;}
            }
            if(added>0)logger.LogInfo("Test-only resource catalogue: all "+ids.Count+" changed native resource identities present in actual inventory; added="+added+"; verified drop fixtures now collectible. No live save used.");
            return true;
        }
        private void Request(InventoryItem.ITEM_TYPE type,Vector3 position)
        {
            InventoryItem.Spawn(type,1,position,4f,pickup=>
            {
                if(pickup==null)return;
                // Keep only these isolated fixtures available for observation.
                // Collection itself is never invoked by this visual proof.
                pickup.MagnetToPlayer=false;pickup.CanBePickedUp=false;
                if(!owned.Contains(pickup.gameObject))owned.Add(pickup.gameObject);
                PickupFirstDrawProbe probe=pickup.GetComponentInChildren<PickupFirstDrawProbe>(true);
                SpriteRenderer renderer=probe==null?null:probe.GetComponent<SpriteRenderer>();
                Collider2D collider=pickup.GetComponent<Collider2D>();
                drops.Add(new Drop{Native=pickup,Input=type,NativeType=pickup.type,Quantity=pickup.Quantity,Probe=probe,Renderer=renderer,
                    Collider=collider,Trigger=collider!=null&&collider.isTrigger});
            });
        }
        private bool CheckRecolours()
        {
            string[] target={"chorus_lime","spider_eye_red","rainbow_dye"},source={"chorus_fruit","spider_eye","lime_dye"};
            for(int n=0;n<target.Length;n++)
            {
                Texture2D a=assets.Texture(source[n]),b=assets.Texture(target[n]);
                if(a==null||b==null||a==b||a.width!=b.width||a.height!=b.height)return false;
                Color32[] raw=a.GetPixels32(),changed=b.GetPixels32();
                var hues=new HashSet<int>();int first=Int32.MaxValue,last=Int32.MinValue;float firstHue=0f,lastHue=0f;
                for(int i=0;i<raw.Length;i++)
                {
                    if(raw[i].a!=changed[i].a){detail="Recolour changed alpha for "+target[n];return false;}
                    if(raw[i].a==0)continue;
                    float h,s,v;Color.RGBToHSV(changed[i],out h,out s,out v);
                    if(n==0&&Mathf.Abs(h-0.25f)>0.02f||n==1&&(Mathf.Min(h,1f-h)>0.02f||s<0.75f))
                    {detail="Recolour hue incorrect for "+target[n];return false;}
                    if(n==2)
                    {
                        hues.Add(Mathf.RoundToInt(h*20f));int diagonal=i%a.width+a.height-1-i/a.width;
                        if(diagonal<first){first=diagonal;firstHue=h;}if(diagonal>last){last=diagonal;lastHue=h;}
                    }
                }
                if(n==2&&(hues.Count!=8||firstHue<0.30f||firstHue>0.38f||lastHue<0.12f||lastHue>0.19f))
                {detail="Rainbow needs eight visible bands, green at top-left and yellow at bottom-right; hues="+hues.Count;return false;}
            }
            return true;
        }
        private bool CheckDrop(Drop drop)
        {
            if(drop.Native==null){detail="native drop destroyed before observation for "+drop.Input;return false;}
            // ObjectPool invokes its spawn callback before activating a newly loaded
            // prefab. Its real OnEnable postfix installs the render probe afterwards.
            // Resolve that probe after activation; its recorded first draw remains
            // authoritative even if this observer runs after the rendered frame.
            if(drop.Probe==null)drop.Probe=drop.Native.GetComponentInChildren<PickupFirstDrawProbe>(true);
            if(drop.Probe!=null)drop.Renderer=drop.Probe.GetComponent<SpriteRenderer>();
            if(drop.Probe==null||drop.Renderer==null){detail="native initialization probe not yet created for "+drop.Input;return false;}
            if(drop.Native.type!=drop.NativeType||drop.Native.Quantity!=drop.Quantity||drop.Native.GetComponent<Collider2D>()!=drop.Collider||drop.Collider!=null&&drop.Collider.isTrigger!=drop.Trigger)
            {detail="native item identity/quantity/collider changed for "+drop.Input;return false;}
            if(drop.Input==InventoryItem.ITEM_TYPE.FOUND_ITEM_DECORATION||drop.Input==InventoryItem.ITEM_TYPE.FOUND_ITEM_FOLLOWERSKIN)
            {
                FoundItemPickUp found=drop.Native.GetComponent<FoundItemPickUp>();
                var expected=drop.Input==InventoryItem.ITEM_TYPE.FOUND_ITEM_DECORATION?UINewItemOverlayController.TypeOfCard.Decoration:UINewItemOverlayController.TypeOfCard.FollowerSkin;
                if(found==null||found.typeOfCard!=expected||found.itemSprite!=drop.Renderer||drop.NativeType!=InventoryItem.ITEM_TYPE.NONE)
                {detail="actual native scroll discriminator differs for "+drop.Input;return false;}
            }
            Texture2D texture=assets.Texture(PickupVisuals.ResourceTexture(drop.Input));
            bool source=drop.Renderer.sprite!=null&&drop.Renderer.sprite.texture==texture&&drop.Probe.FirstGameDrawSprite!=null&&drop.Probe.FirstGameDrawSprite.texture==texture;
            if(!source||drop.Probe.NativePickup!=drop.Native||drop.Probe.FirstGameDrawFrame<0||!drop.Probe.FirstGameDrawHadMinecraft||drop.Probe.AppliedFrame>drop.Probe.FirstGameDrawFrame||drop.Probe.AssetPendingAtFirstGameDraw)
            {detail="native first-frame texture incomplete for "+drop.Input+": source="+source+", applied frame="+drop.Probe.AppliedFrame+", game frame="+drop.Probe.FirstGameDrawFrame+", correct="+drop.Probe.FirstGameDrawHadMinecraft;return false;}
            DroppedItemBinding model=PickupVisuals.Current.DroppedFor(drop.Renderer);
            if((drop.Input==InventoryItem.ITEM_TYPE.LOG||drop.Input==InventoryItem.ITEM_TYPE.STONE||drop.Input==InventoryItem.ITEM_TYPE.LOG_REFINED||drop.Input==InventoryItem.ITEM_TYPE.STONE_REFINED||drop.Input==InventoryItem.ITEM_TYPE.PUMPKIN||drop.Input==InventoryItem.ITEM_TYPE.HOPS||drop.Input==InventoryItem.ITEM_TYPE.GIFT_MEDIUM)&&(model==null||!model.IsCube))
            {detail="LOG/STONE native pickup must render a full depth cube";return false;}
            if(model==null||!model.HasVolume||!model.NativeArtSuppressed||!model.SpinObserved||model.WorldSize<MinecraftAvatar.BodyHeight*0.25f||model.WorldSize>MinecraftAvatar.BodyHeight*0.35f||model.GetComponent<Collider>()!=null)
            {detail="native small3D drop incomplete for "+drop.Input+": model="+(model!=null)+", camera frames="+(model==null?0:model.GameDrawFrames);return false;}
            return true;
        }
        private bool CheckIcons()
        {
            foreach(InventoryIconMapping map in Resources.FindObjectsOfTypeAll<InventoryIconMapping>())
            {
                Sprite globe=map.GetImage(InventoryItem.ITEM_TYPE.FOUND_ITEM_DECORATION),creeper=map.GetImage(InventoryItem.ITEM_TYPE.FOUND_ITEM_FOLLOWERSKIN),
                    alt=map.GetImage(InventoryItem.ITEM_TYPE.FOUND_ITEM_DECORATION_ALT),poppy=map.GetImage(InventoryItem.ITEM_TYPE.FLOWER_RED);
                if(globe!=null&&creeper!=null&&alt!=null&&poppy!=null&&globe.texture==assets.Texture("globe_banner_pattern")&&creeper.texture==assets.Texture("creeper_banner_pattern")&&
                    globe.texture!=creeper.texture&&alt.texture==globe.texture&&poppy.texture==assets.Texture("poppy")&&
                    map.GetImage(InventoryItem.ITEM_TYPE.GIFT_SMALL)!=null&&map.GetImage(InventoryItem.ITEM_TYPE.GIFT_SMALL).texture==assets.Texture("black_bundle")&&Spec.Asset("black_bundle").Entry.EndsWith("/black_bundle.png",StringComparison.Ordinal)&&Spec.Asset("black_bundle").SourceJarVersion=="1.21.2"&&map.GetImage(InventoryItem.ITEM_TYPE.GIFT_MEDIUM)!=null&&map.GetImage(InventoryItem.ITEM_TYPE.GIFT_MEDIUM).texture==assets.Texture("black_shulker_box")&&assets.Texture("black_shulker_box").width==32)return true;
            }
            detail="actual native inventory icon map not yet observed with two distinct pattern textures, ALT and poppy";return false;
        }
        private bool CheckRose()
        {
            if(flowerObject==null||Gameplay.Current==null)return false;
            if(flower==null)foreach(Structure native in flowerObject.GetComponentsInChildren<Structure>(true))if(native.Type==StructureBrain.TYPES.RED_FLOWER_BUSH){flower=native;break;}
            if(flower==null||flower.Brain==null||flower.Structure_Info==null||flower.Structure_Info.MultipleLootToDrop==null||
                !flower.Structure_Info.MultipleLootToDrop.Contains(InventoryItem.ITEM_TYPE.FLOWER_RED)||!flower.Structure_Info.MultipleLootToDrop.Contains(InventoryItem.ITEM_TYPE.SEED_FLOWER_RED)||!Gameplay.Current.NativeStructurePreserved(flower))
            {detail="actual RED_FLOWER_BUSH140 native brain/FLOWER_RED55 loot/collider snapshot not ready";return false;}
            Transform visual=Gameplay.Current.NativeStructureVisual(flower);
            if(visual==null||Gameplay.Current.NativeVegetationId(flower)!="rose_bush_full")return false;
            bool lower=false,upper=false;
            foreach(MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter=renderer.GetComponent<MeshFilter>();Material material=renderer.sharedMaterial;
                if(!renderer.enabled||renderer.forceRenderingOff||!renderer.gameObject.activeInHierarchy||filter==null||filter.sharedMesh==null||filter.sharedMesh.vertexCount<16||
                    material==null||material.shader==null||!material.shader.isSupported||renderer.transform.localToWorldMatrix.determinant<=0f||renderer.GetComponent<Collider>()!=null)return false;
                Bounds bounds=filter.sharedMesh.bounds;
                if(material.mainTexture==assets.Texture("rose_bush_bottom"))lower=bounds.min.y>=-0.001f&&bounds.max.y<=0.801f;
                if(material.mainTexture==assets.Texture("rose_bush_top"))upper=bounds.min.y>=0.799f&&bounds.max.y<=1.601f;
            }
            if(!lower||!upper)detail="rose facade lacks actual vertically stacked lower/upper Minecraft crossed block model halves";
            return lower&&upper;
        }
        public void Dispose()
        {
            foreach(GameObject obj in owned)if(obj!=null)
            {
                PickUp pickup=obj.GetComponent<PickUp>();
                if(pickup!=null&&ObjectPool.IsSpawned(obj))ObjectPool.Recycle(obj);
                else UnityEngine.Object.Destroy(obj);
            }
            owned.Clear();
        }
    }
}
