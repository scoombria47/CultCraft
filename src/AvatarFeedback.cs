using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Presentation reacts to the native hit event and the native B action.</summary>
    public sealed class AvatarFeedback : IDisposable
    {
        private static readonly System.Collections.Generic.List<AvatarFeedback> instances = new System.Collections.Generic.List<AvatarFeedback>();
        private readonly MinecraftAssets assets;
        private static readonly Harmony harmony = new Harmony("cultcraft.avatar.feedback");
        private PlayerFarming player;
        private Health health;
        private float lastModernSound = -10f;
        private bool disposed;
        public float RedUntil { get; private set; }
        public float CrouchUntil { get; private set; }
        public int ConfirmedHits { get; private set; }
        public int DeathSerial { get; private set; }

        public AvatarFeedback(MinecraftAssets source)
        {
            assets = source;
            instances.Add(this);
            if(instances.Count>1)return;
            try
            {
                // Replaces this one native coroutine only. Its normal input, eligibility,
                // state ownership and duration are preserved; other fleece abilities remain native.
                var bleat = AccessTools.Method(typeof(PlayerFarming), "BleatRoutine");
                if (bleat == null) throw new MissingMethodException("PlayerFarming.BleatRoutine");
                harmony.Patch(bleat, prefix: new HarmonyMethod(typeof(AvatarFeedback), "BleatPrefix"));
                var getHitSound = AccessTools.Method(typeof(AudioManager), "PlayOneShot", new[] { typeof(string), typeof(Vector3) });
                if (getHitSound == null) throw new MissingMethodException("AudioManager.PlayOneShot(string, Vector3)");
                harmony.Patch(getHitSound, prefix: new HarmonyMethod(typeof(AvatarFeedback), "NativeHurtSoundPrefix"));
            }
            catch { harmony.UnpatchSelf(); instances.Remove(this); throw; }
        }

        public void Bind(PlayerFarming value)
        {
            if (disposed || player == value) return;
            if (health != null) { health.OnHit -= OnHit; health.OnDie -= OnDie; }
            player = value;
            health = player == null ? null : player.GetComponent<Health>();
            if (health != null) { health.OnHit += OnHit; health.OnDie += OnDie; }
            RedUntil = CrouchUntil = 0f;
        }

        private void OnHit(GameObject attacker, Vector3 location, Health.AttackTypes attackType, bool fromBehind)
        {
            // OnHit is raised by native Health after a real accepted attack. No HP is assigned here.
            RedUntil = Time.unscaledTime + Spec.Visual("damage_flash_duration").Value;
            ConfirmedHits++;
            PlayModern();
        }

        private void OnDie(GameObject attacker,Vector3 location,Health victim,Health.AttackTypes attackType,Health.AttackFlags flags)
        { DeathSerial++; }

        private bool Play(string id)
        {
            if (disposed) return false;
            var settings = SettingsManager.Settings;
            float volume = settings != null && settings.Audio != null ?
                Spec.Visual("feedback_volume").Value * Mathf.Clamp01(settings.Audio.MasterVolume) * Mathf.Clamp01(settings.Audio.SFXVolume) : 0f;
            return assets.TryPlaySound(id, volume);
        }

        private bool PlayModern()
        {
            // The native sound and accepted OnHit event can arrive in either order.
            if (Time.unscaledTime - lastModernSound < 0.1f) return true;
            if (!Play("hurt_modern")) return false;
            lastModernSound = Time.unscaledTime;
            return true;
        }

        private static bool NativeHurtSoundPrefix(string __0)
        {
            // Suppress the native event only after actual FMOD playback succeeded.
            if(__0!="event:/player/gethit")return true;
            foreach(var current in instances)if(current.player!=null&&current.PlayModern())return false;
            return true;
        }

        private static bool BleatPrefix(PlayerFarming __instance, ref IEnumerator __result)
        {
            foreach(var current in instances)
                if(!current.disposed&&__instance==current.player){__result=current.CrouchRoutine(__instance);return false;}
            return true;
        }

        private IEnumerator CrouchRoutine(PlayerFarming owner)
        {
            owner.state.CURRENT_STATE = StateMachine.State.CustomAnimation;
            CrouchUntil = Time.unscaledTime + Spec.Visual("bleat_crouch_hold").Value;
            yield return new WaitForEndOfFrame();
            Play("hurt_classic");
            yield return new WaitForSeconds(Spec.Visual("bleat_duration").Value);
            CrouchUntil = 0f;
            if (owner != null && owner.state != null && owner.state.CURRENT_STATE == StateMachine.State.CustomAnimation)
                owner.state.CURRENT_STATE = StateMachine.State.Idle;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (health != null) { health.OnHit -= OnHit; health.OnDie -= OnDie; }
            instances.Remove(this);
            if(instances.Count==0)harmony.UnpatchSelf();
        }
    }
}
