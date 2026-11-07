using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.Assets;
using Lamb.UI.MainMenu;
using MMBiomeGeneration;
using MMRoomGeneration;
using MMTools;
using UnityEngine;
using src.Managers;

namespace CultCraft
{
    /// <summary>Only Melty's exact test marker enables this memory-only native game session.</summary>
    public sealed class SelfTest : IDisposable
    {
        private static bool isolateSaves;
        private static readonly string testPrefix = "cultcraft-test-" + Guid.NewGuid().ToString("N") + "-";
        private readonly MinecraftAssets assets;
        private readonly MinecraftAvatar avatar;
        private readonly CameraRig camera;
        private readonly Gameplay gameplay;
        private readonly StationVisuals stations;
        private readonly ManualLogSource logger;
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string,bool> subcheckResults=new Dictionary<string,bool>(StringComparer.Ordinal);
        private readonly HashSet<string> emittedGroups=new HashSet<string>(StringComparer.Ordinal);
        private readonly Harmony guard;
        private readonly float started;
        private readonly bool markerFound;
        private bool disposed;
        private bool startedWorld;
        private int stage;
        private float stageSince;
        private Vector3 movementStart;
        private int movementAttempt;
        private Camera observedCamera;
        private bool nativeOrthographic;
        private float nativeFov;
        private float nativeNear;
        private Quaternion nativeRotation;
        private string lastError;
        private static SelfTest activeTest;
        private bool buildingRequested;
        private bool bedObserved;
        private bool hutRequested;
        private bool hutObserved;
        private bool stationObserved;
        private Interaction_PlacementRegion observedStation;
        private Collider[] stationColliders;
        private bool[] stationColliderStates;
        private Collider2D[] stationColliders2D;
        private bool[] stationColliderStates2D;
        private int stationCapturedFrame;
        private PlacementRegion stationRegion;
        private Transform stationLock;
        private float stationActivationDistance;
        private Vector3 stationActivatorOffset;
        private bool campfireRequested;
        private GameObject campfireObject;
        private Structure campfireNative;
        private bool campfireObserved;
        private int campfireFirstFrame=-1;
        private readonly Dictionary<Mesh,Vector2> campfireUvBaseline=new Dictionary<Mesh,Vector2>();
        private readonly HashSet<Mesh> campfireAdvanced=new HashSet<Mesh>();
        private float constructionStarted = -1f;
        private bool gearCaptured;
        private PlayerFarming gearOwner;
        private EquipmentType savedWeapon;
        private int savedWeaponLevel;
        private int progressionStep;
        private bool progressionEquipped;
        private bool materialsObserved = true;
        private bool enchantmentsObserved = true;
        private int[] progressionLevels;
        private Enchantments sourceEnchantments;
        private Health bowTarget;
        private int bowTargetId;
        private string lastValidTargetState;
        private string lastTargetState;
        private string targetLossReason;
        private BiomeRoom targetRoom;
        private BiomeRoom preparationRoom;
        private BiomeRoom advanceFrom;
        private BiomeRoom advanceTo;
        private bool advancePending;
        private float advanceStarted;
        private float roomWaitStarted;
        private int combatSteps;
        private float combatPreparationStarted;
        private readonly HashSet<BiomeRoom> attemptedRooms = new HashSet<BiomeRoom>();
        private readonly HashSet<int> attemptedEntryPodiums = new HashSet<int>();
        private bool bowAssertionStarted;
        private int shotsAtStart;
        private int arrowDrawsAtStart;
        private int arrowHitsAtStart;
        private int observedNativeBowHits;
        private float observedNativeBowDamage;
        private bool bowInputObserved;
        private readonly HashSet<string> enemyDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        private bool cameraObserved;
        private bool billboardObserved;
        private float nextBillboardProbe;
        private bool hitstopRequested;
        private bool hitstopObserved;
        private bool hitstopViewLost;
        private float hitstopStarted;
        private bool slowmoRequested;
        private bool slowmoViewLost;
        private int slowmoRenderFrames;
        private float slowmoStarted;
        private int nativeBleatRequests;
        private int bleatRequestsBefore;
        private int bleatSoundsBefore;
        private float bleatStarted = -1f;
        private bool bleatInputObserved;
        private bool bleatPoseObserved;
        private bool bleatSoundObserved;
        private float meditationStarted = -1f;
        private bool meditationInputObserved;
        private bool meditationGeometryObserved;
        private bool meditationReleased;
        private int meditationRenderFrames;
        private string meditationDetail;
        private int restorationCycles;
        private bool repeatView;
        private bool foodAttempted;
        private bool foodObserved;
        private float foodStarted;
        private int foodCountBefore;
        private int foodUsesBefore;
        private float nativeFoodHealing;
        private bool resourceIconsObserved;
        private bool vegetationRequested;
        private string stoneFitDetail;
        private bool vegetationObserved;
        private float vegetationStarted = -1f;
        private float nextVegetationLog;
        private int vegetationStage;
        private readonly GameObject[] vegetationObjects = new GameObject[8];
        private readonly Structure[] vegetationNatives = new Structure[8];
        private readonly StructuresData[] vegetationData = new StructuresData[8];
        private string treeFixtureSpecies;
        private readonly Dictionary<Renderer, bool> farmGroundRenderers = new Dictionary<Renderer, bool>();
        private bool farmPlanted;
        private int treeRevision;
        private int berryRevision;
        private int farmRevision;
        private int berriesBeforeTestHarvest;
        private bool nativeBerryHarvestDidNotGrantLoot;
        private float treeSmallHeight;
        private float treeYoungHeight;
        private Transform harvestedBerryFacade;
        private GameObject devotionFixture;
        private int devotionFirstId;
        private int devotionCycle;
        private int devotionCallbacks;
        private int devotionCollections;
        private int devotionCollectionsBefore;
        private int devotionDrawsBefore;
        private int devotionSuccessfulDrawsBefore;
        private int devotionApplyCount;
        private int devotionCreatedFrame;
        private float devotionStarted = -1f;
        private bool devotionCycleVisible;
        private bool devotionObserved;
        private bool devotionReused;

        public bool Active { get { return markerFound && !disposed; } }
        private FeatureSelfTest features;
        private BroomSelfTest broomProof;
        private RockCollisionSelfTest stoneWalk;
        private FishingSelfTest fishingProof;
        private SkinSelfTest skinProof;
        internal void ConfigureSkinTest(MonoBehaviour host,PlayerSkins skins,Func<PlayerFarming,MinecraftAvatar> find)
        {if(Active)skinProof=new SkinSelfTest(host,skins,avatar,camera,find,Report,localCoopOnly);}
        private FoodSelfTest foodProof;
        private LootVisualSelfTest lootProof;
        private DeathSelfTest deathProof;
        private Scenes082SelfTest scenesProof;
        private Temple083SelfTest templeProof;
        private Hotbar084SelfTest hotbarProof;
        private readonly bool hotbarOnly=Array.Exists(Spec.TestRows,row=>row.Id=="minecraft-return-held-items");
        private readonly bool localCoopOnly=Array.Exists(Spec.TestRows,row=>row.Id=="minecraft-local-coop");
        private readonly bool templeOnly=Array.Exists(Spec.TestRows,row=>row.Id=="minecraft-temple-ceremonies");
        private readonly bool scenesOnly=Array.Exists(Spec.TestRows,row=>row.Id=="minecraft-cutscene-player");
        private bool scenesFallPassed,deathProbeStarted;
        internal void ConfigureScenesTest(MonoBehaviour host,PlayerSkins skins,CinematicActors actors,KnucklebonesHeads heads)
        {if(Active)scenesProof=new Scenes082SelfTest(host,skins,actors,heads,logger,Report);}
        private bool deathPrepared;
        public bool SettingsHotkeyArmed { get { return Active && features!=null && features.SettingsHotkeyArmed; } }
        private float V(string id) { return Spec.Setting(id).Value; }

        public SelfTest(MinecraftAssets assets, MinecraftAvatar avatar, CameraRig camera, Gameplay gameplay, ManualLogSource logger, StationVisuals stations = null, AttackCooldownUi attackCooldown = null)
        {
            this.assets = assets; this.avatar = avatar; this.camera = camera; this.gameplay = gameplay; this.logger = logger; this.stations = stations;
            string marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", "cultcraft.json");
            markerFound = File.Exists(marker);
            if (!markerFound) return;
            started = Time.unscaledTime;
            stageSince = started;
            // Install isolation before any native new-game or menu action. The original save directory is never read.
            isolateSaves = true;
            guard = new Harmony("cultcraft.test.save-isolation");
            InstallGuards();
            activeTest = this;
            sourceEnchantments = new Enchantments(assets);
            progressionLevels = new[] { (int)V("test_progression_base"), (int)V("test_progression_enchanted"), (int)V("test_progression_next_material") };
            MethodInfo damage = AccessTools.Method(typeof(Health), "DealDamage", new[] { typeof(float), typeof(GameObject), typeof(Vector3), typeof(bool), typeof(Health.AttackTypes), typeof(bool), typeof(Health.AttackFlags) });
            if (damage == null) throw new MissingMethodException("Native Health.DealDamage was not found for passive observation.");
            guard.Patch(damage, prefix: new HarmonyMethod(typeof(SelfTest), "DamageProbePrefix"), postfix: new HarmonyMethod(typeof(SelfTest), "DamageProbePostfix"));
            Patch(typeof(Health), "OnDisable", Type.EmptyTypes, "TargetLifecyclePrefix");
            Patch(typeof(Health), "OnDestroy", Type.EmptyTypes, "TargetLifecyclePrefix");
            BiomeGenerator.OnBiomeLeftRoom += NativeRoomLeft;
            BiomeGenerator.OnBiomeChangeRoom += NativeRoomChanged;
            MethodInfo heal = AccessTools.Method(typeof(HealthPlayer), "Heal", new[] { typeof(float) });
            if (heal == null) throw new MissingMethodException("HealthPlayer.Heal");
            guard.Patch(heal, prefix: new HarmonyMethod(typeof(SelfTest), "HealProbePrefix"), postfix: new HarmonyMethod(typeof(SelfTest), "HealProbePostfix"));
            Patch(typeof(PlayerFarming), "BleatRoutine", Type.EmptyTypes, "BleatProbePrefix");
            Patch(typeof(RewiredGameplayInputSource), "GetBleatButtonDown", new[] { typeof(PlayerFarming) }, "VirtualBleatPrefix");
            Patch(typeof(RewiredGameplayInputSource), "GetMeditateButtonDown", new[] { typeof(PlayerFarming) }, "VirtualMeditateDownPrefix");
            Patch(typeof(RewiredGameplayInputSource), "GetMeditateButtonHeld", new[] { typeof(PlayerFarming) }, "VirtualMeditateHeldPrefix");
            MethodInfo collect = AccessTools.Method(typeof(SoulCustomTargetLerp), "CollectMe", Type.EmptyTypes);
            if (collect == null) throw new MissingMethodException("Native pooled devotion CollectMe is unavailable.");
            guard.Patch(collect, prefix: new HarmonyMethod(typeof(SelfTest), "DevotionCollectPrefix"), postfix: new HarmonyMethod(typeof(SelfTest), "DevotionCollectPostfix"));
            PlayerFarming.OnGetSoul += DevotionCallbackObserved;
            InstallProgressionInputGuards();
            MethodInfo nativeGun=AccessTools.Method(typeof(PlayerWeapon),"DoBlunderbussAttack",new[] {typeof(float),typeof(Vector3),typeof(Health.AttackTypes),typeof(bool)});
            if(nativeGun==null)throw new MissingMethodException(typeof(PlayerWeapon).FullName,"DoBlunderbussAttack");
            guard.Patch(nativeGun,postfix:new HarmonyMethod(typeof(SelfTest),"NativeGunProbePostfix") { after=new[] {"cultcraft.gameplay"} });
            Camera.onPreCull += ObserveCameraFrame;
            camera.AllowTestInput(true);
            features=new FeatureSelfTest(assets,avatar,camera,logger,Report,attackCooldown);
            broomProof = new BroomSelfTest(avatar, camera, logger, Report);
            stoneWalk = new RockCollisionSelfTest(camera, logger);
            fishingProof = new FishingSelfTest(avatar, camera, logger, Report);
            foodProof = new FoodSelfTest(gameplay, logger, Report);
            lootProof=new LootVisualSelfTest(assets,logger,Report);
            if(templeOnly)templeProof=new Temple083SelfTest(avatar,Report);
            if(hotbarOnly)hotbarProof=new Hotbar084SelfTest(gameplay,avatar,Report);
            deathProof=new DeathSelfTest(avatar,logger,(name,passed,why)=>{if(scenesOnly||hotbarOnly){scenesFallPassed=passed;logger.LogInfo("Native death prerequisite "+passed+": "+why);}else Report(name,passed,why);});
            RegisterActions();
            logger.LogInfo("Melty marker verified: autonomous checks use a fresh memory-only world; personal saves are blocked.");
        }

        private void InstallGuards()
        {
            Patch(typeof(SaveAndLoad), "Save", Type.EmptyTypes, "SavePrefix");
            Patch(typeof(SaveAndLoad), "Save", new[] { typeof(string) }, "SavePrefix");
            Patch(typeof(SaveAndLoad), "SaveMetaData", new[] { typeof(MetaData) }, "NoIoPrefix");
            Patch(typeof(SaveAndLoad), "Load", new[] { typeof(int) }, "NoIoPrefix");
            Patch(typeof(SaveAndLoad), "LoadByPath", new[] { typeof(string) }, "NoIoPrefix");
            Patch(typeof(SaveAndLoad), "DeleteSaveSlot", new[] { typeof(int) }, "NoIoPrefix");
            Patch(typeof(SaveAndLoad), "DeleteScreenshotInfo", new[] { typeof(int) }, "NoIoPrefix");
            Patch(typeof(SaveAndLoad), "SaveExist", new[] { typeof(int) }, "AbsentPrefix");
            Patch(typeof(SaveAndLoad), "MakeSaveSlot", new[] { typeof(int) }, "SlotPrefix");
            Patch(typeof(SaveAndLoad), "MakeMetaSlot", new[] { typeof(int) }, "SlotPrefix");
            Patch(typeof(PersistenceManager), "Save", Type.EmptyTypes, "NoIoPrefix");
            GuardReader(typeof(COTLDataReadWriter<DataManager>), typeof(DataManager));
            GuardReader(typeof(COTLDataReadWriter<MetaData>), typeof(MetaData));
            // These concrete native backing readers also cover direct calls that bypass the outer wrapper.
            Type json = AccessTools.TypeByName("MMJsonDataReadWriter`1");
            if (json == null) throw new TypeLoadException("MMJsonDataReadWriter`1 was not found; test startup is refused.");
            GuardReader(json.MakeGenericType(typeof(DataManager)), typeof(DataManager));
            GuardReader(json.MakeGenericType(typeof(MetaData)), typeof(MetaData));
        }

        private void GuardReader(Type reader, Type data)
        {
            Patch(reader, "Write", new[] { data, typeof(string), typeof(bool), typeof(bool) }, "ReaderWritePrefix");
            Patch(reader, "Read", new[] { typeof(string) }, "ReaderReadPrefix");
            Patch(reader, "Delete", new[] { typeof(string) }, "ReaderDeletePrefix");
            Patch(reader, "FileExists", new[] { typeof(string) }, "AbsentPrefix");
        }

        private void Patch(Type type, string name, Type[] args, string prefix)
        {
            MethodInfo method = AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            guard.Patch(method, prefix: new HarmonyMethod(typeof(SelfTest), prefix));
        }

        private void InstallProgressionInputGuards()
        {
            // These exact native gameplay queries all accept one optional player.
            // Result hooks also cover top-down play and leave controller maps intact.
            foreach(string name in new[] { "GetHorizontalAxis", "GetVerticalAxis", "GetHorizontalSecondaryAxis", "GetVerticalSecondaryAxis" })
                PatchProgressionInput(name,"ProgressionAxisPostfix");
            foreach(string name in new[] {
                "GetAttackButtonDown", "GetAttackButtonHeld", "GetAttackButtonUp", "GetHeavyAttackButtonDown", "GetHeavyAttackButtonHeld",
                "GetDodgeButtonDown", "GetDodgeButtonHeld", "GetDodgeRollButtonDown", "GetCurseButtonDown", "GetCurseButtonHeld", "GetCurseButtonUp",
                "GetInteractButtonDown", "GetInteractButtonHeld", "GetInteractButtonUp", "GetInteract2ButtonDown", "GetInteract2ButtonHeld",
                "GetInteract3ButtonDown", "GetInteract3ButtonHeld", "GetInteract4ButtonDown", "GetFleeceAbilityButtonDown", "GetFleeceAbilityButtonHeld",
                "GetRelicButtonDown", "GetRelicButtonHeld", "GetAbilityButtonHeld", "GetReturnToBaseButtonHeld",
                "GetMeditateButtonDown", "GetMeditateButtonHeld", "GetBleatButtonDown", "GetBleatButtonHeld", "GetBleatButtonUp" })
                PatchProgressionInput(name,"ProgressionButtonPostfix");
        }

