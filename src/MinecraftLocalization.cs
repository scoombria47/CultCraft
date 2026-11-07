using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using I2.Loc;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Uses the active CotL language and the owner's installed Minecraft language objects.</summary>
    public sealed class MinecraftLocalization
    {
        private readonly MinecraftAssets assets;
        private readonly Dictionary<string, JObject> languages = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> languageFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool indexed;
        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>(StringComparer.Ordinal);
        private static readonly string[] UiKeys = { "settings", "mouse_sensitivity", "field_of_view", "close", "reset_defaults", "settings_hint", "first_person", "top_down", "use_item", "eat_hold", "no_food", "full_health", "empty", "hotbar_hint", "show_hints" };
        // These are original interface labels. Item and enchantment names come from local Minecraft data.
        private static readonly Dictionary<string, string[]> UiLabels = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "en", new[] { "CultCraft camera", "Mouse sensitivity", "Field of view", "Close", "Reset defaults", "F6 — camera settings", "First person", "Top-down", "Use selected item: left click", "Hold left click to eat", "No food", "Health is full", "Empty", "1–3 / wheel – slot · Hold 3 – choose food · F5 – view · F6 – settings", "Show hints" } },
            { "ru", new[] { "Камера CultCraft", "Чувствительность мыши", "Поле зрения", "Закрыть", "По умолчанию", "F6 — настройки камеры", "От первого лица", "Вид сверху", "ЛКМ — использовать предмет", "Удерживайте ЛКМ, чтобы съесть", "Нет еды", "Здоровье полное", "Пусто", "1–3 / колесо – слот · Удерживай 3 – выбор еды · F5 – вид · F6 – настройки", "Показать подсказки" } },
            { "uk", new[] { "Камера CultCraft", "Чутливість миші", "Поле зору", "Закрити", "Типові налаштування", "F6 — налаштування камери", "Від першої особи", "Вигляд згори", "ЛКМ — використати предмет", "Утримуйте ЛКМ, щоб з’їсти", "Немає їжі", "Здоров’я повне", "Порожньо", "1–3 / колесо – слот · Утримуй 3 – вибір їжі · F5 – вигляд · F6 – налаштування", "Показувати підказки" } },
            { "ja", new[] { "CultCraft カメラ", "マウス感度", "視野角", "閉じる", "初期設定に戻す", "F6 — カメラ設定", "一人称", "見下ろし", "左クリックで使用", "左クリック長押しで食べる", "食べ物がありません", "体力は満タンです", "空", "1–3 / ホイール – スロット · 3長押し – 食べ物 · F5 – 視点 · F6 – 設定", "ヒントを表示" } },
            { "fr", new[] { "Caméra CultCraft", "Sensibilité de la souris", "Champ de vision", "Fermer", "Réinitialiser", "F6 — réglages caméra", "Première personne", "Vue du dessus", "Clic gauche : utiliser", "Maintenir le clic gauche pour manger", "Pas de nourriture", "Santé maximale", "Vide", "1–3 / molette – case · Maintenir 3 – nourriture · F5 – vue · F6 – réglages", "Afficher les astuces" } },
            { "de", new[] { "CultCraft-Kamera", "Mausempfindlichkeit", "Sichtfeld", "Schließen", "Zurücksetzen", "F6 — Kameraeinstellungen", "Erste Person", "Draufsicht", "Linksklick: Gegenstand benutzen", "Linksklick zum Essen halten", "Kein Essen", "Gesundheit ist voll", "Leer", "1–3 / Mausrad – Platz · 3 halten – Essen wählen · F5 – Ansicht · F6 – Einstellungen", "Hinweise anzeigen" } },
            { "es", new[] { "Cámara de CultCraft", "Sensibilidad del ratón", "Campo de visión", "Cerrar", "Restablecer", "F6 — ajustes de cámara", "Primera persona", "Vista superior", "Clic izquierdo: usar objeto", "Mantén el clic izquierdo para comer", "No hay comida", "Salud al máximo", "Vacío", "1–3 / rueda – ranura · Mantén 3 – comida · F5 – vista · F6 – ajustes", "Mostrar pistas" } },
            { "pt", new[] { "Câmera CultCraft", "Sensibilidade do mouse", "Campo de visão", "Fechar", "Restaurar padrões", "F6 — configurações da câmera", "Primeira pessoa", "Visão superior", "Clique esquerdo: usar item", "Segure o clique esquerdo para comer", "Sem comida", "Vida completa", "Vazio", "1–3 / roda – espaço · Segure 3 – comida · F5 – visão · F6 – opções", "Mostrar dicas" } },
            { "zh", new[] { "CultCraft 镜头", "鼠标灵敏度", "视野", "关闭", "恢复默认", "F6 — 镜头设置", "第一人称", "俯视", "左键使用物品", "按住左键进食", "没有食物", "生命值已满", "空", "1–3 / 滚轮 – 物品栏 · 按住 3 – 选择食物 · F5 – 视角 · F6 – 设置", "显示提示" } },
            { "zh_tw", new[] { "CultCraft 鏡頭", "滑鼠靈敏度", "視野", "關閉", "恢復預設", "F6 — 鏡頭設定", "第一人稱", "俯視", "左鍵使用物品", "按住左鍵進食", "沒有食物", "生命值已滿", "空", "1–3 / 滾輪 – 物品欄 · 按住 3 – 選擇食物 · F5 – 視角 · F6 – 設定", "顯示提示" } },
            { "ko", new[] { "CultCraft 카메라", "마우스 감도", "시야각", "닫기", "기본값 복원", "F6 — 카메라 설정", "1인칭", "위에서 보기", "왼쪽 클릭: 아이템 사용", "왼쪽 클릭을 길게 눌러 먹기", "생 소고기가 없습니다", "체력이 가득 찼습니다", "비어 있음", "1–3 / 휠 – 슬롯 · 3 길게 – 음식 선택 · F5 – 시점 · F6 – 설정", "힌트 표시" } },
            { "it", new[] { "Telecamera CultCraft", "Sensibilità del mouse", "Campo visivo", "Chiudi", "Ripristina", "F6 — impostazioni telecamera", "Prima persona", "Vista dall’alto", "Clic sinistro: usa oggetto", "Tieni premuto il clic sinistro per mangiare", "Nessun cibo", "Salute al massimo", "Vuoto", "1–3 / rotella – slot · Tieni 3 – cibo · F5 – vista · F6 – opzioni", "Mostra suggerimenti" } },
            { "nl", new[] { "CultCraft-camera", "Muisgevoeligheid", "Gezichtsveld", "Sluiten", "Standaardwaarden", "F6 — camera-instellingen", "Eerste persoon", "Bovenaanzicht", "Linkermuisknop: gebruiken", "Houd links ingedrukt om te eten", "Geen eten", "Gezondheid is vol", "Leeg", "1–3 / wiel – vak · Houd 3 vast – eten kiezen · F5 – beeld · F6 – instellingen", "Hints tonen" } },
            { "tr", new[] { "CultCraft kamerası", "Fare hassasiyeti", "Görüş alanı", "Kapat", "Varsayılanlara dön", "F6 — kamera ayarları", "Birinci şahıs", "Üstten görünüm", "Sol tık: eşya kullan", "Yemek için sol tıkı basılı tut", "Yiyecek yok", "Can dolu", "Boş", "1–3 / tekerlek – yuva · 3 basılı – yiyecek seç · F5 – görünüm · F6 – ayarlar", "İpuçlarını göster" } },
            { "ar", new[] { "كاميرا CultCraft", "حساسية الفأرة", "مجال الرؤية", "إغلاق", "الإعدادات الافتراضية", "F6 — إعدادات الكاميرا", "منظور الشخص الأول", "منظور علوي", "استخدم العنصر بالزر الأيسر", "اضغط مطولاً على الزر الأيسر للأكل", "لا يوجد طعام", "الصحة كاملة", "فارغ", "1–3 / العجلة – خانة · اضغط مطولاً على 3 – اختيار الطعام · F5 – المنظور · F6 – الإعدادات", "إظهار التلميحات" } }
        };
        private static readonly Dictionary<string,string> SettingsControls=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            {"en","Tab / ↑↓: Select · ←→: Adjust · F6 / Esc: Close"},
            {"ru","Tab / ↑↓: выбор · ←→: настройка · F6 / Esc: закрыть"},
            {"uk","Tab / ↑↓: вибір · ←→: зміна · F6 / Esc: закрити"},
            {"ja","Tab / ↑↓: 選択 · ←→: 調整 · F6 / Esc: 閉じる"},
            {"fr","Tab / ↑↓ : choisir · ←→ : régler · F6 / Esc : fermer"},
            {"de","Tab / ↑↓: Auswahl · ←→: Ändern · F6 / Esc: Schließen"},
            {"es","Tab / ↑↓: elegir · ←→: ajustar · F6 / Esc: cerrar"},
            {"pt","Tab / ↑↓: escolher · ←→: ajustar · F6 / Esc: fechar"},
            {"zh","Tab / ↑↓ 选择 · ←→ 调整 · F6 / Esc 关闭"},
            {"zh_tw","Tab / ↑↓ 選擇 · ←→ 調整 · F6 / Esc 關閉"},
            {"ko","Tab / ↑↓: 선택 · ←→: 조정 · F6 / Esc: 닫기"},
            {"it","Tab / ↑↓: scegli · ←→: regola · F6 / Esc: chiudi"},
            {"nl","Tab / ↑↓: kiezen · ←→: wijzigen · F6 / Esc: sluiten"},
            {"tr","Tab / ↑↓: Seç · ←→: Değiştir · F6 / Esc: Kapat"},
            {"ar","Tab / ↑↓: اختيار · ←→: ضبط · F6 / Esc: إغلاق"}
        };
        private static readonly Dictionary<string,string> HandPickerHints=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            {"en","Hold 1 + wheel – sword / empty hand (outside combat)"},
            {"ru","Зажми 1 + колесо – меч / пустая рука (вне боя)"},
            {"uk","Утримуй 1 + колесо – меч / порожня рука (поза боєм)"},
            {"ja","1長押し＋ホイール – 剣／素手（戦闘外）"},
            {"fr","Maintenir 1 + molette – épée / main vide (hors combat)"},
            {"de","1 halten + Mausrad – Schwert / leere Hand (außerhalb des Kampfes)"},
            {"es","Mantén 1 + rueda – espada / mano vacía (fuera de combate)"},
            {"pt","Segure 1 + roda – espada / mão vazia (fora de combate)"},
            {"zh","按住 1 + 滚轮 – 剑 / 空手（非战斗区域）"},
            {"zh_tw","按住 1 + 滾輪 – 劍 / 空手（非戰鬥區域）"},
            {"ko","1 길게 + 휠 – 검 / 빈손 (전투 지역 밖)"},
            {"it","Tieni 1 + rotella – spada / mano vuota (fuori dal combattimento)"},
            {"nl","Houd 1 + wiel – zwaard / lege hand (buiten gevechten)"},
            {"tr","1 basılı + tekerlek – kılıç / boş el (savaş dışında)"},
            {"ar","اضغط مطولاً على 1 + العجلة – سيف / يد فارغة (خارج القتال)"}
        };

        public MinecraftLocalization(MinecraftAssets assets) { this.assets = assets; }

        public static Font UiFont
        {
            get
            {
                string code = LanguageCode.Split('_')[0];
                Font font;
                if (fonts.TryGetValue(code, out font)) return font;
                string[] names = code == "zh" ? new[] { "Microsoft YaHei", "Microsoft JhengHei", "SimSun", "Arial" } :
                    code == "ja" ? new[] { "Yu Gothic", "Meiryo", "MS Gothic", "Arial" } :
                    code == "ko" ? new[] { "Malgun Gothic", "Gulim", "Arial" } : new[] { "Segoe UI", "Arial" };
                font = Font.CreateDynamicFontFromOSFont(names, 18);
                fonts[code] = font;
                return font;
            }
        }

        public static string LanguageCode
        {
            get
            {
                string name = LocalizationManager.CurrentLanguage ?? "English";
                if (name == "Chinese (Traditional)") return "zh_tw";
                if (name == "Chinese (Simplified)") return "zh_cn";
                if (name == "French (Canadian)") return "fr_ca";
                if (name == "Portuguese (Brazil)") return "pt_br";
                string code = (LocalizationManager.CurrentLanguageCode ?? "en").ToLowerInvariant().Replace('-', '_');
                switch (code.Split('_')[0])
                {
                    case "ja": return "ja_jp"; case "ru": return "ru_ru"; case "fr": return "fr_fr";
                    case "de": return "de_de"; case "es": return "es_es"; case "pt": return "pt_br";
                    case "zh": return code.Contains("tw") || code.Contains("hant") ? "zh_tw" : "zh_cn";
                    case "ko": return "ko_kr"; case "it": return "it_it"; case "nl": return "nl_nl";
                    case "tr": return "tr_tr"; case "ar": return "ar_sa"; case "uk": return "uk_ua";
                    default: return "en_us";
                }
            }
        }

        private static string Display(string text)
        {
            return LanguageCode.StartsWith("ar", StringComparison.Ordinal) ? LocalizationManager.ApplyRTLfix(text) : text;
        }

        public static string Ui(string key)
        {
            if(key=="settings")return "CultCraft Settings";
            if(key=="settings_controls")
            {
                string language=LanguageCode,selected=language=="zh_tw"?language:language.Split('_')[0],hint;
                return Display(SettingsControls.TryGetValue(selected,out hint)?hint:SettingsControls["en"]);
            }
            bool peacefulHint = key == "peaceful_hotbar_hint";
            int index = Array.IndexOf(UiKeys, peacefulHint ? "hotbar_hint" : key);
            if (index < 0) return key;
            string code = LanguageCode;
            string uiCode = code == "zh_tw" ? code : code.Split('_')[0];
            string[] labels;
            if (!UiLabels.TryGetValue(uiCode, out labels)) labels = UiLabels["en"];
            string label = labels[index];
            if (peacefulHint)
            {
                string handHint;
                if (!HandPickerHints.TryGetValue(uiCode, out handHint)) handHint = HandPickerHints["en"];
                label += " · " + handHint;
            }
            return Display(label);
        }

        public string Item(string id)
        {
            if (id == "raw_beef") id = "beef";
            if (id == "berry") id = "sweet_berries";
            if (id == "seeds") id = "wheat_seeds";
            return Translation("item.minecraft." + id, id.Replace('_', ' '));
        }

        public string Enchantment(string id, int level)
        {
            if (level <= 0) return "";
            return Translation("enchantment.minecraft." + id, id) + " " + Numeral(level);
        }

        public string Translation(string key, string fallback)
        {
            JObject language = GetLanguage(LanguageCode);
            string text = language == null ? null : (string)language[key];
            if (String.IsNullOrEmpty(text))
            {
                JObject english = GetLanguage("en_us");
                text = english == null ? null : (string)english[key];
            }
            return Display(text ?? fallback);
        }

        private JObject GetLanguage(string code)
        {
            JObject result;
            if (languages.TryGetValue(code, out result)) return result;
            try
            {
                string text = assets.ReadText("assets/minecraft/lang/" + code + ".json");
                if (text == null)
                {
                    if (!indexed) IndexLanguageObjects();
                    string path;
                    if (languageFiles.TryGetValue(code, out path)) text = File.ReadAllText(path);
                }
                result = text == null ? null : JObject.Parse(text);
            }
            catch { result = null; }
            languages[code] = result;
            return result;
        }

        private void IndexLanguageObjects()
        {
            indexed = true;
            var roots = new List<string>();
            DirectoryInfo version = Directory.GetParent(assets.ClientJar);
            if (version != null && version.Parent != null && version.Parent.Name == "versions" && version.Parent.Parent != null) roots.Add(version.Parent.Parent.FullName);
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
            foreach (string root in roots)
            {
                string indexFolder = Path.Combine(root, "assets", "indexes");
                if (!Directory.Exists(indexFolder)) continue;
                var indices = new List<string>();
                // Prefer the exact client version's asset index before any older local installations.
                try
                {
                    string versionJson = Path.ChangeExtension(assets.ClientJar, ".json");
                    string id = (string)JObject.Parse(File.ReadAllText(versionJson))["assetIndex"]?["id"];
                    if (!String.IsNullOrEmpty(id) && Path.GetFileName(id) == id) indices.Add(Path.Combine(indexFolder, id + ".json"));
                }
                catch { }
                indices.AddRange(Directory.GetFiles(indexFolder, "*.json"));
                foreach (string index in indices)
                {
                    try
                    {
                        if (!File.Exists(index) || new FileInfo(index).Length > 16 * 1024 * 1024) continue;
                        var objects = JObject.Parse(File.ReadAllText(index))["objects"] as JObject;
                        if (objects == null) continue;
                        foreach (var entry in objects.Properties())
                        {
                            if (!entry.Name.StartsWith("minecraft/lang/", StringComparison.Ordinal) || !entry.Name.EndsWith(".json", StringComparison.Ordinal)) continue;
                            string hash = (string)entry.Value["hash"];
                            if (hash == null || hash.Length != 40 || !IsHex(hash)) continue;
                            string code = Path.GetFileNameWithoutExtension(entry.Name);
                            if (languageFiles.ContainsKey(code)) continue;
                            string path = Path.Combine(root, "assets", "objects", hash.Substring(0, 2), hash);
                            if (File.Exists(path) && new FileInfo(path).Length <= 4 * 1024 * 1024) languageFiles.Add(code, path);
                        }
                    }
                    catch { }
                }
            }
        }

        private static bool IsHex(string value)
        {
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }

        private static string Numeral(int level)
        {
            if (level >= 1 && level <= 5) return new[] { "I", "II", "III", "IV", "V" }[level - 1];
            return level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
