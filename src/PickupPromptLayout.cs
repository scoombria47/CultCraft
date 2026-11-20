using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using src.UI.Prompts;
using UnityEngine;

namespace CultCraft
{
    internal sealed class PickupPromptLayout : IDisposable
    {
        private static PickupPromptLayout current;
        private readonly Harmony harmony = new Harmony("cultcraft.pickup.prompt.layout");
        private readonly List<PickupInfoPosition> cards = new List<PickupInfoPosition>();
        private static readonly FieldInfo ContainerField = AccessTools.Field(typeof(UIPromptBase), "_containerRectTransform");

        internal PickupPromptLayout()
        {
            current = this;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(UIPromptBase), nameof(UIPromptBase.Init)),
                    prefix: new HarmonyMethod(typeof(PickupPromptLayout), nameof(InitPrefix)),
                    postfix: new HarmonyMethod(typeof(PickupPromptLayout), nameof(InitPostfix)));
                harmony.Patch(AccessTools.Method(typeof(UIRelicPickupPromptController), nameof(UIRelicPickupPromptController.Show),
                    new[] { typeof(RelicData), typeof(RelicData), typeof(PlayerFarming), typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(PickupPromptLayout), nameof(RelicPrefix)));
                harmony.Patch(AccessTools.Method(typeof(UIPromptBase), nameof(UIPromptBase.UpdateAllPromptPositions)),
                    postfix: new HarmonyMethod(typeof(PickupPromptLayout), nameof(PositionsPostfix)));
            }
            catch { harmony.UnpatchSelf(); current = null; throw; }
        }

        private static void InitPrefix(UIPromptBase __instance)
        {
            if (current == null || __instance.OffsetContainer != null) return;
            var container = ContainerField.GetValue(__instance) as RectTransform;
            if (container == null || !(container.parent is RectTransform parent)) return;
            var wrapper = new GameObject("CultCraft pickup info offset", typeof(RectTransform)).GetComponent<RectTransform>();
            wrapper.SetParent(parent, false);
            wrapper.anchorMin = Vector2.zero;
            wrapper.anchorMax = Vector2.one;
            wrapper.pivot = parent.pivot;
            wrapper.offsetMin = wrapper.offsetMax = Vector2.zero;
            container.SetParent(wrapper, false);
            __instance.OffsetContainer = wrapper;
        }

        private static void RelicPrefix(UIRelicPickupPromptController __instance, PlayerFarming playerFarming)
        {
            if (current != null && playerFarming != null && __instance.playerFarming != playerFarming)
                __instance.Init(playerFarming);
        }

        private static void InitPostfix(UIPromptBase __instance)
        {
            if (current == null || __instance.OffsetContainer == null) return;
            if (!__instance.TryGetComponent<PickupInfoPosition>(out var position))
            {
                position = __instance.gameObject.AddComponent<PickupInfoPosition>();
                position.Initialise(__instance, ContainerField.GetValue(__instance) as RectTransform);
                current.cards.Add(position);
            }
            position.CaptureNativeOffset();
        }

        private static void PositionsPostfix()
        {
            if (current == null) return;
            for (int i = current.cards.Count - 1; i >= 0; i--)
            {
                PickupInfoPosition position = current.cards[i];
                if (position == null) current.cards.RemoveAt(i);
                else position.CaptureNativeOffset();
            }
        }

        public void Dispose()
        {
            harmony.UnpatchSelf();
            foreach (PickupInfoPosition card in cards)
                if (card != null) { card.Restore(); card.RemoveWrapper(); UnityEngine.Object.Destroy(card); }
            cards.Clear();
            if (current == this) current = null;
        }
    }

    [DefaultExecutionOrder(10000)]
    public sealed class PickupInfoPosition : MonoBehaviour
    {
        private UIPromptBase prompt;
        private RectTransform container;
        private RectTransform offset;
        private Canvas canvas;
        private CanvasGroup group;
        private Vector3 nativeOffset;
        private readonly Vector3[] corners = new Vector3[4];
        internal float LastGap { get; private set; }
        internal Rect VisibleBounds => container != null && canvas != null
            ? ScreenBounds(container, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, corners) : Rect.zero;

        internal void Initialise(UIPromptBase prompt, RectTransform container)
        {
            this.prompt = prompt;
            this.container = container;
            offset = prompt.OffsetContainer as RectTransform;
            canvas = prompt.GetComponentInParent<Canvas>();
            group = prompt.GetComponent<CanvasGroup>();
            CaptureNativeOffset();
        }

        internal void CaptureNativeOffset()
        {
            if (offset != null) nativeOffset = offset.localPosition;
        }

        internal void Restore()
        {
            if (offset != null) offset.localPosition = nativeOffset;
        }

        internal void RemoveWrapper()
        {
            if (offset == null || offset.name != "CultCraft pickup info offset" || container == null) return;
            Transform wrapper = offset;
            container.SetParent(wrapper.parent, false);
            if (prompt != null) prompt.OffsetContainer = null;
            UnityEngine.Object.Destroy(wrapper.gameObject);
            offset = null;
        }

        private void LateUpdate()
        {
            if (prompt == null || container == null || offset == null || canvas == null ||
                prompt.playerFarming == null || group != null && group.alpha <= 0f ||
                !(offset.parent is RectTransform parent)) return;
            Restore();
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect card = ScreenBounds(container, uiCamera, corners);
            float minimum = Hotbar.InteractionPromptMinimumY;
            Indicator indicator = prompt.playerFarming.indicator;
            if (indicator != null && indicator.IsActive && indicator.canvasGroup != null && indicator.canvasGroup.alpha > 0f)
            {
                minimum = Mathf.Max(minimum, PromptTop(indicator, corners) + 8f * Mathf.Max(1f, PixelUi.Scale));
            }
            LastGap = card.yMin - minimum;
            if (LastGap >= 0f) return;
            Vector2 bottom = new Vector2(card.center.x, card.yMin);
            Vector2 target = new Vector2(card.center.x, minimum);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, bottom, uiCamera, out Vector2 from) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, target, uiCamera, out Vector2 to))
            {
                offset.localPosition += (Vector3)(to - from);
                LastGap = ScreenBounds(container, uiCamera, corners).yMin - minimum;
            }
        }

        internal static float PromptTop(Indicator indicator, Vector3[] corners)
        {
            if (indicator.text == null) return 0f;
            Canvas canvas = indicator.text.GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            float top = ScreenBounds(indicator.text.rectTransform, camera, corners).yMax;
            if (indicator.BG != null && indicator.BG.isActiveAndEnabled)
                top = Mathf.Max(top, ScreenBounds(indicator.BG.rectTransform, camera, corners).yMax);
            if (indicator.ContainerImage != null && indicator.ContainerImage.isActiveAndEnabled)
                top = Mathf.Max(top, ScreenBounds(indicator.ContainerImage.rectTransform, camera, corners).yMax);
            return top;
        }

        internal static Rect ScreenBounds(RectTransform rect, Camera camera, Vector3[] corners)
        {
            rect.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
