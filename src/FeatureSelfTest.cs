using System;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using FMOD.Studio;

namespace CultCraft
{
    // Additional probes run only inside SelfTest's already isolated native world.
    internal sealed class FeatureSelfTest : IDisposable
    {
        private readonly MinecraftAssets assets;
        private readonly MinecraftAvatar avatar;
        private readonly CameraRig camera;
        private readonly ManualLogSource logger;
        private readonly Action<string,bool,string> report;
        private readonly AttackCooldownUi attackCooldown;
        private readonly AttackCooldownSelfTest attackProof;
        private readonly RepairSelfTest repairs;
        private readonly Animation074SelfTest animations074;
        private WeaponPresentationSelfTest pickupProof;
        private bool bookPassed,bookReported;
        private string bookDetail;
        private float settingsStarted=-1f,phaseSince;
        private float savedSensitivity,savedFov,changedSensitivity,changedFov;
        private int settingsPhase,initialSaves,initialChanges,initialFrames;
        private int savedScale, changedScale;
        private bool savedHints, changedHints;
        private bool settingsDone,settingsCaptured,openedWithF6;
        private readonly string[] bookAnimations={"sermons/sermon-start","sermons/sermon-loop","sermons/sermon-stop","reeducate-1","find-item/find-item-combined"};
        private readonly HashSet<string> observedBookTracks=new HashSet<string>(StringComparer.Ordinal);
        private int bookStep,bookDrawsBefore,bookRevisionBefore;
        private float bookStarted=-1f,bookStepStarted,bookMaxOpen;
        private bool bookRequested,bookFinished,bookSawClosed,bookSawOpen,bookStopHidden,earlyBookPose;
        private PlayerFarming bookOwner;
        private Quaternion bookFirstArm;
        private float bookArmMotion;
        private SkeletonAnimation sermonEffect;
        private float nextSermonRetry;
        private int voiceRequestsBefore,voiceStemsBefore,utterancesBefore,mouthBefore,pagesBefore,glyphDrawsBefore,incomingDrawsBefore,unknownStemsBefore;
        private EventInstance nativePreachLoop;
        private bool preachRequested,mixedRequested,bubbleRequested,preachStopped,glyphOutRequested,glyphOutComplete,glyphsObserved;
        public bool SettingsHotkeyArmed { get; private set; }
        public bool Complete { get { return settingsDone && BookComplete && repairs.Complete && animations074.Complete; } }
        public bool BookComplete { get; private set; }

        public FeatureSelfTest(MinecraftAssets assets,MinecraftAvatar avatar,CameraRig camera,ManualLogSource logger,Action<string,bool,string> report,AttackCooldownUi attackCooldown=null)
        {this.assets=assets;this.avatar=avatar;this.camera=camera;this.logger=logger;this.report=report;this.attackCooldown=attackCooldown;attackProof=new AttackCooldownSelfTest(attackCooldown,camera,avatar,report,logger);repairs=new RepairSelfTest(assets,logger);animations074=new Animation074SelfTest(avatar,logger);}

        public bool CheckAttacks(PlayerFarming player,Gameplay gameplay)
        {
            if(pickupProof==null)pickupProof=new WeaponPresentationSelfTest(avatar,logger);
            if(!pickupProof.Complete){pickupProof.Tick(player,gameplay);if(pickupProof.Complete&&!pickupProof.Passed)report("minecraft-attack-cooldown",false,pickupProof.Detail);return false;}
            return attackProof.Check(player,gameplay);
        }

        public void Tick(PlayerFarming player)
        {
            if(player==null||!player.gameObject.activeInHierarchy)return;
            if(!settingsDone){CheckSettings();return;}
            if(BookComplete)
            {
                repairs.Tick(player);
                if(repairs.Complete)animations074.Tick(player);
                if(repairs.Complete&&animations074.Complete&&!bookReported){bookReported=true;bool pass=bookPassed&&repairs.Passed&&animations074.Passed;report("minecraft-book-sermon",pass,pass?null:bookDetail+"; "+repairs.Detail+"; "+animations074.Detail);}
                return;
            }
            CheckBooks(player);
        }

