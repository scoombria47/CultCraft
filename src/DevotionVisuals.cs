using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Changes only proven devotion-to-player visuals; native callbacks and rewards stay intact.</summary>
    public sealed class DevotionVisuals : IDisposable
    {
        private static DevotionVisuals current;
        private readonly Harmony harmony = new Harmony("cultcraft.devotion");
        private readonly ManualLogSource logger;
        private readonly MinecraftAssets assets;
        [ThreadStatic] private static int suppressNativePickup;
        [ThreadStatic] private static int quietExperienceSpawn;
        private readonly Dictionary<MethodInfo, bool> callbackKinds = new Dictionary<MethodInfo, bool>();
        private readonly List<DevotionOrbVisual> visuals = new List<DevotionOrbVisual>();
        private readonly Mesh orbMesh;
        private readonly Material orbMaterial;
        private bool disposed;
        public static DevotionVisuals Current { get { return current; } }
        public int ExperienceSoundsPlayed { get; private set; }
        private readonly System.Random soundRandom=new System.Random();
        private readonly HashSet<float> experiencePitches=new HashSet<float>();
        public int DistinctExperiencePitches { get { return experiencePitches.Count; } }
        public int SermonOrbs { get; private set; }
        public int Count { get; private set; }
        public int ReuseCount { get; private set; }
        public int VisibleCount { get { int count=0;foreach(DevotionOrbVisual visual in visuals)if(visual!=null&&visual.VisibleNow)count++;return count; } }
        public int FallbackCount { get { int count=0;foreach(DevotionOrbVisual visual in visuals)if(visual!=null)count+=visual.FallbackTransitions;return count; } }

        public DevotionVisuals(MinecraftAssets assets, ManualLogSource logger)
        {
            this.logger = logger;this.assets=assets;
            // A native effect material can keep drawing its original white glow despite a
            // changed Sprite. Render a separate atlas quad and hide only the native art.
            try
            {
                Texture2D texture=assets.Texture("experience_orb");
                if(texture==null||texture.width!=64||texture.height!=64)throw new InvalidOperationException("Installed experience_orb must be 64x64.");
                bool opaque=false;Color32[] rgba=texture.GetPixels32();
                // Source top-row tile 2 is the nonempty, 16x16 Minecraft XP glyph.
                for(int y=48;y<64;y++)for(int x=32;x<48;x++)if(rgba[y*64+x].a>26)opaque=true;
                if(!opaque)throw new InvalidOperationException("Installed experience_orb atlas tile is transparent.");
                Shader shader=null;
                foreach(string name in new[]{"Sprites/Default","Unlit/Transparent Cutout"})
                {
                    Shader candidate=Shader.Find(name);
                    if(candidate!=null&&candidate.isSupported){shader=candidate;break;}
                }
                if(shader==null)throw new InvalidOperationException("No supported Minecraft orb presentation shader is available.");
                orbMaterial=new Material(shader){name="CultCraft actual Minecraft XP atlas",mainTexture=assets.TintedTexture("experience_orb",new Color(0.4f,1f,0.12f,1f))};
                if(orbMaterial.HasProperty("_Color"))orbMaterial.SetColor("_Color",Color.white);
                if(orbMaterial.HasProperty("_RendererColor"))orbMaterial.SetColor("_RendererColor",Color.white);
                if(orbMaterial.HasProperty("_EnableExternalAlpha"))orbMaterial.SetFloat("_EnableExternalAlpha",0f);
                if(orbMaterial.HasProperty("_Cutoff"))orbMaterial.SetFloat("_Cutoff",PixelUi.V("orb_cutout"));
            }
            catch(Exception error){if(logger!=null)logger.LogWarning("Minecraft XP visual unavailable; native orbs retained: "+error.Message);}
            orbMesh=new Mesh{name="CultCraft actual Minecraft XP atlas glyph"};
            orbMesh.vertices=new[]{new Vector3(-0.5f,-0.5f,0f),new Vector3(0.5f,-0.5f,0f),new Vector3(0.5f,0.5f,0f),new Vector3(-0.5f,0.5f,0f)};
            orbMesh.uv=new[]{new Vector2(0.5f,0.75f),new Vector2(0.75f,0.75f),new Vector2(0.75f,1f),new Vector2(0.5f,1f)};
            orbMesh.triangles=new[]{0,1,2,0,2,3,0,2,1,0,3,2};orbMesh.RecalculateNormals();orbMesh.RecalculateBounds();
            orbMesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            current = this;
            try
            {
                MethodInfo init = AccessTools.Method(typeof(SoulCustomTarget), "Init", new[] { typeof(GameObject), typeof(Color), typeof(Action), typeof(Sprite), typeof(float), typeof(float), typeof(bool), typeof(bool) });
                MethodInfo lerp = AccessTools.Method(typeof(SoulCustomTargetLerp), "Init", new[] { typeof(GameObject), typeof(Vector3), typeof(Color), typeof(float), typeof(Action) });
                MethodInfo oldSoul = AccessTools.Method(typeof(Soul), "Start");
                if (init == null || lerp == null || oldSoul == null) throw new MissingMethodException("Native devotion initialization API changed.");
                harmony.Patch(init, prefix: new HarmonyMethod(typeof(DevotionVisuals), "InitPrefix"), postfix: new HarmonyMethod(typeof(DevotionVisuals), "InitPostfix"));
                harmony.Patch(lerp, prefix: new HarmonyMethod(typeof(DevotionVisuals), "LerpPrefix"), postfix: new HarmonyMethod(typeof(DevotionVisuals), "LerpPostfix"));
                harmony.Patch(oldSoul, postfix: new HarmonyMethod(typeof(DevotionVisuals), "SoulPostfix"));
                foreach(Type type in new[]{typeof(SoulCustomTarget),typeof(SoulCustomTargetLerp),typeof(Soul)})
                    harmony.Patch(AccessTools.Method(type,"CollectMe"),prefix:new HarmonyMethod(typeof(DevotionVisuals),"CollectPrefix"),finalizer:new HarmonyMethod(typeof(DevotionVisuals),"CollectFinalizer"));
                foreach(Type parameter in new[]{typeof(Vector3),typeof(GameObject)})
                    harmony.Patch(AccessTools.Method(typeof(AudioManager),"PlayOneShot",new[]{typeof(string),parameter}),prefix:new HarmonyMethod(typeof(DevotionVisuals),"PickupSoundPrefix"));
                foreach(MethodInfo method in typeof(SoulCustomTarget).GetMethods(BindingFlags.Public|BindingFlags.Static))
                    if(method.Name=="Create")foreach(ParameterInfo parameter in method.GetParameters())if(parameter.Name=="Callback"&&parameter.ParameterType==typeof(Action))
                    {harmony.Patch(method,prefix:new HarmonyMethod(typeof(DevotionVisuals),"SpawnPrefix"),finalizer:new HarmonyMethod(typeof(DevotionVisuals),"SpawnFinalizer"));break;}
                Camera.onPreCull+=BeforeCameraRender;
            }
            catch { Dispose(); throw; }
        }

        private static void InitPrefix(SoulCustomTarget __instance)
        {
            DevotionOrbVisual visual = __instance.GetComponent<DevotionOrbVisual>();
            if (visual != null) visual.Restore();
        }

        private static void InitPostfix(SoulCustomTarget __instance,GameObject Target, Action Callback)
        {
            if(current==null)return;
            bool sermon=current.IsSermonCallback(Callback)||IsTempleTarget(Target);
            if(current.IsDevotion(Callback)||sermon){current.Apply(__instance.gameObject,__instance.Image,__instance.Trail);if(sermon)current.SermonOrbs++;}
        }

        private static bool IsTempleTarget(GameObject target)
        {PlayerFarming p=PlayerFarming.Instance;return p!=null&&p.CrownBone!=null&&target==p.CrownBone.gameObject&&ChurchFollowerManager.Instance!=null&&ChurchFollowerManager.Instance.gameObject.activeInHierarchy&&BookAnimationBinding.IsReading(p);}

        private bool IsSermonCallback(Action callback)
        {
            if(callback==null)return false;
            foreach(Delegate action in callback.GetInvocationList())if(CallsSermonBar(action.Method))return true;
            return false;
        }
        public static bool CallsSermonBar(MethodInfo method)
        {
            if(method==null||method.DeclaringType==null||method.DeclaringType.Assembly!=typeof(PlayerFarming).Assembly)return false;
            if(method.DeclaringType==typeof(SermonController)&&(method.Name=="IncrementXPBar"||method.Name=="IncrementCustomXPBar"))return true;
            try{foreach(CodeInstruction instruction in PatchProcessor.GetCurrentInstructions(method))
                {MethodInfo call=instruction.operand as MethodInfo;if(call!=null&&call.DeclaringType==typeof(SermonController)&&(call.Name=="IncrementXPBar"||call.Name=="IncrementCustomXPBar"))return true;}}
            catch{}return false;
        }
        private static void CollectPrefix(Component __instance,out bool __state)
        {
            __state=false;if(current==null||__instance==null)return;
            DevotionOrbVisual visual=__instance.GetComponent<DevotionOrbVisual>();
            if(visual==null||!visual.IsBound)return;
            var settings=SettingsManager.Settings;
            float volume=settings!=null&&settings.Audio!=null?Spec.Visual("experience_pickup_volume").Value*Mathf.Clamp01(settings.Audio.MasterVolume)*Mathf.Clamp01(settings.Audio.SFXVolume):0f;
            // Minecraft's triangular pickup-pitch variation, independent of
            // Unity's gameplay RNG (so cosmetic audio never changes loot rolls).
            float pitch=0.9f+(float)(current.soundRandom.NextDouble()-current.soundRandom.NextDouble())*0.35f;
            if(current.assets.TryPlaySound("experience_pickup",volume,pitch))
            {__state=true;suppressNativePickup++;current.ExperienceSoundsPlayed++;current.experiencePitches.Add(current.assets.LastSoundPitch);}
        }
        private static Exception CollectFinalizer(Exception __exception,bool __state)
        {if(__state)suppressNativePickup=Math.Max(0,suppressNativePickup-1);return __exception;}
        private static void SpawnPrefix(object __0,Action Callback,out bool __state)
        {__state=current!=null&&(current.IsDevotion(Callback)||IsTempleTarget(__0 as GameObject))&&current.assets.PrepareSoundPlayback("experience_pickup")!=null;if(__state)quietExperienceSpawn++;}
        private static Exception SpawnFinalizer(Exception __exception,bool __state)
        {if(__state)quietExperienceSpawn=Math.Max(0,quietExperienceSpawn-1);return __exception;}
        private static bool PickupSoundPrefix(string __0)
        {return suppressNativePickup==0&&quietExperienceSpawn==0||!String.Equals(__0,"event:/player/collect_black_soul",StringComparison.OrdinalIgnoreCase);}

        private static void LerpPrefix(SoulCustomTargetLerp __instance)
        {
            DevotionOrbVisual visual = __instance.GetComponent<DevotionOrbVisual>();
            if (visual != null) visual.Restore();
        }

        private static void LerpPostfix(SoulCustomTargetLerp __instance, Action Callback)
        {
            if (current != null && current.IsDevotion(Callback)) current.Apply(__instance.gameObject, __instance.Image, __instance.Trail);
        }

        private static void SoulPostfix(Soul __instance)
        {
            // The installed Soul.CollectMe unconditionally calls PlayerFarming.GetSoul().
            // Black-soul/fervour resources use a different implementation and are not touched.
            if (current == null || __instance.Image == null) return;
            current.Apply(__instance.gameObject, __instance.Image.GetComponentInChildren<SpriteRenderer>(true), __instance.GetComponentInChildren<TrailRenderer>(true));
        }

        private bool IsDevotion(Action callback)
        {
            if (disposed || callback == null) return false;
            foreach (Delegate action in callback.GetInvocationList())
            {
                MethodInfo method = action.Method;
                bool devotion;
                if (!callbackKinds.TryGetValue(method, out devotion))
                {
                    devotion = CallsNativeDevotion(method);
                    callbackKinds.Add(method, devotion);
                }
                if (devotion) return true;
            }
            return false;
        }

        private bool CallsNativeDevotion(MethodInfo method)
        {
            if (method == null || method.DeclaringType == null || method.DeclaringType.Assembly != typeof(PlayerFarming).Assembly) return false;
            if (method.DeclaringType == typeof(PlayerFarming) && method.Name == "GetSoul") return true;
            if(CallsSermonBar(method))return true;
            try
            {
                // Native Shrine.GivePlayerSoul and follower-interaction generated callbacks
                // call GetSoul directly. Classify the actual call, not generic currency names,
                // colors, target objects or generated callback/compiler suffixes.
                foreach (CodeInstruction instruction in PatchProcessor.GetCurrentInstructions(method))
                {
                    MethodInfo called = instruction.operand as MethodInfo;
                    if (called != null && called.DeclaringType == typeof(PlayerFarming) && (called.Name == "GetSoul" || called.Name == "GetXP")) return true;
                }
            }
            catch (Exception error) { if (logger != null) logger.LogWarning("Devotion callback inspection: " + error.Message); }
            return false;
        }

        private void Apply(GameObject owner, SpriteRenderer image, TrailRenderer trail)
        {
            if (disposed || owner == null || image == null) return;
            DevotionOrbVisual visual = owner.GetComponent<DevotionOrbVisual>();
            if (visual == null)
            {
                visual = owner.AddComponent<DevotionOrbVisual>();
                visuals.Add(visual);
            }
            if(visual.ApplyCount>0)ReuseCount++;
            visual.Apply(image, trail, orbMesh, orbMaterial);
            Count++;
            if (Count % 64 == 0) visuals.RemoveAll(value => value == null);
        }

        private void BeforeCameraRender(Camera camera)
        {
            if(disposed||camera==null||camera.cameraType!=CameraType.Game)return;
            foreach(DevotionOrbVisual visual in visuals)if(visual!=null)visual.PrepareForCamera(camera);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Camera.onPreCull-=BeforeCameraRender;
            harmony.UnpatchSelf();
            if (current == this) current = null;
            foreach (DevotionOrbVisual visual in visuals)
                if (visual != null) { visual.Restore(); UnityEngine.Object.Destroy(visual); }
            visuals.Clear();
            callbackKinds.Clear();
            if (orbMesh != null) UnityEngine.Object.Destroy(orbMesh);
            if (orbMaterial != null) UnityEngine.Object.Destroy(orbMaterial);
        }
    }

    public sealed class DevotionOrbVisual : MonoBehaviour
    {
        private SpriteRenderer image;
        private readonly Dictionary<Renderer,bool> hidden=new Dictionary<Renderer,bool>();
        private GameObject display;
        private MeshRenderer displayRenderer;
        private MeshFilter displayFilter;
        private Mesh expectedMesh;
        private Material expectedMaterial;
        private Texture expectedTexture;
        private Camera lastGameCamera;
        private readonly List<Vector2> drawUv=new List<Vector2>(4);
        private readonly List<Color> drawColors=new List<Color>(4);
        private bool applied;
        private bool fallback;
        public bool IsBound { get { return applied; } }
        public int ApplyCount { get; private set; }
        public int FallbackTransitions { get; private set; }
        public int RenderedFrames { get; private set; }
        public int LastGameCameraFrame { get; private set; } = -1;
        public int LastGameCameraId { get; private set; }
        public int SuccessfulDrawFrames { get; private set; }
        public int SuccessfulGameDraws { get { return SuccessfulDrawFrames; } }
        public int LastSuccessfulGameCameraFrame { get; private set; } = -1;
        public int LastSuccessfulGameDrawFrame { get { return LastSuccessfulGameCameraFrame; } }
        public int LastSuccessfulGameCameraId { get; private set; }
        public int LastSuccessfulApplyCount { get; private set; }
        public bool LastSuccessfulNativeSuppressed { get; private set; }
        public bool LastSuccessfulAtlasValid { get; private set; }
        public string FallbackReason { get; private set; }
        public MeshRenderer AtlasRenderer { get { return displayRenderer; } }
        public bool NativeImageEnabled { get { return image!=null&&image.enabled; } }
        public bool NativeFallbackActive { get { return applied&&(fallback||!NativeArtSuppressed); } }
        public bool UsesMinecraftAtlas
        {
            get
            {
                return displayFilter!=null&&expectedMesh!=null&&displayFilter.sharedMesh==expectedMesh&&
                    expectedMaterial!=null&&displayRenderer!=null&&displayRenderer.sharedMaterial==expectedMaterial&&
                    expectedTexture!=null&&expectedMaterial.mainTexture==expectedTexture&&expectedTexture.width==64&&expectedTexture.height==64;
            }
        }
        public bool NativeArtSuppressed
        {
            get
            {
                if(!applied||fallback||hidden.Count==0)return false;
                foreach(var entry in hidden)if(entry.Key!=null&&!entry.Key.forceRenderingOff)return false;
                return true;
            }
        }
        public bool VisibleNow
        {
            get
            {
                return applied&&!fallback&&gameObject.activeInHierarchy&&UsesMinecraftAtlas&&displayRenderer.enabled&&
                    display.activeInHierarchy&&!displayRenderer.forceRenderingOff&&LastGameCameraFrame>=0&&LastGameCameraFrame>=Time.frameCount-2;
            }
        }

        public void Apply(SpriteRenderer renderer, TrailRenderer trailRenderer, Mesh mesh, Material material)
        {
            Restore();
            ApplyCount++;image=renderer;expectedMesh=mesh;expectedMaterial=material;expectedTexture=material==null?null:material.mainTexture;
            LastGameCameraFrame=-1;LastGameCameraId=0;
            LastSuccessfulGameCameraFrame=-1;LastSuccessfulGameCameraId=0;LastSuccessfulApplyCount=0;
            LastSuccessfulNativeSuppressed=false;LastSuccessfulAtlasValid=false;
            if(renderer!=null)hidden[renderer]=renderer.forceRenderingOff;
            if(trailRenderer!=null)hidden[trailRenderer]=trailRenderer.forceRenderingOff;
            if(display==null)
            {
                display=new GameObject("CultCraft Minecraft experience orb");
                // Sibling of the native image: follows the native pool owner's lifetime,
                // without inheriting a renderer/LOD flag from the native image object.
                display.transform.SetParent(transform,false);
                displayFilter=display.AddComponent<MeshFilter>();
                displayRenderer=display.AddComponent<MeshRenderer>();
                displayRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;displayRenderer.receiveShadows=false;
                display.AddComponent<DevotionOrbDrawProbe>().Owner=this;
            }
            displayFilter.sharedMesh=mesh;displayRenderer.sharedMaterial=material;
            applied=true;enabled=true;
            PrepareForCamera(PreferredCamera());
        }

        public void PrepareForCamera(Camera camera)
        {
            if(!applied)return;
            if(!gameObject.activeInHierarchy){ShowNativeArt();if(display!=null)display.SetActive(false);return;}
            if(camera==null)camera=lastGameCamera;
            if(camera==null&&LastGameCameraFrame>=0&&LastGameCameraFrame>=Time.frameCount-2)return;
            string failure=null;
            if(image==null||displayRenderer==null||displayFilter==null)failure="native image or atlas renderer unavailable";
            else if(!UsesMinecraftAtlas||expectedMaterial.shader==null||!expectedMaterial.shader.isSupported)failure="atlas mesh/material unavailable";
            // Camera.main may be null between native rendering passes, and a
            // disabled camera can legitimately be rendered manually by the game.
            // The last actual game-camera draw is stronger evidence than either flag.
            else if(camera==null)failure="gameplay rendering camera unavailable";
            Vector3 parentScale=transform.lossyScale;
            if(Mathf.Abs(parentScale.x)<0.0001f||Mathf.Abs(parentScale.y)<0.0001f||Mathf.Abs(parentScale.z)<0.0001f)failure="native owner scale is zero";
            if(failure!=null)
            {
                if(!fallback||FallbackReason!=failure)FallbackTransitions++;
                fallback=true;FallbackReason=failure;ShowNativeArt();
                if(display!=null)display.SetActive(false);
                return;
            }
            if((camera.cullingMask&(1<<image.gameObject.layer))==0)
            {
                // Some native FX layers use another gameplay camera. Let that
                // camera prepare the same atlas in its actual pre-cull pass.
                if(LastGameCameraFrame<0||LastGameCameraFrame<Time.frameCount-2)
                {
                    ShowNativeArt();display.SetActive(false);
                    FallbackReason="waiting for a gameplay camera that draws the native orb layer";
                }
                return;
            }
            fallback=false;FallbackReason=null;
            display.layer=image.gameObject.layer;
            displayRenderer.sortingLayerID=image.sortingLayerID;displayRenderer.sortingOrder=image.sortingOrder;
            display.transform.SetPositionAndRotation(image.transform.position,Quaternion.LookRotation(camera.transform.forward,camera.transform.up));
            float size=PixelUi.V("orb_world_size");
            display.transform.localScale=new Vector3(size/Mathf.Abs(parentScale.x),size/Mathf.Abs(parentScale.y),size/Mathf.Abs(parentScale.z));
            displayRenderer.enabled=true;displayRenderer.forceRenderingOff=false;display.SetActive(true);
            // Native Init does not set Image.enabled. In particular, pooled Lerp.Init
            // executes before SetActive(true). The owner lifetime is authoritative.
            // Suppress only the two replaced art components, after our draw is ready.
            if(LastGameCameraFrame>=0&&LastGameCameraFrame>=Time.frameCount-2)HideNativeArt();
            else {ShowNativeArt();FallbackReason="waiting for an actual game-camera atlas draw";}
        }

        private Camera PreferredCamera(){return lastGameCamera!=null?lastGameCamera:Camera.main;}
        private void LateUpdate(){PrepareForCamera(PreferredCamera());}
        private void OnEnable(){if(applied)PrepareForCamera(PreferredCamera());}
        private bool ValidAtlasDraw()
        {
            if(!UsesMinecraftAtlas||expectedMesh.vertexCount!=4||expectedMaterial.shader==null||!expectedMaterial.shader.isSupported)return false;
            expectedMesh.GetUVs(0,drawUv);expectedMesh.GetColors(drawColors);
            if(drawUv.Count!=4||drawColors.Count!=4)return false;
            foreach(Vector2 point in drawUv)if(point.x<0.5f||point.x>0.75f||point.y<0.75f||point.y>1f)return false;
            foreach(Color color in drawColors)if(color.r<0.99f||color.g<0.99f||color.b<0.99f||color.a<0.99f)return false;
            return true;
        }
        internal void RecordGameDraw(Camera camera)
        {
            if(!applied||camera==null||camera.cameraType!=CameraType.Game||!gameObject.activeInHierarchy||
                displayRenderer==null||!displayRenderer.enabled||displayRenderer.forceRenderingOff||display==null||!display.activeInHierarchy||
                (camera.cullingMask&(1<<display.layer))==0||!ValidAtlasDraw())return;
            lastGameCamera=camera;fallback=false;
            if(LastGameCameraFrame!=Time.frameCount)RenderedFrames++;
            LastGameCameraFrame=Time.frameCount;LastGameCameraId=camera.GetInstanceID();
            FallbackReason=null;HideNativeArt();
            if(!NativeArtSuppressed)return;
            // This snapshot belongs to this native Init lifetime and this real draw.
            // Update-time observers need not race a later camera's preparation pass.
            bool newFrame=LastSuccessfulGameCameraFrame!=Time.frameCount;
            LastSuccessfulGameCameraFrame=Time.frameCount;LastSuccessfulGameCameraId=camera.GetInstanceID();
            LastSuccessfulApplyCount=ApplyCount;LastSuccessfulNativeSuppressed=true;LastSuccessfulAtlasValid=true;
            if(newFrame)SuccessfulDrawFrames++;
        }
        private void HideNativeArt(){foreach(var entry in hidden)if(entry.Key!=null)entry.Key.forceRenderingOff=true;}
        private void ShowNativeArt(){foreach(var entry in hidden)if(entry.Key!=null)entry.Key.forceRenderingOff=entry.Value;}

        public void Restore()
        {
            ShowNativeArt();
            hidden.Clear();if(display!=null)display.SetActive(false);
            applied=false;fallback=false;FallbackReason=null;image=null;
            enabled=false;
        }
        private void OnDisable(){ShowNativeArt();if(display!=null)display.SetActive(false);}
        private void OnDestroy(){Restore();if(display!=null)UnityEngine.Object.Destroy(display);}
    }

    public sealed class DevotionOrbDrawProbe : MonoBehaviour
    {
        internal DevotionOrbVisual Owner;
        private void OnWillRenderObject(){if(Owner!=null)Owner.RecordGameDraw(Camera.current);}
    }
}
