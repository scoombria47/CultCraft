using System;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    // Drives the actual hotbar with 3, the mouse wheel and LMB in the isolated game.
    internal sealed class FoodSelfTest : IDisposable
    {
        internal const string SurvivalId = "minecraft-survival-food";

        private sealed class FoodCase
        {
            internal readonly ConsumableRow Food;
            internal readonly float Hunger, Nutrition, Red, Blue, Black, StartingBlue;
            internal readonly bool Survival, FullHealth;
            internal bool Consumed => Nutrition + Red + Blue + Black > 0f;

            internal FoodCase(string id, float nutrition = 0f, float red = 0f, float blue = 0f, float black = 0f,
                float hunger = 45f, float startingBlue = 0f, bool survival = true, bool fullHealth = false)
            {
                Food = Spec.Consumable(id);
                Hunger = hunger;
                Nutrition = nutrition;
                Red = red;
                Blue = blue;
                Black = black;
                StartingBlue = startingBlue;
                Survival = survival;
                FullHealth = fullHealth;
            }
        }

        private static readonly FoodCase[] SettlementCases =
        {
            new FoodCase("berry", nutrition: 13f),
            new FoodCase("tuna_cotl", nutrition: 25f, fullHealth: true),
            new FoodCase("raw_beef", nutrition: 25f, hunger: 92f),
            new FoodCase("follower_meat"),
            new FoodCase("blowfish"),
            new FoodCase("raw_beef", survival: false)
        };

        private static readonly FoodCase[] SurvivalDungeonCases =
        {
            new FoodCase("raw_beef", nutrition: 25f, red: 2f),
            new FoodCase("morsel", nutrition: 13f, red: 1f),
            new FoodCase("camellia", nutrition: 13f, red: 1f),
            new FoodCase("salmon_cotl", nutrition: 13f, red: 1f),
            new FoodCase("follower_meat", nutrition: 13f, blue: 1f),
            new FoodCase("follower_meat", nutrition: 13f, blue: 1f, startingBlue: 1f),
            new FoodCase("follower_meat", nutrition: 13f, startingBlue: 2f),
            new FoodCase("follower_meat", startingBlue: 2f, survival: false),
            new FoodCase("follower_meat", nutrition: 13f, startingBlue: 4f),
            new FoodCase("blowfish", nutrition: 25f, black: 2f),
            new FoodCase("berry", red: 1f, survival: false)
        };

        private static readonly FoodCase[] StandardDungeonCases = Spec.ConsumableRows
            .Select(food => new FoodCase(food.Id, survival: false,
                red: food.Heart == "red" ? food.Healing : 0f,
                blue: food.Heart == "blue" ? food.Healing : 0f,
                black: food.Heart == "diseased" ? food.Healing : 0f)).ToArray();

        private readonly Gameplay gameplay;
        private readonly ManualLogSource logger;
        private readonly Action<string, bool, string> report;
        private readonly bool survivalProof;
        private HealthPlayer health;
        private DataManager data;
        private int index, uses, quantity, dungeonQuantity, committedBeforePicker;
        private float originalHp, originalBlue, originalBlack, originalSpirit, originalHunger, originalTimer;
        private bool originalSurvival;
        private float beforeHp, lastHunger, started, eatingStarted, nextScroll, nextPickerPress;
        private bool picking, eating, rearmPicker;
        public bool SettlementComplete { get; private set; }
        public bool Complete { get; private set; }

        public FoodSelfTest(Gameplay gameplay, ManualLogSource logger, Action<string, bool, string> report, bool survivalProof)
        {
            this.gameplay = gameplay;
            this.logger = logger;
            this.report = report;
            this.survivalProof = survivalProof;
        }

        public void TickSettlement(PlayerFarming player, bool ready)
        {
            if (!SettlementComplete) Tick(player, ready, SettlementCases, settlement: true);
        }

        public void TickDungeon(PlayerFarming player, bool ready)
        {
            if (!Complete) Tick(player, ready, survivalProof ? SurvivalDungeonCases : StandardDungeonCases, settlement: false);
        }

        private void Tick(PlayerFarming player, bool ready, FoodCase[] cases, bool settlement)
        {
            if (health == null)
            {
                if (!ready || player?.health == null || DataManager.Instance == null) return;
                data = DataManager.Instance;
                if (survivalProof && !data.SurvivalModeActive)
                {
                    Finish(false, "The isolated game did not enter the native survival mode.");
                    return;
                }
                health = player.health;
                originalHp = health.HP;
                originalBlue = health.BlueHearts;
                originalBlack = health.BlackHearts;
                originalSpirit = health.SpiritHearts;
                originalHunger = data.SurvivalMode_Hunger;
                originalSurvival = data.SurvivalModeActive;
                originalTimer = TimeManager.SurvivalDamagedTimer;
                MeltyPlay.Point(0.5f, 0.5f);
                Begin(cases[index], settlement);
                return;
            }

            FoodCase sample = cases[index];
            int foodId = sample.Food.NativeId;
            float now = Time.unscaledTime;
            if (now - started > 10f)
            {
                Finish(false, "Timed out using " + sample.Food.Id + ", selection=" + gameplay.SelectedFoodId +
                    ", picker=" + gameplay.FoodPickerOpen + ", progress=" + gameplay.EatingProgress);
                return;
            }
            if (picking)
            {
                if (rearmPicker) { MeltyPlay.Hold("3", true); rearmPicker = false; return; }
                if (!gameplay.FoodPickerOpen)
                {
                    if (now >= nextPickerPress) { MeltyPlay.Hold("3", false); rearmPicker = true; nextPickerPress = now + 0.9f; }
                    return;
                }
                if (gameplay.SelectedFoodId != committedBeforePicker)
                {
                    Finish(false, "Food selection committed before releasing 3.");
                    return;
                }
                if (gameplay.PreviewFoodId != foodId)
                {
                    if (now >= nextScroll) { MeltyPlay.Scroll(120f); nextScroll = now + 0.18f; }
                    return;
                }
                MeltyPlay.Hold("3", false);
                picking = false;
                return;
            }
            if (!eating)
            {
                if (gameplay.FoodPickerOpen || !ready) return;
                if (gameplay.SelectedFoodId != foodId)
                {
                    Finish(false, "Releasing 3 did not commit " + sample.Food.Id);
                    return;
                }
                eating = true;
                eatingStarted = Time.time;
                lastHunger = data.SurvivalMode_Hunger;
                MeltyPlay.Button(0, true);
                return;
            }

            int eaten = gameplay.FoodsEaten - uses;
            if (eaten == 0 && (sample.Consumed || Time.time - eatingStarted < sample.Food.UseSeconds + 0.8f))
            {
                lastHunger = data.SurvivalMode_Hunger;
                return;
            }

            float red = health.HP - beforeHp, blue = health.BlueHearts - sample.StartingBlue, black = health.BlackHearts;
            int left = Gameplay.CountFood(sample.Food);
            int dungeonLeft = Inventory.GetDungeonItemByType(foodId)?.quantity ?? 0;
            float expectedHunger = Mathf.Min(100f, lastHunger + sample.Nutrition);
            bool correctHunger = Mathf.Abs(data.SurvivalMode_Hunger - expectedHunger) < 0.75f;
            int spent = sample.Consumed ? 1 : 0;
            bool valid = eaten == spent && left == quantity - spent && dungeonLeft == Mathf.Max(0, dungeonQuantity - spent)
                && Mathf.Approximately(red, sample.Red) && Mathf.Approximately(blue, sample.Blue)
                && Mathf.Approximately(black, sample.Black) && correctHunger;
            string detail = (settlement ? "settlement" : "dungeon") + " " + sample.Food.Id +
                ", food=" + quantity + "->" + left + ", dungeon=" + dungeonQuantity + "->" + dungeonLeft +
                ", red/blue/diseased delta=" + red + "/" + blue + "/" + black +
                ", hunger=" + lastHunger.ToString("F2") + "->" + data.SurvivalMode_Hunger.ToString("F2") +
                " (expected " + expectedHunger.ToString("F2") + ")";
            if (!valid) { Finish(false, detail); return; }

            logger.LogInfo("Native food verified: " + detail);
            if (++index < cases.Length) { Begin(cases[index], settlement); return; }
            if (settlement)
            {
                Dispose();
                index = 0;
                SettlementComplete = true;
            }
            else Finish(true, "Native inventory, heart fields and hunger verified through the actual food picker and LMB.");
        }

        private void Begin(FoodCase sample, bool settlement)
        {
            data.SurvivalModeActive = sample.Survival;
            data.SurvivalMode_Hunger = sample.Hunger;
            health.HP = sample.FullHealth ? health.totalHP : health.totalHP - 2f;
            health.BlueHearts = sample.StartingBlue;
            health.BlackHearts = 0f;
            beforeHp = health.HP;
            uses = gameplay.FoodsEaten;
            Inventory.AddItem(sample.Food.NativeId, 1, ForceNormalInventory: settlement);
            quantity = Gameplay.CountFood(sample.Food);
            dungeonQuantity = Inventory.GetDungeonItemByType(sample.Food.NativeId)?.quantity ?? 0;
            committedBeforePicker = gameplay.SelectedFoodId;
            started = nextScroll = Time.unscaledTime;
            nextPickerPress = started + 0.9f;
            rearmPicker = false;
            picking = true;
            eating = false;
            MeltyPlay.Button(0, false);
            MeltyPlay.Hold("3", true);
        }

        private void Finish(bool pass, string detail)
        {
            Dispose();
            Complete = SettlementComplete = true;
            report(survivalProof ? SurvivalId : "minecraft-food", pass, detail);
        }

        public void Dispose()
        {
            if (health == null) return;
            health.HP = originalHp;
            health.BlueHearts = originalBlue;
            health.BlackHearts = originalBlack;
            health.SpiritHearts = originalSpirit;
            data.SurvivalModeActive = originalSurvival;
            data.SurvivalMode_Hunger = originalHunger;
            TimeManager.SurvivalDamagedTimer = originalTimer;
            MeltyPlay.Hold("3", false);
            MeltyPlay.Button(0, false);
            gameplay.SelectSlot(0);
            health = null;
        }
    }
}