        private void CheckBooks(PlayerFarming player)
        {
            if(BookComplete)return;
            if(bookStarted<0f)
            {
                if(player.Spine==null||player.Spine.Skeleton==null||player.state==null||PlayerFarming.Location!=FollowerLocation.Base)return;
                bookOwner=player;bookStarted=Time.unscaledTime;
                foreach(string name in bookAnimations)if(player.Spine.Skeleton.Data.FindAnimation(name)==null){FinishBooks(false,"Installed native book animation missing: "+name);return;}
                if(camera.FirstPerson)camera.Toggle();
                SermonAudio audio=SermonAudio.Current;
                if(audio==null){FinishBooks(false,"Native sermon audio observer not connected.");return;}
                voiceRequestsBefore=audio.NativeRequests;voiceStemsBefore=audio.ReplacedVoiceStems;utterancesBefore=audio.ClassicUtterances;mouthBefore=audio.MouthDrivenUtterances;pagesBefore=audio.PreservedPageStems;glyphDrawsBefore=avatar.GlyphGameDrawFrames;incomingDrawsBefore=avatar.BookIncomingGlyphGameDrawFrames;unknownStemsBefore=audio.UnknownStems;
                PrepareSermonEffect(player);
            }
            if(Time.unscaledTime-bookStarted>55f)
            {
                SermonAudio audio=SermonAudio.Current;
                FinishBooks(false,"Native book proof timed out: step="+bookStep+", seen="+String.Join(",",observedBookTracks)+", closed="+bookSawClosed+", open="+bookSawOpen+", stop hidden="+bookStopHidden+", early pose="+earlyBookPose+", arm motion="+bookArmMotion+", incoming frames="+(avatar.BookIncomingGlyphGameDrawFrames-incomingDrawsBefore)+", glyphs="+avatar.ReplacedNativeGlyphCount+", glyph camera frames="+avatar.GlyphGameDrawFrames+", glyph latched="+glyphsObserved+", out complete="+glyphOutComplete+", voice requests="+(audio==null?-1:audio.NativeRequests-voiceRequestsBefore)+", replaced="+(audio==null?-1:audio.ReplacedVoiceStems-voiceStemsBefore)+", mouth="+(audio==null?-1:audio.MouthDrivenUtterances-mouthBefore)+", page="+(audio==null?-1:audio.PreservedPageStems-pagesBefore)+", pitches="+(audio==null?-1:audio.DistinctPitches)+", active loops="+(audio==null?-1:audio.ActiveLongVoiceCount));return;
            }
            if(player!=bookOwner)return;
            if(sermonEffect==null&&Time.unscaledTime>=nextSermonRetry)PrepareSermonEffect(player);
            if(sermonEffect!=null&&sermonEffect.gameObject.activeInHierarchy&&avatar.ReplacedNativeGlyphCount>0&&avatar.GlyphGameDrawFrames>glyphDrawsBefore&&avatar.BookNonGlyphSlotsPreserved)glyphsObserved=true;
            if(bookStep>=bookAnimations.Length)
            {
                if(!glyphOutRequested&&sermonEffect!=null)
                {
                    glyphOutRequested=true;
                    sermonEffect.AnimationState.SetAnimation(0,"sermons/1-out",false).Complete+=delegate {glyphOutComplete=true;};
                    return;
                }
                SermonAudio audio=SermonAudio.Current;
                bool glyphs=sermonEffect!=null&&glyphsObserved&&avatar.BookNonGlyphSlotsPreserved&&glyphOutComplete;
                bool sound=audio!=null&&audio.NativeRequests>=voiceRequestsBefore+3&&audio.ReplacedVoiceStems>=voiceStemsBefore+3&&audio.ClassicUtterances>utterancesBefore&&audio.MouthDrivenUtterances>mouthBefore&&audio.PreservedPageStems>pagesBefore&&audio.LastPageNativeUnmuted&&audio.DistinctPitches>=2&&preachStopped&&audio.ActiveLongVoiceCount==0&&audio.UnknownStems==unknownStemsBefore;
                bool poses=observedBookTracks.Count==bookAnimations.Length&&bookSawClosed&&bookSawOpen&&bookStopHidden&&earlyBookPose&&bookArmMotion>5f&&avatar.BookIncomingGlyphGameDrawFrames>incomingDrawsBefore;
                if(glyphs&&sound&&poses)
                    FinishBooks(true,"five actual native book tracks rendered with seven original Minecraft model parts; closed/open/disappear timing and moving native-bound arms; actual native sermon glyph source camera frames="+(avatar.GlyphGameDrawFrames-glyphDrawsBefore)+", non-glyph slots retained; native voice requests="+(audio.NativeRequests-voiceRequestsBefore)+", replaced stems="+(audio.ReplacedVoiceStems-voiceStemsBefore)+", mouth-driven utterances="+(audio.MouthDrivenUtterances-mouthBefore)+", original page stems="+(audio.PreservedPageStems-pagesBefore)+", pitch variants="+audio.DistinctPitches+", native loop stopped and no active replacement loop.");
                return;
            }
            string animation=bookAnimations[bookStep];
            if(!bookRequested)
            {
                if(player.state.CURRENT_STATE!=StateMachine.State.Idle&&player.state.CURRENT_STATE!=StateMachine.State.Moving)return;
                bookRequested=true;bookFinished=false;bookStepStarted=Time.unscaledTime;
                bookDrawsBefore=avatar.BookGameDrawFrames;bookRevisionBefore=avatar.BookNativePoseRevision;
                Transform arm=avatar.Root==null?null:avatar.Root.Find(Spec.Avatar("torso").Label+"/"+Spec.Avatar("left_arm").Label);
                bookFirstArm=arm==null?Quaternion.identity:arm.localRotation;
                player.CustomAnimationWithCallback(animation,false,delegate {
                    bookFinished=true;
                    if(bookOwner!=null&&bookOwner.state!=null&&bookOwner.state.CURRENT_STATE==StateMachine.State.CustomAnimation)bookOwner.state.CURRENT_STATE=StateMachine.State.Idle;
                });
                logger.LogInfo("Native book fixture requested through PlayerFarming.CustomAnimationWithCallback: "+animation+"; no mod pose or attachment is assigned by this probe.");
                return;
            }
            TrackEntry track=player.Spine.AnimationState.GetCurrent(0);
            bool native=track!=null&&track.Animation!=null&&track.Animation.Name==animation;
            if(bookStep==0&&native&&track.AnimationTime>0.1f&&track.AnimationTime<0.7f&&!avatar.BookVisible&&avatar.BookPoseActive&&avatar.BookPoseDriven&&avatar.BookNativePoseRevision>bookRevisionBefore)earlyBookPose=true;
            if(bookStep==1&&native&&avatar.BookNativeBound)
            {
                float time=track.AnimationTime;
                if(!preachRequested)
                {
                    nativePreachLoop=AudioManager.Instance.CreateLoop("event:/sermon/preach_loop",player.gameObject,true,false);preachRequested=true;
                    if(sermonEffect!=null){sermonEffect.gameObject.SetActive(true);sermonEffect.AnimationState.SetAnimation(0,"sermons/1-in",false);sermonEffect.AnimationState.AddAnimation(0,"sermons/1-loop",true,0f);}
                }
                if(!mixedRequested&&time>0.8f){mixedRequested=true;AudioManager.Instance.PlayOneShot("event:/player/speak_to_follower",player.transform.position);}
                if(!bubbleRequested&&time>2f){bubbleRequested=true;AudioManager.Instance.PlayOneShot("event:/sermon/sermon_speech_bubble",player.transform.position);}
            }
            if(native&&avatar.BookNativeBound&&avatar.BookPoseDriven&&avatar.BookNativeAnimation==animation&&avatar.BookNativePoseRevision>bookRevisionBefore&&avatar.BookGameDrawFrames>=bookDrawsBefore+2&&BookMeshValid())
            {
                observedBookTracks.Add(animation);bookMaxOpen=Math.Max(bookMaxOpen,avatar.BookOpenness);
                bookSawClosed|=avatar.BookOpenness<0.25f;
                bookSawOpen|=avatar.BookOpenness>0.8f;
                Transform arm=avatar.Root.Find(Spec.Avatar("torso").Label+"/"+Spec.Avatar("left_arm").Label);
                if(arm!=null)bookArmMotion=Math.Max(bookArmMotion,Quaternion.Angle(bookFirstArm,arm.localRotation));
            }
            if(bookStep==2&&Time.unscaledTime-bookStepStarted>0.45f&&!avatar.BookVisible)bookStopHidden=true;
            if(!bookFinished)return;
            if(bookStep==1&&preachRequested){AudioManager.Instance.StopLoop(nativePreachLoop,STOP_MODE.IMMEDIATE);preachStopped=true;}
            if(!observedBookTracks.Contains(animation)){FinishBooks(false,"Actual native track completed without a rendered, native-bound Minecraft book: "+animation+", native attachment="+avatar.BookNativeAttachment+", visible="+avatar.BookVisible+", draw delta="+(avatar.BookGameDrawFrames-bookDrawsBefore));return;}
            logger.LogInfo("Native book fixture observed: "+animation+", source attachment/bones and game-camera draws="+(avatar.BookGameDrawFrames-bookDrawsBefore)+", maximum open="+bookMaxOpen.ToString("F2")+", arm motion="+bookArmMotion.ToString("F1")+" degrees.");
            bookStep++;bookRequested=false;
        }

