using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CultCraft
{
    /// <summary>A small persistent selection bar; quantities always come from the native inventory.</summary>
    public sealed class Hotbar
    {
        private readonly MinecraftAssets assets;
        private readonly Gameplay gameplay;
        private readonly MinecraftLocalization language;
        private readonly PixelUi pixels;
        private float lastWheel;
        private float foodHoldStarted = -1f;
        private float handHoldStarted=-1f;
        private bool emptyHand,previewEmptyHand;
        private int foodIndex, previewIndex;
        private PlayerFarming labelOwner;
        private string labelItem;
        private int labelSlot = -1, labelEnchantment;
        private float labelUntil;
        private string hintLanguage, combatHint, peacefulHint;
        private float hintWidth;
        private readonly List<int> availableFood = new List<int>(Spec.ConsumableRows.Length);
        public int SelectedSlot { get; private set; }
        public ConsumableRow Food { get { return Spec.ConsumableRows[foodIndex]; } }
        internal ConsumableRow PreviewFood { get { return Spec.ConsumableRows[previewIndex]; } }
        public bool FoodPickerOpen { get; private set; }
        public bool HandPickerOpen {get;private set;}
        public bool EmptyHand {get{return emptyHand&&!gameplay.CombatLocation;}}
        public string SelectedId { get { return SelectedSlot == 0 ? EmptyHand?"":gameplay.WeaponId : SelectedSlot == 1 ? "bow" : Food.Texture; } }

        public Hotbar(MinecraftAssets assets, Gameplay gameplay)
        {
            this.assets = assets;
            this.gameplay = gameplay;
            language = new MinecraftLocalization(assets);
            pixels = PixelUi.Shared(assets);
        }

        private static Rect BarRect()
        {
            float scale = PixelUi.Scale, width = PixelUi.V("hotbar_width"), height = PixelUi.V("hotbar_height");
            float bottomGap = Mathf.Max(PixelUi.V("hotbar_bottom_gap"), 10f * Mathf.Max(1f, scale - 1f) + 6f);
            return new Rect(Mathf.Round((Screen.width - width * scale) * 0.5f), Screen.height - height * scale - bottomGap, width * scale, height * scale);
        }
        public static Rect ScreenRect { get { return BarRect(); } }

        public bool PointerOverBar
        {
            get
            {
                if (Cursor.lockState == CursorLockMode.Locked) return false;
                Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
                return BarRect().Contains(new Vector2(mouse.x, Screen.height - mouse.y));
            }
        }

        public void TickInput(bool allowed)
        {
            if (!allowed) { CloseFoodPicker(); CloseHandPicker(); return; }
            var keyboard = Keyboard.current;
            bool holdingHand=keyboard!=null&&(keyboard.digit1Key.isPressed||keyboard.numpad1Key.isPressed);
            bool holdingFood = keyboard != null && (keyboard.digit3Key.isPressed || keyboard.numpad3Key.isPressed);
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) Select(0);
                else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) Select(1);
                else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) Select(2);
            }
            var mouse = Mouse.current;
            float now = Time.unscaledTime;
            float wheel = mouse == null || now < lastWheel ? 0f : mouse.scroll.ReadValue().y;
            int direction = Mathf.Abs(wheel) > 0.1f ? (wheel > 0f ? -1 : 1) : 0;
            if(SelectedSlot==0&&!gameplay.CombatLocation&&holdingHand)
            {
                if(handHoldStarted<0f)handHoldStarted=now;
                if(!HandPickerOpen&&(now-handHoldStarted>=0.28f||direction!=0))
                {
                    HandPickerOpen=true;
                    previewEmptyHand=emptyHand;
                }
                if(HandPickerOpen&&direction!=0)
                {
                    previewEmptyHand=!previewEmptyHand;
                    lastWheel=now+0.12f;
                }
                return;
            }
            if(HandPickerOpen&&SelectedSlot==0&&!gameplay.CombatLocation)
            {
                emptyHand=previewEmptyHand;
                CloseHandPicker();
                return;
            }
            CloseHandPicker();
            if (holdingFood && SelectedSlot == 2)
            {
                if (foodHoldStarted < 0f) foodHoldStarted = now;
                if (!FoodPickerOpen && (now - foodHoldStarted >= 0.28f || direction != 0))
                {
                    FoodPickerOpen = true;
                    previewIndex = foodIndex;
                }
                if (FoodPickerOpen)
                {
                    availableFood.Clear();
                    for (int i = 0; i < Spec.ConsumableRows.Length; i++)
                        if (Gameplay.CountFood(Spec.ConsumableRows[i]) > 0) availableFood.Add(i);
                    if (availableFood.Count > 0 && direction != 0)
                    {
                        int index = availableFood.IndexOf(previewIndex);
                        previewIndex = availableFood[index < 0 ? (direction < 0 ? availableFood.Count - 1 : 0) :
                            (index + direction + availableFood.Count) % availableFood.Count];
                    }
                    if (direction != 0) lastWheel = now + 0.12f;
                }
                return;
            }
            if (FoodPickerOpen)
            {
                if (SelectedSlot == 2 && Gameplay.CountFood(PreviewFood) > 0) foodIndex = previewIndex;
                CloseFoodPicker();
                return;
            }
            CloseFoodPicker();
            if (direction == 0) return;
            Select((SelectedSlot + direction + 3) % 3);
            lastWheel = now + 0.12f;
        }

        private void CloseFoodPicker() { FoodPickerOpen = false; foodHoldStarted = -1f; }
        private void CloseHandPicker(){HandPickerOpen=false;handHoldStarted=-1f;}
        public void ResetMainHand(){emptyHand=false;CloseHandPicker();Select(0);}

        public void Select(int slot)
        {
            if (slot < 0 || slot >= 3) return;
            if (slot != 2) CloseFoodPicker();
            if (slot != 0) CloseHandPicker();
            SelectedSlot = slot;
        }

        public void Draw(PlayerFarming player, bool interactive)
        {
            if (player == null || !player.gameObject.activeInHierarchy) return;
            Rect bar = BarRect();
            float scale = bar.width / PixelUi.V("hotbar_width");
            float textScale = Mathf.Max(1f, scale - 1f);
            float slotWidth = PixelUi.V("hotbar_slot_width");
            Color original = GUI.color;
            Texture2D background = assets.Texture("gui_hotbar");
            Texture2D selection = assets.Texture("gui_hotbar_selection");
            ConsumableRow visibleFood = FoodPickerOpen ? PreviewFood : Food;
            int visibleFoodCount = Gameplay.CountFood(visibleFood);
            GUI.color = Color.white;
            if (FoodPickerOpen) DrawFoodPicker(bar, scale, background, textScale);
            if (HandPickerOpen)
            {
                Rect alternative=new Rect(bar.x,bar.y-slotWidth*scale,(slotWidth+2f)*scale,bar.height);
                DrawPickerSlot(alternative,scale,background);
                if(previewEmptyHand)DrawPickerIcon(alternative,scale,gameplay.WeaponId);
                else pixels.Text(alternative,"–",textScale,Color.white);
            }
            if (background != null)
            {
                // Three native 20px slots, with the original right end cap retained.
                float first = slotWidth * 3f + 1f;
                GUI.DrawTextureWithTexCoords(new Rect(bar.x, bar.y, first * scale, bar.height), background, new Rect(0f, 0f, first / 182f, 1f), true);
                GUI.DrawTextureWithTexCoords(new Rect(bar.x + first * scale, bar.y, scale, bar.height), background, new Rect(181f / 182f, 0f, 1f / 182f, 1f), true);
            }
            if (selection != null) GUI.DrawTexture(new Rect(bar.x - scale + SelectedSlot * slotWidth * scale, bar.y - scale, 24f * scale, 23f * scale), selection, ScaleMode.StretchToFill, true);
            for (int i = 0; i < 3; i++)
            {
                Rect slot = new Rect(bar.x + (1f + i * slotWidth) * scale, bar.y + scale, slotWidth * scale, slotWidth * scale);
                string id = i == 0 ? (HandPickerOpen?previewEmptyHand:EmptyHand)?"":gameplay.WeaponId : i == 1 ? "bow" : visibleFood.Texture;
                Texture2D icon = id.Length==0?null:assets.Texture(id);
                GUI.color = i == 2 && visibleFoodCount == 0 ? new Color(0.5f, 0.5f, 0.5f, 0.75f) : Color.white;
                if (icon != null) GUI.DrawTexture(new Rect(bar.x + (3f + i * slotWidth) * scale, bar.y + 3f * scale, 16f * scale, 16f * scale), icon, ScaleMode.ScaleToFit, true);
                GUI.color = Color.white;
                pixels.Text(new Rect(slot.x + scale, slot.y, 8f * textScale, 9f * textScale), (i + 1).ToString(), textScale, Color.white, TextAnchor.MiddleLeft);
                if (i == 2) DrawCount(slot, visibleFoodCount, scale, textScale);
                if (i == 0 && Gameplay.IsCrossbowItem(id)) DrawCount(slot,gameplay.CrossbowAmmo,scale,textScale);
                if (interactive && Cursor.lockState != CursorLockMode.Locked && Event.current.type == EventType.MouseDown && Event.current.button == 0 && slot.Contains(Event.current.mousePosition))
                {
                    Select(i);
                    Event.current.Use();
                }
            }
            GUI.color = Color.white;
            if (PixelUi.ShowHints) DrawHint(bar, textScale);
            if (FoodPickerOpen||HandPickerOpen)
            {
                GUI.color = original;
                return;
            }
            DrawSelectedLabel(player, bar, textScale);
            if (gameplay.EatingProgress > 0f)
            {
                Fill(new Rect(bar.x, bar.y - 9f, bar.width, 5f), new Color(0.1f, 0.1f, 0.1f));
                Fill(new Rect(bar.x, bar.y - 9f, bar.width * gameplay.EatingProgress, 5f), new Color(0.5f, 0.9f, 0.35f));
            }
            GUI.color = original;
        }

        private void DrawHint(Rect bar, float textScale)
        {
            string code = MinecraftLocalization.LanguageCode;
            if (hintLanguage != code)
            {
                hintLanguage = code;
                combatHint = MinecraftLocalization.Ui("hotbar_hint");
                peacefulHint = MinecraftLocalization.Ui("peaceful_hotbar_hint");
                hintWidth = Mathf.Max(pixels.Measure(peacefulHint, 1f), pixels.Measure(combatHint, 1f));
            }

            float scale = Mathf.Min(textScale, Mathf.Max(1f, Screen.width - 24f) / (hintWidth + 1f));
            pixels.Text(new Rect(0f, bar.yMax + 2f, Screen.width, 10f * scale),
                gameplay.CombatLocation ? combatHint : peacefulHint, scale, new Color(0.91f, 0.91f, 0.91f));
        }

        private void DrawSelectedLabel(PlayerFarming player, Rect bar, float scale)
        {
            string item = gameplay.SelectedItemId;
            if(Gameplay.IsCrossbowItem(item))item="crossbow";
            int level = gameplay.DisplayedEnchantmentLevel;
            float now = Time.unscaledTime;
            if (labelOwner != player || labelSlot != SelectedSlot || labelItem != item || labelEnchantment != level)
            {
                labelOwner = player;
                labelSlot = SelectedSlot;
                labelItem = item;
                labelEnchantment = level;
                labelUntil = now + 2f;
            }
            float opacity = Mathf.Clamp01((labelUntil - now) / 0.5f);
            if (opacity <= 0f) return;

            bool foodSelected = SelectedSlot == 2 && !gameplay.BowHeld;
            string name = foodSelected ? FoodName(Food) : item.Length==0?MinecraftLocalization.Ui("empty"):language.Item(item);
            string detail = language.Enchantment(item == "bow" ? "power" : "sharpness", level);
            if (foodSelected)
            {
                detail = MinecraftLocalization.Ui("eat_hold");
                if (gameplay.FoodCount == 0) detail = MinecraftLocalization.Ui("no_food");
                else if (Food.Heart == "red" && player.health != null && player.health.HP >= player.health.totalHP)
                    detail = MinecraftLocalization.Ui("full_health");
            }
            pixels.Text(new Rect(0f, bar.y - PixelUi.V("name_line_offset") * scale, Screen.width, 12f * scale),
                name, scale, new Color(1f, 1f, 1f, opacity));
            pixels.Text(new Rect(0f, bar.y - PixelUi.V("enchantment_line_offset") * scale, Screen.width, 11f * scale),
                detail, scale, new Color(0.72f, 0.57f, 1f, opacity));
        }

        private static string FoodName(ConsumableRow food) { return InventoryItem.LocalizedName((InventoryItem.ITEM_TYPE)food.NativeId); }

        private static void DrawPickerSlot(Rect rect,float scale,Texture2D background)
        {
            if(background==null)return;
            GUI.DrawTextureWithTexCoords(new Rect(rect.x,rect.y,21f*scale,rect.height),background,new Rect(0f,0f,21f/182f,1f),true);
            GUI.DrawTextureWithTexCoords(new Rect(rect.x+21f*scale,rect.y,scale,rect.height),background,new Rect(181f/182f,0f,1f/182f,1f),true);
        }

        private void DrawPickerIcon(Rect rect,float scale,string id)
        {
            Texture2D icon=assets.Texture(id);
            if(icon!=null)GUI.DrawTexture(new Rect(rect.x+3f*scale,rect.y+3f*scale,16f*scale,16f*scale),icon,ScaleMode.ScaleToFit,true);
        }

        private void DrawFoodPicker(Rect bar, float scale, Texture2D background, float textScale)
        {
            float step = PixelUi.V("hotbar_slot_width") * scale;
            float x = bar.x + 2f * step;
            int selected = availableFood.IndexOf(previewIndex);
            int otherCount = availableFood.Count - (selected >= 0 ? 1 : 0);
            for (int above = Spec.ConsumableRows.Length - 1; above >= 1; above--)
            {
                float y = bar.y - above * step;
                if (y + bar.height < 0f) continue;
                if (background != null)
                {
                    GUI.color = above <= otherCount ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                    GUI.DrawTextureWithTexCoords(new Rect(x, y, step + scale, bar.height), background, new Rect(0f, 0f, 21f / 182f, 1f), true);
                    GUI.DrawTextureWithTexCoords(new Rect(x + step + scale, y, scale, bar.height), background, new Rect(181f / 182f, 0f, 1f / 182f, 1f), true);
                    GUI.color = Color.white;
                }
                if (above > otherCount) continue;
                int index = selected < 0 ? availableFood.Count - above : (selected - above + availableFood.Count) % availableFood.Count;
                ConsumableRow item = Spec.ConsumableRows[availableFood[index]];
                Texture2D icon = assets.Texture(item.Texture);
                if (icon != null) GUI.DrawTexture(new Rect(x + 3f * scale, y + 3f * scale, 16f * scale, 16f * scale), icon, ScaleMode.ScaleToFit, true);
                DrawCount(new Rect(x + scale, y + scale, step, step), Gameplay.CountFood(item), scale, textScale);
            }
        }

        private void DrawCount(Rect slot, int count, float scale, float textScale)
        {
            pixels.Text(new Rect(slot.x, slot.yMax - 8f * textScale, slot.width - scale, 8f * textScale),
                count.ToString(), textScale, Color.white, TextAnchor.MiddleRight);
        }

        private static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

    }
}
