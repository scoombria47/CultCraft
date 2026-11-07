using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CultCraft
{
    /// <summary>Presentation of the real native divine-inspiration XP and unspent ability points.</summary>
    public sealed class DevotionHud : IDisposable
    {
        private readonly MinecraftAssets assets;
        private readonly PixelUi pixels;
        private readonly Dictionary<CanvasGroup,float> originalAlpha=new Dictionary<CanvasGroup,float>();
        private readonly List<CanvasGroup> addedGroups=new List<CanvasGroup>();
        private float nextScan;
        private bool disposed;
        public int AvailableRevelations { get { return Math.Max(0,UpgradeSystem.AbilityPoints); } }
        public float Progress { get; private set; }
        public bool Visible { get; private set; }

        public DevotionHud(MinecraftAssets source)
        {
            assets=source;pixels=PixelUi.Shared(source);
            SceneManager.activeSceneChanged+=SceneChanged;
        }

        private void SceneChanged(Scene previous,Scene current)
        {
            RestoreNativeHud();Visible=false;Progress=0f;
        }

        private void RestoreNativeHud()
        {
            foreach(var entry in originalAlpha)if(entry.Key!=null)entry.Key.alpha=entry.Value;
            originalAlpha.Clear();nextScan=0f;
        }

        public void Draw(PlayerFarming player)
        {
            Visible=false;
            if(disposed)return;
            if(player==null||!player.gameObject.activeInHierarchy||DataManager.Instance==null||
                !DataManager.Instance.XPEnabled||(!GameManager.HasUnlockAvailable()&&!DataManager.Instance.DeathCatBeaten))
            {RestoreNativeHud();Progress=0f;return;}
            Texture2D background=assets.Texture("gui_xp_background"),fill=assets.Texture("gui_xp_progress");
            if(background==null||fill==null){RestoreNativeHud();return;}
            if(Time.unscaledTime>=nextScan)
            {
                nextScan=Time.unscaledTime+1f;
                var expired=new List<CanvasGroup>();
                foreach(CanvasGroup group in originalAlpha.Keys)if(group==null)expired.Add(group);
                foreach(CanvasGroup group in expired)originalAlpha.Remove(group);
                addedGroups.RemoveAll(group=>group==null);
                foreach(HUD_XP native in UnityEngine.Object.FindObjectsOfType<HUD_XP>(true))
                {
                    if(native==null)continue;
                    CanvasGroup group=native.GetComponent<CanvasGroup>();
                    if(group==null){group=native.gameObject.AddComponent<CanvasGroup>();addedGroups.Add(group);}
                    if(!originalAlpha.ContainsKey(group))originalAlpha.Add(group,group.alpha);
                }
            }
            // Keep native events, transitions, target XP and unlock logic running.
            foreach(var entry in originalAlpha)if(entry.Key!=null)entry.Key.alpha=0f;
            int index=Mathf.Min(DataManager.Instance.Level,Mathf.Max(DataManager.TargetXP.Count-1,0));
            int target=Math.Max(1,DataManager.GetTargetXP(index));
            Progress=Mathf.Clamp01((float)DataManager.Instance.XP/target);
            float scale=PixelUi.Scale;
            Rect bar=new Rect(Mathf.Round((Screen.width-182f*scale)*0.5f),Mathf.Round(PixelUi.V("xp_top_offset")*scale),182f*scale,5f*scale);
            Color original=GUI.color;GUI.color=Color.white;
            GUI.DrawTexture(bar,background,ScaleMode.StretchToFill,true);
            float filled=Mathf.Floor(182f*Progress);
            if(filled>0f)GUI.DrawTextureWithTexCoords(new Rect(bar.x,bar.y,filled*scale,bar.height),fill,new Rect(0f,0f,filled/182f,1f),true);
            pixels.Text(new Rect(bar.x,bar.y-11f*scale,bar.width,11f*scale),AvailableRevelations.ToString(),scale,new Color(0.5f,1f,0.125f));
            GUI.color=original;Visible=true;
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;SceneManager.activeSceneChanged-=SceneChanged;
            RestoreNativeHud();Visible=false;Progress=0f;
            foreach(CanvasGroup group in addedGroups)if(group!=null)UnityEngine.Object.Destroy(group);
            addedGroups.Clear();pixels.Dispose();
        }
    }
}