        private void PrepareSermonEffect(PlayerFarming player)
        {
            nextSermonRetry=Time.unscaledTime+1f;
            SkeletonAnimation original=null;
            if(ChurchFollowerManager.Instance!=null)original=ChurchFollowerManager.Instance.SermonEffect;
            if(original==null)foreach(ChurchFollowerManager manager in Resources.FindObjectsOfTypeAll<ChurchFollowerManager>())if(manager!=null&&manager.SermonEffect!=null){original=manager.SermonEffect;break;}
            if(original==null||original.skeletonDataAsset==null){logger.LogInfo("Native sermon effect fixture is waiting for the actual church effect asset; no replacement-only fixture is fabricated.");return;}
            sermonEffect=SkeletonAnimation.NewSkeletonAnimationGameObject(original.skeletonDataAsset);
            sermonEffect.name="CultCraft isolated actual native SermonEffect fixture";
            sermonEffect.transform.position=player.transform.position;
            sermonEffect.transform.rotation=original.transform.rotation;
            sermonEffect.transform.localScale=original.transform.lossyScale;
            sermonEffect.gameObject.layer=original.gameObject.layer;
            Renderer source=original.GetComponent<Renderer>(),destination=sermonEffect.GetComponent<Renderer>();
            if(source!=null&&destination!=null){destination.sortingLayerID=source.sortingLayerID;destination.sortingOrder=source.sortingOrder;}
            BookAnimationBinding.ObserveNativeSermonEffect(sermonEffect);
            sermonEffect.gameObject.SetActive(false);
            logger.LogInfo("Native sermon fixture uses the real ChurchFollowerManager.SermonEffect skeletonDataAsset and original scale/material sorting; real in/loop/out tracks retain native non-glyph slots.");
        }

