using System;
using BepInEx.Logging;
using HarmonyLib;
using MMTools;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CultCraft
{
    // Only constructed by the save-isolated, marker-gated Melty test.
    internal sealed class FishingSelfTest : IDisposable
    {
        private static FishingSelfTest current;
        private readonly MinecraftAvatar avatar;
        private readonly CameraRig camera;
        private readonly ManualLogSource logger;
        private readonly Action<string, bool, string> report;
        private readonly Harmony guard = new Harmony("cultcraft.test.fishing");
        private readonly string[] clips = {
            "Fishing/fishing-start", "Fishing/fishing-loop", "Fishing/fishing-reel", "Fishing/fishing-catch",
            "Fishing/fishing-start-side", "Fishing/fishing-loop-side", "Fishing/fishing-reel-side", "Fishing/fishing-catch-side",
            "Fishing/fishing-start", "Fishing/fishing-loop", "Fishing/fishing-reel"
        };
        private PlayerFarming owner;
        private bool initialized, travelling, returning;
        private Interaction_Fishing native;
        private Vector3 originalPosition;
        private float originalFacing, since, totalSince;
        private int step, drawsBefore, revisionBefore, bobberBefore, lineBefore, groundFrames;
        private Quaternion firstRight, firstLeft;
        private bool sampled, requested, castSeen;
        private float rightMotion, leftMotion, tipMotion, reelingSince = -1f;
        private Vector3 firstHook;
        public bool Complete { get; private set; }

        public FishingSelfTest(MinecraftAvatar avatar, CameraRig camera, ManualLogSource logger, Action<string, bool, string> report)
        {
            this.avatar = avatar; this.camera = camera; this.logger = logger; this.report = report;
        }

        public void Tick(PlayerFarming player, bool ready)
        {
            if (Complete) return;
            try
            {
                if (!initialized)
                {
                    if (!ready || player.state.CURRENT_STATE != StateMachine.State.Idle || PlayerFarming.Location != FollowerLocation.Base) return;
                    initialized = true;
                    owner = player; originalPosition = player.transform.position; originalFacing = player.state.facingAngle;
                    if (camera.FirstPerson) camera.Toggle();
                    totalSince = Time.unscaledTime;
                }
                if (step >= clips.Length && player != null) owner = player;
                if (Time.unscaledTime - totalSince > 75f) { Finish(false, "Fishing probe timeout: step=" + step + ", clip=" + avatar.AnimationPose + ", native=" + (native == null ? "absent" : native.States[0].ToString())); return; }
                if (step < clips.Length) { CheckClip(); return; }
                CheckCast();
            }
            catch (Exception error) { Finish(false, "Fishing native fixture: " + error); }
        }

        private void CheckClip()
        {
            if (!requested)
            {
                if (step == 8) camera.Toggle();
                if (owner.Spine.Skeleton.Data.FindAnimation(clips[step]) == null) { Finish(false, "Native fishing clip missing: " + clips[step]); return; }
                owner.CustomAnimation(clips[step], true);
                requested = true; since = Time.unscaledTime; drawsBefore = avatar.FishingDraws; revisionBefore = avatar.FishingRevision;
            }
            Transform root = avatar.Root;
            if (root != null && avatar.AnimationPose == "fishing/" + clips[step])
            {
                Transform rightLeg = root.Find(Spec.Avatar("right_leg").Label), leftLeg = root.Find(Spec.Avatar("left_leg").Label);
                if (Quaternion.Angle(rightLeg.localRotation, Quaternion.identity) > 0.05f || Quaternion.Angle(leftLeg.localRotation, Quaternion.identity) > 0.05f)
                { Finish(false, "Fishing unplanted the feet in " + clips[step]); return; }
                groundFrames++;
                Transform torso = root.Find(Spec.Avatar("torso").Label);
                Transform right = torso.Find(Spec.Avatar("right_arm").Label), left = torso.Find(Spec.Avatar("left_arm").Label);
                if (!sampled) { sampled = true; firstRight = right.localRotation; firstLeft = left.localRotation; }
                rightMotion = Mathf.Max(rightMotion, Quaternion.Angle(firstRight, right.localRotation));
                leftMotion = Mathf.Max(leftMotion, Quaternion.Angle(firstLeft, left.localRotation));
                if ((right.localRotation * Vector3.down).z < 0.12f || (left.localRotation * Vector3.down).z < 0.12f)
                { Finish(false, "Fishing arm reach lost its forward depth."); return; }
                if (avatar.FishingGripError > 0.002f) { Finish(false, "Rod detached from the palm."); return; }
            }
            if (Time.unscaledTime - since < 1.05f) return;
            bool firstPerson = step >= 8;
            bool shown = avatar.HeldItemId == "fishing_rod" && avatar.FishingDraws - drawsBefore >= 2 && avatar.FishingRevision > revisionBefore &&
                (!firstPerson || camera.Active && avatar.ViewArmHidden && avatar.ViewItemOnRight);
            if (!shown) { Finish(false, "Fishing clip render failed: " + clips[step] + ", item=" + avatar.HeldItemId + ", FPS=" + camera.Active + ", draws=" + (avatar.FishingDraws - drawsBefore)); return; }
            logger.LogInfo("Native fishing animation rendered: " + clips[step] + ", FPS=" + firstPerson + ", frames=" + (avatar.FishingDraws - drawsBefore));
            step++; requested = false;
            if (step == clips.Length)
            {
                if (camera.FirstPerson) camera.Toggle();
                owner.state.CURRENT_STATE = StateMachine.State.Idle;
                owner.Spine.AnimationState.SetAnimation(0, "idle", true);
                since = Time.unscaledTime;
            }
        }

        private void CheckCast()
        {
            if (step == clips.Length)
            {
                if (!requested)
                {
                    requested = true; since = Time.unscaledTime;
                    // FISHING_SPOT is a serialized shoreline structure; it has
                    // no build-menu prefab. Load the actual shore and use its
                    // real interaction, hook curves, water and minigame instead.
                    travelling = true;
                    DataManager.Instance.BroughtFishingRod = true;
                    logger.LogInfo("Fishing test travelling to actual native Hub-Shore.");
                    Travel("Hub-Shore");
                    return;
                }
                if (MMTransition.IsPlaying || PlayerFarming.Location != FollowerLocation.HubShore || owner == null || owner.GoToAndStopping || owner.state.CURRENT_STATE != StateMachine.State.Idle) return;
                native = Interaction_Fishing.Instance;
                if (native == null || !native.gameObject.activeInHierarchy) return;
                travelling = false;
                current = this;
                foreach (string name in new[] { "GetInteractButtonDown", "GetInteractButtonHeld", "GetInteractButtonUp", "GetCancelFishingButtonDown" })
                    guard.Patch(AccessTools.Method(typeof(RewiredGameplayInputSource), name, new[] { typeof(PlayerFarming) }), prefix: new HarmonyMethod(typeof(FishingSelfTest), "VirtualButton"));
                owner.transform.position = native.transform.position;
                native.OnInteract(owner.state);
                step++; since = Time.unscaledTime; return;
            }
            if (step == clips.Length + 1)
            {
                if (native.States[0] != StateMachine.State.Aiming) return;
                bobberBefore = avatar.FishingBobberDraws; lineBefore = avatar.FishingLineDraws;
                firstHook = native.FishingHooks[0].position;
                MeltyPlay.Hold("e", true);
                step++; since = Time.unscaledTime; return;
            }
            if (step == clips.Length + 2)
            {
                tipMotion = Mathf.Max(tipMotion, Vector3.Distance(firstHook, native.FishingHooks[0].position));
                castSeen |= native.States[0] == StateMachine.State.Casting;
                if (Time.unscaledTime - since > 0.8f) MeltyPlay.Hold("e", false);
                bool inWater = native.States[0] == StateMachine.State.Reeling || native.States[0] == StateMachine.State.Attacking;
                if (inWater && reelingSince < 0f) reelingSince = Time.unscaledTime;
                if (inWater && reelingSince >= 0f && Time.unscaledTime - reelingSince >= 2.5f && avatar.HeldItemId == "fishing_rod_cast" &&
                    avatar.FishingBobberDraws - bobberBefore >= 2 && avatar.FishingLineDraws - lineBefore >= 2 &&
                    avatar.FishingNativeArtHidden && avatar.FishingLineTipError < 0.002f && tipMotion > 1f && castSeen)
                {
                    logger.LogInfo("Native fishing cast: hook trajectory=" + tipMotion + ", Minecraft bobber camera draws=" + (avatar.FishingBobberDraws - bobberBefore) + ", line draws=" + (avatar.FishingLineDraws - lineBefore));
                    MeltyPlay.Hold("escape", true); step++; since = Time.unscaledTime;
                }
                return;
            }
            if (returning)
            {
                if (MMTransition.IsPlaying || PlayerFarming.Location != FollowerLocation.Base || owner == null || owner.GoToAndStopping || owner.state.CURRENT_STATE != StateMachine.State.Idle) return;
                returning = false; travelling = false;
                Finish(rightMotion > 15f && leftMotion > 15f && groundFrames >= 8,
                    "All eight native fishing clips, FPS rod, actual native Hub-Shore cast trajectory and cancel; planted frames=" + groundFrames + ", arm motion=" + rightMotion + "/" + leftMotion);
                return;
            }
            MeltyPlay.Hold("escape", false);
            if (native.States[0] != StateMachine.State.Idle) { MeltyPlay.Hold("escape", true); return; }
            if (Time.unscaledTime - since < 0.4f) return;
            guard.UnpatchSelf(); if (current == this) current = null;
            returning = true; travelling = true;
            Travel("Base Biome 1");
        }

        private static void Travel(string scene)
        {
            SimulationManager.Pause();
            MMTransition.Play(MMTransition.TransitionType.ChangeRoomWaitToResume, MMTransition.Effect.BlackFade, scene, 0.5f, "", null, null);
        }

        private static bool VirtualButton(PlayerFarming __0, System.Reflection.MethodBase __originalMethod, ref bool __result)
        {
            FishingSelfTest test = current;
            Keyboard keys = Keyboard.current;
            if (test == null || test.Complete || keys == null || (__0 != null && __0 != test.owner)) return true;
            switch (__originalMethod.Name)
            {
                case "GetInteractButtonDown": __result = keys.eKey.wasPressedThisFrame; break;
                case "GetInteractButtonHeld": __result = keys.eKey.isPressed; break;
                case "GetInteractButtonUp": __result = keys.eKey.wasReleasedThisFrame; break;
                default: __result = keys.escapeKey.wasPressedThisFrame; break;
            }
            return false;
        }
        private void Finish(bool passed, string detail)
        {
            Complete = true; logger.LogInfo("Fishing0.7.7 proof: " + detail);
            Dispose(); report("minecraft-fishing", passed, passed ? null : detail);
        }
        public void Dispose()
        {
            guard.UnpatchSelf(); if (current == this) current = null;
            MeltyPlay.Hold("e", false); MeltyPlay.Hold("escape", false);
            if (camera.FirstPerson) camera.Toggle();
            if (owner != null && !travelling && PlayerFarming.Location == FollowerLocation.Base)
            {
                owner.transform.position = originalPosition; owner.state.facingAngle = originalFacing;
                owner.state.CURRENT_STATE = StateMachine.State.Idle;
                owner.Spine.AnimationState.SetAnimation(0, "idle", true);
            }
            owner = null;
        }
    }
}
