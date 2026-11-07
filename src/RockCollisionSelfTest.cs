using System;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    // Place the isolated test actor just outside the fixture, then approach it
    // through the normal controller/physics. No obstacle or collider is changed.
    internal sealed class RockCollisionSelfTest : IDisposable
    {
        private readonly CameraRig camera;
        private readonly ManualLogSource logger;
        private PlayerFarming owner;
        private RockPileFit.Footprint footprint;
        private Vector3 originalPosition, start;
        private float originalFacing, since, bestGap = Single.PositiveInfinity;
        private bool originalView, feetEntered;
        private Vector3 previous;
        private float stoppedFor;
        public bool Complete { get; private set; }
        public bool Passed { get; private set; }
        public string Detail { get; private set; }
        public RockCollisionSelfTest(CameraRig camera, ManualLogSource logger) { this.camera = camera; this.logger = logger; }

        public void Tick(PlayerFarming player, Structure rock, bool ready)
        {
            if (Complete) return;
            if (owner == null)
            {
                if (!ready || player.circleCollider2D == null || !player.circleCollider2D.enabled) return;
                footprint = RockPileFit.BlockingFootprint(rock.transform);
                if (footprint == null) { Finish(false, "Native stone has no blocking footprint."); return; }
                owner = player; originalPosition = player.transform.position; originalFacing = player.state.facingAngle; originalView = camera.FirstPerson;
                Vector3 center = footprint.Bounds.center;
                float front = footprint.Bounds.min.y;
                for (float y = front; y <= footprint.Bounds.max.y; y += 0.025f)
                    if (footprint.Contains(new Vector2(center.x, y))) { front = y; break; }
                start = new Vector3(center.x, front - 0.9f, originalPosition.z);
                // Other real settlement buildings can border a randomized
                // fixture. Choose a clear approach lane; never remove them or
                // mistake their collision for contact with the target stone.
                for(int lane=0;lane<9;lane++)
                {
                    float x=center.x+(lane==0?0f:((lane+1)/2)*(lane%2==1?1f:-1f)*0.35f);
                    float boundary=footprint.Bounds.min.y;bool found=false;
                    for(float y=boundary;y<=footprint.Bounds.max.y;y+=0.025f)
                        if(footprint.Contains(new Vector2(x,y))){boundary=y;found=true;break;}
                    if(!found)continue;
                    Vector2 candidate=new Vector2(x,boundary-0.9f);bool clear=true;
                    foreach(RaycastHit2D hit in Physics2D.CircleCastAll(candidate,0.3f,Vector2.up,0.9f))
                    {
                        Collider2D collider=hit.collider;if(collider==null||collider.isTrigger||collider.transform.IsChildOf(player.transform))continue;
                        if(Array.IndexOf(footprint.Colliders,collider)<0){clear=false;break;}
                    }
                    if(clear){start=new Vector3(x,boundary-0.9f,originalPosition.z);break;}
                }
                player.transform.position = start;
                Physics2D.SyncTransforms();
                if (!camera.FirstPerson) camera.Toggle();
                previous = start;
                since = Time.unscaledTime;
                return;
            }
            if (camera.Active && camera.TestLookAt(footprint.Bounds.center)) camera.BeginTestInput(Vector2.up);
            foreach (Collider2D collider in footprint.Colliders)
            {
                ColliderDistance2D gap = owner.circleCollider2D.Distance(collider);
                if (gap.isValid) bestGap = Mathf.Min(bestGap, gap.distance);
            }
            // The game's circle includes a larger service/damage radius than
            // the movement contact. Test the planted feet and an actual sustained
            // stop under forward input, rather than that service circle's overlap.
            feetEntered |= footprint.Contains(owner.transform.position);
            float delta = Vector2.Distance(previous, owner.transform.position);
            stoppedFor = delta < 0.015f && Time.unscaledTime - since > 0.3f ? stoppedFor + Time.unscaledDeltaTime : 0f;
            previous = owner.transform.position;
            if (Time.unscaledTime - since < 1.8f) return;
            float footGap = Single.PositiveInfinity;
            foreach (Collider2D collider in footprint.Colliders)
                footGap = Mathf.Min(footGap, Vector2.Distance(owner.transform.position, collider.ClosestPoint(owner.transform.position)));
            float walked = Vector2.Distance(start, owner.transform.position);
            // Contact is measured against the actual player collider, whose
            // offset varies with native pose/facing. A fixed foot-gap cutoff
            // incorrectly rejects legitimate contact with a sloped polygon.
            Finish(walked > 0.15f && bestGap <= 0.02f && stoppedFor >= 0.3f && !feetEntered,
                "Native walk into stone: moved=" + walked.ToString("F3") + ", real player-circle/obstruction gap=" + bestGap.ToString("F3") + ", foot gap=" + footGap.ToString("F3") + ", stopped under input=" + stoppedFor.ToString("F2") + ", feet entered=" + feetEntered);
        }
        private void Finish(bool passed, string detail)
        {
            Complete = true; Passed = passed; Detail = detail;
            logger.LogInfo("Stone collision proof: " + detail); Dispose();
        }
        public void Dispose()
        {
            if (owner == null) return;
            camera.EndTestInput();
            owner.transform.position = originalPosition; owner.state.facingAngle = originalFacing;
            Physics2D.SyncTransforms();
            if (camera.FirstPerson != originalView) camera.Toggle();
            owner = null;
        }
    }
}