        private void PatchProgressionInput(string name,string callback)
        {
            MethodInfo method=AccessTools.Method(typeof(RewiredGameplayInputSource),name,new[] { typeof(PlayerFarming) });
            if(method==null)throw new MissingMethodException(typeof(RewiredGameplayInputSource).FullName,name);
            var hook=new HarmonyMethod(typeof(SelfTest),callback) { after=new[] { "cultcraft.camera", "cultcraft.gameplay" } };
            guard.Patch(method,postfix:hook);
        }

        private static bool BlockProgressionInput(PlayerFarming player)
        {
            SelfTest test=activeTest;
            return test!=null && test.Active && test.stage==7 && test.gearOwner!=null && (player==null?PlayerFarming.Instance:player)==test.gearOwner;
        }
        private static void ProgressionAxisPostfix(PlayerFarming __0,ref float __result) { if(BlockProgressionInput(__0))__result=0f; }
        private static void ProgressionButtonPostfix(PlayerFarming __0,ref bool __result) { if(BlockProgressionInput(__0))__result=false; }
        private static void NativeGunProbePostfix(PlayerWeapon __instance,bool __runOriginal)
        {
            SelfTest test=activeTest;
            if(test!=null && test.Active && test.stage==7 && test.replacementStep==1 && test.gearOwner!=null && __instance==test.gearOwner.playerWeapon && __runOriginal)test.nativeGunOriginalCalls++;
        }

        private static bool NoIoPrefix() { return !isolateSaves; }
        private static bool AbsentPrefix(ref bool __result) { if (!isolateSaves) return true; __result = false; return false; }
        private static bool SlotPrefix(int __0, MethodBase __originalMethod, ref string __result)
        {
            if (!isolateSaves) return true;
            __result = testPrefix + (__originalMethod.Name == "MakeMetaSlot" ? "meta-" : "slot-") + __0 + ".json";
            return false;
        }
        private static bool SavePrefix()
        {
            if (!isolateSaves) return true;
            SaveAndLoad.OnSaveCompleted?.Invoke();
            return false;
        }
        private static void Callback(object instance, string field)
        {
            var f = instance.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            var action = f == null ? null : f.GetValue(instance) as Action;
            action?.Invoke();
        }
        private static bool ReaderWritePrefix(object __instance) { if (!isolateSaves) return true; Callback(__instance, "OnWriteCompleted"); return false; }
        private static bool ReaderReadPrefix(object __instance) { if (!isolateSaves) return true; Callback(__instance, "OnCreateDefault"); return false; }
        private static bool ReaderDeletePrefix(object __instance) { if (!isolateSaves) return true; Callback(__instance, "OnDeletionComplete"); return false; }
        private static void DamageProbePrefix(Health __instance, out float __state) { __state = __instance == null ? 0f : __instance.CurrentHP; }
        private static void DamageProbePostfix(Health __instance, GameObject Attacker, Health.AttackTypes AttackType, bool __result, float __state)
        {
            var test = activeTest;
            if (test == null || !test.Active || test.gearOwner == null || test.stage != 10 || __instance != test.bowTarget || Attacker != test.gearOwner.gameObject || AttackType != Health.AttackTypes.Projectile) return;
            float after = __instance.CurrentHP;
            if (__result && after < __state)
            {
                test.observedNativeBowHits++;
                test.observedNativeBowDamage += __state - after;
            }
        }
        private static void HealProbePrefix(HealthPlayer __instance, out float __state) { __state = __instance == null ? 0f : __instance.CurrentHP; }
        private static void HealProbePostfix(HealthPlayer __instance, float __state)
        {
            SelfTest test = activeTest;
            if (test == null || !test.Active || test.stage != 11 || test.gearOwner == null || test.gearOwner.health != __instance ||
                test.gameplay.SelectedSlot != 2 || UnityEngine.InputSystem.Mouse.current == null || !UnityEngine.InputSystem.Mouse.current.leftButton.isPressed) return;
            if (__instance.CurrentHP > __state) test.nativeFoodHealing += __instance.CurrentHP - __state;
        }

        private void ObserveCameraFrame(Camera rendered)
        {
            if (Active && stage == 1 && rendered != null && rendered.cameraType == CameraType.Game &&
                meditationGeometryObserved && !meditationReleased && avatar.AnimationPose == "meditate" && avatar.WorldVisible)
                meditationRenderFrames++;
            if (!Active || rendered != observedCamera) return;
            bool held = camera.Active && camera.FirstPerson && rendered == camera.ActiveCamera && !rendered.orthographic && avatar.ViewVisible && avatar.ViewArmHidden && avatar.ViewItemOnRight && Math.Abs(rendered.fieldOfView-camera.FieldOfView)<0.1f;
            if (stage==2 && !held && ((hitstopRequested && !hitstopViewLost && Time.unscaledTime-hitstopStarted<V("test_camera_effect_timeout") && Time.timeScale==0f) ||
                (slowmoRequested && !slowmoViewLost && Time.unscaledTime-slowmoStarted<V("test_camera_effect_timeout") && Math.Abs(Time.timeScale-0.2f)<0.01f)))
                logger.LogWarning("First combat view failure: active="+camera.Active+", requested="+camera.FirstPerson+", same camera="+(rendered==camera.ActiveCamera)+", ortho="+rendered.orthographic+", item="+avatar.HeldItemId+", visible="+avatar.ViewVisible+", arm hidden="+avatar.ViewArmHidden+", right="+avatar.ViewItemOnRight+", FOV="+rendered.fieldOfView+"/"+camera.FieldOfView+", pose="+avatar.AnimationPose);
            if (hitstopRequested && Time.unscaledTime-hitstopStarted<V("test_camera_effect_timeout") && Time.timeScale == 0f)
            {
                if(held)hitstopObserved=true;else hitstopViewLost=true;
            }
            if (slowmoRequested && Time.unscaledTime-slowmoStarted<V("test_camera_effect_timeout") && Math.Abs(Time.timeScale-0.2f)<0.01f)
            {
                if(held)slowmoRenderFrames++;else slowmoViewLost=true;
            }
        }

        private static void BleatProbePrefix(PlayerFarming __instance)
        {
            SelfTest test=activeTest;
            if(test!=null && test.Active && test.stage==1 && __instance==PlayerFarming.Instance)test.nativeBleatRequests++;
        }

        private static bool VirtualBleatPrefix(PlayerFarming __0, ref bool __result)
        {
            // Melty's virtual Input System keyboard is separate from Rewired.
            // Translate only that real key transition during this isolated probe;
            // native PlayerFarming still owns eligibility and calls BleatRoutine.
            var test=activeTest;
            var keyboard=UnityEngine.InputSystem.Keyboard.current;
            if(test==null || !test.Active || test.stage!=1 || __0!=PlayerFarming.Instance || keyboard==null || !keyboard.bKey.wasPressedThisFrame)return true;
            __result=true;return false;
        }

        private static bool VirtualMeditateDownPrefix(PlayerFarming __0, ref bool __result)
        {
            var test = activeTest;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (test == null || !test.Active || test.stage != 1 || test.meditationStarted < 0f || test.meditationReleased ||
                (__0 != null && __0 != PlayerFarming.Instance) || keyboard == null || !keyboard.lKey.wasPressedThisFrame) return true;
            // Translate Melty's actual virtual key into the native query only in the
            // marked test. Native PlayerFarming still performs the state transition.
            __result = true; return false;
        }

        private static bool VirtualMeditateHeldPrefix(PlayerFarming __0, ref bool __result)
        {
            var test = activeTest;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (test == null || !test.Active || test.stage != 1 || test.meditationStarted < 0f ||
                (__0 != null && __0 != PlayerFarming.Instance) || keyboard == null) return true;
            __result = !test.meditationReleased && keyboard.lKey.isPressed; return false;
        }

        private void DevotionCallbackObserved(int delta) { if (delta > 0) devotionCallbacks++; }
        private static void DevotionCollectPrefix(SoulCustomTargetLerp __instance, out int __state)
        {
            var test = activeTest;
            __state = test == null ? 0 : test.devotionCallbacks;
        }
        private static void DevotionCollectPostfix(SoulCustomTargetLerp __instance, int __state)
        {
            var test = activeTest;
            if (test != null && test.Active && test.stage == 1 && test.devotionFixture == __instance.gameObject && test.devotionCallbacks > __state)
                test.devotionCollections++;
        }

        private bool CameraProof { get { return cameraObserved && billboardObserved && hitstopObserved && !hitstopViewLost && slowmoRenderFrames>=2 && !slowmoViewLost; } }
        private string CameraProofDetail { get { return "live FPS="+cameraObserved+", aligned actor="+billboardObserved+", native hitstop="+hitstopObserved+", hitstop view lost="+hitstopViewLost+", real kill slow-motion render frames="+slowmoRenderFrames+", slow-motion view lost="+slowmoViewLost; } }

        private static void TargetLifecyclePrefix(Health __instance, MethodBase __originalMethod)
        {
            var test = activeTest;
            if (test == null || !test.Active || !System.Object.ReferenceEquals(__instance, test.bowTarget)) return;
            test.targetLossReason = "native Health." + __originalMethod.Name + " in room " + RoomIdentity(BiomeGenerator.Instance == null ? null : BiomeGenerator.Instance.CurrentRoom);
            test.LogTarget(test.targetLossReason, true);
        }

        public void Tick(PlayerFarming player)
        {
            if (!Active) return;
            MeltyPlay.Report("cultcraft.test.stage", stage);
            MeltyPlay.Report("cultcraft.test.world_started", startedWorld);
            MeltyPlay.Report("cultcraft.test.saves_isolated", isolateSaves);
            MeltyPlay.Report("cultcraft.test.done", stage == 12&&(!scenesOnly||scenesProof!=null&&scenesProof.DeathComplete));
            MeltyPlay.Report("cultcraft.player.ready", Ready(player));
            MeltyPlay.Report("cultcraft.camera.active", camera.Active);
            MeltyPlay.Report("cultcraft.camera.observed", cameraObserved);
            MeltyPlay.Report("cultcraft.billboards.observed", billboardObserved);
            MeltyPlay.Report("cultcraft.weapon.tier", gameplay.Tier);
            MeltyPlay.Report("cultcraft.bow.shots", gameplay.ShotsFired);
            MeltyPlay.Report("cultcraft.bow.damage_hits", gameplay.ArrowDamageHits);
            MeltyPlay.Report("cultcraft.bow.observed_hp_loss", observedNativeBowDamage);
            MeltyPlay.Report("cultcraft.bow.input_observed", bowInputObserved);
            MeltyPlay.Report("cultcraft.hotbar.selected", gameplay.SelectedSlot);
            MeltyPlay.Report("cultcraft.bow.lmb_state", UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed);
            MeltyPlay.Report("cultcraft.camera.discovery_passes", camera.DiscoveryPasses);
            MeltyPlay.Report("cultcraft.camera.static_props", camera.StaticPropCount);
            MeltyPlay.Report("cultcraft.camera.visible_actors", camera.VisibleBillboardCount);
            MeltyPlay.Report("cultcraft.camera.hitstop_preserved", hitstopObserved);
            MeltyPlay.Report("cultcraft.camera.kill_slowmo_render_frames", slowmoRenderFrames);
            MeltyPlay.Report("cultcraft.avatar.native_bleats", nativeBleatRequests);
            MeltyPlay.Report("cultcraft.avatar.classic_sound_observed", bleatSoundObserved);
            MeltyPlay.Report("cultcraft.camera.restore_cycles", restorationCycles);
            MeltyPlay.Report("cultcraft.food.observed_native_healing", nativeFoodHealing);
            MeltyPlay.Report("cultcraft.food.observed", foodObserved);
            MeltyPlay.Report("cultcraft.vegetation.stage", vegetationStage);
            MeltyPlay.Report("cultcraft.vegetation.observed", vegetationObserved);
            MeltyPlay.Report("cultcraft.devotion.pool_proof", devotionObserved);
            MeltyPlay.Report("cultcraft.devotion.native_collections", devotionCollections);
            MeltyPlay.Report("cultcraft.avatar.lotus_frames", meditationRenderFrames);
            MeltyPlay.Report("cultcraft.bow.r_state", UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.rKey.isPressed);
            if (player != null)
            {
                MeltyPlay.Report("cultcraft.player.x", player.transform.position.x);
                MeltyPlay.Report("cultcraft.player.y", player.transform.position.y);
            }
            if (Time.unscaledTime - started > V("test_total_timeout"))
            {
                camera.EndTestInput();
                ReleaseBowInput();
                MeltyPlay.Hold("b",false);
                MeltyPlay.Hold("l",false);
                RestoreGear();
                broomProof.Dispose();
                fishingProof.Dispose();
                if(skinProof!=null)skinProof.Dispose();
                stoneWalk.Dispose();
                foodProof.Dispose();
                Report("first-person-camera-active", CameraProof, CameraProof ? null : "Observation timeout: "+CameraProofDetail);
                foreach (TestRow test in Spec.TestRows)
                    if (test.Id != "mod-loads" && !reported.Contains(test.Id)) Report(test.Id, false, "Observation timeout at stage " + stage + (lastError == null ? "" : "; " + lastError));
                foreach(string required in scenesOnly||templeOnly||hotbarOnly||localCoopOnly?new string[0]:new[]{"minecraft-resource-visuals","minecraft-enchantment-progression","minecraft-attack-cooldown","minecraft-broom","minecraft-food","minecraft-fishing"})
                    if(!reported.Contains(required))Report(required,false,"Required native subcheck timed out at stage "+stage);
                stage = 12;
                return;
            }
            try { Run(player); }
            catch (Exception ex)
            {
                lastError = ex.GetBaseException().Message;
                logger.LogWarning("Self-test stage " + stage + ": " + lastError);
                camera.EndTestInput();
                ReleaseBowInput();
            }
        }

