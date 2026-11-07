using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Read-only presentation state from native tree variants and farm growth.</summary>
    internal static class VegetationState
    {
        private static readonly Regex NativeTreeName = new Regex(@"(?:^|[^a-z])tree[ _-]?([1-4])(?:[^0-9]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static int TreeVariant(Structure native)
        {
            if (native == null) return 0;
            // Several native forms share a Spine asset. The actual instantiated
            // prefab and explicit native variant take precedence over shared art.
            if (TryTreeName(native.name, out int rootVariant) && rootVariant > 0) return rootVariant;
            if (native.VariantIndex > 0 && native.VariantIndex < 4) return native.VariantIndex;
            if (native.Structure_Info != null && native.Structure_Info.VariantIndex > 0 && native.Structure_Info.VariantIndex < 4) return native.Structure_Info.VariantIndex;
            // A saved StructuresData can retain variant0 while the actual scene
            // prefab is Tree2/3/4. Inspect the live selected art before its template.
            foreach (RandomObjectPicker picker in native.GetComponentsInChildren<RandomObjectPicker>(true))
                if (picker.CreatedObject != null && TryTreeName(picker.CreatedObject.name, out int picked) && picked > 0) return picked;
            Interaction_Woodcutting interaction = native.GetComponentInChildren<Interaction_Woodcutting>(true);
            if (interaction != null && interaction.TreeSpine != null && interaction.TreeSpine.skeletonDataAsset != null &&
                TryTreeName(interaction.TreeSpine.skeletonDataAsset.name, out int skeleton) && skeleton > 0) return skeleton;
            int hierarchy = TreeVariant(native.transform, -1);
            if (hierarchy > 0) return hierarchy;
            if (native.Structure_Info != null && TryTreeName(native.Structure_Info.PrefabPath, out int prefab) && prefab > 0) return prefab;
            return native.Structure_Info != null && native.Structure_Info.VariantIndex >= 0 && native.Structure_Info.VariantIndex < 4 ? native.Structure_Info.VariantIndex : 0;
        }

        public static int TreeVariant(Transform source, int fallback = 0)
        {
            bool ordinary = false;
            for (Transform node = source; node != null; node = node.parent)
                if (TryTreeName(node.name, out int variant))
                {
                    if (variant > 0) return variant;
                    ordinary = true;
                }
            return ordinary ? 0 : fallback;
        }

        private static bool TryTreeName(string name, out int variant)
        {
            Match match = NativeTreeName.Match(name ?? "");
            variant = match.Success ? match.Groups[1].Value[0] - '1' : 0;
            return match.Success;
        }

        public static string TreeSpecies(int variant, StructuresData data, Vector3 position)
        {
            if (variant == 1) return "birch";
            if (variant == 2) return "oak";
            if (variant == 3) return "spruce";
            // Ordinary Tree1 saves can genuinely contain only variant0. Give
            // those trees stable visual variety without changing their save IDs,
            // native form/health/loot or the game's random-number generator.
            int seed = data != null && data.ID > 0 ? data.ID : unchecked(Mathf.RoundToInt(position.x * 16f) * 73856093 ^ Mathf.RoundToInt(position.y * 16f) * 19349663);
            uint hash = unchecked((uint)seed * 2654435761u);
            int choice = (int)(hash % 3u);
            return choice == 1 ? "birch" : choice == 2 ? "spruce" : "oak";
        }
        public static bool IsVegetation(Structure native)
        {
            if (native == null) return false;
            return native.Type == StructureBrain.TYPES.TREE || native.Type == StructureBrain.TYPES.BERRY_BUSH || native.Type == StructureBrain.TYPES.RED_FLOWER_BUSH ||
                native.Type == StructureBrain.TYPES.FARM_PLOT;
        }

        public static string Resolve(Structure native)
        {
            if (native == null || native.Brain == null || native.Structure_Info == null) return null;
            StructuresData data = native.Structure_Info;
            if (native.Type == StructureBrain.TYPES.TREE)
            {
                int variant = TreeVariant(native);
                // These are the four inspected native Tree1..Tree4 resource forms.
                // Minecraft species are stable visual choices, not new save identities.
                string species = TreeSpecies(variant, data, data.Position);
                if (data.IsSapling)
                {
                    if (data.GrowthStage <= 0f) return species + "_stump";
                    if (data.GrowthStage <= 2f) return species + "_sapling";
                    if (data.GrowthStage <= 4f) return species + "_small";
                    return MatureTree(species, variant);
                }
                if (data.ProgressTarget > 0f && data.Progress >= data.ProgressTarget) return species + "_stump";
                return MatureTree(species, variant);
            }
            if (native.Type == StructureBrain.TYPES.BERRY_BUSH || native.Type == StructureBrain.TYPES.RED_FLOWER_BUSH)
            {
                // A mature farm crop has its own native child Structure. The farm
                // parent owns its full four-stage facade, so never draw it twice.
                if (native.GetComponentInParent<CropController>() != null) return null;
                var bush = native.Brain as Structures_BerryBush;
                bool picked = bush != null ? bush.BerryPicked : data.ProgressTarget > 0f && data.Progress >= data.ProgressTarget;
                Interaction_Berries interaction=native.GetComponentInChildren<Interaction_Berries>(true);
                if(native.Type==StructureBrain.TYPES.RED_FLOWER_BUSH || interaction!=null&&interaction.BerryBushType==InventoryItem.ITEM_TYPE.FLOWER_RED)
                    return picked?"rose_bush_picked":"rose_bush_full";
                return picked ? "berry_0" : "berry_3";
            }
            if (native.Type == StructureBrain.TYPES.FARM_PLOT)
            {
                var plot = native.Brain as Structures_FarmerPlot;
                if (plot == null || data.Inventory == null || !plot.HasPlantedSeed()) return null;
                InventoryItem planted = plot.GetPlantedSeed();
                if (planted == null || planted.type != (int)InventoryItem.ITEM_TYPE.SEED || data.Withered) return null;
                float fullGrowth = CropController.CropGrowthTimes(InventoryItem.ITEM_TYPE.SEED);
                // This is the same stage formula as native CropController.SetCropImage.
                int stage = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(data.GrowthStage, fullGrowth) / fullGrowth * 3f), 0, 3);
                return "berry_" + stage;
            }
            return null;
        }

        private static string MatureTree(string species, int variant)
        {
            return species + (variant == 2 ? "_large" : variant == 1 || variant == 3 ? "_small" : "_medium");
        }

        public static float Scale(Structure native)
        {
            if (native == null || native.Structure_Info == null || native.Type != StructureBrain.TYPES.TREE) return 1f;
            StructuresData data = native.Structure_Info;
            if (!data.IsSapling || data.GrowthStage <= 0f) return Spec.Growth("mature").Scale;
            if (data.GrowthStage <= 2f) return Spec.Growth("sapling").Scale;
            if (data.GrowthStage <= 4f) return Spec.Growth("young").Scale;
            return Spec.Growth("near_mature").Scale;
        }
    }
}
