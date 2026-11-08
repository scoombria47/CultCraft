using System;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace CultCraft
{
    // Snapshot the completed native Spine pose on its renderer callback, just as
    // the book binding does. Never touch the animation, events or gameplay bones.
    internal sealed class FishingAnimationBinding : IDisposable
    {
        private SkeletonAnimation source;
        private Skeleton skeleton;
        private Bone handR, handL, shoulderR, shoulderL, tool, back, head;
        private Vector2 right, left;
        private float toolAngle, backAngle, headAngle;
        public string Clip { get; private set; } = "";
        public float Time { get; private set; }
        public bool Active { get; private set; }
        public int Revision { get; private set; }
        public Vector3 RodDirection { get; private set; } = Vector3.up;
        public Vector3 RodTip { get; private set; }
        public float GripError { get; private set; }

        public void Bind(PlayerFarming player)
        {
            SkeletonAnimation next = player == null ? null : player.Spine;
            if (next == source && (next == null || ReferenceEquals(skeleton, next.Skeleton))) return;
            if (!ReferenceEquals(source, null)) source.OnMeshAndMaterialsUpdated -= Capture;
            source = next; skeleton = next == null ? null : next.Skeleton; Active = false;
            if (skeleton == null) return;
            handR = skeleton.FindBone("HAND_RIGHT"); handL = skeleton.FindBone("HAND_LEFT");
            shoulderR = skeleton.FindBone("SHOULDER_RIGHT"); shoulderL = skeleton.FindBone("SHOULDER_LEFT");
            tool = skeleton.FindBone("TOOL"); back = skeleton.FindBone("BACK_BTM"); head = skeleton.FindBone("HEAD");
            source.OnMeshAndMaterialsUpdated += Capture;
        }

        internal void Capture(SkeletonRenderer renderer)
        {
            TrackEntry track = source.AnimationState == null ? null : source.AnimationState.GetCurrent(0);
            Clip = track?.Animation?.Name ?? "";
            Active = Clip.StartsWith("Fishing/fishing-", StringComparison.Ordinal) &&
                handR != null && handL != null && shoulderR != null && shoulderL != null && tool != null;
            if (!Active) return;
            Time = track.AnimationTime;
            // Undo the native sprite mirror. Avatar root already supplies facing.
            float flip = skeleton.ScaleX < 0f ? -1f : 1f;
            right = new Vector2((handR.WorldX - shoulderR.WorldX) * flip, handR.WorldY - shoulderR.WorldY);
            left = new Vector2((handL.WorldX - shoulderL.WorldX) * flip, handL.WorldY - shoulderL.WorldY);
            toolAngle = tool.WorldRotationX;
            backAngle = back == null ? 105f : back.WorldRotationX;
            headAngle = head == null ? 83f : head.WorldRotationX;
            Revision++;
        }

        public void Apply(Transform root, Transform torso, Transform steveHead, Transform rightArm,
            Transform leftArm, Transform rightLeg, Transform leftLeg)
        {
            if (!Active) return;
            // All native fishing clips take place standing on the bank. Native
            // squash and flat leg kicks must not unplant either rigid Steve leg.
            rightLeg.localRotation = leftLeg.localRotation = Quaternion.identity;
            torso.localRotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(105f, backAngle) * 0.32f, -9f, 12f), 0f, 0f);
            steveHead.localRotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(83f, headAngle) * 0.45f + 10f, -25f, 30f), 0f, 0f);
            // Native Lamb carries the rod in HAND_LEFT. Retarget that motion to
            // Steve's right hand, preserving our established right-hand layout.
            PoseArm(rightArm, left, torso, true);
            PoseArm(leftArm, right, torso, false);
            float elevation = 36f + Mathf.Clamp(Mathf.DeltaAngle(106f, toolAngle), -110f, 155f) * 0.75f;
            float radians = elevation * Mathf.Deg2Rad;
            RodDirection = new Vector3(-0.12f, Mathf.Sin(radians), Mathf.Cos(radians)).normalized;
        }

        private static void PoseArm(Transform arm, Vector2 native, Transform torso, bool rod)
        {
            Vector2 direction = native.sqrMagnitude > 0.001f ? native.normalized : Vector2.down;
            // In 2D both hands extend sideways towards the water. Lift that
            // reach into the forward hemisphere so arms never sweep through the chest.
            float y = Mathf.Clamp(direction.y * 0.85f, -0.70f, 0.88f);
            float x = (rod ? -0.22f : 0.40f) + direction.x * 0.05f;
            float z = Mathf.Sqrt(Mathf.Max(0.10f, 1f - x * x - y * y));
            Quaternion aim = Quaternion.FromToRotation(Vector3.down, new Vector3(x, y, z).normalized);
            float twist = Mathf.Clamp(Vector2.SignedAngle(Vector2.left, direction) * 0.25f, -25f, 25f);
            arm.localRotation = Quaternion.Inverse(torso.localRotation) * aim * Quaternion.AngleAxis(twist, Vector3.down);
        }

        public void PlaceRod(Transform root, Transform arm, Transform item, float meshSize)
        {
            Vector3 palm = arm.TransformPoint(new Vector3(MinecraftAvatar.PalmX(arm,true), -9f, 1f) * MinecraftAvatar.PixelSize);
            // Keep the generated model centered so both source display contexts
            // work. Locate its handle texel explicitly instead of gripping its center.
            Vector3 grip = new Vector3(3f / 16f - 0.5f, 2f / 16f - 0.5f, 0f) * meshSize;
            Vector3 shaft = new Vector3(10.5f, 12.5f, 0f).normalized;
            Quaternion align = Quaternion.AngleAxis(Vector3.SignedAngle(shaft, Vector3.up, Vector3.forward), Vector3.forward);
            // Roll around the shaft so the uncast texture's hanging string
            // lies below the rod. This does not alter its grip, tip or FPS display.
            Vector3 normal = Vector3.ProjectOnPlane(new Vector3(-0.8f, 0f, 0.6f), RodDirection).normalized;
            item.rotation = root.rotation * Quaternion.LookRotation(normal, RodDirection) * align;
            item.localScale = Vector3.one * 1.25f;
            item.position = palm - item.TransformVector(grip);
            GripError = Vector3.Distance(palm, item.TransformPoint(grip));
            RodTip = item.TransformPoint(new Vector3(13.5f / 16f - 0.5f, 14.5f / 16f - 0.5f, 0f) * meshSize);
        }

        public void Dispose()
        {
            if (!ReferenceEquals(source, null)) source.OnMeshAndMaterialsUpdated -= Capture;
            source = null; skeleton = null; Active = false;
        }
    }
}
