using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using MMTools;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CultCraft
{
    internal sealed class StartupMovie : IDisposable
    {
        internal const string CheckId = "startup-mojang-video";
        private const string DevolverVideo = "Devolver_Animated_Logo_v001_4k_Audio-_HQ_HBR_MP4";
        private static StartupMovie current;
        private readonly Harmony harmony = new Harmony("cultcraft.startup.movie");
        private readonly ManualLogSource logger;
        private static readonly MethodInfo SplashRoutine = AccessTools.Method(typeof(LoadMainMenu), "RunSplashScreens");
        private readonly string path;
        private FMOD.Sound soundtrack;
        private FMOD.Channel audioChannel;
        private PreparedSoundPlayback audioPlayback;
        private bool audioStarted;
        private string audioError;
        private float audioVolume;
        private GameObject playback;
        private GameObject overlay;
        private VideoPlayer video;
        private RawImage image;
        private AspectRatioFitter aspect;
        private string error;
        private bool completed;
        private bool attempted;
        private bool finished;
        private bool reported;
        private int visibleFrames;
        private double firstTime;
        private double lastTime;
        private bool disposed;

        internal StartupMovie(ManualLogSource logger)
        {
            this.logger = logger;
            path = Path.Combine(Path.GetDirectoryName(typeof(StartupMovie).Assembly.Location), "CultCraftMedia", "MojangStudios.mp4");
            current = this;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(LoadMainMenu), "Start"),
                    prefix: new HarmonyMethod(typeof(StartupMovie), nameof(PreparePrefix)),
                    transpiler: new HarmonyMethod(typeof(StartupMovie), nameof(WrapSplashCall)));
                harmony.Patch(AccessTools.Method(typeof(MMVideoPlayer), nameof(MMVideoPlayer.Play)),
                    prefix: new HarmonyMethod(typeof(StartupMovie), nameof(NativeVideoPrefix)));
            }
            catch { harmony.UnpatchSelf(); current = null; throw; }
        }

        private static void PreparePrefix()
        {
            if (current == null || current.disposed) return;
            try { current.Prepare(); }
            catch (Exception failure) { current.error = failure.Message; current.Cleanup(); }
        }

        private static IEnumerable<CodeInstruction> WrapSplashCall(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(SplashRoutine))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(StartupMovie), nameof(RunWithMovie));
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("The startup splash routine call has changed.");
        }

        private static IEnumerator RunWithMovie(LoadMainMenu owner)
        {
            var native = (IEnumerator)SplashRoutine.Invoke(owner, null);
            current?.logger.LogInfo("CultCraft startup coroutine wrapper attached.");
            return current != null && !current.disposed ? current.InsertMovie(owner, native) : native;
        }

        private IEnumerator InsertMovie(LoadMainMenu owner, IEnumerator native)
        {
            try
            {
                while (native.MoveNext())
                {
                    object wait = native.Current;
                    // The startup coroutine yields the Massive Monster Spine
                    // animation before starting the Devolver video and audio.
                    bool massiveMonster = wait is IEnumerator && owner != null &&
                        owner.spine != null && owner.spine.gameObject.activeInHierarchy;
                    yield return wait;
                    if (massiveMonster && !attempted && !disposed && owner != null)
                    {
                        logger.LogInfo("CultCraft Massive Monster animation completed; inserting Mojang video.");
                        yield return GuardedPlayback(owner);
                    }
                }
            }
            finally { (native as IDisposable)?.Dispose(); Cleanup(); }
        }

        private IEnumerator GuardedPlayback(LoadMainMenu owner)
        {
            IEnumerator movie = Play(owner);
            try
            {
                while (!disposed)
                {
                    bool advanced;
                    try { advanced = movie.MoveNext(); }
                    catch (Exception failure)
                    {
                        error = failure.Message;
                        logger.LogWarning("CultCraft startup movie skipped: " + error);
                        break;
                    }
                    if (!advanced) break;
                    yield return movie.Current;
                }
            }
            finally { (movie as IDisposable)?.Dispose(); }
        }

        private void Prepare()
        {
            if (video != null || attempted) return;
            if (!File.Exists(path)) { error = "The packaged Mojang video is missing."; return; }
            playback = new GameObject("CultCraft startup video");
            UnityEngine.Object.DontDestroyOnLoad(playback);
            video = playback.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.isLooping = false;
            video.source = VideoSource.Url;
            video.url = new Uri(path).AbsoluteUri;
            video.renderMode = VideoRenderMode.APIOnly;
            video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.errorReceived += VideoError;
            video.loopPointReached += VideoEnd;
            video.started += VideoStarted;
            video.Prepare();
        }

        private IEnumerator Play(LoadMainMenu owner)
        {
            attempted = true;
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!disposed && video != null && !video.isPrepared && error == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (disposed || video == null || !video.isPrepared || error != null)
            {
                error = error ?? "The Mojang video did not prepare within six seconds.";
                logger.LogWarning("CultCraft startup movie skipped: " + error);
                Cleanup();
                yield break;
            }
            bool nativeActive = owner.spine != null && owner.spine.gameObject.activeSelf;
            try
            {
                if (owner.spine != null) owner.spine.gameObject.SetActive(false);
                CreateOverlay();
                var settings = SettingsManager.Settings;
                audioVolume = settings != null && settings.Audio != null
                    ? Mathf.Clamp01(settings.Audio.MasterVolume) * Mathf.Clamp01(settings.Audio.SFXVolume) : 1f;
                PrepareSoundtrack();
                video.Play();
                logger.LogInfo("CultCraft startup movie started after Massive Monster; FMOD volume=" + audioVolume.ToString("F2"));
                firstTime = -1;
                float playbackDeadline = Time.realtimeSinceStartup + Mathf.Clamp((float)video.length + 5f, 10f, 120f);
                while (!disposed && video != null && !completed && error == null && Time.realtimeSinceStartup < playbackDeadline)
                {
                    if (video.texture != null)
                    {
                        if (image.texture != video.texture) image.texture = video.texture;
                        if (video.frame >= 0)
                        {
                            if (firstTime < 0) firstTime = video.time;
                            lastTime = video.time;
                            visibleFrames++;
                        }
                    }
                    SynchronizeSoundtrack();
                    yield return null;
                }
                finished = completed && error == null && visibleFrames > 1 && lastTime > firstTime;
                if (!finished) error = error ?? "The Mojang video did not finish normally.";
            }
            finally
            {
                Cleanup();
                if (owner != null && owner.spine != null) owner.spine.gameObject.SetActive(nativeActive);
            }
        }

        private void CreateOverlay()
        {
            overlay = new GameObject("CultCraft Mojang fullscreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(overlay);
            Canvas canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var background = new GameObject("Black background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(overlay.transform, false);
            background.GetComponent<Image>().color = Color.black;
            background.GetComponent<Image>().raycastTarget = false;
            Stretch(background.GetComponent<RectTransform>());
            var picture = new GameObject("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            picture.transform.SetParent(background.transform, false);
            image = picture.GetComponent<RawImage>();
            image.raycastTarget = false;
            Stretch(picture.GetComponent<RectTransform>());
            aspect = picture.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = video.height > 0 ? (float)video.width / video.height : 16f / 9f;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void VideoError(VideoPlayer source, string message) { error = message; }
        private void VideoEnd(VideoPlayer source) { completed = true; }

        private void PrepareSoundtrack()
        {
            string wav = Path.ChangeExtension(path, ".wav");
            try
            {
                if (!File.Exists(wav)) { audioError = "The packaged soundtrack is missing."; return; }
                FMOD.RESULT result = FMODUnity.RuntimeManager.CoreSystem.createSound(wav, FMOD.MODE.CREATESAMPLE, out soundtrack);
                if (result != FMOD.RESULT.OK) { audioError = result.ToString(); return; }
                audioPlayback = new PreparedSoundPlayback(FMODUnity.RuntimeManager.CoreSystem, soundtrack, null, "Mojang intro");
            }
            catch (Exception failure) { audioError = failure.Message; }
        }

        private void VideoStarted(VideoPlayer source)
        {
            audioStarted = audioPlayback != null && audioPlayback.Play(audioVolume, 1f, out audioChannel);
            if (audioStarted) audioChannel.setPosition((uint)(source.time * 1000.0), FMOD.TIMEUNIT.MS);
            else if (audioVolume > 0f) audioError = audioError ?? "FMOD did not start the soundtrack channel.";
            logger.LogInfo("CultCraft Mojang soundtrack: native FMOD channel playing=" + audioStarted +
                ", volume=" + audioVolume.ToString("F2") + ", error=" + (audioError ?? "none"));
        }

        private void SynchronizeSoundtrack()
        {
            if (!audioStarted || !audioChannel.hasHandle() || video == null) return;
            if (audioChannel.getPosition(out uint position, FMOD.TIMEUNIT.MS) == FMOD.RESULT.OK &&
                Math.Abs(position - video.time * 1000.0) > 100.0)
                audioChannel.setPosition((uint)(video.time * 1000.0), FMOD.TIMEUNIT.MS);
        }

        private static void NativeVideoPrefix(string _FileName)
        {
            if (current == null || current.disposed || current.reported || _FileName != DevolverVideo) return;
            current.reported = true;
            string detail = "Massive Monster -> Mojang -> Devolver; Mojang frames=" + current.visibleFrames +
                ", elapsed video time=" + (current.lastTime - current.firstTime).ToString("F2") +
                ", complete=" + current.finished + ", FMOD audio=" + current.audioStarted +
                ", error=" + (current.error ?? current.audioError ?? "none");
            current.logger.LogInfo("CultCraft startup sequence: " + detail);
            MeltyTest.Report(CheckId, current.attempted && current.finished &&
                (current.audioStarted || current.audioVolume <= 0f && current.audioError == null), detail);
        }

        private void Cleanup()
        {
            if (audioChannel.hasHandle()) audioChannel.stop();
            audioChannel = default(FMOD.Channel);
            audioPlayback?.Invalidate();
            audioPlayback = null;
            if (soundtrack.hasHandle()) soundtrack.release();
            soundtrack = default(FMOD.Sound);
            if (overlay != null) { overlay.SetActive(false); UnityEngine.Object.Destroy(overlay); overlay = null; }
            if (video != null)
            {
                video.errorReceived -= VideoError;
                video.loopPointReached -= VideoEnd;
                video.started -= VideoStarted;
                video.Stop();
                video = null;
            }
            if (playback != null) { UnityEngine.Object.Destroy(playback); playback = null; }
            image = null;
            aspect = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            harmony.UnpatchSelf();
            Cleanup();
            if (current == this) current = null;
        }
    }
}
