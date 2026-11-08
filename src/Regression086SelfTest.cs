using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Lamb.UI.Assets;
using MMTools;
using TMPro;
using UnityEngine;

namespace CultCraft
{
    internal sealed class Regression086SelfTest
    {
        private readonly MinecraftAvatar avatar;
        private readonly MinecraftAssets assets;
        private readonly Action<string,bool,string> report;
        private readonly PlayerAnimationAudit audit;
        private bool started;
        private bool recording;
        private string evidenceDirectory;
        private static PlayerFarming carryingProbe;
        internal bool Complete { get; private set; }

        internal Regression086SelfTest(MinecraftAvatar avatar, MinecraftAssets assets, Action<string,bool,string> report)
        {
            this.avatar = avatar; this.assets = assets; this.report = report;
            if(Array.Exists(Spec.TestRows,row=>row.Id=="minecraft-animation-catalog"))
                audit=new PlayerAnimationAudit(avatar,report);
        }

        internal void PrepareAnimationAudit(PlayerFarming player)=>audit?.Prepare(player);
        internal void DrawAnimationAuditLabel()=>audit?.DrawLabel();

        internal void Tick(PlayerFarming player, bool ready)
        {
            if (started || !ready) return;
            started = true;
            player.StartCoroutine(Run(player));
        }

        private IEnumerator Run(PlayerFarming player)
        {
            var camera = CameraFollowTarget.Instance;
            float distance = camera.targetDistance, min = camera.MinZoom, max = camera.MaxZoom;
            bool look = camera.DisablePlayerLook;
            Vector3 clearing = Clearing(player);
            player.transform.position = clearing;
            camera.targetDistance = camera.MinZoom = camera.MaxZoom = 6.4f;
            camera.DisablePlayerLook = true;
            try
            {
                yield return new WaitForSecondsRealtime(2f);
                evidenceDirectory=Path.Combine(Path.GetTempPath(),"CultCraft-audit-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(evidenceDirectory);
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"CultCraft-latest-audit.txt"),evidenceDirectory);
                player.transform.position=clearing+Vector3.down*3f;
                yield return new WaitForSecondsRealtime(0.75f);
                recording = true;
                player.StartCoroutine(RecordFrames("rewards",()=>recording));
                yield return CheckCrouch(player);
                player.transform.position=clearing;
                yield return CheckHarvest(player, StructureBrain.TYPES.TREE, "stone_axe", new Vector3(-1.35f,0.75f,0f));
                player.transform.position = clearing;
                yield return CheckHarvest(player, StructureBrain.TYPES.RUBBLE, "stone_pickaxe", new Vector3(1.35f,0.65f,0f));
                player.transform.position = clearing;
                yield return CheckCorpse(player);
                player.transform.position = clearing + Vector3.down * 3f;
                yield return new WaitForSecondsRealtime(0.75f);
                yield return CheckRewards(player);
                recording=false;
                yield return CheckSleep(player);
                if(audit!=null)
                    yield return audit.Run(player,evidenceDirectory,()=>player.StartCoroutine(RecordFrames("animations",()=>audit.Showcase)));
                Complete = true;
            }
            finally
            {
                recording = false;
                if (camera != null)
                {
                    camera.targetDistance = distance; camera.MinZoom = min; camera.MaxZoom = max;
                    camera.DisablePlayerLook = look;
                }
            }
        }