        private void Run(PlayerFarming player)
        {
            if(scenesOnly){RunScenesOnly(player);return;}
            if(hotbarOnly){RunHotbar084(player);return;}
            if(localCoopOnly){RunLocalCoop(player);return;}
            if(templeOnly)
            {
                if(!reported.Contains("minecraft-assets-loaded")&&assets.Ready&&assets.PlayerSkin!=null)Report("minecraft-assets-loaded",true,null);
                if(stage==0&&Time.unscaledTime-started>=V("test_menu_settle")&&BeginNewGame())Next(1);
                if(stage==1&&startedWorld)
                {
                    bool ready=Ready(player)&&PlayerFarming.Location==FollowerLocation.Base;
                    if(ready)lootProof.EnsureCatalog();
                    templeProof.Tick(player,ready);
                    if(templeProof.Complete)
                    {
                        templeProof.ReturnToSettlement(player);
                        Next(22);
                    }
                }
                if(stage==22&&Ready(player)&&EnterDungeon())Next(23);
                if(stage==23&&gameplay.IsDungeon&&player!=null&&(deathProbeStarted||Ready(player)))
                {
                    deathProbeStarted=true;
                    if(deathProof.Tick(player))Next(12);
                }
                return;
            }
            if(stage==12&&Ready(player)&&PlayerFarming.Location==FollowerLocation.Base)lootProof.EnsureCatalog();
            if (!reported.Contains("minecraft-assets-loaded") && assets.Ready && assets.PlayerSkin != null && assets.PlayerSkin.width == 64 && assets.PlayerSkin.height == 64 && assets.Texture("wooden_sword") != null)
                Report("minecraft-assets-loaded", true, null);
            float elapsed = Time.unscaledTime - stageSince;
            if (stage == 1 && !broomProof.Complete)
            {
                if (startedWorld) broomProof.Tick(player, Ready(player) && PlayerFarming.Location == FollowerLocation.Base);
                return;
            }
            if (stage == 1 && !foodProof.Complete)
            {
                foodProof.Tick(player, Ready(player) && PlayerFarming.Location == FollowerLocation.Base);
                return;
            }
            if(stage==1&&startedWorld&&elapsed>=V("test_world_settle")&&skinProof!=null&&!skinProof.Complete)
            {skinProof.Tick(player,Ready(player));return;}
            if (stage == 1 && features.Complete && !fishingProof.Complete)
            {
                fishingProof.Tick(player, Ready(player));
                return;
            }
            if(stage==1) { CheckAvatarFeedback(player); CheckMeditation(player); CheckDevotion(player); }
            if(stage==1 && reported.Contains("minecraft-avatar-active") && reported.Contains("minecraft-resource-visuals") && reported.Contains("minecraft-construction-facade"))features.Tick(player);
            if (camera.Active) CheckBillboards();
            switch (stage)
            {
                case 0:
                    if (Time.unscaledTime - started >= V("test_menu_settle") && BeginNewGame()) Next(1);
                    break;
                case 1:
                    if (!Ready(player) || elapsed < V("test_world_settle") || !startedWorld) break;
                    if (constructionStarted < 0f && assets.Texture(Spec.Building("bed").Texture) != null) constructionStarted = Time.unscaledTime;
                    if (avatar.WorldVisible && avatar.LambHidden && avatar.VisibleBodyParts >= 6 && avatar.OpaqueDepthMaterial && bleatSoundObserved && bleatPoseObserved && meditationGeometryObserved && meditationRenderFrames >= 2 && meditationReleased)
                        Report("minecraft-avatar-active", true, null);
                    CheckResources(player);
                    CheckConstruction(player);
                    if (!reported.Contains("minecraft-construction-facade") && constructionStarted >= 0f && Time.unscaledTime - constructionStarted > V("test_build_timeout"))
                        Report("minecraft-construction-facade", false, "Native construction proof incomplete: BED2 straw="+bedObserved+", BED_2 red="+hutObserved+", real construction station="+stationObserved+", CookingFire71 footprint/native colliders/animated campfire="+campfireObserved+", animated materials advanced="+campfireAdvanced.Count+"/2; "+(lastError??"no completed textured native building"));
                    if (!reported.Contains("minecraft-construction-facade")) break;
                    if (!reported.Contains("minecraft-avatar-active") || !reported.Contains("minecraft-resource-visuals")) break;
                    if (!features.Complete) break;
                    if (!fishingProof.Complete) break;
                    observedCamera = Camera.main;
                    if (observedCamera == null) break;
                    nativeOrthographic = observedCamera.orthographic;
                    nativeFov = observedCamera.fieldOfView;
                    nativeNear = observedCamera.nearClipPlane;
                    nativeRotation = Quaternion.Euler(-45f, 0f, 0f);
                    if (!camera.FirstPerson) camera.Toggle();
                    Next(2);
                    break;
                case 2:
                    if (!hitstopRequested && (elapsed < V("test_view_settle") || !Ready(player) || !camera.Active || camera.ActiveCamera == null || camera.ActiveCamera.orthographic || !avatar.ViewVisible)) break;
                    if (camera.MouseSensitivity < 0.02f || camera.MouseSensitivity > 1f || camera.FieldOfView < 55f || camera.FieldOfView > 110f ||
                        !hitstopRequested && Math.Abs(camera.ActiveCamera.fieldOfView - camera.FieldOfView) > 0.1f) break;
                    cameraObserved = true;
                    if(!CheckNativeCameraEffects(player))break;
                    movementAttempt = 0;
                    movementStart = player.transform.position;
                    camera.BeginTestInput(Vector2.up);
                    Next(3);
                    break;
                case 3:
                    if (player == null) break;
                    float distance = Vector2.Distance(movementStart, player.transform.position);
                    if (distance >= V("test_move_threshold") && camera.LastWorldMovement.sqrMagnitude > 0.01f && camera.LastRawMovement.sqrMagnitude > 0.01f)
                    {
                        logger.LogInfo("Native first-person movement observed: " + distance.ToString("F3", CultureInfo.InvariantCulture) + " XY units.");
                        Report("first-person-movement", true, null);
                        camera.EndTestInput();
                        if (camera.FirstPerson) camera.Toggle();
                        Next(4);
                    }
                    else if (elapsed > V("test_move_attempt"))
                    {
                        camera.EndTestInput();
                        movementAttempt++;
                        if (movementAttempt >= 4)
                        {
                            Report("first-person-movement", false, "Native input was sent in four directions; displacement stayed below threshold.");
                            if (camera.FirstPerson) camera.Toggle();
                            Next(4);
                        }
                        else
                        {
                            Vector2 direction = movementAttempt == 1 ? Vector2.right : movementAttempt == 2 ? Vector2.down : Vector2.left;
                            movementStart = player.transform.position;
                            camera.BeginTestInput(direction);
                            stageSince = Time.unscaledTime;
                        }
                    }
                    break;
                case 4:
                    if (elapsed < V("test_view_settle") || !Ready(player) || observedCamera == null) break;
                    if (repeatView)
                    {
                        if (!camera.Active || !avatar.ViewVisible) break;
                        camera.Toggle();
                        repeatView = false;
                        stageSince = Time.unscaledTime;
                        break;
                    }
                    if (camera.Active || camera.FirstPerson) break;
                    bool restored = observedCamera.orthographic == nativeOrthographic && Math.Abs(observedCamera.fieldOfView - nativeFov) < 0.1f && Math.Abs(observedCamera.nearClipPlane - nativeNear) < 0.01f && Quaternion.Angle(observedCamera.transform.rotation, nativeRotation) < 0.5f && avatar.WorldVisible;
                    if (!restored) break;
                    restorationCycles++;
                    logger.LogInfo("Native top-down canonical rotation observed after perspective cycle " + restorationCycles + ": " + observedCamera.transform.eulerAngles);
                    if (restorationCycles < (int)V("test_camera_restore_cycles"))
                    {
                        camera.Toggle();
                        repeatView = true;
                        stageSince = Time.unscaledTime;
                        break;
                    }
                    Report("top-down-camera-restored", true, null);
                    Next(5);
                    break;
                case 5:
                    if (Ready(player) && elapsed > V("test_view_settle") && EnterDungeon()) Next(6);
                    break;
                case 6:
                    if (!Ready(player) || !gameplay.IsDungeon || elapsed < V("test_world_settle") || player.playerWeapon == null) break;
                    WeaponData weapon = EquipmentManager.GetWeaponData(player.currentWeapon);
                    if (weapon != null && weapon.PrimaryEquipmentType == EquipmentType.Sword && player.currentWeaponLevel >= 1 && RenderedReplacement(gameplay.WeaponId))
                    {
                        Report("minecraft-weapon-equipped", true, null);
                        gearOwner = player;
                        savedWeapon = player.currentWeapon;
                        savedWeaponLevel = player.currentWeaponLevel;
                        gearCaptured = true;
                        progressionStep = 0;
                        progressionEquipped = false;
                        Next(7);
                    }
                    break;
                case 7:
                    RunProgression(player);
                    break;
                case 8:
                    PrepareNaturalCombat(player);
                    break;
                case 9:
                    if (!Ready(player) || player != gearOwner) break;
                    if (!camera.FirstPerson) camera.Toggle();
                    if (!camera.Active || !avatar.ViewVisible) break;
                    if (!bowAssertionStarted)
                    {
                        shotsAtStart = gameplay.ShotsFired;
                        arrowDrawsAtStart = gameplay.ArrowEntityDrawFrames;
                        arrowHitsAtStart = gameplay.ArrowDamageHits;
                        observedNativeBowHits = 0;
                        observedNativeBowDamage = 0f;
                        bowInputObserved = false;
                        bowAssertionStarted = true;
                    }
                    ReleaseBowInput();
                    AssignTarget(FindHostile(player), "combat ready before actual hotbar and left-mouse input");
                    if (bowTarget == null) { preparationRoom = null; Next(8); break; }
                    MeltyPlay.Hold("2", true);
                    Next(10);
                    break;
                case 10:
                    if (player != null && player == gearOwner && gameplay.IsDungeon)
                    {
                        LogTarget("passive live target state", false);
                        if (!UsableHostile(bowTarget) || targetRoom != (BiomeGenerator.Instance == null ? null : BiomeGenerator.Instance.CurrentRoom))
                            AssignTarget(FindHostile(player), "reacquire actual live native hostile");
                    }
                    if (gameplay.SelectedSlot == 1 && UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed && gameplay.BowHeld && avatar.HeldItemId == "bow" && avatar.ViewVisible)
                    {
                        if (!bowInputObserved) logger.LogInfo("Observed actual digit2 hotbar selection and left-mouse input: Gameplay.BowHeld plus rendered Minecraft bow.");
                        bowInputObserved = true;
                    }
                    if (bowInputObserved && observedNativeBowHits > 0 && observedNativeBowDamage > 0f && gameplay.ArrowDamageHits > arrowHitsAtStart &&
                        gameplay.ShotsFired > shotsAtStart && gameplay.ArrowEntityDrawFrames > arrowDrawsAtStart)
                    {
                        ReleaseBowInput();
                        Report("minecraft-bow-hit", true, null);
                        logger.LogInfo("Actual native projectile HP loss observed: " + observedNativeBowDamage.ToString("F3", CultureInfo.InvariantCulture) +
                            ", Minecraft arrow entity camera draws: " + (gameplay.ArrowEntityDrawFrames - arrowDrawsAtStart));
                        Next(11);
                        break;
                    }
                    if (bowTarget == null && player != null && Ready(player) && !AnyNaturalHostile())
                    {
                        ReleaseBowInput();
                        preparationRoom = null;
                        Next(8);
                        break;
                    }
                    if (elapsed > V("test_bow_timeout"))
                    {
                        ReleaseBowInput();
                        Report("minecraft-bow-hit", false, "Bow input/render/projectile proof incomplete; input=" + bowInputObserved + ", target=" + (bowTarget == null ? "absent" : bowTarget.name) + ", shots=" + (gameplay.ShotsFired - shotsAtStart) + ", collisions=" + (gameplay.ArrowDamageHits - arrowHitsAtStart) + ", native HP loss=" + observedNativeBowDamage.ToString("F3", CultureInfo.InvariantCulture) + ", target lifecycle=" + (targetLossReason ?? "none") + ", error=" + (lastError ?? "none"));
                        Next(11);
                        break;
                    }
                    if (player == null || player != gearOwner || !Ready(player) || !camera.Active)
                    {
                        ReleaseBowInput();
                        break;
                    }
                    if (gameplay.SelectedSlot != 1)
                    {
                        MeltyPlay.Hold("2", true);
                        MeltyPlay.Button(0, false);
                    }
                    else
                    {
                        MeltyPlay.Hold("2", false);
                        if (UsableHostile(bowTarget) && camera.TestLookAt(bowTarget.transform.position)) MeltyPlay.Button(0, true);
                        else if (observedNativeBowHits == 0) MeltyPlay.Button(0, false);
                    }
                    break;
                case 11:
                    if (!deathPrepared)
                    {
                        if (!ObserveFoodUse(player)) break;
                        deathPrepared=true;
                        camera.EndTestInput();ReleaseBowInput();RestoreGear();
                        if(camera.FirstPerson)camera.Toggle();
                    }
                    if(!deathProof.Tick(player))break;
                    camera.EndTestInput();
                    ReleaseBowInput();
                    RestoreGear();
                    Report("first-person-camera-active", CameraProof, CameraProof ? null : "Combined perspective check incomplete: "+CameraProofDetail);
                    if (camera.FirstPerson) camera.Toggle();
                    Next(12);
                    break;
            }
        }

        private void RunLocalCoop(PlayerFarming player)
        {
            if(!reported.Contains("minecraft-assets-loaded")&&assets.Ready&&assets.PlayerSkin!=null)Report("minecraft-assets-loaded",true,null);
            if(stage==0&&Time.unscaledTime-started>=V("test_menu_settle")&&BeginNewGame())Next(1);
            if(stage==1&&startedWorld&&skinProof!=null)
            {
                skinProof.Tick(player,Ready(player)&&PlayerFarming.Location==FollowerLocation.Base);
                if(skinProof.Complete)Next(12);
            }
        }

        private void RunHotbar084(PlayerFarming player)
        {
            if(!reported.Contains("minecraft-assets-loaded")&&assets.Ready&&assets.PlayerSkin!=null)Report("minecraft-assets-loaded",true,null);
            if(stage==0)
            {
                if(Time.unscaledTime-started>=V("test_menu_settle")&&BeginNewGame())Next(1);
                return;
            }
            bool ready=Ready(player);
            if(stage==1&&startedWorld)
            {
                bool safe=ready&&PlayerFarming.Location==FollowerLocation.Base;
                if(safe)lootProof.EnsureCatalog();
                hotbarProof.TickSafe(player,safe);
                if(hotbarProof.SafeComplete)Next(30);
            }
            if(stage==30&&ready&&Time.unscaledTime-stageSince>1f&&EnterDungeon())Next(31);
            if(stage==31&&gameplay.IsDungeon&&player!=null)
            {
                hotbarProof.TickDungeon(player,ready);
                if(hotbarProof.DungeonComplete)Next(32);
            }
            if(stage==32&&player!=null&&(deathProbeStarted||ready))
            {
                deathProbeStarted=true;
                if(deathProof.Tick(player))
                {
                    hotbarProof.BeginReturn(player);
                    Next(33);
                }
            }
            if(stage==33)
            {
                hotbarProof.TickReturn(player,ready);
                if(hotbarProof.ReturnComplete)Next(12);
            }
        }

        private void RunScenesOnly(PlayerFarming player)
        {
            if(!reported.Contains("minecraft-assets-loaded")&&assets.Ready&&assets.PlayerSkin!=null)Report("minecraft-assets-loaded",true,null);
            if(stage==0){if(Time.unscaledTime-started>=V("test_menu_settle")&&BeginNewGame())Next(1);return;}
            if(scenesProof==null)return;
            if(stage==1)
            {
                if(!startedWorld||PlayerFarming.Location!=FollowerLocation.Base)return;
                if(Ready(player))lootProof.EnsureCatalog();
                scenesProof.TickHeads(player,Ready(player));
                if(scenesProof.HeadsComplete&&Ready(player)&&EnterDungeon())Next(20);
                return;
            }
            if(stage==20)
            {
                if(!gameplay.IsDungeon||player==null||(!deathProbeStarted&&!Ready(player)))return;
                deathProbeStarted=true;
                if(!deathProof.Tick(player))return;
                if(!scenesFallPassed){Report("minecraft-cutscene-player",false,"Real native death prerequisite failed.");Next(12);return;}
                Next(21);
            }
            if(stage==21){scenesProof.TickDeath(player);if(scenesProof.DeathComplete)Next(12);}
        }

        private bool CheckNativeCameraEffects(PlayerFarming player)
        {
            if(player==null || player.playerWeapon==null)return false;
            float now=Time.unscaledTime;
            if(!hitstopRequested)
            {
                hitstopRequested=true;hitstopStarted=now;
                GameManager.GetInstance().HitStop(V("test_hitstop_duration"));
                return false;
            }
            if(!slowmoRequested)
            {
                if(now-hitstopStarted<V("test_hitstop_duration")+0.05f || Time.timeScale<0.9f && now-hitstopStarted<V("test_camera_effect_timeout"))return false;
                slowmoRequested=true;slowmoStarted=now;
                // This is the real kill effect, including its native camera zoom.
                // Its .2 timescale previously made MenusBlocked falsely look like a menu.
                player.playerWeapon.DoSlowMo(setZoom:true);
                return false;
            }
            if(now-slowmoStarted<V("test_kill_slowmo_observe") || Time.timeScale<0.9f && now-slowmoStarted<V("test_camera_effect_timeout"))return false;
            logger.LogInfo("Actual native combat camera effects observed: "+CameraProofDetail);
            return true;
        }

        private void CheckAvatarFeedback(PlayerFarming player)
        {
            if(reported.Contains("minecraft-avatar-active"))return;
            float now=Time.unscaledTime;
            if(bleatStarted<0f)
            {
                if(!Ready(player) || !avatar.WorldVisible)return;
                bleatStarted=now;bleatRequestsBefore=nativeBleatRequests;bleatSoundsBefore=assets.SoundsPlayed;
                MeltyPlay.Hold("b",true);
                return;
            }
            bool down=now-bleatStarted<V("test_bleat_input_hold");
            MeltyPlay.Hold("b",down);
            if(UnityEngine.InputSystem.Keyboard.current!=null && UnityEngine.InputSystem.Keyboard.current.bKey.isPressed)bleatInputObserved=true;
            if(bleatInputObserved && nativeBleatRequests>bleatRequestsBefore && avatar.AnimationPose=="crouch" && avatar.WorldVisible)bleatPoseObserved=true;
            if(!bleatSoundObserved && bleatInputObserved && nativeBleatRequests>bleatRequestsBefore && assets.SoundsPlayed>bleatSoundsBefore && assets.LastSoundPlayback=="hurt_classic: FMOD channel playing")
            {
                bleatSoundObserved=true;
                logger.LogInfo("Actual native B proof: real key="+bleatInputObserved+", BleatRoutine calls="+(nativeBleatRequests-bleatRequestsBefore)+", rendered crouch="+bleatPoseObserved+", playback="+assets.LastSoundPlayback);
            }
            if(bleatSoundObserved && bleatPoseObserved)
            {
                MeltyPlay.Hold("b",false);
                return;
            }
            if(now-bleatStarted>V("test_bleat_timeout"))
            {
                MeltyPlay.Hold("b",false);
                Report("minecraft-avatar-active",false,"Native B/crouch/audio proof incomplete: real key="+bleatInputObserved+", native BleatRoutine calls="+(nativeBleatRequests-bleatRequestsBefore)+", rendered crouch="+bleatPoseObserved+", actual FMOD sound="+bleatSoundObserved+", playback="+assets.LastSoundPlayback);
            }
        }

        private static bool Ready(PlayerFarming p)
        {
            if (p == null || p.state == null || p.GoToAndStopping || p.IsKnockedOut || !p.gameObject.activeInHierarchy || MMTransition.IsPlaying || LetterBox.IsPlaying || MMConversation.isPlaying || Time.timeScale <= 0f) return false;
            return p.state.CURRENT_STATE == StateMachine.State.Idle || p.state.CURRENT_STATE == StateMachine.State.Moving || p.state.CURRENT_STATE == StateMachine.State.Idle_Winter || p.state.CURRENT_STATE == StateMachine.State.Moving_Winter;
        }