        private bool BookMeshValid()
        {
            Transform root=avatar.WorldBookRoot;
            if(root==null||!root.gameObject.activeInHierarchy||avatar.MinecraftBookPartCount!=7||!avatar.BookVolumetricPages||!avatar.BookArmsHaveDepth)return false;
            MeshRenderer[] renderers=root.GetComponentsInChildren<MeshRenderer>();int modelParts=0;
            foreach(MeshRenderer renderer in renderers)
            {
                if(renderer.sharedMaterial!=null&&renderer.sharedMaterial.mainTexture==assets.Texture("enchanting_glyphs"))continue;
                MeshFilter filter=renderer.GetComponent<MeshFilter>();
                if(!renderer.enabled||renderer.forceRenderingOff||filter==null||filter.sharedMesh==null||filter.sharedMesh.vertexCount<24||renderer.sharedMaterial==null||renderer.sharedMaterial.mainTexture!=assets.Texture("enchanting_table_book")||renderer.sharedMaterial.shader==null||!renderer.sharedMaterial.shader.isSupported||renderer.transform.localToWorldMatrix.determinant<=0f||renderer.GetComponent<Collider>()!=null)return false;
                modelParts++;
            }
            return modelParts==7;
        }
        private void FinishBooks(bool pass,string detail)
        {BookComplete=true;bookPassed=pass;bookDetail=detail;CleanupBooks();logger.LogInfo("Native Minecraft book/sermon proof: "+detail);if(!pass){bookReported=true;report("minecraft-book-sermon",false,detail);}}
        private void CleanupBooks()
        {if(preachRequested&&!preachStopped&&AudioManager.Instance!=null){AudioManager.Instance.StopLoop(nativePreachLoop,STOP_MODE.IMMEDIATE);preachStopped=true;}if(sermonEffect!=null){UnityEngine.Object.Destroy(sermonEffect.gameObject);sermonEffect=null;}}