        private IEnumerator CheckCrouch(PlayerFarming player)
        {
            float facing=player.state.facingAngle;
            player.state.facingAngle=220f;
            int sounds=assets.SoundsPlayed;
            int draws=avatar.SkinRenderFrames;
            MeltyPlay.Hold("b",true);
            try
            {
                yield return Wait(()=>avatar.AnimationPose=="crouch"&&avatar.CrouchWeight>=0.95f,4f);
            }
            finally {MeltyPlay.Hold("b",false);}
            bool planted=avatar.CrouchWeight>=0.8f&&avatar.FootGroundError<0.025f&&avatar.SkinRenderFrames>draws;
            float contact=avatar.FootGroundError;
            var joints=new Quaternion[7];
            avatar.ReadAuditPose(joints);
            bool referencePose=Quaternion.Angle(joints[1],Quaternion.identity)>25f&&
                Quaternion.Angle(joints[1]*joints[2],Quaternion.identity)<2f&&
                Quaternion.Angle(joints[5],Quaternion.identity)<1f&&Quaternion.Angle(joints[6],Quaternion.identity)<1f;
            yield return Wait(()=>avatar.CrouchWeight<=0.001f,2f);
            bool sound=assets.SoundsPlayed>sounds&&assets.LastSoundPlayback=="hurt_classic: FMOD channel playing";
            report("minecraft-bleat-crouch",planted&&sound&&referencePose&&avatar.CrouchWeight<=0.001f,
                "Native B input, straight legs and level head="+referencePose+", classic sound="+sound+
                ", floor error="+contact.ToString("F4")+", eased return="+(avatar.CrouchWeight<=0.001f));
            player.state.facingAngle=facing;
            yield return new WaitForSecondsRealtime(0.3f);
        }