        private void CheckMeditation(PlayerFarming player)
        {
            if (reported.Contains("minecraft-avatar-active") || meditationReleased) return;
            if (meditationStarted < 0f)
            {
                if (!bleatPoseObserved || !bleatSoundObserved || !Ready(player) || !avatar.WorldVisible || PlayerFarming.Location != FollowerLocation.Base) return;
                meditationStarted = Time.unscaledTime;
                MeltyPlay.Hold("l", true);
                return;
            }
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.lKey.isPressed) meditationInputObserved = true;
            if (player != null && player.state != null && player.state.CURRENT_STATE == StateMachine.State.Meditate &&
                meditationInputObserved && avatar.AnimationPose == "meditate" && avatar.WorldVisible)
                meditationGeometryObserved = ObserveLotusGeometry(out meditationDetail);
            if (meditationGeometryObserved && meditationRenderFrames >= 2 && Time.unscaledTime - meditationStarted >= 2f)
            {
                MeltyPlay.Hold("l", false); meditationReleased = true;
                logger.LogInfo("Actual native L/lotus proof: real virtual L="+meditationInputObserved+", native State.Meditate, game-camera frames="+meditationRenderFrames+", "+meditationDetail+"; release leaves native PlayerFarming to restore time/state.");
                return;
            }
            if (Time.unscaledTime - meditationStarted > 8f)
            {
                MeltyPlay.Hold("l", false); meditationReleased = true;
                Report("minecraft-avatar-active", false, "Native L/lotus proof incomplete: key="+meditationInputObserved+", state="+(player==null||player.state==null?"none":player.state.CURRENT_STATE.ToString())+", actual geometry="+meditationGeometryObserved+", game-camera frames="+meditationRenderFrames+", "+meditationDetail);
            }
        }

