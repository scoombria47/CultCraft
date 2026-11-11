using System;
using System.Collections;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CultCraft
{
    [BepInPlugin(Spec.Guid, Spec.Title, Spec.LoaderVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private MinecraftAssets assets;
        private CultLanguages languages;
        private MinecraftAvatar avatar;
        private PlayerSkins skins;
        private CinematicActors cinematicActors;
        private KnucklebonesHeads knucklebonesHeads;
        private readonly System.Collections.Generic.Dictionary<PlayerFarming,MinecraftAvatar> otherAvatars=new System.Collections.Generic.Dictionary<PlayerFarming,MinecraftAvatar>();
        private readonly System.Collections.Generic.List<PlayerFarming> removedAvatars=new System.Collections.Generic.List<PlayerFarming>();
        private CameraRig camera;
        private Gameplay gameplay;
        private SelfTest test;
        private DevotionVisuals devotion;
        private DevotionHud devotionHud;
        private WorldResourceVisuals worldResources;
        private StationVisuals stations;
        private SermonAudio sermonAudio;
        private AttackCooldownUi attackCooldown;
        private string startupError;
        private float nextError;
        private GUIStyle hud;
        private bool firstTick;
        private int lastBowShots;
        private float nextMetrics;
        private float measuredTime;
        private int measuredFrames;

        private void Awake()
        {
            gameObject.name = "CultCraft Runtime";
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            Logger.LogInfo("CultCraft Awake lifecycle: enabled=" + enabled + ", active=" + gameObject.activeInHierarchy);
            MeltyTest.Start();
            try
            {
                assets = new MinecraftAssets(Environment.GetEnvironmentVariable("CULTCRAFT_MINECRAFT"));
                if (!assets.Ready && assets.CanPreparePrimary)
                {
                    Logger.LogWarning(assets.Error + " Preparing verified Minecraft resource fallback.");
                    StartCoroutine(LoadPrimaryFallback());
                    return;
                }
                Initialize();
            }
            catch (Exception error) { FailStartup(error); }
        }

        private IEnumerator LoadPrimaryFallback()
        {
            yield return assets.PreparePrimary(Logger);
            try { Initialize(); }
            catch (Exception error) { FailStartup(error); }
        }

        private void Initialize()
        {
            if (!assets.Ready) throw new InvalidOperationException(assets.Error);
            languages = new CultLanguages(Logger);
            avatar = new MinecraftAvatar(assets);
            camera = new CameraRig(Logger, Config, assets);
            skins=new PlayerSkins(this,assets,Config,Logger);
            camera.SetSkins(skins);
            cinematicActors=new CinematicActors(assets,skins);
            knucklebonesHeads=new KnucklebonesHeads(assets,skins);
            camera.RenderPoseApplied += avatar.AlignViewCamera;
            gameplay = new Gameplay(assets, Logger);
            gameplay.SetCamera(camera);
            devotion = new DevotionVisuals(assets, Logger);
            devotionHud = new DevotionHud(assets);
            worldResources = new WorldResourceVisuals(assets, gameplay, Logger);
            stations = new StationVisuals(assets, Logger);
            sermonAudio = new SermonAudio(assets, Logger);
            attackCooldown = new AttackCooldownUi(assets, gameplay, camera, Logger);
            test = new SelfTest(assets, avatar, camera, gameplay, Logger, stations, attackCooldown);
            test.ConfigureSkinTest(this,skins,FindAvatar);
            test.ConfigureScenesTest(this,skins,cinematicActors,knucklebonesHeads);
            gameplay.AllowTestInput(test.Active);
            StartCoroutine(PrepareMinecraftSources());
            Logger.LogInfo("CultCraft " + Spec.Version + " loaded; official Minecraft resources " + assets.Version + "; solo and local co-op.");
        }

        private IEnumerator PrepareMinecraftSources()
        {
            yield return assets.PrepareOptionalSources(Logger);
            if (test != null && test.Active && Array.Exists(Spec.TestRows, row => row.Id == StartupCompatibilitySelfTest.Id))
                yield return StartupCompatibilitySelfTest.Run(assets, Logger);
        }

        private void FailStartup(Exception error)
        {
            startupError = error.Message;
            Logger.LogError(error);
            MeltyTest.Report("minecraft-assets-loaded", false, error.Message);
            DisposeParts();
        }

        private void Update()
        {
            if (!firstTick) { firstTick = true; Logger.LogInfo("CultCraft first Update; runtime component active and persistent."); }
            if (test == null) return;
            try
            {
                MeltyPlay.Tick();
                if (test.Active) languages.Test(Logger, assets);
                assets.TickSounds();
                cinematicActors.Tick();
                if (Application.isFocused && !test.Active && TogglePressed()) camera.Toggle();
                if ((Application.isFocused && !test.Active || test.SettingsHotkeyArmed) && SettingsPressed()) camera.ToggleSettings();
                PlayerFarming player = PlayerFarming.Instance;
                sermonAudio.Tick(player);
                camera.Tick(player);
                gameplay.Tick(player!=null&&cinematicActors.Owns(player.gameObject)?null:player);
                attackCooldown.Tick(player);
                worldResources.Tick();
                stations.Tick();
                avatar.SetUseProgress(gameplay.EatingProgress);
                if (gameplay.ShotsFired != lastBowShots) { lastBowShots=gameplay.ShotsFired; avatar.NotifyBowShot(); }
                test.Tick(player);
                measuredTime += Time.unscaledDeltaTime;
                measuredFrames++;
                if (Time.unscaledTime >= nextMetrics)
                {
                    nextMetrics=Time.unscaledTime+10f;
                    float fps=measuredTime>0f ? measuredFrames/measuredTime : 0f;
                    MeltyPlay.Report("cultcraft.performance.fps",fps);
                    MeltyPlay.Report("cultcraft.camera.dynamic_visible",camera.VisibleBillboardCount);
                    MeltyPlay.Report("cultcraft.camera.static_props",camera.StaticPropCount);
                    MeltyPlay.Report("cultcraft.camera.scene_discovery_passes",camera.DiscoveryPasses);
                    MeltyPlay.Report("cultcraft.devotion.orbs",devotion == null ? 0 : devotion.Count);
                    MeltyPlay.Report("cultcraft.resources.world",worldResources.Count);
                    MeltyPlay.Report("cultcraft.audio.channels_started",assets.SoundsPlayed);
                    if (test.Active) Logger.LogInfo("Runtime metrics: FPS="+fps.ToString("F1")+", FPS view="+camera.Active+", dynamic="+camera.VisibleBillboardCount+", fixed props="+camera.StaticPropCount+", discovery passes="+camera.DiscoveryPasses+", sounds="+assets.SoundStatus);
                    measuredTime=0f;measuredFrames=0;
                }
                if (player != null)
                {
                    MeltyPlay.Report("player.x", player.transform.position.x);
                    MeltyPlay.Report("player.y", player.transform.position.y);
                    MeltyPlay.Report("camera.first_person", camera.Active);
                    MeltyPlay.Report("weapon.tier", gameplay.Tier);
                    MeltyPlay.Report("world.dungeon", gameplay.IsDungeon);
                }
            }
            catch (Exception error)
            {
                if (Time.unscaledTime >= nextError)
                {
                    Logger.LogError(error);
                    nextError = Time.unscaledTime + 10f;
                }
            }
        }

        private void LateUpdate()
        {
            if (startupError != null || camera == null) return;
            try
            {
                PlayerFarming player = PlayerFarming.Instance;
                camera.LateTick(player);
                removedAvatars.Clear();
                foreach(var entry in otherAvatars)
                    if(entry.Key==null||entry.Key==player||!PlayerFarming.players.Contains(entry.Key)||cinematicActors.Owns(entry.Key.gameObject)){entry.Value.Dispose();removedAvatars.Add(entry.Key);}
                foreach(var key in removedAvatars)otherAvatars.Remove(key);
                avatar.SetEnchantment(gameplay.DisplayedEnchantmentLevel);
                var choice=skins.Get(PlayerSkins.Role(player));
                avatar.SetSkin(choice.Texture,choice.Slim);
                test?.PrepareAnimationAudit(player);
                avatar.Tick(player!=null&&cinematicActors.Owns(player.gameObject)?null:player, camera.Active, gameplay.SelectedItemId);
                foreach(PlayerFarming other in PlayerFarming.players)
                {
                    if(other==null||other==player||cinematicActors.Owns(other.gameObject))continue;
                    MinecraftAvatar model;
                    if(!otherAvatars.TryGetValue(other,out model)){model=new MinecraftAvatar(assets);otherAvatars.Add(other,model);Logger.LogInfo("Minecraft co-op avatar created: native id="+other.GetInstanceID()+", role="+PlayerSkins.Role(other)+", active="+other.gameObject.activeInHierarchy);}
                    var skin=skins.Get(PlayerSkins.Role(other));model.SetSkin(skin.Texture,skin.Slim);
                    string item=OtherWeapon(other);
                    model.Tick(other,false,item);
                }
                worldResources.LateTick();
                stations.LateTick();
                cinematicActors.LateTick();
                knucklebonesHeads.LateTick();
            }
            catch (Exception error)
            {
                if (Time.unscaledTime >= nextError) { Logger.LogError(error); nextError = Time.unscaledTime + 10f; }
            }
        }

        private static bool TogglePressed()
        {
            try { if (Input.GetKeyDown(KeyCode.F5)) return true; } catch (InvalidOperationException) { }
            return Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame;
        }
        private static string OtherWeapon(PlayerFarming player)
        {
            return Gameplay.PlayerWeaponTexture(player);
        }
        private MinecraftAvatar FindAvatar(PlayerFarming player)
        {
            if(avatar.Owner==player)return avatar;
            MinecraftAvatar model;if(otherAvatars.TryGetValue(player,out model))return model;
            Logger.LogInfo("Co-op avatar lookup absent: target="+player.GetInstanceID()+", players="+PlayerFarming.players.Count+", active="+player.gameObject.activeInHierarchy+", main="+(PlayerFarming.Instance==null?0:PlayerFarming.Instance.GetInstanceID())+", primary bound="+(avatar.Owner==null?0:avatar.Owner.GetInstanceID())+", other count="+otherAvatars.Count);
            return null;
        }

        private static bool SettingsPressed()
        {
            try { if (Input.GetKeyDown(KeyCode.F6)) return true; } catch (InvalidOperationException) { }
            return Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame;
        }

        private void OnGUI()
        {
            if (hud == null)
            {
                hud = new GUIStyle(GUI.skin.label);
                hud.fontSize = 15;
                hud.normal.textColor = Color.white;
                hud.wordWrap = true;
            }
            if (startupError != null)
            {
                GUI.Box(new Rect(18, 18, 600, 100), "");
                GUI.Label(new Rect(30, 28, 575, 80), "CultCraft could not start. Check that Minecraft: Java Edition has been launched and that the resource download can reach Mojang. See BepInEx/LogOutput.log for the cause.", hud);
                return;
            }
            if (assets != null && assets.CanPreparePrimary)
            {
                GUI.Box(new Rect(18, 18, 600, 56), "");
                GUI.Label(new Rect(30, 28, 575, 42), "CultCraft is preparing Minecraft resources (" + assets.PrimaryStatus + ").", hud);
                return;
            }
            if (devotionHud != null) devotionHud.Draw(PlayerFarming.Instance);
            if (PlayerFarming.Instance == null || !PlayerFarming.Instance.gameObject.activeInHierarchy) return;
            if (gameplay != null) gameplay.DrawHotbar();
            test?.DrawAnimationAuditLabel();
            if (attackCooldown != null) attackCooldown.Draw(PlayerFarming.Instance);
            if (camera != null) camera.DrawSettings();
            if (camera != null && camera.Active && !camera.SettingsOpen)
            {
                GUI.Label(new Rect(Screen.width / 2f - 5, Screen.height / 2f - 12, 20, 24), "+", hud);
            }
        }

        private void DisposeParts()
        {
            if (languages != null) { languages.Dispose(); languages = null; }
            if (test != null) { test.Dispose(); test = null; }
            if(knucklebonesHeads!=null){knucklebonesHeads.Dispose();knucklebonesHeads=null;}
            if(cinematicActors!=null){cinematicActors.Dispose();cinematicActors=null;}
            if (attackCooldown != null) { attackCooldown.Dispose(); attackCooldown = null; }
            if (sermonAudio != null) { sermonAudio.Dispose(); sermonAudio = null; }
            if (stations != null) { stations.Dispose(); stations = null; }
            if (worldResources != null) { worldResources.Dispose(); worldResources = null; }
            if (devotionHud != null) { devotionHud.Dispose(); devotionHud = null; }
            if (devotion != null) { devotion.Dispose(); devotion = null; }
            if (camera != null) { camera.Dispose(); camera = null; }
            if (gameplay != null) { gameplay.Dispose(); gameplay = null; }
            if (avatar != null) { avatar.Dispose(); avatar = null; }
            foreach(var model in otherAvatars.Values)model.Dispose();otherAvatars.Clear();
            if(skins!=null){skins.Dispose();skins=null;}
            if (assets != null) { assets.Dispose(); assets = null; }
        }

        private void Start() { Logger.LogInfo("CultCraft Start; component enabled=" + enabled); }
        private void OnEnable() { Logger.LogInfo("CultCraft runtime OnEnable."); }
        private void OnDisable() { Logger.LogInfo("CultCraft runtime OnDisable."); }
        private void OnDestroy() { Logger.LogInfo("CultCraft runtime OnDestroy."); DisposeParts(); }
    }
}
