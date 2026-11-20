using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using Lamb.UI;
using Lamb.UI.Assets;
using HarmonyLib;
using MMTools;
using src.Extensions;
using src.UI.Prompts;
using UnityEngine;

namespace CultCraft
{
    internal sealed class PickupPromptSelfTest
    {
        internal const string Id = "pickup-info-clearance";
        private readonly CameraRig camera;
        private readonly ManualLogSource logger;
        private readonly Action<string, bool, string> report;
        private bool started;
        internal bool Complete { get; private set; }

        internal PickupPromptSelfTest(CameraRig camera, ManualLogSource logger, Action<string, bool, string> report)
        { this.camera = camera; this.logger = logger; this.report = report; }

        internal void Tick(PlayerFarming player, bool ready)
        {
            if (started || !ready) return;
            started = true;
            player.StartCoroutine(RunChecked(player));
        }

        private IEnumerator RunChecked(PlayerFarming player)
        {
            IEnumerator run = Run(player);
            try
            {
                while (true)
                {
                    bool advanced;
                    try { advanced = run.MoveNext(); }
                    catch (Exception error)
                    {
                        logger.LogError("Pickup card test failed: " + error);
                        report(Id, false, error.Message);
                        break;
                    }
                    if (!advanced) break;
                    yield return run.Current;
                }
            }
            finally { (run as IDisposable)?.Dispose(); Complete = true; }
        }

        private IEnumerator Run(PlayerFarming player)
        {
            int originalScale = PixelUi.ScaleMode;
            bool originalView = camera.FirstPerson;
            Interactor interactor = player.interactor;
            bool enabled = interactor.enabled;
            Indicator indicator = player.indicator;
            UIPromptBase card = null;
            bool passed = true;
            var samples = new List<string>();
            try
            {
                interactor.enabled = false;
                indicator.SetGameObjectActive(true);
                indicator.text.text = I2.Loc.ScriptLocalization.Interactions.Equip;
                indicator.SecondaryText.text = I2.Loc.ScriptLocalization.Interactions.Recycle;
                indicator.HasSecondaryInteraction = true;
                indicator.Interactable = indicator.SecondaryInteractable = true;
                indicator.Reset();
                indicator.canvasGroup.alpha = 1f;
                for (int kind = 0; kind < 3; kind++)
                {
                    // Native scene changes can leave destroyed prompt entries in
                    // this fresh test world's static registry.
                    var registry = (List<UIPromptBase>)AccessTools.Field(typeof(UIPromptBase), "prompts").GetValue(null);
                    int removed = registry.RemoveAll(prompt => prompt == null);
                    logger.LogInfo("Pickup card test: kind=" + kind + ", removed stale native prompts=" + removed +
                        ", players=" + PlayerFarming.players.Count + ", valid indicator=" + (player.indicator != null));
                    var manager = MonoSingleton<UIManager>.Instance;
                    if (kind < 2)
                    {
                        var weapon = manager.WeaponPickPromptControllerTemplate.Instantiate();
                        card = weapon;
                        weapon.Init(player);
                        yield return null;
                        weapon.Show(player, kind == 0 ? EquipmentType.Axe_Poison : EquipmentType.Tentacles, 15f, 1.2f, 3);
                    }
                    else
                    {
                        var relic = manager.RelicPickupPromptControllerTemplate.Instantiate();
                        card = relic;
                        yield return null;
                        RelicData data = EquipmentManager.GetRelicData(RelicType.LightningStrike);
                        relic.Show(data, null, player);
                    }
                    yield return new WaitForSecondsRealtime(0.8f);
                    for (int view = 0; view < 2; view++)
                    {
                        if (camera.FirstPerson != (view == 1)) camera.Toggle();
                        for (int scale = 0; scale <= 3; scale++)
                        {
                            PixelUi.ScaleMode = scale;
                            yield return new WaitForSecondsRealtime(0.12f);
                            yield return new WaitForEndOfFrame();
                            var layout = card.GetComponent<PickupInfoPosition>();
                            float gap = layout == null ? float.NegativeInfinity : layout.LastGap;
                            Rect bounds = layout == null ? Rect.zero : layout.VisibleBounds;
                            float firstY = bounds.yMin;
                            for (int frame = 0; frame < 4; frame++) yield return null;
                            yield return new WaitForEndOfFrame();
                            bool stable = layout != null && Mathf.Abs(layout.VisibleBounds.yMin - firstY) < 0.6f;
                            bool inside = bounds.height > 20f && bounds.yMin >= 0 && bounds.yMax <= Screen.height + 1f;
                            passed &= gap >= -0.6f && stable && inside;
                            samples.Add(kind + "/" + view + "/" + scale + "=" + gap.ToString("F1") +
                                ", stable=" + stable + ", onscreen=" + inside);
                            if (scale == 2) yield return new WaitForSecondsRealtime(2.1f);
                        }
                    }
                    logger.LogInfo("Pickup card samples after kind " + kind + ": " + string.Join("; ", samples));
                    card.Hide(true);
                    UnityEngine.Object.Destroy(card.gameObject);
                    card = null;
                }
                string detail = "Native weapon/curse/relic cards; top-down/FPS; Auto/1/2/3 gaps " + string.Join("; ", samples);
                logger.LogInfo("CultCraft pickup card clearance: " + detail);
                report(Id, passed, detail);
            }
            finally
            {
                if (card != null) { card.Hide(true); UnityEngine.Object.Destroy(card.gameObject); }
                PixelUi.ScaleMode = originalScale;
                if (camera.FirstPerson != originalView) camera.Toggle();
                indicator.SetGameObjectActive(false);
                interactor.PreviousInteraction = null;
                interactor.enabled = enabled;
                Complete = true;
            }
        }
    }
}
