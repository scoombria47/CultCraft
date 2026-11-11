using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace CultCraft
{
    // Uses the local Minecraft resources/index only; no extraction, download or shipped sound files.
    internal sealed class InstalledSounds : IDisposable
    {
        private readonly Dictionary<string,UnityWebRequest> pending=new Dictionary<string,UnityWebRequest>();
        private readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        private readonly Dictionary<string,string> states=new Dictionary<string,string>();
        private readonly Dictionary<string,string> paths=new Dictionary<string,string>();
        private readonly Dictionary<string,FMOD.Sound> nativeSounds=new Dictionary<string,FMOD.Sound>();
        private readonly Dictionary<string,PreparedSoundPlayback> prepared=new Dictionary<string,PreparedSoundPlayback>();
        private readonly object playbackLock=new object();
        private int playedCount;
        public int PlayedCount { get { return Interlocked.CompareExchange(ref playedCount,0,0); } }
        public string LastPlayback { get; private set; }
        public float LastPitch { get; private set; } = 1f;
        public string Status { get { var result=new List<string>();foreach(var item in states)result.Add(item.Key+"="+item.Value);return String.Join(", ",result.ToArray()); } }
        public InstalledSounds(MinecraftAssetIndex index)
        {
            foreach(SoundSourceRow source in Spec.SoundSourceRows)
            {
                try
                {
                    string path=index.FindLegacy(source.LegacyPath) ?? index.Find(source.Entry);
                    if(path==null){states[source.Id]="not installed";continue;}
                    paths[source.Id]=path;
                    var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,AudioType.OGGVORBIS);
                    request.SendWebRequest();pending.Add(source.Id,request);states[source.Id]="loading";
                }
                catch(Exception error){states[source.Id]="unavailable ("+error.GetType().Name+")";}
            }
        }
        public void Tick()
        {
            if(pending.Count==0)return;
            var finished=new List<string>();
            foreach(var entry in pending)
            {
                UnityWebRequest request=entry.Value;if(!request.isDone)continue;
                if(request.result==UnityWebRequest.Result.Success)
                {
                    AudioClip clip=DownloadHandlerAudioClip.GetContent(request);
                    if(clip!=null){clip.name="CultCraft local Minecraft "+entry.Key;clips[entry.Key]=clip;states[entry.Key]="ready";}
                    else states[entry.Key]="decode failed";
                }
                else states[entry.Key]="decode failed";
                request.Dispose();finished.Add(entry.Key);
            }
            foreach(string id in finished)pending.Remove(id);
        }
        public AudioClip Get(string id){Tick();AudioClip clip;return clips.TryGetValue(id,out clip)?clip:null;}
        public bool Play(string id,float volume) { return Play(id,volume,1f); }
        public bool Play(string id,float volume,float pitch)
        {
            PreparedSoundPlayback player=Prepare(id);FMOD.Channel channel;
            return player!=null&&player.Play(volume,pitch,out channel);
        }
        // Called on the main thread. The returned immutable engine/sound handle can
        // be played from FMOD's native callbacks without Unity or file access.
        internal PreparedSoundPlayback Prepare(string id)
        {
            lock(playbackLock)
            {
                PreparedSoundPlayback result;if(prepared.TryGetValue(id,out result))return result;
                string path;if(!paths.TryGetValue(id,out path))return null;
                try
                {
                FMOD.Sound sound;
                if(!nativeSounds.TryGetValue(id,out sound)||!sound.hasHandle())
                {
                    FMOD.RESULT loaded=FMODUnity.RuntimeManager.CoreSystem.createSound(path,FMOD.MODE.CREATESAMPLE,out sound);
                    if(loaded!=FMOD.RESULT.OK){LastPlayback=id+": "+loaded;return null;}
                    nativeSounds[id]=sound;
                }
                result=new PreparedSoundPlayback(FMODUnity.RuntimeManager.CoreSystem,sound,this,id);
                prepared.Add(id,result);return result;
                }
                catch(Exception error){LastPlayback=id+": "+error.GetType().Name;return null;}
            }
        }
        internal void RecordPlayback(string id,float pitch)
        {
            LastPitch=pitch;Interlocked.Increment(ref playedCount);LastPlayback=id+": FMOD channel playing";
        }
        public void Dispose()
        {
            lock(playbackLock)
            {
                foreach(PreparedSoundPlayback sound in prepared.Values)sound.Invalidate();prepared.Clear();
                foreach(FMOD.Sound sound in nativeSounds.Values)if(sound.hasHandle())sound.release();nativeSounds.Clear();
            }
            foreach(var request in pending.Values)request.Dispose();pending.Clear();
            foreach(AudioClip clip in clips.Values)if(clip!=null)UnityEngine.Object.Destroy(clip);clips.Clear();
        }
    }

    internal sealed class PreparedSoundPlayback
    {
        private readonly FMOD.System system;
        private readonly FMOD.Sound sound;
        private readonly InstalledSounds owner;
        private readonly string id;
        private volatile bool valid=true;
        internal PreparedSoundPlayback(FMOD.System system,FMOD.Sound sound,InstalledSounds owner,string id)
        {this.system=system;this.sound=sound;this.owner=owner;this.id=id;}
        internal void Invalidate(){valid=false;}
        internal bool Play(float volume,float pitch,out FMOD.Channel channel)
        {
            channel=default(FMOD.Channel);
            if(!valid||volume<=0f||Single.IsNaN(pitch)||Single.IsInfinity(pitch)||pitch<=0f)return false;
            try
            {
                if(system.playSound(sound,default(FMOD.ChannelGroup),true,out channel)!=FMOD.RESULT.OK)return false;
                float actual;
                if(channel.setVolume(Math.Min(1f,volume))!=FMOD.RESULT.OK||channel.setPitch(pitch)!=FMOD.RESULT.OK||
                    channel.getPitch(out actual)!=FMOD.RESULT.OK||Math.Abs(actual-pitch)>0.001f||channel.setPaused(false)!=FMOD.RESULT.OK)
                {channel.stop();return false;}
                bool playing;
                if(channel.isPlaying(out playing)!=FMOD.RESULT.OK||!playing){channel.stop();return false;}
                owner.RecordPlayback(id,actual);return true;
            }
            catch{if(channel.hasHandle())channel.stop();return false;}
        }
    }
}
