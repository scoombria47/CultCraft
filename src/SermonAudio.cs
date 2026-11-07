using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using AOT;
using BepInEx.Logging;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace CultCraft
{
    // Native sermon events retain their timeline, pages, routing and lifetime.
    // Only audited vocal Core channels are replaced, after Minecraft playback succeeds.
    public sealed class SermonAudio : IDisposable
    {
        private sealed class EventContext
        {
            public EventInstance Instance;
            public string Path;
            public PlayerFarming Owner;
            public readonly HashSet<string> Processed=new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> ObservedVoices=new HashSet<string>(StringComparer.Ordinal);
        }
        private sealed class PendingSound
        {
            public EventContext Event;
            public FMOD.Sound Sound;
            public string Name;
            public int Attempts;
        }
        private sealed class Voice
        {
            public EventContext Event;
            public FMOD.Channel Original;
            public FMOD.Channel Replacement;
            public string Stem;
            public float NativePitch;
            public float Gain=1f;
            public bool WasMuted;
            public bool Long;
            public string LastMouth;
            public int Utterances;
            public FMOD.DSP Silencer;
        }
        private static SermonAudio current;
        private static readonly EVENT_CALLBACK callback=EventCallback;
        private readonly MinecraftAssets assets;
        private readonly ManualLogSource logger;
        private readonly Harmony harmony=new Harmony("cultcraft.sermon.audio");
        private readonly object gate=new object();
        private readonly Dictionary<IntPtr,EventContext> events=new Dictionary<IntPtr,EventContext>();
        private readonly Queue<PendingSound> pending=new Queue<PendingSound>();
        private readonly Queue<string> diagnostics=new Queue<string>();
        private readonly List<Voice> voices=new List<Voice>();
        private readonly HashSet<int> pitches=new HashSet<int>();
        private volatile PreparedSoundPlayback classic;
        private volatile float volume;
        private volatile bool disposed;
        private int nativeRequests,nativeVoiceStems,replacedStems,utterances,mouthUtterances,preservedPages,unknownStems;
        private int silencedStems;
        public int SilencedVoiceStems {get{return silencedStems;}}
        public static SermonAudio Current { get { return current; } }
        public int NativeRequests { get { return nativeRequests; } }
        public int NativeVoiceStems { get { return nativeVoiceStems; } }
        public int ReplacedVoiceStems { get { return replacedStems; } }
        public int ClassicUtterances { get { return utterances; } }
        public int MouthDrivenUtterances { get { return mouthUtterances; } }
        public int PreservedPageStems { get { return preservedPages; } }
        public int UnknownStems { get { return unknownStems; } }
        public int ActiveLongVoiceCount { get { lock(gate){int count=0;foreach(Voice voice in voices)if(voice.Long)count++;return count;} } }
        public int DistinctPitches { get { lock(gate)return pitches.Count; } }
        public string LastNativePath { get; private set; }
        public string LastNativeStem { get; private set; }
        public float LastNativePitch { get; private set; }
        public float LastReplacementPitch { get; private set; }
        public bool LastPageNativeUnmuted { get; private set; }

        public SermonAudio(MinecraftAssets assets,ManualLogSource logger)
        {
            this.assets=assets;this.logger=logger;current=this;
            try
            {
                Patch("PlayOneShot",new[]{typeof(string)},"OneShotPrefix");
                Patch("PlayOneShot",new[]{typeof(string),typeof(Vector3)},"PositionPrefix");
                Patch("PlayOneShot",new[]{typeof(string),typeof(GameObject)},"AttachedPrefix");
                MethodInfo loop=AccessTools.Method(typeof(AudioManager),"CreateLoopInternal",new[]{typeof(string),typeof(EventInstance).MakeByRefType()});
                if(loop==null)throw new MissingMethodException("AudioManager.CreateLoopInternal");
                harmony.Patch(loop,postfix:new HarmonyMethod(typeof(SermonAudio),"LoopCreatedPostfix"));
            }
            catch{harmony.UnpatchSelf();if(current==this)current=null;throw;}
        }
        private void Patch(string method,Type[] args,string prefix)
        {
            MethodInfo found=AccessTools.Method(typeof(AudioManager),method,args);
            if(found==null)throw new MissingMethodException("AudioManager."+method);
            harmony.Patch(found,prefix:new HarmonyMethod(typeof(SermonAudio),prefix));
        }
        private static bool KnownEvent(string path)
        {
            return String.Equals(path,"event:/sermon/preach_loop",StringComparison.OrdinalIgnoreCase)||
                String.Equals(path,"event:/sermon/sermon_speech_bubble",StringComparison.OrdinalIgnoreCase)||
                String.Equals(path,"event:/sermon/goat_sermon_speech_bubble",StringComparison.OrdinalIgnoreCase)||
                String.Equals(path,"event:/player/speak_to_follower",StringComparison.OrdinalIgnoreCase)||
                String.Equals(path,"event:/player/speak_to_follower_noBookPage",StringComparison.OrdinalIgnoreCase)||
                String.Equals(path,"event:/player/goat_player/goat_speak_to_follower",StringComparison.OrdinalIgnoreCase)||
                String.Equals(path,"event:/player/goat_player/goat_speak_to_follower_noBookPage",StringComparison.OrdinalIgnoreCase);
        }
        private bool Eligible(string path)
        {
            // Called only from the main-thread native creation paths. No Unity
            // object, animation or settings are read on FMOD's callback thread.
            // Native blessing/inspiring voices are emitted before their animation
            // starts, and do not necessarily expose the BOOK slot at all.
            return !disposed&&classic!=null&&PlayerFarming.Instance!=null&&KnownEvent(path)&&
                (path.StartsWith("event:/player/",StringComparison.OrdinalIgnoreCase)||BookAnimationBinding.IsReading(PlayerFarming.Instance));
        }
        private static bool OneShotPrefix(string __0)
        {return current==null||!current.PlayNativeEvent(__0,Vector3.zero,null);}
        private static bool PositionPrefix(string __0,Vector3 __1)
        {return current==null||!current.PlayNativeEvent(__0,__1,null);}
        private static bool AttachedPrefix(string __0,GameObject __1)
        {return current==null||!current.PlayNativeEvent(__0,__1==null?Vector3.zero:__1.transform.position,__1);}
        private bool PlayNativeEvent(string path,Vector3 position,GameObject attached)
        {
            if(!disposed&&classic!=null&&PlayerFarming.Instance!=null&&
                (String.Equals(path,"event:/player/reassure_baah",StringComparison.OrdinalIgnoreCase)||String.Equals(path,"event:/player/long_laugh_baah",StringComparison.OrdinalIgnoreCase)))
            {
                FMOD.Channel replacement;
                if(classic.Play(volume,1f,out replacement)){Interlocked.Increment(ref utterances);QueueLog("Pure native player baa event replaced: "+path);return true;}
            }
            if(!Eligible(path))return false;
            EventInstance instance=default(EventInstance);
            try
            {
                instance=RuntimeManager.CreateInstance(path);
                if(!instance.isValid())return false;
                if(!Register(path,instance)){instance.release();return false;}
                instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
                if(attached!=null)RuntimeManager.AttachInstanceToGameObject(instance,attached.transform,(Rigidbody)null);
                if(instance.start()!=FMOD.RESULT.OK){instance.release();return false;}
                instance.release();
                return true;
            }
            catch{if(instance.isValid())instance.release();return false;}
        }
        private static void LoopCreatedPostfix(string __0,EventInstance __1)
        {if(current!=null&&current.Eligible(__0))current.Register(__0,__1);}
        private bool Register(string path,EventInstance instance)
        {
            if(!instance.isValid())return false;
            var context=new EventContext{Instance=instance,Path=path,Owner=PlayerFarming.Instance};
            lock(gate)events[instance.handle]=context;
            FMOD.RESULT result=instance.setCallback(callback,EVENT_CALLBACK_TYPE.SOUND_PLAYED|EVENT_CALLBACK_TYPE.DESTROYED);
            if(result!=FMOD.RESULT.OK){lock(gate)events.Remove(instance.handle);return false;}
            Interlocked.Increment(ref nativeRequests);return true;
        }
        [MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT EventCallback(EVENT_CALLBACK_TYPE type,IntPtr instance,IntPtr parameter)
        {
            SermonAudio owner=current;if(owner==null||owner.disposed)return FMOD.RESULT.OK;
            try
            {
                EventContext context;lock(owner.gate)if(!owner.events.TryGetValue(instance,out context))return FMOD.RESULT.OK;
                if(type==EVENT_CALLBACK_TYPE.DESTROYED){lock(owner.gate)owner.events.Remove(instance);return FMOD.RESULT.OK;}
                if(type!=EVENT_CALLBACK_TYPE.SOUND_PLAYED||parameter==IntPtr.Zero)return FMOD.RESULT.OK;
                var sound=new FMOD.Sound(parameter);string name;
                if(sound.getName(out name,256)!=FMOD.RESULT.OK)return FMOD.RESULT.OK;
                var work=new PendingSound{Event=context,Sound=sound,Name=name};
                if(!owner.Process(work))lock(owner.gate)owner.pending.Enqueue(work);
            }
            catch{ /* A missing native instrument keeps its original audio. */ }
            return FMOD.RESULT.OK;
        }
        private static bool Vocal(string name)
        {
            if(name=="Preach Loop"||name=="Sermon Speech Bubble"||name=="Sermon Speech Bubble2"||name=="Sermon Speech Bubble3"||name=="sheep baah5")return true;
            int variant;
            return name!=null&&(name.StartsWith("Goat ",StringComparison.Ordinal)&&Int32.TryParse(name.Substring(5),out variant)&&variant>=1&&variant<=9||
                name.StartsWith("soft GOAT",StringComparison.Ordinal)&&Int32.TryParse(name.Substring(9),out variant)&&variant>=1&&variant<=6);
        }
        private static bool PreservedEffect(string name)
        {return name=="Scroll Sermon Menu"||name=="Scroll Sermon Menu2"||name=="Scroll Sermon Menu3"||name=="Select Upgrade";}
        private bool Process(PendingSound work)
        {
            if(disposed||!work.Event.Instance.isValid())return true;
            FMOD.ChannelGroup root;
            if(work.Event.Instance.getChannelGroup(out root)!=FMOD.RESULT.OK)return false;
            var found=new List<FMOD.Channel>();FindChannels(root,work.Sound,work.Name,found,0);
            if(found.Count==0)return false;
            bool retry=false;
            foreach(FMOD.Channel channel in found)
            {
                string instrument=channel.handle.ToString()+"/"+work.Sound.handle.ToString()+"/"+work.Name;
                lock(work.Event.Processed)if(!work.Event.Processed.Add(instrument))continue;
                if(work.Name=="Book Pages Loop")
                {
                    bool muted;LastPageNativeUnmuted=channel.getMute(out muted)==FMOD.RESULT.OK&&!muted;
                    Interlocked.Increment(ref preservedPages);QueueLog("Native book-page stem preserved: "+work.Name+", unmuted="+LastPageNativeUnmuted);continue;
                }
                if(PreservedEffect(work.Name)){QueueLog("Native sermon non-vocal stem preserved: "+work.Name);continue;}
                if(!Vocal(work.Name)){Interlocked.Increment(ref unknownStems);QueueLog("Native sermon unknown stem retained: "+work.Name+" in "+work.Event.Path);continue;}
                lock(work.Event.Processed)if(work.Event.ObservedVoices.Add(instrument))Interlocked.Increment(ref nativeVoiceStems);
                bool wasMuted;
                if(channel.getMute(out wasMuted)!=FMOD.RESULT.OK){retry|=RetryVoice(work,instrument);continue;}
                if(wasMuted)continue;
                float pitch,scale,final,gain;
                if(channel.getPitch(out pitch)!=FMOD.RESULT.OK||work.Event.Instance.getPitch(out scale,out final)!=FMOD.RESULT.OK||
                    channel.getVolume(out gain)!=FMOD.RESULT.OK)
                {retry|=RetryVoice(work,instrument);continue;}
                pitch*=final;
                if(pitch<=0f||Single.IsNaN(pitch)||Single.IsInfinity(pitch))
                {retry|=RetryVoice(work,instrument);continue;}
                var voice=new Voice{Event=work.Event,Original=channel,Stem=work.Name,NativePitch=pitch,Gain=gain,WasMuted=wasMuted,Long=work.Name=="Preach Loop"};
                if(!PlayClassic(voice,0)){retry|=RetryVoice(work,instrument);continue;}
                lock(gate)
                {
                    // Disposal takes the same gate: no callback can mute a
                    // native channel after the restoration snapshot is taken.
                    if(disposed||channel.setMute(true)!=FMOD.RESULT.OK){voice.Replacement.stop();retry|=RetryVoice(work,instrument);continue;}
                    // Studio can reapply channel state after our initial mute.
                    // A per-vocal Core fader remains in that exact channel's DSP
                    // chain; page and music channels never pass through it.
                    FMOD.DSP silencer;
                    if(RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.FADER,out silencer)==FMOD.RESULT.OK)
                    {
                        if(silencer.setParameterFloat(0,-80f)==FMOD.RESULT.OK&&channel.addDSP((int)FMOD.CHANNELCONTROL_DSP_INDEX.TAIL,silencer)==FMOD.RESULT.OK){voice.Silencer=silencer;Interlocked.Increment(ref silencedStems);}
                        else silencer.release();
                    }
                    voices.Add(voice);
                }
                Interlocked.Increment(ref replacedStems);
                QueueLog("Native sermon voice replaced: path="+work.Event.Path+", stem="+work.Name+", sourcePitch="+pitch.ToString("F4")+", classicPitch="+LastReplacementPitch.ToString("F4")+", actual Minecraft FMOD channel playing; page/music stems retained.");
            }
            return !retry;
        }
        private bool RetryVoice(PendingSound work,string instrument)
        {
            if(disposed||volume<=0f)return false;
            // Release only a failed vocal reservation. The native channel keeps
            // playing during a bounded retry of an initial fade or Core readiness.
            lock(work.Event.Processed)work.Event.Processed.Remove(instrument);
            return true;
        }
        private static void FindChannels(FMOD.ChannelGroup group,FMOD.Sound target,string targetName,List<FMOD.Channel> found,int depth)
        {
            if(depth>12)return;
            int count;
            if(group.getNumChannels(out count)==FMOD.RESULT.OK)for(int i=0;i<count;i++)
            {
                FMOD.Channel channel;FMOD.Sound sound;
                if(group.getChannel(i,out channel)!=FMOD.RESULT.OK||channel.getCurrentSound(out sound)!=FMOD.RESULT.OK)continue;
                string name;
                if(sound.handle==target.handle||sound.getName(out name,256)==FMOD.RESULT.OK&&name==targetName)found.Add(channel);
            }
            if(group.getNumGroups(out count)==FMOD.RESULT.OK)for(int i=0;i<count;i++)
            {FMOD.ChannelGroup child;if(group.getGroup(i,out child)==FMOD.RESULT.OK)FindChannels(child,target,targetName,found,depth+1);}
        }
        private bool PlayClassic(Voice voice,int sequence)
        {
            PreparedSoundPlayback player=classic;if(player==null||disposed)return false;
            int nativeVariant;
            int offset=voice.Stem=="Sermon Speech Bubble"?-2:0;
            if(voice.Stem=="Sermon Speech Bubble3")offset=2;
            if(voice.Stem=="sheep baah5")offset=2;
            if(voice.Stem.StartsWith("Goat ",StringComparison.Ordinal)&&Int32.TryParse(voice.Stem.Substring(5),out nativeVariant))offset=(nativeVariant%3-1)*2;
            if(voice.Stem.StartsWith("soft GOAT",StringComparison.Ordinal)&&Int32.TryParse(voice.Stem.Substring(9),out nativeVariant))offset=(nativeVariant%3-1)*2;
            if(voice.Long)offset=(sequence%3-1)*2;
            float pitch=voice.NativePitch*(float)Math.Pow(2d,offset/12d);
            FMOD.Channel channel;if(!player.Play(volume*voice.Gain,pitch,out channel))return false;
            voice.Replacement=channel;voice.Utterances++;
            LastNativePath=voice.Event.Path;LastNativeStem=voice.Stem;LastNativePitch=voice.NativePitch;LastReplacementPitch=pitch;
            lock(gate)pitches.Add((int)Math.Round(pitch*1000f));
            Interlocked.Increment(ref utterances);return true;
        }
        private void QueueLog(string text){lock(gate)if(diagnostics.Count<256)diagnostics.Enqueue(text);}
        public void Tick(PlayerFarming player)
        {
            if(disposed)return;
            if(classic==null)classic=assets.PrepareSoundPlayback("hurt_classic");
            var settings=SettingsManager.Settings;
            volume=settings!=null&&settings.Audio!=null?Spec.Visual("feedback_volume").Value*Mathf.Clamp01(settings.Audio.MasterVolume)*Mathf.Clamp01(settings.Audio.VOVolume):0f;
            PendingSound[] jobs;Voice[] active;string[] messages;
            lock(gate){jobs=pending.ToArray();pending.Clear();active=voices.ToArray();messages=diagnostics.ToArray();diagnostics.Clear();}
            foreach(PendingSound job in jobs)if(!Process(job)&&++job.Attempts<30)lock(gate)pending.Enqueue(job);
            foreach(string message in messages)if(logger!=null)logger.LogInfo(message);
            string mouth=ReadMouth(player);
            foreach(Voice voice in active)
            {
                bool playing;PLAYBACK_STATE state;
                if(!voice.Event.Instance.isValid()||voice.Event.Instance.getPlaybackState(out state)!=FMOD.RESULT.OK||state==PLAYBACK_STATE.STOPPED||state==PLAYBACK_STATE.STOPPING||
                    voice.Original.isPlaying(out playing)!=FMOD.RESULT.OK||!playing)
                {voice.Replacement.stop();ReleaseSilencer(voice);lock(gate)voices.Remove(voice);continue;}
                // Retain the native mute as well when Studio updates a live instrument.
                voice.Original.setMute(true);
                if(!voice.Long||voice.Event.Owner!=player)continue;
                float gain;if(voice.Original.getVolume(out gain)==FMOD.RESULT.OK)voice.Gain=gain;
                if(mouth!=voice.LastMouth)
                {
                    // TALK1..5 are distinct authored speech poses. A long line
                    // may stay inside that family; each changed speech pose is
                    // a native cue once the previous Minecraft utterance ends.
                    bool opening=IsOpenMouth(mouth);voice.LastMouth=mouth;
                    bool speaking;
                    if(opening&&(voice.Replacement.isPlaying(out speaking)!=FMOD.RESULT.OK||!speaking)&&PlayClassic(voice,voice.Utterances))Interlocked.Increment(ref mouthUtterances);
                }
            }
        }
        private static string ReadMouth(PlayerFarming player)
        {
            if(player==null||player.Spine==null||player.Spine.Skeleton==null)return null;
            Spine.Slot mouth=player.Spine.Skeleton.FindSlot("MOUTH");
            return mouth==null||mouth.Attachment==null?null:mouth.Attachment.Name;
        }
        private static bool IsOpenMouth(string mouth)
        {return !String.IsNullOrEmpty(mouth)&&mouth.IndexOf("MOUTH_TALK",StringComparison.Ordinal)>=0;}
        private static void ReleaseSilencer(Voice voice)
        {if(voice.Silencer.hasHandle()){voice.Original.removeDSP(voice.Silencer);voice.Silencer.release();voice.Silencer.clearHandle();}}
        public void Dispose()
        {
            if(disposed)return;disposed=true;harmony.UnpatchSelf();
            Voice[] active;EventContext[] registered;
            lock(gate){active=voices.ToArray();registered=new List<EventContext>(events.Values).ToArray();voices.Clear();events.Clear();pending.Clear();}
            foreach(Voice voice in active){voice.Replacement.stop();ReleaseSilencer(voice);voice.Original.setMute(voice.WasMuted);}
            foreach(EventContext context in registered)if(context.Instance.isValid())context.Instance.setCallback(null,EVENT_CALLBACK_TYPE.ALL);
            if(current==this)current=null;
        }
    }
}
