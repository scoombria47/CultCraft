using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CultCraft
{
    /// <summary>Damage enchantments evaluated from the owner's installed Minecraft data files.</summary>
    public sealed class Enchantments
    {
        private sealed class LinearDamage
        {
            public float Base;
            public float PerLevel;
            public int Maximum;
            public string TranslationKey;

            public float Evaluate(int level)
            {
                return level <= 0 ? 0f : Base + (level - 1) * PerLevel;
            }
        }

        private readonly Dictionary<string, LinearDamage> effects = new Dictionary<string, LinearDamage>(StringComparer.Ordinal);
        public bool Ready { get; private set; }
        public string Error { get; private set; }

        public Enchantments(MinecraftAssets assets)
        {
            try
            {
                if (assets == null || !assets.Ready) throw new InvalidOperationException("Installed Minecraft assets are unavailable.");
                foreach (EnchantmentRow row in Spec.EnchantmentRows) effects.Add(row.Id, Parse(assets, row));
                Ready = effects.ContainsKey("sharpness") && effects.ContainsKey("power");
                if (!Ready) throw new InvalidOperationException("Both Sharpness and Power source definitions are required.");
            }
            catch (Exception ex)
            {
                effects.Clear();
                Ready = false;
                Error = ex.Message;
            }
        }

        private static LinearDamage Parse(MinecraftAssets assets, EnchantmentRow row)
        {
            string entry = Spec.Asset(row.DataAsset).Entry;
            string json = assets.ReadText(entry);
            if (String.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("The installed Minecraft enchantment is missing: " + entry);
            JObject definition = JObject.Parse(json);
            var effectList = definition["effects"] == null ? null : definition["effects"][row.EffectKey] as JArray;
            if (effectList == null || effectList.Count != 1) throw new InvalidOperationException("Unsupported Minecraft damage effect list: " + row.Id);
            JToken wrapper = effectList[0];
            JToken effect = wrapper["effect"];
            JToken value = effect == null ? null : effect["value"];
            if (effect == null || (string)effect["type"] != row.EffectType || value == null || (string)value["type"] != row.ValueType)
                throw new InvalidOperationException("Unsupported installed Minecraft damage expression: " + row.Id);
            if (row.Id == "power")
            {
                // Power's source predicate requires the direct attacker to be an arrow.
                // BowBonus is called exclusively for the mod's arrow projectile, never melee.
                JToken requirement = wrapper["requirements"];
                if (requirement == null || (string)requirement["condition"] != "minecraft:entity_properties" || (string)requirement["entity"] != "direct_attacker" || (string)(requirement["predicate"]?["type"]) != "#minecraft:arrows")
                    throw new InvalidOperationException("Unsupported installed Minecraft Power predicate.");
            }
            else if (wrapper["requirements"] != null) throw new InvalidOperationException("Conditional Sharpness damage is not supported.");
            if (value["base"] == null || value["per_level_above_first"] == null || definition["max_level"] == null)
                throw new InvalidOperationException("Incomplete installed Minecraft linear damage data: " + row.Id);
            var result = new LinearDamage
            {
                Base = (float)value["base"],
                PerLevel = (float)value["per_level_above_first"],
                Maximum = (int)definition["max_level"],
                TranslationKey = (string)(definition["description"]?["translate"])
            };
            if (Single.IsNaN(result.Base) || Single.IsInfinity(result.Base) || Single.IsNaN(result.PerLevel) || Single.IsInfinity(result.PerLevel) || result.Maximum < 1 || String.IsNullOrEmpty(result.TranslationKey))
                throw new InvalidOperationException("Invalid installed Minecraft enchantment values: " + row.Id);
            return result;
        }

        private int Level(string id, int nativeWeaponLevel)
        {
            if (!Ready) return 0;
            EnchantmentRow row = Spec.Enchantment(id);
            int materialStart = Int32.MinValue;
            foreach (WeaponRow weapon in Spec.WeaponRows)
                if (weapon.MinLevel <= nativeWeaponLevel && weapon.MinLevel > materialStart) materialStart = weapon.MinLevel;
            if (materialStart == Int32.MinValue) return 0;
            int level = nativeWeaponLevel - materialStart - row.FirstEnchantOffset + 1;
            return Math.Max(0, Math.Min(level, Math.Min(row.MaxPerMaterial, effects[id].Maximum)));
        }

        public int SharpnessLevel(int nativeWeaponLevel) { return Level("sharpness", nativeWeaponLevel); }
        public int PowerLevel(int nativeWeaponLevel) { return Level("power", nativeWeaponLevel); }
        public int PowerLevel(int nativeWeaponLevel, bool enchantedRangedPickup)
        {
            int level=PowerLevel(nativeWeaponLevel);
            return Ready && enchantedRangedPickup ? Math.Max(1,level) : level;
        }
        public float MeleeBonus(int nativeWeaponLevel)
        {
            return Ready ? effects["sharpness"].Evaluate(SharpnessLevel(nativeWeaponLevel)) : 0f;
        }
        public float BowBonus(int nativeWeaponLevel)
        {
            return Ready ? effects["power"].Evaluate(PowerLevel(nativeWeaponLevel)) : 0f;
        }
        public float BowBonus(int nativeWeaponLevel, bool enchantedRangedPickup)
        {
            return Ready ? effects["power"].Evaluate(PowerLevel(nativeWeaponLevel,enchantedRangedPickup)) : 0f;
        }

        public string Label(int nativeWeaponLevel)
        {
            int sharpness = SharpnessLevel(nativeWeaponLevel);
            int power = PowerLevel(nativeWeaponLevel);
            if (sharpness == 0 && power == 0) return Spec.Enchantment("sharpness").InactiveLabel;
            return Spec.Enchantment("sharpness").Name + " " + Numeral(sharpness) + " · " + Spec.Enchantment("power").Name + " " + Numeral(power);
        }

        private static string Numeral(int level)
        {
            if (level == 1) return "I";
            if (level == 2) return "II";
            if (level == 3) return "III";
            if (level == 4) return "IV";
            if (level == 5) return "V";
            return level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