        private void CheckSettings()
        {
            float now=Time.unscaledTime;
            if(settingsStarted<0f)
            {
                savedSensitivity=camera.MouseSensitivity;savedFov=camera.FieldOfView;savedScale=PixelUi.ScaleMode;savedHints=PixelUi.ShowHints;settingsCaptured=true;
                initialSaves=camera.SettingsSaveCount;initialChanges=camera.SettingsSliderChangeCount;initialFrames=camera.SettingsRenderFrameCount;
                settingsStarted=phaseSince=now;SettingsHotkeyArmed=true;
            }
            if(now-settingsStarted>25f)
            {
                FinishSettings(false,"F6 native input/UI/persistence probe timed out: phase="+settingsPhase+", open="+camera.SettingsOpen+", style="+camera.SettingsPixelStyleReady+", frames="+(camera.SettingsRenderFrameCount-initialFrames)+", changes="+(camera.SettingsSliderChangeCount-initialChanges)+", "+camera.SettingsStyleError);
                return;
            }
            switch(settingsPhase)
            {
                case 0:
                    MeltyPlay.Hold("f6",Mathf.Repeat(now-phaseSince,0.6f)<0.25f);
                    if(Keyboard.current!=null&&Keyboard.current.f6Key.isPressed)openedWithF6=true;
                    if(!camera.SettingsOpen)break;
                    MeltyPlay.Hold("f6",false);AdvanceSettings(1);break;
                case 1:
                    if(!camera.SettingsPixelStyleReady||camera.SettingsRenderFrameCount-initialFrames<6||now-phaseSince<2f)break;
                    if(camera.SettingsSliderRects.Length!=2)break;
                    AdvanceSettings(2);break;
                case 2:
                    DragSlider(0,Math.Abs(savedSensitivity-0.7256f)<0.15f?0.3f:0.72f);
                    if(Math.Abs(camera.MouseSensitivity-savedSensitivity)<0.03f||camera.SettingsSliderChangeCount<=initialChanges||now-phaseSince<0.3f)break;
                    MeltyPlay.Button(0,false);AdvanceSettings(13);break;
                case 13:
                    // QueueStateEvent release is processed on the next input update;
                    // record the final released slider value, not an in-flight drag sample.
                    if(now-phaseSince<0.25f)break;
                    changedSensitivity=camera.MouseSensitivity;AdvanceSettings(3);break;
                case 3:
                    if(now-phaseSince<0.2f)break;
                    DragSlider(1,Math.Abs(savedFov-96f)>5f?0.75f:0.2f);AdvanceSettings(4);break;
                case 4:
                    DragSlider(1,Math.Abs(savedFov-96f)>5f?0.75f:0.2f);
                    if(Math.Abs(camera.FieldOfView-savedFov)<2f||now-phaseSince<0.3f)break;
                    MeltyPlay.Button(0,false);AdvanceSettings(14);break;
                case 14:
                    if(now-phaseSince<0.25f)break;
                    changedFov=camera.FieldOfView;AdvanceSettings(9);break;
                case 5:
                    if(now-phaseSince<2.5f)break;
                    Point(camera.SettingsCloseRect.center);MeltyPlay.Button(0,true);AdvanceSettings(6);break;
                case 6:
                    Point(camera.SettingsCloseRect.center);
                    if(now-phaseSince<0.2f)break;
                    MeltyPlay.Button(0,false);
                    if(camera.SettingsOpen){if(now-phaseSince>0.6f)AdvanceSettings(5);break;}
                    if(camera.SettingsSaveCount<=initialSaves)break;
                    AdvanceSettings(7);break;
                case 7:
                    MeltyPlay.Hold("f6",Mathf.Repeat(now-phaseSince,0.6f)<0.25f);
                    if(!camera.SettingsOpen)break;
                    MeltyPlay.Hold("f6",false);AdvanceSettings(8);break;
                case 8:
                    if(now-phaseSince<0.5f)break;
                    bool values=Math.Abs(camera.MouseSensitivity-changedSensitivity)<0.001f&&Math.Abs(camera.FieldOfView-changedFov)<0.1f&&PixelUi.ScaleMode==changedScale&&PixelUi.ShowHints==changedHints;
                    bool bounds=camera.MouseSensitivity>=0.02f&&camera.MouseSensitivity<=1f&&camera.FieldOfView>=55f&&camera.FieldOfView<=110f;
                    bool scales = PixelUi.ResolveScale(0,1366,768)==3f && PixelUi.ResolveScale(2,1366,768)==2f &&
                        PixelUi.ResolveScale(1,2560,1440)==5f && PixelUi.ResolveScale(2,2560,1440)==4f && PixelUi.ResolveScale(3,2560,1440)==3f;
                    FinishSettings(openedWithF6&&values&&bounds&&scales&&camera.SettingsPixelStyleReady&&camera.SettingsSliderChangeCount>=initialChanges+2,
                        "actual F6="+openedWithF6+", pixel widget/background/font="+camera.SettingsPixelStyleReady+", real GUI frames="+(camera.SettingsRenderFrameCount-initialFrames)+", actual slider changes="+(camera.SettingsSliderChangeCount-initialChanges)+", sensitivity="+changedSensitivity.ToString("F3")+"/"+camera.MouseSensitivity.ToString("F3")+", FOV="+changedFov.ToString("F1")+"/"+camera.FieldOfView.ToString("F1")+", scale="+changedScale+"/"+PixelUi.ScaleMode+", hints="+changedHints+"/"+PixelUi.ShowHints+", close saved="+(camera.SettingsSaveCount>initialSaves)+", reopened values retained="+values);
                    break;
                case 9:
                    if(now-phaseSince<0.2f)break;
                    Point(camera.SettingsScaleRect.center);MeltyPlay.Button(0,true);AdvanceSettings(10);break;
                case 10:
                    Point(camera.SettingsScaleRect.center);
                    if(now-phaseSince<0.2f)break;
                    MeltyPlay.Button(0,false);
                    if(PixelUi.ScaleMode==savedScale){if(now-phaseSince>0.6f)AdvanceSettings(9);break;}
                    changedScale=PixelUi.ScaleMode;AdvanceSettings(11);break;
                case 11:
                    if(now-phaseSince<0.2f)break;
                    Point(camera.SettingsHintsRect.center);MeltyPlay.Button(0,true);AdvanceSettings(12);break;
                case 12:
                    Point(camera.SettingsHintsRect.center);
                    if(now-phaseSince<0.2f)break;
                    MeltyPlay.Button(0,false);
                    if(PixelUi.ShowHints==savedHints){if(now-phaseSince>0.6f)AdvanceSettings(11);break;}
                    changedHints=PixelUi.ShowHints;AdvanceSettings(5);break;
            }
        }

