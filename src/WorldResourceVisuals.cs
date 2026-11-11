using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CultCraft
{
    /// <summary>Read-only visual coverage for native world resources outside Structure.Structures.</summary>
    public sealed class WorldResourceVisuals : IDisposable
    {
        private static WorldResourceVisuals current;
        private readonly Harmony harmony=new Harmony("cultcraft.world.resources");
        private readonly HashSet<DropLootOnDeath> pendingDrops=new HashSet<DropLootOnDeath>();
        private enum Kind { Tree, Berry, Stone, Flower }
        private sealed class Resource
        {
            public Component Source;
            public Transform Owner;
            public int OwnerId;
            public Structure Native;
            public StructuresData Data;
            public Tree WorldTree;
            public Interaction_Berries Berries;
            public Health Health;
            public Kind Kind;
            public GameObject Visual;
            public string Profile;
            public float Scale;
            public Vector3 AnchorOffset;
            public bool RockFitted;
            public float RockReadyAt;
            public readonly Dictionary<Renderer,bool> Hidden = new Dictionary<Renderer,bool>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }

        private readonly MinecraftAssets assets;
        private readonly Gameplay gameplay;
        private readonly ManualLogSource logger;
        private readonly List<Resource> resources = new List<Resource>();
        private readonly Dictionary<int,Resource> owners = new Dictionary<int,Resource>();
        private readonly HashSet<Health> inspectedHealth = new HashSet<Health>();
        private readonly HashSet<string> reportedKinds = new HashSet<string>();
        private float nextScan;
        private bool sceneDirty = true;
        private bool disposed;
        public int Count { get { return resources.Count; } }
        public static WorldResourceVisuals Current { get { return current; } }
        public GameObject VisualFor(GameObject native)
        {foreach(Resource item in resources)if(item.Owner!=null&&(item.Owner.gameObject==native||item.Owner.IsChildOf(native.transform)))return item.Visual;return null;}
        public string ProfileFor(GameObject native)
        {foreach(Resource item in resources)if(item.Owner!=null&&(item.Owner.gameObject==native||item.Owner.IsChildOf(native.transform)))return item.Profile;return null;}

        public WorldResourceVisuals(MinecraftAssets assets, Gameplay gameplay, ManualLogSource logger)
        {
            this.assets=assets; this.gameplay=gameplay; this.logger=logger;
            current=this;
            harmony.Patch(AccessTools.Method(typeof(DropLootOnDeath),"OnEnable"),postfix:new HarmonyMethod(typeof(WorldResourceVisuals),"SourceEnabled"));
            harmony.Patch(AccessTools.Method(typeof(DropLootOnDeath),"OnDisable"),postfix:new HarmonyMethod(typeof(WorldResourceVisuals),"SourceDisabled"));
            SceneManager.sceneLoaded += SceneLoaded;
        }
        private static void SourceEnabled(DropLootOnDeath __instance){if(current!=null)current.pendingDrops.Add(__instance);}
        private static void SourceDisabled(DropLootOnDeath __instance)
        {if(current==null)return;current.pendingDrops.Remove(__instance);for(int i=current.resources.Count-1;i>=0;i--)if(current.resources[i].Source==__instance)current.Remove(i,true);}

        public static Vector3 NativeVisualAnchor(Structure native)
        {
            if(native==null)return Vector3.zero;
            Vector3 ground=native.Structure_Info!=null ? native.Structure_Info.Position : native.transform.position;
            if(native.Type==StructureBrain.TYPES.COOKING_FIRE)
            {
                foreach(Interaction_Kitchen kitchen in native.GetComponentsInChildren<Interaction_Kitchen>(true))
                {
                    if(kitchen.structure!=native)continue;
                    // CookAll moves the player to Data.Position+(0.1,2.5).
                    // The cooking animation is the actual bowl, whereas the old
                    // pathfinding collider also includes the sideways work surface.
                    Vector3 bowl=kitchen.CookingMealAnimation!=null?kitchen.CookingMealAnimation.transform.position:kitchen.transform.position;
                    return new Vector3(ground.x+0.1f,bowl.y-Spec.Visual("campfire_work_offset").Value,ground.z);
                }
                foreach(Interaction_Cauldron cauldron in native.GetComponentsInChildren<Interaction_Cauldron>(true))
                {
                    if(cauldron.structure!=native)continue;
                    // The native menu/facing uses this interaction; its serialized
                    // collider is the actual stationary stove footprint.
                    Vector3 position=cauldron.Collider!=null && cauldron.Collider.enabled ? cauldron.Collider.bounds.center : cauldron.transform.position;
                    return new Vector3(position.x,position.y,ground.z);
                }
                foreach(Interaction_Cook cook in native.GetComponentsInChildren<Interaction_Cook>(true))
                    if(cook.structure==native)return new Vector3(cook.transform.position.x,cook.transform.position.y,ground.z);
            }
            return ground;
        }

        private void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            sceneDirty=true;nextScan=0f;inspectedHealth.Clear();
        }

        public void Tick(PlayerFarming player)
        {
            if(disposed)return;
            if(Time.unscaledTime>=nextScan)
            {
                nextScan=Time.unscaledTime+Math.Max(0.25f,Spec.Combat("bow").ScanInterval);
                Discover();
                Refresh();
            }
            LateTick();
        }

        public void LateTick()
        {
            if(disposed)return;
            for(int i=resources.Count-1;i>=0;i--)
            {
                Resource item=resources[i];
                if(item.Source==null || item.Owner==null || !item.Source.gameObject.scene.IsValid() ||
                    item.Native!=null && (item.Native.Structure_Info!=item.Data || gameplay.NativeStructureVisual(item.Native)!=null) ||
                    item.Berries!=null && item.Berries.withered!=null && item.Berries.withered.activeInHierarchy)
                { Remove(i,true);continue; }
                bool visible=item.Owner.gameObject.activeInHierarchy && item.Source.gameObject.activeInHierarchy;
                if(item.Data!=null && item.Data.Destroyed)visible=false;
                if(item.Kind!=Kind.Tree && item.Health!=null && item.Health.CurrentHP<=0f)visible=false;
                if(item.Visual!=null)
                {
                    if(item.Visual.activeSelf!=visible)item.Visual.SetActive(visible);
                    if(!visible)continue;
                    Vector3 position=item.Native!=null ? NativeVisualAnchor(item.Native) : item.Owner.position;
                    item.Visual.transform.position=position+item.AnchorOffset;
                }
                foreach(var pair in item.Hidden)if(pair.Key!=null)pair.Key.forceRenderingOff=item.Visual!=null || pair.Value;
            }
        }

        private bool HasNativeFacade(Structure native)
        { return native!=null && gameplay.NativeStructureVisual(native)!=null; }

        private void Discover()
        {
            if(pendingDrops.Count>0)
            {
                foreach(DropLootOnDeath drop in new List<DropLootOnDeath>(pendingDrops))if(drop!=null)InspectDrop(drop);
                pendingDrops.Clear();
            }
            // These are maintained by the actual resource classes, including
            // objects that never enter Structure.Structures.
            foreach(TreeBase tree in TreeBase.Trees)
                if(tree!=null)Register(tree,Kind.Tree,tree.Structure);
            foreach(Tree tree in Tree.Trees)
                if(tree!=null)Register(tree,Kind.Tree,tree.GetComponent<Structure>());
            foreach(Interaction_Berries bush in Interaction_Berries.Berries)
                if(bush!=null && bush.GetComponentInParent<CropController>()==null)
                {
                    if(bush.BerryBushType==InventoryItem.ITEM_TYPE.BERRY)Register(bush,Kind.Berry,bush.Structure);
                    else if(bush.BerryBushType==InventoryItem.ITEM_TYPE.FLOWER_RED||bush.BerryBushType==InventoryItem.ITEM_TYPE.FLOWERS)Register(bush,Kind.Flower,bush.Structure);
                }
            foreach(Rubble rubble in Rubble.Rubbles)
                if(rubble!=null && rubble.Structure!=null && rubble.StructureInfo!=null && rubble.StructureInfo.LootToDrop==InventoryItem.ITEM_TYPE.STONE)
                    Register(rubble,Kind.Stone,rubble.Structure);
            if(sceneDirty)
            {
                sceneDirty=false;
                // One scene discovery pass catches dormant/static natural props.
                // Subsequent pooled spawns are found through Health's native list.
                foreach(DropLootOnDeath drop in UnityEngine.Object.FindObjectsOfType<DropLootOnDeath>())InspectDrop(drop);
            }
            foreach(Health health in Health.allUnits)
                if(health!=null && health.gameObject.activeInHierarchy && inspectedHealth.Add(health))InspectDrop(health.GetComponent<DropLootOnDeath>());
            inspectedHealth.RemoveWhere(health=>health==null);
        }

        private void InspectDrop(DropLootOnDeath drop)
        {
            if(drop==null || drop.GetComponent<UnitObject>()!=null || drop.GetComponentInParent<PlayerFarming>()!=null)return;
            InventoryItem.ITEM_TYPE type=drop.DropLootDataSource==DropLootOnDeath.DataSource.StructureData && drop.Structure!=null && drop.Structure.Structure_Info!=null
                ? drop.Structure.Structure_Info.LootToDrop : drop.LootToDrop;
            StructuresData data=drop.Structure==null?null:drop.Structure.Structure_Info;
            if(data!=null&&data.MultipleLootToDrop!=null&&data.MultipleLootToDrop.Contains(InventoryItem.ITEM_TYPE.FLOWER_RED))type=InventoryItem.ITEM_TYPE.FLOWER_RED;
            if(data!=null&&data.MultipleLootToDrop!=null&&data.MultipleLootToDrop.Contains(InventoryItem.ITEM_TYPE.FLOWERS))type=InventoryItem.ITEM_TYPE.FLOWERS;
            if(drop.Structure!=null&&drop.Structure.Type==StructureBrain.TYPES.RED_FLOWER_BUSH)type=InventoryItem.ITEM_TYPE.FLOWER_RED;
            if(!drop.IsNaturalResource&&type!=InventoryItem.ITEM_TYPE.FLOWER_RED&&type!=InventoryItem.ITEM_TYPE.FLOWERS)return;
            if(type==InventoryItem.ITEM_TYPE.STONE)Register(drop,Kind.Stone,drop.Structure);
            else if(type==InventoryItem.ITEM_TYPE.LOG)Register(drop,Kind.Tree,drop.Structure);
            else if(type==InventoryItem.ITEM_TYPE.BERRY && drop.GetComponentInParent<CropController>()==null)Register(drop,Kind.Berry,drop.Structure);
            else if((type==InventoryItem.ITEM_TYPE.FLOWER_RED||type==InventoryItem.ITEM_TYPE.FLOWERS) && drop.GetComponentInParent<CropController>()==null)Register(drop,Kind.Flower,drop.Structure);
        }

        private void Register(Component source,Kind kind,Structure native)
        {
            if(source==null || !source.gameObject.activeInHierarchy || !source.gameObject.scene.IsValid() || HasNativeFacade(native))return;
            Interaction_Berries berries=source as Interaction_Berries;
            if(berries!=null && berries.withered!=null && berries.withered.activeInHierarchy)return;
            Transform owner=native!=null ? native.transform : source.transform;
            int key=owner.GetInstanceID();if(owners.ContainsKey(key))return;
            var item=new Resource { Source=source,Owner=owner,OwnerId=key,Native=native,Data=native==null?null:native.Structure_Info,Kind=kind,
                WorldTree=source as Tree ?? owner.GetComponent<Tree>(),Berries=berries ?? owner.GetComponentInChildren<Interaction_Berries>(true),Health=owner.GetComponent<Health>() };
            item.RockReadyAt=Time.unscaledTime+0.4f;
            Capture(item);
            if(item.Hidden.Count==0)return;
            try { Build(item); }
            catch(Exception error){ReleaseVisual(item);if(logger!=null)logger.LogWarning("World resource visual: "+error.Message);return;}
            owners.Add(key,item);resources.Add(item);
            string kindKey=kind+":"+item.Profile;
            if(reportedKinds.Add(kindKey) && logger!=null)
                logger.LogInfo("Actual world resource covered: "+source.GetType().Name+"/"+owner.name+", native="+(native==null?"none":native.Type.ToString())+", profile="+item.Profile);
        }

        private static void Capture(Resource item)
        {
            foreach(Renderer renderer in item.Owner.GetComponentsInChildren<Renderer>(true))
            {
                string name=renderer.name.ToLowerInvariant();
                if(renderer is ParticleSystemRenderer || renderer.GetComponentInParent<Canvas>()!=null || name.Contains("prompt") || name.Contains("indicator") || name.Contains("icon") || name.Contains("halo") || name.Contains("shadow"))continue;
                if(!item.Hidden.ContainsKey(renderer))item.Hidden.Add(renderer,renderer.forceRenderingOff);
            }
        }

        private string Profile(Resource item)
        {
            if(item.Kind==Kind.Stone)return "stone";
            if(item.Kind==Kind.Berry || item.Kind==Kind.Flower)
            {
                bool picked=item.Berries!=null && item.Berries.StructureBrain!=null && item.Berries.StructureBrain.BerryPicked;
                if(item.Data!=null && item.Data.ProgressTarget>0f && item.Data.Progress>=item.Data.ProgressTarget)picked=true;
                if(item.Kind==Kind.Flower)return picked?"rose_bush_picked":"rose_bush_full";
                return picked ? "berry_0" : "berry_3";
            }
            int variant=item.Native!=null ? VegetationState.TreeVariant(item.Native) : VegetationState.TreeVariant(item.Owner);
            string species=VegetationState.TreeSpecies(variant,item.Data,item.Owner.position);
            if(item.WorldTree!=null && item.WorldTree.Dead)return species+"_stump";
            if(item.Data!=null)
            {
                if(item.Data.IsSapling)
                    return species+(item.Data.GrowthStage<=0f?"_stump":item.Data.GrowthStage<=2f?"_sapling":item.Data.GrowthStage<=4f?"_small":variant==2?"_large":variant==1||variant==3?"_small":"_medium");
                if(item.Data.ProgressTarget>0f && item.Data.Progress>=item.Data.ProgressTarget)return species+"_stump";
            }
            return species+(variant==2?"_large":variant==1||variant==3?"_small":"_medium");
        }

        private void Build(Resource item)
        {
            item.Profile=Profile(item);
            item.Scale=item.Native!=null ? VegetationState.Scale(item.Native) : 1f;
            item.Visual=new GameObject("CultCraft world resource "+item.Profile);
            item.Visual.transform.SetPositionAndRotation(item.Native!=null?NativeVisualAnchor(item.Native):item.Owner.position,Quaternion.Euler(-90f,0f,0f));
            BuildingRow design=Spec.Building(item.Kind==Kind.Tree?"tree":item.Kind==Kind.Berry?"berry_bush":item.Kind==Kind.Flower?"camellia_bush":"rock");
            if(item.Kind==Kind.Stone && item.Native!=null && item.Native.Type==StructureBrain.TYPES.RUBBLE_BIG)
            { design.Width*=2.5f;design.Depth*=2.5f;design.Height*=2.5f; }
            if(item.Kind==Kind.Stone){design=RockPileFit.Layout(item.Owner,item.Hidden.Keys,design);RockPileFit.Align(item.Visual.transform,item.Owner,item.Hidden.Keys);}
            new BlockModels(assets,item.Visual.transform,item.Materials,item.Meshes).Build(design,item.Kind==Kind.Stone?null:item.Profile,item.Scale,
                item.Kind==Kind.Stone?RockPileFit.BlockingFootprint(item.Owner):null);
            if(item.Kind==Kind.Stone && RockPileFit.Align(item.Visual.transform,item.Owner,item.Hidden.Keys))
                item.AnchorOffset=item.Visual.transform.position-(item.Native!=null?NativeVisualAnchor(item.Native):item.Owner.position);
        }

        private void Refresh()
        {
            foreach(Resource item in resources)
            {
                // The temple disables the settlement root. Refresh its models when it is visible again.
                if(item.Source==null||item.Owner==null||!item.Source.gameObject.activeInHierarchy||!item.Owner.gameObject.activeInHierarchy)continue;
                int captured=item.Hidden.Count;
                Capture(item);
                bool refitStone=item.Kind==Kind.Stone && Time.unscaledTime>=item.RockReadyAt && (!item.RockFitted || captured!=item.Hidden.Count);
                string profile=Profile(item);
                float scale=item.Native!=null?VegetationState.Scale(item.Native):1f;
                if(item.Visual!=null && profile==item.Profile && scale==item.Scale && !refitStone)continue;
                ReleaseVisual(item);
                try { Build(item); if(refitStone)item.RockFitted=true; }
                catch(Exception error)
                {
                    ReleaseVisual(item);
                    foreach(var pair in item.Hidden)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
                    if(logger!=null)logger.LogWarning("World resource update: "+error.Message);
                }
            }
        }

        public Transform VisualFor(Component source)
        {
            foreach(Resource item in resources)
                if(item.Source==source || item.Native==source || source!=null && item.Owner==source.transform)return item.Visual==null?null:item.Visual.transform;
            return null;
        }

        private static void ReleaseVisual(Resource item)
        {
            if(item.Visual!=null){item.Visual.SetActive(false);UnityEngine.Object.Destroy(item.Visual);item.Visual=null;}
            foreach(Material material in item.Materials)if(material!=null)UnityEngine.Object.Destroy(material);
            foreach(Mesh mesh in item.Meshes)if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            item.Materials.Clear();item.Meshes.Clear();
        }

        private void Remove(int index,bool restore)
        {
            Resource item=resources[index];
            if(restore)foreach(var pair in item.Hidden)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
            owners.Remove(item.OwnerId);
            if(item.Health!=null)inspectedHealth.Remove(item.Health);
            ReleaseVisual(item);resources.RemoveAt(index);
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;SceneManager.sceneLoaded-=SceneLoaded;harmony.UnpatchSelf();if(current==this)current=null;pendingDrops.Clear();
            for(int i=resources.Count-1;i>=0;i--)Remove(i,true);
            inspectedHealth.Clear();owners.Clear();
        }
    }
}
