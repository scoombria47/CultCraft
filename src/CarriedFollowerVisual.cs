using Spine.Unity;
using UnityEngine;

namespace CultCraft
{
    // A render-only copy of the native wrapped follower. The ground object and
    // the pickup, drop and burial logic remain owned by the game.
    internal sealed class CarriedFollowerVisual
    {
        private DeadWorshipper source;
        private SkeletonAnimation visual;
        private MeshFilter mesh;
        private MeshRenderer renderer;
        internal int Draws { get; private set; }

        internal void Tick(PlayerFarming player, Camera camera, Transform root, Vector3 palms, Renderer original)
        {
            var next = player == null ? null : Interaction_HarvestMeat.CurrentMovingBody?.DeadWorshipperTmp;
            if (next == null || next.StructureInfo == null || next.StructureInfo.FollowerID != player.CarryingDeadFollowerID || camera == null)
            {
                if (visual != null) visual.gameObject.SetActive(false);
                return;
            }
            if (next != source || visual == null)
            {
                Dispose();
                var native = next.Spine;
                if (native == null || native.Skeleton == null || native.Skeleton.Data.FindAnimation("corpse") == null) return;
                source = next;
                visual = SkeletonAnimation.NewSkeletonAnimationGameObject(native.skeletonDataAsset);
                visual.name = "CultCraft wrapped follower";
                visual.transform.SetParent(root, false);
                visual.gameObject.layer = native.gameObject.layer;
                if (native.Skeleton.Skin != null) visual.Skeleton.SetSkin(native.Skeleton.Skin);
                visual.Skeleton.SetSlotsToSetupPose();
                visual.AnimationState.SetAnimation(0, "corpse", true);
                mesh = visual.GetComponent<MeshFilter>();
                renderer = visual.GetComponent<MeshRenderer>();
                visual.gameObject.AddComponent<BookRenderProbe>().Observed = drawn =>
                {
                    if (drawn == Camera.main && drawn != null && drawn.cameraType == CameraType.Game) Draws++;
                };
            }

            visual.gameObject.SetActive(true);
            var transform = visual.transform;
            transform.rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, -8f);
            if (original != null)
            {
                renderer.sortingLayerID = original.sortingLayerID;
                renderer.sortingOrder = original.sortingOrder + 1;
            }
            if (mesh.sharedMesh == null || mesh.sharedMesh.bounds.size.x < 0.01f)
            {
                transform.position = palms;
                return;
            }
            Bounds bounds = mesh.sharedMesh.bounds;
            transform.localScale = Vector3.one * (0.91f / bounds.size.x);
            Vector3 baseCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            transform.position = palms + root.up * 0.035f - transform.TransformVector(baseCenter) - camera.transform.forward * 0.025f;
        }

        internal void Dispose()
        {
            if (visual != null) Object.Destroy(visual.gameObject);
            visual = null;
            source = null;
        }
    }
}
