using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using I2.Loc;
using Lamb.UI.Settings;
using TMPro;
using UnityEngine;

namespace CultCraft
{
    internal sealed class CultLanguages : IDisposable
    {
        private sealed class Locale
        {
            public readonly string Name, Code, BaseLanguage, MinecraftCode;
            public readonly Func<string, string> Translate;
            public readonly Func<string, string, string> TranslateTerm;
            public string LabelTerm => "CultCraft/Languages/" + Code;

            public Locale(string name, string code, Func<string, string> translate, string baseLanguage = "English",
                string minecraftCode = null, Func<string, string, string> translateTerm = null)
            {
                Name = name;
                Code = code;
                BaseLanguage = baseLanguage;
                MinecraftCode = minecraftCode ?? code;
                Translate = translate;
                TranslateTerm = translateTerm ?? ((_, text) => translate(text));
            }
        }

        private static readonly Locale Lolcat = new Locale("LOLCAT (Kingdom of Cats)", "lol_us", LolcatText.Translate);
        private const string CoinTerm = "Inventory/BLACK_GOLD";
        private static readonly Locale Pirate = new Locale("Pirate Speak (The Seven Seas)", "en_pt", PirateText.Translate,
            translateTerm: (term, text) => term == CoinTerm ? "Doubloon" : PirateText.Translate(text));
        private static readonly Locale RussianPirate = new Locale("Пиратская речь (Семь морей)", "ru_pirate", RussianPirateText.Translate, "Russian", "ru_ru",
            (term, text) => term == CoinTerm ? "Дублон" : RussianPirateText.Translate(text));
        private static readonly Locale Anglish = new Locale("Anglish (Oned Riches)", "enp", AnglishText.Translate);
        private static readonly Locale Shakespeare = new Locale("Shakespearean English (Kingdom of England)", "enws",
            ShakespeareText.Translate, translateTerm: ShakespeareText.TranslateTerm);
        private static readonly Locale PreReform = new Locale("Русскій дореформенный", "ru-x-rpr",
            PreReformRussianText.Translate, "Russian", "rpr", PreReformRussianText.TranslateTerm);
        private static readonly Locale[] Locales = { Lolcat, Pirate, RussianPirate, Anglish, Shakespeare, PreReform };
        private static readonly string[] Names = LanguageUtilities.AllLanguages.Concat(Locales.Select(locale => locale.Name)).ToArray();
        private static readonly string[] Labels = LanguageUtilities.AllLanguagesLocalizations.Concat(Locales.Select(locale => locale.LabelTerm)).ToArray();
        private readonly Harmony harmony = new Harmony("cultcraft.languages");
        private bool tested;
        private static bool testingSelection;
        private static TMP_FontAsset historicalFallback;
        private static bool historicalFontChecked;

