using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CultCraft
{
    internal sealed partial class MinecraftSettingsUi
    {
        private const float LanguageRowHeight = 24f;
        private const int LanguageDone = 12, LanguageBack = 13;
        private bool languageMenuOpen, languageScrollDrag;
        private int selectedLanguageRow, firstLanguageRow, visibleLanguageRows, pressedLanguageRow = -1;
        private int wheelEventFrame = -1;
        private float languageScrollGrab;
        private int[] languageIndexes;
        private string[] languageLabels;
        private Rect languageList, languageScroll, languageDoneRect, languageBackRect;

        private int LastLanguageStart => Mathf.Max(0, languageIndexes.Length - visibleLanguageRows);
        internal bool LanguageMenuOpen => languageMenuOpen;
        internal Rect LanguageControlRect => languageRect;
        internal Rect LanguageListRect => languageList;
        internal Rect LanguageDoneRect => languageDoneRect;
        internal int FirstLanguageRow => firstLanguageRow;
        internal int LanguageChoiceCount => languageIndexes.Length;
        internal int SelectedLanguageIndex => languageIndexes[selectedLanguageRow];

        private void OpenLanguageMenu()
        {
            languageIndexes = CultLanguages.AvailableIndexes();
            if (languageIndexes.Length == 0) return;
            languageLabels = Array.ConvertAll(languageIndexes, index => CultLanguages.Label(index, ownLanguage: true));
            languageMenuOpen = true;
            Editing = false;
            firstLanguageRow = 0;
            LayoutLanguageMenu();
            SelectLanguageRow(Mathf.Max(0, Array.IndexOf(languageIndexes, CultLanguages.CurrentIndex)));
        }

        private void CloseLanguageMenu()
        {
            if (languageMenuOpen && GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
            languageMenuOpen = languageScrollDrag = false;
            pressedLanguageRow = pressedButton = -1;
            wheelEventFrame = -1;
            languageIndexes = null;
            languageLabels = null;
        }

        private void LayoutLanguageMenu()
        {
            float padding = V("settings_padding"), height = V("settings_height"), width = V("settings_width") - 2f * padding;
            const float listTop = 40f;
            float buttonY = height - padding - V("settings_control_height");
            visibleLanguageRows = Mathf.Max(1, Mathf.FloorToInt((buttonY - listTop - 10f) / LanguageRowHeight));
            float listHeight = visibleLanguageRows * LanguageRowHeight;
            languageList = At(padding, listTop, width, listHeight);
            languageScroll = At(padding + width - 7f, listTop, 7f, listHeight);
            float gap = V("settings_button_gap"), half = (width - gap) * 0.5f;
            languageDoneRect = At(padding, buttonY, half, V("settings_control_height"));
            languageBackRect = At(padding + half + gap, buttonY, half, V("settings_control_height"));
            firstLanguageRow = Mathf.Clamp(firstLanguageRow, 0, LastLanguageStart);
        }

        internal Rect LanguageRowRect(int row) => new Rect(languageList.x + scale,
            languageList.y + (row - firstLanguageRow) * LanguageRowHeight * scale + scale,
            languageList.width - (LastLanguageStart > 0 ? 9f : 2f) * scale, (LanguageRowHeight - 2f) * scale);

        private Rect LanguageScrollThumb()
        {
            float height = Mathf.Round(languageScroll.height * visibleLanguageRows / languageIndexes.Length);
            float y = languageScroll.y + (languageScroll.height - height) * firstLanguageRow / LastLanguageStart;
            return new Rect(languageScroll.x, Mathf.Round(y), languageScroll.width, height);
        }

        private void DrawLanguageMenu()
        {
            pixels.Text(At(8f, V("settings_title_y"), V("settings_width") - 16f, 12f),
                language.Translation("options.language", "Language"), scale, Color.white);
            Fill(languageList, new Color(0f, 0f, 0f, 0.72f));
            int end = Mathf.Min(firstLanguageRow + visibleLanguageRows, languageIndexes.Length);
            for (int row = firstLanguageRow; row < end; row++)
            {
                Rect rect = LanguageRowRect(row);
                if (row == selectedLanguageRow)
                {
                    Fill(rect, Color.white);
                    Fill(new Rect(rect.x + scale, rect.y + scale, rect.width - 2f * scale, rect.height - 2f * scale), Color.black);
                }
                else if (rect.Contains(pointer)) Fill(rect, new Color(1f, 1f, 1f, 0.14f));
                pixels.Text(rect, languageLabels[row], scale, Color.white);
            }
            if (LastLanguageStart > 0)
            {
                Fill(languageScroll, Color.black);
                Rect thumb = LanguageScrollThumb();
                Fill(thumb, new Color(0.45f, 0.45f, 0.45f));
                Fill(new Rect(thumb.x, thumb.y, thumb.width - scale, thumb.height - scale), new Color(0.7f, 0.7f, 0.7f));
            }
            DrawButton(languageDoneRect, LanguageDone, language.Translation("gui.done", "Done"));
            DrawButton(languageBackRect, LanguageBack, language.Translation("gui.cancel", "Cancel"));
        }

        private void SelectLanguageRow(int row)
        {
            selectedLanguageRow = Mathf.Clamp(row, 0, languageIndexes.Length - 1);
            firstLanguageRow = Mathf.Clamp(firstLanguageRow,
                Mathf.Max(0, selectedLanguageRow - visibleLanguageRows + 1), Mathf.Min(selectedLanguageRow, LastLanguageStart));
        }

        private int LanguageRowAt(Vector2 position)
        {
            if (!languageList.Contains(position)) return -1;
            int row = firstLanguageRow + Mathf.FloorToInt((position.y - languageList.y) / (LanguageRowHeight * scale));
            return row < languageIndexes.Length && LanguageRowRect(row).Contains(position) ? row : -1;
        }

        private void ScrollLanguages(Vector2 position, float up)
        {
            if (up == 0f || !languageList.Contains(position)) return;
            firstLanguageRow = Mathf.Clamp(firstLanguageRow + (up > 0f ? -3 : 3), 0, LastLanguageStart);
            wheelEventFrame = Time.frameCount;
        }

        private void LanguagePointerDown(Vector2 position)
        {
            if (LastLanguageStart > 0 && languageScroll.Contains(position))
            {
                Rect thumb = LanguageScrollThumb();
                languageScrollGrab = thumb.Contains(position) ? position.y - thumb.y : thumb.height * 0.5f;
                languageScrollDrag = true;
                DragLanguageScroll(position);
            }
            else if (languageDoneRect.Contains(position)) pressedButton = LanguageDone;
            else if (languageBackRect.Contains(position)) pressedButton = LanguageBack;
            else pressedLanguageRow = LanguageRowAt(position);
            if (pressedButton >= 0 || pressedLanguageRow >= 0 || languageScrollDrag) GUIUtility.hotControl = controlId;
        }

        private void DragLanguageScroll(Vector2 position)
        {
            float travel = languageScroll.height - LanguageScrollThumb().height;
            float relative = Mathf.Clamp01((position.y - languageScroll.y - languageScrollGrab) / travel);
            firstLanguageRow = Mathf.RoundToInt(relative * LastLanguageStart);
        }

        private void LanguagePointerUp(Vector2 position)
        {
            int row = LanguageRowAt(position);
            if (row >= 0 && row == pressedLanguageRow) SelectLanguageRow(row);
            else if (pressedButton == LanguageDone && languageDoneRect.Contains(position)) ApplyLanguageSelection();
            else if (pressedButton == LanguageBack && languageBackRect.Contains(position)) CloseLanguageMenu();
            pressedLanguageRow = pressedButton = -1;
            languageScrollDrag = false;
            if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
        }

        private void ApplyLanguageSelection()
        {
            if (CultLanguages.Select(SelectedLanguageIndex, save: !testInput)) CloseLanguageMenu();
        }

        private bool HandleLanguageKey(KeyCode key, bool shift)
        {
            switch (key)
            {
                case KeyCode.Escape: CloseLanguageMenu(); break;
                case KeyCode.Tab: SelectLanguageRow(selectedLanguageRow + (shift ? -1 : 1)); break;
                case KeyCode.UpArrow: SelectLanguageRow(selectedLanguageRow - 1); break;
                case KeyCode.DownArrow: SelectLanguageRow(selectedLanguageRow + 1); break;
                case KeyCode.PageUp: SelectLanguageRow(selectedLanguageRow - visibleLanguageRows); break;
                case KeyCode.PageDown: SelectLanguageRow(selectedLanguageRow + visibleLanguageRows); break;
                case KeyCode.Home: SelectLanguageRow(0); break;
                case KeyCode.End: SelectLanguageRow(languageIndexes.Length - 1); break;
                case KeyCode.Return: case KeyCode.KeypadEnter: case KeyCode.Space: ApplyLanguageSelection(); break;
                default: return false;
            }
            return true;
        }

        private void PollLanguageKeys(Keyboard keyboard, Gamepad pad)
        {
            if (keyboard?.escapeKey.wasPressedThisFrame == true || pad?.buttonEast.wasPressedThisFrame == true)
                HandleLanguageKey(KeyCode.Escape, false);
            else if (keyboard?.enterKey.wasPressedThisFrame == true || keyboard?.numpadEnterKey.wasPressedThisFrame == true ||
                keyboard?.spaceKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true)
                HandleLanguageKey(KeyCode.Return, false);
            else if (keyboard?.upArrowKey.wasPressedThisFrame == true || pad?.dpad.up.wasPressedThisFrame == true)
                HandleLanguageKey(KeyCode.UpArrow, false);
            else if (keyboard?.downArrowKey.wasPressedThisFrame == true || pad?.dpad.down.wasPressedThisFrame == true)
                HandleLanguageKey(KeyCode.DownArrow, false);
            else if (keyboard?.homeKey.wasPressedThisFrame == true) HandleLanguageKey(KeyCode.Home, false);
            else if (keyboard?.endKey.wasPressedThisFrame == true) HandleLanguageKey(KeyCode.End, false);
            else if (keyboard?.pageUpKey.wasPressedThisFrame == true) HandleLanguageKey(KeyCode.PageUp, false);
            else if (keyboard?.pageDownKey.wasPressedThisFrame == true) HandleLanguageKey(KeyCode.PageDown, false);
            else if (keyboard?.tabKey.wasPressedThisFrame == true) HandleLanguageKey(KeyCode.Tab, keyboard.shiftKey.isPressed);
        }
    }
}
