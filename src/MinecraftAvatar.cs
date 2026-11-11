using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace CultCraft
{
    /// <summary>A real six-part cuboid avatar using the locally installed Steve skin.</summary>
    public sealed class MinecraftAvatar : IDisposable
    {
        public static float PixelSize { get { return V("pixel_size"); } }
        public static float BodyHeight { get { return V("body_pixels") * PixelSize; } }
        public bool BookArmsHaveDepth { get { return rightArm!=null&&leftArm!=null&&(rightArm.localRotation*Vector3.down).z>0.25f&&(leftArm.localRotation*Vector3.down).z>0.25f; } }
        public static float EyeHeight { get { return V("eye_pixels") * PixelSize; } }
        private static float V(string id) { return Spec.Visual(id).Value; }

        private readonly MinecraftAssets assets;
        private Texture2D selectedSkin, hurtSkin;
        internal static readonly Color DamageTintColor=new Color(1f,0.25f,0.25f,1f);
        private bool slimArms;
        internal bool SlimArms { get { return slimArms; } }
        internal Texture2D SkinTexture { get { return selectedSkin ?? assets.PlayerSkin; } }
        internal float ArmMeshWidth
        {
            get
            {
                if(rightArm==null)return 0f;
                // The first six quads are the solid arm; transparent sleeves
                // deliberately emit no geometry and must not affect model width.
                Vector3[] vertices=rightArm.GetComponent<MeshFilter>().sharedMesh.vertices;
                float min=Single.PositiveInfinity,max=Single.NegativeInfinity;
                for(int i=0;i<Math.Min(24,vertices.Length);i++){min=Mathf.Min(min,vertices[i].x);max=Mathf.Max(max,vertices[i].x);}
                return (max-min)/PixelSize;
            }
        }
        internal static float PalmX(Transform arm,bool right)
        {var filter=arm.GetComponent<MeshFilter>();return filter!=null&&filter.sharedMesh!=null?filter.sharedMesh.bounds.center.x/PixelSize:right?1f:-1f;}
        internal int SkinRenderFrames { get; private set; }
        internal void SetSkin(Texture2D texture, bool slim)
        {
            if(texture==null||(selectedSkin==texture&&slimArms==slim))return;
            selectedSkin=texture;slimArms=slim;
            if(hurtSkin!=null)UnityEngine.Object.Destroy(hurtSkin);
            hurtSkin=new Texture2D(64,64,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
            Color[] colors=texture.GetPixels();for(int i=0;i<colors.Length;i++)colors[i]*=DamageTintColor;
            hurtSkin.SetPixels(colors);hurtSkin.Apply();
            string[] ids={"torso","head","right_arm","left_arm","right_leg","left_leg"};
            for(int i=0;i<bodyPartRenderers.Count;i++)
            {
                var filter=bodyPartRenderers[i].GetComponent<MeshFilter>();Mesh old=filter.sharedMesh;
                filter.sharedMesh=BuildBodyMesh(PartRow(ids[i]));meshes.Remove(old);UnityEngine.Object.Destroy(old);
            }
            if(skinMaterial!=null)skinMaterial.mainTexture=SkinTexture;
            SkinRenderFrames=0;
        }
        private readonly AvatarFeedback feedback;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> bodyPartRenderers = new List<MeshRenderer>();
        private readonly Dictionary<string, Mesh> itemMeshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        private readonly Dictionary<string, Material> itemMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
        private GameObject worldObject;
        private GameObject viewObject;
        private Transform body;
        private Transform head;
        private Transform rightArm;
        private Transform leftArm;
        private Transform rightLeg;
        private Transform leftLeg;
        private Transform heldItem;
        private Transform viewArm;
        private Transform viewItem;
        private Vector3 viewItemBaseScale=Vector3.one;
        private Quaternion viewDisplayRotation=Quaternion.identity;
        private Vector3 viewDisplayTranslation;
        private BookVisual worldBook;
        private BookVisual firstPersonBook;
        private readonly BookAnimationBinding bookBinding;
        private readonly FishingAnimationBinding fishingBinding = new FishingAnimationBinding();
        private readonly FishingVisuals fishingVisuals;
        private readonly CarriedFollowerVisual carriedFollower = new CarriedFollowerVisual();
        internal int CarriedFollowerDraws { get { return carriedFollower.Draws; } }
        internal bool CarryHandsRaised { get; private set; }
        internal int FishingDraws { get; private set; }
        internal int FishingRevision { get { return fishingBinding.Revision; } }
        internal int FishingBobberDraws { get { return fishingVisuals.BobberDraws; } }
        internal int FishingLineDraws { get { return fishingVisuals.LineDraws; } }
        internal bool FishingNativeArtHidden { get { return fishingVisuals.NativeArtHidden; } }
        internal float FishingGripError { get { return fishingBinding.GripError; } }
        internal float FishingLineTipError { get { return fishingVisuals.LineTipError; } }
        private MeshFilter heldFilter;
        private MeshFilter viewFilter;
        private MeshRenderer heldRenderer;
        private MeshRenderer viewRenderer;
        private MeshRenderer heldGlintRenderer;
        private MeshRenderer viewGlintRenderer;
        private MeshFilter heldGlintFilter;
        private MeshFilter viewGlintFilter;
        private Material glintMaterial;
        private int enchantmentLevel;
        private Renderer lambRenderer;
        private bool lambWasEnabled;
        private PlayerFarming player;
        internal PlayerFarming Owner { get { return player; } }
        private Vector3 lastPosition;
        private float walkPhase;
        private float swingTime;
        private bool wasAttacking;
        private StateMachine.State previousState;
        private string itemId;
        private float bowShotUntil;
        private float useProgress;
        private int observedDeath;
        private bool dying;
        private float deathTime,lastFacing;
        private Vector3 deathOrigin;
        private struct CeremonyFrame
        {
            public Vector3 Offset,RightShoulder,LeftShoulder;
            public Quaternion Body,Head,RightArm,LeftArm,RightLeg,LeftLeg;
        }
        private string ceremonyClip;
        private float crouchWeight;
        private bool sleepBedSearched;
        private Structure sleepBed;
        private BuildingRow sleepBedDesign;
        private float sleepEntrySide;
        private bool wakeExitSearched, wakeExitFound;
        private readonly Collider2D[] wakeObstacles=new Collider2D[24];
        internal bool SleepOnBed {get;private set;}
        internal float CrouchWeight=>crouchWeight;
        internal float FootGroundError=>Mathf.Max(Mathf.Abs(SoleHeight(rightLeg)),Mathf.Abs(SoleHeight(leftLeg)));
        private float ceremonyMix;
        private CeremonyFrame ceremonyFrom,ceremonyDrawn;
        internal float DeathRoll { get; private set; }
        internal bool DeathTint { get { return dying&&skinMaterial!=null&&skinMaterial.mainTexture!=SkinTexture; } }
        internal int DeathDrawFrames { get; private set; }
        public int ConfirmedDamageReactions { get { return feedback.ConfirmedHits; } }
        public string AnimationPose { get; private set; }
        internal void SampleAuditPose(PlayerFarming owner)
        {
            bookBinding.Bind(owner);
            fishingBinding.Bind(owner);
            bookBinding.Capture();
            fishingBinding.Capture(owner.Spine);
        }

        internal void ReadAuditPose(Quaternion[] rotations)
        {
            rotations[0]=Root.rotation;
            rotations[1]=body.localRotation;
            rotations[2]=head.localRotation;
            rotations[3]=rightArm.localRotation;
            rotations[4]=leftArm.localRotation;
            rotations[5]=rightLeg.localRotation;
            rotations[6]=leftLeg.localRotation;
        }
        public bool BookVisible { get { return worldBook != null && (worldBook.Visible || firstPersonBook.Visible); } }
        public bool BookNativeBound { get { return bookBinding.Visible && BookVisible; } }
        public bool BookPoseDriven { get { return bookBinding.PoseDriven; } }
        public bool BookPoseActive { get { return bookBinding.PoseActive; } }
        public int AnimationPageDraws {get{return bookBinding.PageGameDrawFrames;}}
        public bool AnimationPageThrown {get{return bookBinding.PageThrown;}}
        public bool AnimationCardsDrawn {get{return bookBinding.CardsDrawn;}}
        public bool AnimationCardsHorizontal {get{return bookBinding.CardsHorizontal;}}
        public bool AnimationPageBookOpen {get{return bookBinding.PageBookReceptionValid;}}
        public int WeaponPresentationDraws {get;private set;}
        internal int BroomDraws { get; private set; }
        internal int HeldRenderFrames {get;private set;}
        private int lastWeaponPresentationFrame=-1;
        public bool AnimationGrounded {get{return bookBinding.Grounded;}}
        public float BookRightReachError {get{return bookBinding.RightReachError;}}
        public string BookNativeAttachment { get { return bookBinding.AttachmentName; } }
        public string BookNativeAnimation { get { return bookBinding.AnimationName; } }
        public int BookNativePoseRevision { get { return bookBinding.PoseRevision; } }
        public float BookOpenness { get { return bookBinding.Openness; } }
        public int MinecraftBookPartCount { get { return worldBook == null ? 0 : worldBook.PartCount; } }
        public bool BookVolumetricPages { get { return worldBook != null && worldBook.VolumetricPages; } }
        public int BookGameDrawFrames { get { return worldBook == null ? 0 : worldBook.GameDrawFrames + firstPersonBook.GameDrawFrames; } }
        public int BookIncomingGlyphGameDrawFrames { get { return worldBook==null?0:worldBook.ParticleGameDrawFrames+firstPersonBook.ParticleGameDrawFrames; } }
        public Transform WorldBookRoot { get { return worldBook == null ? null : worldBook.Root; } }
        public int ReplacedNativeGlyphCount { get { return bookBinding.ReplacedGlyphCount; } }
        public int GlyphGameDrawFrames { get { return bookBinding.GlyphGameDrawFrames; } }
        public int GlyphOpaquePixels { get { return bookBinding.GlyphOpaquePixels; } }
        public bool BookNonGlyphSlotsPreserved { get { return bookBinding.NativeGlyphsPreserved; } }
        public bool OpaqueDepthMaterial
        {
            get
            {
                return skinMaterial != null && skinMaterial.shader != null && skinMaterial.shader.isSupported &&
                    (skinMaterial.shader.name == "Unlit/Texture" || skinMaterial.shader.name == "Unlit/Transparent Cutout") &&
                    worldObject != null && worldObject.transform.localToWorldMatrix.determinant > 0f;
            }
        }
        public void NotifyBowShot() { bowShotUntil = Time.time + V("bow_release_duration"); }
        public void AlignViewCamera(Camera camera)
        {
            if (disposed || camera == null || viewObject == null || !viewObject.activeInHierarchy) return;
            // Native camera effects can run after LateUpdate. Match the final
            // render pose too, without advancing animation or parenting to a scene camera.
            viewObject.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            float projectionScale=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f)/Mathf.Tan(V("view_item_reference_fov")*Mathf.Deg2Rad*0.5f);
            viewObject.transform.localScale = new Vector3(projectionScale,projectionScale,1f);
        }
        public void SetUseProgress(float value) { useProgress = Mathf.Clamp01(value); }
        private Material skinMaterial;
        private bool disposed;
        private readonly CinematicPose cinematicPose=new CinematicPose();
        internal void TickCinematic(GameObject actor,Spine.Unity.SkeletonAnimation source,StateMachine state,bool usePentagramPose)
        {
            if(disposed||actor==null||source==null||!assets.Ready)return;
            if(worldObject==null)Create(null);
            Renderer native=source.GetComponent<Renderer>();
            if(lambRenderer!=native){RestoreLamb();lambRenderer=native;if(native!=null)lambWasEnabled=native.enabled;}
            bool visible=actor.activeInHierarchy&&source.gameObject.activeInHierarchy;
            worldObject.SetActive(visible);viewObject.SetActive(false);if(!visible)return;
            if(native!=null)native.enabled=false;
            SetDamageTint(false);dying=false;
            ResetPartPositions();body.localRotation=head.localRotation=Quaternion.identity;
            rightArm.localRotation=leftArm.localRotation=rightLeg.localRotation=leftLeg.localRotation=Quaternion.identity;
            float facing=state==null?85f:state.facingAngle;
            worldObject.transform.SetPositionAndRotation(actor.transform.position,Quaternion.AngleAxis(facing+V("ground_yaw_offset"),Vector3.forward)*Quaternion.Euler(V("ground_pitch"),0f,0f));
            worldObject.transform.localScale=Vector3.one;
            if(usePentagramPose)
            {
                // The encounter's native track supplies timing for the existing CultCraft warp.
                var track=source.AnimationState?.GetCurrent(0);
                string clip=track?.Animation?.Name??"";
                bool warpingIn=clip.StartsWith("warp-in",StringComparison.Ordinal);
                bool warpingOut=clip.StartsWith("warp-out",StringComparison.Ordinal);
                if(warpingIn||warpingOut)
                {
                    float phase=track.TrackTime/Mathf.Max(0.001f,track.Animation.Duration);
                    if(ApplyWarpPose(warpingIn,warpingOut,phase)<0.015f)worldObject.SetActive(false);
                }
                else
                {
                    AnimationPose="walk/idle";
                    rightArm.localRotation=Quaternion.Euler(0f,0f,V("arm_rest_roll"));
                    leftArm.localRotation=Quaternion.Euler(0f,0f,-V("arm_rest_roll"));
                    ApplyIdleHead(0f);
                }
            }
            else
            {
                cinematicPose.Bind(source);
                cinematicPose.Apply(worldObject.transform,body,head,rightArm,leftArm,rightLeg,leftLeg);
                KeepAboveGround(actor.transform.position.z);
                AnimationPose="cinematic/"+cinematicPose.Clip;
            }
            SetWeapon("");worldBook.Root.gameObject.SetActive(false);firstPersonBook.Root.gameObject.SetActive(false);
            foreach(var part in bodyPartRenderers){part.enabled=true;if(native!=null){part.sortingLayerID=native.sortingLayerID;part.sortingOrder=native.sortingOrder;}}
        }

        private void ApplyIdleHead(float yaw)
        {
            float pitch=Mathf.Sin(Time.time*V("head_idle_frequency"))*V("head_idle_degrees");
            head.localRotation=Quaternion.Euler(pitch,yaw,0f);
        }

        private float ApplyWarpPose(bool warpingIn,bool warpingOut,float phase)
        {
            AnimationPose=warpingIn?"warp-in":"warp-out";
            rightArm.localRotation=Quaternion.Euler(-45f,0f,110f);
            leftArm.localRotation=Quaternion.Euler(-45f,0f,-110f);
            head.localRotation=Quaternion.Euler(-20f,0f,0f);
            float reveal=warpingOut?1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(V("warp_out_hide_start"),V("warp_out_hide_end"),phase)):
                warpingIn?Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(V("warp_in_show_start"),V("warp_in_show_end"),phase)):1f;
            worldObject.transform.localScale*=Mathf.Max(0.001f,reveal);
            return reveal;
        }

        public Transform Root { get { return worldObject != null ? worldObject.transform : null; } }
        public bool LambHidden { get { return lambRenderer != null && !lambRenderer.enabled; } }
        public string HeldItemId { get { return !disposed && (IsRendering(heldRenderer) || IsRendering(viewRenderer)) ? itemId : String.Empty; } }
        public bool GlintActive { get { return !disposed && enchantmentLevel > 0 && (IsRendering(heldGlintRenderer) || IsRendering(viewGlintRenderer)); } }
        public bool WorldVisible { get { return IsBodyVisibleBelow(worldObject); } }
        public bool ViewVisible { get { return !disposed && viewObject!=null && viewObject.activeInHierarchy && IsRendering(viewRenderer); } }
        public bool ViewArmHidden { get { return viewArm!=null && !IsRendering(viewArm.GetComponent<MeshRenderer>()); } }
        public bool ViewItemOnRight
        {
            get
            {
                Camera camera=Camera.main;if(!ViewVisible||camera==null)return false;
                Vector3 projected=camera.WorldToViewportPoint(viewRenderer.bounds.center);
                return projected.z>0f && projected.x>0.52f && projected.x<0.98f;
            }
        }
        public int ActiveRendererCount
        {
            get
            {
                if (disposed) return 0;
                int count = 0;
                foreach (MeshRenderer renderer in renderers) if (IsRendering(renderer)) count++;
                return count;
            }
        }

        private bool IsBodyVisibleBelow(GameObject root)
        {
            if (disposed || root == null || !root.activeInHierarchy) return false;
            foreach (MeshRenderer renderer in bodyPartRenderers)
                if (IsRendering(renderer) && renderer.transform.IsChildOf(root.transform)) return true;
            return false;
        }

        private static bool IsRendering(MeshRenderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Material material = renderer.sharedMaterial;
            return filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0 && material != null && material.mainTexture != null && material.shader != null && material.shader.isSupported;
        }

        /// <summary>Enabled textured body meshes in the current view; held items are excluded.</summary>
        public int VisibleBodyParts
        {
            get
            {
                if (disposed) return 0;
                int count = 0;
                foreach (MeshRenderer renderer in bodyPartRenderers)
                {
                    if (IsRendering(renderer)) count++;
                }
                return count;
            }
        }

        /// <summary>Model rendering exists in the active view and the original Lamb renderer is hidden.</summary>
        public bool Active
        {
            get { return !disposed && assets.Ready && player != null && worldObject != null && lambRenderer != null && !lambRenderer.enabled && (WorldVisible || ViewVisible); }
        }

        private sealed class DisplayTransform
        {
            public float[] rotation;
            public float[] translation;
            public float[] scale;
        }

        public MinecraftAvatar(MinecraftAssets assets)
        {
            if (assets == null) throw new ArgumentNullException("assets");
            this.assets = assets;
            feedback = new AvatarFeedback(assets);
            bookBinding = new BookAnimationBinding(assets);
            fishingVisuals = new FishingVisuals(assets);
        }

        public void Tick(PlayerFarming p, bool firstPerson, string weaponId)
        {
            if (disposed || !assets.Ready) return;
            feedback.Bind(p);
            bookBinding.Bind(p);
            fishingBinding.Bind(p);
            fishingVisuals.Bind(p, fishingBinding.Active);
            if (p == null)
            {
                if (worldObject != null) worldObject.SetActive(false);
                if (viewObject != null) viewObject.SetActive(false);
                RestoreLamb();
                player = null;
                ceremonyClip=null;
                crouchWeight=0f;sleepBedSearched=false;sleepBed=null;
                return;
            }
            if (worldObject == null) Create(p);
            if (player != p)
            {
                RestoreLamb();
                player = p;
                dying=false;deathTime=0f;observedDeath=feedback.DeathSerial;
                lastFacing=p.state==null?V("default_facing"):p.state.facingAngle;
                ceremonyClip=null;
                crouchWeight=0f;sleepBedSearched=false;sleepBed=null;
                lambRenderer = p.Spine == null ? null : p.Spine.GetComponent<Renderer>();
                if (lambRenderer != null) lambWasEnabled = lambRenderer.enabled;
                lastPosition = p.transform.position;
            }
            bool visible = p.gameObject.activeInHierarchy && (p.Spine == null || p.Spine.gameObject.activeInHierarchy);
            // Hide rendering alone: Spine continues all gameplay animation events, weapon collisions and tools.
            if (lambRenderer != null) lambRenderer.enabled = false;
            worldObject.SetActive(visible && !firstPerson);
            Camera camera = Camera.main;
            viewObject.SetActive(visible && firstPerson && camera != null);
            if (viewObject.activeSelf)
            {
                AlignViewCamera(camera);
            }
            Vector3 position = p.transform.position;
            Vector3 delta = position - lastPosition;
            float speed = new Vector2(delta.x, delta.y).magnitude / Mathf.Max(0.0001f, Time.deltaTime);
            lastPosition = position;
            float move = Mathf.Clamp01(speed / V("move_full_speed"));
            walkPhase += Time.deltaTime * V("walk_frequency") * move;
            float stride = Mathf.Sin(walkPhase) * V("walk_degrees") * move;
            StateMachine.State currentState = p.state != null ? p.state.CURRENT_STATE : StateMachine.State.Idle;
            bool deathState=currentState==StateMachine.State.Dieing||currentState==StateMachine.State.Dead||currentState==StateMachine.State.GameOver||currentState==StateMachine.State.FinalGameOver;
            if(!dying&&(deathState||observedDeath!=feedback.DeathSerial))
            {dying=true;deathTime=0f;deathOrigin=position;DeathDrawFrames=0;}
            observedDeath=feedback.DeathSerial;
            if(dying&&!deathState&&p.health!=null&&p.health.CurrentHP>0f)
            {dying=false;deathTime=0f;}
            if(dying)
            {
                // Native GameOver freezes world time but explicitly runs Spine independently.
                // Continue this presentation at quarter speed during that native freeze;
                // ordinary slow motion still uses its exact scaled delta, and pause stays paused.
                bool nativeFreeze=deathState&&Time.timeScale==0f&&(Lamb.UI.UIManager.Instance==null||!Lamb.UI.UIManager.Instance.IsPaused);
                deathTime+=nativeFreeze?Time.unscaledDeltaTime*0.25f:Time.deltaTime;
            }
            bool attacking = currentState == StateMachine.State.Attacking || currentState == StateMachine.State.SignPostAttack || currentState == StateMachine.State.RecoverFromAttack || currentState == StateMachine.State.Casting;
            if (attacking && (!wasAttacking || (currentState == StateMachine.State.Attacking && previousState != currentState))) swingTime = V("swing_duration");
            wasAttacking = attacking;
            previousState = currentState;
            swingTime = Mathf.Max(0f, swingTime - Time.deltaTime);
            float swing = swingTime > 0f ? Mathf.Sin((1f - swingTime / V("swing_duration")) * Mathf.PI) : 0f;
            float angle = p.state != null ? p.state.facingAngle : V("default_facing");
            // Native OnDie turns toward the attacker before this frame is drawn.
            if(!dying)lastFacing=angle;
            // COTL's gameplay plane is XY, with negative Z as height. The model uses ordinary XYZ.
            float heightOffset = p.Spine != null ? p.Spine.transform.position.z - p.transform.position.z : 0f;
            worldObject.transform.position = position + new Vector3(0f, 0f, heightOffset - Mathf.Abs(Mathf.Sin(walkPhase)) * V("body_bob") * move);
            worldObject.transform.rotation = Quaternion.AngleAxis(angle + V("ground_yaw_offset"), Vector3.forward) * Quaternion.Euler(V("ground_pitch"), 0f, 0f);
            // Ignore a transient visual squash on the Lamb; root scale still follows actual relic size changes.
            float scale = p.playerRelic != null ? Mathf.Max(V("minimum_scale"), p.playerRelic.PlayerScaleModifier) : 1f;
            worldObject.transform.localScale = Vector3.one * scale;
            rightLeg.localRotation = Quaternion.Euler(stride, 0f, 0f);
            leftLeg.localRotation = Quaternion.Euler(-stride, 0f, 0f);
            leftArm.localRotation = Quaternion.Euler(stride, 0f, -V("arm_rest_roll"));
            rightArm.localRotation = Quaternion.Euler(-stride * 0.5f - (String.IsNullOrEmpty(weaponId)?0f:V("mc_item_arm_pitch")) - swing * V("attack_arm_pitch"), -swing * V("attack_arm_yaw"), V("arm_rest_roll") + swing * V("attack_arm_roll"));
            body.localRotation = Quaternion.Euler(0f, -swing * V("attack_torso_yaw"), 0f);
            ApplyIdleHead(swing * V("attack_head_yaw"));
            // The original Spine continues ticking while hidden. Read its actual track and timing
            // so resource actions are synchronized with the game's harvesting events.
            var nativeTrack = p.Spine != null && p.Spine.AnimationState != null ? p.Spine.AnimationState.GetCurrent(0) : null;
            string nativeAnimation = nativeTrack != null && nativeTrack.Animation != null ? nativeTrack.Animation.Name : "";
            float nativeDuration = nativeTrack != null && nativeTrack.Animation != null ? Mathf.Max(0.001f, nativeTrack.Animation.Duration) : 1f;
            float nativePhase = nativeTrack == null ? 0f : nativeTrack.TrackTime / nativeDuration;
            bool meditating = currentState == StateMachine.State.Meditate;
            bool crouchPressed=Time.unscaledTime<feedback.CrouchUntil;
            crouchWeight=Mathf.MoveTowards(crouchWeight,crouchPressed?1f:0f,Time.unscaledDeltaTime/(crouchPressed?0.09f:0.13f));
            bool crouching=crouchWeight>0f;
            bool warpingOut = currentState == StateMachine.State.CustomAnimation && nativeAnimation.StartsWith("warp-out", StringComparison.Ordinal);
            bool warpingIn = currentState == StateMachine.State.CustomAnimation && nativeAnimation.StartsWith("warp-in", StringComparison.Ordinal);
            bool utilityAllowed = !meditating && !crouching && !warpingOut && !warpingIn;
            bool sleeping=utilityAllowed&&nativeAnimation.StartsWith("sleeping",StringComparison.Ordinal);
            SleepOnBed=false;
            if(!sleeping)
            {
                sleepBedSearched=false;sleepBed=null;sleepEntrySide=0f;
                wakeExitSearched=false;wakeExitFound=false;
            }
            bool carrying = p.CarryingDeadFollowerID >= 0 &&
                (currentState == StateMachine.State.Idle_CarryingBody || currentState == StateMachine.State.Moving_CarryingBody);
            bool sweeping = currentState == StateMachine.State.CustomAnimation && nativeAnimation == "cleaning";
            bool showingBroom = currentState == StateMachine.State.CustomAnimation &&
                (nativeAnimation.StartsWith("Mop/upgrade-mop-", StringComparison.Ordinal) || nativeAnimation == "Mop/collect");
            BroomVisual broom = null;
            float sweepStroke = Mathf.Sin(Mathf.Repeat(nativePhase, 1f) * Mathf.PI * 2f);
            float broomLift = 1f;
            if (nativeAnimation == "Mop/upgrade-mop-start")
                broomLift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.42f, nativePhase));
            else if (nativeAnimation == "Mop/upgrade-mop-stop")
                broomLift = Mathf.SmoothStep(1f, 0f, nativePhase);
            Spine.TrackEntry harvest = null;
            if (currentState == StateMachine.State.CustomAnimation && p.Spine?.AnimationState != null)
                foreach (Spine.TrackEntry track in p.Spine.AnimationState.Tracks)
                    if (track?.Animation?.Name == "actions/chop-wood" || track?.Animation?.Name == "actions/chop-stone") harvest = track;
            bool chopping = harvest?.Animation.Name == "actions/chop-wood";
            float harvestPhase = harvest == null ? 0f : Mathf.Repeat(harvest.TrackTime / Mathf.Max(0.001f, harvest.Animation.Duration), 1f);
            bool collecting = utilityAllowed && nativeAnimation == "actions/collect-berries" && currentState == StateMachine.State.TimedAction;
            bool constructing = utilityAllowed && nativeAnimation == "actions/hammer" && currentState == StateMachine.State.CustomAnimation;
            var secondTrack = p.Spine != null && p.Spine.AnimationState != null ? p.Spine.AnimationState.GetCurrent(1) : null;
            string secondAnimation = secondTrack != null && secondTrack.Animation != null ? secondTrack.Animation.Name : "";
            bool watering = utilityAllowed && (nativeAnimation == "oldstuff/Farming-water_track" || secondAnimation == "Farming-water_track");
            bool blessing = utilityAllowed && nativeAnimation == "bless" && currentState == StateMachine.State.CustomAnimation;
            bool reading = utilityAllowed && bookBinding.PoseActive;
            bool ceremony = utilityAllowed && bookBinding.CeremonyClip.Length>0;
            Vector3 ceremonyAnchor=worldObject.transform.position;
            bool nativeGesture=utilityAllowed&&bookBinding.GestureActive;
            bool showingWeapon=utilityAllowed&&Gameplay.IsWeaponPresentation(p,nativeAnimation);
            ResetPartPositions();
            AnimationPose = "walk/idle";
            if (carrying)
            {
                AnimationPose = "carry-follower";
                weaponId = "";
                body.localRotation = Quaternion.Euler(10f + 3f * move, 0f, stride * 0.1f);
                head.localRotation = Quaternion.Euler(-8f, 0f, 0f);
                rightArm.localRotation = Quaternion.Euler(-165f, 0f, 14f);
                leftArm.localRotation = Quaternion.Euler(-165f, 0f, -14f);
            }
            else if (harvest != null)
            {
                AnimationPose = chopping ? "chop-wood" : "chop-stone";
                weaponId = chopping ? "stone_axe" : "stone_pickaxe";
                swing = 0.5f - 0.5f * Mathf.Cos(harvestPhase * Mathf.PI * 2f);
                rightArm.localRotation = Quaternion.Euler(-25f - swing * 105f, -15f, 8f + swing * 12f);
                leftArm.localRotation = Quaternion.Euler(-25f - swing * 55f, 15f, -18f);
                body.localRotation = Quaternion.Euler(10f * swing, -8f * swing, 0f);
                head.localRotation = Quaternion.Euler(14f, 0f, 0f);
            }
            else if (ceremony)
            {
                if(ceremonyClip==null)ceremonyDrawn=CaptureCeremony(ceremonyAnchor);
                AnimationPose="ceremony/"+bookBinding.CeremonyClip;
                weaponId="";
                bookBinding.ApplyCeremonyPose(worldObject.transform,body,head,rightArm,leftArm,rightLeg,leftLeg);
                BlendCeremony(bookBinding.CeremonyClip,ceremonyAnchor);
            }
            else if (utilityAllowed && fishingBinding.Active)
            {
                AnimationPose = "fishing/" + fishingBinding.Clip;
                weaponId = fishingVisuals.Cast ? "fishing_rod_cast" : "fishing_rod";
                fishingBinding.Apply(worldObject.transform, body, head, rightArm, leftArm, rightLeg, leftLeg);
            }
            else if(showingWeapon)
            {
                AnimationPose="weapon-pickup";weaponId=Gameplay.PresentedWeapon(p);
                bookBinding.ApplyPose(worldObject.transform,body,head,rightArm,leftArm,rightLeg,leftLeg);
            }
            else if ((sweeping || showingBroom) && DataManager.Instance != null)
            {
                int level = p.isLamb ? DataManager.Instance.ChoreXPLevel : DataManager.Instance.ChoreXPLevel_Coop;
                broom = BroomVisual.ForLevel(level);
                weaponId = broom.Id;
                AnimationPose = sweeping ? "sweep" : "broom-presentation";
                rightLeg.localRotation = leftLeg.localRotation = Quaternion.identity;
                body.localRotation = Quaternion.Euler(sweeping ? 14f : 0f, sweeping ? 12f * sweepStroke : 0f, 0f);
                head.localRotation = Quaternion.Euler(sweeping ? 18f : 0f, sweeping ? 9f * sweepStroke : 0f, 0f);
                rightArm.localRotation = sweeping
                    ? Quaternion.Euler(-27f, -12f, 24f * sweepStroke - 8f)
                    : Quaternion.Euler(Mathf.Lerp(-25f, -118f, broomLift), -9f, Mathf.Lerp(5f, 20f, broomLift));
                leftArm.localRotation = Quaternion.Euler(-12f, 0f, -12f);
            }
            else if (collecting)
            {
                AnimationPose = "collect-berries"; weaponId = "berry";
                float reach = Mathf.Sin(nativePhase * Mathf.PI * 2f);
                rightArm.localRotation = Quaternion.Euler(-70f - reach * 28f, -12f, 8f);
                leftArm.localRotation = Quaternion.Euler(-55f + reach * 24f, 12f, -8f);
                body.localRotation = Quaternion.Euler(16f, reach * 6f, 0f);
                head.localRotation = Quaternion.Euler(12f, 0f, 0f);
            }
            else if (constructing)
            {
                AnimationPose = "build"; weaponId = ConstructionItem(p, nativeTrack == null ? Time.time : nativeTrack.TrackTime);
                float place = 0.5f - 0.5f * Mathf.Cos(nativePhase * Mathf.PI * 2f);
                rightArm.localRotation = Quaternion.Euler(-55f - place * 55f, -12f, 8f);
                leftArm.localRotation = Quaternion.Euler(-45f - place * 35f, 12f, -8f);
                body.localRotation = Quaternion.Euler(place * 10f, 0f, 0f);
                head.localRotation = Quaternion.Euler(15f, 0f, 0f);
            }
            else if (watering)
            {
                AnimationPose = "water"; weaponId = "water_bucket";
                rightArm.localRotation = Quaternion.Euler(-75f + Mathf.Sin(nativePhase * 8f) * 4f, -10f, 12f);
                leftArm.localRotation = Quaternion.Euler(-40f, 10f, -8f);
                head.localRotation = Quaternion.Euler(20f, 0f, 0f);
            }
            else if (blessing)
            {
                AnimationPose = "bless"; weaponId = "";
                rightArm.localRotation = Quaternion.Euler(-105f + Mathf.Sin(nativePhase * Mathf.PI) * 22f, -8f, 18f);
                leftArm.localRotation = Quaternion.Euler(-45f, 8f, -12f);
                head.localRotation = Quaternion.Euler(-5f, 0f, 0f);
            }
            else if (reading)
            {
                AnimationPose = "book/" + bookBinding.AnimationName; weaponId = "";
                bookBinding.ApplyPose(worldObject.transform, body, head, rightArm, leftArm, rightLeg, leftLeg);
            }
            else if(nativeGesture)
            {
                AnimationPose="gesture/"+bookBinding.AnimationName;weaponId="";
                bookBinding.ApplyPose(worldObject.transform,body,head,rightArm,leftArm,rightLeg,leftLeg);
            }
            else if (meditating)
            {
                AnimationPose = "meditate";
                weaponId = "";
                LowerTorso(V("meditation_drop"));
                // The rigid Minecraft legs extend in front of the pelvis and cross inward.
                // Slightly separated hip joints leave the upper legs visible beside the torso.
                rightLeg.localPosition += new Vector3(V("meditation_hip_out"), 0f, V("meditation_leg_front"));
                leftLeg.localPosition += new Vector3(-V("meditation_hip_out"), 0f, V("meditation_leg_front") + V("meditation_cross_offset"));
                rightLeg.localRotation = Quaternion.Euler(V("meditation_leg_pitch"), 0f, -V("meditation_leg_cross"));
                leftLeg.localRotation = Quaternion.Euler(V("meditation_leg_pitch"), 0f, V("meditation_leg_cross"));
                // Palms descend and reach forward toward the crossed knees rather than sideways.
                rightArm.localRotation = Quaternion.Euler(V("meditation_arm_pitch"), 0f, -V("meditation_arm_inward"));
                leftArm.localRotation = Quaternion.Euler(V("meditation_arm_pitch"), 0f, V("meditation_arm_inward"));
                head.localRotation = Quaternion.Euler(10f, 0f, 0f);
            }
            else if (crouching)
            {
                AnimationPose = "crouch";
                ApplyCrouch(crouchWeight);
            }
            else if(sleeping)
            {
                AnimationPose="sleep/"+nativeAnimation;
                weaponId="";
                ApplySleep(p,nativeAnimation,nativePhase);
            }
            else if (warpingOut || warpingIn || currentState == StateMachine.State.Teleporting || currentState == StateMachine.State.SpawnIn || currentState == StateMachine.State.SpawnOut)
            {
                weaponId = "";
                if (ApplyWarpPose(warpingIn, warpingOut, nativePhase) < 0.015f)
                { worldObject.SetActive(false); viewObject.SetActive(false); }
            }
            else if(IsScriptedPose(currentState,nativeAnimation)&&
                bookBinding.ApplyGroundedAction(nativeAnimation,body,head,rightArm,leftArm,rightLeg,leftLeg))
            {
                AnimationPose="native/"+nativeAnimation;
                if(!IsCombatGesture(currentState))weaponId=ActionItem(nativeAnimation);
            }
            else if (weaponId == "bow" || Gameplay.IsCrossbowItem(weaponId))
            {
                AnimationPose = weaponId=="bow"?"bow":"crossbow";
                float release = Time.time < bowShotUntil ? (bowShotUntil - Time.time) / V("bow_release_duration") : 0f;
                rightArm.localRotation = Quaternion.Euler(-90f + 12f * release, -15f, 0f);
                leftArm.localRotation = Quaternion.Euler(-85f, 25f + 20f * release, -8f);
            }
            if(!ceremony&&ceremonyClip!=null)
            {
                if(!dying&&utilityAllowed&&AnimationPose=="walk/idle"&&move<0.12f&&nativeAnimation.StartsWith("idle",StringComparison.Ordinal))
                {
                    weaponId="";
                    rightArm.localRotation=Quaternion.Euler(0f,0f,V("arm_rest_roll"));
                    BlendCeremony("",ceremonyAnchor);
                }
                else ceremonyClip=null;
            }
            SetDamageTint(dying || Time.unscaledTime < feedback.RedUntil);
            if (currentState == StateMachine.State.Dodging) worldObject.transform.rotation *= Quaternion.Euler(V("dodge_degrees"), 0f, 0f);
            if (useProgress > 0f && !ceremony && harvest == null && !carrying && !sleeping && !crouching &&
                !AnimationPose.StartsWith("native/",StringComparison.Ordinal))
            {
                AnimationPose = "eat";
                float bite = Mathf.Sin(useProgress * Mathf.PI * 8f);
                rightArm.localRotation = Quaternion.Euler(-105f + bite * 8f, -20f, -20f);
            }
            DeathRoll=0f;
            if(dying)
            {
                AnimationPose="death";weaponId="";reading=nativeGesture=showingWeapon=false;broom=null;
                ResetPartPositions();body.localRotation=head.localRotation=Quaternion.identity;
                rightLeg.localRotation=leftLeg.localRotation=Quaternion.identity;
                rightArm.localRotation=Quaternion.Euler(0f,0f,3f);leftArm.localRotation=Quaternion.Euler(0f,0f,-3f);
                DeathRoll=90f*Mathf.SmoothStep(0f,1f,Mathf.Clamp01(deathTime/V("death_fall_duration")));
                // Native death can begin in first person; show the falling actor and hide the view item.
                worldObject.SetActive(visible);viewObject.SetActive(false);
                Quaternion facing=Quaternion.AngleAxis(lastFacing+V("ground_yaw_offset"),Vector3.forward)*Quaternion.Euler(V("ground_pitch"),0f,0f);
                Quaternion fall=facing*Quaternion.AngleAxis(-DeathRoll,Vector3.forward);
                // Rotate about the right edge of the feet. A local roll keeps the model's forward axis fixed.
                Vector3 pivot=Vector3.right*(4f*PixelSize*scale);
                worldObject.transform.SetPositionAndRotation(deathOrigin+facing*pivot-fall*pivot,fall);
                KeepAboveGround(deathOrigin.z);
                // OnDie disables nearby MeshRenderers including detached Minecraft parts.
                // Restore only our body, leaving the game's enemies and props untouched.
                foreach(MeshRenderer part in bodyPartRenderers)part.enabled=true;
            }
            // Item geometry is anchored at the handle pixel, independent of model-center translation.
            // Keep its camera-space orientation readable while the right arm follows the native swing.
            SetWeapon(weaponId);
            Vector3 rightPalm = Vector3.zero, leftPalm = Vector3.zero;
            CarryHandsRaised = false;
            if (carrying && !dying)
            {
                rightPalm = rightArm.TransformPoint(new Vector3(PalmX(rightArm, true), -10f, 0f) * PixelSize);
                leftPalm = leftArm.TransformPoint(new Vector3(PalmX(leftArm, false), -10f, 0f) * PixelSize);
                CarryHandsRaised = Vector3.Dot(rightPalm - rightArm.position, worldObject.transform.up) > 0.2f &&
                    Vector3.Dot(leftPalm - leftArm.position, worldObject.transform.up) > 0.2f;
            }
            carriedFollower.Tick(CarryHandsRaised && visible && !firstPerson ? p : null, camera, worldObject.transform,
                (rightPalm + leftPalm) * 0.5f, lambRenderer);
            if (broom != null && itemId == broom.Id)
            {
                PoseBroom(broom, sweeping, sweepStroke, nativePhase, nativeAnimation, broomLift);
            }
            else if (viewItem != null && viewItem.gameObject.activeSelf)
            {
                float unit=16f*PixelSize;
                Vector3 hand=new Vector3(V("mc_hand_x"),V("mc_hand_y"),V("mc_hand_z"))*unit;
                Quaternion action=Quaternion.identity;Vector3 swingOffset=Vector3.zero;
                if(swingTime>0f || harvest != null)
                {
                    float phase=harvest != null ? harvestPhase : Mathf.Clamp01(1f-swingTime/V("swing_duration"));
                    float a=Mathf.Sin(Mathf.PI*phase*phase),b=Mathf.Sin(Mathf.PI*Mathf.Sqrt(phase));
                    action=Quaternion.AngleAxis(-45f+20f*a,Vector3.up)*Quaternion.AngleAxis(-20f*b,Vector3.forward)*Quaternion.AngleAxis(80f*b,Vector3.right)*Quaternion.AngleAxis(45f,Vector3.up);
                    swingOffset=new Vector3(-0.4f*b,0.2f*Mathf.Sin(2f*Mathf.PI*Mathf.Sqrt(phase)),0.2f*Mathf.Sin(Mathf.PI*phase))*unit;
                }
                if(collecting||constructing)swingOffset.y+=Mathf.Sin(nativePhase*Mathf.PI*2f)*0.025f;
                if(watering)action*=Quaternion.AngleAxis(25f*Mathf.Sin(nativePhase*Mathf.PI),Vector3.right);
                Vector3 local=hand+action*viewDisplayTranslation+swingOffset;
                Quaternion rotation=action*viewDisplayRotation;
                if(useProgress>0f)
                {
                    float remaining=1f-useProgress,amount=1f-Mathf.Pow(remaining,27f);
                    float bob=remaining<0.8f?Mathf.Abs(Mathf.Cos(remaining*32f*Mathf.PI/4f)*0.1f):0f;
                    Quaternion eat=Quaternion.AngleAxis(-90f*amount,Vector3.up)*Quaternion.AngleAxis(-10f*amount,Vector3.right)*Quaternion.AngleAxis(30f*amount,Vector3.forward);
                    local=eat*(hand+viewDisplayTranslation)+new Vector3(0.6f*amount,bob-0.5f*amount,0f)*unit;
                    rotation=eat*viewDisplayRotation;
                }
                local.y-=Mathf.Abs(Mathf.Sin(walkPhase))*V("view_bob")*move;
                viewItem.localPosition=local;viewItem.localRotation=rotation;viewItem.localScale=viewItemBaseScale;
            }
            if (fishingBinding.Active && itemId.StartsWith("fishing_rod", StringComparison.Ordinal))
            {
                fishingBinding.PlaceRod(worldObject.transform, rightArm, heldItem, V("item_world_size"));
                if (firstPerson && viewItem != null && viewItem.gameObject.activeSelf)
                {
                    // Preserve the original Minecraft first-person item context;
                    // native cast/reel motion supplies the additional pitch and yaw.
                    float pitch = Mathf.Atan2(fishingBinding.RodDirection.y, fishingBinding.RodDirection.z) * Mathf.Rad2Deg;
                    viewItem.localRotation = Quaternion.Euler(-Mathf.Clamp(pitch - 36f, -65f, 100f) * 0.5f, 0f, 0f) * viewItem.localRotation;
                }
            }
            Vector3 fishingTip = fishingBinding.RodTip;
            if (firstPerson && viewItem != null && fishingBinding.Active)
                fishingTip = viewItem.TransformPoint(new Vector3(13.5f / 16f - 0.5f, 14.5f / 16f - 0.5f, 0f) * V("item_world_size"));
            fishingVisuals.Tick(fishingBinding.Active, fishingTip, camera);
            bookBinding.Present(worldBook, firstPersonBook, worldObject.transform, leftArm,rightArm, visible && (reading||nativeGesture) && !showingWeapon, firstPerson);
            UpdateGlint();
            if (lambRenderer != null)
            {
                worldBook.SetSorting(lambRenderer.sortingLayerID, lambRenderer.sortingOrder);
                firstPersonBook.SetSorting(lambRenderer.sortingLayerID, lambRenderer.sortingOrder);
                foreach (MeshRenderer renderer in renderers)
                {
                    if (renderer == null) continue;
                    renderer.sortingLayerID = lambRenderer.sortingLayerID;
                    renderer.sortingOrder = lambRenderer.sortingOrder;
                }
            }
        }

        private string ConstructionItem(PlayerFarming owner, float nativeTime)
        {
            return Gameplay.Current?.ConstructionItem(owner,nativeTime)??"";
        }

        private void Create(PlayerFarming p)
        {
            worldObject = new GameObject("CultCraft_Steve_World");
            viewObject = new GameObject("CultCraft_Steve_FirstPerson");
            UnityEngine.Object.DontDestroyOnLoad(worldObject);
            // Keep the viewmodel alive when the level camera is replaced.
            UnityEngine.Object.DontDestroyOnLoad(viewObject);
            skinMaterial = MakeMaterial(SkinTexture, p);
            body = MakePart(worldObject.transform, "torso");
            // Head and shoulder joints follow the torso, including crouch/warp rotations.
            head = MakePart(body, "head");
            rightArm = MakePart(body, "right_arm");
            leftArm = MakePart(body, "left_arm");
            rightLeg = MakePart(worldObject.transform, "right_leg");
            leftLeg = MakePart(worldObject.transform, "left_leg");
            heldItem = MakeTransform(rightArm, "MinecraftHeldItem");
            heldFilter = heldItem.gameObject.AddComponent<MeshFilter>();
            heldRenderer = heldItem.gameObject.AddComponent<MeshRenderer>();
            ConfigureRenderer(heldRenderer);
            heldItem.gameObject.AddComponent<BookRenderProbe>().Observed=delegate(Camera rendered)
            {if(rendered!=null&&rendered==Camera.main&&rendered.cameraType==CameraType.Game&&AnimationPose=="weapon-pickup"&&heldRenderer.enabled&&heldItem.gameObject.activeInHierarchy&&!String.IsNullOrEmpty(itemId)&&lastWeaponPresentationFrame!=Time.frameCount){lastWeaponPresentationFrame=Time.frameCount;WeaponPresentationDraws++;}};
            heldItem.gameObject.AddComponent<BookRenderProbe>().Observed = ObserveBroom;
            viewArm = MakeTransform(viewObject.transform, "Minecraft invisible hand reference");
            viewItem = MakeTransform(viewObject.transform, "MinecraftFirstPersonItem");
            viewFilter = viewItem.gameObject.AddComponent<MeshFilter>();
            viewRenderer = viewItem.gameObject.AddComponent<MeshRenderer>();
            ConfigureRenderer(viewRenderer);
            viewItem.gameObject.AddComponent<BookRenderProbe>().Observed = ObserveBroom;
            worldBook = new BookVisual(assets, body, false);
            firstPersonBook = new BookVisual(assets, viewObject.transform, true);
            CreateGlint();
            worldObject.SetActive(false);
            viewObject.SetActive(false);
        }

        private void ObserveBroom(Camera rendered)
        {
            if(rendered==null||rendered!=Camera.main||rendered.cameraType!=CameraType.Game)return;
            HeldRenderFrames++;
            if(AnimationPose=="sweep"||AnimationPose=="broom-presentation")BroomDraws++;
            if(AnimationPose!=null&&AnimationPose.StartsWith("fishing/",StringComparison.Ordinal))FishingDraws++;
        }

        public void SetEnchantment(int level)
        {
            enchantmentLevel = Math.Max(0, level);
            UpdateGlint();
        }

        private void CreateGlint()
        {
            EnchantmentRow row = Spec.Enchantment("sharpness");
            Texture2D texture = assets.Texture(row.GlintAsset);
            if (texture == null) return;
            Shader shader = null;
            foreach (string name in row.GlintShaders.Split('|'))
            {
                Shader candidate = Shader.Find(name);
                if (candidate != null && candidate.isSupported) { shader = candidate; break; }
            }
            // Keep the true enchantment damage when a game build has stripped every additive shader.
            // In that case GlintActive remains false; no unrelated texture or fake glow is substituted.
            if (shader == null) return;
            glintMaterial = new Material(shader);
            glintMaterial.name = "CultCraft original Minecraft enchantment glint";
            texture.wrapMode = TextureWrapMode.Repeat;
            glintMaterial.mainTexture = texture;
            glintMaterial.mainTextureScale = Vector2.one * row.GlintTile;
            Color tint = new Color(1f, 1f, 1f, row.GlintAlpha);
            if (glintMaterial.HasProperty("_TintColor")) glintMaterial.SetColor("_TintColor", tint);
            if (glintMaterial.HasProperty("_Color")) glintMaterial.SetColor("_Color", tint);
            if (glintMaterial.HasProperty("_ZWrite")) glintMaterial.SetInt("_ZWrite", 0);
            glintMaterial.renderQueue = (int)V("material_queue") + row.GlintQueueOffset;
            materials.Add(glintMaterial);
            Transform held = MakeTransform(heldItem, "MinecraftOriginalEnchantmentGlint");
            heldGlintFilter = held.gameObject.AddComponent<MeshFilter>();
            heldGlintRenderer = held.gameObject.AddComponent<MeshRenderer>();
            heldGlintRenderer.sharedMaterial = glintMaterial;
            ConfigureRenderer(heldGlintRenderer);
            Transform view = MakeTransform(viewItem, "MinecraftOriginalFirstPersonGlint");
            viewGlintFilter = view.gameObject.AddComponent<MeshFilter>();
            viewGlintRenderer = view.gameObject.AddComponent<MeshRenderer>();
            viewGlintRenderer.sharedMaterial = glintMaterial;
            ConfigureRenderer(viewGlintRenderer);
            UpdateGlint();
        }

        private void UpdateGlint()
        {
            if (disposed || heldGlintRenderer == null || viewGlintRenderer == null) return;
            bool enabled = enchantmentLevel > 0 && !String.IsNullOrEmpty(itemId) && (itemId.EndsWith("_sword", StringComparison.Ordinal) || itemId.EndsWith("_axe", StringComparison.Ordinal) || itemId == "mace" || itemId == "bow");
            heldGlintRenderer.enabled = enabled;
            viewGlintRenderer.enabled = enabled;
            heldGlintFilter.sharedMesh = heldFilter.sharedMesh;
            viewGlintFilter.sharedMesh = viewFilter.sharedMesh;
            if (glintMaterial != null)
            {
                EnchantmentRow row = Spec.Enchantment("sharpness");
                glintMaterial.mainTextureOffset = new Vector2(Mathf.Repeat(Time.time * row.GlintScrollU, 1f), Mathf.Repeat(Time.time * row.GlintScrollV, 1f));
            }
        }

        private static Transform MakeTransform(Transform parent, string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            return obj.transform;
        }

        private Transform MakePart(Transform parent, string id)
        {
            AvatarRow row = PartRow(id);
            Transform part = MakeTransform(parent, row.Label);
            part.localPosition = new Vector3(row.PivotX, row.PivotY, row.PivotZ) * PixelSize;
            if (parent == body) part.localPosition -= PartPosition("torso");
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = BuildBodyMesh(row);
            MeshRenderer renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = skinMaterial;
            ConfigureRenderer(renderer);
            bodyPartRenderers.Add(renderer);
            part.gameObject.AddComponent<BookRenderProbe>().Observed=delegate(Camera rendered){if(rendered==Camera.main&&rendered!=null&&rendered.cameraType==CameraType.Game){SkinRenderFrames++;if(dying&&DeathTint)DeathDrawFrames++;}};
            return part;
        }
        private AvatarRow PartRow(string id)
        {
            AvatarRow row=Spec.Avatar(id);
            if(slimArms&&(id=="right_arm"||id=="left_arm"))
            {row.Width=3;row.CenterX=id=="right_arm"?0.5f:-0.5f;row.PivotY-=0.5f;}
            return row;
        }
        private Mesh BuildBodyMesh(AvatarRow row)
        {
            Mesh mesh=GenerateSkinPart(SkinTexture,row);meshes.Add(mesh);return mesh;
        }
        internal static Mesh GenerateSkinPart(Texture2D skin,AvatarRow row)
        {
            Vector3 center = new Vector3(row.CenterX, row.CenterY, row.CenterZ) * PixelSize;
            var builder = new MeshBuilder();
            AddBox(builder, center, new Vector3(row.Width, row.Height, row.Depth) * PixelSize, row.Width, row.Height, row.Depth, row.U, row.V, false, row.MirrorU, row.SwapSides,skin);
            // Second skin layers are sparse geometry, so transparent pixels work even with opaque unlit shaders.
            float shell = row.OverlayExpansion;
            AddBox(builder, center, new Vector3(row.Width + shell, row.Height + shell, row.Depth + shell) * PixelSize, row.Width, row.Height, row.Depth, row.OverlayU, row.OverlayV, true, row.MirrorU, row.SwapSides,skin);
            Mesh mesh = builder.Build("CultCraft Steve " + row.Label);
            return mesh;
        }

        private void ConfigureRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderers.Add(renderer);
        }

        private Material MakeMaterial(Texture2D texture, PlayerFarming p)
        {
            // A 3D body must write depth and cull back faces. Sprites/Default fixes
            // ZWrite Off and Cull Off in its shader pass; material properties cannot repair it.
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Transparent Cutout");
            Material material;
            if (shader != null) material = new Material(shader);
            else
            {
                Renderer original = p != null && p.Spine != null ? p.Spine.GetComponent<Renderer>() : null;
                if (original == null || original.sharedMaterial == null) throw new InvalidOperationException("No usable shader is loaded for the Minecraft avatar.");
                material = new Material(original.sharedMaterial);
            }
            material.name = "CultCraft local texture material";
            material.mainTexture = texture;
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", Color.white);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 1);
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", 2);
            material.renderQueue = (int)V("material_queue");
            materials.Add(material);
            return material;
        }

        private void SetWeapon(string requested)
        {
            string next = String.IsNullOrEmpty(requested) ? "" : requested.Replace("minecraft:", "").Replace(".png", "");
            int slash = next.LastIndexOf('/');
            if (slash >= 0) next = next.Substring(slash + 1);
            if (itemId == next)
            {
                if(next.Length>0)ShowHeldItem();
                return;
            }
            if (next.Length == 0)
            {
                itemId = next;
                heldItem.gameObject.SetActive(false);
                viewItem.gameObject.SetActive(false);
                return;
            }
            // Resolve both contexts before committing a new selection. A failed asset
            // load must remain retryable instead of leaving this item hidden forever.
            BroomVisual broom = BroomVisual.Find(next);
            DisplayTransform third = broom == null ? FindDisplay(next, false) : null;
            DisplayTransform first = broom == null ? FindDisplay(next, true) : null;
            Mesh mesh;
            Material material;
            if (!itemMeshes.TryGetValue(next, out mesh))
            {
                bool block=IsBlockItem(next);
                Sprite nativeMaterial=null;
                if(next.StartsWith("native_material_",StringComparison.Ordinal)&&Int32.TryParse(next.Substring(16),out int nativeId))
                    nativeMaterial=Gameplay.Current?.ConstructionMaterialIcon(nativeId);
                Texture2D texture = nativeMaterial!=null?nativeMaterial.texture:broom != null ? assets.BroomTexture(broom) :
                    block ? assets.HeldBlockTexture(next) : assets.Texture(next);
                if (texture == null)
                {
                    itemId="";
                    heldItem.gameObject.SetActive(false);
                    viewItem.gameObject.SetActive(false);
                    return;
                }
                mesh = nativeMaterial!=null?NativeMaterialMesh(nativeMaterial):block ? BlockItemMesh(next) : ExtrudeItem(texture, next, broom?.Grip);
                material = MakeMaterial(texture, player);
                itemMeshes.Add(next, mesh);
                itemMaterials.Add(next, material);
            }
            else material = itemMaterials[next];
            heldFilter.sharedMesh = mesh;
            viewFilter.sharedMesh = mesh;
            heldRenderer.sharedMaterial = material;
            viewRenderer.sharedMaterial = material;
            // Resolve the original client item's display data through its parent chain.
            if (broom == null)
            {
                ApplyDisplay(heldItem, third, false, next);
                ApplyDisplay(viewItem, first, true, next);
                viewItemBaseScale = viewItem.localScale;
            }
            ShowHeldItem();
            itemId = next;
        }

        private void ShowHeldItem()
        {
            // Native death can disable our detached mesh renderers. Rebinding
            // an item restores both camera contexts, even with a cached mesh.
            if(!heldRenderer.enabled)heldRenderer.enabled=true;
            if(!viewRenderer.enabled)viewRenderer.enabled=true;
            if(!heldItem.gameObject.activeSelf)heldItem.gameObject.SetActive(true);
            if(!viewItem.gameObject.activeSelf)viewItem.gameObject.SetActive(true);
        }

        private Mesh NativeMaterialMesh(Sprite icon)
        {
            Vector2[] spriteVertices=icon.vertices;
            ushort[] source=icon.triangles;
            var vertices=new Vector3[spriteVertices.Length];
            var triangles=new int[source.Length*2];
            Vector2 center=icon.bounds.center;
            float size=V("item_world_size")/Mathf.Max(0.01f,Mathf.Max(icon.bounds.size.x,icon.bounds.size.y));
            for(int i=0;i<vertices.Length;i++)vertices[i]=(spriteVertices[i]-center)*size;
            for(int i=0;i<source.Length;i+=3)
            {
                triangles[i]=source[i];triangles[i+1]=source[i+1];triangles[i+2]=source[i+2];
                int back=source.Length+i;
                triangles[back]=source[i];triangles[back+1]=source[i+2];triangles[back+2]=source[i+1];
            }
            var mesh=new Mesh {name="CultCraft construction material",vertices=vertices,uv=icon.uv,triangles=triangles};
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            return mesh;
        }

        private void PoseBroom(BroomVisual broom, bool sweeping, float stroke, float phase, string animation, float lift)
        {
            const float reach = 0.82f;
            float scale = reach / (V("item_world_size") * broom.BristleReach);
            heldItem.localPosition = new Vector3(slimArms?0.5f:1f, -9f, 1.5f) * PixelSize;
            Vector3 towardsBristles;
            float reveal = 1f;
            if (sweeping)
            {
                // Choose a ground-facing arc in avatar space, independent of the
                // wrist's own rotation. The palm remains the item's local origin.
                float palmHeight = Root.InverseTransformPoint(heldItem.position).y;
                float down = Mathf.Clamp(palmHeight - 0.06f, 0.05f, reach - 0.03f);
                Vector3 heading = Quaternion.AngleAxis(stroke * 50f - 15f, Vector3.up) * Vector3.forward;
                towardsBristles = (heading * Mathf.Sqrt(reach * reach - down * down) + Vector3.down * down).normalized;
            }
            else
            {
                float angle;
                switch (animation)
                {
                    case "Mop/upgrade-mop-start":
                        angle = Mathf.SmoothStep(-35f, 325f, Mathf.InverseLerp(0.13f, 0.85f, phase));
                        reveal = Mathf.Lerp(0.03f, 1f, lift);
                        break;
                    case "Mop/upgrade-mop-loop":
                        angle = -35f + stroke * 8f;
                        break;
                    case "Mop/upgrade-mop-stop":
                        angle = Mathf.SmoothStep(-30f, -125f, phase);
                        reveal = Mathf.Lerp(0.03f, 1f, lift);
                        break;
                    default:
                        angle = -360f * Mathf.Repeat(phase, 1f);
                        break;
                }
                towardsBristles = Quaternion.AngleAxis(angle, Vector3.forward) * Vector3.up;
            }
            heldItem.rotation = Root.rotation * broom.Orient(towardsBristles, Vector3.forward);
            heldItem.localScale = Vector3.one * (scale * reveal);

            // Camera-space sweep from the lower right, with the shaft visible
            // above the bottom edge and the brush end angled toward the floor.
            viewItem.localPosition = new Vector3(0.43f + stroke * 0.055f, -0.12f, 0.94f);
            Vector3 viewDirection = sweeping ? new Vector3(-0.44f + stroke * 0.42f, -0.54f, 0.63f) : towardsBristles;
            viewItem.localRotation = broom.Orient(viewDirection, Vector3.back);
            viewItem.localScale = Vector3.one * (scale * reveal * 0.57f);
        }

        private DisplayTransform FindDisplay(string id, bool firstPerson)
        {
            string assetId=id;
            foreach(AssetRow asset in Spec.AssetRows)
                if(asset.Id==id && asset.Entry.EndsWith(".png",StringComparison.Ordinal))
                {id=asset.Entry.Substring(asset.Entry.LastIndexOf('/')+1).Replace(".png","");break;}
            string model = "item/" + id;
            foreach(HeldItemRow profile in Spec.HeldItemRows)
                if(profile.Id==assetId || profile.Id==id)
                {model=Spec.Asset(profile.Model).Entry.Replace("assets/minecraft/models/","").Replace(".json","");break;}
            for (int depth = 0; depth < 8; depth++)
            {
                string json = assets.ReadText("assets/minecraft/models/" + model + ".json");
                if (json == null) break;
                JObject data=JObject.Parse(json);
                DisplayTransform display = ReadDisplay(data,firstPerson);
                if (display != null) return display;
                string parent=(string)data["parent"];
                if (String.IsNullOrEmpty(parent) || parent.StartsWith("builtin/", StringComparison.Ordinal)) break;
                model = parent.Replace("minecraft:", "");
            }
            string fallback=assets.ReadText("assets/minecraft/models/item/generated.json");
            DisplayTransform result=String.IsNullOrEmpty(fallback)?null:ReadDisplay(JObject.Parse(fallback),firstPerson);
            if(result==null)throw new InvalidDataException("Minecraft item display context is missing: "+assetId+" ("+(firstPerson?"first":"third")+" person).");
            return result;
        }

        private static DisplayTransform ReadDisplay(JObject model,bool firstPerson)
        {
            JObject display=model["display"]?[firstPerson?"firstperson_righthand":"thirdperson_righthand"] as JObject;
            if(display==null)return null;
            return new DisplayTransform { rotation=display["rotation"]?.ToObject<float[]>(), translation=display["translation"]?.ToObject<float[]>(), scale=display["scale"]?.ToObject<float[]>() };
        }

        private static Vector3 ArrayVector(float[] value, Vector3 fallback)
        {
            return value != null && value.Length >= 3 ? new Vector3(value[0], value[1], value[2]) : fallback;
        }

        internal static bool IsBlockItem(string id) { return id=="oak_log" || id=="oak_planks" || id=="stone" || id=="cobblestone" || id=="stone_bricks" || id=="pumpkin" || id=="melon" || id=="black_shulker_box"; }
        private static Quaternion MinecraftDisplayRotation(Vector3 degrees)
        { return Quaternion.AngleAxis(-degrees.x,Vector3.right)*Quaternion.AngleAxis(-degrees.y,Vector3.up)*Quaternion.AngleAxis(degrees.z,Vector3.forward); }

        private void ApplyDisplay(Transform target, DisplayTransform display, bool firstPerson, string id)
        {
            Vector3 scale = display == null ? Vector3.one : ArrayVector(display.scale, Vector3.one);
            Vector3 source=display==null?Vector3.zero:ArrayVector(display.translation,Vector3.zero);
            Quaternion rotation=MinecraftDisplayRotation(display==null?Vector3.zero:ArrayVector(display.rotation,Vector3.zero));
            float units=16f*PixelSize/V("item_world_size");
            target.localScale=scale*units*(firstPerson?V("item_view_scale"):1f);
            if(firstPerson)
            {
                viewDisplayTranslation=new Vector3(source.x,source.y,-source.z)*PixelSize;
                viewDisplayRotation=rotation;
                target.localPosition=new Vector3(V("mc_hand_x"),V("mc_hand_y"),V("mc_hand_z"))*16f*PixelSize+viewDisplayTranslation;
                target.localRotation=rotation;
            }
            else
            {
                // Exact native right-arm item layer, converted into this avatar's
                // positive geometry basis; source translation locates model center.
                target.localPosition=new Vector3((slimArms?0.5f:1f)+source.x,-10f+source.z,2f+source.y)*PixelSize;
                target.localRotation=Quaternion.AngleAxis(90f,Vector3.right)*rotation;
            }
        }

        private Mesh BlockItemMesh(string id)
        {
            Mesh mesh=GenerateBlockMesh(id,V("item_world_size"));meshes.Add(mesh);return mesh;
        }
        public static Mesh GenerateBlockMesh(string id,float size,bool reward=false)
        {
            float h=size*0.5f;
            Vector3 min=Vector3.one*-h,max=Vector3.one*h;
            var builder=new MeshBuilder();
            int tiles=reward?4:3;
            BlockItemFace(builder,new Vector3(min.x,min.y,max.z),new Vector3(max.x,min.y,max.z),max,new Vector3(min.x,max.y,max.z),0,V("front_shade"),tiles);
            BlockItemFace(builder,new Vector3(max.x,min.y,min.z),min,new Vector3(min.x,max.y,min.z),new Vector3(max.x,max.y,min.z),0,V("back_shade"),tiles);
            BlockItemFace(builder,min,new Vector3(min.x,min.y,max.z),new Vector3(min.x,max.y,max.z),new Vector3(min.x,max.y,min.z),reward?3:0,V("left_shade"),tiles);
            BlockItemFace(builder,new Vector3(max.x,min.y,max.z),new Vector3(max.x,min.y,min.z),new Vector3(max.x,max.y,min.z),max,reward?3:0,V("right_shade"),tiles);
            BlockItemFace(builder,new Vector3(min.x,max.y,max.z),max,new Vector3(max.x,max.y,min.z),new Vector3(min.x,max.y,min.z),1,V("top_shade"),tiles);
            BlockItemFace(builder,min,new Vector3(max.x,min.y,min.z),new Vector3(max.x,min.y,max.z),new Vector3(min.x,min.y,max.z),2,V("bottom_shade"),tiles);
            return builder.Build("CultCraft Minecraft centered block "+id);
        }

        private static void BlockItemFace(MeshBuilder builder,Vector3 a,Vector3 b,Vector3 c,Vector3 d,int tile,float shade,int tiles)
        {
            float low=(float)tile/tiles,high=(float)(tile+1)/tiles;
            builder.Face(a,b,c,d,new Vector2(0f,low),new Vector2(1f,low),new Vector2(1f,high),new Vector2(0f,high),shade);
        }

        private Mesh ExtrudeItem(Texture2D texture, string id, Vector2? grip = null)
        {
            Mesh mesh=GenerateItemMesh(texture,id,V("item_world_size"),PixelSize*V("item_half_depth"),grip);
            meshes.Add(mesh);return mesh;
        }

        public static Mesh GenerateItemMesh(Texture2D texture,string id,float size,float halfDepth,Vector2? gripPoint=null)
        {
            int width = texture.width;
            int height = texture.height;
            Color32[] pixels = texture.GetPixels32();
            var builder = new MeshBuilder();
            float px = size / width;
            float py = size / height;
            // Vanilla contextual transforms operate on a centered generated model.
            Vector2 grip = gripPoint ?? new Vector2(0.5f, 0.5f);
            int threshold = Mathf.RoundToInt(V("geometry_alpha_threshold") * Byte.MaxValue);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a < threshold) continue;
                    float left = (x - width * grip.x) * px;
                    float right = left + px;
                    float bottom = (y - height * grip.y) * py;
                    float top = bottom + py;
                    Vector2 uv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                    builder.SolidFace(new Vector3(left, bottom, halfDepth), new Vector3(right, bottom, halfDepth), new Vector3(right, top, halfDepth), new Vector3(left, top, halfDepth), uv, V("front_shade"));
                    builder.SolidFace(new Vector3(right, bottom, -halfDepth), new Vector3(left, bottom, -halfDepth), new Vector3(left, top, -halfDepth), new Vector3(right, top, -halfDepth), uv, V("back_shade"));
                    if (x == 0 || pixels[y * width + x - 1].a < threshold) builder.SolidFace(new Vector3(left, bottom, -halfDepth), new Vector3(left, bottom, halfDepth), new Vector3(left, top, halfDepth), new Vector3(left, top, -halfDepth), uv, V("left_shade"));
                    if (x == width - 1 || pixels[y * width + x + 1].a < threshold) builder.SolidFace(new Vector3(right, bottom, halfDepth), new Vector3(right, bottom, -halfDepth), new Vector3(right, top, -halfDepth), new Vector3(right, top, halfDepth), uv, V("right_shade"));
                    if (y == 0 || pixels[(y - 1) * width + x].a < threshold) builder.SolidFace(new Vector3(left, bottom, -halfDepth), new Vector3(right, bottom, -halfDepth), new Vector3(right, bottom, halfDepth), new Vector3(left, bottom, halfDepth), uv, V("bottom_shade"));
                    if (y == height - 1 || pixels[(y + 1) * width + x].a < threshold) builder.SolidFace(new Vector3(left, top, halfDepth), new Vector3(right, top, halfDepth), new Vector3(right, top, -halfDepth), new Vector3(left, top, -halfDepth), uv, V("top_shade"));
                }
            }
            Mesh mesh = builder.Build("CultCraft Minecraft generated item " + id);
            return mesh;
        }

        private static void AddBox(MeshBuilder builder, Vector3 center, Vector3 size, int w, int h, int d, int u, int v, bool sparse, bool mirrorU, bool swapSides,Texture2D skin)
        {
            Vector3 min = center - size * 0.5f;
            Vector3 max = center + size * 0.5f;
            AddSkinFace(builder, new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), u + d, v + d, w, h, sparse, V("front_shade"), mirrorU,skin);
            AddSkinFace(builder, new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), u + d + w + d, v + d, w, h, sparse, V("back_shade"), mirrorU,skin);
            AddSkinFace(builder, new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), swapSides ? u + d + w : u, v + d, d, h, sparse, V("left_shade"), mirrorU,skin);
            AddSkinFace(builder, new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), swapSides ? u : u + d + w, v + d, d, h, sparse, V("right_shade"), mirrorU,skin);
            AddSkinFace(builder, new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), u + d, v, w, d, sparse, V("top_shade"), mirrorU,skin);
            AddSkinFace(builder, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), u + d + w, v, w, d, sparse, V("bottom_shade"), mirrorU,skin);
        }

        private static void AddSkinFace(MeshBuilder builder, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int u, int v, int width, int height, bool sparse, float shade, bool mirrorU,Texture2D skin)
        {
            float skinSize = V("skin_size");
            if (!sparse)
            {
                float leftU = (mirrorU ? u + width : u) / skinSize;
                float rightU = (mirrorU ? u : u + width) / skinSize;
                builder.Face(a, b, c, d, new Vector2(leftU, 1f - (v + height) / skinSize), new Vector2(rightU, 1f - (v + height) / skinSize), new Vector2(rightU, 1f - v / skinSize), new Vector2(leftU, 1f - v / skinSize), shade);
                return;
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sourceX = mirrorU ? width - 1 - x : x;
                    if (skin.GetPixel(u + sourceX, (int)skinSize - 1 - (v + y)).a < V("geometry_alpha_threshold")) continue;
                    float left = (float)x / width;
                    float right = (float)(x + 1) / width;
                    float bottom = 1f - (float)(y + 1) / height;
                    float top = 1f - (float)y / height;
                    Vector3 pa = Vector3.Lerp(Vector3.Lerp(a, b, left), Vector3.Lerp(d, c, left), bottom);
                    Vector3 pb = Vector3.Lerp(Vector3.Lerp(a, b, right), Vector3.Lerp(d, c, right), bottom);
                    Vector3 pc = Vector3.Lerp(Vector3.Lerp(a, b, right), Vector3.Lerp(d, c, right), top);
                    Vector3 pd = Vector3.Lerp(Vector3.Lerp(a, b, left), Vector3.Lerp(d, c, left), top);
                    builder.SolidFace(pa, pb, pc, pd, new Vector2((u + sourceX + 0.5f) / skinSize, 1f - (v + y + 0.5f) / skinSize), shade);
                }
            }
        }

        private Vector3 PartPosition(string id)
        {
            AvatarRow row = PartRow(id);
            return new Vector3(row.PivotX, row.PivotY, row.PivotZ) * PixelSize;
        }

        private void ResetPartPositions()
        {
            body.localPosition = PartPosition("torso");
            head.localPosition = PartPosition("head") - PartPosition("torso");
            rightArm.localPosition = PartPosition("right_arm") - PartPosition("torso");
            leftArm.localPosition = PartPosition("left_arm") - PartPosition("torso");
            rightLeg.localPosition = PartPosition("right_leg");
            leftLeg.localPosition = PartPosition("left_leg");
        }

        private void LowerTorso(float amount)
        {
            Vector3 drop = Vector3.up * amount;
            body.localPosition -= drop;
            rightLeg.localPosition -= drop; leftLeg.localPosition -= drop;
        }

        private void ApplyCrouch(float amount)
        {
            float bend=Mathf.SmoothStep(0f,1f,amount);
            rightLeg.localRotation=leftLeg.localRotation=Quaternion.identity;
            body.localPosition+=new Vector3(0f,-V("crouch_drop")*0.75f,-PixelSize*0.7f)*bend;
            body.localRotation=Quaternion.Euler(28f*bend,0f,0f);
            head.localRotation=Quaternion.Inverse(body.localRotation);
            rightArm.localRotation=Quaternion.Euler(3f*bend,0f,V("arm_rest_roll"));
            leftArm.localRotation=Quaternion.Euler(3f*bend,0f,-V("arm_rest_roll"));
        }

        private float SoleHeight(Transform leg)
        {
            Bounds bounds=leg.GetComponent<MeshFilter>().sharedMesh.bounds;
            float lowest=float.PositiveInfinity;
            for(int x=0;x<2;x++)
                for(int z=0;z<2;z++)
                {
                    Vector3 corner=new Vector3(x==0?bounds.min.x:bounds.max.x,bounds.min.y,z==0?bounds.min.z:bounds.max.z);
                    lowest=Mathf.Min(lowest,Root.InverseTransformPoint(leg.TransformPoint(corner)).y);
                }
            return lowest;
        }

        private void ApplySleep(PlayerFarming owner,string clip,float phase)
        {
            bool waking=clip.StartsWith("sleeping-stop",StringComparison.Ordinal);
            bool movingAtBed=waking||clip.StartsWith("sleeping-start",StringComparison.Ordinal);
            float bedPhase=waking?1f-Mathf.InverseLerp(0.06f,1f,phase):phase;
            if(!waking){wakeExitSearched=false;wakeExitFound=false;}
            if(!sleepBedSearched)
            {
                sleepBedSearched=true;
                float distance=1.65f*1.65f;
                foreach(Interaction interaction in Interaction.interactions)
                {
                    if(!(interaction is Interaction_Bed bed)||bed.Structure==null||bed.Structure.Structure_Info==null||
                        !bed.gameObject.activeInHierarchy)continue;
                    float candidate=((Vector2)(bed.transform.position-owner.transform.position)).sqrMagnitude;
                    if(candidate>=distance)continue;
                    foreach(BuildingRow design in Spec.BuildingRows)
                    {
                        if(design.NativeType!=(int)bed.Structure.Structure_Info.Type||
                            (design.Model!="bed"&&design.Model!="straw_bed"))continue;
                        distance=candidate;sleepBed=bed.Structure;sleepBedDesign=design;break;
                    }
                }
            }
            Transform root=worldObject.transform;
            Quaternion reclined=root.rotation*Quaternion.Euler(-90f,0f,0f);
            Vector3 anchor=root.position, target=anchor;
            if(sleepBed!=null&&StationVisuals.TryBedPose(sleepBed,sleepBedDesign,out Vector3 bedOrigin,out Quaternion bedRotation))
            {
                SleepOnBed=true;
                Vector3 pillow=bedOrigin+bedRotation*BlockModels.BedPillow(sleepBedDesign);
                reclined=Quaternion.LookRotation(-(bedRotation*Vector3.forward),Vector3.back)*Quaternion.Euler(-90f,0f,0f);
                target=pillow+Vector3.back*(4f*PixelSize)-reclined*(Vector3.up*28f*PixelSize*root.localScale.y);
                if(movingAtBed)
                {
                    float height=sleepBedDesign.Height*(sleepBedDesign.Model=="straw_bed"?0.8f:1f);
                    if(waking&&!wakeExitSearched)
                    {
                        wakeExitSearched=true;
                        wakeExitFound=TryLeaveBed(owner,target,reclined,bedOrigin,bedRotation);
                        if(wakeExitFound)
                        {
                            Vector3 exit=owner.transform.position;
                            anchor=new Vector3(exit.x,exit.y,anchor.z);
                            lastPosition=exit;
                        }
                    }
                    MoveAtBedEdge(root,anchor,target,reclined,bedOrigin,bedRotation,sleepBedDesign.Width,height,
                        bedPhase,waking&&wakeExitFound);
                    return;
                }
            }
            if(movingAtBed)
            {
                MoveAtBedEdge(root,anchor,target,reclined,anchor,root.rotation,0.55f,0f,bedPhase);
                return;
            }
            root.SetPositionAndRotation(target,reclined);
            body.localRotation=head.localRotation=Quaternion.identity;
            rightLeg.localRotation=leftLeg.localRotation=Quaternion.identity;
            rightArm.localRotation=Quaternion.Euler(-9f,0f,5f);
            leftArm.localRotation=Quaternion.Euler(-9f,0f,-5f);
            KeepAboveGround(anchor.z);
        }

        private bool TryLeaveBed(PlayerFarming owner,Vector3 target,Quaternion reclined,Vector3 bedOrigin,Quaternion bedRotation)
        {
            var feet=owner.GetComponent<CircleCollider2D>();
            var room=BiomeBaseManager.Instance?.Room;
            if(feet==null||room==null||PlayerFarming.Location!=FollowerLocation.Base)return false;
            float radius=feet.radius*Mathf.Max(Mathf.Abs(feet.transform.lossyScale.x),Mathf.Abs(feet.transform.lossyScale.y));
            Vector3 hip=target+reclined*(PartPosition("torso")*Root.localScale.y);
            Vector3 bedRight=bedRotation*Vector3.right;
            Vector3 from=owner.transform.position;
            float preferred=sleepEntrySide!=0f?sleepEntrySide:Vector3.Dot(from-bedOrigin,bedRight)<0f?-1f:1f;
            var filter=new ContactFilter2D {useTriggers=false};
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(feet.gameObject.layer));
            int supported=0;
            string blocker="";
            for(int side=0;side<2;side++)
            {
                float sign=side==0?preferred:-preferred;
                for(int step=0;step<3;step++)
                {
                    Vector3 exit=new Vector3(hip.x,hip.y,from.z)+bedRight*(sign*(sleepBedDesign.Width*0.5f+radius+0.08f+step*0.13f));
                    Vector2 center=(Vector2)(exit+feet.transform.TransformVector(feet.offset));
                    bool floor=false;
                    foreach(var piece in room.Pieces)floor|=piece.Collider!=null&&piece.Collider.OverlapPoint(center);
                    if(!floor&&PlacementRegion.Instance!=null)
                    {
                        var tile=PlacementRegion.Instance.GetFreeClosestTileGridTileAtWorldPosition(exit);
                        floor=tile!=null&&Vector2.Distance(tile.WorldPosition,exit)<0.55f;
                    }
                    if(!floor)continue;
                    supported++;
                    int hits=Physics2D.OverlapCircle(center,radius,filter,wakeObstacles);
                    bool blocked=hits==wakeObstacles.Length;
                    for(int i=0;i<hits&&!blocked;i++)
                    {
                        Collider2D hit=wakeObstacles[i];
                        if(hit==null||hit.transform.IsChildOf(owner.transform))continue;
                        bool ground=false;
                        foreach(var piece in room.Pieces)ground|=piece.Collider==hit;
                        blocked=!ground;
                        if(blocked)blocker=hit.name;
                    }
                    if(blocked)continue;
                    owner.transform.position=exit;
                    sleepEntrySide=sign;
                    return true;
                }
            }
            Debug.Log("CultCraft bed exit: no free candidate, supported="+supported+", last blocker="+blocker+", radius="+radius);
            return false;
        }

        private void MoveAtBedEdge(Transform root,Vector3 anchor,Vector3 target,Quaternion reclined,
            Vector3 bedOrigin,Quaternion bedRotation,float width,float mattressHeight,float phase,bool fromOutside=false)
        {
            Vector3 up=Vector3.back;
            Vector3 bedRight=bedRotation*Vector3.right;
            if(sleepEntrySide==0f)sleepEntrySide=Vector3.Dot(anchor-bedOrigin,bedRight)<0f?-1f:1f;
            Vector3 outward=bedRight*sleepEntrySide;
            Quaternion upright=root.rotation;
            Quaternion seated=Quaternion.LookRotation(outward,up);
            Vector3 pelvis=PartPosition("torso")*root.localScale.y;
            Vector3 restHip=target+reclined*pelvis;
            Vector3 mattressCenter=new Vector3(restHip.x,restHip.y,bedOrigin.z);
            Vector3 edgeHip=mattressCenter+outward*(width*0.41f)+up*(mattressHeight+2f*PixelSize*root.localScale.y);
            Vector3 standHip=fromOutside?anchor+up*pelvis.y:
                mattressCenter+outward*(width*0.5f+2f*PixelSize)+up*pelvis.y;
            float approach=SleepStage(phase,0f,0.24f);
            float sit=SleepStage(phase,0.21f,0.52f);
            float settle=SleepStage(phase,0.52f,0.98f);
            Vector3 hip=Vector3.Lerp(Vector3.Lerp(anchor+upright*pelvis,standHip,approach),edgeHip,sit);
            hip=Vector3.Lerp(hip,restHip,settle);
            Quaternion rotation=Quaternion.Slerp(Quaternion.Slerp(upright,seated,approach),reclined,settle);
            root.SetPositionAndRotation(hip-rotation*pelvis,rotation);

            float perch=sit*(1f-settle);
            float seatLeg=Mathf.Acos(Mathf.Clamp01((mattressHeight+PixelSize)/pelvis.y))*Mathf.Rad2Deg;
            float step=Mathf.Sin(approach*Mathf.PI)*18f;
            float lead=Mathf.Sin(SleepStage(phase,0.52f,0.89f)*Mathf.PI);
            float follow=Mathf.Sin(SleepStage(phase,0.62f,0.98f)*Mathf.PI);
            rightLeg.localRotation=Quaternion.Euler(step-seatLeg*perch-43f*lead,0f,0f);
            leftLeg.localRotation=Quaternion.Euler(-step-seatLeg*perch-43f*follow,0f,0f);
            body.localRotation=Quaternion.Euler(13f*perch,0f,sleepEntrySide*8f*Mathf.Sin(settle*Mathf.PI));
            head.localRotation=Quaternion.Euler(-8f*perch,0f,0f);
            rightArm.localRotation=Quaternion.Euler(-9f*settle+13f*perch,0f,5f*settle+12f*perch);
            leftArm.localRotation=Quaternion.Euler(-9f*settle+7f*perch,0f,-5f*settle-14f*perch);
            KeepAboveGround(anchor.z);
        }

        private static float SleepStage(float phase,float from,float to)
        {return Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(from,to,phase));}

        private static bool IsCombatGesture(StateMachine.State state)
        {
            return state==StateMachine.State.Casting||state==StateMachine.State.Defending||
                state==StateMachine.State.Aiming||state==StateMachine.State.ChargingHeavyAttack||
                state==StateMachine.State.Charging||state==StateMachine.State.Grapple;
        }

        private static bool IsScriptedPose(StateMachine.State state,string clip)
        {
            if(String.IsNullOrEmpty(clip)||clip=="idle"||clip=="idle2"||
                clip.StartsWith("idle-",StringComparison.Ordinal)||clip.StartsWith("run",StringComparison.Ordinal)||
                clip.StartsWith("walk",StringComparison.Ordinal))return false;
            return IsCombatGesture(state)||state==StateMachine.State.CustomAnimation||state==StateMachine.State.TimedAction||
                state==StateMachine.State.CustomAction0||state==StateMachine.State.Worshipping||state==StateMachine.State.Sleeping||
                state==StateMachine.State.FoundItem||state==StateMachine.State.Building||state==StateMachine.State.Dancing||
                state==StateMachine.State.Idle_CarryingBody||state==StateMachine.State.Moving_CarryingBody||
                state==StateMachine.State.ChargingSnowball||state==StateMachine.State.Reeling||state==StateMachine.State.Heal||
                state==StateMachine.State.KnockBack||state==StateMachine.State.HitThrown||
                state==StateMachine.State.CoopReviving||state==StateMachine.State.KnockedOut||
                state==StateMachine.State.Grabbed||state==StateMachine.State.TiedToAltar;
        }

        private static string ActionItem(string clip)
        {
            switch(clip)
            {
                case "actions/shear":return "shears";
                case "drink-good":case "drink-bad":return "glass_bottle";
                case "give-item/gift-small":return "black_bundle";
                case "give-item/gift-medium":return "black_shulker_box";
                default:return "";
            }
        }

        private void KeepAboveGround(float ground)
        {
            float lowest=ground;
            foreach(var part in bodyPartRenderers)lowest=Mathf.Max(lowest,part.bounds.max.z);
            worldObject.transform.position+=Vector3.back*(lowest-ground);
        }

        private CeremonyFrame CaptureCeremony(Vector3 anchor)
        {
            return new CeremonyFrame
            {
                Offset=worldObject.transform.position-anchor,
                RightShoulder=rightArm.localPosition,LeftShoulder=leftArm.localPosition,
                Body=body.localRotation,Head=head.localRotation,
                RightArm=rightArm.localRotation,LeftArm=leftArm.localRotation,
                RightLeg=rightLeg.localRotation,LeftLeg=leftLeg.localRotation
            };
        }

        private void BlendCeremony(string clip,Vector3 anchor)
        {
            if(ceremonyClip!=clip)
            {
                ceremonyFrom=ceremonyDrawn;
                ceremonyClip=clip;
                ceremonyMix=0f;
            }
            var target=CaptureCeremony(anchor);
            ceremonyMix=Mathf.Min(1f,ceremonyMix+Mathf.Min(Time.unscaledDeltaTime,0.05f)/0.12f);
            if(ceremonyMix>=1f)
            {
                ceremonyDrawn=target;
                if(clip.Length==0)ceremonyClip=null;
                return;
            }
            float weight=Mathf.SmoothStep(0f,1f,ceremonyMix);
            ceremonyDrawn=new CeremonyFrame
            {
                Offset=Vector3.Lerp(ceremonyFrom.Offset,target.Offset,weight),
                RightShoulder=Vector3.Lerp(ceremonyFrom.RightShoulder,target.RightShoulder,weight),
                LeftShoulder=Vector3.Lerp(ceremonyFrom.LeftShoulder,target.LeftShoulder,weight),
                Body=Quaternion.Slerp(ceremonyFrom.Body,target.Body,weight),
                Head=Quaternion.Slerp(ceremonyFrom.Head,target.Head,weight),
                RightArm=Quaternion.Slerp(ceremonyFrom.RightArm,target.RightArm,weight),
                LeftArm=Quaternion.Slerp(ceremonyFrom.LeftArm,target.LeftArm,weight),
                RightLeg=Quaternion.Slerp(ceremonyFrom.RightLeg,target.RightLeg,weight),
                LeftLeg=Quaternion.Slerp(ceremonyFrom.LeftLeg,target.LeftLeg,weight)
            };
            worldObject.transform.position=anchor+ceremonyDrawn.Offset;
            rightArm.localPosition=ceremonyDrawn.RightShoulder;
            leftArm.localPosition=ceremonyDrawn.LeftShoulder;
            body.localRotation=ceremonyDrawn.Body;
            head.localRotation=ceremonyDrawn.Head;
            rightArm.localRotation=ceremonyDrawn.RightArm;
            leftArm.localRotation=ceremonyDrawn.LeftArm;
            rightLeg.localRotation=ceremonyDrawn.RightLeg;
            leftLeg.localRotation=ceremonyDrawn.LeftLeg;
        }

        private void SetDamageTint(bool hurt)
        {
            if (skinMaterial == null) return;
            // Keep the same depth-writing shader during damage; tint a cached texture in RAM.
            skinMaterial.mainTexture = hurt ? (hurtSkin ?? assets.TintedTexture("steve", DamageTintColor)) : SkinTexture;
        }

        private void RestoreLamb()
        {
            if (lambRenderer != null) lambRenderer.enabled = lambWasEnabled;
            lambRenderer = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cinematicPose.Dispose();
            carriedFollower.Dispose();
            if(hurtSkin!=null)UnityEngine.Object.Destroy(hurtSkin);
            feedback.Dispose();
            fishingBinding.Dispose();
            fishingVisuals.Dispose();
            bookBinding.Dispose();
            if (worldBook != null) worldBook.Dispose();
            if (firstPersonBook != null) firstPersonBook.Dispose();
            RestoreLamb();
            if (worldObject != null) UnityEngine.Object.Destroy(worldObject);
            if (viewObject != null) UnityEngine.Object.Destroy(viewObject);
            foreach (Mesh mesh in meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            foreach (Material material in materials) if (material != null) UnityEngine.Object.Destroy(material);
            meshes.Clear();
            materials.Clear();
            renderers.Clear();
            bodyPartRenderers.Clear();
        }

        internal sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> indices = new List<int>();
            private readonly List<Color> colors = new List<Color>();

            public void SolidFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 sample, float shade)
            {
                Face(a, b, c, d, sample, sample, sample, sample, shade);
            }

            public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td, float shade)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(ta); uv.Add(tb); uv.Add(tc); uv.Add(td);
                Color color = new Color(shade, shade, shade, 1f);
                colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh();
                mesh.name = name;
                if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