        private bool ObserveLotusGeometry(out string detail)
        {
            detail = "avatar joints unavailable";
            Transform root = avatar.Root;
            if (root == null || Camera.main == null) return false;
            Transform rightLeg = root.Find(Spec.Avatar("right_leg").Label), leftLeg = root.Find(Spec.Avatar("left_leg").Label);
            Transform torso = root.Find(Spec.Avatar("torso").Label);
            Transform rightArm = torso == null ? null : torso.Find(Spec.Avatar("right_arm").Label), leftArm = torso == null ? null : torso.Find(Spec.Avatar("left_arm").Label);
            if (rightLeg == null || leftLeg == null || rightArm == null || leftArm == null) return false;
            float pixel = MinecraftAvatar.PixelSize;
            Vector3 rightHip = root.InverseTransformPoint(rightLeg.position), leftHip = root.InverseTransformPoint(leftLeg.position);
            Vector3 rightFoot = root.InverseTransformPoint(rightLeg.TransformPoint(Vector3.down * Spec.Avatar("right_leg").Height * pixel));
            Vector3 leftFoot = root.InverseTransformPoint(leftLeg.TransformPoint(Vector3.down * Spec.Avatar("left_leg").Height * pixel));
            AvatarRow ar = Spec.Avatar("right_arm"), al = Spec.Avatar("left_arm");
            Vector3 rightHand = root.InverseTransformPoint(rightArm.TransformPoint(new Vector3(ar.CenterX, ar.CenterY-ar.Height*0.5f, ar.CenterZ)*pixel));
            Vector3 leftHand = root.InverseTransformPoint(leftArm.TransformPoint(new Vector3(al.CenterX, al.CenterY-al.Height*0.5f, al.CenterZ)*pixel));
            Vector3 rightShoulder = root.InverseTransformPoint(rightArm.position), leftShoulder = root.InverseTransformPoint(leftArm.position);
            bool cross = rightHip.x > leftHip.x && rightFoot.x < leftFoot.x && rightFoot.x < 0f && leftFoot.x > 0f;
            bool front = rightFoot.z > rightHip.z + 4f*pixel && leftFoot.z > leftHip.z + 4f*pixel;
            float rightKneeGap, leftKneeGap;
            bool rightKnee = HandNearLegSurface(root,rightHand,rightLeg,leftLeg,out rightKneeGap);
            bool leftKnee = HandNearLegSurface(root,leftHand,rightLeg,leftLeg,out leftKneeGap);
            bool knees = rightHand.z > rightShoulder.z + 2f*pixel && leftHand.z > leftShoulder.z + 2f*pixel &&
                rightKnee && leftKnee &&
                Vector3.Angle(rightHand-rightShoulder, Vector3.down) < 45f && Vector3.Angle(leftHand-leftShoulder, Vector3.down) < 45f;
            MeshRenderer bodyRenderer = torso.GetComponent<MeshRenderer>();
            bool visible = bodyRenderer != null && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(Camera.main), bodyRenderer.bounds);
            detail = "front-crossed leg endpoints="+rightFoot.ToString("F3")+" / "+leftFoot.ToString("F3")+", hips="+rightHip.ToString("F3")+" / "+leftHip.ToString("F3")+", low forward palms="+rightHand.ToString("F3")+" / "+leftHand.ToString("F3")+", actual leg surface gaps="+rightKneeGap.ToString("F3")+" / "+leftKneeGap.ToString("F3")+", cross="+cross+", front="+front+", palms at knees="+knees+", in frustum="+visible;
            return cross && front && knees && visible;
        }

        private static bool HandNearLegSurface(Transform root,Vector3 hand,Transform rightLeg,Transform leftLeg,out float gap)
        {
            gap=Single.PositiveInfinity;
            // Intersect the actual rotated leg mesh bounds along the avatar's up
            // axis. Hip height is not a knee surface when rigid legs are crossed.
            foreach(Transform leg in new[] {rightLeg,leftLeg})
            {
                MeshFilter filter=leg.GetComponent<MeshFilter>();
                if(filter==null || filter.sharedMesh==null)continue;
                Vector3 origin=leg.InverseTransformPoint(root.TransformPoint(hand+Vector3.up*0.25f));
                Vector3 direction=leg.InverseTransformDirection(root.TransformDirection(Vector3.down));
                Ray ray=new Ray(origin,direction);float distance;
                if(!filter.sharedMesh.bounds.IntersectRay(ray,out distance))continue;
                Vector3 surface=root.InverseTransformPoint(leg.TransformPoint(ray.GetPoint(distance)));
                float separation=Mathf.Abs(hand.y-surface.y);
                if(separation<gap)gap=separation;
            }
            return gap<=0.03f;
        }

        private void CheckDevotion(PlayerFarming player)
        {
            if (devotionObserved || reported.Contains("minecraft-resource-visuals") || !meditationReleased || player == null || !Ready(player) || PlayerFarming.Location != FollowerLocation.Base) return;
            if (devotionStarted < 0f) devotionStarted = Time.unscaledTime;
            if (Time.unscaledTime-devotionStarted > 14f)
            {
                DevotionOrbVisual failed = devotionFixture == null ? null : devotionFixture.GetComponent<DevotionOrbVisual>();
                Report("minecraft-resource-visuals",false,"Actual native XP pool proof incomplete: cycle="+devotionCycle+", collections="+devotionCollections+", actual draw latched="+devotionCycleVisible+", reuse="+devotionReused+", objectActive="+(devotionFixture!=null&&devotionFixture.activeInHierarchy)+", atlas="+(failed!=null&&failed.UsesMinecraftAtlas)+", renderedFrames="+(failed==null?0:failed.RenderedFrames)+", successfulDrawDelta="+(failed==null?0:failed.SuccessfulGameDraws-devotionSuccessfulDrawsBefore)+", successfulApply="+(failed==null?0:failed.LastSuccessfulApplyCount)+"/"+devotionApplyCount+", drawFrame="+(failed==null?-1:failed.LastSuccessfulGameDrawFrame)+"/spawn="+devotionCreatedFrame+", drawNativeSuppressed="+(failed!=null&&failed.LastSuccessfulNativeSuppressed)+", drawAtlasValid="+(failed!=null&&failed.LastSuccessfulAtlasValid)+", nativeImage="+(failed!=null&&failed.NativeImageEnabled)+", fallback="+(failed==null?"none":failed.FallbackReason));
                return;
            }
            if (devotionFixture == null)
            {
                if (player.CameraBone == null) return;
                Vector3 position = player.transform.position + new Vector3(1.4f, -0.4f, 0f);
                devotionFixture = SoulCustomTargetLerp.Create(player.CameraBone, position, 2.5f, Color.white, new Action(player.GetSoul));
                if (devotionFixture == null) throw new InvalidOperationException("Native SoulCustomTargetLerp.Create returned no pooled object.");
                DevotionOrbVisual applied = devotionFixture.GetComponent<DevotionOrbVisual>();
                devotionCollectionsBefore = devotionCollections;
                devotionDrawsBefore = applied == null ? 0 : applied.RenderedFrames;
                devotionSuccessfulDrawsBefore = applied == null ? 0 : applied.SuccessfulGameDraws;
                devotionApplyCount = applied == null ? 0 : applied.ApplyCount;
                devotionCreatedFrame = Time.frameCount;
                devotionCycleVisible = false;
                if (devotionCycle == 0) devotionFirstId = devotionFixture.GetInstanceID();
                else devotionReused = devotionFixture.GetInstanceID() == devotionFirstId && applied != null && applied.ApplyCount >= 2;
                logger.LogInfo("Actual native XP pooled fixture cycle="+devotionCycle+", id="+devotionFixture.GetInstanceID()+", Init applied before native activation, native GetSoul callback retained; reuse="+devotionReused);
                return;
            }
            DevotionOrbVisual visual = devotionFixture.GetComponent<DevotionOrbVisual>();
            // Native pool deactivation and other camera passes can restore art
            // between Update calls. Require the state captured in this lifetime's
            // actual successful game-camera draw, rather than a timing-dependent
            // poll of the owner's current preparation flags after that draw.
            if (visual != null && visual.UsesMinecraftAtlas && visual.SuccessfulGameDraws > devotionSuccessfulDrawsBefore &&
                visual.LastSuccessfulGameDrawFrame >= devotionCreatedFrame && visual.LastSuccessfulGameCameraId != 0 &&
                visual.LastSuccessfulApplyCount == devotionApplyCount && visual.LastSuccessfulNativeSuppressed && visual.LastSuccessfulAtlasValid)
            {
                MeshRenderer renderer = visual.AtlasRenderer;
                MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.mainTexture == assets.TintedTexture("experience_orb",new Color(0.4f,1f,0.12f,1f)))
                {
                    Vector2[] uv = filter.sharedMesh.uv;
                    bool glyph = uv.Length == 4;
                    foreach (Vector2 point in uv) glyph &= point.x >= 0.5f && point.x <= 0.75f && point.y >= 0.75f && point.y <= 1f;
                    Color[] colors = filter.sharedMesh.colors;
                    foreach (Color color in colors) glyph &= color.a > 0.99f;
                    devotionCycleVisible |= glyph && colors.Length == 4;
                }
            }
            if (!devotionFixture.activeInHierarchy && devotionCollections > devotionCollectionsBefore && devotionCycleVisible)
            {
                logger.LogInfo("Actual native XP collected: cycle="+devotionCycle+", native GetSoul callback+CollectMe+pool deactivation observed, rendered Minecraft atlas frames="+(visual.RenderedFrames-devotionDrawsBefore)+", validated actual game-camera draws="+(visual.SuccessfulGameDraws-devotionSuccessfulDrawsBefore)+", drawFrame="+visual.LastSuccessfulGameDrawFrame+", native lifetime Apply="+visual.LastSuccessfulApplyCount+", source image enabled="+visual.NativeImageEnabled+", fallback transitions="+visual.FallbackTransitions);
                if (devotionCycle == 0) { devotionCycle=1; devotionFixture=null; }
                else if (devotionReused)
                {
                    bool varied=DevotionVisuals.Current!=null&&DevotionVisuals.Current.DistinctExperiencePitches>=2;
                    if(!varied){Report("minecraft-resource-visuals",false,"Native XP collection did not produce distinct actual FMOD pitches.");return;}
                    devotionObserved=true;logger.LogInfo("Actual native XP pool proof PASS: two naturally collected native SoulCustomTargetLerp lifetimes, same pool instance, atlas visible, native callbacks retained, distinct actual FMOD pickup pitches="+DevotionVisuals.Current.DistinctExperiencePitches);
                }
            }
        }

        private bool BeginNewGame()
        {
            if (startedWorld) return true;
            UIMainMenuController main = UIMainMenuController.Instance;
            if (main == null || main.LoadMenu == null || MMTransition.IsPlaying) return false;
            SaveAndLoad.SAVE_SLOT = 0; // Every resulting filename is uniquely intercepted; no slot is read or written.
            var options = new UINewGameOptionsMenuController.NewGameOptions { QuickStart = true, DifficultyIndex = 1, PermadeathMode = false, SurvivalMode = false, WinterMode = false };
            MethodInfo begin = AccessTools.Method(typeof(LoadMenu), "BeginNewGame", new[] { typeof(UINewGameOptionsMenuController.NewGameOptions) });
            if (begin == null) throw new MissingMethodException("Native LoadMenu.BeginNewGame was not found.");
            begin.Invoke(main.LoadMenu, new object[] { options });
            startedWorld = true;
            logger.LogInfo("Native QuickStart new game requested with save isolation active.");
            return true;
        }

        private bool EnterDungeon()
        {
            if (!startedWorld || MMTransition.IsPlaying) return false;
            if (GameManager.IsDungeon(PlayerFarming.Location)) return true;
            PlayerFarming player = PlayerFarming.Instance;
            if (!Ready(player)) return false;
            Collider2D playerCollider = null;
            foreach (Collider2D candidate in player.GetComponentsInChildren<Collider2D>(true))
                if (candidate != null && candidate.gameObject.CompareTag("Player")) { playerCollider = candidate; break; }
            if (playerCollider == null) { lastError = "No actual Player-tagged native collider is ready for the dungeon doorway."; return false; }
            foreach (Interaction_BaseDungeonDoor door in Resources.FindObjectsOfTypeAll<Interaction_BaseDungeonDoor>())
            {
                if (door == null || door.Location != FollowerLocation.Dungeon1_1 || !door.gameObject.scene.IsValid() || !door.gameObject.scene.isLoaded || String.IsNullOrWhiteSpace(door.SceneName)) continue;
                // Use the live serialized destination and the complete native trigger path.
                // MMTransition's Dungeon1 route loads dungeon assets; the legacy Game Biome 1 scene alone does not.
                MethodInfo trigger = AccessTools.Method(typeof(Interaction_BaseDungeonDoor), "OnTriggerEnter2D", new[] { typeof(Collider2D) });
                if (trigger == null) throw new MissingMethodException("Native dungeon doorway trigger is unavailable.");
                logger.LogInfo("Entering real native Darkwood doorway; destination=" + door.SceneName);
                trigger.Invoke(door, new object[] { playerCollider });
                return true;
            }
            lastError = "The loaded cult scene has no native Darkwood doorway yet.";
            return false;
        }

        private void CheckResources(PlayerFarming player)
        {
            if (reported.Contains("minecraft-resource-visuals")) return;
            if (!resourceIconsObserved)
            {
                InventoryIconMapping[] maps = Resources.FindObjectsOfTypeAll<InventoryIconMapping>();
                foreach (InventoryIconMapping map in maps)
                {
                    int beforeWood = Inventory.GetItemQuantity(InventoryItem.ITEM_TYPE.LOG);
                    int beforeStone = Inventory.GetItemQuantity(InventoryItem.ITEM_TYPE.STONE);
                    Sprite wood = map.GetImage(InventoryItem.ITEM_TYPE.LOG);
                    Sprite stone = map.GetImage(InventoryItem.ITEM_TYPE.STONE);
                    if (wood != null && stone != null && wood.texture == assets.Texture("oak_log") && stone.texture == assets.Texture("stone") && beforeWood == Inventory.GetItemQuantity(InventoryItem.ITEM_TYPE.LOG) && beforeStone == Inventory.GetItemQuantity(InventoryItem.ITEM_TYPE.STONE))
                    {
                        resourceIconsObserved = true;
                        break;
                    }
                }
            }
            CheckVegetation(player);
            bool newLootObserved=lootProof.Check(player);
            if (resourceIconsObserved && vegetationObserved && devotionObserved && newLootObserved) Report("minecraft-resource-visuals", true, null);
        }

        private void CheckVegetation(PlayerFarming player)
        {
            if (vegetationObserved || player == null || !Ready(player) || PlayerFarming.Location != FollowerLocation.Base) return;
            if (vegetationStarted < 0f) vegetationStarted = Time.unscaledTime;
            if (Time.unscaledTime - vegetationStarted > V("test_vegetation_timeout"))
            {
                LogVegetation("timeout");
                Report("minecraft-resource-visuals", false, "Native vegetation observation timed out at stage " + vegetationStage + "; " + (lastError ?? "fixtures not ready"));
                return;
            }
            if (!vegetationRequested)
            {
                vegetationRequested = true;
                // All eight native fixtures exist only in the marked memory-only world.
                // Use real native structure definitions and construction, never detached proof meshes.
                for (int i = 0; i < vegetationObjects.Length; i++)
                {
                    int index = i;
                    StructureBrain.TYPES type = VegetationFixtureType(i);
                    StructuresData design = StructuresData.GetInfoByType(type, i < 4 ? i : 0);
                    if (design == null) throw new InvalidOperationException("Native vegetation definition is absent: " + type + "/" + i);
                    // GetInfoByType allocates a fresh native definition. These are initial
                    // fixture conditions, not changes to a player's world or native templates.
                    if (i == 0) { design.IsSapling = true; design.GrowthStage = 2f; design.Progress = 0f; }
                    if (i == 4) { design.CanRegrow = true; design.Progress = 0f; }
                    Vector3 offset = i < 4 ? new Vector3((i - 1.5f) * V("test_vegetation_spacing"), -V("test_vegetation_distance"), 0f) :
                        new Vector3((i == 4 ? -1f : 1f) * V("test_vegetation_distance"), -V("test_vegetation_spacing"), 0f);
                    if(i>=6)offset=new Vector3((i==6?-1f:1f)*V("test_vegetation_spacing"),V("test_vegetation_distance"),0f);
                    StructureManager.BuildStructure(PlayerFarming.Location, design, player.transform.position + offset,
                        new Vector2Int(Math.Max(1, design.TILE_WIDTH), Math.Max(1, design.TILE_HEIGHT)), false,
                        created => { vegetationObjects[index] = created; logger.LogInfo("Native vegetation fixture " + index + " completed: " + (created == null ? "absent" : created.name)); }, null, false);
                }
                logger.LogInfo("Requested native isolated resource fixtures: four TREE variants, regrowing berry bush, farm plot, actual RUBBLE61 and RUBBLE_BIG159.");
                return;
            }
            for (int i = 0; i < vegetationNatives.Length; i++)
            {
                if (vegetationNatives[i] != null || vegetationObjects[i] == null) continue;
                StructureBrain.TYPES type = VegetationFixtureType(i);
                foreach (Structure native in vegetationObjects[i].GetComponentsInChildren<Structure>(true))
                {
                    if (native == null || native.Type != type || native.Brain == null || native.Structure_Info == null) continue;
                    vegetationNatives[i] = native;
                    vegetationData[i] = native.Structure_Info;
                    break;
                }
            }
            if (Time.unscaledTime >= nextVegetationLog) { nextVegetationLog = Time.unscaledTime + 2f; LogVegetation("waiting"); }
            for (int i = 0; i < vegetationNatives.Length; i++)
                if (vegetationNatives[i] == null && !(vegetationStage >= 4 && i == 4)) return;
            var plot = vegetationNatives[5].Brain as Structures_FarmerPlot;
            if (plot == null) throw new InvalidOperationException("Actual FARM_PLOT fixture lacks native Structures_FarmerPlot.");
            if (!farmPlanted)
            {
                plot.PlantSeed(InventoryItem.ITEM_TYPE.SEED);
                plot.OnGrowthStageChanged?.Invoke();
                farmPlanted = true;
                logger.LogInfo("Planted native berry SEED in actual isolated farm plot; native inventory and CropController own crop state.");
                return;
            }
            if (vegetationStage == 0)
            {
                string initial=gameplay.NativeVegetationId(vegetationNatives[0]);
                if(initial!="oak_sapling" && initial!="birch_sapling" && initial!="spruce_sapling")return;
                treeFixtureSpecies=initial.Substring(0,initial.IndexOf('_'));
                string[] expected = { treeFixtureSpecies+"_sapling", "birch_small", "oak_large", "spruce_small", "berry_3", "berry_0" };
                float[] heights = new float[expected.Length];
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!ObserveVegetationFacade(vegetationNatives[i], expected[i], out heights[i])) return;
                    if (!gameplay.NativeStructurePreserved(vegetationNatives[i])) { lastError="Native fixture "+i+"/"+expected[i]+" collision/data snapshot changed after construction."; return; }
                }
                if (heights[2] <= heights[1] * V("test_vegetation_large_ratio") || heights[2] <= heights[3] * V("test_vegetation_large_ratio"))
                {
                    lastError = "Rendered native large tree is not actually taller than native small forms: large=" + heights[2] + ", birch=" + heights[1] + ", spruce=" + heights[3];
                    return;
                }
                float smallRock=0f,bigRock=0f;
                if(!ObserveStoneFacade(vegetationNatives[6],out smallRock) || !ObserveStoneFacade(vegetationNatives[7],out bigRock) || bigRock<=smallRock*V("test_vegetation_large_ratio"))
                { lastError="Actual RUBBLE61/159 stone mesh/native preservation proof incomplete: small="+smallRock+", big="+bigRock+"; "+stoneFitDetail;return; }
                FarmPlot nativePlot = FarmPlot.GetFarmPlot(vegetationData[5].ID);
                Renderer[] plotRenderers = nativePlot != null ? nativePlot.GetComponentsInChildren<Renderer>(true) : vegetationNatives[5].GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in plotRenderers)
                    if (renderer != null && !(renderer is ParticleSystemRenderer) && renderer.GetComponentInParent<CropController>() == null && renderer.GetComponentInParent<Canvas>() == null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        farmGroundRenderers[renderer] = renderer.enabled;
                if (farmGroundRenderers.Count == 0) { lastError = "The planted farm has no retained visible native ground renderer."; return; }
                treeRevision = gameplay.NativeVegetationRevision(vegetationNatives[0]);
                berryRevision = gameplay.NativeVegetationRevision(vegetationNatives[4]);
                farmRevision = gameplay.NativeVegetationRevision(vegetationNatives[5]);
                AdvanceNativeTreeDay();
                vegetationStage = 1;
                LogVegetation("initial species/size/sapling/ripe/planted models observed; actual native day advanced");
                return;
            }
            float height;
            if (vegetationStage == 1)
            {
                if (vegetationData[0].GrowthStage != 3f || !vegetationData[0].IsSapling || !ObserveVegetationFacade(vegetationNatives[0], treeFixtureSpecies+"_small", out height) || gameplay.NativeVegetationRevision(vegetationNatives[0]) <= treeRevision) return;
                treeSmallHeight = height;
                treeRevision = gameplay.NativeVegetationRevision(vegetationNatives[0]);
                AdvanceNativeTreeDay(); AdvanceNativeTreeDay();
                vegetationStage = 2;
                LogVegetation("native growth2->3 produced actual small-tree mesh; advanced to native day5");
                return;
            }
            if (vegetationStage == 2)
            {
                if (vegetationData[0].GrowthStage != 5f || !vegetationData[0].IsSapling || !ObserveVegetationFacade(vegetationNatives[0], treeFixtureSpecies+"_medium", out height) || gameplay.NativeVegetationRevision(vegetationNatives[0]) <= treeRevision || height <= treeSmallHeight) return;
                treeYoungHeight = height;
                treeRevision = gameplay.NativeVegetationRevision(vegetationNatives[0]);
                AdvanceNativeTreeDay();
                vegetationStage = 3;
                LogVegetation("native day5 produced actual taller young-tree mesh; advanced to maturity");
                return;
            }
            if (vegetationStage == 3)
            {
                if (vegetationData[0].IsSapling || !ObserveVegetationFacade(vegetationNatives[0], treeFixtureSpecies+"_medium", out height) || gameplay.NativeVegetationRevision(vegetationNatives[0]) <= treeRevision || height <= treeYoungHeight) return;
                Structures_BerryBush bush = vegetationNatives[4].Brain as Structures_BerryBush;
                if (bush == null) throw new InvalidOperationException("Actual native berry fixture lacks Structures_BerryBush.");
                berriesBeforeTestHarvest = Inventory.GetItemQuantity(InventoryItem.ITEM_TYPE.BERRY);
                harvestedBerryFacade = gameplay.NativeStructureVisual(vegetationNatives[4]);
                bush.PickBerries(Math.Max(1f, bush.Data.ProgressTarget - bush.Data.Progress), false);
                nativeBerryHarvestDidNotGrantLoot = Inventory.GetItemQuantity(InventoryItem.ITEM_TYPE.BERRY) == berriesBeforeTestHarvest;
                plot.ForceFullyGrown();
                vegetationStage = 4;
                LogVegetation("native mature-tree transition observed; native berry harvest(no loot) and native ForceFullyGrown crop fixture requested");
                return;
            }
            if (vegetationStage == 4)
            {
                bool berryPicked = vegetationData[4].Progress >= vegetationData[4].ProgressTarget;
                bool berryGone = vegetationNatives[4] == null || !vegetationNatives[4].gameObject.activeInHierarchy;
                bool harvestedVisual = berryGone ? harvestedBerryFacade == null || !harvestedBerryFacade.gameObject.activeInHierarchy :
                    ObserveVegetationFacade(vegetationNatives[4], "berry_0", out height) && gameplay.NativeVegetationRevision(vegetationNatives[4]) > berryRevision;
                if (!berryPicked || !harvestedVisual || !nativeBerryHarvestDidNotGrantLoot || !plot.IsFullyGrown ||
                    !ObserveVegetationFacade(vegetationNatives[5], "berry_3", out height) || gameplay.NativeVegetationRevision(vegetationNatives[5]) <= farmRevision) return;
                foreach (var pair in farmGroundRenderers) if (pair.Key == null || pair.Key.enabled != pair.Value) { lastError = "Native farm ground was hidden or replaced during crop facade rebuild."; return; }
                if (!stoneWalk.Complete) { stoneWalk.Tick(player, vegetationNatives[7], Ready(player) && meditationReleased); return; }
                if (!stoneWalk.Passed) Report("minecraft-resource-visuals", false, stoneWalk.Detail);
                vegetationObserved = true;
                vegetationStage = 5;
                LogVegetation("PASS: actual species/size variants, RUBBLE61/159 textured stone models, native growth rebuilds, no-loot native berry harvest and planted0->ripe3; ground preserved");
            }
        }

        private static StructureBrain.TYPES VegetationFixtureType(int index)
        {
            return index<4 ? StructureBrain.TYPES.TREE : index==4 ? StructureBrain.TYPES.BERRY_BUSH : index==5 ? StructureBrain.TYPES.FARM_PLOT : index==6 ? StructureBrain.TYPES.RUBBLE : StructureBrain.TYPES.RUBBLE_BIG;
        }

        private bool ObserveStoneFacade(Structure native,out float height)
        {
            height=0f;
            if(native==null || !native.gameObject.activeInHierarchy || native.Brain==null || native.Structure_Info==null || native.Brain.Data!=native.Structure_Info || native.Structure_Info.LootToDrop!=InventoryItem.ITEM_TYPE.STONE || !gameplay.NativeStructurePreserved(native))return false;
            Transform facade=gameplay.NativeStructureVisual(native);
            if(facade==null || !facade.gameObject.activeInHierarchy || facade.localToWorldMatrix.determinant<=0f)return false;
            bool found=false;
            Bounds bounds=new Bounds();
            foreach(MeshRenderer renderer in facade.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff)continue;
                MeshFilter filter=renderer.GetComponent<MeshFilter>();
                if(filter==null || filter.sharedMesh==null || filter.sharedMesh.vertexCount<24 || renderer.sharedMaterial==null || renderer.sharedMaterial.mainTexture!=assets.Texture("stone") || renderer.sharedMaterial.shader==null || !renderer.sharedMaterial.shader.isSupported || renderer.transform.localToWorldMatrix.determinant<=0f || renderer.GetComponent<Collider>()!=null)return false;
                if(!found)bounds=renderer.bounds;else bounds.Encapsulate(renderer.bounds);
                found=true;
            }
            height=found?bounds.size.z:0f;
            if (found && height > 0.05f && !RockPileFit.Aligned(facade, native.transform, out string fitDetail)) { stoneFitDetail = "Rock fit: " + fitDetail; return false; }
            return found && height>0.05f;
        }

        private void AdvanceNativeTreeDay()
        {
            Structures_Tree tree = vegetationNatives[0].Brain as Structures_Tree;
            MethodInfo day = AccessTools.Method(typeof(Structures_Tree), "OnNewDayStarted", Type.EmptyTypes);
            if (tree == null || day == null) throw new MissingMethodException("Native Structures_Tree.OnNewDayStarted fixture API is unavailable.");
            day.Invoke(tree, null);
        }

        private bool ObserveVegetationFacade(Structure native, string expected, out float height)
        {
            height = 0f;
            if (native == null || !native.gameObject.activeInHierarchy || native.Brain == null || native.Structure_Info == null || native.Brain.Data != native.Structure_Info || gameplay.NativeVegetationId(native) != expected)
            {lastError="Native "+expected+" state mismatch: exists="+(native!=null)+", active="+(native!=null&&native.gameObject.activeInHierarchy)+", data="+(native!=null&&native.Brain!=null&&native.Brain.Data==native.Structure_Info)+", profile="+(native==null?"absent":gameplay.NativeVegetationId(native));return false;}
            Transform facade = gameplay.NativeStructureVisual(native);
            if (facade == null || !facade.gameObject.activeInHierarchy || facade.localToWorldMatrix.determinant <= 0f)
            {lastError="Native "+expected+" facade missing/inactive/nonpositive.";return false;}
            int renderers = 0;
            bool textureObserved = false, leafObserved = false, hasBounds = false;
            Bounds bounds = new Bounds();
            string species = expected.StartsWith("birch_", StringComparison.Ordinal) ? "birch" : expected.StartsWith("spruce_", StringComparison.Ordinal) ? "spruce" : "oak";
            bool berry = expected.StartsWith("berry_", StringComparison.Ordinal), sapling = expected.EndsWith("_sapling", StringComparison.Ordinal);
            string required = berry ? "sweet_berry_bush_stage" + expected.Substring(expected.Length - 1) : species + (sapling ? "_sapling" : "_log");
            foreach (MeshRenderer renderer in facade.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount < 4 || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null || !renderer.sharedMaterial.shader.isSupported || renderer.sharedMaterial.mainTexture == null || renderer.GetComponent<Collider>() != null || renderer.transform.localToWorldMatrix.determinant <= 0f)
                {lastError="Native "+expected+" invalid renderer="+renderer.name+", mesh="+(filter!=null&&filter.sharedMesh!=null?filter.sharedMesh.vertexCount:0)+", material="+(renderer.sharedMaterial==null?"absent":renderer.sharedMaterial.name)+", determinant="+renderer.transform.localToWorldMatrix.determinant;return false;}
                if (renderer.sharedMaterial.mainTexture == assets.Texture(required)) textureObserved = true;
                if (!berry && !sapling && renderer.sharedMaterial.name.Contains(species + "_leaves")) leafObserved = true;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds);
                renderers++;
            }
            height = hasBounds ? bounds.size.z : 0f; // Native XY ground, negative-Z height.
            bool observed=textureObserved && (berry || sapling || leafObserved) && height > 0.05f && renderers > 0 && renderers <= (berry || sapling ? 1 : 3);
            if(!observed)lastError="Actual "+expected+" visual: expected texture="+required+", matched="+textureObserved+", leaves="+leafObserved+", height="+height+", renderers="+renderers;
            return observed;
        }

        private void LogVegetation(string reason)
        {
            string text = "Native vegetation " + reason + "; stage=" + vegetationStage;
            for (int i = 0; i < vegetationNatives.Length; i++)
            {
                Structure native = vegetationNatives[i];
                text += "; " + i + ":" + (native == null ? "absent" : gameplay.NativeVegetationId(native) ?? "no profile") + "/revision=" + (native == null ? 0 : gameplay.NativeVegetationRevision(native));
                if (native == null && vegetationObjects[i] != null) text += "/created=" + vegetationObjects[i].name;
                if (vegetationData[i] != null) text += "/nativeGrowth=" + vegetationData[i].GrowthStage + "/sapling=" + vegetationData[i].IsSapling;
            }
            if(lastError!=null)text+="; detail="+lastError;
            logger.LogInfo(text);
        }

        private float progressionStepStarted = -1f;
        private float progressionStableSince = -1f;
        private int progressionRetries;
        private string lastProgressionInterruption;
        private bool axeEquipped;
        private float axeStarted=-1f;
        private float axeStableSince=-1f;
        private bool axeProofFinished;
        private int replacementStep;
        private bool replacementEquipped;
        private float replacementStarted=-1f;
        private float replacementEquippedAt=-1f;
        private float replacementStableSince=-1f;
        private int replacementShotsBefore, replacementArrowDrawsBefore;
        private bool replacementDigitObserved;
        private bool replacementLmbObserved;
        private bool replacementArrowObserved;
        private bool replacementProofFinished;
        private int nativeGunOriginalCalls;

        private void RunProgression(PlayerFarming player)
        {
            if(progressionStep>=progressionLevels.Length)
            {
                if(replacementProofFinished)
                {
                    if(features.CheckAttacks(player,gameplay))
                    {preparationRoom=null;combatPreparationStarted=Time.unscaledTime;Next(8);}
                    return;
                }
                if(!axeProofFinished)RunAxeProof(player);else RunReplacementProof(player);
                return;
            }
            int level = progressionLevels[progressionStep];
            int maxRetries = (int)V("test_progression_retries");
            float now = Time.unscaledTime;
            if (progressionStepStarted < 0f)
            {
                progressionStepStarted = now;
                progressionRetries = 0;
                progressionStableSince = -1f;
            }
            // A real pickup coroutine can finish between test updates and equip its
            // own native level. Keep one absolute deadline for this step across all
            // retries, rather than repeatedly extending the test until it passes.
            bool deadline = now - progressionStepStarted >= V("test_progression_deadline");
            ReleaseBowInput();
            MeltyPlay.Hold("1", gameplay.SelectedSlot != 0);
            // Evaluate the same monotonic deadline even during genuine native
            // transitions/actions. Readiness remains required for equip and proof.
            if(!Ready(player) || player!=gearOwner || player.playerWeapon==null)
            {
                InterruptProgressionProof(player,"native player not ready for level "+level);
                if(deadline)CompleteProgressionStep(player,level,false,false,float.NaN,false);
                return;
            }
            if (!progressionEquipped && !deadline && gameplay.SelectedSlot == 0)
            {
                player.playerWeapon.SetWeapon(EquipmentType.Sword, level);
                progressionEquipped = true;
                progressionStableSince = -1f;
                stageSince = now;
                return;
            }
            float elapsed = now - stageSince;
            WeaponRow expected = Spec.WeaponRows[0];
            foreach (WeaponRow candidate in Spec.WeaponRows)
                if (candidate.MinLevel <= level && candidate.MinLevel >= expected.MinLevel) expected = candidate;
            WeaponData actualWeapon = player.currentWeapon == EquipmentType.None ? null : EquipmentManager.GetWeaponData(player.currentWeapon);
            bool nativeExpected = player.currentWeaponLevel == level && actualWeapon != null && actualWeapon.PrimaryEquipmentType == EquipmentType.Sword;
            bool material = nativeExpected && gameplay.SelectedSlot == 0 && gameplay.WeaponId == expected.Id && RenderedReplacement(expected.Id);
            int expectedEnchant = sourceEnchantments.SharpnessLevel(level);
            float expectedBonus = sourceEnchantments.MeleeBonus(level);
            // Native BaseDamage is multiplicative: zero observes only the actual
            // installed additive enchantment, without duplicating the native formula.
            float observedBonus = PlayerWeapon.GetDamage(0f, level, player);
            bool enchantment = sourceEnchantments.Ready && nativeExpected && gameplay.SelectedSlot == 0 && gameplay.EnchantmentLevel == expectedEnchant && Math.Abs(observedBonus - expectedBonus) < 0.01f && avatar.GlintActive == (expectedEnchant > 0);
            bool stableProof = false;
            if (progressionEquipped && material && enchantment)
            {
                if (progressionStableSince < 0f) { progressionStableSince = now;lastProgressionInterruption=null; }
                stableProof = now - progressionStableSince >= V("test_progression_settle");
            }
            else InterruptProgressionProof(player,"native/material/enchantment/render condition interrupted at level "+level+", equipped="+progressionEquipped+", material="+material+", enchantment="+enchantment);
            if (!stableProof && !deadline)
            {
                bool interrupted = progressionEquipped && (!nativeExpected || gameplay.SelectedSlot != 0);
                bool attemptExpired = progressionEquipped && elapsed >= V("test_progression_timeout");
                if (interrupted || attemptExpired)
                {
                    if (progressionRetries < maxRetries)
                    {
                        progressionRetries++;
                        logger.LogInfo("Retrying interrupted progression step " + level + ": actual native=" + player.currentWeapon + "/" + player.currentWeaponLevel +
                            ", selected=" + gameplay.SelectedSlot + ", item=" + avatar.HeldItemId + ", enchantment=" + gameplay.EnchantmentLevel + ", glint=" + avatar.GlintActive +
                            ", retry=" + progressionRetries + "/" + maxRetries + "; the absolute step deadline is unchanged; "+ProgressionContext(player));
                        progressionEquipped = false;
                        progressionStableSince = -1f;
                        return;
                    }
                }
                else return;
            }
            if (!stableProof) { material = false; enchantment = false; }
            CompleteProgressionStep(player,level,material,enchantment,observedBonus,stableProof);
        }

        private string ProgressionContext(PlayerFarming player)
        {
            return "state="+(player==null || player.state==null?"absent":player.state.CURRENT_STATE.ToString())+
                ", ready="+Ready(player)+", transition="+MMTransition.IsPlaying+", letterbox="+LetterBox.IsPlaying+", conversation="+MMConversation.isPlaying+
                ", native goto="+(player!=null && player.GoToAndStopping)+", room="+RoomIdentity(BiomeGenerator.Instance==null?null:BiomeGenerator.Instance.CurrentRoom);
        }

        private void InterruptProgressionProof(PlayerFarming player,string reason)
        {
            string detail=reason+"; "+ProgressionContext(player);
            if(progressionStableSince>=0f || detail!=lastProgressionInterruption)logger.LogInfo("Continuous progression proof reset: "+detail);
            lastProgressionInterruption=detail;
            progressionStableSince=-1f;
        }

        private void CompleteProgressionStep(PlayerFarming player,int level,bool material,bool enchantment,float observedBonus,bool stableProof)
        {
            materialsObserved &= material;
            enchantmentsObserved &= enchantment;
            logger.LogInfo("Observed progression level " + level + ": native=" + (player==null?"absent":player.currentWeapon+"/"+player.currentWeaponLevel) + ", selected=" + gameplay.SelectedSlot +
                ", item=" + avatar.HeldItemId + ", tier=" + gameplay.WeaponId + ", enchantment=" + gameplay.EnchantmentLevel + ", glint=" + avatar.GlintActive +
                ", additive damage=" + observedBonus.ToString("F3", CultureInfo.InvariantCulture) + ", stable proof=" + stableProof + ", retries=" + progressionRetries+
                ", absolute elapsed="+(Time.unscaledTime-progressionStepStarted).ToString("F3",CultureInfo.InvariantCulture)+"; "+ProgressionContext(player));
            MeltyPlay.Hold("1", false);
            progressionStep++;
            progressionEquipped = false;
            progressionStepStarted = -1f;
            progressionStableSince = -1f;
            progressionRetries = 0;
            lastProgressionInterruption=null;
            if (progressionStep < progressionLevels.Length) return;
        }

        private void RunAxeProof(PlayerFarming player)
        {
            float now=Time.unscaledTime;
            int level=(int)V("test_axe_level");
            ReleaseBowInput();
            MeltyPlay.Hold("1",gameplay.SelectedSlot!=0);
            if(axeStarted<0f)axeStarted=now;
            if(!Ready(player) || player!=gearOwner || player.playerWeapon==null)
            {
                if(axeStableSince>=0f)logger.LogInfo("Continuous native axe proof reset; "+ProgressionContext(player));
                axeStableSince=-1f;
                if(now-axeStarted>=V("test_axe_timeout"))CompleteAxeProof(player,null,false,false);
                return;
            }
            if(!axeEquipped && now-axeStarted<V("test_axe_timeout") && gameplay.SelectedSlot==0)
            {
                player.playerWeapon.SetWeapon(EquipmentType.Axe,level);
                axeEquipped=true;
                return;
            }
            WeaponData actual=player.currentWeapon==EquipmentType.None?null:EquipmentManager.GetWeaponData(player.currentWeapon);
            bool observed=axeEquipped && actual!=null && actual.PrimaryEquipmentType==EquipmentType.Axe && player.currentWeaponLevel==level && gameplay.SelectedSlot==0 && gameplay.WeaponId=="wooden_axe" && avatar.HeldItemId=="wooden_axe" && avatar.WorldVisible && avatar.Root!=null;
            bool mesh=false;
            if(observed)
                foreach(MeshRenderer renderer in avatar.Root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter filter=renderer.GetComponent<MeshFilter>();
                    if(renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff && renderer.sharedMaterial!=null && renderer.sharedMaterial.mainTexture==assets.Texture("wooden_axe") && renderer.sharedMaterial.shader!=null && renderer.sharedMaterial.shader.isSupported && filter!=null && filter.sharedMesh!=null && filter.sharedMesh.vertexCount>=4 && renderer.transform.localToWorldMatrix.determinant>0f)mesh=true;
                }
            if(observed && mesh){if(axeStableSince<0f)axeStableSince=now;}else axeStableSince=-1f;
            bool stable=axeStableSince>=0f && now-axeStableSince>=V("test_axe_settle");
            if(!stable && now-axeStarted<V("test_axe_timeout"))return;
            CompleteAxeProof(player,actual,mesh,stable);
        }

        private void CompleteAxeProof(PlayerFarming player,WeaponData actual,bool mesh,bool stable)
        {
            materialsObserved &= stable;
            logger.LogInfo("Actual native axe proof: PrimaryEquipmentType="+(actual==null?"absent":actual.PrimaryEquipmentType.ToString())+", native="+(player==null?"absent":player.currentWeapon+"/"+player.currentWeaponLevel)+", Minecraft="+gameplay.WeaponId+", actual textured mesh="+mesh+", stable="+stable+"; "+ProgressionContext(player));
            MeltyPlay.Hold("1",false);
            axeProofFinished=true;
        }

        private bool RenderedReplacement(string id)
        {
            if(!avatar.WorldVisible || avatar.Root==null || avatar.HeldItemId!=id || Camera.main==null)return false;
            Plane[] frustum=GeometryUtility.CalculateFrustumPlanes(Camera.main);
            foreach(MeshRenderer renderer in avatar.Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter=renderer.GetComponent<MeshFilter>();
                if(renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff && renderer.sharedMaterial!=null && renderer.sharedMaterial.mainTexture==assets.Texture(id) && renderer.sharedMaterial.shader!=null && renderer.sharedMaterial.shader.isSupported && filter!=null && filter.sharedMesh!=null && filter.sharedMesh.vertexCount>=4 && renderer.transform.localToWorldMatrix.determinant>0f && GeometryUtility.TestPlanesAABB(frustum,renderer.bounds))return true;
            }
            return false;
        }

        private void RunReplacementProof(PlayerFarming player)
        {
            float now=Time.unscaledTime;
            bool ranged=replacementStep==1;
            EquipmentType type=ranged?EquipmentType.Blunderbuss:EquipmentType.Hammer;
            string item=ranged?"crossbow_arrow":"mace";
            int level=(int)V("test_axe_level");
            if(replacementStarted<0f)replacementStarted=now;
            ReleaseBowInput();
            // Repeat a real press until the active Input System keyboard has
            // observed it. A brief initial hold can be missed if another device
            // becomes current while native equipment is settling.
            bool selectionPulse=ranged && !replacementDigitObserved && Mathf.Repeat(now-replacementStarted,0.4f)<0.22f;
            MeltyPlay.Hold("1",gameplay.SelectedSlot!=0 || selectionPulse);
            bool ready=Ready(player) && player==gearOwner && player.playerWeapon!=null;
            if(ready && !replacementEquipped && now-replacementStarted<V("test_axe_timeout") && gameplay.SelectedSlot==0)
            {
                player.playerWeapon.SetWeapon(type,level);
                replacementEquipped=true;replacementEquippedAt=now;replacementShotsBefore=gameplay.ShotsFired;
                replacementArrowDrawsBefore=gameplay.ArrowEntityDrawFrames;
                if(ranged)MeltyPlay.Hold("1",true);
                return;
            }
            WeaponData actual=player==null || player.currentWeapon==EquipmentType.None?null:EquipmentManager.GetWeaponData(player.currentWeapon);
            bool mesh=RenderedReplacement(item);
            bool native=ready && replacementEquipped && actual!=null && actual.PrimaryEquipmentType==type && player.currentWeapon==type && player.currentWeaponLevel==level;
            bool material=native && gameplay.SelectedSlot==0 && gameplay.WeaponId==item && mesh;
            bool enchantment=!ranged || sourceEnchantments.Ready && gameplay.NativeRangedEquipped &&
                gameplay.BowEnchantmentLevel==sourceEnchantments.PowerLevel(level) && !gameplay.BowHeld && !avatar.GlintActive;
            bool shot=!ranged;
            if(ranged)
            {
                if(UnityEngine.InputSystem.Keyboard.current!=null && UnityEngine.InputSystem.Keyboard.current.digit1Key.isPressed)replacementDigitObserved=true;
                if(material && enchantment && replacementDigitObserved)
                {
                    Camera aimingCamera=Camera.main;
                    if(aimingCamera!=null)
                    {
                        Vector3 pointer=aimingCamera.WorldToScreenPoint(player.transform.position+Vector3.right);
                        MeltyPlay.Point(pointer.x/Math.Max(1,Screen.width),1f-pointer.y/Math.Max(1,Screen.height));
                    }
                    MeltyPlay.Button(0,true);
                }
                if(material && enchantment && replacementDigitObserved && UnityEngine.InputSystem.Mouse.current!=null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)replacementLmbObserved=true;
                if(replacementLmbObserved && gameplay.ArrowEntityDrawFrames>replacementArrowDrawsBefore)
                    replacementArrowObserved=true;
                shot=replacementDigitObserved && replacementLmbObserved && replacementArrowObserved && gameplay.ShotsFired>replacementShotsBefore && nativeGunOriginalCalls==0;
            }
            if(material && enchantment && shot){if(replacementStableSince<0f)replacementStableSince=now;}else replacementStableSince=-1f;
            bool stable=replacementStableSince>=0f && now-replacementStableSince>=V("test_axe_settle");
            if(!stable && now-replacementStarted<V("test_axe_timeout"))return;
            ReleaseBowInput();MeltyPlay.Hold("1",false);
            materialsObserved &= stable;
            if(ranged)enchantmentsObserved &= stable && enchantment;
            logger.LogInfo("Actual native replacement proof: "+type+"/"+level+" -> "+item+", native identity/level="+native+", textured mesh="+mesh+", Power="+gameplay.BowEnchantmentLevel+", glint="+avatar.GlintActive+", digit1="+replacementDigitObserved+", raw LMB="+replacementLmbObserved+", custom shots="+(gameplay.ShotsFired-replacementShotsBefore)+", actual arrow="+replacementArrowObserved+", native gun original calls="+nativeGunOriginalCalls+", stable="+stable+"; "+ProgressionContext(player));
            replacementStep++;
            replacementEquipped=false;replacementStarted=-1f;replacementEquippedAt=-1f;replacementStableSince=-1f;
            if(replacementStep<2)return;
            Report("minecraft-material-progression", materialsObserved, materialsObserved ? null : "Native sword levels, axe, mace or crossbow did not produce the required textured native-preserving replacement proof.");
            Report("minecraft-enchantment-progression", enchantmentsObserved, enchantmentsObserved ? null : "Native sword levels or ranged Power/glint did not produce the required stable source-derived enchantment proof within bounded observation attempts; source error=" + (sourceEnchantments.Error ?? "none"));
            RestoreGear();
            replacementProofFinished=true;
        }

        private void RestoreGear()
        {
            if (!gearCaptured) return;
            if (gearOwner != null && gearOwner.playerWeapon != null)
                gearOwner.playerWeapon.SetWeapon(savedWeapon, Math.Max(1, savedWeaponLevel));
            gearCaptured = false;
        }

        private void CheckConstruction(PlayerFarming player)
        {
            if (reported.Contains("minecraft-construction-facade") || player == null || Structure.Structures == null) return;
            foreach (Structure native in Structure.Structures)
            {
                if (native == null || !native.gameObject.activeInHierarchy || native.Brain == null) continue;
                bool straw = native.Type == StructureBrain.TYPES.BED;
                bool red = native.Type == StructureBrain.TYPES.BED_2;
                if ((!straw && !red) || straw && bedObserved || red && hutObserved) continue;
                // Native building colliders may live on the prefab root or siblings,
                // and an unassigned bed need not have an enabled child collider.
                // Verify the actual native brain/data and the pre-mod collision snapshot.
                if (!gameplay.NativeStructurePreserved(native)) continue;
                Transform facade = gameplay.NativeStructureVisual(native);
                if (facade == null) continue;
                BuildingRow design = Spec.Building(straw ? "bed" : "bed_2");
                if (design.Model != (straw ? "straw_bed" : "bed")) continue;
                Dwelling dwelling = native.GetComponentInChildren<Dwelling>(true);
                if (dwelling == null || dwelling.Positions.Count == 0 || dwelling.Positions[0].BedOccupied == null) continue;
                Vector3 pillow = facade.TransformPoint(BlockModels.BedPillow(design));
                Vector3 nativeHead = dwelling.Positions[0].BedOccupied.transform.position;
                if (Vector2.Distance(new Vector2(pillow.x,pillow.y),new Vector2(nativeHead.x,nativeHead.y)) > 0.03f)
                { lastError="Native bed pillow/head mismatch: type="+native.Type+", pillow="+pillow+", native sleeping head="+nativeHead; continue; }
                foreach (MeshRenderer renderer in facade.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter mesh = renderer.GetComponent<MeshFilter>();
                    if (renderer.name.StartsWith("CultCraft batched ", StringComparison.Ordinal) && renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff && mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.vertexCount >= 4 && mesh.sharedMesh.triangles.Length>=6 && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.isSupported && renderer.sharedMaterial.mainTexture == assets.Texture(design.Texture) && renderer.GetComponent<Collider>() == null && renderer.transform.localToWorldMatrix.determinant > 0f)
                    {
                        if (straw) bedObserved=true; else hutObserved=true;
                        logger.LogInfo("Actual native bed proof: "+native.Type+" → "+design.Model+", source texture="+design.Texture+", model pillow="+pillow+", native Dwelling sleeping head="+nativeHead+", native brain/data/colliders preserved and positive textured geometry.");
                        break;
                    }
                }
            }
            if (!buildingRequested && PlayerFarming.Location == FollowerLocation.Base && Ready(player))
            {
                buildingRequested = true;
                var design = StructuresData.GetInfoByType(StructureBrain.TYPES.BED, 0);
                Vector3 position = player.transform.position + Vector3.right * V("test_build_distance");
                StructureManager.BuildStructure(PlayerFarming.Location, design, position, new Vector2Int(Math.Max(1, design.TILE_WIDTH), Math.Max(1, design.TILE_HEIGHT)), false, created => logger.LogInfo("Native test bed completed: " + (created == null ? "absent" : created.name)), null, false);
            }
            if (!hutRequested && PlayerFarming.Location == FollowerLocation.Base && Ready(player))
            {
                hutRequested = true;
                var design = StructuresData.GetInfoByType(StructureBrain.TYPES.BED_2, 0);
                Vector3 position = player.transform.position + new Vector3(V("test_build_distance"),2.5f,0f);
                StructureManager.BuildStructure(PlayerFarming.Location, design, position, new Vector2Int(Math.Max(1,design.TILE_WIDTH),Math.Max(1,design.TILE_HEIGHT)),false,created=>logger.LogInfo("Native test hut BED_2 completed: "+(created==null?"absent":created.name)),null,false);
            }
            if(!campfireRequested && PlayerFarming.Location==FollowerLocation.Base && Ready(player))
            {
                campfireRequested=true;
                StructuresData design=StructuresData.GetInfoByType(StructureBrain.TYPES.COOKING_FIRE,0);
                Vector3 position=player.transform.position-Vector3.right*V("test_build_distance");
                StructureManager.BuildStructure(PlayerFarming.Location,design,position,new Vector2Int(Math.Max(1,design.TILE_WIDTH),Math.Max(1,design.TILE_HEIGHT)),false,
                    created=>{campfireObject=created;logger.LogInfo("Actual native CookingFire71 completed: "+(created==null?"absent":created.name));},null,false);
            }
            if(!campfireObserved)campfireObserved=ObserveCampfire();
            if(!stationObserved)stationObserved=ObserveConstructionStation();
            if(bedObserved && hutObserved && stationObserved && campfireObserved)Report("minecraft-construction-facade",true,null);
        }

        private bool ObserveConstructionStation()
        {
            if (stations == null) { lastError="Actual construction station manager not connected to self-test."; return false; }
            if (observedStation == null)
            {
                foreach (Interaction interaction in Interaction.interactions)
                {
                    Interaction_PlacementRegion native = interaction as Interaction_PlacementRegion;
                    if (native == null || !native.gameObject.scene.IsValid() || !native.isActiveAndEnabled || native.placementRegion == null || native.ActiveObject == null || stations.NativeVisual(native) == null) continue;
                    observedStation=native;
                    stationRegion=native.placementRegion;stationLock=native.LockPosition;
                    stationActivationDistance=native.ActivateDistance;stationActivatorOffset=native.ActivatorOffset;
                    stationColliders=native.GetComponentsInChildren<Collider>(true); stationColliderStates=new bool[stationColliders.Length];
                    stationColliders2D=native.GetComponentsInChildren<Collider2D>(true); stationColliderStates2D=new bool[stationColliders2D.Length];
                    for(int i=0;i<stationColliders.Length;i++)stationColliderStates[i]=stationColliders[i].enabled;
                    for(int i=0;i<stationColliders2D.Length;i++)stationColliderStates2D[i]=stationColliders2D[i].enabled;
                    stationCapturedFrame=Time.frameCount;
                    break;
                }
                return false;
            }
            if (Time.frameCount <= stationCapturedFrame+1 || !observedStation.isActiveAndEnabled || observedStation.placementRegion != stationRegion || observedStation.LockPosition != stationLock || observedStation.ActivateDistance != stationActivationDistance || observedStation.ActivatorOffset != stationActivatorOffset) return false;
            GameObject facade=stations.NativeVisual(observedStation);
            if(facade==null || !facade.activeInHierarchy || facade.transform.localToWorldMatrix.determinant<=0f || facade.GetComponentsInChildren<Collider>(true).Length!=0 || facade.GetComponentsInChildren<Collider2D>(true).Length!=0)return false;
            Collider[] now=observedStation.GetComponentsInChildren<Collider>(true);Collider2D[] now2D=observedStation.GetComponentsInChildren<Collider2D>(true);
            if(now.Length!=stationColliders.Length || now2D.Length!=stationColliders2D.Length)return false;
            for(int i=0;i<now.Length;i++)if(now[i]!=stationColliders[i] || now[i].enabled!=stationColliderStates[i])return false;
            for(int i=0;i<now2D.Length;i++)if(now2D[i]!=stationColliders2D[i] || now2D[i].enabled!=stationColliderStates2D[i])return false;
            bool top=false,side=false;
            foreach(MeshRenderer renderer in facade.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter=renderer.GetComponent<MeshFilter>();Material material=renderer.sharedMaterial;
                if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy || filter==null || filter.sharedMesh==null || filter.sharedMesh.vertexCount==0 || material==null || material.shader==null || !material.shader.isSupported)return false;
                top |= material.mainTexture==assets.Texture("crafting_table_top");
                side |= material.mainTexture==assets.Texture("crafting_table_side");
            }
            if(top&&side)logger.LogInfo("Actual construction station proof: native Interaction_PlacementRegion, serialized PlacementRegion/LockPosition/ActivateDistance/ActivatorOffset retained; actual workbench side/top mesh materials render, native collider identities/states unchanged across frames, no visual colliders. Native interaction range does not require a collider.");
            return top&&side;
        }

        private bool ObserveCampfire()
        {
            if(campfireObject==null || !campfireObject.activeInHierarchy)return false;
            if(campfireNative==null)
                foreach(Structure native in campfireObject.GetComponentsInChildren<Structure>(true))
                    if(native.Type==StructureBrain.TYPES.COOKING_FIRE && native.Brain!=null && native.Structure_Info!=null){campfireNative=native;break;}
            if(campfireNative==null || !gameplay.NativeStructurePreserved(campfireNative))return false;
            Transform facade=gameplay.NativeStructureVisual(campfireNative);
            if(facade==null || !facade.gameObject.activeInHierarchy || facade.localToWorldMatrix.determinant<=0f)return false;
            Interaction_Cauldron interaction=null;
            foreach(Interaction_Cauldron candidate in campfireNative.GetComponentsInChildren<Interaction_Cauldron>(true))
                if(candidate.structure==campfireNative && candidate.Collider!=null && candidate.Collider.enabled){interaction=candidate;break;}
            if(interaction==null){lastError="Actual native cooking fixture has no enabled serialized footprint collider.";return false;}
            Vector3 footprint=interaction.Collider.bounds.center;
            Interaction_Kitchen kitchen=interaction as Interaction_Kitchen;
            Vector3 expected=kitchen!=null?new Vector3(campfireNative.Structure_Info.Position.x+0.1f,(kitchen.CookingMealAnimation!=null?kitchen.CookingMealAnimation.transform.position.y:kitchen.transform.position.y)-Spec.Visual("campfire_work_offset").Value,campfireNative.Structure_Info.Position.z):new Vector3(footprint.x,footprint.y,campfireNative.Structure_Info.Position.z);
            if(Vector3.Distance(facade.position,expected)>0.03f){lastError="Campfire facade is offset from actual native cooking footprint: visual="+facade.position+", footprint="+expected;return false;}
            MinecraftTextureAnimator animation=facade.GetComponent<MinecraftTextureAnimator>();
            if(animation==null || !animation.enabled || animation.AnimatedMaterialCount!=2 || animation.AnimatedMeshCount!=2)return false;
            bool fire=false,lit=false,wood=false;
            foreach(MeshRenderer renderer in facade.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)continue;
                MeshFilter mesh=renderer.GetComponent<MeshFilter>();
                Material material=renderer.sharedMaterial;
                if(mesh==null || mesh.sharedMesh==null || mesh.sharedMesh.vertexCount<4 || material==null || material.shader==null || !material.shader.isSupported || renderer.transform.localToWorldMatrix.determinant<=0f || renderer.GetComponent<Collider>()!=null)return false;
                bool animated=material.mainTexture==assets.Texture("campfire_fire") || material.mainTexture==assets.Texture("campfire_log_lit");
                if(material.mainTexture==assets.Texture("campfire_fire"))fire=true;
                if(material.mainTexture==assets.Texture("campfire_log_lit"))lit=true;
                if(material.mainTexture==assets.Texture("campfire_log"))wood=true;
                if(!animated)continue;
                Vector2[] renderedUv=mesh.sharedMesh.uv;
                if(renderedUv.Length==0 || material.mainTextureScale!=Vector2.one || material.mainTextureOffset!=Vector2.zero)return false;
                float minV=Single.MaxValue,maxV=Single.MinValue;
                foreach(Vector2 point in renderedUv){minV=Math.Min(minV,point.y);maxV=Math.Max(maxV,point.y);}
                float frameRange=(float)material.mainTexture.width/material.mainTexture.height;
                if(maxV-minV>frameRange+0.0001f || minV<0f || maxV>1.0001f)return false;
                Vector2 baseline;
                if(!campfireUvBaseline.TryGetValue(mesh.sharedMesh,out baseline))
                {campfireUvBaseline.Add(mesh.sharedMesh,renderedUv[0]);campfireFirstFrame=Time.frameCount;}
                else if(Time.frameCount>campfireFirstFrame && (renderedUv[0]-baseline).sqrMagnitude>0.000001f)campfireAdvanced.Add(mesh.sharedMesh);
            }
            bool observed=wood && fire && lit && campfireUvBaseline.Count==2 && campfireAdvanced.Count==2;
            if(observed)logger.LogInfo("Actual native CookingFire71 proof: retained native colliders, facade="+facade.position+", actual footprint="+expected+", authentic log/fire textures, two rendered MESH UV strips each crop one atlas frame and advance over real frames; no shader ST dependency.");
            return observed;
        }

        private void CheckBillboards()
        {
            if (billboardObserved || !camera.Active || camera.ActiveCamera == null || camera.BillboardCount <= 0 || Time.unscaledTime < nextBillboardProbe) return;
            nextBillboardProbe = Time.unscaledTime + 0.25f;
            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(camera.ActiveCamera);
            const string prefix = "CultCraft.Billboard.";
            foreach (Renderer clone in renderers)
            {
                if (clone == null || !clone.name.StartsWith(prefix, StringComparison.Ordinal) || !clone.enabled || clone.forceRenderingOff || !clone.gameObject.activeInHierarchy || clone.sharedMaterial == null || clone.sharedMaterial.mainTexture == null || clone.sharedMaterial.shader == null || !clone.sharedMaterial.shader.isSupported || Vector3.Dot(clone.transform.up, Vector3.back) < 0.99f || Vector3.Angle(Vector3.ProjectOnPlane(clone.transform.forward, Vector3.forward), Vector3.ProjectOnPlane(camera.ActiveCamera.transform.forward, Vector3.forward)) > 1f || !GeometryUtility.TestPlanesAABB(frustum, clone.bounds)) continue;
                MeshFilter mesh = clone.GetComponent<MeshFilter>();
                SpriteRenderer sprite = clone as SpriteRenderer;
                if (!((mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.vertexCount > 0) || (sprite != null && sprite.sprite != null))) continue;
                string sourceName = clone.name.Substring(prefix.Length);
                foreach (Renderer source in renderers)
                    if (source != null && source != clone && source.name == sourceName && source.enabled && source.forceRenderingOff && source.gameObject.activeInHierarchy && source.sharedMaterial == clone.sharedMaterial)
                    {
                        billboardObserved = true;
                        logger.LogInfo("Observed actual visible aligned native billboard: " + clone.name);
                        return;
                    }
            }
        }

        private Health FindHostile(PlayerFarming player)
        {
            if (Health.team2 == null || player == null) return null;
            Health nearest = null;
            float distance = V("test_bow_target_range");
            foreach (Health target in Health.team2)
            {
                if (!UsableHostile(target)) continue;
                float next = Vector2.Distance(player.transform.position, target.transform.position);
                if (next < distance) { nearest = target; distance = next; }
            }
            return nearest;
        }

        private void PrepareNaturalCombat(PlayerFarming player)
        {
            if (player == null || player != gearOwner || !gameplay.IsDungeon) return;
            BiomeGenerator generator = BiomeGenerator.Instance;
            BiomeRoom room = generator == null ? null : generator.CurrentRoom;
            if (advancePending)
            {
                if (!MMTransition.IsPlaying && room != null && room != advanceFrom)
                {
                    logger.LogInfo("Native room transition observed: " + RoomIdentity(advanceFrom) + " -> " + RoomIdentity(room) + "; expected=" + RoomIdentity(advanceTo));
                    advancePending = false;
                    preparationRoom = null;
                }
                else if (Time.unscaledTime - advanceStarted > V("test_room_transition_timeout"))
                {
                    FailCombatPreparation("Native Door transition did not finish; from=" + RoomIdentity(advanceFrom) + ", to=" + RoomIdentity(advanceTo) + ", current=" + RoomIdentity(room));
                }
                return;
            }
            if (Time.unscaledTime - combatPreparationStarted > V("test_combat_preparation_timeout"))
            {
                FailCombatPreparation("Timed out waiting for a naturally populated native combat room; steps=" + combatSteps + ", current=" + RoomIdentity(room) + ", last=" + (lastError ?? "none"));
                return;
            }
            if (!Ready(player) || room == null || room.generateRoom == null || !room.generateRoom.GeneratedDecorations) return;
            if (preparationRoom != room)
            {
                preparationRoom = room;
                roomWaitStarted = Time.unscaledTime;
                attemptedRooms.Add(room);
                EnemyInfo("room:" + RoomIdentity(room), "Native combat preparation room=" + RoomIdentity(room) + ", active=" + room.Active + ", completed=" + room.Completed + ", real hostile list=" + Health.team2.Count);
            }
            if (!room.Active || Time.unscaledTime - roomWaitStarted < V("test_room_spawn_grace")) return;
            Health target = FindHostile(player);
            if (target != null)
            {
                camera.EndTestInput();
                AssignTarget(target, "naturally spawned native combat enemy ready");
                Next(9);
                return;
            }
            if (AnyNaturalHostile())
            {
                // A live enemy outside the initial range is approached through native
                // camera-relative movement. Spawn and invulnerability phases are awaited.
                Health distant = null;
                float nearest = float.MaxValue;
                foreach (Health candidate in Health.team2)
                {
                    if (!UsableHostile(candidate)) continue;
                    float distance = Vector2.Distance(player.transform.position, candidate.transform.position);
                    if (distance < nearest) { nearest = distance; distant = candidate; }
                }
                if (distant != null && nearest > V("test_bow_target_range"))
                {
                    if (!camera.FirstPerson) camera.Toggle();
                    if (camera.Active && camera.TestLookAt(distant.transform.position)) camera.BeginTestInput(Vector2.up);
                }
                else camera.EndTestInput();
                return;
            }
            camera.EndTestInput();
            if (!room.Active || Time.unscaledTime - roomWaitStarted < V("test_room_spawn_grace")) return;
            if (combatSteps >= (int)V("test_combat_room_steps"))
            {
                FailCombatPreparation("No genuine hostile was available after bounded native connected-room steps=" + combatSteps);
                return;
            }
            Door selected = null;
            BiomeRoom destination = null;
            foreach (Door door in Door.Doors)
            {
                if (door == null || !door.gameObject.activeInHierarchy || !door.enabled) continue;
                GenerateRoom owner = door.GetComponentInParent<GenerateRoom>();
                if (owner != null && owner != room.generateRoom) continue;
                if (door.RoomLockController != null && door.RoomLockController.gameObject.activeInHierarchy &&
                    (door.RoomLockController.ForcedLocked || !door.RoomLockController.Open)) continue;
                BiomeRoom next = null;
                if (door.ConnectionType == GenerateRoom.ConnectionTypes.DungeonFirstRoom) next = generator.RoomEntrance;
                else if (door.ConnectionType == GenerateRoom.ConnectionTypes.True ||
                    door.ConnectionType == GenerateRoom.ConnectionTypes.RelicShop ||
                    door.ConnectionType == GenerateRoom.ConnectionTypes.WeaponShop ||
                    door.ConnectionType == GenerateRoom.ConnectionTypes.Tarot ||
                    door.ConnectionType == GenerateRoom.ConnectionTypes.LoreStoneRoom)
                    // Door.OnTriggerEnter2D routes these through the same native
                    // connected-room transition. Random entrances can lead to a shop first.
                    next = DoorDestination(room, door.direction);
                else continue;
                if (next == null || next == room || attemptedRooms.Contains(next)) continue;
                if (selected == null || door.ConnectionType == GenerateRoom.ConnectionTypes.DungeonFirstRoom || next.Distance > destination.Distance)
                {
                    selected = door;
                    destination = next;
                }
                if (door.ConnectionType == GenerateRoom.ConnectionTypes.DungeonFirstRoom) break;
            }
            if (selected == null)
            {
                EnemyInfo("no-door:" + RoomIdentity(room), "No unlocked unvisited native connected-room door yet; room=" + RoomIdentity(room) + ", doors=" + Door.Doors.Count + ", RoomLockController.DoorsOpen=" + RoomLockController.DoorsOpen);
                LogEntryDoors(player, room, generator);
                PrepareEntryEquipment(player, room);
                return;
            }
            Collider2D playerCollider = NativePlayerCollider(player);
            if (playerCollider == null) { lastError = "Actual native player-owned Collider2D is unavailable for the connected-room doorway."; return; }
            MethodInfo trigger = AccessTools.Method(typeof(Door), "OnTriggerEnter2D", new[] { typeof(Collider2D) });
            if (trigger == null) throw new MissingMethodException("Door.OnTriggerEnter2D");
            advanceFrom = room;
            advanceTo = destination;
            advanceStarted = Time.unscaledTime;
            advancePending = true;
            combatSteps++;
            ReleaseBowInput();
            logger.LogInfo("Using actual native connected door=" + selected.name + ", type=" + selected.ConnectionType + ", direction=" + selected.direction + ", from=" + RoomIdentity(room) + ", to=" + RoomIdentity(destination) + "; no position/HP is assigned by test.");
            trigger.Invoke(selected, new object[] { playerCollider });
        }

        private void LogEntryDoors(PlayerFarming player, BiomeRoom room, BiomeGenerator generator)
        {
            string equipment = "weapon=" + player.currentWeapon + ", curse=" + player.currentCurse + ", spells enabled=" + DataManager.Instance.EnabledSpells +
                ", native room entrance=" + RoomIdentity(generator.RoomEntrance) + ", ready=" + Ready(player) + ", player state=" + player.state.CURRENT_STATE;
            EnemyInfo("entry-equipment:" + RoomIdentity(room) + equipment, "Native entry gate diagnostic: " + equipment);
            foreach (Door door in Door.Doors)
            {
                if (door == null) continue;
                GenerateRoom owner = door.GetComponentInParent<GenerateRoom>();
                RoomLockController nativeLock = door.RoomLockController;
                BiomeRoom next = door.ConnectionType == GenerateRoom.ConnectionTypes.DungeonFirstRoom ? generator.RoomEntrance : DoorDestination(room, door.direction);
                FieldInfo usedField = AccessTools.Field(typeof(Door), "Used");
                string state = "name=" + door.name + ", direction=" + door.direction + ", type=" + door.ConnectionType + ", active=" + door.gameObject.activeInHierarchy +
                    ", enabled=" + door.enabled + ", owner matches=" + (owner == null || owner == room.generateRoom) + ", used=" + (usedField != null && (bool)usedField.GetValue(door)) +
                    ", next=" + RoomIdentity(next) + ", attempted=" + (next != null && attemptedRooms.Contains(next)) +
                    ", lock=" + (nativeLock == null ? "absent" : "active:" + nativeLock.gameObject.activeInHierarchy + "/open:" + nativeLock.Open + "/forced:" + nativeLock.ForcedLocked +
                        "/blocker:" + (nativeLock.BlockingCollider != null && nativeLock.BlockingCollider.activeInHierarchy));
                EnemyInfo("entry-door:" + RoomIdentity(room) + state, "Native connected door diagnostic: " + state);
            }
        }

        private void PrepareEntryEquipment(PlayerFarming player, BiomeRoom room)
        {
            // Entrance podiums intentionally keep native doors closed until every
            // enabled equipment category is selected. Auto-equipped Minecraft sword
            // alone does not satisfy GetAllPlayersWearingWeapons when spells are on.
            // Invoke the live podium's normal interaction; its own GoTo, animations,
            // SetSpell/EquipRelic events and TryToOpenDoors own the entire gate.
            foreach (Interaction_WeaponSelectionPodium podium in Interaction_WeaponSelectionPodium.Podiums)
            {
                if (podium == null || !podium.enabled || !podium.gameObject.activeInHierarchy || !podium.Interactable ||
                    attemptedEntryPodiums.Contains(podium.GetInstanceID())) continue;
                GenerateRoom owner = podium.GetComponentInParent<GenerateRoom>();
                if (owner != null && owner != room.generateRoom) continue;
                bool needed = podium.Type == Interaction_WeaponSelectionPodium.Types.Weapon && player.currentWeapon == EquipmentType.None ||
                    podium.Type == Interaction_WeaponSelectionPodium.Types.Curse && player.currentCurse == EquipmentType.None && DataManager.Instance.EnabledSpells ||
                    podium.Type == Interaction_WeaponSelectionPodium.Types.Relic;
                FieldInfo activatedField = AccessTools.Field(typeof(Interaction_WeaponSelectionPodium), "activated");
                bool activated = activatedField != null && (bool)activatedField.GetValue(podium);
                if (!needed || activated || podium.WeaponTaken || (podium.Type != Interaction_WeaponSelectionPodium.Types.Relic && podium.TypeOfWeapon == EquipmentType.None)) continue;
                attemptedEntryPodiums.Add(podium.GetInstanceID());
                ReleaseBowInput();
                camera.EndTestInput();
                logger.LogInfo("Using actual native entry podium interaction: name=" + podium.name + ", type=" + podium.Type + ", equipment=" + podium.TypeOfWeapon +
                    ", weapon=" + player.currentWeapon + ", curse=" + player.currentCurse + "; native equip events must unlock doors normally.");
                podium.OnInteract(player.state);
                return;
            }
        }

        private void FailCombatPreparation(string reason)
        {
            lastError = reason;
            camera.EndTestInput();
            ReleaseBowInput();
            Report("minecraft-bow-hit", false, reason);
            Next(11);
        }

        private static BiomeRoom DoorDestination(BiomeRoom room, Door.Direction direction)
        {
            switch (direction)
            {
                case Door.Direction.North: return room.N_Room == null ? null : room.N_Room.Room;
                case Door.Direction.East: return room.E_Room == null ? null : room.E_Room.Room;
                case Door.Direction.South: return room.S_Room == null ? null : room.S_Room.Room;
                case Door.Direction.West: return room.W_Room == null ? null : room.W_Room.Room;
                default: return null;
            }
        }

        private static Collider2D NativePlayerCollider(PlayerFarming player)
        {
            if (player == null) return null;
            foreach (Collider2D collider in player.GetComponents<Collider2D>())
                if (collider != null && collider.enabled && collider.gameObject.GetComponent<PlayerFarming>() == player) return collider;
            return null;
        }

        private static bool UsableHostile(Health target)
        {
            if (target == null || !target.enabled || target.team != Health.Team.Team2 || !target.gameObject.activeInHierarchy ||
                target.CurrentHP <= 0f || target.invincible || target.untouchable || target.IgnoreProjectiles || !Health.team2.Contains(target)) return false;
            CircleCollider2D body = target.GetComponent<CircleCollider2D>();
            Rigidbody2D rigidbody = target.GetComponent<Rigidbody2D>();
            return body != null && body.enabled && (rigidbody == null || rigidbody.simulated);
        }

        private static bool AnyNaturalHostile()
        {
            foreach (Health health in Health.team2)
                if (health != null && health.enabled && health.gameObject.activeInHierarchy && health.CurrentHP > 0f) return true;
            return false;
        }

        private static string RoomIdentity(BiomeRoom room)
        {
            return room == null ? "absent" : room.x + "," + room.y + ":" + (room.GameObjectPath ?? "native room");
        }

        private void AssignTarget(Health target, string reason)
        {
            if (System.Object.ReferenceEquals(target, bowTarget)) return;
            bowTarget = target;
            targetRoom = BiomeGenerator.Instance == null ? null : BiomeGenerator.Instance.CurrentRoom;
            bowTargetId = target == null ? 0 : target.GetInstanceID();
            lastTargetState = null;
            targetLossReason = null;
            LogTarget(reason, true);
        }

        private void LogTarget(string reason, bool force)
        {
            Health target = bowTarget;
            if (target == null)
            {
                if (force) logger.LogInfo("Native bow target absent; reason=" + reason + ", previous id=" + bowTargetId + ", last valid=" + (lastValidTargetState ?? "none"));
                return;
            }
            CircleCollider2D collider = target.GetComponent<CircleCollider2D>();
            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            string state = "id=" + target.GetInstanceID() + ", name=" + target.name + ", active=" + target.gameObject.activeInHierarchy + ", enabled=" + target.enabled +
                ", HP=" + target.CurrentHP.ToString("F3", CultureInfo.InvariantCulture) + ", invincible=" + target.invincible + ", untouchable=" + target.untouchable +
                ", IgnoreProjectiles=" + target.IgnoreProjectiles + ", collider=" + (collider == null ? "absent" : "enabled:" + collider.enabled + "/trigger:" + collider.isTrigger) +
                ", layer=" + target.gameObject.layer + "/" + LayerMask.LayerToName(target.gameObject.layer) + ", simulated=" + (body != null && body.simulated) +
                ", room=" + RoomIdentity(targetRoom);
            if (!force && state == lastTargetState) return;
            lastTargetState = state;
            lastValidTargetState = state;
            logger.LogInfo("Native bow target (" + reason + "): " + state);
            if (force && collider != null)
            {
                Vector2 point = collider.bounds.center;
                bool defaultHit = false, allHit = false;
                foreach (Collider2D hit in Physics2D.OverlapCircleAll(point, 0.12f, Physics2D.DefaultRaycastLayers)) if (hit == collider) defaultHit = true;
                foreach (Collider2D hit in Physics2D.OverlapCircleAll(point, 0.12f, Physics2D.AllLayers)) if (hit == collider) allHit = true;
                logger.LogInfo("Native bow collision probe: queriesHitTriggers=" + Physics2D.queriesHitTriggers + ", body in default layers=" + defaultHit + ", body in AllLayers=" + allHit + ", center=" + point);
            }
        }

        private void NativeRoomLeft()
        {
            if (!Active) return;
            targetLossReason = "native OnBiomeLeftRoom; previous=" + RoomIdentity(BiomeGenerator.Instance == null ? null : BiomeGenerator.Instance.CurrentRoom);
            LogTarget(targetLossReason, true);
            ReleaseBowInput();
            camera.EndTestInput();
            bowTarget = null;
            targetRoom = null;
        }

        private void NativeRoomChanged()
        {
            if (!Active) return;
            roomWaitStarted = Time.unscaledTime;
            preparationRoom = null;
            logger.LogInfo("Native OnBiomeChangeRoom observed: " + RoomIdentity(BiomeGenerator.Instance == null ? null : BiomeGenerator.Instance.CurrentRoom));
        }

        private void EnemyInfo(string key, string message)
        {
            if (enemyDiagnostics.Add(key)) logger.LogInfo(message);
        }

        private bool ObserveFoodUse(PlayerFarming player)
        {
            if (player == null || player != gearOwner || player.health == null || player.health.CurrentHP <= 0f) return true;
            if (!foodAttempted)
            {
                foodAttempted = true;
                foodStarted = Time.unscaledTime;
                ReleaseBowInput();
                // The marked session owns fresh memory-only inventory; this supplies
                // one real native food item, never a private counter or personal save.
                Inventory.AddItem(InventoryItem.ITEM_TYPE.MEAT, 1, true);
                foodCountBefore = gameplay.FoodCount;
                foodUsesBefore = gameplay.FoodsEaten;
                MeltyPlay.Hold("3", true);
            }
            if (gameplay.FoodsEaten > foodUsesBefore && nativeFoodHealing > 0f && gameplay.FoodCount < foodCountBefore)
            {
                foodObserved = true;
                logger.LogInfo("Food observed: actual digit3/LMB, native HealthPlayer.Heal HP increase=" + nativeFoodHealing.ToString("F3", CultureInfo.InvariantCulture) + ", real native meat count=" + foodCountBefore + "->" + gameplay.FoodCount);
                ReleaseBowInput();
                return true;
            }
            if (Time.unscaledTime - foodStarted > V("test_food_timeout") || !gameplay.IsDungeon)
            {
                logger.LogInfo("Food runtime observation incomplete: natural health deficit=" + (player.health.PLAYER_HEALTH < player.health.PLAYER_TOTAL_HEALTH) + ", selected=" + gameplay.SelectedSlot + ", native healing=" + nativeFoodHealing + "; no player HP was assigned by the harness.");
                ReleaseBowInput();
                return true;
            }
            if (gameplay.SelectedSlot != 2)
            {
                MeltyPlay.Hold("3", true);
                MeltyPlay.Button(0, false);
            }
            else
            {
                MeltyPlay.Hold("3", false);
                MeltyPlay.Button(0, Ready(player) && player.health.PLAYER_HEALTH < player.health.PLAYER_TOTAL_HEALTH);
            }
            return false;
        }

        private void ReleaseBowInput()
        {
            if (!markerFound) return;
            MeltyPlay.Hold("r", false);
            MeltyPlay.Hold("2", false);
            MeltyPlay.Hold("3", false);
            MeltyPlay.Button(0, false);
        }

        private void RegisterActions()
        {
            MeltyPlay.Action("cultcraft_new_game", "Start the isolated native QuickStart world; no personal saves are loaded.", (args, seconds) => BeginNewGame());
            MeltyPlay.Action("cultcraft_enter_dungeon", "Use native Darkwood GetFloor/NewRun and scene transition in the isolated world.", (args, seconds) => EnterDungeon());
            MeltyPlay.Action("cultcraft_camera", "Set perspective: fps or topdown.", (args, seconds) => {
                bool desired = args.Trim().Equals("fps", StringComparison.OrdinalIgnoreCase);
                if (camera.FirstPerson != desired) camera.Toggle();
                return !desired || camera.Active;
            });
            MeltyPlay.Action("cultcraft_move", "Native first-person movement: x,y,duration; position is never assigned.", (args, seconds) => {
                string[] parts = args.Split(',');
                float x, y, duration;
                if (parts.Length != 3 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out duration)) return true;
                if (seconds >= Math.Max(0.05f, Math.Min(10f, duration))) { camera.EndTestInput(); return true; }
                if (!camera.FirstPerson) camera.Toggle();
                if (camera.Active) camera.BeginTestInput(new Vector2(x, y));
                return false;
            });
        }

        private void Next(int next)
        {
            if(next==7 && stage!=7)
            {
                camera.EndTestInput();
                ReleaseBowInput();
                foreach(string key in new[] { "w", "a", "s", "d", "up", "down", "left", "right", "space", "shift", "rshift", "e", "f", "b", "l" })MeltyPlay.Hold(key,false);
                MeltyPlay.Button(1,false);
                logger.LogInfo("Entered bounded native progression input window: virtual movement/attack released once; only this test player's native gameplay query results are held neutral until stage8.");
            }
            stage=next;stageSince=Time.unscaledTime;
        }
        private void Report(string name, bool passed, string why)
        {
            if (!reported.Add(name)) return;
            subcheckResults[name]=passed;
            string group=name,partner=null;
            if(name=="minecraft-death"||name=="first-person-camera-active")
            {group="first-person-camera-active";partner=name==group?"minecraft-death":group;}
            if(name=="minecraft-resource-visuals"||name=="minecraft-construction-facade")
            {group="minecraft-construction-facade";partner=name==group?"minecraft-resource-visuals":group;}
            if(name=="minecraft-enchantment-progression"||name=="minecraft-material-progression")
            {group="minecraft-material-progression";partner=name==group?"minecraft-enchantment-progression":group;}
            if(name=="minecraft-attack-cooldown"||name=="minecraft-weapon-equipped")
            {group="minecraft-weapon-equipped";partner=name==group?"minecraft-attack-cooldown":group;}
            if(name=="minecraft-broom"||name=="minecraft-avatar-active"||name=="minecraft-food"||name=="minecraft-fishing"||name=="minecraft-skins")
            {
                group="minecraft-avatar-active";
                if (passed && !(SubcheckPassed(group) && SubcheckPassed("minecraft-broom") && SubcheckPassed("minecraft-food") && SubcheckPassed("minecraft-fishing") && (skinProof==null||SubcheckPassed("minecraft-skins"))))
                {
                    logger.LogInfo("Self-test pass " + name);
                    return;
                }
            }
            bool other;
            if(partner==null||!passed||subcheckResults.TryGetValue(partner,out other)&&other)
                if(emittedGroups.Add(group))MeltyTest.Report(group,passed,why);
            logger.LogInfo("Self-test " + (passed ? "pass " : "fail ") + name + (why == null ? "" : ": " + why));
        }

        private bool SubcheckPassed(string name) { return subcheckResults.TryGetValue(name, out bool passed) && passed; }

        public void Dispose()
        {
            if (disposed) return;
            if(scenesProof!=null)scenesProof.Dispose();
            disposed = true;
            if (!markerFound) return;
            if(features!=null)features.Dispose();
            if (broomProof != null) broomProof.Dispose();
            if (fishingProof != null) fishingProof.Dispose();
            if(skinProof!=null)skinProof.Dispose();
            if (stoneWalk != null) stoneWalk.Dispose();
            if (foodProof != null) foodProof.Dispose();
            if(lootProof!=null)lootProof.Dispose();
            camera.EndTestInput();
            camera.AllowTestInput(false);
            ReleaseBowInput();
            MeltyPlay.Hold("b",false);
            MeltyPlay.Hold("l",false);
            RestoreGear();
            Camera.onPreCull -= ObserveCameraFrame;
            BiomeGenerator.OnBiomeLeftRoom -= NativeRoomLeft;
            BiomeGenerator.OnBiomeChangeRoom -= NativeRoomChanged;
            PlayerFarming.OnGetSoul -= DevotionCallbackObserved;
            if (activeTest == this) activeTest = null;
            // Save guards intentionally remain until process exit: native quit callbacks can save after plugin OnDestroy.
            // They exist only in this exact marked test process and must also protect late shutdown callbacks.
        }
    }
}
