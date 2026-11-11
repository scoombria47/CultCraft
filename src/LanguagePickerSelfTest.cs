using System;
using System.Collections;
using I2.Loc;
using UnityEngine;

namespace CultCraft
{
    internal static class LanguagePickerSelfTest
    {
        internal const string Id = "minecraft-language-picker";

        internal static IEnumerator Run(CameraRig camera, Action<string, bool, string> report)
        {
            int original = CultLanguages.CurrentIndex;
            string saved = SettingsManager.Settings.Game.Language;
            var ui = camera.SettingsUi;
            try
            {
                if (!camera.SettingsOpen) camera.ToggleSettings();
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Click(ui.LanguageControlRect.center);
                if (!ui.LanguageMenuOpen || !ui.PixelStyleReady || !CultLanguages.Select(0, save: false))
                {
                    report(Id, false, "The F6 language button did not open a rendered language list.");
                    yield break;
                }
                yield return Key("home");
                bool startsAtTop = ui.FirstLanguageRow == 0 && ui.SelectedLanguageIndex == 0;
                Vector2 pointer = ui.LanguageListRect.center;
                MeltyPlay.Point(pointer.x / Screen.width, pointer.y / Screen.height);
                for (int i = 0; i < 8; i++)
                {
                    MeltyPlay.Scroll(-120f);
                    yield return new WaitForSecondsRealtime(0.12f);
                }
                bool scrolled = ui.FirstLanguageRow > 0;
                yield return Click(ui.LanguageRowRect(ui.LanguageChoiceCount - 1).center);
                bool staged = ui.LanguageMenuOpen && CultLanguages.CurrentIndex == 0 && ui.SelectedLanguageIndex > 0;
                yield return Click(ui.LanguageDoneRect.center);
                bool applied = !ui.LanguageMenuOpen && LocalizationManager.CurrentLanguage == "Русскій дореформенный" &&
                    SettingsManager.Settings.Game.Language == LocalizationManager.CurrentLanguage;
                yield return new WaitForSecondsRealtime(2.2f);
                yield return Click(ui.LanguageControlRect.center);
                bool reopened = ui.LanguageMenuOpen && ui.SelectedLanguageIndex == CultLanguages.CurrentIndex;
                if (reopened)
                {
                    yield return Key("home");
                    yield return Key("escape");
                }
                bool cancelled = !ui.LanguageMenuOpen && camera.SettingsOpen && LocalizationManager.CurrentLanguage == "Русскій дореформенный";
                report(Id, startsAtTop && scrolled && staged && applied && reopened && cancelled,
                    "English first=" + startsAtTop + ", mouse wheel=" + scrolled + ", row click stages language=" + staged +
                    ", Done applies pre-reform Russian=" + applied + ", current row highlighted on reopen=" + reopened + ", Esc cancels=" + cancelled);
            }
            finally
            {
                MeltyPlay.Button(0, false);
                MeltyPlay.Hold("home", false);
                MeltyPlay.Hold("escape", false);
                if (camera.SettingsOpen) camera.ToggleSettings();
                CultLanguages.Select(original, save: false);
                SettingsManager.Settings.Game.Language = saved;
            }
        }

        private static IEnumerator Click(Vector2 point)
        {
            MeltyPlay.Point(point.x / Screen.width, point.y / Screen.height);
            MeltyPlay.Button(0, true);
            yield return new WaitForSecondsRealtime(0.12f);
            MeltyPlay.Button(0, false);
            yield return new WaitForSecondsRealtime(0.18f);
        }

        private static IEnumerator Key(string key)
        {
            MeltyPlay.Hold(key, true);
            yield return new WaitForSecondsRealtime(0.12f);
            MeltyPlay.Hold(key, false);
            yield return new WaitForSecondsRealtime(0.12f);
        }
    }
}
