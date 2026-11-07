using System;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    // Exercise the real cleaning clip and all saved level values in the isolated test world.
    internal sealed class BroomSelfTest : IDisposable
    {
        private readonly MinecraftAvatar avatar;
        private readonly CameraRig camera;
        private readonly ManualLogSource logger;
        private readonly Action<string, bool, string> report;
        private PlayerFarming owner;
        private DataManager data;
        private int originalLevel, stage, drawsBefore;
        private float originalXp, since;
        private bool sawOld, sawSwap, sawLoop, sawStop;
        public bool Complete { get; private set; }

        public BroomSelfTest(MinecraftAvatar avatar, CameraRig camera, ManualLogSource logger,
            Action<string, bool, string> report)
        {
            this.avatar = avatar;
            this.camera = camera;
            this.logger = logger;
            this.report = report;
        }

        public void Tick(PlayerFarming player, bool ready)
        {
            if (Complete) return;
            if (owner == null)
            {
                if (!ready || player.state.CURRENT_STATE != StateMachine.State.Idle || !player.isLamb) return;
                owner = player;
                data = DataManager.Instance;
                originalLevel = data.ChoreXPLevel;
                originalXp = data.ChoreXP;
                if (camera.FirstPerson) camera.Toggle();
                BeginStage();
                owner.CustomAnimation("cleaning", true);
                return;
            }
            if (stage == 11)
            {
                CheckUpgrade();
                return;
            }

            int level = Mathf.Min(stage, 9);
            int draws = avatar.BroomDraws - drawsBefore;
            bool shown = avatar.AnimationPose == "sweep" && avatar.HeldItemId == BroomVisual.ForLevel(level).Id &&
                draws >= 2 && (stage != 10 || camera.Active && avatar.ViewItemOnRight && avatar.ViewArmHidden);
            float elapsed = Time.unscaledTime - since;
            if (!shown && elapsed > 4f)
            {
                Finish(false, "Stage " + stage + ", pose=" + avatar.AnimationPose + ", item=" + avatar.HeldItemId + ", draws=" + draws);
                return;
            }
            if (!shown || elapsed < 0.85f) return;
            logger.LogInfo("Native broom clip rendered: stage=" + stage + ", item=" + avatar.HeldItemId + ", camera draws=" + draws);
            stage++;
            BeginStage();
            if (stage == 10) camera.Toggle();
            if (stage == 11)
            {
                if (camera.FirstPerson) camera.Toggle();
                owner.state.CURRENT_STATE = StateMachine.State.Idle;
                data.ChoreXPLevel = 8;
                data.ChoreXP = DataManager.TargetChoreXP[8] - 1f;
                owner.SetSkin();
                owner.playerChoreXPBarController.AddChoreXP(owner);
            }
        }

        private void CheckUpgrade()
        {
            var track = owner.Spine.AnimationState.GetCurrent(0);
            string clip = track?.Animation?.Name;
            if (avatar.BroomDraws > drawsBefore && avatar.AnimationPose == "broom-presentation")
            {
                bool oldModel = data.ChoreXPLevel == 8 && avatar.HeldItemId == "broom_wheat";
                bool newModel = data.ChoreXPLevel == 9 && avatar.HeldItemId == "broom_brush";
                if (clip == "Mop/upgrade-mop-start")
                {
                    sawOld |= oldModel;
                    sawSwap |= sawOld && newModel;
                }
                sawLoop |= clip == "Mop/upgrade-mop-loop" && newModel;
                sawStop |= clip == "Mop/upgrade-mop-stop" && newModel;
            }
            drawsBefore = avatar.BroomDraws;

            if (sawOld && sawSwap && sawLoop && sawStop && owner.state.CURRENT_STATE == StateMachine.State.Idle)
            {
                logger.LogInfo("Native broom upgrade rendered: wheat -> brush on MopSwap, start/loop/stop, player returned to idle.");
                Finish(true, "Ten cleaning models, first-person cleaning, and native chore XP upgrade.");
            }
            else if (Time.unscaledTime - since > 9f)
            {
                Finish(false, "Native broom upgrade incomplete: old=" + sawOld + ", swap=" + sawSwap +
                    ", loop=" + sawLoop + ", stop=" + sawStop + ", level=" + data.ChoreXPLevel + ", clip=" + clip);
            }
        }

        private void BeginStage()
        {
            since = Time.unscaledTime;
            drawsBefore = avatar.BroomDraws;
            data.ChoreXPLevel = Mathf.Min(stage, 9);
        }

        private void Finish(bool passed, string detail)
        {
            Dispose();
            Complete = true;
            report("minecraft-broom", passed, passed ? null : detail);
        }

        public void Dispose()
        {
            if (owner == null) return;
            data.ChoreXPLevel = originalLevel;
            data.ChoreXP = originalXp;
            if (owner != null) owner.SetSkin();
            if (owner.state.CURRENT_STATE == StateMachine.State.CustomAnimation)
                owner.state.CURRENT_STATE = StateMachine.State.Idle;
            if (camera.FirstPerson) camera.Toggle();
            owner = null;
        }
    }
}