        private IEnumerator CheckSleep(PlayerFarming player)
        {
            Vector3 before=player.transform.position;
            GameObject fixture=null;
            StructuresData design=StructuresData.GetInfoByType(StructureBrain.TYPES.BED_2,0);
            StructureManager.BuildStructure(FollowerLocation.Base,design,before+new Vector3(3f,2.5f,0f),
                new Vector2Int(Math.Max(1,design.TILE_WIDTH),Math.Max(1,design.TILE_HEIGHT)),false,
                created=>fixture=created,null,false);
            yield return Wait(()=>fixture!=null,8f);
            yield return null;
            var bed=fixture==null?null:fixture.GetComponentInChildren<Interaction_Bed>();
            if(bed==null){report("minecraft-bed-sleep",false,"Native bed fixture unavailable.");yield break;}
            player.transform.position=bed.transform.position+new Vector3(0.09f,0.211f,-0.02f);
            player.state.facingAngle=180f;
            bool running=true;
            player.StartCoroutine(RecordFrames("sleep",()=>running));
            player.CustomAnimation("sleeping-start",Loop:false);
            var joints=new Quaternion[7];
            bool sat=false,swung=false,finishedEntry=false;
            float until=Time.unscaledTime+4f;
            while(Time.unscaledTime<until)
            {
                yield return new WaitForEndOfFrame();
                if(avatar.AnimationPose!="sleep/sleeping-start"||!avatar.SleepOnBed)continue;
                avatar.ReadAuditPose(joints);
                float upright=Vector3.Dot(avatar.Root.up,Vector3.back);
                sat|=upright>0.95f&&Quaternion.Angle(joints[5],Quaternion.identity)>22f&&
                    Quaternion.Angle(joints[6],Quaternion.identity)>22f;
                swung|=upright<0.9f&&Quaternion.Angle(joints[5],joints[6])>12f;
                var track=player.Spine.AnimationState.GetCurrent(0);
                if(track==null||track.TrackTime<track.Animation.Duration*0.985f)continue;
                finishedEntry=true;
                break;
            }
            Vector3 entryPosition=avatar.Root.position;
            Quaternion entryRotation=avatar.Root.rotation;
            player.CustomAnimation("sleeping",Loop:true);
            yield return Wait(()=>avatar.AnimationPose=="sleep/sleeping"&&avatar.SleepOnBed&&
                Mathf.Abs(Vector3.Dot(avatar.Root.up,Vector3.back))<0.05f,4f);
            bool onBed=avatar.SleepOnBed&&avatar.AnimationPose=="sleep/sleeping"&&
                Mathf.Abs(Vector3.Dot(avatar.Root.up,Vector3.back))<0.05f;
            Transform torso=avatar.Root.Find("Torso");
            Transform head=torso.Find("Head");
            BuildingRow redBed=Spec.Building("bed_2");
            bool pillowAligned=false;
            if(StationVisuals.TryBedPose(bed.Structure,redBed,out Vector3 bedOrigin,out Quaternion bedRotation))
            {
                Vector3 pillow=bedOrigin+bedRotation*BlockModels.BedPillow(redBed);
                Bounds headBounds=head.GetComponent<MeshRenderer>().bounds;
                pillowAligned=Vector2.Distance(headBounds.center,pillow)<0.04f&&Mathf.Abs(headBounds.max.z-pillow.z)<0.035f;
            }
            avatar.ReadAuditPose(joints);
            bool restored=Quaternion.Angle(joints[1],Quaternion.identity)<1f&&Quaternion.Angle(joints[2],Quaternion.identity)<1f&&
                Quaternion.Angle(joints[5],Quaternion.identity)<1f&&Quaternion.Angle(joints[6],Quaternion.identity)<1f;
            bool joined=finishedEntry&&Vector3.Distance(entryPosition,avatar.Root.position)<0.04f&&
                Quaternion.Angle(entryRotation,avatar.Root.rotation)<3f;
            yield return new WaitForSecondsRealtime(0.9f);
            Vector3 sleepingPosition=avatar.Root.position;
            Quaternion sleepingRotation=avatar.Root.rotation;
            running=false;
            bool recordingWake=true;
            player.StartCoroutine(RecordFrames("wake",()=>recordingWake));
            player.CustomAnimation("sleeping-stop",Loop:false);
            bool sawWake=false,wakeJoined=false,wakeSat=false,wakeSwung=false,stood=false,outside=false;
            until=Time.unscaledTime+4f;
            while(Time.unscaledTime<until)
            {
                yield return new WaitForEndOfFrame();
                if(avatar.AnimationPose!="sleep/sleeping-stop"||!avatar.SleepOnBed)continue;
                if(!sawWake)
                {
                    sawWake=true;
                    wakeJoined=Vector3.Distance(sleepingPosition,avatar.Root.position)<0.04f&&
                        Quaternion.Angle(sleepingRotation,avatar.Root.rotation)<3f;
                }
                avatar.ReadAuditPose(joints);
                float upright=Vector3.Dot(avatar.Root.up,Vector3.back);
                wakeSat|=upright>0.95f&&Quaternion.Angle(joints[5],Quaternion.identity)>22f&&
                    Quaternion.Angle(joints[6],Quaternion.identity)>22f;
                wakeSwung|=upright<0.9f&&Quaternion.Angle(joints[5],joints[6])>12f;
                var track=player.Spine.AnimationState.GetCurrent(0);
                if(track==null||track.TrackTime<track.Animation.Duration*0.985f)continue;
                stood=upright>0.995f&&avatar.FootGroundError<0.025f&&
                    Vector2.Distance(avatar.Root.position,player.transform.position)<0.04f;
                outside=Mathf.Abs(Vector3.Dot(player.transform.position-bedOrigin,bedRotation*Vector3.right))>
                    redBed.Width*0.5f;
                break;
            }
            report("minecraft-bed-sleep",onBed&&pillowAligned&&sat&&swung&&restored&&joined&&
                wakeJoined&&wakeSat&&wakeSwung&&stood&&outside,
                "Native bed="+onBed+", sat on edge="+sat+", legs swing separately="+swung+", continuous landing="+joined+
                ", previous sleeping pose="+restored+", head on pillow="+pillowAligned+", continuous wake="+wakeJoined+
                ", wakes through seat="+wakeSat+", legs swing separately on wake="+wakeSwung+", feet planted after wake="+stood+
                ", player and avatar exit the bed footprint="+outside);
            player.state.CURRENT_STATE=StateMachine.State.Idle;
            player.Spine.AnimationState.SetAnimation(0,"idle",true);
            yield return new WaitForSecondsRealtime(0.35f);
            recordingWake=false;
            player.transform.position=before;
        }

