using System;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    // Real 3/wheel/LMB input, native inventory, and native heart fields in the isolated base.
    internal sealed class FoodSelfTest : IDisposable
    {
        private readonly Gameplay gameplay;
        private readonly ManualLogSource logger;
        private readonly Action<string, bool, string> report;
        private HealthPlayer health;
        private int index, uses, quantity, committedBeforePicker;
        private float hp, blue, black, spirit, beforeHp, beforeBlue, beforeBlack, started, nextScroll;
        private bool picking, eating, rearmPicker;
        private float nextPickerPress;
        public bool Complete { get; private set; }

        public FoodSelfTest(Gameplay gameplay, ManualLogSource logger, Action<string, bool, string> report)
        {
            this.gameplay = gameplay;
            this.logger = logger;
            this.report = report;
        }

        public void Tick(PlayerFarming player, bool ready)
        {
            if (Complete) return;
            if (health == null)
            {
                if (!ready || player.health == null) return;
                health = player.health;
                hp = health.HP;
                blue = health.BlueHearts;
                black = health.BlackHearts;
                spirit = health.SpiritHearts;
                foreach (ConsumableRow food in Spec.ConsumableRows)
                    Inventory.AddItem((InventoryItem.ITEM_TYPE)food.NativeId, 1, true);
                MeltyPlay.Point(0.5f, 0.5f);
                index = Spec.ConsumableRows.Length - 1;
                Begin();
                return;
            }
            ConsumableRow item = Spec.ConsumableRows[index];
            float now = Time.unscaledTime;
            if (now - started > 8f)
            {
                Finish(false, "Timed out using " + item.Id + ", selection=" + gameplay.SelectedFoodId +
                    ", picker=" + gameplay.FoodPickerOpen + ", progress=" + gameplay.EatingProgress);
                return;
            }
            if (picking)
            {
                if (rearmPicker) { MeltyPlay.Hold("3", true); rearmPicker = false; return; }
                if (!gameplay.FoodPickerOpen)
                {
                    // Repeat a real virtual key transition if a native UI/arrival
                    // swallowed the first hold. Never choose a food programmatically.
                    if (now >= nextPickerPress) { MeltyPlay.Hold("3", false); rearmPicker = true; nextPickerPress = now + 0.9f; }
                    return;
                }
                if (gameplay.SelectedFoodId != committedBeforePicker)
                {
                    Finish(false, "Food selection was committed before releasing 3.");
                    return;
                }
                if (gameplay.PreviewFoodId != item.NativeId)
                {
                    if (now >= nextScroll) { MeltyPlay.Scroll(120f); nextScroll = now + 0.16f; }
                    return;
                }
                MeltyPlay.Hold("3", false);
                picking = false;
                return;
            }
            if (!eating)
            {
                if (gameplay.FoodPickerOpen || !ready) return;
                if (gameplay.SelectedFoodId != item.NativeId)
                {
                    Finish(false, "Releasing 3 did not commit the preview for " + item.Id);
                    return;
                }
                eating = true;
                MeltyPlay.Button(0, true);
                return;
            }
            if (gameplay.FoodsEaten <= uses) return;

            float expectedRed = item.Heart == "red" ? item.Healing : 0f;
            float expectedBlue = item.Heart == "blue" ? item.Healing : 0f;
            float expectedBlack = item.Heart == "diseased" ? item.Healing : 0f;
            bool valid = Gameplay.CountFood(item) == quantity - 1 &&
                Mathf.Approximately(health.HP - beforeHp, expectedRed) &&
                Mathf.Approximately(health.BlueHearts - beforeBlue, expectedBlue) &&
                Mathf.Approximately(health.BlackHearts - beforeBlack, expectedBlack);
            string detail = item.Id + ", count " + quantity + " -> " + Gameplay.CountFood(item) +
                ", red/blue/diseased=" + (health.HP - beforeHp) + "/" + (health.BlueHearts - beforeBlue) + "/" + (health.BlackHearts - beforeBlack);
            if (!valid) { Finish(false, detail); return; }
            logger.LogInfo("Native food picker and effect verified: " + detail);
            index--;
            if (index < 0) Finish(true, "All eight foods selected and consumed through real input.");
            else Begin();
        }

        private void Begin()
        {
            ConsumableRow item = Spec.ConsumableRows[index];
            health.HP = health.totalHP - (item.Heart == "red" ? item.Healing : 0f);
            health.BlueHearts = blue;
            health.BlackHearts = black;
            beforeHp = health.HP;
            beforeBlue = health.BlueHearts;
            beforeBlack = health.BlackHearts;
            uses = gameplay.FoodsEaten;
            quantity = Gameplay.CountFood(item);
            committedBeforePicker = gameplay.SelectedFoodId;
            started = nextScroll = Time.unscaledTime;
            nextPickerPress = Time.unscaledTime + 0.9f; rearmPicker = false;
            picking = true;
            eating = false;
            MeltyPlay.Button(0, false);
            MeltyPlay.Hold("3", true);
        }

        private void Finish(bool pass, string detail)
        {
            Dispose();
            Complete = true;
            report("minecraft-food", pass, pass ? null : detail);
        }

        public void Dispose()
        {
            if (health == null) return;
            health.HP = hp;
            health.BlueHearts = blue;
            health.BlackHearts = black;
            health.SpiritHearts = spirit;
            MeltyPlay.Hold("3", false);
            MeltyPlay.Button(0, false);
            gameplay.SelectSlot(0);
            health = null;
        }
    }
}