        internal CultLanguages(ManualLogSource logger)
        {
            try
            {
                var patch = new HarmonyMethod(typeof(CultLanguages), nameof(NativeLanguageList));
                foreach (string method in new[] { "OnShowStarted", "GetLanguageIndex", "OnLanguageChanged" })
                    harmony.Patch(AccessTools.Method(typeof(GameSettings), method), transpiler: patch);
                harmony.Patch(AccessTools.Method(typeof(LocalizationManager), "AddSource"),
                    postfix: new HarmonyMethod(typeof(CultLanguages), nameof(SourceAdded)));
                harmony.Patch(AccessTools.Method(typeof(LocalizationManager), nameof(LocalizationManager.SetupFonts)),
                    postfix: new HarmonyMethod(typeof(CultLanguages), nameof(AddHistoricalGlyphs)));
                harmony.Patch(AccessTools.Method(typeof(LocalizationManager), "CreateCultureForCode"),
                    prefix: new HarmonyMethod(typeof(CultLanguages), nameof(UseRussianCulture)));
                harmony.Patch(AccessTools.Method(typeof(TwitchManager), nameof(TwitchManager.SetLanguage)),
                    prefix: new HarmonyMethod(typeof(CultLanguages), nameof(TwitchLanguage)));
                int terms = 0;
                foreach (LanguageSourceData source in LocalizationManager.Sources.ToArray())
                    terms += Populate(source);
                if (terms > 0)
                {
                    Locale saved = Find(SettingsManager.Settings?.Game?.Language);
                    if (saved != null) LocalizationManager.CurrentLanguage = saved.Name;
                }
                logger.LogInfo("CultCraft meme languages registered for both settings menus; initial text terms=" + terms);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static Locale Find(string name)
        {
            foreach (Locale locale in Locales)
                if (locale.Name == name) return locale;
            return null;
        }

        internal static string MinecraftCode(string name) => Find(name)?.MinecraftCode;
        internal static string MinecraftFallbackCode(string name) => name == PreReform.Name ? "ru_ru" : null;
        internal static string TranslateOriginal(string text) => Find(LocalizationManager.CurrentLanguage)?.Translate(text) ?? text;
        internal static string TranslateMinecraft(string text) =>
            LocalizationManager.CurrentLanguage == RussianPirate.Name ? RussianPirate.Translate(text) : text;

        private static string[] NativeNames() => Names;
        private static string[] NativeLabels() => Labels;

        private static IEnumerable<CodeInstruction> NativeLanguageList(IEnumerable<CodeInstruction> instructions)
        {
            var names = AccessTools.Field(typeof(LanguageUtilities), nameof(LanguageUtilities.AllLanguages));
            var labels = AccessTools.Field(typeof(LanguageUtilities), nameof(LanguageUtilities.AllLanguagesLocalizations));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, names))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CultLanguages), nameof(NativeNames));
                }
                else if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, labels))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CultLanguages), nameof(NativeLabels));
                }
                yield return instruction;
            }
        }

        private static void SourceAdded(LanguageSourceData __0) => Populate(__0);

        private static void UseRussianCulture(ref string __0)
        {
            if (__0 == PreReform.Code) __0 = "ru";
        }

        private static void AddHistoricalGlyphs()
        {
            var fallbackFonts = TMP_Settings.fallbackFontAssets;
            if (historicalFontChecked || fallbackFonts == null) return;
            historicalFontChecked = true;
            string glyphs = PreReformRussianText.HistoricalGlyphs;
            if (glyphs.All(glyph => fallbackFonts.Any(font => font != null && font.HasCharacter(glyph)))) return;
            try
            {
                historicalFallback = HistoricalCyrillicFont.Create(glyphs);
                if (historicalFallback != null) fallbackFonts.Add(historicalFallback);
            }
            catch (Exception error) { Debug.LogWarning("CultCraft could not load historical Cyrillic glyphs: " + error); }
        }

        private static bool TwitchLanguage(ref string __0)
        {
            if (testingSelection) return false;
            foreach (Locale locale in Locales)
                if (locale.Code == __0)
                {
                    __0 = LocalizationManager.GetLanguageCode(locale.BaseLanguage);
                    break;
                }
            return true;
        }

        private static int Populate(LanguageSourceData source)
        {
            if (source == null) return 0;
            int english = source.GetLanguageIndex("English", false, false);
            if (english < 0) return 0;
            source.LoadLanguage(english, false, false, false, false);
            int terms = 0;
            foreach (Locale locale in Locales)
            {
                if (source.GetLanguageIndex(locale.Name, false, false) >= 0) continue;
                int original = source.GetLanguageIndex(locale.BaseLanguage, false, false);
                if (original < 0) original = english;
                if (original != english) source.LoadLanguage(original, false, false, false, false);
                source.AddLanguage(locale.Name, locale.Code);
                int index = source.GetLanguageIndex(locale.Name, false, false);
                foreach (TermData term in source.mTerms)
                {
                    string value = term.Languages[original];
                    if (String.IsNullOrEmpty(value)) value = term.Languages[english];
                    if (term.TermType == eTermType.Text && !String.IsNullOrEmpty(value) &&
                        !term.Term.StartsWith("UI/Settings/Game/Languages/", StringComparison.Ordinal) &&
                        !term.Term.StartsWith("CultCraft/Languages/", StringComparison.Ordinal))
                    {
                        value = locale.TranslateTerm(term.Term, value);
                        terms++;
                    }
                    term.Languages[index] = value;
                }
                source.mLanguages[index].SetLoaded(true);
                TermData label = source.AddTerm(locale.LabelTerm, eTermType.Text, false);
                for (int i = 0; i < source.mLanguages.Count; i++) label.Languages[i] = locale.Name;
            }
            return terms;
        }

        internal static int CurrentIndex => Array.IndexOf(Names, LocalizationManager.CurrentLanguage);

        internal static string CurrentLabel() => CurrentIndex < 0
            ? LocalizationManager.CurrentLanguage : Label(CurrentIndex);

        internal static int[] AvailableIndexes() => Enumerable.Range(0, Names.Length)
            .Where(index => FindLanguage(index) != null).ToArray();

        internal static string Label(int index, bool ownLanguage = false)
        {
            string label = LocalizationManager.GetTranslation(Labels[index], FixForRTL: !ownLanguage,
                overrideLanguage: ownLanguage ? Names[index] : null) ?? Names[index];
            return ownLanguage && Names[index] == "Arabic" ? LocalizationManager.ApplyRTLfix(label) : label;
        }

        private static LanguageData FindLanguage(int index)
        {
            if (index < 0 || index >= Names.Length) return null;
            foreach (LanguageSourceData source in LocalizationManager.Sources)
            {
                int match = source.GetLanguageIndex(Names[index], AllowDiscartingRegion: false);
                if (match >= 0 && !String.IsNullOrEmpty(source.mLanguages[match].Code)) return source.mLanguages[match];
            }
            return null;
        }

        internal static bool Select(int index, bool save = true)
        {
            LanguageData language = FindLanguage(index);
            SettingsData.GameSettings settings = SettingsManager.Settings?.Game;
            if (language == null || settings == null) return false;
            if (LocalizationManager.CurrentLanguage == language.Name && settings.Language == language.Name) return true;
            bool synchronous = LocalizationManager.ForceLoadSynchronous;
            try
            {
                LocalizationManager.ForceLoadSynchronous = true;
                // I2's CurrentLanguage setter can resolve a language through another locale's code.
                settings.Language = language.Name;
                LocalizationManager.SetLanguageAndCode(language.Name, language.Code, RememberLanguage: save);
                LocalizationManager.EnableChangingCultureInfo(true);
                LocalizationManager.SetupFonts();
                if (save) SettingsManager.Instance.SaveSettings();
            }
            finally { LocalizationManager.ForceLoadSynchronous = synchronous; }
            if (save) TwitchManager.SetLanguage(LocalizationManager.CurrentLanguageCode);
            return true;
        }

        internal void Test(ManualLogSource logger, MinecraftAssets assets)
        {
            if (tested || SettingsManager.Settings?.Game == null) return;
            GameSettings menu = UnityEngine.Resources.FindObjectsOfTypeAll<GameSettings>().FirstOrDefault();
            if (menu == null || !Locales.All(locale => LocalizationManager.HasLanguage(locale.Name))) return;
            tested = true;
            testingSelection = true;
            string previous = LocalizationManager.CurrentLanguage;
            string saved = SettingsManager.Settings.Game.Language;
            try
            {
                var minecraft = new MinecraftLocalization(assets);
                const string englishSample = "Press {0} to pick up";
                var errors = new[]
                {
                    TestNativeChoices(menu, logger),
                    TestLocale(menu, logger, minecraft, Lolcat, "Dropp", "CultCraft Opshuns", "Wuddn Sord", englishSample, "Presh {0} tu pikk upp"),
                    TestLocale(menu, logger, minecraft, Pirate, "Set down", "CultCraft Ship's Orders", "Cutlass o' timber", englishSample, "Press {0} to haul aboard"),
                    TestLocale(menu, logger, minecraft, RussianPirate, "Опустить", "Судовой устав CultCraft", "Деревянный тесак", "Награда: {0}", "Добыча: {0}"),
                    TestLocale(menu, logger, minecraft, Anglish, "Set down", "CultCraft Settings", "Wood Sword", englishSample, "Push {0} to pick up"),
                    TestLocale(menu, logger, minecraft, Shakespeare, "Lay Down", "CultCraft's Settings", "Wooden Sword", englishSample, "Press {0} to take up"),
                    TestLocale(menu, logger, minecraft, PreReform, "Положить", "Настройки CultCraft", "Деревянный мечъ",
                        "Награда {0}: Железный меч", "Награда {0}: Желѣзный мечъ")
                };
                MeltyTest.Report("meme-languages", errors.All(error => error == null), String.Join(" | ", errors.Where(error => error != null)));
            }
            finally
            {
                LocalizationManager.CurrentLanguage = previous;
                SettingsManager.Settings.Game.Language = saved;
                testingSelection = false;
            }
        }

        private static string TestLocale(GameSettings menu, ManualLogSource logger, MinecraftLocalization minecraft,
            Locale locale, string drop, string title, string sword, string sample, string phrase)
        {
            try
            {
                int index = (int)AccessTools.Method(typeof(GameSettings), "GetLanguageIndex").Invoke(menu, new object[] { locale.Name });
                if (index != Array.IndexOf(Names, locale.Name)) throw new InvalidOperationException("Native selector omitted " + locale.Name);
                AccessTools.Method(typeof(GameSettings), "OnLanguageChanged").Invoke(menu, new object[] { index });
                if (LocalizationManager.CurrentLanguage != locale.Name || SettingsManager.Settings.Game.Language != locale.Name ||
                    CurrentLabel() != locale.Name || LocalizationManager.GetTranslation(Labels[index]) != locale.Name ||
                    LocalizationManager.GetTranslation("Interactions/Drop") != drop)
                    throw new InvalidOperationException("Native selector failed to activate the translated language.");
                const string markup = "</color> [i2p_One][Key] %1$s [i2nt]Keep this[/i2nt]";
                const string prefix = "<color=#FF00FF>";
                if (locale.Translate(prefix + sample + markup) != prefix + phrase + markup)
                    throw new InvalidOperationException("Text placeholders or markup changed.");
                if (!Select(0, save: false) || LocalizationManager.CurrentLanguage != Names[0] || !Select(index, save: false))
                    throw new InvalidOperationException("F6 could not select a specific language.");
                if (LocalizationManager.CurrentLanguage != locale.Name || MinecraftLocalization.Ui("settings") != title)
                    throw new InvalidOperationException("F6 failed to activate the translated language.");
                string item = minecraft.Item("wooden_sword");
                if (!String.Equals(item, sword, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected Minecraft sword label: " + item);
                if ((locale == Pirate && InventoryItem.LocalizedName(InventoryItem.ITEM_TYPE.BLACK_GOLD) != "Doubloon") ||
                    (locale == RussianPirate && InventoryItem.LocalizedName(InventoryItem.ITEM_TYPE.BLACK_GOLD) != "Дублон"))
                    throw new InvalidOperationException("The native inventory did not use the doubloon name.");
                if (locale == Anglish &&
                    (LocalizationManager.GetTranslation("Interactions/Doctrine") != "Teaching" ||
                     LocalizationManager.GetTranslation("Interactions/TakeTrinket") != "Draw a Wyrd Leaf" ||
                     LocalizationManager.GetTranslation("Objectives/Custom/CollectDivineInspiration") !=
                        "Gather <color=#FFD201>Godsent Insight</color> from the <color=#FFD201>Shrine</color>"))
                    throw new InvalidOperationException("Anglish gameplay terms or objective markup did not match.");
                if (locale == Shakespeare &&
                    (LocalizationManager.GetTranslation("Conversation_NPC/DeathCatBossFight/Intro0") != "Well hast thou done, Lamb." ||
                     LocalizationManager.GetTranslation("Objectives/Custom/CollectDivineInspiration") !=
                        "Gather <color=#FFD201>Heavenly Boon</color> from the <color=#FFD201>Shrine</color>" ||
                     minecraft.Translation("block.minecraft.crafting_table", "Crafting Table") != "Crafts Table"))
                    throw new InvalidOperationException("Shakespearean dialogue, objective markup, or crafting table did not match.");
                if (locale == PreReform) TestPreReform(minecraft);
                int total = 0, changed = 0;
                foreach (LanguageSourceData source in LocalizationManager.Sources)
                {
                    int original = source.GetLanguageIndex(locale.BaseLanguage, false, false);
                    int localized = source.GetLanguageIndex(locale.Name, false, false);
                    if (original < 0 || localized < 0) continue;
                    source.LoadLanguage(original, false, false, false, false);
                    foreach (TermData term in source.mTerms)
                    {
                        if (term.TermType != eTermType.Text || String.IsNullOrEmpty(term.Languages[original])) continue;
                        total++;
                        if (term.Languages[localized] != term.Languages[original]) changed++;
                    }
                }
                if (changed == 0) throw new InvalidOperationException("No native terms were translated.");
                logger.LogInfo(locale.Name + " check: " + changed + "/" + total + " native text terms changed; Drop=" + drop + "; sword=" + item);
                return null;
            }
            catch (Exception error) { return locale.Name + ": " + error; }
        }

        private static string TestNativeChoices(GameSettings menu, ManualLogSource logger)
        {
            int[] choices = AvailableIndexes();
            if (choices.Length == 0 || choices[0] != 0 || choices[choices.Length - 1] != Names.Length - 1)
                return "The language list does not run from English to pre-reform Russian.";
            var getNativeIndex = AccessTools.Method(typeof(GameSettings), "GetLanguageIndex");
            foreach (int index in choices)
            {
                if ((int)getNativeIndex.Invoke(menu, new object[] { Names[index] }) != index ||
                    !Select(index, save: false) || CurrentIndex != index || SettingsManager.Settings.Game.Language != Names[index])
                    return "Could not select the exact native language " + Names[index] + ".";
            }
            logger.LogInfo("CultCraft language selection verified in native order: " + String.Join(", ", choices.Select(index => Names[index])));
            return null;
        }

        private static void TestPreReform(MinecraftLocalization minecraft)
        {
            string death = LocalizationManager.GetTranslation("UI/DeathScreen/Killed/Title");
            if (death != "Вы погибли за вѣру") throw new InvalidOperationException("Native death text: " + death);
            string quest = LocalizationManager.GetTranslation("Objectives/Custom/CollectDivineInspiration");
            if (quest != "Получить <color=#FFD201>откровеніе</color> у <color=#FFD201>алтаря</color>")
                throw new InvalidOperationException("Native objective: " + quest);
            string use = MinecraftLocalization.Ui("use_item");
            if (use != "ЛКМ — использовать предметъ") throw new InvalidOperationException("F6 item use text: " + use);
            string planks = minecraft.Translation("block.minecraft.oak_planks", "Дубовые доски");
            if (planks != "Дубовыя доски") throw new InvalidOperationException("Minecraft oak planks: " + planks);
            if (LocalizationManager.CurrentCulture.TwoLetterISOLanguageName != "ru" ||
                GoogleLanguages.GetPluralType(PreReform.Code, 22) != GoogleLanguages.GetPluralType("ru", 22))
                throw new InvalidOperationException("Russian culture or plural rules did not match.");
            if (PreReform.Translate("Миф про новый мир. Покойся с миром.") != "Миѳъ про новый міръ. Покойся съ миромъ.")
                throw new InvalidOperationException("Contextual pre-reform spelling did not match.");
            string missing = new string(PreReformRussianText.HistoricalGlyphs.Where(glyph =>
                !TMP_Settings.fallbackFontAssets.Any(font => font != null && font.HasCharacter(glyph))).ToArray());
            if (missing.Length > 0) throw new InvalidOperationException("Missing native glyphs: " + missing);
        }

        public void Dispose()
        {
            harmony.UnpatchSelf();
            historicalFontChecked = false;
            if (historicalFallback != null)
            {
                TMP_Settings.fallbackFontAssets?.Remove(historicalFallback);
                UnityEngine.Object.Destroy(historicalFallback.atlasTexture);
                UnityEngine.Object.Destroy(historicalFallback.material);
                UnityEngine.Object.Destroy(historicalFallback);
                historicalFallback = null;
            }
        }
    }
}