        private static Vector3 Clearing(PlayerFarming player)
        {
            var trees = UnityEngine.Object.FindObjectsOfType<Interaction_Woodcutting>();
            var ground = BiomeBaseManager.Instance.Room.Pieces[0].Collider;
            Vector3 best = new Vector3(0f,-6.5f,player.transform.position.z);
            float bestClearance = 0f;
            for (int y = -11; y <= -5; y += 2)
                for (int x = -6; x <= 6; x += 3)
                {
                    var candidate = new Vector3(x,y,player.transform.position.z);
                    if (!ground.OverlapPoint(candidate)) continue;
                    float clearance = float.PositiveInfinity;
                    foreach (var tree in trees) clearance = Mathf.Min(clearance,Vector2.Distance(candidate,tree.transform.position));
                    if (clearance > bestClearance) { best = candidate; bestClearance = clearance; }
                }
            return best;
        }

        private IEnumerator RecordFrames(string segment,Func<bool> active)
        {
            // Called only in the save-isolated test. The recording is local
            // and stops with the scenario or at the fixed time limit.
            string directory = Path.Combine(evidenceDirectory,segment);
            Directory.CreateDirectory(directory);
            Debug.Log("CultCraft gameplay recording: " + directory);
            float start = Time.realtimeSinceStartup, next = start;
            var endOfFrame = new WaitForEndOfFrame();
            while (active() && Time.realtimeSinceStartup-start < 50f)
            {
                yield return endOfFrame;
                float now = Time.realtimeSinceStartup;
                if (now < next) continue;
                next = Mathf.Max(next+1f/12f,now);
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null) continue;
                try
                {
                    int timestamp = Mathf.RoundToInt((now-start)*1000f);
                    File.WriteAllBytes(Path.Combine(directory,timestamp.ToString("D6")+".jpg"),frame.EncodeToJPG(88));
                }
                finally { UnityEngine.Object.Destroy(frame); }
            }
        }


        private IEnumerator CheckHarvest(PlayerFarming player, StructureBrain.TYPES type, string item, Vector3 offset)
        {
            GameObject fixture = null;
            StructuresData design = StructuresData.GetInfoByType(type, 0);
            if (type == StructureBrain.TYPES.TREE) { design.GrowthStage = 5f; design.IsSapling = false; }
            StructureManager.BuildStructure(FollowerLocation.Base, design, player.transform.position + offset,
                new Vector2Int(Math.Max(1,design.TILE_WIDTH),Math.Max(1,design.TILE_HEIGHT)), false, created => fixture = created, null, false);
            yield return Wait(() => fixture != null, 10f);
            yield return null;
            Interaction interaction = fixture == null ? null : type == StructureBrain.TYPES.TREE
                ? (Interaction)fixture.GetComponentInChildren<Interaction_Woodcutting>()
                : fixture.GetComponentInChildren<Interaction_PlayerClearRubble>();
            string check = type == StructureBrain.TYPES.TREE ? "minecraft-tree-tool" : "minecraft-stone-tool";
            if (interaction == null) { report(check, false, "Native harvest fixture was unavailable."); yield break; }
            int draws = avatar.HeldRenderFrames;
            interaction.OnInteract(player.state);
            yield return Wait(() => avatar.HeldItemId == item && avatar.HeldRenderFrames > draws, 8f);
            bool drawn = avatar.HeldItemId == item && avatar.HeldRenderFrames > draws;
            report(check, drawn, "Actual settlement interaction: expected " + item + ", held " + avatar.HeldItemId +
                ", pose " + avatar.AnimationPose + ", track " + player.Spine.AnimationState.GetCurrent(0)?.Animation?.Name);
            yield return new WaitForSecondsRealtime(4f);
            if (player.state.CURRENT_STATE == StateMachine.State.CustomAnimation) player.state.CURRENT_STATE = StateMachine.State.Idle;
            yield return new WaitForSecondsRealtime(1.5f);
        }

        private IEnumerator CheckCorpse(PlayerFarming player)
        {
            GameObject fixture = null;
            FollowerInfo dead = FollowerInfo.NewCharacter(FollowerLocation.Base);
            DataManager.Instance.Followers_Dead.Add(dead);
            DataManager.Instance.OnboardedDeadFollower = true;
            StructuresData design = StructuresData.GetInfoByType(StructureBrain.TYPES.DEAD_WORSHIPPER, 0);
            design.FollowerID = dead.ID;
            design.BodyWrapped = true;
            StructureManager.BuildStructure(FollowerLocation.Base, design, player.transform.position + Vector3.down * 1.35f,
                Vector2Int.one, false, created => fixture = created, null, false);
            yield return Wait(() => fixture != null, 10f);
            yield return null;
            var interaction = fixture == null ? null : fixture.GetComponentInChildren<Interaction_HarvestMeat>();
            if (interaction == null) { report("minecraft-carry-follower",false,"Native wrapped follower fixture was unavailable."); yield break; }
            int draws = avatar.CarriedFollowerDraws;
            interaction.OnInteract(player.state);
            player.state.facingAngle = Utils.GetAngle(player.transform.position,player.transform.position+Vector3.down);
            yield return Wait(() => avatar.CarryHandsRaised && avatar.CarriedFollowerDraws >= draws + 4, 8f);
            bool carried = player.CarryingDeadFollowerID == dead.ID && avatar.CarryHandsRaised &&
                avatar.HeldItemId.Length == 0 && avatar.CarriedFollowerDraws >= draws + 4;
            Indicator indicator = player.indicator;
            bool cleanPrompt = false;
            if (carried && indicator != null && indicator.Thirdtext != null && indicator.Fourthtext != null)
            {
                indicator.Thirdtext.text = indicator.Fourthtext.text = Gameplay.NativePromptPlaceholder;
                yield return Wait(() => indicator.Thirdtext.text.Length == 0 && indicator.Fourthtext.text.Length == 0, 2f);
                yield return new WaitForEndOfFrame();
                var label = indicator.text.rectTransform;
                var canvas = label.GetComponentInParent<Canvas>();
                Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 bottom = RectTransformUtility.WorldToScreenPoint(uiCamera,label.TransformPoint(new Vector3(label.rect.center.x,label.rect.yMin)));
                cleanPrompt = indicator.Thirdtext.text.Length == 0 && indicator.Fourthtext.text.Length == 0 &&
                    indicator.text.text == I2.Loc.ScriptLocalization.Interactions.Drop &&
                    bottom.y > Screen.height - Hotbar.ScreenRect.y;
            }
            yield return new WaitForSecondsRealtime(3f);
            bool walked = false;
            if (carried)
            {
                var input = new Harmony("cultcraft.test.086-carry-motion");
                var axis = AccessTools.Method(typeof(RewiredGameplayInputSource),"GetVerticalAxis",new[] { typeof(PlayerFarming) });
                var postfix = AccessTools.Method(typeof(Regression086SelfTest),nameof(WalkWithBody));
                input.Patch(axis,postfix:new HarmonyMethod(postfix));
                carryingProbe = player;
                Vector3 start = player.transform.position;
                try
                {
                    yield return Wait(() => player.state.CURRENT_STATE == StateMachine.State.Moving_CarryingBody &&
                        Vector2.Distance(start,player.transform.position)>0.4f && avatar.CarryHandsRaised, 1.5f);
                    walked = Vector2.Distance(start,player.transform.position)>0.4f && avatar.CarryHandsRaised;
                }
                finally { carryingProbe = null; input.UnpatchSelf(); }
            }
            yield return new WaitForSecondsRealtime(3f);
            if (Interaction_HarvestMeat.CurrentMovingBody == interaction)
            {
                // End the test through the game's actual drop path.
                interaction.StopAllCoroutines();
                player.DropDeadFollower();
            }
            yield return Wait(() => player.CarryingDeadFollowerID < 0 && !avatar.CarryHandsRaised, 6f);
            report("minecraft-carry-follower",carried && walked && cleanPrompt && player.CarryingDeadFollowerID < 0 && !avatar.CarryHandsRaised,
                "Actual pickup, walking=" + walked + " and drop, both hands raised, clean Drop prompt=" + cleanPrompt +
                ", wrapped follower camera draws=" + (avatar.CarriedFollowerDraws - draws));
        }

        private static void WalkWithBody(PlayerFarming __0,ref float __result)
        {
            if (carryingProbe != null && (__0 ?? PlayerFarming.Instance) == carryingProbe) __result = -0.4f;
        }

        private IEnumerator CheckRewards(PlayerFarming player)
        {
            var mappingField = AccessTools.Field(typeof(InventoryIconMapping), "_itemImages");
            var icons = new Dictionary<InventoryItem.ITEM_TYPE,Sprite>();
            foreach (var mapping in Resources.FindObjectsOfTypeAll<InventoryIconMapping>())
                if (mappingField.GetValue(mapping) is List<InventoryIconMapping.ItemSpritePair> pairs)
                    foreach (var pair in pairs) if (pair.Sprite != null && !icons.ContainsKey(pair.ItemType)) icons.Add(pair.ItemType,pair.Sprite);
            InventoryItem.ITEM_TYPE gift = InventoryItem.ITEM_TYPE.GIFT_MEDIUM;
            var groups = new[] {
                new[] { InventoryItem.ITEM_TYPE.LOG, gift, InventoryItem.ITEM_TYPE.STONE },
                new[] { InventoryItem.ITEM_TYPE.LOG_REFINED, InventoryItem.ITEM_TYPE.PUMPKIN, InventoryItem.ITEM_TYPE.STONE_REFINED },
                new[] { InventoryItem.ITEM_TYPE.BERRY, InventoryItem.ITEM_TYPE.HOPS, gift },
                new[] { InventoryItem.ITEM_TYPE.GIFT_SMALL, InventoryItem.ITEM_TYPE.MEAT, InventoryItem.ITEM_TYPE.SEED }
            };
            bool passed=true;
            var confirmed=new List<string>();
            foreach(var types in groups)
                yield return CheckRewardGroup(player,types,icons,good=>passed&=good,confirmed);
            report("minecraft-gift-reward",passed,"Actual reveal and game camera verified "+String.Join(", ",confirmed));
        }

        private IEnumerator CheckRewardGroup(PlayerFarming player,InventoryItem.ITEM_TYPE[] types,
            Dictionary<InventoryItem.ITEM_TYPE,Sprite> icons,Action<bool> result,List<string> confirmed)
        {
            var gift=InventoryItem.ITEM_TYPE.GIFT_MEDIUM;
            foreach (var type in types)
                if (!icons.ContainsKey(type)) { result(false); yield break; }
            DataManager.Instance.ShowLoyaltyBars = true;
            var rewards = new GameObject("CultCraft test: native three-choice rewards");
            rewards.SetActive(false);
            var renderers = new SpriteRenderer[types.Length];
            SpriteRenderer giftRenderer = null;
            int draws = 0;
            for (int i = 0; i < types.Length; i++)
            {
                var optionObject = new GameObject("Reward " + types[i]);
                optionObject.transform.SetParent(rewards.transform,false);
                optionObject.transform.position = player.transform.position + new Vector3((i - 1) * 1.08f, 0.95f, -1.1f);
                var option = optionObject.AddComponent<SingleChoiceRewardOption>();
                option.StartHidden = true;
                option.itemOptions = new List<BuyEntry> { new BuyEntry { itemToBuy = types[i], quantity = types[i]==gift?1:10, SingleQuantityItem = types[i]==gift } };
                var art = new GameObject("Native item display");
                art.transform.SetParent(optionObject.transform,false);
                art.transform.rotation = Camera.main.transform.rotation;
                var renderer = art.AddComponent<SpriteRenderer>();
                renderers[i] = renderer;
                renderer.sortingOrder = 25;
                var display = art.AddComponent<InventoryItemDisplay>();
                Sprite native = icons[types[i]];
                AccessTools.Field(typeof(InventoryItemDisplay),"ItemImages").SetValue(display,new List<InventoryItemDisplay.MyDictionaryEntry>
                    { new InventoryItemDisplay.MyDictionaryEntry { key = types[i], value = native } });
                AccessTools.Field(typeof(SingleChoiceRewardOption),"itemDisplay").SetValue(option,display);
                AccessTools.Field(typeof(SingleChoiceRewardOption),"otherOptions").SetValue(option,new SingleChoiceRewardOption[0]);
                AccessTools.Field(typeof(SingleChoiceRewardOption),"interaction").SetValue(option,optionObject.AddComponent<Interaction_PickUpLoot>());
                var label = new GameObject("Native quantity");
                label.transform.SetParent(optionObject.transform,false);
                label.transform.localPosition = Vector3.down * 0.37f;
                label.transform.rotation = Camera.main.transform.rotation;
                option.QuantityText = label.AddComponent<TextMeshPro>();
                option.QuantityText.fontSize = 1.5f;
                option.QuantityText.alignment = TextAlignmentOptions.Center;
                if (types[i] == gift)
                {
                    giftRenderer = renderer;
                    art.AddComponent<BookRenderProbe>().Observed = drawn => { if (drawn == Camera.main) draws++; };
                }
            }
            rewards.SetActive(true);
            yield return null;
            foreach (var option in rewards.GetComponentsInChildren<SingleChoiceRewardOption>(true)) option.Reveal();
            yield return new WaitForSecondsRealtime(2f);
            bool passed=true;
            for(int index=0;index<types.Length;index++)
            {
                var type=types[index];
                string id=PickupVisuals.ResourceTexture(type);
                float nativeSize=Mathf.Min(icons[type].bounds.size.x,icons[type].bounds.size.y);
                var cube=PickupVisuals.Current.DroppedFor(renderers[index]);
                bool good;
                if(type==gift)
                {
                    Sprite shown=giftRenderer.sprite;
                    var faces=assets.HeldBlockTexture("black_shulker_box");
                    bool closed=faces!=null;
                    if(closed)
                        for(int y=0;y<32;y++)for(int x=0;x<32;x++)closed&=faces.GetPixel(x,y).a>0.99f;
                    good=shown!=null&&shown.name=="CultCraft fitted black shulker reward"&&shown.texture.width==128&&
                        shown.bounds.size.x<=nativeSize+0.01f&&shown.bounds.size.y<=nativeSize+0.01f&&draws>3&&closed;
                }
                else if(MinecraftAvatar.IsBlockItem(id))
                    good=cube!=null&&cube.IsCube&&cube.NativeArtSuppressed&&cube.GameDrawFrames>3&&
                        cube.transform.parent==renderers[index].transform&&cube.transform.localScale.x*Mathf.Sqrt(3f)<=Mathf.Min(nativeSize,0.49f)+0.001f&&
                        cube.CubeAtlasMatches(renderers[index].sprite.texture,assets.RewardBlockTexture(id));
                else
                {
                    Sprite shown=renderers[index].sprite;
                    Bounds native=icons[type].bounds;
                    good=cube==null&&shown!=null&&shown.texture==assets.Texture(id)&&
                        shown.bounds.size.x<=native.size.x+0.01f&&shown.bounds.size.y<=native.size.y+0.01f&&
                        Vector2.Distance(shown.bounds.center,native.center)<0.02f;
                }
                passed&=good;
                confirmed.Add(type+"="+good);
            }
            result(passed);
            yield return new WaitForSecondsRealtime(3f);
            UnityEngine.Object.Destroy(rewards);
            yield return null;
        }

        private static IEnumerator Wait(Func<bool> condition,float seconds)
        {
            float until=Time.unscaledTime+seconds;
            while(!condition()&&Time.unscaledTime<until)yield return null;
        }
    }
}