        private void AdvanceSettings(int phase){settingsPhase=phase;phaseSince=Time.unscaledTime;}
        private void DragSlider(int index,float fraction)
        {Rect rect=camera.SettingsSliderRects[index];Point(new Vector2(Mathf.Lerp(rect.xMin,rect.xMax,fraction),rect.center.y));MeltyPlay.Button(0,Mathf.Repeat(Time.unscaledTime-phaseSince,0.5f)<0.3f);}
        private static void Point(Vector2 point){MeltyPlay.Point(point.x/Math.Max(1,Screen.width),point.y/Math.Max(1,Screen.height));}
        private void FinishSettings(bool pass,string detail)
        {
            SettingsHotkeyArmed=false;MeltyPlay.Hold("f6",false);MeltyPlay.Button(0,false);
            if(camera.SettingsOpen)camera.ToggleSettings();
            if(settingsCaptured)camera.RestoreTestPreferences(savedSensitivity,savedFov,savedScale,savedHints);
            settingsDone=true;logger.LogInfo("Actual Minecraft F6 UI proof: "+detail+"; original preferences restored after observations.");
            report("minecraft-settings-ui",pass,pass?null:detail);
        }

        public void Dispose()
        {
            repairs.Dispose();
            attackProof.Dispose();
            CleanupBooks();
            SettingsHotkeyArmed=false;MeltyPlay.Hold("f6",false);MeltyPlay.Button(0,false);
            if(settingsCaptured&&!settingsDone){if(camera.SettingsOpen)camera.ToggleSettings();camera.RestoreTestPreferences(savedSensitivity,savedFov,savedScale,savedHints);}
        }
    }
}
